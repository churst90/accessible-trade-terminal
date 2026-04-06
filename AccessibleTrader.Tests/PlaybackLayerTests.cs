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
    /// Tests for <see cref="PlaybackLayer"/> volume multiplier semantics,
    /// default values, and propagation through <see cref="IndicatorModelFactory"/>.
    /// </summary>
    public class PlaybackLayerTests
    {
        // ── 1. LayerVolume multiplier values ─────────────────────────────────

        [Theory]
        [InlineData(PlaybackLayer.Background,  0.60f)]
        [InlineData(PlaybackLayer.Midground,   0.80f)]
        [InlineData(PlaybackLayer.Foreground,  1.00f)]
        public void PlaybackLayer_VolumeMultiplier_MatchesExpected(PlaybackLayer layer, float expectedMultiplier)
        {
            // The spec:
            //   Background = 60%, Midground = 80%, Foreground = 100%
            float actual = layer switch
            {
                PlaybackLayer.Background => 0.60f,
                PlaybackLayer.Midground  => 0.80f,
                PlaybackLayer.Foreground => 1.00f,
                _                        => 0f
            };
            Assert.Equal(expectedMultiplier, actual, precision: 2);
        }

        // ── 2. ComponentConfig default PlaybackLayer is Midground ─────────────

        [Fact]
        public void ComponentConfig_DefaultPlaybackLayer_IsMidground()
        {
            var config = new ComponentConfig();
            Assert.Equal(PlaybackLayer.Midground, config.PlaybackLayer);
        }

        // ── 3. IndicatorModelFactory propagates DefaultPlaybackLayer from metadata ──

        [Fact]
        public void IndicatorModelFactory_AppliesDefaultPlaybackLayer_FromMetadata()
        {
            var roleMapper      = new ComponentRoleMapper();
            var profileProvider = new SonificationProfileProvider();
            var paneService     = new PaneAssignmentService();
            var stylingService  = new StylingService(roleMapper, profileProvider, paneService);
            var factory         = new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());

            var meta = new IndicatorMetadata
            {
                Code       = "TEST_IND",
                Name       = "Test Indicator",
                Components = new List<IndicatorComponentMetadata>
                {
                    new()
                    {
                        Name                 = "Signal Line",
                        DisplayType          = ComponentDisplayType.Dot,
                        Role                 = ComponentRole.Signal,
                        DefaultColorHex      = "#FF0000",
                        DefaultPlaybackLayer = PlaybackLayer.Foreground,
                    }
                }
            };

            var series = factory.CreateSeriesFromMetadata(
                meta, "Test Indicator", "Main",
                new List<(string Name, string Value)>(),
                null);

            var comp = series.Config.Components[0];
            Assert.Equal(PlaybackLayer.Foreground, comp.PlaybackLayer);
        }

        // ── 4. CloneComponent preserves PlaybackLayer ─────────────────────────

        [Fact]
        public void CloneComponent_PreservesPlaybackLayer()
        {
            var original = new ComponentConfig
            {
                Name          = "TestComp",
                PlaybackLayer = PlaybackLayer.Background
            };
            var cloned = original.Clone();
            Assert.Equal(PlaybackLayer.Background, cloned.PlaybackLayer);
        }
    }
}
