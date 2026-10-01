using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Scrolling back into history must not make the narrator speak again.</b>
///
/// <para>
/// Survey A2q §4 item 3, suspected on 2026-09-28 and demonstrated here on 2026-10-01. Walking or
/// panning left near the start of the chart loads older bars IN FRONT of the ones on screen
/// (<c>HistoryBufferCoordinator</c> → <c>DataManager.PrependOlderDataAsync</c>), and every index
/// on the chart moves right by the number of bars that arrived. The narrator remembers what it has
/// already said BY BAR INDEX (its seed, the set of announced markers, the last pivot it saw), and
/// nothing told it the indices had moved. Two things followed, both heard by a user who did
/// nothing but scroll back to look at history:
/// </para>
/// <list type="number">
///   <item>The <c>RedrawEvent</c> that follows the backfill saw the bar count jump and took it
///         for a bar close, so it narrated a bar from deep in the loaded history as though it
///         had just closed.</item>
///   <item>On the next real bar close the 20-bar look-back window reached the signal the user
///         had ALREADY heard, under its new index, and said it again.</item>
/// </list>
/// <para>
/// The real wiring, as DI builds it: one <see cref="SpeechFeedbackRouter"/> shared by the
/// coordinator and the narrator, a real <see cref="IndicatorContextAnalyzer"/>, the store's own
/// order of events (commit, <see cref="NewBarEvent"/>, then the recalculation's
/// <see cref="RedrawEvent"/>).
/// </para>
/// </summary>
public sealed class HistoryBackfillNarrationTests
{
    private static readonly DateTime Day0 = new(2026, 1, 1);

    /// <summary>Bars <paramref name="first"/>..<paramref name="first"/>+n-1, one a day, where
    /// day 0 is the first bar the chart loaded with. Negative days are history loaded later.</summary>
    private static List<Ohlcv> Bars(int first, int n) => Enumerable.Range(first, n)
        .Select(d => new Ohlcv(Day0.AddDays(d), 100 + d, 101 + d, 99 + d, 100.5 + d, 10))
        .ToList();

    private sealed class Harness
    {
        public MockWorkspaceStore Store { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public List<string> Spoken { get; } = new();

        public Harness()
        {
            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            var narrator = new AutoNarrationService(Store, Bus, router, new IndicatorContextAnalyzer());
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), narrator);
        }
    }

    /// <summary>
    /// A narrated marker series whose dots sit on calendar DAYS, not indices, so a prepend moves
    /// them the way it moves a real indicator's output. A dot is stamped only once its bar is
    /// behind the live edge, as a live indicator's is.
    /// </summary>
    private static ChartSeries Series(List<Ohlcv> bars, params int[] signalDays)
    {
        var cfg = new SeriesConfig
        {
            Id = "cb", Name = "Cipher B", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B",
            Pane = "Pane_CIPHER_B", IsAutoNarrated = true, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Gold", DisplayName = "Triple Confluence Buy",
            DisplayType = ComponentDisplayType.Dot, IsVisible = true,
            SignalSpeechTemplate = "Triple confluence buy, strong confirmation",
        });
        var d = new double[bars.Count];
        Array.Fill(d, double.NaN);
        for (int i = 0; i < bars.Count - 1; i++)
            if (signalDays.Contains((int)(bars[i].Date - Day0).TotalDays)) d[i] = 1.0;
        var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
        buf.ComponentData["Gold"] = d;
        return new ChartSeries(cfg, buf);
    }

    private static WorkspaceState State(List<Ohlcv> bars, params int[] signalDays) => WorkspaceState.Initial with
    {
        Identity = new ChartIdentity("Spot", "Harness", "BTCUSD", "1d"),
        Data = new TimeSeriesBuffer<Ohlcv>(bars),
        ActiveSeries = ImmutableList.Create(Series(bars, signalDays)),
        CurrentDataIndex = bars.Count - 1,
        InitStatus = InitializationStatus.Ready,
        DataStatus = DataStatus.Ready,
        IsSpeechEnabled = true,
        AnnounceNewBars = true,
    };

    /// <summary>Days 0..99 loaded, narration on with day 99 forming; day 99 closes with a gold
    /// dot, which the user hears.</summary>
    private static Harness HeardTheSignalOnDay99(params int[] historicalSignalDays)
    {
        int[] days = historicalSignalDays.Append(99).ToArray();
        var h = new Harness();
        h.Store.EmitState(State(Bars(0, 100), days));
        h.Bus.Publish(new RedrawEvent());

        var after = Bars(0, 101);
        h.Store.EmitState(State(after, days));
        h.Bus.Publish(new NewBarEvent(after[99], after[100]));
        h.Bus.Publish(new RedrawEvent());

        Assert.Contains(h.Spoken, s => s.Contains("Triple confluence buy", StringComparison.OrdinalIgnoreCase));
        return h;
    }

    /// <summary>What the backfill does: 200 older bars arrive in front, then the redraw.</summary>
    private static List<string> ScrollBackLoads200Bars(Harness h, int[] days)
    {
        h.Spoken.Clear();
        h.Store.EmitState(State(Bars(-200, 301), days));
        h.Bus.Publish(new RedrawEvent());
        return h.Spoken.ToList();
    }

    [Fact]
    public void A_signal_already_heard_is_not_said_again_after_older_history_loads()
    {
        int[] days = { 99 };
        var h = HeardTheSignalOnDay99();
        ScrollBackLoads200Bars(h, days);

        // Day 100 closes. Nothing printed on it.
        h.Spoken.Clear();
        var after = Bars(-200, 302);
        h.Store.EmitState(State(after, days));
        h.Bus.Publish(new NewBarEvent(after[300], after[301]));
        h.Bus.Publish(new RedrawEvent());

        string said = string.Join(" | ", h.Spoken);
        Assert.Contains("Close 200.50", said, StringComparison.Ordinal);   // day 100 closed
        Assert.False(said.Contains("Triple confluence", StringComparison.OrdinalIgnoreCase),
            "the gold dot on day 99 was announced when day 99 closed; one scroll back into history "
          + $"later the next bar close announced it again: \"{said}\"");
    }

    [Fact]
    public void Loading_older_history_does_not_narrate_an_old_bar_as_though_it_just_closed()
    {
        // A gold dot 100 days BEFORE the chart's original first bar: history, loaded by the
        // scroll-back, and nothing the user has not had a chance to read.
        int[] days = { -100, 99 };
        var h = HeardTheSignalOnDay99(-100);

        var said = ScrollBackLoads200Bars(h, days);

        Assert.True(said.Count == 0,
            "scrolling back loaded older bars and the narrator spoke as if a bar had closed: \""
          + string.Join(" | ", said) + "\"");
    }

    [Fact]
    public void After_older_history_loads_the_next_new_signal_is_still_heard()
    {
        // The vacuity partner: a fix that simply stopped the narrator after a backfill would pass
        // both tests above. Day 100 prints its own dot, and that is news.
        int[] days = { 99, 100 };
        var h = HeardTheSignalOnDay99();
        ScrollBackLoads200Bars(h, days);

        h.Spoken.Clear();
        var after = Bars(-200, 302);
        h.Store.EmitState(State(after, days));
        h.Bus.Publish(new NewBarEvent(after[300], after[301]));
        h.Bus.Publish(new RedrawEvent());

        string one = Assert.Single(h.Spoken);
        Assert.Contains("Close 200.50", one, StringComparison.Ordinal);
        Assert.Contains("Triple confluence buy", one, StringComparison.OrdinalIgnoreCase);
    }
}
