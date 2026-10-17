using System.Collections.Immutable;
using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher A is Market Cipher A as measured (2026-10-17).</b>
///
/// <para>Two kinds of golden answer, neither of them a copy of the C#:</para>
/// <list type="bullet">
/// <item><b>What real Market Cipher A drew.</b> The symbols on two TradingView snapshots of
/// BTCUSD 1D Bitstamp Heikin-Ashi, digitised from the pixels (<see cref="CipherAGoldenData.Measured"/>),
/// and 654 ribbon-line colours read off one of them. The provider is fed the same Heikin-Ashi bars
/// TradingView fed Market Cipher A and must put the symbols on the same candles.</item>
/// <item><b>The fitted model's numbers.</b> The Python reference model the fit produced, run on the
/// raw candles (<see cref="CipherAGoldenData.RawEvents"/>, <see cref="CipherAGoldenData.RawValues"/>).</item>
/// </list>
/// <para>Then: an old saved Cipher A restores as the new one, a strategy or alert naming a removed
/// component is told why, and Red Diamond is exactly Cipher B's wave cross down (the WaveTrend
/// arithmetic is a copy until the two are merged — <see cref="WaveTrendMath"/>).</para>
/// </summary>
public sealed class CipherAProviderTests
{
    private readonly ITestOutputHelper _out;
    public CipherAProviderTests(ITestOutputHelper output) => _out = output;

    private static readonly string[] Symbols =
    {
        CipherAProvider.CompGreenDot, CipherAProvider.CompBlueTriangle, CipherAProvider.CompYellowDiamond,
        CipherAProvider.CompRedX, CipherAProvider.CompRedDiamond, CipherAProvider.CompBloodDiamond, CipherAProvider.CompYellowX,
    };

    // ── bars ─────────────────────────────────────────────────────────────────────────

