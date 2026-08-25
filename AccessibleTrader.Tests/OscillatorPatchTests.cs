using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cody's 2026-07-16 RSI report: assigning a patch must never flatten the
    /// oscillator into "one sound everywhere". Pins: (1) the OB/OS zone texture
    /// survives a patch on BOTH renderers via the shared PatchLayerNoise rule,
    /// (2) oscillators split their above/below patches at the visible midline —
    /// not by candle direction, which is meaningless for an RSI.
    /// </summary>
    public class OscillatorPatchTests
    {
        // ── The shared layer-noise rule ──────────────────────────────────────

        [Fact]
        public void Layer0_CarriesZoneNoise_WhenPatchIsClean()
        {
            var layer = new OscillatorLayer { NoiseAmount = 0f };
            var (amount, type) = PatchLayerNoise.Merge(0, layer, zoneNoise: 0.45f, zoneNoiseType: "pink");
            Assert.Equal(0.45f, amount);
            Assert.Equal("pink", type);
        }

        [Fact]
        public void Layer0_KeepsPatchNoise_WhenStrongerThanZone()
        {
            var layer = new OscillatorLayer { NoiseAmount = 0.8f, NoiseType = "white" };
            var (amount, type) = PatchLayerNoise.Merge(0, layer, 0.45f, "pink");
            Assert.Equal(0.8f, amount);
            Assert.Equal("white", type);
        }

        [Fact]
        public void UpperLayers_NeverGetZoneNoise_NoDoubling()
        {
            var layer = new OscillatorLayer { NoiseAmount = 0.1f };
            var (amount, _) = PatchLayerNoise.Merge(1, layer, 0.45f, "pink");
            Assert.Equal(0.1f, amount);
        }

        // ── Strategy-level: patched RSI in an OB zone keeps its texture ─────

        private static (ChartSeries series, ComponentConfig comp) RsiSeries(
            string? patchId = null, string? abovePatch = null, string? belowPatch = null)
        {
            var comp = new ComponentConfig
            {
                Name = "Rsi",
                DisplayName = "RSI",
                DisplayType = ComponentDisplayType.Oscillator,
                IsVisible = true,
                Volume = 1f,
                SoundPatchId = patchId,
                BullishSoundPatchId = abovePatch,
                BearishSoundPatchId = belowPatch,
                Waveform = "sine",
                EnvelopeType = "Sustain",
            };
            var config = new SeriesConfig { Id = "rsi-1", IndicatorCode = "RSI", Name = "RSI (14)" };
            config.Components.Add(comp);
            config.Levels.Add(new LevelConfig
            {
                Name = "Overbought", Value = 70, IsVisible = true,
                PlayEarcon = true, ZoneNoiseAmount = 0.45f, ZoneNoiseType = "pink",
            });
            var data = new SeriesDataBuffer { SeriesId = config.Id };
            return (new ChartSeries(config, data) { Volume = 1f }, comp);
        }

        private static DefaultSonificationStrategy Strategy(params Sdk.Models.SoundPatch[] userPatches)
        {
            var registry = new SoundPatchRegistry();
            var library = Substitute.For<ISoundPatchLibrary>();
            foreach (var p in userPatches)
                library.GetPatch(p.Id).Returns(p);
            return new DefaultSonificationStrategy(registry, library);
        }

        private static Ohlcv Bar() => new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

        [Fact]
        public void PatchedOscillator_InOverboughtZone_KeepsZoneTexture()
        {
            var multiLayerPatch = new Sdk.Models.SoundPatch
            {
                Id = "user-organ", Name = "Organ", EnvelopeType = "Sustain",
                Oscillators = new List<OscillatorLayer>
                {
                    new() { Waveform = "sine", Gain = 1f },
                    new() { Waveform = "sine", FreqRatio = 2.0, Gain = 0.4f },
                },
            };
            var (series, comp) = RsiSeries(patchId: "user-organ");
            var strategy = Strategy(multiLayerPatch);

            // RSI at 85 — inside the overbought zone (level 70, texture 0.45).
            var pt = strategy.CreateAudioPoint(series, comp, val: 85, Bar(), 0, 100, (0, 100), 1f);

            Assert.NotNull(pt.PatchLayers);           // the patch renders as layers…
            Assert.True(pt.NoiseAmount >= 0.45f,       // …and the zone cue is still on the point
                $"Zone texture lost under a patch: NoiseAmount {pt.NoiseAmount}.");
        }

        [Fact]
        public void PatchedOscillator_OutsideZones_HasNoTexture()
        {
            var patch = new Sdk.Models.SoundPatch { Id = "user-clean", Name = "Clean" };
            var (series, comp) = RsiSeries(patchId: "user-clean");
            var strategy = Strategy(patch);

            var pt = strategy.CreateAudioPoint(series, comp, val: 50, Bar(), 0, 100, (0, 100), 1f);

            Assert.True(pt.NoiseAmount < 0.05f,
                $"Clean mid-range should have no texture; got {pt.NoiseAmount}.");
        }

        // ── Oscillator above/below-midline patch selection ───────────────────

        [Fact]
        public void Oscillator_PicksAbovePatch_AboveTheMidline()
        {
            var above = new Sdk.Models.SoundPatch { Id = "p-above", Name = "Above", Waveform = "square" };
            var below = new Sdk.Models.SoundPatch { Id = "p-below", Name = "Below", Waveform = "triangle" };
            var (series, comp) = RsiSeries(abovePatch: "p-above", belowPatch: "p-below");
            var strategy = Strategy(above, below);

            // Pane range 0–100: RSI 75 is above the midline (50); the bar itself is
            // bullish (close > open) — irrelevant for oscillators.
            var ptAbove = strategy.CreateAudioPoint(series, comp, 75, Bar(), 0, 100, (0, 100), 1f);
            var ptBelow = strategy.CreateAudioPoint(series, comp, 25, Bar(), 0, 100, (0, 100), 1f);

            // Single-layer user patches surface through the waveform/envelope fields;
            // distinguish via which library patch got resolved: check PatchLayers is null
            // (single layer) and waveform came from the right patch by giving them
            // different waveforms.
            Assert.NotEqual(ptAbove.Waveform, ptBelow.Waveform);
        }

        [Fact]
        public void Oscillator_MidlineSplit_IgnoresCandleDirection()
        {
            var above = new Sdk.Models.SoundPatch { Id = "p-a", Name = "A", Waveform = "square" };
            var below = new Sdk.Models.SoundPatch { Id = "p-b", Name = "B", Waveform = "triangle" };
            var (series, comp) = RsiSeries(abovePatch: "p-a", belowPatch: "p-b");
            var strategy = Strategy(above, below);

            // Bearish candle (close < open) but RSI above midline → still the ABOVE patch.
            var bearishBar = new Ohlcv(DateTime.UtcNow, 110, 111, 99, 100, 1000);
            var pt = strategy.CreateAudioPoint(series, comp, 80, bearishBar, 0, 100, (0, 100), 1f);

            Assert.Equal("square", pt.Waveform);
        }
    }
}
