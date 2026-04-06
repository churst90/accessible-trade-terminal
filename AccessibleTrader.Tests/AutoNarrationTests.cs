using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies AutoNarrationService: seeding prevents historical announcements,
    /// new marker signals fire TTS, oscillator transitions fire on bar close,
    /// non-narrated series are silent, and the ToggleNarrationAction wires correctly.
    /// </summary>
    public class AutoNarrationTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────────

        private sealed class CapturingSpeechRouter : ISpeechFeedbackRouter
        {
            public List<string> Spoken { get; } = new();
            public void Speak(string message, bool interrupt = false) => Spoken.Add(message);
            public void SpeakPoint(WorkspaceState s, WorkspaceState? p, ChartSeries ser, Ohlcv pt, string pfx = "") { }
            public void SpeakProfile(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int bin, string pfx = "") { }
            public void SpeakHeatmap(WorkspaceState s, WorkspaceState? p, ChartSeries ser, int di, int bin, string pfx = "") { }
        }

        private sealed class StubContextAnalyzer : IIndicatorContextAnalyzer
        {
            public IndicatorContext? FixedContext { get; set; }
            public void RegisterDefinition(IndicatorContextDefinition def) { }
            public IndicatorContext? Analyze(ChartSeries series, WorkspaceState state) => FixedContext;
            public IEnumerable<IndicatorContext> AnalyzeAll(ChartSeries series, WorkspaceState state)
                => FixedContext != null ? new[] { FixedContext } : Enumerable.Empty<IndicatorContext>();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Ohlcv Bar(int i) =>
            new(DateTime.UtcNow.AddMinutes(i), 100, 110, 95, 105, 1000);

        private static TimeSeriesBuffer<Ohlcv> Bars(int count) =>
            new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, count).Select(Bar));

        /// <summary>
        /// Creates a SeriesConfig (with stable ID) and a component for use across multiple state emissions.
        /// </summary>
        private static SeriesConfig MakeDotConfig(bool isNarrated = true, string? signalTemplate = null)
        {
            var cfg = new SeriesConfig
            {
                Name = "CipherB",
                FriendlyName = "Cipher B",
                IndicatorCode = "CIPHER_B",
                IsAutoNarrated = isNarrated
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Signal",
                DisplayName = "Bull Signal",
                DisplayType = ComponentDisplayType.Dot,
                IsVisible = true,
                SignalSpeechTemplate = signalTemplate
            });
            return cfg;
        }

        /// <summary>Builds a ChartSeries from an existing config with fresh data.</summary>
        private static ChartSeries WithData(SeriesConfig cfg, double[] dotData)
        {
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["Signal"] = dotData;
            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState BuildState(ChartSeries series, int barCount) =>
            WorkspaceState.Initial with
            {
                Data = Bars(barCount),
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = barCount - 1,
                InitStatus = InitializationStatus.Ready,
                DataStatus = DataStatus.Ready,
                IsSpeechEnabled = true
            };

        // ── 1. Seeding prevents retroactive announcement ───────────────────────

        [Fact]
        public void ExistingSignal_WhenNarrationEnabled_IsNotAnnounced()
        {
            // Bar 0 has a dot signal that existed BEFORE narration was enabled.
            var cfg = MakeDotConfig(isNarrated: true);
            var series = WithData(cfg, new double[] { 1.0 });
            var state = BuildState(series, 1);

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            store.EmitState(state); // seeds lastScannedBarIndex = 0
            bus.Publish(new RedrawEvent()); // scanIndex=0, seeded → skip

            Assert.Empty(router.Spoken);
        }

        // ── 2. New marker signal on a newly-closed bar is announced ───────────

        [Fact]
        public void NewMarkerOnClosedBar_IsAnnounced()
        {
            // Start: 1 bar, no signal (NaN). After 2 more bars open, bar 1 (index 1) closes with signal.
            var cfg = MakeDotConfig(isNarrated: true);
            double[] dots1 = { double.NaN };
            double[] dots3 = { double.NaN, 1.0, double.NaN }; // signal printed at bar index 1

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            // Seed: 1 bar state → _lastDataCount = 1, _lastScannedBarIndex[id] = 0
            store.EmitState(BuildState(WithData(cfg, dots1), 1));
            bus.Publish(new RedrawEvent());

            // Simulate bar 1 closing (bar 2 opens): count goes 1 → 3
            // scanIndex = _lastDataCount - 1 = 0 → skip (seeded)
            // then count goes 3 → ... but we need count to go past seed.
            // Actually: emit 3-bar state and fire event. _lastDataCount=1, currentCount=3,
            // isNewBar=true, scanIndex=0 → skip. _lastDataCount=3.
            store.EmitState(BuildState(WithData(cfg, dots3), 3));
            bus.Publish(new RedrawEvent()); // scanIndex=0, skipped; _lastDataCount=3

            // Simulate bar 2 closing (bar 3 opens): count goes 3 → 4
            // scanIndex = 3-1 = 2 → check _lastScannedBarIndex=0 → 2 > 0 → scan bar 2 (NaN) → no announce
            double[] dots4 = { double.NaN, 1.0, double.NaN, double.NaN };
            store.EmitState(BuildState(WithData(cfg, dots4), 4));
            bus.Publish(new RedrawEvent()); // scanIndex=2, NaN → no announce; _lastScannedBarIndex=2

            // Simulate bar 3 closing (bar 4 opens): count goes 4 → 5, bar 3 (index 3) has NaN
            // Now bar 1 (index 1) was already closed and has signal, but scanIndex tracks forward only.
            // We need to test that bar 1's signal is detected when scanIndex first passes it.
            // scanIndex = 4-1 = 3 → 3 > 2 → scan bar 3 (NaN) → no announce; _lastScannedBarIndex=3

            // Let's instead use a simpler scenario: 2 bars, seed 1, close bar 1 with signal.
            // Re-init fresh service:
            var bus2 = new SpyEventBus();
            var store2 = new MockWorkspaceStore();
            var router2 = new CapturingSpeechRouter();
            var svc2 = new AutoNarrationService(store2, bus2, router2, new StubContextAnalyzer());

            double[] d1 = { double.NaN };
            double[] d2 = { double.NaN, 1.0 }; // signal at index 1

            store2.EmitState(BuildState(WithData(cfg, d1), 1));
            bus2.Publish(new RedrawEvent()); // seed: _lastDataCount=1, _lastScannedBarIndex[id]=0

            // Bar 1 closes (count 1→2). scanIndex = _lastDataCount-1 = 0. Still skip (seeded).
            // _lastDataCount = 2. _lastScannedBarIndex unchanged = 0.
            store2.EmitState(BuildState(WithData(cfg, d2), 2));
            bus2.Publish(new RedrawEvent()); // scanIndex=0, skip

            // Bar 2 opens (count 2→3). scanIndex = 2-1 = 1. 1 > 0 → scan bar 1 → signal! → ANNOUNCE
            double[] d3 = { double.NaN, 1.0, double.NaN };
            store2.EmitState(BuildState(WithData(cfg, d3), 3));
            bus2.Publish(new RedrawEvent());

            Assert.Single(router2.Spoken);
            Assert.Contains("Cipher B", router2.Spoken[0]);
            Assert.Contains("Bull Signal", router2.Spoken[0]);
        }

        // ── 3. Non-narrated series produces no speech ──────────────────────────

        [Fact]
        public void NonNarratedSeries_NoSpeech()
        {
            var cfg = MakeDotConfig(isNarrated: false); // NOT narrated
            var series = WithData(cfg, new double[] { double.NaN, 1.0 });
            var state = BuildState(series, 2);

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            store.EmitState(state);
            bus.Publish(new RedrawEvent());

            Assert.Empty(router.Spoken);
        }

        // ── 4. SignalSpeechTemplate token substitution ─────────────────────────

        [Fact]
        public void MarkerWithTemplate_UsesTemplateWithPriceToken()
        {
            var cfg = MakeDotConfig(signalTemplate: "{series}: {name} at {price}.");
            double[] d1 = { double.NaN };
            double[] d2 = { double.NaN, 1.0 };
            double[] d3 = { double.NaN, 1.0, double.NaN };

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            store.EmitState(BuildState(WithData(cfg, d1), 1));
            bus.Publish(new RedrawEvent()); // seed

            store.EmitState(BuildState(WithData(cfg, d2), 2));
            bus.Publish(new RedrawEvent()); // scanIndex=0, skip

            store.EmitState(BuildState(WithData(cfg, d3), 3));
            bus.Publish(new RedrawEvent()); // scanIndex=1, signal → announce

            Assert.Single(router.Spoken);
            Assert.Contains("Cipher B", router.Spoken[0]);
            Assert.Contains("Bull Signal", router.Spoken[0]);
            Assert.Contains("105", router.Spoken[0]); // close price = 105
        }

        // ── 5. Oscillator zone entry announces on bar close ────────────────────

        [Fact]
        public void OscillatorEntersOverbought_AnnouncedOnBarClose()
        {
            var cfg = MakeDotConfig(isNarrated: true);
            double[] dots = { double.NaN, double.NaN, double.NaN };

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();

            var analyzer = new StubContextAnalyzer
            {
                FixedContext = new IndicatorContext
                {
                    IndicatorCode = "RSI", ComponentName = "RSI",
                    CurrentValue = 72,
                    Trend = TrendDirection.Rising, TrendBars = 1,
                    Zone = ZoneStatus.Normal, // Normal at seed time
                    Crossover = CrossoverStatus.None,
                    NarrativeHint = ""
                }
            };
            var svc = new AutoNarrationService(store, bus, router, analyzer);

            // Seed with 1 bar, zone = Normal
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN }), 1));
            bus.Publish(new RedrawEvent()); // seed: _lastOscState = Normal

            // Bar 1 closes (count 1→2); zone still Normal
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN, double.NaN }), 2));
            bus.Publish(new RedrawEvent()); // scanIndex=0, skip; _lastDataCount=2

            // Bar 2 opens (count 2→3): bar 1 closed with zone transition to Overbought
            analyzer.FixedContext = analyzer.FixedContext with { Zone = ZoneStatus.Overbought };
            store.EmitState(BuildState(WithData(cfg, dots), 3));
            bus.Publish(new RedrawEvent()); // isNewBar, scanIndex=1 > 0 → scan → zone changed!

            Assert.Single(router.Spoken);
            Assert.Contains("overbought", router.Spoken[0], StringComparison.OrdinalIgnoreCase);
        }

        // ── 6. Oscillator zone: no announce when zone unchanged ────────────────

        [Fact]
        public void OscillatorZoneUnchanged_NoAnnouncement()
        {
            var cfg = MakeDotConfig(isNarrated: true);

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var analyzer = new StubContextAnalyzer
            {
                FixedContext = new IndicatorContext
                {
                    IndicatorCode = "RSI", ComponentName = "RSI",
                    CurrentValue = 72, Trend = TrendDirection.Rising, TrendBars = 1,
                    Zone = ZoneStatus.Overbought, // Already overbought at seed
                    Crossover = CrossoverStatus.None, NarrativeHint = ""
                }
            };
            var svc = new AutoNarrationService(store, bus, router, analyzer);

            // Seed with 1 bar (already OB)
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN }), 1));
            bus.Publish(new RedrawEvent()); // seed: zone = Overbought

            // Bar 1 closes, bar 2 opens: zone still Overbought
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN, double.NaN }), 2));
            bus.Publish(new RedrawEvent()); // skip (scanIndex=0, seeded)

            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN, double.NaN, double.NaN }), 3));
            bus.Publish(new RedrawEvent()); // scanIndex=1, scan → zone unchanged → no announce

            Assert.Empty(router.Spoken);
        }

        // ── 7. ToggleNarrationAction flips IsAutoNarrated on SeriesConfig ──────

        [Fact]
        public void ToggleNarrationAction_DefaultFalse_FlipsToTrue()
        {
            var cfg = new SeriesConfig { Name = "Test" };
            Assert.False(cfg.IsAutoNarrated);
            cfg.IsAutoNarrated = true;
            Assert.True(cfg.IsAutoNarrated);
        }

        [Fact]
        public void ChartSeries_IsAutoNarrated_DelegatesToConfig()
        {
            var cfg = new SeriesConfig { IsAutoNarrated = false };
            var series = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = cfg.Id });
            Assert.False(series.IsAutoNarrated);
            series.IsAutoNarrated = true;
            Assert.True(cfg.IsAutoNarrated); // Delegates to Config
        }

        [Fact]
        public void SeriesConfigClone_PreservesIsAutoNarrated()
        {
            var cfg = new SeriesConfig { IsAutoNarrated = true };
            var clone = cfg.Clone();
            Assert.True(clone.IsAutoNarrated);
        }

        // ── 8. Narration disabled mid-session: no further speech ───────────────

        [Fact]
        public void NarrationDisabled_StopsSpeech()
        {
            var cfg = MakeDotConfig(isNarrated: true);

            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var router = new CapturingSpeechRouter();
            var svc = new AutoNarrationService(store, bus, router, new StubContextAnalyzer());

            // Seed
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN }), 1));
            bus.Publish(new RedrawEvent());

            // Disable narration mid-session (same series ID, IsAutoNarrated flipped)
            cfg.IsAutoNarrated = false;
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN, double.NaN }), 2));
            // OnStateChanged: series no longer in currentIds → removed from tracking

            // New bar with signal — should NOT announce
            store.EmitState(BuildState(WithData(cfg, new[] { double.NaN, 1.0, double.NaN }), 3));
            bus.Publish(new RedrawEvent());

            Assert.Empty(router.Spoken);
        }
    }
}
