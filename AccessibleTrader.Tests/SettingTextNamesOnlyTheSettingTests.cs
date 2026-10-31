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
    public void Cipher_Bs_saved_mode_choices_are_dropped_on_restore_and_change_nothing()
    {
        // The modes were names for Cipher B's calculation ("MCB", then "Standard") beside the older
        // Legacy / Gated / Percentile ones; all three went on 2026-10-31 with Cipher B's parts
        // Market Cipher B does not draw, so a save's choice is dropped and computes the same.
        var meta = new CipherBProvider().GetIndicators();
        var saved = JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
        Assert.Equal("MCB", saved.StringParameters["MoneyFlowMode"]);
        Assert.Equal("MCB", saved.StringParameters["BuyDotMode"]);

        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);

        Assert.False(saved.StringParameters.ContainsKey("MoneyFlowMode"));
        Assert.False(saved.StringParameters.ContainsKey("BuyDotMode"));
        Assert.False(saved.StringParameters.ContainsKey("ThresholdMode"));
        var p = meta[0].Parameters;
        Assert.DoesNotContain(p, x => x.Name is "MoneyFlowMode" or "BuyDotMode" or "ThresholdMode");

        var bars = CausalityProbeSeries.Bars(0, 1400);
        var none = Run(bars);
        foreach (var choices in new[]
                 {
                     new (string, object)[] { ("MoneyFlowMode", "MCB"), ("BuyDotMode", "MCB") },
                     new (string, object)[] { ("MoneyFlowMode", "Standard"), ("BuyDotMode", "Standard") },
                     new (string, object)[] { ("MoneyFlowMode", "Legacy"), ("BuyDotMode", "Gated"), ("ThresholdMode", "Percentile") },
                 })
        {
            var r = Run(bars, choices);
            Assert.Equal(none.Keys.OrderBy(k => k), r.Keys.OrderBy(k => k));
            foreach (var (k, v) in none) Assert.Equal(v, r[k]);
        }
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
