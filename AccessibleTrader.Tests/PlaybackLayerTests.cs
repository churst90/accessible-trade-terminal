using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="PlaybackLayer"/> volume multiplier semantics,
    /// default values, and propagation through <see cref="IndicatorModelFactory"/>.
    /// </summary>
    public class PlaybackLayerTests
    {
        // ── 1. LayerVolume multiplier values, AS THE SEQUENCER APPLIES THEM ──

        /// <summary>
        /// <b>This used to assert its own copy of the switch.</b>
        ///
        /// <para>
        /// The previous version of this test re-implemented
        /// <c>AudioSequencer.LayerVolume</c> inside the test body and compared that to the
        /// <c>InlineData</c> — two hand-written copies of the same table, neither of them
        /// production code. It passed whatever the sequencer did, and the A2g mutant set proved
        /// it: flattening Background from 0.60 to 1.00 in <c>AudioSequencer</c> broke nothing.
        /// That is the A2 pathology "tests that mirror production logic", in its purest form.
        /// </para>
        ///
        /// <para>
        /// <c>LayerVolume</c> is private, and rightly so, so the contract is asserted where it is
        /// actually observable: the volume the sequencer hands the audio driver for one bar of
        /// real playback. That also makes the test about the thing the user experiences — what
        /// sits behind what during a full-chart playback — rather than about a constant.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(PlaybackLayer.Background,  0.60f)]
        [InlineData(PlaybackLayer.Midground,   0.80f)]
        [InlineData(PlaybackLayer.Foreground,  1.00f)]
        public async Task ThePlaybackLayerScalesTheVolumeTheSequencerArms(PlaybackLayer layer, float expectedMultiplier)
        {
            float atThisLayer  = await PlaybackVolumeForLayerAsync(layer);
            float atForeground = await PlaybackVolumeForLayerAsync(PlaybackLayer.Foreground);

            Assert.True(atForeground > 0f, "the Foreground reference was silent — this case proves nothing");
            Assert.Equal(expectedMultiplier, atThisLayer / atForeground, precision: 2);
        }

        /// <summary>
        /// The three layers must be three DISTINCT levels. Asserted separately from the ratios
        /// above because a table that collapsed two of them to the same number would still
        /// satisfy a per-layer ratio check if the expectations collapsed with it.
        /// </summary>
        [Fact]
        public async Task TheThreeLayersAreThreeDistinctLevels()
        {
            float back = await PlaybackVolumeForLayerAsync(PlaybackLayer.Background);
            float mid  = await PlaybackVolumeForLayerAsync(PlaybackLayer.Midground);
            float fore = await PlaybackVolumeForLayerAsync(PlaybackLayer.Foreground);

            Assert.True(back < mid && mid < fore,
                $"the layers did not sit behind one another: Background {back:F4}, Midground {mid:F4}, Foreground {fore:F4}");
        }

        // ── The harness for the two tests above ──────────────────────────────

        private sealed class VolumeSpyDriver : IAudioDriver
        {
            public List<(int Slot, float Volume)> Calls { get; } = new();
            public int SampleRate => 44100;
            public int Channels => 2;
            public event Action<int>? PointReached { add { } remove { } }
            public void SetVoice(int slot, double frequency, float volume, float pan, string waveform,
                bool continuous, double durationSeconds = 0.2, int dataIndex = -1, string envelope = "Sustain",
                bool click = false, float noiseAmount = 0f, string noiseType = "pink", float squareMix = 0f,
                float sawMix = 0f, float triangleMix = 0f, float subSawMix = 0f)
            {
                lock (Calls) Calls.Add((slot, volume));
            }
            public void StopVoice(int slot) { }
            public void StopAll() { }
            public void Reset() { }
            public void SetMasterGain(float gain) { }
            public void Pause() { }
            public void Resume() { }
        }

        /// <summary>Plays one bar of a one-component series at the given layer and returns the
        /// loudest playback voice (slots 32-95) the sequencer armed.</summary>
        private static async Task<float> PlaybackVolumeForLayerAsync(PlaybackLayer layer)
        {
            const int BarCount = 20;

            var comp = new ComponentConfig
            {
                Name = "line", DisplayName = "line", DisplayType = ComponentDisplayType.Line,
                Role = ComponentRole.PriceAction, DataMapping = "close",
                IsVisible = true, IsEnabled = true, Volume = 1f, Waveform = "sine",
                AmplitudeMapping = AmplitudeMapping.None, PitchMapping = PitchMapping.Value,
                BaseFrequency = 440, FreqMultiplier = 1.0, EnvelopeType = "Sustain",
                PlaybackLayer = layer,
            };
            var cfg = new SeriesConfig { Id = "s", Name = "s", Pane = "Main", IsVisible = true, Volume = 1f };
            cfg.Components.Add(comp);

            var data = new SeriesDataBuffer { SeriesId = "s" };
            var values = new double[BarCount];
            for (int i = 0; i < BarCount; i++) values[i] = 100;   // flat, so every bar is identical
            data.ComponentData["line"] = values;
            var series = new ChartSeries(cfg, data);

            var bars = new List<Ohlcv>(BarCount);
            var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < BarCount; i++) bars.Add(new Ohlcv(t.AddHours(i), 100, 102, 98, 101, 1000));

            var store = new MockWorkspaceStore();
            store.EmitState(WorkspaceState.Initial with
            {
                ActiveSeries = ImmutableList.Create(series),
                ViewportStartIndex = 0,
                ViewportLength = BarCount,
                ViewportRange = (90, 110),
                PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Main", (90, 110)),
                ChartVolume = 1f,
                PlaybackSpeed = 1.0f,
            });

            var driver = new VolumeSpyDriver();
            var sequencer = new AudioSequencer(driver, new DefaultSonificationStrategy(new SoundPatchRegistry()),
                store, new SoundPatchRegistry(), NullLogger<AudioSequencer>.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await sequencer.StartPlaybackAsync(series, bars, BarCount - 1, cts.Token);

            var playbackVoices = driver.Calls.Where(c => c.Slot is >= 32 and <= 95).ToList();
            return playbackVoices.Count == 0 ? 0f : playbackVoices.Max(c => c.Volume);
        }

        // ── 2. ComponentConfig default PlaybackLayer is Midground ─────────────

        [Fact]
        public void ComponentConfig_DefaultPlaybackLayer_IsMidground()
        {
            var config = new ComponentConfig();
            Assert.Equal(PlaybackLayer.Midground, config.PlaybackLayer);
        }

        // ── 3. IndicatorModelFactory propagates DefaultPlaybackLayer from metadata ──

        [Fact]
        public void IndicatorModelFactory_AppliesDefaultPlaybackLayer_FromMetadata()
        {
            var roleMapper      = new ComponentRoleMapper();
            var profileProvider = new SonificationProfileProvider();
            var paneService     = new PaneAssignmentService();
            var stylingService  = new StylingService(roleMapper, profileProvider, paneService);
            var factory         = new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());

            var meta = new IndicatorMetadata
            {
                Code       = "TEST_IND",
                Name       = "Test Indicator",
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name                 = "Signal Line",
                        DisplayType          = ComponentDisplayType.Dot,
                        Role                 = ComponentRole.Signal,
                        DefaultColorHex      = "#FF0000",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    }
                }
            };

            var series = factory.CreateSeriesFromMetadata(
                meta, "Test Indicator", "Main",
                new List<(string Name, string Value)>(),
                null);

            var comp = series.Config.Components[0];
            Assert.Equal(PlaybackLayer.Foreground, comp.PlaybackLayer);
        }

        // ── 4. CloneComponent preserves PlaybackLayer ─────────────────────────

        [Fact]
        public void CloneComponent_PreservesPlaybackLayer()
        {
            var original = new ComponentConfig
            {
                Name          = "TestComp",
                PlaybackLayer = PlaybackLayer.Background
            };
            var cloned = original.Clone();
            Assert.Equal(PlaybackLayer.Background, cloned.PlaybackLayer);
        }
    }
}
