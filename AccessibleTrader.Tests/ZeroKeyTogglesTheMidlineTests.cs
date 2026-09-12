using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The <c>0</c> key switches the pane's midline off and on.</b>
///
/// <para>
/// Cody, 2026-09-12: <i>"0 should toggle the visibility/earcons of the 0 line, pressing it now
/// doesn't seem to do much of anything."</i> Both halves of that were true. On any indicator whose
/// provider declares its midline — RSI's Midpoint at 50, MACD's Zero, Stochastic, MFI, Cipher B —
/// the key's only outcome was the sentence "Midpoint already marks 50.00 on this pane", every
/// press, forever. The line was protected from being duplicated and that protection was the whole
/// behaviour. On a pane with no declared midline it added one; on the price pane it marked the
/// cursor's price; everywhere else it explained itself. Nowhere did it change the line that was
/// already there.
/// </para>
///
/// <para>
/// It does now, and it moves visibility and the crossing earcon together, because from the
/// keyboard "is this line switched on" is one idea. A level you added yourself is still REMOVED by
/// a second press rather than left behind switched off — for your own line, off and gone are the
/// same wish, and removal is the only route the keyboard has ever had.
/// </para>
/// </summary>
public sealed class ZeroKeyTogglesTheMidlineTests
{
    private static LevelConfig Midline(double value = 50, bool visible = true, bool earcon = true) => new()
    {
        Name = "Midpoint", Value = value, IsVisible = visible, PlayEarcon = earcon, Role = LevelRole.Auto,
    };

