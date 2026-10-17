using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cipher SR's components since the Market Cipher SR rebuild (2026-10-17): the two held levels
    /// (zone lines, drawn as runs of dots), the session range and VWAP (muted by default), and the
    /// factory carrying the zone-line flag through to the component config.
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

        [Fact]
        public void CipherSr_DeclaresMarketCipherSrsSixParts_ZoneLinesFirst()
        {
            Assert.Equal(new[]
            {
                CipherSrProvider.CompResistanceLine, CipherSrProvider.CompSupportLine,
                CipherSrProvider.CompRangeHigh, CipherSrProvider.CompRangeLow, CipherSrProvider.CompRangeMid,
                CipherSrProvider.CompVwap,
            }, GetComponents().Select(c => c.Name));
        }

        [Fact]
        public void TheHeldLevelsAreZoneLinesDrawnAsDots_InMarketCiphersColours()
        {
            foreach (var (name, colour) in new[] { (CipherSrProvider.CompResistanceLine, "#E040FB"), (CipherSrProvider.CompSupportLine, "#FFEB3B") })
            {
                var c = Get(name);
                Assert.True(c.DefaultIsZoneLine);
                Assert.Equal(ComponentDisplayType.Line, c.DisplayType);   // a Dot would be a marker to the narrator
                Assert.Equal(DashStyle.Dot, c.DefaultDashStyle);
                Assert.Equal(colour, c.DefaultColorHex);
            }
        }

        [Fact]
        public void TheRangeAndVwapAreMutedAndAreNotZoneLines()
        {
            foreach (var name in new[] { CipherSrProvider.CompRangeHigh, CipherSrProvider.CompRangeLow, CipherSrProvider.CompRangeMid, CipherSrProvider.CompVwap })
            {
                Assert.True(Get(name).DefaultIsMuted, $"{name} should be muted by default");
                Assert.False(Get(name).DefaultIsZoneLine, $"{name} is not a support/resistance level");
            }
        }

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

            Assert.True(series.Config.Components.Single(c => c.Name == CipherSrProvider.CompResistanceLine).IsZoneLine,
                "Resistance Zone component must have IsZoneLine=true after factory creates config.");
            Assert.False(series.Config.Components.Single(c => c.Name == CipherSrProvider.CompVwap).IsZoneLine,
                "VWAP must have IsZoneLine=false.");
            Assert.True(series.Config.Components.Single(c => c.Name == CipherSrProvider.CompVwap).IsMuted,
                "VWAP must come up muted.");
        }
    }
}
