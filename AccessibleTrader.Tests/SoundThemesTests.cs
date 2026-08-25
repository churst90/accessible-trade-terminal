using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Sound themes: per-indicator-family instrument voices. Pins the two contracts
    /// that matter — semantic timbres (candles/wicks/volume/histograms, whose grit
    /// encodes size) are NEVER themed, and every patch id a theme hands out actually
    /// exists in the factory bank.
    /// </summary>
    public class SoundThemesTests
    {
        [Fact]
        public void Classic_NeverAssignsAPatch()
        {
            foreach (ComponentDisplayType t in System.Enum.GetValues(typeof(ComponentDisplayType)))
                foreach (ComponentRole r in System.Enum.GetValues(typeof(ComponentRole)))
                    Assert.Null(SoundThemes.ResolvePatchId(SoundThemes.ClassicId, t, r));
        }

        [Fact]
        public void NullOrEmptyTheme_MeansClassic()
        {
            Assert.Null(SoundThemes.ResolvePatchId(null, ComponentDisplayType.Oscillator, ComponentRole.None));
            Assert.Null(SoundThemes.ResolvePatchId("", ComponentDisplayType.Oscillator, ComponentRole.None));
        }

        [Theory]
        [InlineData(ComponentDisplayType.Candle, ComponentRole.Body)]
        [InlineData(ComponentDisplayType.Wick, ComponentRole.Wick)]
        [InlineData(ComponentDisplayType.Bar, ComponentRole.Volume)]
        [InlineData(ComponentDisplayType.Histogram, ComponentRole.Histogram)]
        public void SemanticComponents_AreNeverThemed(ComponentDisplayType type, ComponentRole role)
        {
            // Grit encodes size on these — a fixed patch would erase the encoding.
            foreach (var theme in SoundThemes.All)
                Assert.Null(SoundThemes.ResolvePatchId(theme.Id, type, role));
        }

        [Fact]
        public void Orchestra_GivesEachFamilyADistinctVoice()
        {
            string? line = SoundThemes.ResolvePatchId("orchestra", ComponentDisplayType.Line, ComponentRole.None);
            string? osc  = SoundThemes.ResolvePatchId("orchestra", ComponentDisplayType.Oscillator, ComponentRole.None);
            string? zero = SoundThemes.ResolvePatchId("orchestra", ComponentDisplayType.ZeroArea, ComponentRole.None);
            string? band = SoundThemes.ResolvePatchId("orchestra", ComponentDisplayType.Line, ComponentRole.UpperBand);

            Assert.NotNull(line);
            Assert.NotNull(osc);
            Assert.NotNull(zero);
            Assert.NotNull(band);
            // The whole point: four families, four different instruments.
            Assert.Equal(4, new[] { line, osc, zero, band }.Distinct().Count());
        }

        [Fact]
        public void EveryThemedPatchId_ExistsInTheFactoryBank()
        {
            foreach (var theme in SoundThemes.All)
                foreach (ComponentDisplayType t in System.Enum.GetValues(typeof(ComponentDisplayType)))
                    foreach (ComponentRole r in System.Enum.GetValues(typeof(ComponentRole)))
                    {
                        var id = SoundThemes.ResolvePatchId(theme.Id, t, r);
                        if (id != null)
                            Assert.True(SoundThemes.FactoryPatches.ContainsKey(id),
                                $"Theme '{theme.Id}' assigns unknown patch '{id}' for {t}/{r}.");
                    }
        }

        [Fact]
        public void FactoryPatches_AllHaveLayersAndStableVoiceIds()
        {
            Assert.NotEmpty(SoundThemes.FactoryPatches);
            foreach (var (id, patch) in SoundThemes.FactoryPatches)
            {
                Assert.StartsWith("voice_", id);
                Assert.Equal(id, patch.Id);
                Assert.NotEmpty(patch.EffectiveLayers());
                // Layer gains must stay bounded so additive stacks can't clip badly.
                Assert.All(patch.EffectiveLayers(), l => Assert.InRange(l.Gain, 0f, 1f));
            }
        }
    }
}
