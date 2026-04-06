using System.Collections.Generic;
using System.Linq;
using Xunit;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Verifies Cipher SR component metadata (crystal bell, frequencies, zone line flags)
    /// and confirms that IndicatorModelFactory propagates IsZoneLine to ComponentConfig.
    /// </summary>
    public class CipherSrMetadataTests
    {
        private static List<IndicatorComponentMetadata> GetComponents()
        {
            var provider = new CipherSrProvider();
            return provider.GetIndicators()[0].Components;
        }

        private static IndicatorComponentMetadata Get(string name) =>
            GetComponents().Single(c => c.Name == name);

        // ── 1. Component count ────────────────────────────────────────────────

        [Fact]
        public void CipherSr_GetMetadata_Returns4Components()
        {
            Assert.Equal(4, GetComponents().Count);
        }

        // ── 2. Resistance dot — crystal_bell, 700 Hz ─────────────────────────

        [Fact]
        public void Resistance_HasSoundPatchId_CrystalBell()
        {
            Assert.Equal("crystal_bell", Get(CipherSrProvider.CompResistance).DefaultSoundPatchId);
        }

        [Fact]
        public void Resistance_HasBaseFrequency_700()
        {
            Assert.Equal(700.0, Get(CipherSrProvider.CompResistance).DefaultBaseFrequency);
        }

        // ── 3. Support dot — 330 Hz ───────────────────────────────────────────

        [Fact]
        public void Support_HasBaseFrequency_330()
        {
            Assert.Equal(330.0, Get(CipherSrProvider.CompSupport).DefaultBaseFrequency);
        }

        // ── 4. Zone line flags ────────────────────────────────────────────────

        [Fact]
        public void ResistanceLine_HasDefaultIsZoneLine_True()
        {
            Assert.True(Get(CipherSrProvider.CompResistanceLine).DefaultIsZoneLine);
        }

        [Fact]
        public void SupportLine_HasDefaultIsZoneLine_True()
        {
            Assert.True(Get(CipherSrProvider.CompSupportLine).DefaultIsZoneLine);
        }

        [Fact]
        public void Resistance_Dot_HasDefaultIsZoneLine_False()
        {
            Assert.False(Get(CipherSrProvider.CompResistance).DefaultIsZoneLine);
        }

        // ── 5. IndicatorModelFactory propagates IsZoneLine ────────────────────

        [Fact]
        public void IndicatorModelFactory_PropagatesIsZoneLine_ToComponentConfig()
        {
            var roleMapper      = new ComponentRoleMapper();
            var profileProvider = new SonificationProfileProvider();
            var paneService     = new PaneAssignmentService();
            var stylingService  = new StylingService(roleMapper, profileProvider, paneService);
            var factory         = new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());

            var provider  = new CipherSrProvider();
            var meta      = provider.GetIndicators()[0];

            var series = factory.CreateSeriesFromMetadata(
                meta, "Accessible Cipher SR", "Main",
                new List<(string Name, string Value)>(),
                null);

            var resistanceLine = series.Config.Components
                .Single(c => c.Name == CipherSrProvider.CompResistanceLine);
            var resistanceDot  = series.Config.Components
                .Single(c => c.Name == CipherSrProvider.CompResistance);

            Assert.True(resistanceLine.IsZoneLine,
                "Resistance Zone component must have IsZoneLine=true after factory creates config.");
            Assert.False(resistanceDot.IsZoneLine,
                "Resistance dot component must have IsZoneLine=false.");
        }
    }
}
