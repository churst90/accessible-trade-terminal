using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Workspace.Reducers;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// N IS THE THIRD SWITCH, and it resolves its target the same way the other two do.
///
/// <para>
/// Cody, 2026-09-04: <i>"why not change the command to simply N, the same as h and m"</i>. Hide,
/// mute and narrate are the three switches on a chart object; two were a single letter and the
/// third was a four-key chord, which is why it was forgettable. Worse than forgettable: it also
/// resolved its target differently — always the SERIES, never the component under the cursor —
/// so "M muted the component but N narrated the whole series" was the shipped behaviour.
/// </para>
/// </summary>
public class NarrationKeyTests
{
    [Fact]
    public void N_IsBoundBare_AlongsideHAndM()
    {
        var mgr = new ShortcutManager(new FixedPaths(TestTemp.NewDir("narration-key")));

        Assert.Equal(SystemCommand.ToggleNarration,
            mgr.GetCommand("N", shift: false, ctrl: false, alt: false));

        // The chord is KEPT, not replaced: it is the one that works with focus outside the chart.
        Assert.Equal(SystemCommand.ToggleNarration,
            mgr.GetCommand("N", shift: true, ctrl: true, alt: true));
    }

    [Theory]
    [InlineData(InteractionContext.Component, "COMPONENT")]
    [InlineData(InteractionContext.Series, "SERIES")]
    public void TheScopeFollowsWhatTheUserWasLastMovingThrough(InteractionContext ctx, string expected)
    {
        // Exactly the rule ToggleIndicatorVisibility and ToggleIndicatorAudio use, read from the
        // same field. Three switches on one object with three resolution rules is how a user
        // ends up unable to predict what any of them will act on.
        var (dispatcher, bus, store) = Build();
        // Chart-scoped, so it has to get past the no-data gate to reach the scope resolution.
        store.Dispatch(new UpdateSettingsAction(st => st with { Data = Bars(5) }));
        store.Dispatch(new SetInteractionContextAction(ctx));

        var seen = new List<ToggleNarrationEvent>();
        bus.Subscribe<ToggleNarrationEvent>(seen.Add);

        dispatcher.Dispatch(SystemCommand.ToggleNarration);

        Assert.Equal(expected, Assert.Single(seen).Scope);
    }

    [Fact]
    public void SeriesScopeToggle_FlipsTheSeriesFlag_AndSaysWhichWay()
    {
        var (state, bus, spoken) = Reducing();

        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher"), bus);
        Assert.True(state.ActiveSeries[0].IsAutoNarrated);
        Assert.Contains("narrating", Assert.Single(spoken).Message);

        spoken.Clear();
        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher"), bus);
        Assert.False(state.ActiveSeries[0].IsAutoNarrated);
        Assert.Contains("narration off", Assert.Single(spoken).Message);
    }

    [Fact]
    public void AComponentToggledOnASILENTSeries_IsToldThatNothingWillBeSpokenYet()
    {
        // The failure this sentence exists to prevent: the flag is set, the series flag is not,
        // NOTHING SPEAKS, and a bare "narrating" sends the user off to wait for output that
        // will never arrive. Same class as the hide/mute pair fixed earlier the same day.
        var (state, bus, spoken) = Reducing();      // series starts NOT narrating

        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher", "Buy"), bus);

        Assert.True(state.ActiveSeries[0].Components[0].IsAutoNarrated);
        string msg = Assert.Single(spoken).Message;
        Assert.Contains("narrating", msg);
        Assert.Contains("not narrating", msg);      // ...but the series is not
        Assert.Contains("Press N on the series", msg);
    }

    [Fact]
    public void TheFirstComponentSelected_IsAnnouncedAsANARROWING_NotAsAnOn()
    {
        // The series went from narrating everything to narrating one thing. That is a much
        // bigger change than "on", and the one most likely to be mistaken for a fault later —
        // "why did my other signals stop?".
        var (state, bus, spoken) = Reducing();
        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher"), bus);   // series on
        spoken.Clear();

        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher", "Buy"), bus);

        Assert.Contains("only, narrating", Assert.Single(spoken).Message);
    }

    [Fact]
    public void DeselectingTheLastComponent_SaysItWidensBackOut_NotThatItWentOff()
    {
        // Narration widens back to the whole series rather than going quiet, which is the
        // opposite of what a bare "off" implies. See SeriesNarrationScope for why empty means
        // all rather than none.
        var (state, bus, spoken) = Reducing();
        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher"), bus);
        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher", "Buy"), bus);
        spoken.Clear();

        state = SeriesReducer.Reduce(state, new ToggleNarrationAction("cipher", "Buy"), bus);

        string msg = Assert.Single(spoken).Message;
        Assert.Contains("narration off", msg);
        Assert.Contains("Back to the whole series", msg);
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────

    private static (CommandDispatcher dispatcher, EventBus bus, WorkspaceStore store) Build()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(bus, new MockViewportRangeCalculator(),
            new MockViewportNavigationService(), new MockVolumeStateService());
        var dispatcher = new CommandDispatcher(bus, Substitute.For<INavigationEngine>(), store,
            Substitute.For<IBarDetailService>(), new IndicatorCrossingEngine(store, bus));
        // N is chart-scoped, like H and M — without focus the gate drops it silently and every
        // assertion below would be about the gate rather than about narration.
        dispatcher.SetChartActive(true);
        return (dispatcher, bus, store);
    }

    private static (WorkspaceState state, EventBus bus, List<AnnouncementEvent> spoken) Reducing()
    {
        var bus = new EventBus();
        var spoken = new List<AnnouncementEvent>();
        bus.Subscribe<AnnouncementEvent>(spoken.Add);

        var cfg = new SeriesConfig
        {
            Id = "cipher", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Buy", DisplayName = "Buy", IsVisible = true, DisplayType = ComponentDisplayType.Dot,
        });
        var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = "cipher" });

        var state = WorkspaceState.Initial with
        {
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = "cipher",
            PrimarySeriesId = "cipher",
        };
        return (state, bus, spoken);
    }

    [Fact]
    public void WithNoChartLoaded_TheKeyAnswers_RatherThanFallingSilent()
    {
        // N sits beside H and M now, which puts it AFTER the dispatcher's no-data gate — and
        // that gate used to drop all three without a word. A single letter that does nothing and
        // says nothing is indistinguishable, to this user, from a key that stopped working.
        var (dispatcher, bus, _) = Build();

        var spoken = new List<FeedbackRequestEvent>();
        bus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        dispatcher.Dispatch(SystemCommand.ToggleNarration);

        var f = Assert.Single(spoken);
        Assert.Equal("No chart loaded.", f.Message);
        // Boundary, not Error: the key was understood and there is simply no chart. Error would
        // play the failure earcon for a keypress that failed nothing.
        Assert.Equal(FeedbackType.Boundary, f.Type);
    }

    private static TimeSeriesBuffer<Ohlcv> Bars(int n)
    {
        var start = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, n)
            .Select(i => new Ohlcv(start.AddDays(i), 100 + i, 101 + i, 99 + i, 100 + i, 1000)));
    }

    private sealed class FixedPaths : IPlatformPathService
    {
        public FixedPaths(string dir) { AppDataDirectory = dir; CacheDirectory = dir; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }
}
