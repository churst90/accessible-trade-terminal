using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// The volume bed and the candle body were ONE PITCH.
///
/// <para>
/// Volume's profile says <c>PitchMapping.PriceDirection</c>, and that mapping plays the
/// component's Bullish/Bearish pair, never its BaseFrequency. The Volume component declared no
/// pair, so the factory gave it the default — 440/220, the body's — and on every bar the two
/// voices landed on the same note. The profile's 330 Hz was written up as "not what you hear"
/// and left there. <c>SonificationTimbreTests</c> was green throughout, because its helper
/// hard-coded 440/220 onto BOTH components and compared everything except Frequency.
/// </para>
///
/// <para>
/// These tests go through the real chain — metadata → factory → strategy — so the assertion is
/// about what a user hears, not about a number a helper chose.
/// </para>
/// </summary>
public sealed class VolumePitchTests
{
    private static IndicatorModelFactory Factory()
    {
        var roleMapper      = new ComponentRoleMapper();
        var profileProvider = new SonificationProfileProvider();
        var paneService     = new PaneAssignmentService();
        var stylingService  = new StylingService(roleMapper, profileProvider, paneService);
        return new IndicatorModelFactory(stylingService, new MockIndicatorPreferencesService());
    }

    private static IndicatorMetadata RealMeta(string code) =>
        IndicatorProviderFixture.AllProviders()
            .SelectMany(p => p.GetIndicators())
            .First(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));

    private static (ChartSeries Series, ComponentConfig Comp) Built(string code, ComponentRole role)
    {
        var meta = RealMeta(code);
        var series = Factory().CreateSeriesFromMetadata(meta, meta.Name, meta.DefaultPane ?? "Main",
            new List<(string Name, string Value)>(), null);
        return (series, series.Config.Components.First(c => c.Role == role));
    }

    private static AudioPoint Play(ChartSeries series, ComponentConfig comp, Ohlcv bar, double val,
        (double Min, double Max) range)
        => new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(series, comp, val, bar, relativeIndex: 5, viewportWidth: 20,
                viewportRange: range, chartVolume: 1f);

    private static Ohlcv Up   => new(default, 50, 55, 45, 54, 1000);
    private static Ohlcv Down => new(default, 54, 55, 45, 50, 1000);

    /// <summary>The declaration itself: Volume's pair is its own, and sits below the body's.</summary>
    [Fact]
    public void VolumeDeclaresItsOwnDirectionalPair_BelowTheBodys()
    {
        var (_, body) = Built("CANDLES", ComponentRole.Body);
        var (_, vol)  = Built("VOLUME", ComponentRole.Volume);

        Assert.Equal(PitchMapping.PriceDirection, vol.PitchMapping);
        Assert.NotEqual(body.BullishFrequency, vol.BullishFrequency);
        Assert.NotEqual(body.BearishFrequency, vol.BearishFrequency);
        Assert.True(vol.BullishFrequency < body.BullishFrequency, "the bed sits UNDER the body");
        Assert.True(vol.BearishFrequency < body.BearishFrequency, "the bed sits UNDER the body");
    }

    /// <summary>
    /// What is heard: on an up bar and on a down bar, the bed and the body are two notes, and
    /// neither of the bed's notes is a note the body or a wick can play (440, 220, 880).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheVolumeBedAndTheCandleBodyAreTwoPitchesOnEveryBar(bool bullish)
    {
        var bar = bullish ? Up : Down;
        var (candles, body) = Built("CANDLES", ComponentRole.Body);
        var (volume, vol)   = Built("VOLUME", ComponentRole.Volume);

        var b = Play(candles, body, bar, bar.Close, (40, 60));
        var v = Play(volume, vol, bar, bar.Volume, (0, 2000));

        Assert.NotEqual(b.Frequency, v.Frequency);
        Assert.True(v.Frequency < b.Frequency, $"bed {v.Frequency} should sit under body {b.Frequency}");
        Assert.DoesNotContain(v.Frequency, new[] { 440.0, 220.0, 880.0 });
    }

    /// <summary>
    /// A resumed session restores core series AS SAVED, so a workspace written before the pair
    /// was declared carries Volume on 440/220 — the collision, preserved by the restore. The pair
    /// is derived from metadata on migration, the same rule as the pane.
    /// </summary>
    [Fact]
    public void MigrateSeriesConfig_RederivesTheDirectionalPair_FromMetadata()
    {
        var saved = new SeriesConfig { Id = "volume", IndicatorCode = "VOLUME", Pane = "Volume" };
        saved.Components.Add(new ComponentConfig
        {
            Name = "Volume", Role = ComponentRole.Volume, DisplayType = ComponentDisplayType.Bar,
            PitchMapping = PitchMapping.PriceDirection, BullishFrequency = 440.0, BearishFrequency = 220.0,
        });

        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata> { RealMeta("VOLUME") });

        var declared = RealMeta("VOLUME").Components.Single();
        Assert.Equal(declared.DefaultBullishFrequency!.Value, saved.Components[0].BullishFrequency);
        Assert.Equal(declared.DefaultBearishFrequency!.Value, saved.Components[0].BearishFrequency);
    }

    /// <summary>A component whose metadata declares no pair keeps what it saved.</summary>
    [Fact]
    public void MigrateSeriesConfig_LeavesAnUndeclaredPairAlone()
    {
        var saved = new SeriesConfig { Id = "candles", IndicatorCode = "CANDLES", Pane = "Main" };
        saved.Components.Add(new ComponentConfig
        {
            Name = "body", Role = ComponentRole.Body, DisplayType = ComponentDisplayType.Candle,
            BullishFrequency = 494.0, BearishFrequency = 247.0,
        });

        WorkspaceInitializer.MigrateSeriesConfig(saved, new List<IndicatorMetadata> { RealMeta("CANDLES") });

        Assert.Equal(494.0, saved.Components[0].BullishFrequency);
        Assert.Equal(247.0, saved.Components[0].BearishFrequency);
    }
}
