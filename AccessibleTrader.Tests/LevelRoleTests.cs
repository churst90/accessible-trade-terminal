using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// A reference line is identified by what it MEANS, not by what it is called.
///
/// <para>
/// Sixteen providers declare the line their oscillator swings about, spelled four ways: <c>Zero</c>
/// (7), <c>Midpoint</c> (5), <c>Neutral</c> (3), <c>Midline</c> (1). Every reader of that line
/// matched one spelling — <c>IndicatorCrossingEngine</c> tested <c>Name == "Zero"</c> — so nine of
/// the sixteen were invisible to it. RSI is the case that shows the cost: it declares
/// <c>Midpoint</c> at 50 with <c>PlayEarcon: true</c>, so the earcon fires when RSI crosses 50 and
/// Ctrl+Left/Right could not jump to the bar where it happened. Crossing 50 is the momentum event
/// most RSI users are actually looking for.
/// </para>
///
/// <para>
/// The name was load-bearing in the other direction too: adding a level of your own and calling it
/// "Zero" silently changed which crossing algorithm ran on that indicator.
/// </para>
/// </summary>
public class LevelRoleTests
{
    // ── The inference ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Zero")]
    [InlineData("Midpoint")]
    [InlineData("Midline")]
    [InlineData("Neutral")]
    public void AllFourSpellingsOfTheMidlineResolveToOneRole(string name)
    {
        Assert.Equal(LevelRole.Neutral, new LevelConfig { Name = name }.EffectiveRole);
    }

    [Theory]
    [InlineData("Overbought", LevelRole.Overbought)]
    [InlineData("Extreme OB", LevelRole.Overbought)]
    [InlineData("Oversold", LevelRole.Oversold)]
    [InlineData("Extreme OS", LevelRole.Oversold)]
    [InlineData("Fear", LevelRole.None)]
    [InlineData("Long Crowded", LevelRole.None)]
    public void TheExtremesAndTheRestResolveAsDeclared(string name, LevelRole expected)
    {
        Assert.Equal(expected, new LevelConfig { Name = name }.EffectiveRole);
    }

    [Fact]
    public void AMidlineIsMatchedAsAWholeNameNotAsASubstring()
    {
        // "Zero Lag EMA" is an indicator, not a midline. The OB/OS tests are Contains() because
        // "Extreme OB" has to match inside longer provider names, but the midline names are short
        // enough to collide with real indicator names, so those are whole-name matches.
        Assert.Equal(LevelRole.None, new LevelConfig { Name = "Zero Lag EMA" }.EffectiveRole);
        Assert.Equal(LevelRole.None, new LevelConfig { Name = "Midpoint Channel Top" }.EffectiveRole);
    }

    [Fact]
    public void AnExplicitRoleBeatsTheNameInference()
    {
        // The escape hatch a provider needs: a line called "Zero" that is not the midline.
        var declared = new LevelConfig { Name = "Zero", Role = LevelRole.None };
        Assert.Equal(LevelRole.None, declared.EffectiveRole);
    }

    // ── The 0 key: where the level lands ─────────────────────────────────────────

    [Fact]
    public void OnAnRsiPaneTheKeyMarksFiftyNotZero()
    {
        // THE HEADLINE. RSI runs 0-100. A line at 0 sits on the floor of the pane, is never
        // crossed, never fires its earcon and is never navigated to — and it was named "Zero",
        // so the spoken confirmation agreed with the key and not with the chart.
        var level = ReferenceLevelPlacement.For("Oscillator", cursorPrice: 63_920.11,
            existing: null, paneNeutral: 50, out string reason, out bool refused);

        Assert.NotNull(level);
        Assert.Equal(50, level!.Value);
        Assert.False(refused);
        Assert.Contains("50", reason);
        Assert.DoesNotContain("Zero", level.Name);
        Assert.Equal(LevelRole.Neutral, level.EffectiveRole);
    }

    [Fact]
    public void OnAWilliamsPercentRPaneTheKeyMarksMinusFifty()
    {
        // -100..0, so zero is the CEILING here — the mirror image of the RSI case.
        var level = ReferenceLevelPlacement.For("Oscillator", double.NaN,
            existing: null, paneNeutral: -50, out _, out _);

        Assert.Equal(-50, level!.Value);
    }

