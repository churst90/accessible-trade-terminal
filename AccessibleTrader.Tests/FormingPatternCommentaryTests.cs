using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The running commentary on the bar that is still FORMING — "forming doji", "inside a double
/// top" — and the rules that keep it from becoming noise: only with N on the candles, at most once
/// every five seconds, never the same thing twice in a row, never the same formation twice, and
/// muted by F2 like all narration (Cody, 2026-09-30) while keeping narration's priority so the
/// next arrow key does not cut it off mid-word.
///
/// <para>
/// A2q (2026-10-01). <see cref="AccessibilityFeedbackCoordinator"/>'s intra-bar path had one test,
/// about which bars the analyser is handed; none of the gates above had any. The campaign moved
/// the commentary onto Shift+F2's channel, dropped the N gate, the debounce, the change gate and
/// the once-per-formation memory, and every one of them survived.
/// </para>
///
/// <para>
/// Two tests wait out the real five-second debounce. There is no clock seam in the coordinator,
/// and the alternative — reaching into its private fields — would test the fields, not the
/// behaviour.
/// </para>
/// </summary>
public sealed class FormingPatternCommentaryTests
{
    private static readonly DateTime T0 = new(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Thirty ordinary one-minute bars: a gentle rise, middling bodies.</summary>
    private static List<Ohlcv> History() => Enumerable.Range(0, 30)
        .Select(i => new Ohlcv(T0.AddMinutes(i), 100 + i * 0.2, 101 + i * 0.2, 99 + i * 0.2, 100.6 + i * 0.2, 10))
        .ToList();

    /// <summary>The live bar as a doji: open equals close, long shadows both ways.</summary>
    private static Ohlcv FormingDoji(List<Ohlcv> h) => new(T0.AddMinutes(h.Count), 106, 109, 103, 106, 5);

    /// <summary>The live bar as a full-bodied rise with no shadows: a different shape.</summary>
    private static Ohlcv FormingMarubozu(List<Ohlcv> h) => new(T0.AddMinutes(h.Count), 103, 109, 103, 109, 5);

    private sealed class FixedPatterns : IChartPatternCache
    {
        public IReadOnlyList<ChartPattern> Patterns { get; set; } = Array.Empty<ChartPattern>();
        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars) => Patterns;
    }

    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public MockWorkspaceStore Store { get; } = new();
        public CounterSpeechManager Speech { get; } = new();
        public SpeechFeedbackRouter Router { get; }
        public FixedPatterns Patterns { get; } = new();
        public List<Ohlcv> Bars { get; } = History();

        public Harness(bool candlesNarrated = true, bool chartSpeech = true, bool eventSpeech = true,
            bool formations = false)
        {
            var cfg = new SeriesConfig
            {
                Id = CoreSeriesIds.Candles, Name = "Candles", IndicatorCode = "CANDLES",
                Pane = "Main", IsAutoNarrated = candlesNarrated,
            };
            var candles = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
            Store.EmitState(WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(Bars),
                CurrentDataIndex = Bars.Count - 1,
                ActiveSeries = ImmutableList.Create(candles),
                PrimarySeriesId = CoreSeriesIds.Candles,
                AnnounceNewBars = true,
                DescribeCandlePatterns = true,
                DescribeChartPatterns = formations,
                IsSpeechEnabled = chartSpeech,
                IsEventSpeechEnabled = eventSpeech,
            });

            var settings = Substitute.For<IAppSettings>();
            settings.NarrateFormingCandlePatterns.Returns(!formations);
            settings.NarrateFormingChartPatterns.Returns(formations);

