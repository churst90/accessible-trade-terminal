using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for cloud fill sonification configuration across MA Cloud, Ichimoku, and Cipher B.
    /// Verifies backward compatibility (null Sonification OK), Clone preservation, and
    /// that each provider's cloud has distinct frequencies.
    /// </summary>
    public class CloudSonificationTests
    {
        // ── 1. Backward compatibility — null Sonification is valid ────────────

        [Fact]
        public void CloudFillConfig_WithNullSonification_IsValid()
        {
            var fill = new CloudFillConfig
            {
                UpperComponentName = "A",
                LowerComponentName = "B",
                BullishColorHex    = "#00FF00",
                BearishColorHex    = "#FF0000",
                Sonification       = null
            };
            Assert.Null(fill.Sonification);
        }

        // ── 2. CloudFillConfig.Clone preserves Sonification ───────────────────

        [Fact]
        public void CloudFillConfig_Clone_PreservesSonification()
        {
            var original = new CloudFillConfig
            {
                UpperComponentName = "FastEma",
                LowerComponentName = "SlowEma",
                DisplayName        = "Test Fill",
                Sonification       = new CloudSonificationConfig(
                    BullishFrequency: 440f,
                    BearishFrequency: 220f,
                    SoundPatchId:     "sine_bell",
                    DecayMs:          200,
                    MaxVolume:        0.75f)
            };

            var cloned = original.Clone();

            Assert.NotNull(cloned.Sonification);
            Assert.Equal(440f, cloned.Sonification!.BullishFrequency);
            Assert.Equal(220f, cloned.Sonification.BearishFrequency);
            Assert.Equal("sine_bell", cloned.Sonification.SoundPatchId);
            Assert.Equal(200, cloned.Sonification.DecayMs);
        }

        // ── 3. MA Cloud fill ──────────────────────────────────────────────────
        // These three were written against EmaFillProvider, an empty subclass kept as a
        // name alias. The alias is gone (2026-08-25); the shipped defaults it was standing
        // in for are MACloudProvider's, so they are asserted directly.

        [Fact]
        public void MACloudProvider_CloudFill_HasNonNullSonification()
        {
            var provider = new MACloudProvider();
            var fill     = provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.NotNull(fill.Sonification);
        }

        [Fact]
        public void MACloudProvider_CloudFill_BullishFrequency_Is440()
        {
            var provider = new MACloudProvider();
            var fill     = provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(440f, fill.Sonification!.BullishFrequency);
        }

        [Fact]
        public void MACloudProvider_CloudFill_BearishFrequency_Is220()
        {
            var provider = new MACloudProvider();
            var fill     = provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(220f, fill.Sonification!.BearishFrequency);
        }

        // ── 4. IchimokuProvider cloud fill — distinct from MA Cloud ───────────

        [Fact]
        public void IchimokuProvider_CloudFill_BullishFrequency_Is520()
        {
            var provider = new IchimokuProvider();
            var fill     = provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.NotNull(fill.Sonification);
            Assert.Equal(520f, fill.Sonification!.BullishFrequency);
        }

        [Fact]
        public void IchimokuProvider_CloudFill_BearishFrequency_Is180()
        {
            var provider = new IchimokuProvider();
            var fill     = provider.GetIndicators()[0].DefaultCloudFills[0];
            Assert.Equal(180f, fill.Sonification!.BearishFrequency);
        }

        // ── 5. CipherBProvider WT Fill ────────────────────────────────────────

        [Fact]
        public void CipherBProvider_WtFill_IsVisualOnly()
        {
            // Policy (see CloudSonificationConfig XML docs): oscillator-to-oscillator
            // cloud fills stay visual-only because both boundaries (WT1 and WT2)
            // already sonify independently — a third cloud voice between them would
            // duplicate information the user already hears.
            var provider  = new CipherBProvider();
            var meta      = provider.GetIndicators()[0];
            var wtFill    = meta.DefaultCloudFills.Single(f => f.DisplayName == "WT Fill");
            Assert.Null(wtFill.Sonification);
        }

        [Fact]
        public void CipherBProvider_AnchorFill_Sonifies()
        {
            // Anchor Fill is a regime-carrying cloud (HTF polarity signal) so it
            // retains its sonification per the cloud-scoping rule.
            var provider   = new CipherBProvider();
            var meta       = provider.GetIndicators()[0];
            var anchorFill = meta.DefaultCloudFills.Single(f => f.DisplayName == "Anchor Fill");
            Assert.NotNull(anchorFill.Sonification);
        }
    }
}
