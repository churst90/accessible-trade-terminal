using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's RSI and Stochastic are textured at THEIR extremes, 80 and 20.</b>
///
/// <para>Cody, 2026-10-17: "the rsi/stoch though doesn't have the texturing on the os/ob zones … shouldn't
/// the os/ob zones be -53 and 53?" They had none: both subscribed to no level. And ±53 is the waves'
/// scale — the two lines run 0–100 on Cipher B's −100…+100 pane (as Market Cipher draws them), so +53
/// is just above their middle and −53 is never reached. Cody chose 80 / 20, sound only: the
/// component's own zone (ComponentConfig.ZoneOverbought / ZoneOversold), nothing drawn.</para>
///
/// <para>Built as the app builds it: the provider's metadata through <see cref="IndicatorModelFactory"/>,
/// the provider's levels with their noise and earcons (as SeriesManagementService injects them), and
/// the real <see cref="DefaultSonificationStrategy"/>.</para>
/// </summary>
public sealed class CipherBStochasticZonesTests
{
    private static readonly Ohlcv Bar = new(DateTime.UtcNow, 100, 110, 95, 105, 1000);

    private static ChartSeries CipherB()
    {
        var provider = new CipherBProvider();
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var s = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService())
            .CreateSeriesFromMetadata(provider.GetIndicators()[0], "Cipher B", "Pane_CIPHER_B", new List<(string, string)>(), null);
        foreach (var d in provider.GetDefaultLevels("CIPHER_B"))
            s.Levels.Add(new LevelConfig
            {
                Name = d.Name, Value = d.Value, ColorHex = d.ColorHex, DashStyle = d.Dash, IsVisible = true,
                PlayEarcon = d.PlayEarcon, EarconVolume = d.EarconVolume,
                ZoneNoiseAmount = d.ZoneNoiseAmount, ZoneNoiseType = d.ZoneNoiseType,
            });
        return s;
    }

    private static AudioPoint Sound(ChartSeries s, string component, double v, double? prev = null)
    {
        var c = s.Components.Single(x => (x.DisplayName ?? x.Name) == component);
        return new DefaultSonificationStrategy(new SoundPatchRegistry())
            .CreateAudioPoint(s, c, v, Bar, 5, 10, (-100, 100), 1f, prev);
    }

    [Theory]
    [InlineData("RSI")]
    [InlineData("Stochastic")]
    public void Textured_beyond_80_and_20_and_not_at_the_waves_53(string line)
    {
        var s = CipherB();
        float clean = Sound(s, line, 50).NoiseAmount;

        Assert.True(Sound(s, line, 85).NoiseAmount > clean, $"{line} at 85 is not textured");
        Assert.True(Sound(s, line, 15).NoiseAmount > clean, $"{line} at 15 is not textured");
        Assert.Equal(clean, Sound(s, line, 55).NoiseAmount);   // above the waves' +53: not an extreme here
        Assert.Equal(clean, Sound(s, line, 75).NoiseAmount);
    }

    [Theory]
    [InlineData("RSI")]
    [InlineData("Stochastic")]
    public void Clicks_crossing_80_and_20_but_not_its_middle_or_the_waves_lines(string line)
    {
        var s = CipherB();
        Assert.True(Sound(s, line, 81, prev: 79).TriggerClick, $"{line} crossing 80 did not click");
        Assert.True(Sound(s, line, 19, prev: 21).TriggerClick, $"{line} crossing 20 did not click");
        Assert.False(Sound(s, line, 54, prev: 52).TriggerClick, $"{line} clicked at the waves' 53");
        Assert.False(Sound(s, line, 61, prev: 59).TriggerClick, $"{line} clicked at the waves' 60");
        Assert.False(Sound(s, line, 51, prev: 49).TriggerClick, $"{line} clicked at its own middle");
    }

    /// <summary>
    /// The waves keep the pane's ±53 / ±60 — the new zones are the two lines' alone — and the ±53
    /// zone is now HEARD on them: three steps, clean → rough past ±53 → rougher past ±60. Until
    /// 2026-10-17 the ±53 texture (0.10) sat under the waves' own 0.15 and was never played.
    /// </summary>
    [Theory]
    [InlineData("Wave Trend Fast")]
    [InlineData("Wave Trend Slow")]
    public void The_waves_get_rougher_past_53_and_again_past_60(string wave)
    {
        var s = CipherB();
        float clean = Sound(s, wave, 10).NoiseAmount;
        foreach (int sign in new[] { 1, -1 })
        {
            float past53 = Sound(s, wave, sign * 56).NoiseAmount, past60 = Sound(s, wave, sign * 65).NoiseAmount;
            Assert.True(past53 > clean, $"{wave} at {sign * 56}: {past53} is no rougher than at 10 ({clean})");
            Assert.True(past60 > past53, $"{wave} at {sign * 65}: {past60} is no rougher than at {sign * 56} ({past53})");
        }
        Assert.Equal(clean, Sound(s, wave, 30).NoiseAmount);   // 30 is not "oversold 20" for a wave
    }
}
