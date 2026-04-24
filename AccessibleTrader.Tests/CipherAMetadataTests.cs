using System.Collections.Generic;
using System.Linq;
using Xunit;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies Cipher A self-describing metadata: component count, audio patch IDs,
    /// frequencies, decay values, playback layers, and gradient speech flags.
    /// </summary>
    public class CipherAMetadataTests
    {
        private static List<IndicatorComponentMetadata> GetComponents()
        {
            var provider = new CipherAProvider();
            return provider.GetIndicators()[0].Components;
        }

        private static IndicatorComponentMetadata Get(string name) =>
            GetComponents().Single(c => c.Name == name);

        // ── 1. Component count ────────────────────────────────────────────────

        [Fact]
        public void CipherA_GetMetadata_Returns9Components()
        {
            // 8 visible + 1 hidden queryable gradient (WT Momentum Gradient). The gradient
            // is not navigable on chart (IsVisible=false) but is surfaced via SignalCatalog
            // so strategies can gate on momentum strength.
            Assert.Equal(9, GetComponents().Count);
        }

        [Fact]
        public void CipherA_WtMomentumGradient_IsHiddenQueryableComponent()
        {
            var grad = Get(CipherAProvider.CompWtMomentumGradient);
            Assert.False(grad.IsVisible);
            Assert.Equal(ComponentDisplayType.Line, grad.DisplayType);
        }

        // ── 2. Buy Signal audio ───────────────────────────────────────────────

        [Fact]
        public void BuySignal_HasSoundPatchId_SineBell()
        {
            Assert.Equal("sine_bell", Get(CipherAProvider.CompBuySignal).DefaultSoundPatchId);
        }

        [Fact]
        public void BuySignal_HasBaseFrequency_880()
        {
            Assert.Equal(880.0, Get(CipherAProvider.CompBuySignal).DefaultBaseFrequency);
        }

        [Fact]
        public void BuySignal_HasDecayMs_380()
        {
            Assert.Equal(380, Get(CipherAProvider.CompBuySignal).DefaultDecayMs);
        }

        [Fact]
        public void BuySignal_HasPlaybackLayer_Foreground()
        {
            Assert.Equal(PlaybackLayer.Foreground, Get(CipherAProvider.CompBuySignal).DefaultPlaybackLayer);
        }

        // ── 3. Sell Signal base frequency ────────────────────────────────────

        [Fact]
        public void SellSignal_HasBaseFrequency_220()
        {
            Assert.Equal(220.0, Get(CipherAProvider.CompSellSignal).DefaultBaseFrequency);
        }

        // ── 4. Blood Diamond (Overbought Bearish Divergence) ──────────────────

        [Fact]
        public void BloodDiamond_HasDecayMs_500()
        {
            Assert.Equal(500, Get(CipherAProvider.CompBloodDiamond).DefaultDecayMs);
        }

        [Fact]
        public void BloodDiamond_HasBaseFrequency_165()
        {
            Assert.Equal(165.0, Get(CipherAProvider.CompBloodDiamond).DefaultBaseFrequency);
        }

        // ── 5. Manipulation / Exhaustion use detuned_pair_bell ────────────────

        [Fact]
        public void Manipulation_HasSoundPatchId_DetunedPairBell()
        {
            Assert.Equal("detuned_pair_bell", Get(CipherAProvider.CompManipulation).DefaultSoundPatchId);
        }

        [Fact]
        public void Exhaustion_HasSoundPatchId_DetunedPairBell()
        {
            Assert.Equal("detuned_pair_bell", Get(CipherAProvider.CompExhaustion).DefaultSoundPatchId);
        }

        // ── 6. WT Momentum dot gradient speech and layer ──────────────────────

        [Fact]
        public void WtMomentum_HasUsesGradientSpeech_True()
        {
            Assert.True(Get(CipherAProvider.CompWtMomentum).UsesGradientSpeech);
        }

        [Fact]
        public void WtMomentum_HasPlaybackLayer_Background()
        {
            Assert.Equal(PlaybackLayer.Background, Get(CipherAProvider.CompWtMomentum).DefaultPlaybackLayer);
        }

        // ── 7. All 8 components have non-null DefaultColorHex ─────────────────

        [Fact]
        public void AllComponents_HaveNonNullDefaultColorHex()
        {
            foreach (var comp in GetComponents())
            {
                Assert.False(string.IsNullOrEmpty(comp.DefaultColorHex),
                    $"Component '{comp.Name}' must have a non-null DefaultColorHex.");
            }
        }
    }
}
