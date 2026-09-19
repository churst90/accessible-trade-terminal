using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Ctrl+Left/Right aims at the lines the focused component answers to.</b>
///
/// <para>
/// Aroon is one pane with two neutrals: Up and Down swing about a Midpoint at 50, the Oscillator
/// about Zero, and each component declares which line it answers to (<c>SubscribedLevelNames</c>).
/// The earcon and the spoken zone word honoured that. The crossing engine did not: it read the
/// FIRST visible neutral level and the FIRST visible component, so with the Oscillator focused the
/// key landed where AroonUp crossed 50 and announced a Midpoint cross. Carried in TODO as "navigation
/// does not honour SubscribedLevelNames" since 2026-09-12; demonstrated and closed 2026-09-19.
/// </para>
/// </summary>
public sealed class SubscribedLevelNavigationTests
{
    private const int Bars = 120;
    private const int UpCrossesMidpointAt = 30;
    private const int OscillatorCrossesZeroAt = 60;

    /// <summary>
    /// Aroon as production declares it: Midpoint declared BEFORE Zero, Up declared before the
    /// Oscillator. Up crosses 50 at bar 30; the Oscillator crosses 0 at bar 60; nothing else
    /// crosses anything.
    /// </summary>
    private static ChartSeries Aroon()
    {
        var config = new SeriesConfig { Id = "aroon", IndicatorCode = "Aroon", Name = "Aroon", Pane = "Pane_Aroon" };
        config.Components.Add(new ComponentConfig { Name = "AroonUp",    DisplayType = ComponentDisplayType.Line,       IsVisible = true, ReferenceLevel = 50.0, SubscribedLevelNames = new[] { "Midpoint" } });
        config.Components.Add(new ComponentConfig { Name = "AroonDown",  DisplayType = ComponentDisplayType.Line,       IsVisible = true, ReferenceLevel = 50.0, SubscribedLevelNames = new[] { "Midpoint" } });
        config.Components.Add(new ComponentConfig { Name = "Oscillator", DisplayType = ComponentDisplayType.Oscillator, IsVisible = true, ReferenceLevel = 0.0,  SubscribedLevelNames = new[] { "Zero" } });
        config.Levels.Add(new LevelConfig { Name = "Midpoint", Value = 50, IsVisible = true });
        config.Levels.Add(new LevelConfig { Name = "Zero",     Value = 0,  IsVisible = true });

        var buffer = new SeriesDataBuffer { SeriesId = "aroon" };
        buffer.ComponentData["AroonUp"]    = Enumerable.Range(0, Bars).Select(i => i < UpCrossesMidpointAt ? 20.0 : 80.0).ToArray();
        buffer.ComponentData["AroonDown"]  = Enumerable.Range(0, Bars).Select(_ => 20.0).ToArray();
        buffer.ComponentData["Oscillator"] = Enumerable.Range(0, Bars).Select(i => i < OscillatorCrossesZeroAt ? -40.0 : 40.0).ToArray();
        return new ChartSeries(config, buffer);
    }

    private static (int? Landed, string? Said) JumpRight(ChartSeries series, int focusedComponent, int from)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, Bars).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), 100, 101, 99, 100, 10)).ToList()),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = focusedComponent,
            CurrentDataIndex = from,
            ViewportStartIndex = 0,
            ViewportLength = Bars,
        });
        new IndicatorCrossingEngine(store, bus).HandleCrossJump(SystemCommand.NavRightJump);
        return (store.DispatchedActions.OfType<NavigateAction>().LastOrDefault()?.NewIndex,
                bus.Log.OfType<FeedbackRequestEvent>().LastOrDefault()?.Message);
    }

    /// <summary>The report. On the Oscillator, the key must find the Oscillator's zero cross.</summary>
    [Fact]
    public void OnTheOscillator_TheJumpLandsOnItsOwnZeroCross_NotWhereUpCrossedTheMidpoint()
    {
        var (landed, said) = JumpRight(Aroon(), focusedComponent: 2, from: 10);

        Assert.Equal(OscillatorCrossesZeroAt, landed);
        Assert.Contains("Zero", said ?? string.Empty);
        Assert.DoesNotContain("Midpoint", said ?? string.Empty);
    }

    /// <summary>And on Up, the Midpoint is the line — not the Oscillator's zero.</summary>
    [Fact]
    public void OnAroonUp_TheJumpLandsOnTheMidpointCross()
    {
        var (landed, said) = JumpRight(Aroon(), focusedComponent: 0, from: 10);

        Assert.Equal(UpCrossesMidpointAt, landed);
        Assert.Contains("Midpoint", said ?? string.Empty);
    }

    /// <summary>
    /// Past its only line, the Oscillator has nowhere left to go — and the key must say so rather
    /// than borrow a crossing from a sibling. From bar 70 the only crossing ahead belongs to nobody.
    /// </summary>
    [Fact]
    public void OnTheOscillator_PastItsOnlyCross_TheKeyDoesNotBorrowASiblingsLine()
    {
        var series = Aroon();
        // Give Up a second Midpoint cross late, so a sibling-borrowing engine would have a target.
        series.Data.ComponentData["AroonUp"] = Enumerable.Range(0, Bars)
            .Select(i => i < UpCrossesMidpointAt ? 20.0 : i < 100 ? 80.0 : 20.0).ToArray();

        var (landed, said) = JumpRight(series, focusedComponent: 2, from: 70);

        Assert.Null(landed);
        Assert.Equal("No crossing in view", said);
    }

    /// <summary>
    /// Without a declaration the engine falls back to the pane's FIRST neutral — Midpoint, which
    /// the Oscillator never crosses — and says so. That is the ambiguity the declaration exists
    /// to remove, and it is why Aroon's components name their lines.
    /// </summary>
    [Fact]
    public void WithoutADeclaration_TheFirstNeutralWins_WhichIsWhyAroonDeclares()
    {
        var series = Aroon();
        series.Config.Components[2].SubscribedLevelNames = null;

        var (landed, said) = JumpRight(series, focusedComponent: 2, from: 10);

        Assert.Null(landed);
        Assert.Equal("No crossing in view", said);
    }
}
