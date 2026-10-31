using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using Newtonsoft.Json;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A setting says what it does and its default, nothing else (Cody, 2026-10-30).</b>
///
/// <para>Cody: "The indicator properties of cipher b general tab mentions market cipher b and
/// functionality before....strip out that text. Settings are for the setting name and description
/// and value, that's it. No MCB's default, just default". The Properties dialog prints every
/// parameter's Description under it, and Cipher A, B, SR and C, DSS Bressert and DBSI-style said
/// which other product a default matched, what the indicator did before a date, or what a trader
/// uses. Cipher B's default Money Flow Formula and Buy Dot Rule were the value "MCB"; they are
/// "Standard", and a save holding "MCB" is rewritten on restore.</para>
/// </summary>
public sealed class SettingTextNamesOnlyTheSettingTests
{
    /// <summary>Other products, their makers and traders, the original an indicator was built
    /// against, and the indicator's own history (a date, "before 20xx").</summary>
    private static readonly Regex Foreign = new(
        @"Market Cipher|\bMCB\b|\bMCA\b|VuManChu|TradingView|MetaTrader|NinjaTrader|Crypto Face|Cycle Fanatic|DBSI's|\boriginal\b|\b(before|since|until) 20\d\d|\b20\d\d-\d\d-\d\d\b",
        RegexOptions.IgnoreCase);

    [Fact]
    public void No_setting_names_another_product_a_trader_or_the_indicators_history()
    {
        var bad = new List<string>();
        foreach (var provider in IndicatorProviderFixture.AllProviders())
            foreach (var meta in provider.GetIndicators())
                foreach (var p in meta.Parameters)
                {
                    if (Foreign.IsMatch(p.Description ?? "")) bad.Add($"{meta.Name} / {p.DisplayName}: {p.Description}");
                    if (p.DefaultValue is string s && Foreign.IsMatch(s)) bad.Add($"{meta.Name} / {p.DisplayName} default: {s}");
                }
        Assert.True(bad.Count == 0, "Settings that say more than what they do:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void Cipher_Bs_saved_MCB_choices_restore_as_Standard_and_compute_the_same()
    {
        var meta = new CipherBProvider().GetIndicators();
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        Assert.Equal("MCB", saved.StringParameters["MoneyFlowMode"]);
        Assert.Equal("MCB", saved.StringParameters["BuyDotMode"]);

        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);

        Assert.Equal("Standard", saved.StringParameters["MoneyFlowMode"]);
        Assert.Equal("Standard", saved.StringParameters["BuyDotMode"]);
        var p = meta[0].Parameters;
        Assert.Equal("Standard", p.Single(x => x.Name == "MoneyFlowMode").DefaultValue);
        Assert.Equal("Standard", p.Single(x => x.Name == "BuyDotMode").DefaultValue);

        // The word is only a name: "MCB", "Standard" and no value at all are one calculation, and
        // Legacy / Gated (the other choices) are not, so the comparison can see a difference.
        var bars = CausalityProbeSeries.Bars(0, 1400);
        var none = Run(bars);
        var mcb = Run(bars, ("MoneyFlowMode", "MCB"), ("BuyDotMode", "MCB"));
        var std = Run(bars, ("MoneyFlowMode", "Standard"), ("BuyDotMode", "Standard"));
        var other = Run(bars, ("MoneyFlowMode", "Legacy"), ("BuyDotMode", "Gated"));
        foreach (var (k, v) in none)
        {
            Assert.Equal(v, mcb[k]);
            Assert.Equal(v, std[k]);
        }
        Assert.NotEqual(none[CipherBProvider.CompMoneyFlowWave], other[CipherBProvider.CompMoneyFlowWave]);
    }

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, params (string Key, object Value)[] p)
    {
        var pars = new Dictionary<string, object>();
        foreach (var (k, v) in p) pars[k] = v;
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), pars, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }
}
