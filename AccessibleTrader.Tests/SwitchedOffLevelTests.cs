using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A line you switched off is not a place Ctrl+Left and Ctrl+Right take you.</b>
///
/// <para>
/// Cody, 2026-09-12, the same day the <c>0</c> key became a toggle: <i>"when the midline 0 line is
/// hidden, then ctrl left/right shouldn't jump to those points."</i> It generalises past the
/// midline — a level you have switched off is one you have said you are not interested in, so it
/// has to stop being a NAVIGATION target as well as a drawn line and an earcon. Otherwise the new
/// toggle silences and hides a line while the arrow keys keep their old destination, which reads
/// as a key that ignores you.
/// </para>
///
/// <para>
/// The earcon path already worked this way (<c>LevelCrossingMonitor</c> checks
/// <c>!lc.IsVisible || !lc.PlayEarcon</c>) and so did the spoken zone word
/// (<c>SpeechFormatter.ResolveZone</c> skips a hidden level). Navigation was the one reader that
/// did not, across four separate lookups.
/// </para>
/// </summary>
public sealed class SwitchedOffLevelTests
{
    private static LevelConfig Level(string name, double value, bool visible = true) => new()
    {
        Name = name, Value = value, IsVisible = visible, PlayEarcon = visible,
    };

    private static ChartSeries Rsi(params LevelConfig[] levels)
    {
        var config = new SeriesConfig { Id = "rsi", IndicatorCode = "Rsi", Name = "RSI", Pane = "Pane_Rsi" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Rsi", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true, ReferenceLevel = 50.0,
        });
        foreach (var l in levels) config.Levels.Add(l);
        var buffer = new SeriesDataBuffer { SeriesId = "rsi" };
        // A ramp from oversold to overbought: it crosses 30, 50 and 70 exactly once each.
        buffer.ComponentData["Rsi"] = Enumerable.Range(0, 40).Select(i => 20.0 + i * 1.5).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static WorkspaceState StateWith(ChartSeries series, int cursor) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, 40).Select(i => new Ohlcv(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList()),
        ActiveSeries = ImmutableList.Create(series),
        PrimarySeriesId = series.Id,
        FocusedSeriesId = series.Id,
        FocusedComponentIndex = 0,
        CurrentDataIndex = cursor,
        ViewportStartIndex = 0,
        ViewportLength = 40,
    };

    private static (int? Landed, string? Said) JumpRight(ChartSeries series, int from)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(StateWith(series, from));
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);

        var nav = store.DispatchedActions.OfType<NavigateAction>().LastOrDefault();
        var said = bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault()?.Message;
        return (nav?.NewIndex, said);
    }

    /// <summary>
    /// The report, as a test. With the midline switched off, the jump from below it must pass
    /// straight over 50 and land on the overbought crossing instead.
    /// </summary>
    [Fact]
    public void AHiddenMidline_IsNotAJumpTarget()
    {
        var visible = Rsi(Level("Overbought", 70), Level("Midpoint", 50), Level("Oversold", 30));
        var hidden  = Rsi(Level("Overbought", 70), Level("Midpoint", 50, visible: false), Level("Oversold", 30));

        // From bar 10 the value is 35 — already past oversold, still under the midline. The next
        // target to the right is 50, then 70.
        var withMidline = JumpRight(visible, from: 10);
        var without     = JumpRight(hidden, from: 10);

        Assert.NotNull(withMidline.Landed);
        Assert.NotNull(without.Landed);
        Assert.True(without.Landed > withMidline.Landed,
            $"with the midline on the jump landed at {withMidline.Landed}; switched off it landed at {without.Landed} — it should be further right");
        Assert.DoesNotContain("midline", without.Said ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Switching the extremes off leaves the midline as the only target, rather than leaving the
    /// key pointed at lines that are no longer on the chart.
    /// </summary>
    [Fact]
    public void HiddenExtremes_LeaveTheMidlineAsTheTarget()
    {
        var series = Rsi(Level("Overbought", 70, visible: false), Level("Midpoint", 50),
                         Level("Oversold", 30, visible: false));

        var result = JumpRight(series, from: 10);

        Assert.NotNull(result.Landed);
        // The ramp crosses 50 at index 20 (20 + 1.5i >= 50).
        Assert.InRange(result.Landed!.Value, 19, 21);
    }

    /// <summary>
    /// Every level switched off, and the key says so rather than jumping somewhere arbitrary.
    /// A key that silently does nothing is indistinguishable from a key that is not bound.
    /// </summary>
    [Fact]
    public void EveryLevelSwitchedOff_IsAnnounced_NotSilent()
    {
        var series = Rsi(Level("Overbought", 70, visible: false), Level("Midpoint", 50, visible: false),
                         Level("Oversold", 30, visible: false));

        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(StateWith(series, 10));
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);

        Assert.NotEmpty(bus.Log.OfType<FeedbackRequestEvent>());
    }

    /// <summary>
    /// A level switched off does not change what the CROSSING TYPE is when others remain: RSI with
    /// its midline off is still an overbought/oversold instrument, not a zero-line one.
    /// </summary>
    [Fact]
    public void TheRemainingLevelsStillDecideTheCrossingType()
    {
        var series = Rsi(Level("Overbought", 70), Level("Midpoint", 50, visible: false), Level("Oversold", 30));
        var result = JumpRight(series, from: 10);

        Assert.NotNull(result.Landed);
        // The ramp crosses 70 at index ~34, which is the only remaining target to the right.
        Assert.InRange(result.Landed!.Value, 32, 36);
    }
}
