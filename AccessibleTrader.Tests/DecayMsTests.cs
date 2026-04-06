using System.Collections.Generic;
using Xunit;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tests for <see cref="ComponentConfig.DecayMs"/> default, factory propagation,
    /// and clone preservation.
    /// </summary>
    public class DecayMsTests
    {
        // ── 1. ComponentConfig.DecayMs is null by default ─────────────────────

        [Fact]
        public void ComponentConfig_DefaultDecayMs_IsNull()
        {
            var config = new ComponentConfig();
            Assert.Null(config.DecayMs);
        }

        // ── 2. IndicatorModelFactory propagates DefaultDecayMs from metadata ──

        [Fact]
        public void IndicatorModelFactory_AppliesDefaultDecayMs_FromMetadata()
        {
            var roleMapper      = new ComponentRoleMapper();
            var profileProvider = new SonificationProfileProvider();
            var paneService     = new PaneAssignmentService();
            var stylingService  = new StylingService(roleMapper, profileProvider, paneService);
            var factory         = new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());

            var meta = new IndicatorMetadata
            {
                Code       = "DECAY_TEST",
                Name       = "Decay Test",
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name             = "Signal",
                        DisplayType      = ComponentDisplayType.Dot,
                        Role             = ComponentRole.Signal,
                        DefaultColorHex  = "#00FF00",
                        DefaultDecayMs   = 380,
                    }
                }
            };

            var series = factory.CreateSeriesFromMetadata(
                meta, "Decay Test", "Main",
                new List<(string Name, string Value)>(),
                null);

            var comp = series.Config.Components[0];
            Assert.Equal(380, comp.DecayMs);
        }

        // ── 3. Clone preserves non-null DecayMs ───────────────────────────────

        [Fact]
        public void CloneComponent_PreservesDecayMs_WhenNonNull()
        {
            var original = new ComponentConfig
            {
                Name    = "TestComp",
                DecayMs = 500
            };
            var cloned = original.Clone();
            Assert.Equal(500, cloned.DecayMs);
        }

        // ── 4. Clone preserves null DecayMs ──────────────────────────────────

        [Fact]
        public void CloneComponent_PreservesDecayMs_WhenNull()
        {
            var original = new ComponentConfig
            {
                Name    = "TestComp",
                DecayMs = null
            };
            var cloned = original.Clone();
            Assert.Null(cloned.DecayMs);
        }
    }
}
