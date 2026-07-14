using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 2 coverage for <see cref="NavigationSonifier.FireClusterTicksAsync"/> and the
    /// voice-slot discipline called out in <c>NavigationSonifier</c>'s slot-layout comment:
    ///
    ///   Slots  0– 7 : Navigation — focused bar/component (slot 0) + cluster ticks (slots 3–7)
    ///   Slots 16–31 : UI earcons (PlayNote round-robin modulo 16)
    ///   Slots 32–63 : Playback sequencer
    ///
    /// The cluster-tick ordering rule — tier ascending, positive before negative within each
    /// tier — is the load-bearing "significance order" the docstring promises but which no
    /// test previously pinned. These tests drive <c>FireClusterTicksAsync</c> and
    /// <c>SyncNavigationSlots</c> against a spy <see cref="IAudioDriver"/> and read back the
    /// SetVoice/StopVoice calls to assert slot + firing order.
    /// </summary>
    public class NavigationSonifierClusterTests
    {
        // ── Cluster ordering rules (per-bar significance) ────────────────────

        [Fact]
        public async Task Cluster_OrdersByTierAscending()
        {
            // Series holds three marker components with different tiers:
            //   tier 1 = "SR Diamond" (Diamond on Main pane) — structural SR
            //   tier 3 = "Buy Signal" (Dot, name matches "Signal")
            //   tier 4 = "Neutral Dot" (Dot, no tier-match keyword)
            // FireClusterTicksAsync must sort ascending and fire on slots 3,4,5 in that order.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var series = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("srDiamond", "SR Diamond", ComponentDisplayType.Diamond, baseFreq: 500),
                MakeMarker("buySignal", "Buy Signal", ComponentDisplayType.Dot,     baseFreq: 600),
                MakeMarker("neutral",   "Neutral Dot", ComponentDisplayType.Dot,    baseFreq: 300),
            });
            var state = StateWithSeries(series, focusedComponentIndex: -1);

            await sonifier.FireClusterTicksAsync(state, dataIndex: 0, excludeSeriesId: series.Id, excludeComponentIndex: -1);

            // Collect only the cluster-tick calls (slots 3-7).
            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).OrderBy(c => c.Slot).ToList();
            Assert.Equal(3, ticks.Count);
            Assert.Equal(500, ticks[0].Frequency); // tier 1 on slot 3
            Assert.Equal(600, ticks[1].Frequency); // tier 3 on slot 4
            Assert.Equal(300, ticks[2].Frequency); // tier 4 on slot 5
        }

        [Fact]
        public async Task Cluster_PositiveBeforeNegative_WithinSameTier()
        {
            // Two tier-3 signals — one with name "Buy" (positive), one "Sell" (negative).
            // Expected order: positive first (slot 3), negative second (slot 4).
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var series = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("sell",  "Sell Signal", ComponentDisplayType.Dot, baseFreq: 220),
                MakeMarker("buy",   "Buy Signal",  ComponentDisplayType.Dot, baseFreq: 880),
            });
            var state = StateWithSeries(series);

            await sonifier.FireClusterTicksAsync(state, 0, series.Id, -1);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).OrderBy(c => c.Slot).ToList();
            Assert.Equal(2, ticks.Count);
            Assert.Equal(880, ticks[0].Frequency); // "Buy" (positive) first
            Assert.Equal(220, ticks[1].Frequency); // "Sell" (negative) second
        }

        [Fact]
        public async Task Cluster_SkipsNaNComponents()
        {
            // The "Dead Signal" component has NaN at dataIndex=0 — must not emit a voice.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var live = MakeMarker("live", "Live Buy Signal", ComponentDisplayType.Dot, baseFreq: 500);
            var dead = MakeMarker("dead", "Dead Signal",     ComponentDisplayType.Dot, baseFreq: 700);
            var series = MakeSeries("candles", "Main", new[] { live, dead },
                data: new Dictionary<string, double[]>
                {
                    ["live"] = new[] { 1.0, 1.0 },
                    ["dead"] = new[] { double.NaN, 1.0 },
                });

            var state = StateWithSeries(series);
            await sonifier.FireClusterTicksAsync(state, 0, series.Id, -1);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(500, ticks[0].Frequency);
        }

        [Fact]
        public async Task Cluster_SkipsExcludedFocusedComponent()
        {
            // The focused component (already fired on slot 0) must not be re-fired in the cluster.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var series = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("focused", "Focused Buy Signal", ComponentDisplayType.Dot, baseFreq: 500),
                MakeMarker("other",   "Other Buy Signal",   ComponentDisplayType.Dot, baseFreq: 700),
            });
            var state = StateWithSeries(series, focusedComponentIndex: 0);

            await sonifier.FireClusterTicksAsync(state, 0, excludeSeriesId: series.Id, excludeComponentIndex: 0);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(700, ticks[0].Frequency);
        }

        [Fact]
        public async Task Cluster_SkipsIsZoneLineComponents()
        {
            // IsZoneLine markers are handled via proximity speech, not cluster audio.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var normal = MakeMarker("normal", "Normal Buy Signal", ComponentDisplayType.Dot, baseFreq: 500);
            var zone   = MakeMarker("zone",   "Zone Line Signal",  ComponentDisplayType.Dot, baseFreq: 700);
            zone.IsZoneLine = true;

            var series = MakeSeries("candles", "Main", new[] { normal, zone });
            var state = StateWithSeries(series);

            await sonifier.FireClusterTicksAsync(state, 0, series.Id, -1);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(500, ticks[0].Frequency);
        }

        [Fact]
        public async Task Cluster_SkipsNonMarkerDisplayTypes()
        {
            // Line / Histogram / Oscillator components must not participate in the cluster.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var line = MakeMarker("line", "Line Signal", ComponentDisplayType.Line,      baseFreq: 500);
            var hist = MakeMarker("hist", "Hist Signal", ComponentDisplayType.Histogram, baseFreq: 600);
            var dot  = MakeMarker("dot",  "Buy Signal",  ComponentDisplayType.Dot,       baseFreq: 700);
            var series = MakeSeries("candles", "Main", new[] { line, hist, dot });
            var state = StateWithSeries(series);

            await sonifier.FireClusterTicksAsync(state, 0, series.Id, -1);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(700, ticks[0].Frequency);
        }

        [Fact]
        public async Task Cluster_FiresAtMostFiveTicks_OnSlotsThreeThroughSeven()
        {
            // Seven marker components — only five slots (3..7) are available. Sixth and
            // seventh markers must be dropped rather than spilling into navigation slots.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var comps = Enumerable.Range(0, 7)
                .Select(i => MakeMarker($"m{i}", $"Buy Signal {i}", ComponentDisplayType.Dot, baseFreq: 400 + i * 10))
                .ToArray();
            var series = MakeSeries("candles", "Main", comps);
            var state = StateWithSeries(series);

            await sonifier.FireClusterTicksAsync(state, 0, series.Id, -1);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 0 && c.Slot < 32).ToList();
            Assert.Equal(5, ticks.Count);
            Assert.All(ticks, t => Assert.InRange(t.Slot, 3, 7));
            // No navigation or earcon slot was touched.
            Assert.DoesNotContain(driver.SetVoiceCalls, c => c.Slot < 3);
            Assert.DoesNotContain(driver.SetVoiceCalls, c => c.Slot >= 8 && c.Slot < 32);
        }

        // ── Cross-pane / cross-series slot scoping ───────────────────────────

        [Fact]
        public async Task Cluster_NavigationMode_OnlyScansFocusedSeries()
        {
            // Two visible series on two panes. In navigation mode (crossSeriesMode=false) the
            // cluster only scans the focused series (passed as excludeSeriesId). The marker on
            // the OTHER series must not fire — matters because nav gestures target one series
            // and cross-pane audio would be confusing during manual step-through.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var focusedSeries = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("focusedMarker", "Focused Buy Signal", ComponentDisplayType.Dot, baseFreq: 400),
            });
            var otherSeries = MakeSeries("rsi", "RSI", new[]
            {
                MakeMarker("otherMarker", "Other Buy Signal", ComponentDisplayType.Dot, baseFreq: 800),
            });
            var state = StateWithSeries(focusedSeries, otherSeries);

            await sonifier.FireClusterTicksAsync(state, 0, excludeSeriesId: focusedSeries.Id, excludeComponentIndex: -1, crossSeriesMode: false);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(400, ticks[0].Frequency);
        }

        [Fact]
        public async Task Cluster_PlaybackMode_ScansAllVisibleSeries()
        {
            // Same setup as above but crossSeriesMode=true — playback pulse fires every pane's
            // markers together, respecting the significance sort across series.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var s1 = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("mainM", "Main Buy Signal", ComponentDisplayType.Dot, baseFreq: 400),
            });
            var s2 = MakeSeries("rsi", "RSI", new[]
            {
                MakeMarker("rsiM", "RSI Buy Signal", ComponentDisplayType.Dot, baseFreq: 800),
            });
            var state = StateWithSeries(s1, s2);

            await sonifier.FireClusterTicksAsync(state, 0, excludeSeriesId: s1.Id, excludeComponentIndex: -1, crossSeriesMode: true);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Equal(2, ticks.Count);
            var freqs = ticks.Select(t => t.Frequency).ToHashSet();
            Assert.Contains(400.0, freqs);
            Assert.Contains(800.0, freqs);
        }

        [Fact]
        public async Task Cluster_SkipsProfileAndHeatmapSeries()
        {
            // Distribution/Heatmap series use dedicated sonification paths and must never
            // contribute to the cluster bus — even when crossSeriesMode is on.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var dotSeries = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("mainDot", "Main Buy Signal", ComponentDisplayType.Dot, baseFreq: 400),
            });
            var heatmap = MakeSeries("heatmap", "VPVR", new[]
            {
                MakeMarker("heatmapCell", "Heatmap Buy Signal", ComponentDisplayType.Heatmap, baseFreq: 900),
            });
            var state = StateWithSeries(dotSeries, heatmap);

            await sonifier.FireClusterTicksAsync(state, 0, dotSeries.Id, -1, crossSeriesMode: true);

            var ticks = driver.SetVoiceCalls.Where(c => c.Slot >= 3 && c.Slot <= 7).ToList();
            Assert.Single(ticks);
            Assert.Equal(400, ticks[0].Frequency);
        }

        // ── SyncNavigationSlots slot discipline ──────────────────────────────

        [Fact]
        public void SyncNavigationSlots_UsesSlotZero_AndClearsSlotsTwoThroughSeven()
        {
            // SyncNavigationSlots fires the focused component's note on slot 0 and MUST call
            // StopVoice on slots 2-7 so lingering cluster ticks from a prior bar don't
            // overlap the new focused note. Slot 1 is reserved for the gradient/detuned
            // overlay and is left alone by non-gradient components.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            var series = MakeSeries("candles", "Main", new[]
            {
                MakeMarker("focus", "Focus Buy Signal", ComponentDisplayType.Dot, baseFreq: 440),
            });
            var state = StateWithSeries(series, focusedComponentIndex: 0);

            sonifier.SyncNavigationSlots(state);

            Assert.Contains(driver.SetVoiceCalls, c => c.Slot == 0);
            // Slots 2-7 must have been explicitly stopped.
            for (int s = 2; s < 8; s++)
                Assert.Contains(s, driver.StoppedSlots);
        }

        [Fact]
        public void PlayNote_UsesUiSlotRangeSixteenThroughThirtyOne()
        {
            // UI earcons round-robin over slots 16..31, never into navigation slots 0..7.
            // Calling PlayNote 40 times must keep every call inside that window.
            var driver = new SpyAudioDriver();
            var sonifier = BuildSonifier(driver);

            for (int i = 0; i < 40; i++)
                sonifier.PlayNote(freq: 440, dur: 0.05, wave: "sine", vol: 0.1f, pan: 0f);

            Assert.Equal(40, driver.SetVoiceCalls.Count);
            Assert.All(driver.SetVoiceCalls, c => Assert.InRange(c.Slot, 16, 31));
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private static NavigationSonifier BuildSonifier(SpyAudioDriver driver)
            => new NavigationSonifier(driver, new NoOpStrategy(), new SoundPatchRegistry());

        /// <summary>
        /// Builds a marker component with the given display type. DecayMs=50 keeps the Ping
        /// duration deterministic without dragging in any SoundPatch lookup.
        /// </summary>
        private static ComponentConfig MakeMarker(string name, string displayName, ComponentDisplayType type, double baseFreq)
        {
            return new ComponentConfig
            {
                Name = name,
                DisplayName = displayName,
                DisplayType = type,
                BaseFrequency = baseFreq,
                FreqMultiplier = 1.0,
                Volume = 1.0f,
                Waveform = "sine",
                DecayMs = 50,
                IsVisible = true,
                IsMuted = false,
            };
        }

        /// <summary>
        /// Builds a ChartSeries with the supplied components and a ComponentData array of
        /// length 2 per component (defaults to [1.0, 1.0] so dataIndex=0 has a non-NaN
        /// value). Pass a custom <paramref name="data"/> dict to override per-component arrays.
        /// </summary>
        private static ChartSeries MakeSeries(
            string id,
            string pane,
            ComponentConfig[] comps,
            Dictionary<string, double[]>? data = null)
        {
            var cfg = new SeriesConfig { Id = id, Name = id, IndicatorCode = id.ToUpperInvariant(), Pane = pane };
            foreach (var c in comps) cfg.Components.Add(c);
            var buf = new SeriesDataBuffer { SeriesId = id };
            if (data != null)
            {
                buf.ComponentData = new Dictionary<string, double[]>(data);
            }
            else
            {
                foreach (var c in comps)
                    buf.ComponentData[c.Name] = new[] { 1.0, 1.0 };
            }
            return new ChartSeries(cfg, buf);
        }

        private static WorkspaceState StateWithSeries(params ChartSeries[] series)
            => StateWithSeries(series, focusedComponentIndex: 0);

        private static WorkspaceState StateWithSeries(ChartSeries[] series, int focusedComponentIndex)
            => StateWithSeries(series.Length >= 1 ? series[0] : null, series, focusedComponentIndex);

        private static WorkspaceState StateWithSeries(ChartSeries focused, int focusedComponentIndex = 0)
            => StateWithSeries(focused, new[] { focused }, focusedComponentIndex);

        private static WorkspaceState StateWithSeries(
            ChartSeries? focused,
            ChartSeries[] all,
            int focusedComponentIndex)
        {
            // Tiny data buffer — just two bars so dataIndex=0 is valid inside state.Data.
            var bars = new List<Ohlcv>
            {
                new Ohlcv(new DateTime(2026, 04, 23, 0, 0, 0), 100, 100, 100, 100, 0),
                new Ohlcv(new DateTime(2026, 04, 23, 0, 5, 0), 100, 100, 100, 100, 0),
            };
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.CreateRange(all),
                FocusedSeriesId = focused?.Id,
                FocusedSeriesIndex = 0,
                FocusedComponentIndex = focusedComponentIndex,
                CurrentDataIndex = 0,
                ViewportStartIndex = 0,
                ViewportLength = 100,
                ChartVolume = 1.0f,
                PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                    .Add("Main", (0, 200))
                    .Add("RSI",  (0, 100)),
                ViewportRange = (0, 200),
            };
        }

        // ── Spies / no-op stubs ──────────────────────────────────────────────

        private sealed record VoiceCall(
            int Slot, double Frequency, float Volume, float Pan, string Waveform,
            bool Continuous, double DurationSeconds, int DataIndex, string Envelope,
            bool Click, float NoiseAmount, string NoiseType);

        private sealed class SpyAudioDriver : IAudioDriver
        {
            public int SampleRate => 48000;
            public int Channels => 2;
#pragma warning disable CS0067
            public event Action<int>? PointReached;
#pragma warning restore CS0067

            public List<VoiceCall> SetVoiceCalls { get; } = new();
            public List<int> StoppedSlots { get; } = new();

            public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
                bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
                bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f, float sawMix = 0f,
                float triangleMix = 0f, float subSawMix = 0f)
            {
                SetVoiceCalls.Add(new VoiceCall(slot, frequency, volume, pan, waveform, continuous,
                    durationSeconds, dataIndex, envelope, click, noiseAmount, noiseType));
            }

            public void StopVoice(int slot) => StoppedSlots.Add(slot);
            public void StopAll() { }
            public void Reset() { }
            public void SetMasterGain(float gain) { }
            public void Pause() { }
            public void Resume() { }
        }

        private sealed class NoOpStrategy : ISonificationStrategy
        {
            public AudioPoint CreateAudioPoint(ChartSeries series, ComponentConfig comp, double val, Ohlcv point,
                int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume, double? prevVal = null)
                => new AudioPoint(comp.BaseFrequency, 1.0f, comp.Waveform, 0.0, "Ping");

            public AudioPoint MapToAudio(ChartSeries series, int dataIndex, List<Ohlcv> data, int relativeIndex,
                int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
                => new AudioPoint(440, 1, "sine", 0, "Sustain");

            public AudioPoint MapComponentToAudio(ChartSeries series, int componentIndex, int dataIndex, List<Ohlcv> data,
                int relativeIndex, int viewportWidth, (double Min, double Max) viewportRange, float chartVolume)
                => new AudioPoint(440, 1, "sine", 0, "Sustain");

            public int ResolveComponentVoiceCount(ComponentConfig comp) => 1;
        }
    }
}
