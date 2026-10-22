using System.Collections.Immutable;
using System.Security.Cryptography;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fixtures;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The gold dot: kept as a part, removed from Cipher B (2026-10-22).</b>
///
/// <para>Cody, 2026-10-22: "remove the gold dot from the cipher b indicator as it isn't faithful.
/// Keep the gold dot as a drawing element because something may want it later." Market Cipher B
/// draws no gold dot; the only definition that can be read is the open VuManChu Cipher B code's
/// <c>wtGoldBuy</c> (pastebin.com/raw/m8Kp9XZk), ported in <see cref="GoldDot.Rule"/>, and its look
/// and sound are kept in <see cref="GoldDot.Component"/>. No indicator uses either.</para>
///
/// <para>The rule tests below build the slow wave, lows and RSI by hand from that code's
/// definition — bar by bar, with the expected bar written down — rather than restating the
/// implementation. An independent Pine-semantics re-implementation (scratch, not committed) agreed
/// with the port on every bar of daily BTC, ETH, SOL and XRP when it was in Cipher B.</para>
///
/// <para>The rest pins the removal: Cipher B has no gold dot, no gold setting and no gold output; a
/// saved chart or strategy that names it is told why it is gone; the buy dot is unchanged.</para>
/// </summary>
public sealed class CipherBGoldDotTests
{
    // ── The rule, on hand-built series ────────────────────────────────────────────────

    /// <summary>
    /// The shape that fires: a slow-wave bottom of −80 at bar 5 (RSI 25 there, price low 100), a
    /// second bottom of −70 at bar 12 (price low 95: lower price, higher wave), and the slow wave at
    /// −60 at bar 14 — the second bottom is first a bottom once bars 13 and 14 exist, so the dot is
    /// at 14, not 12.
    /// </summary>
    private static (double[] Wt2, double[] Low, double[] Rsi) Base()
    {
        double[] wt2 = { -50, -55, -60, -70, -75, -80, -72, -65, -60, -62, -66, -68, -70, -65, -60, -55, -50, -45, -40, -35 };
        var low = Enumerable.Repeat(110.0, wt2.Length).ToArray();
        low[5] = 100; low[12] = 95;
        var rsi = Enumerable.Repeat(40.0, wt2.Length).ToArray();
        rsi[5] = 25;
        return (wt2, low, rsi);
    }

    private static int[] Gold((double[] Wt2, double[] Low, double[] Rsi) c, bool rsiDivergence = false) =>
        GoldDot.Rule(c.Wt2, c.Low, c.Rsi, rsiDivergence)
            .Select((g, i) => g ? i : -1).Where(i => i >= 0).ToArray();

    public static TheoryData<string, int[]> RuleCases => new()
    {
        { "base: fires two bars after the second bottom", new[] { 14 } },
        { "price made no lower low", Array.Empty<int>() },
        { "the wave made no higher low", Array.Empty<int>() },
        { "earlier bottom only -74", Array.Empty<int>() },
        { "earlier bottom exactly -75 counts", new[] { 14 } },
        { "RSI exactly 30 at the earlier bottom", Array.Empty<int>() },
        { "RSI 29.9 at the earlier bottom", new[] { 14 } },
        { "RSI at the second bottom and now is not read", new[] { 14 } },
        { "slow wave still at -80", Array.Empty<int>() },
        { "slow wave back to -74", new[] { 14 } },
        { "only 2 above the earlier bottom", Array.Empty<int>() },
        { "exactly 5 above the earlier bottom", new[] { 14 } },
        { "a tie beside the second bottom", Array.Empty<int>() },
        { "second bottom above -65", Array.Empty<int>() },
        { "a later bottom is the previous one", Array.Empty<int>() },
    };

    [Theory]
    [MemberData(nameof(RuleCases))]
    public void The_gold_rule_on_hand_built_series(string name, int[] expected)
    {
        var c = Base();
        switch (name)
        {
            case "price made no lower low": c.Low[12] = 101; break;
            case "the wave made no higher low": c.Wt2[12] = -81; break;
            case "earlier bottom only -74": c.Wt2[4] = -72; c.Wt2[5] = -74; break;
            case "earlier bottom exactly -75 counts": c.Wt2[4] = -72; c.Wt2[5] = -75; break;
            case "RSI exactly 30 at the earlier bottom": c.Rsi[5] = 30; break;
            case "RSI 29.9 at the earlier bottom": c.Rsi[5] = 29.9; break;
            case "RSI at the second bottom and now is not read": c.Rsi[12] = 90; c.Rsi[14] = 90; break;
            case "slow wave still at -80":
            case "slow wave back to -74":
                // Earlier bottom −90, second bottom −85; the wave two bars later decides it.
                double[] deep = { -50, -55, -60, -80, -85, -90, -86, -82, -75, -77, -80, -83, -85, -82, -80, -55, -50, -45, -40, -35 };
                deep.CopyTo(c.Wt2, 0);
                if (name == "slow wave back to -74") c.Wt2[14] = -74;
                break;
            case "only 2 above the earlier bottom":
            case "exactly 5 above the earlier bottom":
                // Earlier bottom −76, second −75.5; the wave at −74 is above −75 but only 2 above −76.
                c.Wt2[4] = -72; c.Wt2[5] = -76; c.Wt2[12] = -75.5; c.Wt2[13] = -75;
                c.Wt2[14] = name == "only 2 above the earlier bottom" ? -74 : -71;
                break;
            case "a tie beside the second bottom": c.Wt2[11] = -70; break;
            case "second bottom above -65": c.Wt2[10] = -61; c.Wt2[11] = -62; c.Wt2[12] = -64; c.Wt2[13] = -63; break;
            case "a later bottom is the previous one":
                // A −66 bottom at bar 9 (confirmed at 11) is now the previous one; −70 is not higher.
                c.Wt2[9] = -66; c.Wt2[10] = -63; c.Wt2[11] = -64;
                break;
        }
        Assert.Equal(expected, Gold(c));
    }

