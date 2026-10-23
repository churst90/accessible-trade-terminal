using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies key Cipher B metadata: crossover
    /// frequencies, divergence patches, and Background-layer anchors/oscillators.
    /// </summary>
    public class CipherBMetadataTests
    {
        private static List<IndicatorComponentMetadata> GetComponents()
        {
            var provider = new CipherBProvider();
            return provider.GetIndicators()[0].Components;
        }

        private static IndicatorComponentMetadata Get(string name) =>
            GetComponents().Single(c => c.Name == name);

        // (1. The gold dot's bell went with the dot, 2026-10-22; GoldDot.Component keeps it —
        // CipherBGoldDotTests.The_kept_part_looks_and_sounds_as_the_dot_did.)

        // ── 2. Crossover frequencies ──────────────────────────────────────────

        [Fact]
        public void OversoldCrossover_HasBaseFrequency_840()
        {
            Assert.Equal(840.0, Get(CipherBProvider.CompBlue).DefaultBaseFrequency);
        }

        [Fact]
        public void OverboughtCrossover_HasBaseFrequency_210()
        {
            Assert.Equal(210.0, Get(CipherBProvider.CompRed).DefaultBaseFrequency);
        }

        // ── 3. Divergence dot patches ─────────────────────────────────────────

        [Fact]
        public void BullishDivergence_HasSoundPatchId_TriangleBell()
        {
            Assert.Equal("triangle_bell", Get(CipherBProvider.CompBullDiv).DefaultSoundPatchId);
        }

        [Fact]
        public void BearishDivergence_HasBaseFrequency_310()
        {
            Assert.Equal(310.0, Get(CipherBProvider.CompBearDiv).DefaultBaseFrequency);
        }

        // ── 4. Anchor waves — Background layer ───────────────────────────────

        [Fact]
        public void WT1Anchor_HasPlaybackLayer_Background()
        {
            Assert.Equal(PlaybackLayer.Background, Get(CipherBProvider.CompWT1Anchor).DefaultPlaybackLayer);
        }

        [Fact]
        public void WT2Anchor_HasPlaybackLayer_Background()
        {
            Assert.Equal(PlaybackLayer.Background, Get(CipherBProvider.CompWT2Anchor).DefaultPlaybackLayer);
        }

        // ── 5. WT Histogram — replaces dropped VWAP~ ──────────────────────────

        [Fact]
        public void WtHistogram_HasReferenceLevelZero()
        {
            Assert.Equal(0.0, Get(CipherBProvider.CompWtHistogram).DefaultReferenceLevel);
        }

        /// <summary>
        /// The adaptive OB/OS thresholds are NOT components since 2026-10-17 (Cody's report: hidden
        /// stops that said ±53, which the levels already say). The provider still writes them, as
        /// companion arrays — CipherBAdaptiveThresholdTests.
        /// </summary>
        [Fact]
        public void AdaptiveObOs_AreNotComponents()
        {
            var names = new CipherBProvider().GetIndicators()[0].Components.Select(c => c.Name).ToList();
            Assert.DoesNotContain(names, n => n.Contains("Adaptive", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(CipherBProvider.ThresholdObKey, names);
            Assert.DoesNotContain(CipherBProvider.ThresholdOsKey, names);
        }

        // ── 6. WaveTrend cross dots — MCB-accurate small dots ─────────────────

        [Fact]
        public void CrossBull_ExistsInMetadata()
        {
            Assert.NotNull(Get(CipherBProvider.CompCrossBull));
        }

        [Fact]
        public void CrossBear_ExistsInMetadata()
        {
            Assert.NotNull(Get(CipherBProvider.CompCrossBear));
        }

        [Fact]
        public void CrossBull_IsSmall_SmallerThanOversoldCrossover()
        {
            float crossSize = Get(CipherBProvider.CompCrossBull).DefaultThickness ?? 0f;
            float blueSize  = Get(CipherBProvider.CompBlue).DefaultThickness ?? 0f;
            Assert.True(crossSize < blueSize, "All-cross dot must be smaller than oversold large circle");
        }

        [Fact]
        public void CrossBull_HasForegroundPlaybackLayer()
        {
            Assert.Equal(PlaybackLayer.Foreground, Get(CipherBProvider.CompCrossBull).DefaultPlaybackLayer);
        }

        // ── 7. MF Wave — Background audio ─────────────────────────────────────

        [Fact]
        public void MoneyFlowWave_HasBackgroundPlaybackLayer()
        {
            Assert.Equal(PlaybackLayer.Background, Get(CipherBProvider.CompMoneyFlowWave).DefaultPlaybackLayer);
        }
    }
}
