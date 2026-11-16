using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's Sell dot</b> (Cody, 2026-11-15: "please add a large red sell dot to cipher b,
/// disabled by default, as the original cipher B doesn't use it").
///
/// <para>The mirror of Market Cipher B's green buy dot: where the buy dot marks the waves crossing
/// UP while Wave Trend is under Oversold Level 1, the Sell dot marks them crossing DOWN while it is
/// over Overbought Level 1, drawn large and red at the top of the pane. Market Cipher B does not
/// draw it, so it is left out until Properties → Parts includes it, and a new Cipher B looks and
/// stops exactly as before (<see cref="CipherBStockVisibilityTests"/>).</para>
/// </summary>
public sealed class CipherBSellDotTests
{
    private static Dictionary<string, double[]> Run(Dictionary<string, object>? parameters = null)
    {
        var bars = CipherAProviderTests.RawBars();
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            parameters ?? new Dictionary<string, object>(), new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static IndicatorComponentMetadata Meta() =>
        new CipherBProvider().GetIndicators()[0].Components.Single(c => c.Name == CipherBProvider.CompSellDot);

    [Theory]
    [InlineData(60.0)]   // Market Cipher B's Overbought Level 1
    [InlineData(40.0)]   // moved: the dot follows the setting
    public void A_sell_dot_is_a_cross_down_over_overbought_level_1(double level)
    {
        var r = Run(level == 60.0 ? null : new() { [CipherBProvider.ParamOBLevel1] = level });
        double[] wt1 = r[CipherBProvider.CompWT1], wt2 = r[CipherBProvider.CompWT2], dot = r[CipherBProvider.CompSellDot];
        int dots = 0, crossesNotMarked = 0;
        for (int i = 0; i < dot.Length; i++)
        {
            bool cross = WaveTrendMath.CrossedUnder(wt1, wt2, i);
            bool expected = cross && wt1[i] > level;
            Assert.True(expected == !double.IsNaN(dot[i]), $"bar {i}: cross {cross}, wt1 {wt1[i]:F2}, dot {dot[i]}");
            if (expected) { dots++; Assert.Equal(CipherBProvider.SellDotY, dot[i]); }
            else if (cross) crossesNotMarked++;
        }
        // Vacuity: the data has dots, and crosses down that are not over the level.
        Assert.True(dots > 0, "no sell dots on the data");
        Assert.True(crossesNotMarked > 0, "every cross down was over the level");
    }

    [Fact]
    public void Moving_overbought_level_1_moves_the_dots()
    {
        int Count(Dictionary<string, object>? p) => Run(p)[CipherBProvider.CompSellDot].Count(v => !double.IsNaN(v));
        Assert.True(Count(new() { [CipherBProvider.ParamOBLevel1] = 40.0 }) > Count(null));
    }

    [Fact]
    public void It_is_a_large_red_dot_at_the_top_left_out_by_default()
    {
        var m = Meta();
        Assert.Equal(ComponentDisplayType.Dot, m.DisplayType);
        Assert.Equal(ComponentRole.Signal, m.Role);
        Assert.False(m.DefaultIsEnabled);
        Assert.Equal("Sell dot", m.DisplayName);
        Assert.Equal(96.0, CipherBProvider.SellDotY);   // where the buy dot is at -96
        var look = new ComponentConfig
        {
            Name = m.Name, DisplayName = m.DisplayName!, DisplayType = m.DisplayType, Role = ComponentRole.Signal,
            IsVisible = true, ColorHex = m.DefaultColorHex!, Thickness = m.DefaultThickness ?? 1f,
        };
        var cfg = new SeriesConfig { Id = "cb", Name = "cb", FriendlyName = "Cipher B", IndicatorCode = "CIPHER_B", Pane = "Pane_CIPHER_B", IsVisible = true };
        cfg.Components.Add(look);
        var buf = new SeriesDataBuffer { SeriesId = "cb" };
        buf.ComponentData[m.Name] = new[] { CipherBProvider.SellDotY };
        Assert.Equal("large red dot", MarkerLook.Describe(new ChartSeries(cfg, buf), look, 0));
    }

    /// <summary>Its sound and speech say a sell, not the buy dot's.</summary>
    [Fact]
    public void It_says_a_sell()
    {
        var m = Meta();
        Assert.Contains("Sell dot", m.DefaultSignalSpeechTemplate);
        Assert.Contains("down from overbought", m.DefaultSignalSpeechTemplate);
    }
}
