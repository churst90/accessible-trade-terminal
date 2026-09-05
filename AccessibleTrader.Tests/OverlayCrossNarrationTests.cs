using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// PRICE CROSSING A PLAIN OVERLAY LINE, on the bar close where it happened.
    ///
    /// <para>
    /// Cody, 2026-09-05: <i>"when you add things like ema's, these aren't included in playback
    /// narration even if you enable it, ema crosses should be announced though on new bar
    /// announcements."</i> The first half was already true and is deliberate — playback speaks
    /// discrete signals only, and a line has a value on every bar. The second half was NOT.
    /// </para>
    ///
    /// <para>
    /// <b>What was found.</b> Cross detection lived in <c>AutoNarrationService.ScanZoneLines</c>
    /// behind <c>comp.IsZoneLine</c> — a flag set by exactly two indicators in the repo, Cipher
    /// SR's pivot levels and Spider Lines' fibonacci EMAs. So flagging a plain EMA with N bought
    /// silence forever: no marker to fire, no registered oscillator definition to transition, and
    /// no zone-line flag to cross. The user had done everything the feature asks and the terminal
    /// had nothing to say.
    /// </para>
    ///
    /// <para>
    /// An overlay gets CROSSES and nothing else — no break, touch or approach. Those belong to a
    /// level, which has a polarity and can cease to exist; a moving average has neither.
    /// </para>
    /// </summary>
    public class OverlayCrossNarrationTests
    {
        private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
        {
            public List<string> Spoken { get; } = new();
            public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual) => Spoken.Add(message);
            public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
            public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
            public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
        }

        private sealed class NoContexts : IIndicatorContextAnalyzer
        {
            public void RegisterDefinition(IndicatorContextDefinition def) { }
            public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => null;
            public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
                => Enumerable.Empty<IndicatorContext>();
        }

        /// <summary>Closes walk 100, 100, 120 — the third bar jumps over an EMA parked at 110.</summary>
        private static TimeSeriesBuffer<Ohlcv> Bars(int count, double[] closes)
            => new(Enumerable.Range(0, count).Select(i =>
                new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                          100, 130, 90, closes[i], 1000)));

        private static SeriesConfig EmaConfig(string pane = "Main", bool zoneLine = false)
        {
            var cfg = new SeriesConfig
            {
                Id = "ema9",
                Name = "EMA",
                FriendlyName = "EMA 9",
                IndicatorCode = "Ema",
                Pane = pane,
                IsAutoNarrated = true,
                IsVisible = true,
                IsMuted = false,
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Ema",
                DisplayType = ComponentDisplayType.Line,
                IsVisible = true,
                IsZoneLine = zoneLine,
            });
            return cfg;
        }

        private static ChartSeries WithData(SeriesConfig cfg, double[] line)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Ema"] = line;
            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState State(ChartSeries series, int barCount, double[] closes)
            => WorkspaceState.Initial with
            {
                Data = Bars(barCount, closes),
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
            };

        /// <summary>
        /// Drives the real bar-close sequence: seed at two bars, one growth the scanner skips as
        /// historical, then the growth that closes bar 2 and scans it.
        /// </summary>
        private static List<string> RunToBarTwoClose(SeriesConfig cfg, double[] closes, double[] line)
        {
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            _ = new AutoNarrationService(store, bus, router, new NoContexts());

            store.EmitState(State(WithData(cfg, line.Take(2).ToArray()), 2, closes));
            bus.Publish(new RedrawEvent());

            store.EmitState(State(WithData(cfg, line.Take(3).ToArray()), 3, closes));
            bus.Publish(new RedrawEvent());

            store.EmitState(State(WithData(cfg, line), 4, closes));
            bus.Publish(new RedrawEvent());

            return router.Spoken;
        }

        // closes: bar 1 is below the line, bar 2 is above it.
        private static readonly double[] CrossUpCloses = { 100, 100, 120, 120 };
        private static readonly double[] FlatLine      = { 110, 110, 110, 110 };

        [Fact]
        public void PriceCrossingAnEma_IsAnnouncedOnTheBarClose()
        {
            var spoken = RunToBarTwoClose(EmaConfig(), CrossUpCloses, FlatLine);

            Assert.Contains(spoken, m => m.Contains("Price crossed above EMA 9", StringComparison.Ordinal));
        }

        [Fact]
        public void ADownwardCross_SaysBelow()
        {
            var spoken = RunToBarTwoClose(EmaConfig(), new double[] { 120, 120, 100, 100 }, FlatLine);

            Assert.Contains(spoken, m => m.Contains("Price crossed below EMA 9", StringComparison.Ordinal));
        }

        [Fact]
        public void PriceStayingOnOneSide_SaysNothing()
        {
            // The vacuity partner: the sequence above is a real bar close either way, so a
            // scanner that announced on every close would pass the two tests above.
            var spoken = RunToBarTwoClose(EmaConfig(), new double[] { 120, 120, 125, 125 }, FlatLine);

            Assert.DoesNotContain(spoken, m => m.Contains("crossed", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void AnOverlayInAnotherPane_IsNotComparedToPrice()
        {
            // An RSI line lives on a 0-100 axis. "Price crossed above RSI 14" is a category
            // error, and the close is above 70 on most charts in this repo's fixtures.
            var spoken = RunToBarTwoClose(EmaConfig(pane: "Pane_RSI"), CrossUpCloses, FlatLine);

            Assert.DoesNotContain(spoken, m => m.Contains("crossed", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ADeclaredZoneLine_IsLeftToTheZoneScan()
        {
            // Cipher SR and Spider Lines already announce their crosses with support/resistance
            // vocabulary. Announcing them here too would say the same cross twice in one breath.
            var spoken = RunToBarTwoClose(EmaConfig(zoneLine: true), CrossUpCloses, FlatLine);

            Assert.DoesNotContain(spoken, m => m.Contains("Price crossed above EMA 9", StringComparison.Ordinal));
        }

        [Fact]
        public void ASeriesNobodyFlagged_SaysNothing()
        {
            var cfg = EmaConfig();
            cfg.IsAutoNarrated = false;

            Assert.Empty(RunToBarTwoClose(cfg, CrossUpCloses, FlatLine));
        }

        [Fact]
        public void AComponentSelectionThatExcludesTheLine_SilencesIt()
        {
            // N on a component narrows narration to it; the EMA's own line is then not in the
            // selection and must not speak. (Two components so the selection is not the whole
            // series — see SeriesNarrationScope: an empty selection means ALL.)
            var cfg = EmaConfig();
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Other", DisplayType = ComponentDisplayType.Dot,
                IsVisible = true, IsAutoNarrated = true,
            });

            var spoken = RunToBarTwoClose(cfg, CrossUpCloses, FlatLine);

            Assert.DoesNotContain(spoken, m => m.Contains("crossed", StringComparison.OrdinalIgnoreCase));
        }
    }
}
