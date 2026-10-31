using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies key Cipher B metadata: the buy dot's frequency, the parts Market Cipher B does not
    /// draw being absent, and the Background-layer oscillators.
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

        // ── 3. What Market Cipher B does not draw ─────────────────────────────

        /// <summary>
        /// Overbought Crossover (a sell dot), the four divergences and the two anchor waves were
        /// ours, not Market Cipher B's (it draws the buy dot and the small cross dots only); they
        /// were removed on 2026-10-31 (Cody: "remove all of that old code from cipher b").
        /// </summary>
        [Fact]
        public void The_parts_Market_Cipher_B_does_not_draw_are_not_components()
        {
            var names = GetComponents().Select(c => c.Name).ToList();
            foreach (var gone in new[] { "Overbought Crossover", "Bullish Divergence", "Bearish Divergence",
                         "Hidden Bull Continuation", "Hidden Bear Continuation", "Anchor Wave", "Anchor Wave 2",
                         "Anchor Polarity" })
                Assert.DoesNotContain(gone, names);
        }

        // ── 5. WT Histogram — replaces dropped VWAP~ ──────────────────────────

        [Fact]
        public void WtHistogram_HasReferenceLevelZero()
        {
            Assert.Equal(0.0, Get(CipherBProvider.CompWtHistogram).DefaultReferenceLevel);
        }

        /// <summary>
        /// The adaptive OB/OS thresholds are NOT components since 2026-10-17 (Cody's report: hidden
        /// stops that said ±53, which the levels already say), and not computed at all since
        /// 2026-10-31 — CipherBAdaptiveThresholdTests.
        /// </summary>
        [Fact]
        public void AdaptiveObOs_AreNotComponents()
        {
            var names = new CipherBProvider().GetIndicators()[0].Components.Select(c => c.Name).ToList();
            Assert.DoesNotContain(names, n => n.Contains("Adaptive", StringComparison.OrdinalIgnoreCase));
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
