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
/// <b>Cipher B's gold dot, refitted and shown (2026-10-18); its buy dot renamed.</b>
///
/// <para>Cody: make the gold dot visible by default and refit it to Market Cipher B's gold-dot
/// conditions; keep the old rule selectable; call it "Gold dot"; call the big green dot "Buy dot".
/// The only definition of a Cipher B gold dot that can be read is the open VuManChu Cipher B
/// code's <c>wtGoldBuy</c> (pastebin.com/raw/m8Kp9XZk), so GoldDotMode = MCB ports it exactly
/// (<see cref="CipherBProvider.McbGoldDots"/>, which quotes the lines). No dated Market Cipher B
/// chart we could find shows a gold dot.</para>
///
/// <para>The rule tests below build the slow wave, lows and RSI by hand from that code's
/// definition — bar by bar, with the expected bar written down — rather than restating the
/// implementation. An independent Pine-semantics re-implementation (scratch, not committed) agreed
/// with the port on every bar of daily BTC, ETH, SOL and XRP.</para>
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
        CipherBProvider.McbGoldDots(c.Wt2, c.Low, c.Rsi, rsiDivergence)
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

    // ── Wired into Cipher B ───────────────────────────────────────────────────────────

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, params (string Key, object Value)[] p)
    {
        var pars = new Dictionary<string, object>();
        foreach (var (k, v) in p) pars[k] = v;
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", bars.ToArray(), pars, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static List<int> Fired(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    /// <summary>The default gold dot is the rule above on Cipher B's own slow wave, the raw lows and
    /// RSI(RSIPeriod) of the close; drawn in the money-flow bar where the buy dot is.</summary>
    [Theory]
    [InlineData(14, false)]
    [InlineData(10, false)]
    [InlineData(14, true)]
    public void The_default_gold_dot_is_that_rule_on_the_slow_wave(int rsiPeriod, bool rsiDivergence)
    {
        int total = 0;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var r = Run(bars, ("RSIPeriod", rsiPeriod), ("GoldRsiDivergence", rsiDivergence));
            var rsi = IndicatorMath.Rsi(bars.Select(b => b.Close).ToArray(), rsiPeriod);
            var expected = CipherBProvider.McbGoldDots(r[CipherBProvider.CompWT2], bars.Select(b => b.Low).ToArray(), rsi, rsiDivergence)
                .Select((g, i) => g ? i : -1).Where(i => i >= 0).ToList();
            var gold = Fired(r[CipherBProvider.CompGold]);
            Assert.Equal(expected, gold);
            Assert.All(gold, i => Assert.Equal(CipherBProvider.McbBuyDotY, r[CipherBProvider.CompGold][i]));
            total += gold.Count;
        }
        Assert.True(total > 0, "the MCB gold dot fired on no probe series — nothing was tested");
    }

    /// <summary>
    /// Causal, and on the bar it is first knowable: every gold dot of a 1400-bar run is already
    /// there when the series ENDS on that bar, and no prefix ever shows a dot the full run does not.
    /// (The code it is ported from draws the dot two bars back, on the bottom — offset −2 — which
    /// would need the two bars after it.)
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
            var full = Run(bars, ("GoldRsiDivergence", rsiDivergence))[CipherBProvider.CompGold];
            var ends = Fired(full).Select(t => t + 1).Concat(new[] { 300, 700, 1000, 1399 }).Distinct();
            foreach (int k in ends)
            {
                var prefix = Run(bars.Take(k).ToList(), ("GoldRsiDivergence", rsiDivergence))[CipherBProvider.CompGold];
                for (int i = 0; i < k; i++)
                    Assert.True(double.IsNaN(full[i]) == double.IsNaN(prefix[i]),
                        $"flavour {flavour}: bar {i} {(double.IsNaN(full[i]) ? "has no" : "has a")} gold dot with 1400 bars loaded but not with {k}");
            }
            checkedDots += Fired(full).Count;
        }
        Assert.True(checkedDots > 0, "no gold dot fired — the check would be vacuous");
    }

    // ── The old rule, byte for byte ───────────────────────────────────────────────────

    private static readonly Dictionary<string, (string, object)[]> ParamSets = new()
    {
        ["default"] = Array.Empty<(string, object)>(),
        ["legacy"] = new (string, object)[] { ("WTSignalPeriod", 4.0), ("MoneyFlowMode", "Legacy"), ("BuyDotMode", "Gated") },
        ["percentile"] = new (string, object)[] { ("ThresholdMode", "Percentile"), ("AdaptiveLookback", 300.0) },
        ["noTf_noLag"] = new (string, object)[] { ("TfAware", 0.0), ("DivergenceConfirmLag", false), ("UseAnchorSuppression", 0.0), ("ConfirmBars", 1.0), ("CooldownBars", 3.0) },
        ["legacyPct"] = new (string, object)[] { ("WTSignalPeriod", 4.0), ("MoneyFlowMode", "Legacy"), ("BuyDotMode", "Gated"), ("ThresholdMode", "Percentile"), ("GoldMinConfluence", 1.0), ("AdxGate", 0.0), ("AtrFloorPct", 0.0) },
    };

    private static string Hash(double[] a)
    {
        var bytes = new byte[a.Length * 8];
        Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// GoldDotMode = Confluence gives exactly the gold dot this indicator drew before 2026-10-18 —
    /// saved strategies and backtests were measured on it. The hashes are of the gold array's raw
    /// doubles, captured from main at 6f4b557d before the refit (and the same capture on daily BTC,
    /// ETH, SOL, XRP, ETH 4h and SPY 5m, 60 runs, was byte-identical too; scratch, not committed).
    /// </summary>
    [Theory]
    [InlineData(0, "default", 13, "E1B8EF1599433B4AB97EF3405E2E430FF63E64868738098416154295E8A18BD8")]
    [InlineData(0, "legacy", 12, "221F053565D0562FD2E291376363272B3C5C069836CB474ECE619A0D81FD6AA7")]
    [InlineData(0, "percentile", 4, "2B7F2E1BD922ABACBC1306BC4F36FB7D337BE7A52A80EE317A24E465B9438FE8")]
    [InlineData(0, "noTf_noLag", 22, "F602F201153D5D507B7501E84C5F75E81132596BDD9168C333BEC50D30047F25")]
    [InlineData(0, "legacyPct", 4, "1D4823C38C0850A8B744C411255D3D26738209648E77FD35A05F21907D30BB96")]
    [InlineData(1, "default", 20, "DFB9281E1E5A3BBAE3CE0BAAEABB58EA236501C6BC25216F25B93A1210A78FBB")]
    [InlineData(1, "legacy", 18, "4F7C0E17B17E85B35026B5D27D6795C15C5EA9BAB01E61685190B1DA7BD8CAF9")]
    [InlineData(1, "percentile", 7, "3A4DC20748FA45BD49A73B47612A364315526E35A7F7F8BA87A75F63A72E79A7")]
    [InlineData(1, "noTf_noLag", 24, "74D21C27DF846CE70B3364CCF1F1FAEBAFC1351B301CBFE99AC5A1881BF7D89C")]
    [InlineData(1, "legacyPct", 5, "B10975D9CA334C522B6631ED78E14EB30BCD075C9C9056FE80DAC0F2ADC3A3CA")]
    [InlineData(2, "default", 15, "E5F9235CA81F1671489ECAE567A0FA8CD8B933675A7B639904E553E98D0DFDD5")]
    [InlineData(2, "legacy", 15, "59539EA2F9BA93EB04B1B23A0C9ECE3231B6CEA81BEB102BBE2F5C6F68A38AC1")]
    [InlineData(2, "percentile", 11, "3D32BCED57957B06242EB6CC532C9A91CF2A2C13F84BEC7070C3A1FBAD5464B5")]
    [InlineData(2, "noTf_noLag", 13, "13E6E37401C46C184E82BA2AAAC265079A4AA6056376BCBE81A7721DECE169CB")]
    [InlineData(2, "legacyPct", 10, "65388843C0F8D8AB17F163EC8F9E02A94595B5963344C343BD54368513103A1D")]
    [InlineData(3, "default", 15, "E5F9235CA81F1671489ECAE567A0FA8CD8B933675A7B639904E553E98D0DFDD5")]
    [InlineData(3, "legacyPct", 10, "65388843C0F8D8AB17F163EC8F9E02A94595B5963344C343BD54368513103A1D")]
    public void Confluence_mode_reproduces_the_old_gold_dot_byte_for_byte(int flavour, string set, int fired, string sha256)
    {
        var p = ParamSets[set].Append(("GoldDotMode", (object)"Confluence")).ToArray();
        var gold = Run(CausalityProbeSeries.Bars(flavour, 1400), p)[CipherBProvider.CompGold];
        Assert.Equal(fired, Fired(gold).Count);
        Assert.Equal(sha256, Hash(gold));
    }

    /// <summary>The gold rule moves the gold dot and nothing else: every other array Cipher B writes
    /// is the same in both modes.</summary>
    [Fact]
    public void Gold_dot_mode_changes_only_the_gold_dot()
    {
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var mcb = Run(bars); var conf = Run(bars, ("GoldDotMode", "Confluence"));
            Assert.Equal(conf.Keys.OrderBy(k => k), mcb.Keys.OrderBy(k => k));
            foreach (var key in mcb.Keys.Where(k => k != CipherBProvider.CompGold))
                Assert.Equal(Hash(conf[key]), Hash(mcb[key]));
            if (flavour < 2) Assert.NotEqual(Hash(conf[CipherBProvider.CompGold]), Hash(mcb[CipherBProvider.CompGold]));
        }
    }

    // ── What is said, and is it true ──────────────────────────────────────────────────

    [Fact]
    public void The_two_dots_are_named_Buy_dot_and_Gold_dot_and_keep_their_machine_names()
    {
        var buy = Meta(CipherBProvider.CompBlue);
        var gold = Meta(CipherBProvider.CompGold);
        Assert.Equal(("Oversold Crossover", "Buy dot"), (buy.Name, buy.DisplayName));
        Assert.Equal(("Triple Confluence Buy", "Gold dot"), (gold.Name, gold.DisplayName));
        Assert.Equal("Buy dot, waves crossed up from oversold", buy.DefaultSignalSpeechTemplate);
        Assert.Equal("Gold dot, waves turning up from oversold", gold.DefaultSignalSpeechTemplate);
        // Shown by default again, with its sound unchanged.
        Assert.True(gold.IsVisible);
        Assert.Equal(("dual_tone_bell", 440.0), (gold.DefaultSoundPatchId, gold.DefaultBaseFrequency));
        // Narration (live and playback) leads a template with the component's name unless the
        // template already says it — both do, so neither is heard twice.
        Assert.Equal("Gold dot, waves turning up from oversold",
            SignalClauseSpeech.WithComponentName(gold.DefaultSignalSpeechTemplate!, gold.DisplayName));
        Assert.Equal("Buy dot, waves crossed up from oversold",
            SignalClauseSpeech.WithComponentName(buy.DefaultSignalSpeechTemplate!, buy.DisplayName));
    }

    private static IndicatorComponentMetadata Meta(string name) =>
        new CipherBProvider().GetIndicators()[0].Components.Single(c => c.Name == name);

    /// <summary>
    /// "Waves turning up from oversold" and "waves crossed up from oversold" must be true on every
    /// bar they are spoken, in every mode. Gold MCB: the slow wave made a bottom at or below −65 two
    /// bars ago and is above it now. Gold Confluence: the fast wave crossed up while below −53. Buy
    /// MCB: a cross up with the slow wave at or below −60. Buy Gated: a cross up with the fast wave
    /// below −53.
    /// </summary>
    [Fact]
    public void What_the_dots_say_is_true_on_every_bar_they_fire_in_both_modes()
    {
        int seen = 0;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        {
            var bars = CausalityProbeSeries.Bars(flavour, 1400);
            var mcb = Run(bars);
            var old = Run(bars, ("GoldDotMode", "Confluence"), ("BuyDotMode", "Gated"));
            foreach (var (r, mode) in new[] { (mcb, "MCB"), (old, "old") })
            {
                var fast = r[CipherBProvider.CompWT1]; var slow = r[CipherBProvider.CompWT2];
                bool CrossUp(int t) => fast[t - 1] < slow[t - 1] && fast[t] >= slow[t];
                foreach (int t in Fired(r[CipherBProvider.CompGold]))
                {
                    if (mode == "MCB") Assert.True(slow[t - 2] <= -65 && slow[t - 1] > slow[t - 2] && slow[t] > slow[t - 2], $"MCB gold at {t}");
                    else Assert.True(CrossUp(t) && fast[t] < -53, $"Confluence gold at {t}");
                    seen++;
                }
                foreach (int t in Fired(r[CipherBProvider.CompBlue]))
                {
                    Assert.True(CrossUp(t), $"{mode} buy dot at {t} is not on a cross up");
                    Assert.True(mode == "MCB" ? slow[t] <= -60 : fast[t] < -53, $"{mode} buy dot at {t} is not oversold");
                    seen++;
                }
            }
        }
        Assert.True(seen > 0);
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

    private static (ChartSeries Series, List<Ohlcv> Bars, Dictionary<string, double[]> Results) Chart(int flavour,
        int length = 1400, params (string Key, object Value)[] p)
    {
        var series = Factory.CreateSeriesFromMetadata(new CipherBProvider().GetIndicators()[0], "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
        var bars = CausalityProbeSeries.Bars(flavour, length);
        var results = Run(bars, p);
        foreach (var kv in results) series.Data.ComponentData[kv.Key] = kv.Value;
        return (series, bars, results);
    }

    private static WorkspaceState At(ChartSeries series, List<Ohlcv> bars, string component, int bar) =>
        WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id,
            FocusedSeriesId = series.Id,
            FocusedComponentIndex = series.Components.FindIndex(c => c.Name == component),
            CurrentDataIndex = bar,
            ViewportStartIndex = Math.Max(0, bar - 50),
            ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };

    /// <summary>
    /// Every place a dot is named says "Gold dot" / "Buy dot": Ctrl+Up/Down landing (the formatter
    /// the speech router uses, provider speech answering), the status line, Alt+Shift+D, and the
    /// strategy and alert pickers (SignalCatalog), whose ids stay the machine names.
    /// </summary>
    [Theory]
    [InlineData(CipherBProvider.CompGold, "Gold dot")]
    [InlineData(CipherBProvider.CompBlue, "Buy dot")]
    public void Every_place_a_dot_is_named_uses_its_new_name(string component, string spoken)
    {
        var (series, bars, results) = Chart(1);
        int bar = Fired(results[component])[^1];
        var state = At(series, bars, component, bar);
        string oldName = component;

        string landing = new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, Engine())
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[bar], "");
        Assert.StartsWith($"{spoken}. ", landing);
        Assert.DoesNotContain(oldName, landing, StringComparison.OrdinalIgnoreCase);

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
        var line = ChartStatusLine.SeriesLine(series, bar, new ThemeService(settings).Current, SKColors.Black, bars);
        Assert.NotNull(line);
        Assert.Contains(line!.Values, v => v.Text == spoken);
        Assert.DoesNotContain(line.Values, v => v.Text.Contains(oldName, StringComparison.OrdinalIgnoreCase));

        var bus = new SpyEventBus();
        new BarDetailService(bus).AnnounceDetails(state);
        string detail = bus.Log.OfType<AnnouncementEvent>().Last().Message;
        Assert.Contains(spoken, detail);
        Assert.DoesNotContain(oldName, detail, StringComparison.OrdinalIgnoreCase);

        var catalog = new SignalCatalog(new IIndicatorProvider[] { new CipherBProvider() });
        var d = catalog.All.Single(x => x.Id == $"CIPHER_B.{component}");
        Assert.Equal($"Cipher B — {spoken}", d.DisplayLabel);
    }

    /// <summary>Landing on an MCB gold dot reads the wave as it is: "oversold" only past −53. It
    /// said ", oversold" on every gold bar, true of the Confluence rule only — the MCB rule fires two
    /// bars after a slow-wave bottom, by when the fast wave is often above −53 (daily BTC 2023-03-13:
    /// −38.0; daily ETH 2024-09-10: −51.1). 6000-bar probes, with and without the RSI divergence,
    /// because the 1400-bar ones happen to put every gold dot below −53.</summary>
    [Fact]
    public void Landing_on_a_gold_dot_says_oversold_only_when_the_wave_is()
    {
        int notOversold = 0, total = 0;
        foreach (int flavour in CausalityProbeSeries.AllFlavours)
        foreach (bool rsiDivergence in new[] { false, true })
        {
            var (series, bars, results) = Chart(flavour, 6000, ("GoldRsiDivergence", rsiDivergence));
            var formatter = new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, Engine());
            foreach (int bar in Fired(results[CipherBProvider.CompGold]))
            {
                double wt = results[CipherBProvider.CompWT1][bar];
                string said = formatter.FormatPointFeedback(At(series, bars, CipherBProvider.CompGold, bar),
                    isXMove: false, isYMove: true, series, bars[bar], "");
                Assert.Contains($"Wave Trend {wt:F1}", said);
                Assert.Equal(wt < -53, said.Contains("oversold"));
                if (wt >= -53) notOversold++;
                total++;
            }
        }
        Assert.True(total > 0);
        Assert.True(notOversold > 0, "every MCB gold bar had the fast wave below -53 — the unconditional 'oversold' was never false here, so this proves nothing");
    }

    // ── Shown on saved charts too ─────────────────────────────────────────────────────

    /// <summary>A saved series through the restore path a resumed session takes, against the given
    /// Cipher B metadata (the current one unless a test needs the 2026-10-17 one).</summary>
    private static ChartSeries Restore(SeriesConfig saved, List<IndicatorMetadata>? meta = null)
    {
        meta ??= new CipherBProvider().GetIndicators();
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    private static SeriesConfig CodysSave() => JsonConvert.DeserializeObject<SeriesConfig>(CodysCipherBSave.Json)!;
    private static SeriesConfig SaveAgain(ChartSeries s) => JsonConvert.DeserializeObject<SeriesConfig>(JsonConvert.SerializeObject(s.Config))!;
    private static bool GoldShown(ChartSeries s) => s.Components.Single(c => c.Name == CipherBProvider.CompGold).IsVisible;

    /// <summary>The Cipher B that ran on 2026-10-17 after the stock-MCB change: gold hidden by default.</summary>
    private static List<IndicatorMetadata> MetadataWithGoldHidden()
    {
        var meta = new CipherBProvider().GetIndicators();
        meta[0].Components.Single(c => c.Name == CipherBProvider.CompGold).IsVisible = false;
        return meta;
    }

    [Fact]
    public void A_new_chart_shows_the_gold_dot()
    {
        var s = Factory.CreateSeriesFromMetadata(new CipherBProvider().GetIndicators()[0], "Cipher B", "Pane_CIPHER_B",
            new List<(string, string)>(), componentOverrides: null);
        Assert.True(GoldShown(s));
    }

    /// <summary>
    /// Cody's real save, in both shapes it can have reached disk: as saved at 14:21 (before the
    /// visibility record existed, gold shown), and as the 2026-10-17 build would have re-saved it
    /// (gold hidden BY DEFAULT, the record saying so) — made here by restoring his save against that
    /// build's metadata and saving it. Both now show gold, and the restart after is stable.
    /// </summary>
    [Fact]
    public void Codys_saved_chart_shows_the_gold_dot_whether_saved_before_or_while_it_was_hidden_by_default()
    {
        Assert.True(GoldShown(Restore(CodysSave())));

        var hiddenEra = Restore(CodysSave(), MetadataWithGoldHidden());
        Assert.False(GoldShown(hiddenEra));   // what he had on screen that evening
        var onDisk = SaveAgain(hiddenEra);
        var gold = onDisk.Components.Single(c => c.Name == CipherBProvider.CompGold);
        Assert.Equal((false, (bool?)false), (gold.IsVisible, gold.ProviderIsVisible));

        var now = Restore(onDisk);
        Assert.True(GoldShown(now));
        Assert.True(GoldShown(Restore(SaveAgain(now))));
        // The rest of that day's defaults hold: the red dot and the divergences stay hidden.
        Assert.All(new[] { CipherBProvider.CompRed, CipherBProvider.CompBullDiv, CipherBProvider.CompHidBear },
            n => Assert.False(now.Components.Single(c => c.Name == n).IsVisible));
    }

    /// <summary>A gold the user hid stays hidden: hidden now (saved against a shown default), or
    /// hidden in a save from before the record existed — every such save was written while gold was
    /// shown by default, so a hidden gold there was his.</summary>
    [Fact]
    public void A_gold_dot_the_user_hid_stays_hidden()
    {
        var s = Restore(CodysSave());
        s.Components.Single(c => c.Name == CipherBProvider.CompGold).IsVisible = false;
        Assert.False(GoldShown(Restore(SaveAgain(s))));

        var old = CodysSave();
        old.Components.Single(c => c.Name == CipherBProvider.CompGold).IsVisible = false;
        Assert.Null(old.Components.Single(c => c.Name == CipherBProvider.CompGold).ProviderIsVisible);
        Assert.False(GoldShown(Restore(old)));
    }
}