    internal static List<Ohlcv> RawBars()
    {
        var list = new List<Ohlcv>();
        foreach (var line in CipherAGoldenData.BarsCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Trim().Split(',');
            double P(int k) => double.Parse(f[k], CultureInfo.InvariantCulture);
            list.Add(new Ohlcv(DateTime.ParseExact(f[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                P(1), P(2), P(3), P(4), P(5)));
        }
        return list;
    }

    internal static Dictionary<string, double[]> Run(List<Ohlcv> bars, Dictionary<string, object>? p = null)
    {
        var results = new Dictionary<string, double[]>();
        new CipherAProvider().Calculate("CIPHER_A", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            p ?? new Dictionary<string, object>(), new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static string D(Ohlcv b) => b.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<string> Fired(Dictionary<string, double[]> r, string comp, List<Ohlcv> bars) =>
        Enumerable.Range(0, bars.Count).Where(i => !double.IsNaN(r[comp][i])).Select(i => D(bars[i])).ToList();

    // ── 1. The fitted model's numbers, on the raw candles ──────────────────────────────

    /// <summary>
    /// The eight ribbon EMAs, the two waves and the 80-bar stochastic match the fitted reference
    /// model at three dates to 1e-9 relative — EMA of the CLOSE at 5 11 15 18 21 25 29 33, WaveTrend
    /// hlc3 9/12/SMA3.
    /// </summary>
    [Fact]
    public void The_ribbon_and_waves_are_the_fitted_model()
    {
        var bars = RawBars(); var r = Run(bars);
        Assert.Equal(587, bars.Count);
        foreach (var (date, g) in CipherAGoldenData.RawValues)
        {
            int i = bars.FindIndex(b => D(b) == date);
            for (int k = 0; k < 8; k++)
                Close(g.Ema[k], r[CipherAProvider.RibbonLine(k)][i], $"{CipherAProvider.RibbonLine(k)} on {date}");
            Close(g.Wt1, r[CipherAProvider.Wt1Key][i], $"fast wave on {date}");
            Close(g.Wt2, r[CipherAProvider.Wt2Key][i], $"slow wave on {date}");
            Close(g.Stoch, r[CipherAProvider.StochKey][i], $"80-bar stochastic on {date}");
        }

        static void Close(double want, double got, string what) =>
            Assert.True(Math.Abs(want - got) <= 1e-9 * Math.Max(1, Math.Abs(want)), $"{what}: want {want}, got {got}");
    }

    /// <summary>Every symbol fires on exactly the bars the fitted model fires it on, over 587 real days.</summary>
    [Fact]
    public void Every_symbol_fires_where_the_fitted_model_fires_it()
    {
        var bars = RawBars(); var r = Run(bars);
        foreach (var comp in Symbols)
        {
            var got = Fired(r, comp, bars); var want = CipherAGoldenData.RawEvents[comp];
            Assert.True(got.SequenceEqual(want),
                $"{comp}: model {want.Length} [{string.Join(" ", want.Take(12))}], provider {got.Count} [{string.Join(" ", got.Take(12))}]");
        }
    }

    // ── 2. What real Market Cipher A drew ───────────────────────────────────────────────

    /// <summary>
    /// On AiASKFgl (BTCUSD 1D Bitstamp Heikin-Ashi, Sep 2021 – Feb 2022, about 9 px a candle so no
    /// symbol merges with its neighbour), every symbol real Market Cipher A drew is drawn on the same
    /// candle, and nothing is drawn that it did not draw — except one Yellow Diamond on 2021-10-04,
    /// which sits where the chart's Green Dot covers it.
    /// </summary>
    [Fact]
    public void On_a_real_Market_Cipher_A_chart_every_symbol_lands_on_its_candle()
    {
        var ha = ChartMath.CalculateHeikinAshi(RawBars()); var r = Run(ha);
        var (from, to, events) = CipherAGoldenData.Measured["s4"];
        var report = new List<string>(); int measured = 0;
        foreach (var comp in Symbols)
        {
            var want = events[comp];
            var got = Fired(r, comp, ha).Where(d => string.CompareOrdinal(d, from) >= 0 && string.CompareOrdinal(d, to) <= 0).ToList();
            var missing = want.Except(got).ToList(); var extra = got.Except(want).ToList();
            if (comp == CipherAProvider.CompYellowDiamond) extra.Remove("2021-10-04");
            measured += want.Length;
            _out.WriteLine($"{comp}: real {want.Length}, ours {got.Count}, missing [{string.Join(" ", missing)}], extra [{string.Join(" ", extra)}]");
            if (missing.Count + extra.Count > 0) report.Add($"{comp}: missing [{string.Join(" ", missing)}] extra [{string.Join(" ", extra)}]");
        }
        Assert.True(measured >= 45, $"only {measured} measured symbols in the window — the golden data is not what this test thinks");
        Assert.True(report.Count == 0, "Cipher A differs from real Market Cipher A on AiASKFgl:\n  " + string.Join("\n  ", report));
    }

    /// <summary>
    /// On vBzsjpDu ("MarketCipher A (2.1.2, 1, 21)" in its legend; 3.7 px a candle over 13 months) the
    /// symbols that stay distinct at that size: every Green Dot, Red X, Blood Diamond and Yellow X on
    /// its candle, every Blue Triangle within one candle, and at least 34 of the 38 Red Diamonds
    /// (several sit inside chains of Yellow Diamonds, where the digitiser places them a candle off).
    /// </summary>
    [Fact]
    public void Over_thirteen_months_of_a_second_real_chart_the_distinct_symbols_land()
    {
        var ha = ChartMath.CalculateHeikinAshi(RawBars()); var r = Run(ha);
        var (from, to, events) = CipherAGoldenData.Measured["s2"];
        var idx = ha.Select((b, i) => (D(b), i)).ToDictionary(z => z.Item1, z => z.i);
        int Recall(string comp, int tol)
        {
            var got = Fired(r, comp, ha).Where(d => string.CompareOrdinal(d, from) >= 0 && string.CompareOrdinal(d, to) <= 0).Select(d => idx[d]).ToList();
            return events[comp].Count(d => got.Any(g => Math.Abs(g - idx[d]) <= tol));
        }
        foreach (var comp in new[] { CipherAProvider.CompGreenDot, CipherAProvider.CompRedX, CipherAProvider.CompBloodDiamond, CipherAProvider.CompYellowX })
            Assert.True(Recall(comp, 0) == events[comp].Length, $"{comp}: {Recall(comp, 0)} of {events[comp].Length} on the candle");
        Assert.Equal(events[CipherAProvider.CompBlueTriangle].Length, Recall(CipherAProvider.CompBlueTriangle, 1));
        Assert.True(events[CipherAProvider.CompRedDiamond].Length >= 38 && Recall(CipherAProvider.CompRedDiamond, 0) >= 34,
            $"Red Diamond: {Recall(CipherAProvider.CompRedDiamond, 0)} of {events[CipherAProvider.CompRedDiamond].Length} on the candle");
    }

    private static readonly IndicatorModelFactory Factory = new(
        new StylingService(new ComponentRoleMapper(), new SonificationProfileProvider(), new PaneAssignmentService()),
        new MockIndicatorPreferencesService());

    /// <summary>
    /// Each ribbon line's colour, as the renderer resolves it, against the 654 line colours digitised
    /// from vBzsjpDu: blue/white while the line is above the next-longer one (the last line: below
    /// the one before), grey otherwise. The colour is classed from the pixel the renderer would paint
    /// (blue channel high and above red), not from the declaration.
    /// </summary>
    [Fact]
    public void Each_ribbon_line_is_blue_or_grey_where_Market_Cipher_A_drew_it_so()
    {
        var ha = ChartMath.CalculateHeikinAshi(RawBars()); var r = Run(ha);
        var series = Restored(Array.Empty<string>());
        foreach (var kv in r) series.Data.ComponentData[kv.Key] = kv.Value;
        var idx = ha.Select((b, i) => (D(b), i)).ToDictionary(z => z.Item1, z => z.i);

        int ok = 0; var wrong = new List<string>();
        foreach (var row in CipherAGoldenData.MeasuredRibbonColours)
        {
            var f = row.Split(','); int line = int.Parse(f[1]) - 1; bool blue = f[2] == "B";
            var comp = series.Components.Single(c => c.Name == CipherAProvider.RibbonLine(line));
            int i = idx[f[0]];
            var c = StandardRenderers.ColorAgainstSibling(comp, series, i, r[comp.Name][i]);
            Assert.True(c.HasValue, $"{comp.Name} on {f[0]} is not coloured against a sibling");
            bool paintedBlue = c!.Value.Blue >= 190 && c.Value.Blue > c.Value.Red + 20;
            if (paintedBlue == blue) ok++; else wrong.Add($"{f[0]} {comp.Name}: real {(blue ? "blue" : "grey")}");
        }
        Assert.True(CipherAGoldenData.MeasuredRibbonColours.Length >= 600, "the measured colours are missing");
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {ok + wrong.Count} ribbon colours differ from Market Cipher A:\n  " + string.Join("\n  ", wrong.Take(10)));
    }

    // ── 3. Cipher B parity: one WaveTrend ───────────────────────────────────────────────

    /// <summary>
    /// A Red Diamond (or a Blood Diamond, which is a Red Diamond on a Red X candle) is drawn on
    /// exactly the candles where Cipher B draws its wave-cross-down dot: both read MCB's fitted
    /// WaveTrend. The arithmetic is copied into <see cref="WaveTrendMath"/>, so this is what notices
    /// one copy changing without the other.
    /// </summary>
    [Fact]
    public void Red_and_blood_diamonds_are_Cipher_B_wave_crosses_down()
    {
        var bars = RawBars(); var a = Run(bars);
        var b = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            new Dictionary<string, object>(), new IndicatorResultBuffer(b, bars.Count));
        var aBars = Fired(a, CipherAProvider.CompRedDiamond, bars).Concat(Fired(a, CipherAProvider.CompBloodDiamond, bars)).OrderBy(d => d).ToList();
        var bBars = Fired(b, CipherBProvider.CompCrossBear, bars);
        Assert.True(aBars.Count > 40, "too few wave crosses to compare");
        Assert.True(aBars.SequenceEqual(bBars),
            $"Cipher A {aBars.Count} vs Cipher B {bBars.Count}: only A [{string.Join(" ", aBars.Except(bBars))}] only B [{string.Join(" ", bBars.Except(aBars))}]");
    }

    // ── 4. Sound and speech defaults ────────────────────────────────────────────────────

    /// <summary>
    /// Symbols ping, each with its own patch-and-pitch pair, so no two sound alike; the eight
    /// ribbon lines start muted (eight voices a few percent from price would bury the candles).
    /// </summary>
    [Fact]
    public void Symbols_ping_and_the_ribbon_starts_muted()
    {
        var meta = new CipherAProvider().GetIndicators().Single();
        Assert.False(meta.IsDeprecated);
        Assert.Equal("Cipher A", meta.Name);
        var symbols = meta.Components.Where(c => Symbols.Contains(c.Name)).ToList();
        Assert.Equal(7, symbols.Count);
        Assert.All(symbols, s => Assert.True(s.DefaultIsMuted != true && s.DefaultEnvelopeType == "Ping" && !string.IsNullOrEmpty(s.DefaultSoundPatchId), s.Name));
        Assert.Equal(7, symbols.Select(s => (s.DefaultSoundPatchId, s.DefaultBaseFrequency)).Distinct().Count());
        var ribbon = meta.Components.Where(c => c.Name.StartsWith("EMA ")).ToList();
        Assert.Equal(new[] { "EMA 5", "EMA 11", "EMA 15", "EMA 18", "EMA 21", "EMA 25", "EMA 29", "EMA 33" }, ribbon.Select(c => c.Name));
        Assert.All(ribbon, c => Assert.True(c.DefaultIsMuted == true, c.Name));
    }

    /// <summary>What Ctrl+Up/Down says on each stop of a Cipher A on the bar each symbol fired:
    /// the symbol's name and what made it, and for a ribbon line its value and colour.</summary>
    [Fact]
    public void Each_stop_says_its_symbol_name_and_why()
    {
        var bars = RawBars(); var r = Run(bars);
        var series = Restored(Array.Empty<string>());
        foreach (var kv in r) series.Data.ComponentData[kv.Key] = kv.Value;
        var engine = Engine();
        var expect = new Dictionary<string, string>
        {
            [CipherAProvider.CompGreenDot] = "EMA 11 crossed above EMA 33",
            [CipherAProvider.CompBlueTriangle] = "EMA 5 crossed above EMA 25",
            [CipherAProvider.CompYellowDiamond] = "Wave Trend above its slow wave",
            [CipherAProvider.CompRedX] = "EMA 5 crossed below EMA 11",
            [CipherAProvider.CompRedDiamond] = "Wave Trend crossed down",
            [CipherAProvider.CompBloodDiamond] = "Red diamond and red X on one candle",
            [CipherAProvider.CompYellowX] = "80-bar range",
        };
        foreach (var (comp, words) in expect)
        {
            int at = Enumerable.Range(0, bars.Count).Last(i => !double.IsNaN(r[comp][i]));
            int ci = series.Components.FindIndex(c => c.Name == comp);
            string said = Landing(series, bars, engine, ci, at);
            _out.WriteLine($"{comp}: {said}");
            Assert.Contains(comp, said, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(words, said);
        }
        int e5 = series.Components.FindIndex(c => c.Name == "EMA 5");
        string line = Landing(series, bars, engine, e5, bars.Count - 1);
        _out.WriteLine(line);
        Assert.Matches("(Blue, bullish: above|Grey, bearish: below) EMA 11", line);
    }

    // ── 5. A Cipher A saved before the rebuild ──────────────────────────────────────────

    /// <summary>Every component the all-WaveTrend Cipher A declared, in order — what an old chart carries.</summary>
    internal static readonly string[] SavedBeforeRebuild =
    {
        "WT Momentum", "Buy Signal", "Sell Signal", "Bullish Divergence", "Bearish Divergence",
        "Overbought Bearish Divergence", "Manipulation", "Exhaustion", "WT Momentum Gradient",
    };

    private static IndicatorEngine Engine()
    {
        var providers = new List<IIndicatorProvider> { new CipherAProvider() };
        return new IndicatorEngine(new IndicatorService(providers, NullLogger<IndicatorService>.Instance), new CustomIndicatorRegistry(), providers);
    }

    private static ChartSeries Restored(string[] savedComponents, Dictionary<string, double>? savedParams = null)
    {
        var meta = new CipherAProvider().GetIndicators();
        var saved = new SeriesConfig { Id = "ca-1", Name = "Cipher A (retired) 9 12 53 3 3 True", IndicatorCode = "CIPHER_A", Pane = "Main" };
        foreach (var (k, v) in savedParams ?? new()) saved.Parameters[k] = v;
        foreach (var n in savedComponents)
            saved.Components.Add(new ComponentConfig { Name = n, DisplayName = n, IsVisible = n != "WT Momentum Gradient", IsEnabled = true, Volume = 1f, IsMuted = n == "Manipulation" });
        WorkspaceInitializer.MigrateSeriesConfig(saved, meta);
        return SeriesManagementService.MaterializeSaved(saved, meta[0], Factory,
            Array.Empty<IReadOnlyDictionary<string, object>>(), Engine(), new MockIndicatorPreferencesService());
    }

    private static string Landing(ChartSeries series, List<Ohlcv> bars, IndicatorEngine engine, int index, int at)
    {
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            ActiveSeries = ImmutableList.Create(series),
            PrimarySeriesId = series.Id, FocusedSeriesId = series.Id, FocusedComponentIndex = index,
            CurrentDataIndex = at, ViewportStartIndex = Math.Max(0, at - 50), ViewportLength = 100,
            LastInteractionContext = InteractionContext.Component,
        };
        return new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine)
            .FormatPointFeedback(state, isXMove: false, isYMove: true, series, bars[at], "");
    }

    /// <summary>
    /// An old chart's Cipher A comes back as the new one: the new components in the new order, none
    /// of the old (so no keyboard stop speaks a removed signal), named "Cipher A", with old
    /// parameters that no longer exist dropped from what drives it.
    /// </summary>
    [Fact]
    public void A_Cipher_A_saved_before_the_rebuild_restores_as_the_new_one()
    {
        var series = Restored(SavedBeforeRebuild, new() { ["WT1Period"] = 9.0, ["WT2Period"] = 12.0, ["OBLevel"] = 53.0, ["MFPeriod"] = 3.0, ["PivotBars"] = 3.0, ["DivergenceConfirmLag"] = 1.0 });
        var names = series.Components.Select(c => c.Name).ToList();
        _out.WriteLine(string.Join(" | ", names));
        Assert.Equal(Symbols.Concat(Enumerable.Range(0, 8).Select(CipherAProvider.RibbonLine)), names);
        Assert.Empty(names.Intersect(SavedBeforeRebuild));

        var bars = RawBars(); var r = Run(bars, series.Parameters.ToDictionary(kv => kv.Key, kv => (object)kv.Value));
        foreach (var kv in r) series.Data.ComponentData[kv.Key] = kv.Value;
        var engine = Engine();
        var said = Enumerable.Range(0, series.Components.Count).Select(i => Landing(series, bars, engine, i, bars.Count - 1)).ToList();
        foreach (var s in said) _out.WriteLine(s);
        Assert.DoesNotContain(said, s => SavedBeforeRebuild.Any(o => s.Contains(o, StringComparison.OrdinalIgnoreCase)));
        Assert.True(Fired(r, CipherAProvider.CompRedX, bars).SequenceEqual(CipherAGoldenData.RawEvents[CipherAProvider.CompRedX]),
            "the old saved parameters changed what the restored Cipher A computes");
    }

    // ── 6. Strategies and alerts that named a removed component ─────────────────────────

    private static readonly SignalCatalog Catalog = new(new IIndicatorProvider[] { new CipherAProvider() });

    /// <summary>
    /// A condition on any of the nine removed components is false and says why, naming the date and
    /// what to use instead; a condition on a live one says nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(RemovedIds))]
    public void A_condition_on_a_removed_Cipher_A_signal_says_it_was_removed(string id)
    {
        var bars = RawBars(); var series = Restored(Array.Empty<string>());
        foreach (var kv in Run(bars)) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series) };
        var eval = new ConditionEvaluator(Catalog);

        var result = eval.Evaluate(new ConditionLeaf("a", id, LeafOperator.Fired), bars, state);
        Assert.False(result.OverallTrue);
        Assert.NotNull(eval.LastDegradation);
        Assert.Contains("removed on 2026-10-17", eval.LastDegradation!);
        Assert.Contains("Market Cipher A", eval.LastDegradation!);

        eval.Evaluate(new ConditionLeaf("b", "CIPHER_A.Red X", LeafOperator.Fired), bars, state);
        Assert.Null(eval.LastDegradation);
    }