    private static ChartSeries OscillatorSeries(params LevelConfig[] levels)
    {
        var config = new SeriesConfig { Id = "rsi", IndicatorCode = "Rsi", Name = "RSI", Pane = "Pane_Rsi" };
        config.Components.Add(new ComponentConfig
        {
            Name = "Rsi", DisplayName = "RSI", DisplayType = ComponentDisplayType.Oscillator,
            IsVisible = true, ReferenceLevel = 50.0,
        });
        foreach (var l in levels) config.Levels.Add(l);
        return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "rsi" });
    }

    private static WorkspaceState StateWith(ChartSeries series) => WorkspaceState.Initial with
    {
        Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv>
        {
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1),
        }),
        ActiveSeries = ImmutableList.Create(series),
        PrimarySeriesId = series.Id,
        FocusedSeriesId = series.Id,
        FocusedComponentIndex = 0,
        CurrentDataIndex = 0,
    };

    private static (CommandDispatcher Dispatcher, SpyEventBus Bus, MockWorkspaceStore Store) Build(ChartSeries series)
    {
        var bus = new SpyEventBus();
        var store = new MockWorkspaceStore();
        store.EmitState(StateWith(series));
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        // The 0 key is chart-scoped; without this the dispatcher drops it silently and every
        // assertion below would be about a keypress that never reached the command.
        dispatcher.SetChartActive(true);
        return (dispatcher, bus, store);
    }

    // ── The key, through the real dispatcher ─────────────────────────────────

    /// <summary>Cody's report: the press that used to do nothing now switches the line off.</summary>
    [Fact]
    public void PressingZeroOnAnIndicatorThatDeclaresItsMidline_SwitchesThatLineOff()
    {
        var (dispatcher, bus, store) = Build(OscillatorSeries(Midline()));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var action = Assert.IsType<SetLevelAudibleAction>(Assert.Single(store.DispatchedActions));
        Assert.Equal("rsi", action.SeriesId);
        Assert.Equal("Midpoint", action.LevelName);
        Assert.False(action.Audible);

        var spoken = bus.Log.OfType<FeedbackRequestEvent>().Last();
        Assert.Equal(FeedbackType.Info, spoken.Type);
        Assert.Contains("hidden and silent", spoken.Message);
        Assert.Contains("50", spoken.Message);
    }

    /// <summary>And a second press brings it back — a toggle is only a toggle in both directions.</summary>
    [Fact]
    public void PressingZeroOnASwitchedOffMidline_SwitchesItBackOn()
    {
        var (dispatcher, bus, store) = Build(OscillatorSeries(Midline(visible: false, earcon: false)));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var action = Assert.IsType<SetLevelAudibleAction>(Assert.Single(store.DispatchedActions));
        Assert.True(action.Audible);
        Assert.Contains("audible on crossing", bus.Log.OfType<FeedbackRequestEvent>().Last().Message);
    }

    /// <summary>
    /// It never adds a second line at the same value. That was what the old refusal was protecting
    /// against and the protection has to survive the change — two lines at 50 with two names would
    /// report the same crossing twice.
    /// </summary>
    [Fact]
    public void TheToggleNeverAddsASecondLineAtTheSameValue()
    {
        var (dispatcher, _, store) = Build(OscillatorSeries(Midline()));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        Assert.DoesNotContain(store.DispatchedActions, a => a is AddLevelAction);
    }

    /// <summary>
    /// A line you placed yourself is still removed, not switched off. The 0 key is the only way to
    /// take one back from the keyboard and it has to stay that way.
    /// </summary>
    [Fact]
    public void AUserPlacedMidline_IsStillRemovedRatherThanSwitchedOff()
    {
        var mine = Midline();
        mine.IsUserDefined = true;
        var (dispatcher, _, store) = Build(OscillatorSeries(mine));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var action = Assert.IsType<RemoveLevelAction>(Assert.Single(store.DispatchedActions));
        Assert.Equal("Midpoint", action.LevelName);
    }

    /// <summary>
    /// A pane with no midline at all still ADDS one — the toggle replaces the refusal, not the
    /// placement. Choppiness declares no levels and now declares a neutral of 50, so this is the
    /// first press on a real Chop pane.
    /// </summary>
    [Fact]
    public void APaneWithNoMidline_StillGetsOneAdded()
    {
        var (dispatcher, _, store) = Build(OscillatorSeries());

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var action = Assert.IsType<AddLevelAction>(Assert.Single(store.DispatchedActions));
        Assert.Equal(50.0, action.Level.Value);
        Assert.Equal(LevelRole.Neutral, action.Level.Role);
        Assert.True(action.Level.IsUserDefined);
    }

    /// <summary>
    /// Overbought and oversold lines are not the midline and the key must not grab one. On a pane
    /// whose only declared lines are the extremes, the key places the midline between them.
    /// </summary>
    [Fact]
    public void TheExtremesAreNotTheMidline()
    {
        var ob = new LevelConfig { Name = "Overbought", Value = 70, IsVisible = true, PlayEarcon = true };
        var os = new LevelConfig { Name = "Oversold", Value = 30, IsVisible = true, PlayEarcon = true };
        var (dispatcher, _, store) = Build(OscillatorSeries(ob, os));

        dispatcher.Dispatch(SystemCommand.AddReferenceLevel);

        var action = Assert.IsType<AddLevelAction>(Assert.Single(store.DispatchedActions));
        Assert.Equal(50.0, action.Level.Value);
    }

    // ── The rule, as a function ──────────────────────────────────────────────

    [Fact]
    public void ThePriceePaneHasNoMidlineToToggle()
    {
        var levels = new List<LevelConfig> { Midline(64_000) };
        Assert.Null(ReferenceLevelPlacement.FindToggleable(levels, "Main"));
        Assert.Null(ReferenceLevelPlacement.FindToggleable(levels, null));
    }

    [Theory]
    [InlineData("Zero")] [InlineData("Midpoint")] [InlineData("Midline")] [InlineData("Neutral")]
    public void EverySpellingOfTheMidlineIsToggleable(string name)
    {
        var levels = new List<LevelConfig> { new() { Name = name, Value = 0, IsVisible = true } };
        Assert.NotNull(ReferenceLevelPlacement.FindToggleable(levels, "Pane_Macd"));
    }

    /// <summary>
    /// "On" means perceivable by SOMEONE. A line that is drawn but silent is on to a sighted user;
    /// one that is hidden but chimes is on to a listener. Either half counts, so the toggle always
    /// lands somewhere a person can notice.
    /// </summary>
    [Theory]
    [InlineData(true,  true,  true)]
    [InlineData(true,  false, true)]
    [InlineData(false, true,  true)]
    [InlineData(false, false, false)]
    public void ALineIsOnIfEitherHalfOfItIsOn(bool visible, bool earcon, bool expected)
    {
        Assert.Equal(expected, ReferenceLevelPlacement.IsAudible(
            new LevelConfig { Name = "Midpoint", Value = 50, IsVisible = visible, PlayEarcon = earcon }));
    }

    // ── The reducer applies both halves ──────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheReducerMovesVisibilityAndTheEarconTogether(bool audible)
    {
        var series = OscillatorSeries(Midline(visible: !audible, earcon: !audible));
        var next = SeriesReducer.Reduce(StateWith(series),
            new SetLevelAudibleAction("rsi", "Midpoint", audible), new SpyEventBus());

        var level = next.ActiveSeries.Single().Config.Levels.Single();
        Assert.Equal(audible, level.IsVisible);
        Assert.Equal(audible, level.PlayEarcon);
    }

    /// <summary>
    /// The prior state keeps its own levels. Every other level action clones the series for this
    /// reason — a subscriber holding an earlier state reference must not see the mutation before
    /// the state notification — and a toggle that mutated in place would be the one that did not.
    /// </summary>
    [Fact]
    public void TheReducerDoesNotMutateThePriorState()
    {
        var series = OscillatorSeries(Midline());
        var before = StateWith(series);

        SeriesReducer.Reduce(before, new SetLevelAudibleAction("rsi", "Midpoint", false), new SpyEventBus());

        Assert.True(before.ActiveSeries.Single().Config.Levels.Single().IsVisible);
    }

    /// <summary>A toggle aimed at a series or a level that is not there changes nothing at all.</summary>
    [Theory]
    [InlineData("ghost", "Midpoint")]
    [InlineData("rsi", "NoSuchLevel")]
    public void AToggleWithNoTargetIsANoOp(string seriesId, string levelName)
    {
        var before = StateWith(OscillatorSeries(Midline()));
        var next = SeriesReducer.Reduce(before, new SetLevelAudibleAction(seriesId, levelName, false), new SpyEventBus());
        Assert.Same(before, next);
    }
}
