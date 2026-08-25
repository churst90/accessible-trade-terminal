using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies key Cipher B metadata: Triple Confluence dual-tone bell, crossover
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

        // ── 1. Triple Confluence Buy — dual_tone_bell ─────────────────────────

        [Fact]
        public void TripleConfluenceBuy_HasSoundPatchId_DualToneBell()
        {
            Assert.Equal("dual_tone_bell", Get(CipherBProvider.CompGold).DefaultSoundPatchId);
        }

        [Fact]
        public void TripleConfluenceBuy_HasDecayMs_500()
        {
            Assert.Equal(500, Get(CipherBProvider.CompGold).DefaultDecayMs);
        }

        [Fact]
        public void TripleConfluenceBuy_HasPlaybackLayer_Foreground()
        {
            Assert.Equal(PlaybackLayer.Foreground, Get(CipherBProvider.CompGold).DefaultPlaybackLayer);
        }

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

        [Fact]
        public void AdaptiveObOs_ExistInMetadata()
        {
            Assert.NotNull(Get(CipherBProvider.CompAdaptiveOb));
            Assert.NotNull(Get(CipherBProvider.CompAdaptiveOs));
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

        // ── 7. MF Wave — no sub-pane, Background audio ────────────────────────

        [Fact]
        public void MoneyFlowWave_HasNoSubPane()
        {
            Assert.Null(Get(CipherBProvider.CompMoneyFlowWave).SubPaneName);
        }

        [Fact]
        public void MoneyFlowWave_HasBackgroundPlaybackLayer()
        {
            Assert.Equal(PlaybackLayer.Background, Get(CipherBProvider.CompMoneyFlowWave).DefaultPlaybackLayer);
        }
    }
}
