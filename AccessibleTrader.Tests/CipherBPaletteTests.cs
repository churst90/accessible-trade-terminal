using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's colours against real Market Cipher B.</b>
///
/// <para>2026-10-16 open question: the WT Histogram (#00FF7F / #FF2D55) and Money Flow
/// (#00E676 / #FF1744) had near-identical pairs, so their legend keys and their bars looked
/// alike. Checked on 2026-10-17 against real MCB screenshots (the official product image on
/// marketciphertrading.com; TradingView ideas vBzsjpDu, whose status line reads "MarketCipher B
/// (2.2.6, …)", 37mQbQzO and LfGIzkND): MCB's "VWAP" (our WT Histogram) is ONE yellow, #FFEB3B;
/// its money flow is lime / red; its cross dots are TradingView red #FF5252 and lime #00E676.
/// The reasoning and the composite arithmetic are in CipherBProvider's comments.</para>
///
/// <para>Every colour here is read through the path the chart uses: the provider's metadata,
/// through <see cref="IndicatorModelFactory"/>, into the legend rows or Skia's own hex parser.</para>
/// </summary>
public sealed class CipherBPaletteTests
{
    private static ChartSeries CipherB()
    {
        var meta = new CipherBProvider().GetIndicators()[0];
        var styling = new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService());
        var factory = new IndicatorModelFactory(styling, new MockIndicatorPreferencesService());
        return factory.CreateSeriesFromMetadata(meta, "Cipher B", "Pane_CIPHER_B", new List<(string, string)>(), null);
    }

    private static ThemeService Themes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        return new ThemeService(settings);
    }

    private static float HueDistance(SKColor a, SKColor b)
    {
        a.ToHsv(out float ha, out _, out _);
        b.ToHsv(out float hb, out _, out _);
        float d = Math.Abs(ha - hb) % 360f;
        return d > 180f ? 360f - d : d;
    }

    private static SKColor Parsed(ChartSeries s, string component)
    {
        var comp = s.Components.Single(c => c.Name == component);
        Assert.True(SKColor.TryParse(comp.ColorHex, out var color), $"{component}: '{comp.ColorHex}' does not parse");
        return color;
    }

    /// <summary>
    /// The WT Histogram and Money Flow readings are at least 30° of hue apart, above zero and
    /// below. The old pairs were 1° apart (green 150° vs 151°, red 345° vs 348°). Read through
    /// <see cref="ChartRenderer.ReadingColor"/> — the colour the status line prints each value in
    /// and the axis label is filled with — in every built-in theme, because a colour-vision theme
    /// can remap a bar pair. (Was read from the legend's key rows until the TradingView status
    /// line replaced the legend, 2026-10-17.)
    /// </summary>
    [Fact]
    public void The_wt_histogram_and_money_flow_readings_are_told_apart_by_hue()
    {
        var s = CipherB();
        var hist = s.Components.Single(c => c.Name == CipherBProvider.CompWtHistogram);
        var mf   = s.Components.Single(c => c.DisplayName == "Money Flow");
        foreach (var type in Enum.GetValues<ThemeType>())
        {
            var themes = Themes();
            themes.SetTheme(type);
            foreach (double v in new[] { 10.0, -10.0 })
            {
                var h = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(v, s, hist, 0), themes.Current, null);
                var m = ChartRenderer.ReadingColor(new ChartRenderer.CrosshairReading(v, s, mf, 0), themes.Current, null);
                float d = HueDistance(h, m);
                Assert.True(d >= 30f,
                    $"{type}, value {v}: WT Histogram {h} and Money Flow {m} are only {d:F0}° of hue apart.");
            }
        }
    }

    /// <summary>MCB's "VWAP" is one yellow on both sides of zero; its label colour is #FFEB3B.</summary>
    [Fact]
    public void The_wt_histogram_is_mcb_vwap_yellow_on_both_sides_of_zero()
    {
        var hist = CipherB().Components.Single(c => c.Name == CipherBProvider.CompWtHistogram);
        // At MCB's ~73% opacity since 2026-10-17 (#BA = 186): the colour is compared opaque.
        Assert.Equal(SKColor.Parse("#FFEB3B"), SKColor.Parse(hist.ColorHex).WithAlpha(255));
        Assert.Equal(SKColor.Parse("#FFEB3B"), SKColor.Parse(hist.ColorHexSecondary).WithAlpha(255));
    }

    /// <summary>MCB's money flow: lime above zero, red below (composite-solved, see the provider).</summary>
    [Fact]
    public void Money_flow_is_mcb_lime_and_red()
    {
        var mf = CipherB().Components.Single(c => c.Name == CipherBProvider.CompMoneyFlowWave);
        // At MCB's ~50% opacity since 2026-10-17 (#80): the colour is compared opaque.
        Assert.Equal(SKColor.Parse("#3FFF00"), SKColor.Parse(mf.ColorHex).WithAlpha(255));
        Assert.Equal(SKColor.Parse("#FF1100"), SKColor.Parse(mf.ColorHexSecondary).WithAlpha(255));
    }

    /// <summary>
    /// <b>The bear cross dot drew grey and the bull one a faint pale green.</b> Skia reads an
    /// 8-digit hex as #AARRGGBB; the provider wrote #RRGGBBAA. Parsed the way the renderer parses
    /// them (StylingService.GetPaint), the dots must be Market Cipher B 2.4.0's declared colours
    /// (2026-10-31; they were a 2.2.6 screenshot fit, #00E676 / #FF5252 at 85%): up
    /// rgba(63,255,0,0.8), down rgba(255,0,0,0.65).
    /// </summary>
    [Fact]
    public void The_cross_dots_parse_to_mcb_lime_and_red()
    {
        var s = CipherB();
        var bull = Parsed(s, CipherBProvider.CompCrossBull);
        var bear = Parsed(s, CipherBProvider.CompCrossBear);

        Assert.Equal((0x3F, 0xFF, 0x00, 0xCC), ((int)bull.Red, (int)bull.Green, (int)bull.Blue, (int)bull.Alpha));
        Assert.Equal((0xFF, 0x00, 0x00, 0xA6), ((int)bear.Red, (int)bear.Green, (int)bear.Blue, (int)bear.Alpha));
    }
}
