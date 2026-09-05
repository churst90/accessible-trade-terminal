using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The Narration tab's two master switches, at the services they gate.
    ///
    /// <para>
    /// The tab is organised by TRIGGER rather than by topic: the Speech tab governs how the
    /// terminal says what the user ASKED for, and Narration governs what it says when the user
    /// pressed nothing. These tests are the service half of that claim — that each switch
    /// actually reaches the code that speaks — and each carries its vacuity partner, because
    /// "nothing was announced" is the easiest assertion in this repo to pass for the wrong
    /// reason (a fixture that never had a signal in it passes with the switch either way).
    /// </para>
    /// </summary>
    public class NarrationSettingsTests
    {
        private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
        {
            public List<string> Spoken { get; } = new();
            public void Speak(string message, bool interrupt = false, SpeechChannel channel = SpeechChannel.Manual) => Spoken.Add(message);
            public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
            public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
            public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
        }

        private sealed class StubContextAnalyzer : IIndicatorContextAnalyzer
        {
            public void RegisterDefinition(IndicatorContextDefinition def) { }
            public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => null;
            public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
                => Enumerable.Empty<IndicatorContext>();
        }

        private static TimeSeriesBuffer<Ohlcv> Bars(int count) =>
            new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count)
                .Select(i => new Ohlcv(DateTime.UtcNow.AddMinutes(i), 100, 110, 95, 105, 1000)));

        private static SeriesConfig DotConfig()
        {
            var cfg = new SeriesConfig
            {
                Name = "CipherB",
                FriendlyName = "Cipher B",
                IndicatorCode = "CIPHER_B",
                IsAutoNarrated = true,
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Signal",
                DisplayName = "Bull Signal",
                DisplayType = ComponentDisplayType.Dot,
                IsVisible = true,
            });
            return cfg;
        }

        private static ChartSeries WithData(SeriesConfig cfg, double[] dots)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Signal"] = dots;
            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState State(ChartSeries series, int barCount, bool master) =>
            WorkspaceState.Initial with
            {
                Data = Bars(barCount),
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true,
                NarrateSignalsOnBarClose = master,
            };

        /// <summary>
        /// Drives the exact sequence <c>AutoNarrationTests.NewMarkerOnClosedBar_IsAnnounced</c>
        /// drives — seed at one bar, the signal bar closes, the next bar opens and the scan
        /// reaches it — and returns what was spoken. The scan window is forward-only, so the
        /// three steps are not padding: with fewer, the signal bar is never scanned and the
        /// test would report silence whatever the switch says.
        /// </summary>
        private static List<string> RunBarCloseScan(bool master)
        {
            var cfg = DotConfig();
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            using var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            store.EmitState(State(WithData(cfg, new[] { double.NaN }), 1, master));
            bus.Publish(new RedrawEvent());

            store.EmitState(State(WithData(cfg, new[] { double.NaN, 1.0 }), 2, master));
            bus.Publish(new RedrawEvent());

            store.EmitState(State(WithData(cfg, new[] { double.NaN, 1.0, double.NaN }), 3, master));
            bus.Publish(new RedrawEvent());

            return router.Spoken;
        }

        [Fact]
        public void NarrateSignalsOnBarClose_Off_SilencesTheBarCloseNarrator()
        {
            Assert.Empty(RunBarCloseScan(master: false));
        }

        [Fact]
        public void NarrateSignalsOnBarClose_On_LetsTheSameSignalThrough()
        {
            // The vacuity partner. Without it the test above passes on a fixture that never had
            // a signal in it, on a build where the narrator is broken for some other reason, and
            // on a build where the switch was never read at all.
            var spoken = Assert.Single(RunBarCloseScan(master: true));
            // The component introduces the signal; the series is never named ahead of one
            // (2026-09-05, SignalClauseSpeech).
            Assert.Contains("Bull Signal", spoken);
            Assert.DoesNotContain("Cipher B", spoken);
            Assert.Contains("Bull Signal", spoken);
        }

        [Fact]
        public void NarrateSignalsOnBarClose_DefaultsOn()
        {
            // ON is what shipped: the narrator only ever speaks about series the user flagged
            // with Ctrl+Alt+Shift+N, so a default of OFF would silence a channel the user had
            // already opted into, per series, by hand.
            Assert.True(WorkspaceState.Initial.NarrateSignalsOnBarClose);
            Assert.True(WorkspaceState.Initial.NarrateDuringPlayback);
        }
    }
}