    [Fact]
    public void OnAMacdPaneTheKeyStillMarksZeroAndStillCallsItZero()
    {
        // The control. A zero-centred oscillator must not change behaviour, and the line must
        // keep the name a trader expects to hear.
        var level = ReferenceLevelPlacement.For("Oscillator", double.NaN,
            existing: null, paneNeutral: 0, out string reason, out _);

        Assert.Equal(0, level!.Value);
        Assert.Equal("Zero", level.Name);
        Assert.Contains("Zero", reason);
    }

    [Fact]
    public void WhereTheProviderAlreadyDeclaresAMidlineTheKeyAddsNothingAndSaysSo()
    {
        // RSI ships a Midpoint at 50. Adding a second line at the same value with a different
        // name would give the pane two midlines, and the crossing earcons would report both.
        var existing = new[] { new LevelConfig { Name = "Midpoint", Value = 50 } };

        var level = ReferenceLevelPlacement.For("Oscillator", double.NaN,
            existing, paneNeutral: 50, out string reason, out bool refused);

        Assert.Null(level);
        Assert.Contains("Midpoint", reason);
        Assert.Contains("50", reason);
        // Information, not an error. The two used to be the same return value, so a perfectly
        // ordinary "it is already there" was spoken in the voice reserved for failures.
        Assert.False(refused);
    }