            var formatter = new SpeechFormatter();
            Router = new SpeechFeedbackRouter(Speech, formatter, Store);
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(Router, formatter), Router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                Patterns, new ChartPatternFocus(), new MockAutoNarrationService(),
                appSettings: settings);
        }

        public List<string> Tick(Ohlcv forming)
        {
            int before = Speech.Utterances.Count;
            Bus.Publish(new IntraBarUpdateEvent(forming, Bars[^1], Bars[^2]));
            return Speech.Utterances.Skip(before).Select(u => u.Text).ToList();
        }
    }

    // ── Candle patterns on the live bar ──────────────────────────────────────

    [Fact]
    public void A_forming_candle_pattern_is_commented_on_when_the_candles_are_narrated()
    {
        // The partner every silence test below needs: the fixture does produce commentary.
        var h = new Harness();
        var said = h.Tick(FormingDoji(h.Bars));
        Assert.Contains(said, s => s.Contains("doji", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_N_on_the_candles_there_is_no_running_commentary()
    {
        // Continuous unprompted speech requires the user to have asked for it (2026-04-09).
        var h = new Harness(candlesNarrated: false);
        Assert.Empty(h.Tick(FormingDoji(h.Bars)));
    }

    [Fact]
    public void F2_silences_the_forming_commentary()
    {
        // Narration is F2's (Cody, 2026-09-30): the chart's mute silences the chart's commentary.
        var h = new Harness(chartSpeech: false);
        Assert.Empty(h.Tick(FormingDoji(h.Bars)));
    }

    [Fact]
    public void Shift_F2_does_not_silence_the_forming_commentary()
    {
        // ...and narration is NOT Shift+F2's any more: that is alerts and events.
        var h = new Harness(eventSpeech: false);
        Assert.NotEmpty(h.Tick(FormingDoji(h.Bars)));
    }

    [Fact]
    public void A_second_pattern_within_five_seconds_waits()
    {
        // The debounce. The live bar flickers between shapes tick by tick; without it the user
        // hears "doji", "marubozu", "doji" as fast as the feed arrives.
        var h = new Harness();
        Assert.NotEmpty(h.Tick(FormingDoji(h.Bars)));

        Assert.Empty(h.Tick(FormingMarubozu(h.Bars)));
    }

    [Fact]
    public async Task The_same_forming_pattern_is_not_said_again_after_the_debounce()
    {
        // Only a CHANGE is news. Five seconds later the bar is still a doji: say nothing.
        var h = new Harness();
        Assert.NotEmpty(h.Tick(FormingDoji(h.Bars)));

        await Task.Delay(TimeSpan.FromSeconds(5.3));

        Assert.Empty(h.Tick(FormingDoji(h.Bars)));
    }

    // ── Chart formations on the live bar ─────────────────────────────────────

    /// <summary>A double top whose structure became knowable at bar 25 and which has not
    /// resolved by the live bar (index 29).</summary>
    private static ChartPattern FormingDoubleTop(List<Ohlcv> bars) => new(
        ChartPatternKind.DoubleTop, ChartPatternState.Forming,
        StartBarIndex: 10, EndBarIndex: 25, KnownAtIndex: 25, TriggerLevel: 102.0,
        StartTime: bars[10].Date, EndTime: bars[25].Date, ExpiresAtIndex: 60);

    [Fact]
    public async Task A_forming_formation_is_announced_once_not_every_five_seconds()
    {
        var h = new Harness(formations: true);
        h.Patterns.Patterns = new[] { FormingDoubleTop(h.Bars) };
        var first = h.Tick(FormingDoji(h.Bars));
        Assert.Contains(first, s => s.Contains("double top", StringComparison.OrdinalIgnoreCase));

        await Task.Delay(TimeSpan.FromSeconds(5.3));

        var again = h.Tick(FormingDoji(h.Bars));
        Assert.DoesNotContain(again, s => s.Contains("double top", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_arrow_key_does_not_cut_off_a_forming_formation_mid_sentence()
    {
        // Narration keeps the event tier's priority: the bar reading the user's next arrow
        // press produces is queued behind it, not spoken over it.
        var h = new Harness(formations: true);
        h.Patterns.Patterns = new[] { FormingDoubleTop(h.Bars) };
        Assert.Contains(h.Tick(FormingDoji(h.Bars)), s => s.Contains("double top", StringComparison.OrdinalIgnoreCase));
        int silencedBefore = h.Speech.SilenceCalls;

        h.Router.Speak("Close 105.60", interrupt: true, channel: SpeechChannel.Chart);

        Assert.Equal(silencedBefore, h.Speech.SilenceCalls);
        Assert.False(h.Speech.LastInterrupt);
    }
}