    public static IEnumerable<object[]> RemovedIds() => SavedBeforeRebuild.Select(n => new object[] { $"CIPHER_A.{n}" });

    /// <summary>An alert set on the old Buy Signal says once that it cannot fire, naming it.</summary>
    [Fact]
    public void An_alert_on_the_old_buy_signal_says_it_cannot_fire()
    {
        var bars = RawBars(); var series = Restored(Array.Empty<string>());
        foreach (var kv in Run(bars)) series.Data.ComponentData[kv.Key] = kv.Value;
        var state = WorkspaceState.Initial with { Data = new TimeSeriesBuffer<Ohlcv>(bars), ActiveSeries = ImmutableList.Create(series), CurrentDataIndex = bars.Count - 1 };
        var ev = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
        var said = new List<string>();
        ev.EvaluationDegraded += (_, why) => said.Add(why);
        var alert = new AlertDefinition
        {
            Id = "buy", Name = "buy", Delivery = AlertDelivery.Speech, Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, Threshold = 0, IndicatorCode = "CIPHER_A", ComponentName = "Buy Signal",
        };
        for (int i = 0; i < 2; i++)
            Assert.Empty(ev.EvaluateAlerts(new[] { alert }, state, bars[^1], bars[^2], new Dictionary<string, double>()));
        var why = Assert.Single(said);
        Assert.Contains("Buy Signal", why);
        Assert.Contains("cannot fire", why);
        Assert.Contains("Market Cipher A", why);
    }
}