    [Fact]
    public void APaneThatDeclaresNoNeutralRefusesAndExplains()
    {
        // Silence is the one outcome that must never happen: to a screen-reader user a key that
        // does nothing is indistinguishable from a key that is not bound.
        var level = ReferenceLevelPlacement.For("Oscillator", double.NaN,
            existing: null, paneNeutral: null, out string reason, out bool refused);

        Assert.Null(level);
        Assert.True(refused);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("neutral", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePriceSeriesIsUnchangedAndItsLevelIsNotAMidline()
    {
        // The original defect stays fixed, and a price mark is not given the midline role — it is
        // a price someone noted, not the line the series swings about.
        var level = ReferenceLevelPlacement.For("Main", cursorPrice: 63_920.11,
            existing: null, paneNeutral: 50, out string reason, out _);

        Assert.Equal(63_920.11, level!.Value);
        Assert.Equal(LevelRole.None, level.EffectiveRole);
        Assert.Contains("63,920.11", reason);
    }

    // ── The 0 key: taking it back ────────────────────────────────────────────────

    [Fact]
    public void TheRemovalTargetFollowsTheNeutralSoTheKeyCanUndoItself()
    {
        // The key toggles. If the placement moved to 50 and the removal kept looking at 0, the
        // second press would add a SECOND line rather than take the first one back.
        var mine = new LevelConfig { Name = "Midpoint", Value = 50, IsUserDefined = true };

        var doomed = ReferenceLevelPlacement.FindRemovable(
            new[] { mine }, "Oscillator", cursorPrice: double.NaN, paneNeutral: 50);

        Assert.Same(mine, doomed);
    }

    [Fact]
    public void AProviderLevelIsNeverRemovedByTheKey()
    {
        // Unchanged contract, restated because the removal target moved: an indicator's own
        // midline is part of what the indicator is.
        var providers = new[] { new LevelConfig { Name = "Midpoint", Value = 50 } };

        Assert.Null(ReferenceLevelPlacement.FindRemovable(
            providers, "Oscillator", double.NaN, paneNeutral: 50));
    }

    // ── Navigation: the line the earcon fires on is the line the key reaches ─────

    [Fact]
    public void CtrlRightJumpsToAnRsiMidpointCross()
    {
        // THE DEFECT THIS PASS EXISTS FOR. RSI declares Midpoint at 50 with PlayEarcon true, so
        // the earcon has always fired here. GetCrossingStrategy sent RSI to the OB/OS branch,
        // which only ever scanned 70 and 30, so nothing could jump to the bar where 50 was
        // crossed. Values below cross 50 between index 2 and 3 and never approach 70 or 30.
        var (engine, bus, store) = Build();
        var rsi = Rsi(new[] { 45.0, 46.0, 48.0, 52.0, 54.0 });
        Focus(store, rsi, currentIndex: 0);

        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Contains(spoken, f => f.Message.Contains("Midpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheOverboughtJumpStillWins_WhenItIsTheNearerCrossing()
    {
        // The midline becoming a third target must not push the extremes out of the way. Here
        // 70 is crossed at index 1 and 50 was crossed before the cursor, so overbought is the
        // next thing to the right and must be what the key reports.
        var (engine, bus, store) = Build();
        var rsi = Rsi(new[] { 68.0, 72.0, 74.0, 76.0, 78.0 });
        Focus(store, rsi, currentIndex: 0);

        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(1, store.State.CurrentDataIndex);
        Assert.Contains(spoken, f => f.Message.Contains("verbought", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMidlineSpelledMidpointIsFoundByTheZeroLineScanToo()
    {
        // The other half of the spelling bug: an oscillator with a midline but no OB/OS pair
        // took the zero-line branch, which scanned for sign changes about the NUMBER zero. A
        // Fear & Greed pane whose Neutral sits at 50 reported "no crossing in view" for its
        // whole history — these values never change sign, and cross 50 at index 3.
        var (engine, bus, store) = Build();
        var fng = Oscillator("FNG", new[] { 30.0, 35.0, 44.0, 56.0, 60.0 },
            new LevelConfig { Name = "Neutral", Value = 50 });
        Focus(store, fng, currentIndex: 0);

        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Contains(spoken, f => f.Message.Contains("Neutral", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMidlineOnALineSeriesStillSelectsTheMidlineCrossingStrategy()
    {
        // Written because a sabotage SURVIVED. Reverting GetCrossingStrategy to the literal
        // Name == "Zero" test left the Fear & Greed case above green, because a component whose
        // DisplayType is Oscillator falls through to the zero-line strategy anyway — the fallback
        // was masking the very line the test was meant to be pinning.
        //
        // A LINE component has no such fallback: with the role test it takes the midline strategy,
        // and with the name test it drops through to trend lines and reports that the series has
        // no crossings at all. Values cross the declared Midpoint at 50 between index 2 and 3.
        var (engine, bus, store) = Build();
        var series = Oscillator("SENTIMENT", new[] { 30.0, 35.0, 44.0, 56.0, 60.0 },
            new LevelConfig { Name = "Midpoint", Value = 50 });
        series.Components[0].DisplayType = ComponentDisplayType.Line;
        Focus(store, series, currentIndex: 0);

        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        engine.HandleCrossJump(SystemCommand.NavRightJump);

        Assert.Equal(3, store.State.CurrentDataIndex);
        Assert.Contains(spoken, f => f.Message.Contains("Midpoint", StringComparison.OrdinalIgnoreCase));
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────

    private static ChartSeries Rsi(double[] values) => Oscillator("RSI", values,
        new LevelConfig { Name = "Overbought", Value = 70 },
        new LevelConfig { Name = "Midpoint",   Value = 50 },
        new LevelConfig { Name = "Oversold",   Value = 30 });

    private static ChartSeries Oscillator(string code, double[] values, params LevelConfig[] levels)
    {
        var config = new SeriesConfig
        {
            Id = code.ToLowerInvariant() + "-1", IndicatorCode = code,
            Name = code, FriendlyName = code, Pane = "Oscillator",
        };
        config.Components.Add(new ComponentConfig
        {
            Name = code, DisplayName = code,
            DisplayType = ComponentDisplayType.Oscillator,
            IsVisible = true,
        });
        foreach (var l in levels) config.Levels.Add(l);

        var buffer = new SeriesDataBuffer { SeriesId = config.Id };
        buffer.ComponentData[code] = values;
        return new ChartSeries(config, buffer);
    }

    private static void Focus(WorkspaceStore store, ChartSeries series, int currentIndex)
    {
        store.Dispatch(new UpdateSettingsAction(st => st with
        {
            Data = Bars(series.Data.ComponentData.Values.First().Length),
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = 0,
            CurrentDataIndex = currentIndex,
        }));
    }

    private static TimeSeriesBuffer<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000)));
    }

    private static (IndicatorCrossingEngine engine, EventBus bus, WorkspaceStore store) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        return (new IndicatorCrossingEngine(store, bus), bus, store);
    }
}