    /// <summary>VuManChu's "Show RSI Regular Divergences" (rsiShowDiv, off by default) lets an RSI
    /// divergence stand in for the wave's: here the wave makes a LOWER second bottom (no wave
    /// divergence) while RSI makes a higher one, both at or below 30.</summary>
    [Fact]
    public void An_RSI_divergence_counts_only_when_GoldRsiDivergence_is_on()
    {
        var c = Base();
        c.Wt2[12] = -81;
        double[] rsi = { 40, 40, 40, 30, 25, 20, 24, 28, 40, 40, 35, 30, 25, 28, 32, 40, 40, 40, 40, 40 };
        rsi.CopyTo(c.Rsi, 0);
        Assert.Empty(Gold(c));
        Assert.Equal(new[] { 14 }, Gold(c, rsiDivergence: true));
    }

    // ── The rule is causal ────────────────────────────────────────────────────────────

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, params (string Key, object Value)[] p)
    {
        var pars = new Dictionary<string, object>();
        foreach (var (k, v) in p) pars[k] = v;
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), pars, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static List<int> Fired(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    /// <summary>
    /// On Cipher B's own slow wave, the raw lows and RSI 14 of the close (what it ran on inside Cipher
    /// B): every dot is on the bar it is first knowable, and no shorter history shows a dot the full
    /// one does not. (The code it is ported from draws the dot two bars back, on the bottom — offset
    /// −2 — which would need the two bars after it.)
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_gold_dot_is_on_the_bar_it_is_first_known_and_never_moves(bool rsiDivergence)
    {
        int checkedDots = 0;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var wt2 = Run(bars)[CipherBProvider.CompWT2];
            var low = bars.Select(b => b.Low).ToArray();
            var rsi = IndicatorMath.Rsi(bars.Select(b => b.Close).ToArray(), 14);
            var full = GoldDot.Rule(wt2, low, rsi, rsiDivergence);
            var dots = Enumerable.Range(0, full.Length).Where(i => full[i]).ToList();
            foreach (int k in dots.Select(t => t + 1).Concat(new[] { 300, 700, 1000, 1399 }).Distinct())
            {
                var prefix = GoldDot.Rule(wt2[..k], low[..k], rsi[..k], rsiDivergence);
                for (int i = 0; i < k; i++)
                    Assert.True(full[i] == prefix[i],
                        $"flavour {flavour}: bar {i} {(full[i] ? "has a" : "has no")} gold dot with 1400 bars but not with {k}");
            }
            checkedDots += dots.Count;
        }
        Assert.True(checkedDots > 0, "no gold dot fired — the check would be vacuous");
    }

    /// <summary>The part keeps the look and sound the dot had in Cipher B: a gold dot in the
    /// foreground, a 440 Hz two-tone bell that does not follow the price.</summary>
    [Fact]
    public void The_kept_part_looks_and_sounds_as_the_dot_did()
    {
        var c = GoldDot.Component("Gold", "Gold dot", "Gold dot, waves turning up from oversold");
        Assert.Equal(("Gold", "Gold dot"), (c.Name, c.DisplayName));
        Assert.Equal(ComponentDisplayType.Dot, c.DisplayType);
        Assert.Equal(("#FFD700", 8.0f), (c.DefaultColorHex, c.DefaultThickness));
        Assert.Equal(("dual_tone_bell", 440.0), (c.DefaultSoundPatchId, c.DefaultBaseFrequency));
        Assert.Equal((PitchMapping.None, PlaybackLayer.Foreground), (c.DefaultPitchMapping, c.DefaultPlaybackLayer));
        Assert.Equal("Gold dot, waves turning up from oversold", c.DefaultSignalSpeechTemplate);
    }

    // ── Gone from Cipher B ────────────────────────────────────────────────────────────

    private const string GoldName = "Triple Confluence Buy";

    [Fact]
    public void Cipher_B_has_no_gold_dot_no_gold_setting_and_no_gold_output()
    {
        var meta = new CipherBProvider().GetIndicators()[0];
        Assert.DoesNotContain(meta.Components, c => c.Name == GoldName || (c.DisplayName ?? "").Contains("Gold", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(meta.Parameters, p => p.Name.StartsWith("Gold", StringComparison.Ordinal)
            || p.Name is "RSIPeriod" or "RSIOSLevel" or "AdxPeriod" or "AdxGate" or "AtrPeriod" or "AtrFloorPct");
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            // An old save's gold settings are ignored, not an error.
            var r = Run(CausalityProbeSeries.Bars(flavour, 600), ("GoldDotMode", "Confluence"), ("GoldRsiDivergence", true));
            Assert.DoesNotContain(GoldName, r.Keys);
            Assert.DoesNotContain(r.Keys, k => k.StartsWith(GoldName, StringComparison.Ordinal));
        }
    }

    /// <summary>A strategy or alert built on the gold dot is refused with the reason and the
    /// nearest dot, not evaluated as "never fired".</summary>
    [Fact]
    public void A_strategy_on_the_gold_dot_is_told_it_was_removed()
    {
        var catalog = new SignalCatalog(new IIndicatorProvider[] { new CipherBProvider() });
        Assert.DoesNotContain(catalog.All, d => d.Id == $"CIPHER_B.{GoldName}");
        var why = catalog.RefusalReason($"CIPHER_B.{GoldName}");
        Assert.NotNull(why);
        Assert.Contains("removed on 2026-10-22", why);
        Assert.Contains("Buy dot", why);
    }

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    private static IndicatorEngine Engine()
    {
        var providers = new List<IIndicatorProvider> { new CipherBProvider() };
        return new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance),
            new CustomIndicatorRegistry(), providers);
    }

    private static ChartSeries Restore(SeriesConfig saved)
    {
        var meta = new CipherBProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    private static SeriesConfig CodysSave() => JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
    private static SeriesConfig SaveAgain(ChartSeries s) => JsonConvert.DeserializeObject<SeriesConfig>(JsonConvert.SerializeObject(s.Config))!;

    /// <summary>Cody's real save lists the gold dot. It restores without it, the vanished one has a
    /// retirement notice, and the next save drops it. (What the other components show on his save is
    /// CipherBStockVisibilityTests.Codys_saved_chart_takes_the_new_defaults.)</summary>
    [Fact]
    public void Codys_saved_chart_restores_without_the_gold_dot()
    {
        var saved = CodysSave();
        Assert.Contains(saved.Components, c => c.Name == GoldName);   // else this proves nothing

        var now = Restore(saved);
        Assert.DoesNotContain(now.Components, c => c.Name == GoldName);
        var names = now.Components.Select(c => c.Name).ToHashSet();
        var vanished = saved.Components.Select(c => c.Name)
            .Where(n => !names.Contains(n) && !SignalCatalog.Retired.ContainsKey($"CIPHER_B.{n}")).ToList();
        Assert.True(vanished.Count == 0, $"saved components gone without a retirement notice: {string.Join(", ", vanished)}");
        Assert.DoesNotContain(SaveAgain(now).Components, c => c.Name == GoldName);
    }

    // ── The buy dot is untouched ──────────────────────────────────────────────────────

    /// <summary>"Waves crossed up from oversold" is true on every bar the buy dot fires, in both
    /// modes. MCB: a cross up with the slow wave at or below −60. Gated: a cross up with the fast
    /// wave below −53.</summary>
    [Fact]
    public void What_the_buy_dot_says_is_true_on_every_bar_it_fires_in_both_modes()
    {
        var buy = new CipherBProvider().GetIndicators()[0].Components.Single(c => c.Name == CipherBProvider.CompBlue);
        Assert.Equal(("Oversold Crossover", CipherBProvider.DisplayBuyDot), (buy.Name, buy.DisplayName));
        Assert.Equal("Buy dot, waves crossed up from oversold", buy.DefaultSignalSpeechTemplate);
        int seen = 0;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            foreach (var (r, mode) in new[] { (Run(bars), "MCB"), (Run(bars, ("BuyDotMode", "Gated")), "Gated") })
            {
                var fast = r[CipherBProvider.CompWT1]; var slow = r[CipherBProvider.CompWT2];
                foreach (int t in Fired(r[CipherBProvider.CompBlue]))
                {
                    Assert.True(fast[t - 1] < slow[t - 1] && fast[t] >= slow[t], $"{mode} buy dot at {t} is not on a cross up");
                    Assert.True(mode == "MCB" ? slow[t] <= -60 : fast[t] < -53, $"{mode} buy dot at {t} is not oversold");
                    seen++;
                }
            }
        }
        Assert.True(seen > 0);
    }
}
