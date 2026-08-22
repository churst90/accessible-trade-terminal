using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the Cosasverdes claims empirically instead of taking them on faith.
///
/// Three experiments, all measured by <see cref="LevelRespectAnalyzer"/>:
///
///   E1  Multi-timeframe equivalence. A weekly 10 EMA and a daily 70 EMA cover the SAME
///       70 days but are not the same line: the weekly one ignores the intra-week path and
///       only moves on weekly closes. The claim is that the higher-timeframe sampling is
///       respected more. Each HTF spec is paired against its equal-span same-timeframe twin
///       and the hold rates are compared head to head.
///
///   E2  Which periods a market actually honours — the full EMA/SMA grid, ranked.
///
///   E3  Horizontals anchored on candle BODIES versus candle WICKS. The claim is that bodies
///       make stronger lines.
///
/// <para>
/// CONTROLS ARE THE POINT. In a trending market almost any line-shaped object gets touched
/// often and "held" sometimes, so a raw hold rate proves nothing. Every candidate is therefore
/// paired with a SHIFTED TWIN: the identical line displaced vertically by a fixed ATR multiple.
/// The twin has the same slope, curvature and update cadence, and differs only in being at the
/// wrong price. The reported edge is (real hold rate − twin hold rate). If a market's 10 EMA
/// beats its own shifted copy, the level means something; if it doesn't, the line is decoration.
/// </para>
/// </summary>
public static class RespectCommand
{
    /// <summary>How far the control twin is displaced, in ATR. Far enough to be a different
    /// level, close enough to sit in the same price neighbourhood and see similar traffic.</summary>
    private const double ControlOffsetAtr = 2.0;

    private const int MinTouchesForVerdict = 8;

    public static Task<int> RunAsync(string snapshotDir, string? only, string tf, int surrogates = 30)
    {
        if (!Directory.Exists(snapshotDir))
        {
            Console.Error.WriteLine($"Snapshot dir not found: {snapshotDir}");
            return Task.FromResult(1);
        }

        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No {tf} snapshots matched.");
            return Task.FromResult(1);
        }

        var analyzer = new LevelRespectAnalyzer();
        var ranker = new MaRespectRanker(analyzer, new ResamplerService());
        var opts = RespectOptions.Default;

        // Focused probe for the surrogate test: the periods Cosasverdes names, at chart
        // timeframe and stepped from higher ones. Surrogates are expensive, so this is a
        // deliberate subset rather than the whole grid.
        var focusSpecs = new List<MaSpec>
        {
            new("EMA", 10), new("EMA", 21), new("EMA", 89), new("EMA", 200),
            new("EMA", 10, "2d"), new("EMA", 10, "1w"), new("EMA", 10, "1M"),
            new("EMA", 21, "1w"),
        };
        var e4Rows = new List<SurrogateTest.Result>();

        var e1Rows = new List<MtfRow>();
        var e2Rows = new List<MaRow>();
        var e3Rows = new List<BodyWickRow>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {Path.GetFileName(file)}: {ex.Message}"); continue; }

            var bars = snap.Bars;
            if (bars.Count < 400) { Console.WriteLine($"-- {snap.Symbol} {tf}: only {bars.Count} bars, skipped"); continue; }

            string asset = $"{snap.Symbol} {tf}";
            Console.WriteLine();
            Console.WriteLine($"=== {asset} — {bars.Count} bars, {bars[0].Date:yyyy-MM-dd} → {bars[^1].Date:yyyy-MM-dd} ===");

            var specs = MaRespectRanker.DefaultSpecs(tf).ToList();

            // Build every real candidate plus its shifted control twin in one pass so both are
            // measured over identical bars with identical settings.
            var candidates = new List<LineCandidate>();
            var twinOf = new Dictionary<string, string>();
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), opts.AtrPeriod);

            foreach (var spec in specs)
            {
                var real = ranker.BuildCandidate(bars, tf, spec);
                if (real == null) continue;
                if (real.Values.Count(v => !double.IsNaN(v)) < 200) continue;
                candidates.Add(real);

                // TWO control twins, displaced up AND down. A one-sided control is biased: in a
                // long uptrend a line shifted up sits under price far more often than one shifted
                // down, so its touch profile differs for reasons that have nothing to do with the
                // level being real. Averaging both sides removes that asymmetry.
                var up = Shift(real, atr, ControlOffsetAtr, "U");
                var down = Shift(real, atr, -ControlOffsetAtr, "D");
                candidates.Add(up);
                candidates.Add(down);
                twinOf[real.Id] = up.Id;
            }

            var stats = analyzer.Analyze(bars, candidates, opts).ToDictionary(s => s.Id);

            // ── E2: ranked table ─────────────────────────────────────────────
            var real2 = stats.Values
                .Where(s => !s.Id.StartsWith("CTRL", StringComparison.Ordinal))
                .Where(s => s.Touches >= MinTouchesForVerdict)
                .OrderByDescending(s => s.Score)
                .ToList();

            Console.WriteLine($"  {"line",-16} {"n",4} {"hold%",6} {"ric",4} {"rec",4} {"thru",5} {"pts",5} {"ctrl%",6} {"edge",6}");
            foreach (var s in real2.Take(10))
            {
                double ctrl = ControlHoldRate(stats, twinOf, s.Id);
                Console.WriteLine(
                    $"  {s.Label,-16} {s.Touches,4} {s.HoldRate * 100,5:0}% {s.Ricochets,4} {s.Reclaims,4} " +
                    $"{s.Touches - s.Holds,5} {s.MeanPoints,5:0.00} {ctrl * 100,5:0}% {(s.HoldRate - ctrl) * 100,5:+0;-0;0}pp");
            }

            foreach (var s in real2)
            {
                double ctrl = ControlHoldRate(stats, twinOf, s.Id);
                e2Rows.Add(new MaRow(asset, s.Label, s.Kind, s.Touches, s.HoldRate, s.MeanPoints, ctrl));
            }

            // ── E1: MTF vs equal-span same-timeframe twin ────────────────────
            long chartMs = TimeframeUtility.ToMilliseconds(tf);
            foreach (var spec in specs.Where(sp => sp.SourceTimeframe != null))
            {
                long htfMs = TimeframeUtility.ToMilliseconds(spec.SourceTimeframe!);
                if (chartMs <= 0 || htfMs <= 0) continue;

                int equivalent = (int)Math.Round(spec.Period * (double)htfMs / chartMs);
                if (equivalent < 2 || equivalent > 600) continue;

                if (!stats.TryGetValue(spec.Id, out var htf)) continue;

                // The equal-span twin may not be in the default grid, so build it on demand.
                var sameSpec = new MaSpec(spec.MaType, equivalent);
                var sameCandidate = ranker.BuildCandidate(bars, tf, sameSpec);
                if (sameCandidate == null) continue;
                var same = analyzer.Analyze(bars, new[] { sameCandidate }, opts).FirstOrDefault();
                if (same == null) continue;

                if (htf.Touches < MinTouchesForVerdict || same.Touches < MinTouchesForVerdict) continue;

                e1Rows.Add(new MtfRow(asset, spec.Label, htf.Touches, htf.HoldRate, htf.MeanPoints,
                    $"{equivalent} {spec.MaType}", same.Touches, same.HoldRate, same.MeanPoints));
            }

            // ── E4: surrogate significance ───────────────────────────────────
            // Seeded off the asset name so a rerun reproduces exactly. StableSeed, not
            // GetHashCode — the latter is randomised per process, which is what made this
            // comment untrue for as long as it has been here.
            int seed = StableSeed.From(asset) % 100000;
            foreach (var spec in focusSpecs)
            {
                if (spec.SourceTimeframe != null &&
                    TimeframeUtility.ToMilliseconds(spec.SourceTimeframe) <= TimeframeUtility.ToMilliseconds(tf))
                    continue;

                var r = SurrogateTest.Run(asset, bars, tf, spec, ranker, analyzer, opts, surrogates, seed);
                if (r.SurrogateCount > 0 && r.Touches >= MinTouchesForVerdict) e4Rows.Add(r);
            }

            // ── E3: body-anchored vs wick-anchored horizontals ───────────────
            var bodyLevels = SwingLevels(bars, useBodies: true);
            var wickLevels = SwingLevels(bars, useBodies: false);
            var bodyStats = HorizontalStats(analyzer, bars, bodyLevels, "BODY", opts);
            var wickStats = HorizontalStats(analyzer, bars, wickLevels, "WICK", opts);
            if (bodyStats.n >= MinTouchesForVerdict && wickStats.n >= MinTouchesForVerdict)
            {
                e3Rows.Add(new BodyWickRow(asset, bodyStats.n, bodyStats.hold, wickStats.n, wickStats.hold));
                Console.WriteLine($"  horizontals: bodies {bodyStats.hold * 100:0}% (n={bodyStats.n})  " +
                                  $"wicks {wickStats.hold * 100:0}% (n={wickStats.n})  " +
                                  $"edge {(bodyStats.hold - wickStats.hold) * 100:+0;-0;0}pp");
            }
        }

        PrintSummaries(e1Rows, e2Rows, e3Rows, e4Rows, surrogates);
        return Task.FromResult(0);
    }

    // ── Summaries ─────────────────────────────────────────────────────────────

    private static void PrintSummaries(List<MtfRow> e1, List<MaRow> e2, List<BodyWickRow> e3,
        List<SurrogateTest.Result> e4, int surrogates)
    {
        Console.WriteLine();
        Console.WriteLine($"========== E4: surrogate test ({surrogates} block-bootstrap surrogates each) ==========");
        Console.WriteLine("Does real price respect its own MA more than a statistically identical random");
        Console.WriteLine("series respects ITS own MA? p is the one-sided empirical p-value; p<=0.05 means");
        Console.WriteLine("the real series beat at least 95% of surrogates.");
        Console.WriteLine();
        Console.WriteLine($"  {"asset",-16} {"line",-12} {"n",5} {"real%",6} {"surr%",6} {"edge",7} {"z",6} {"p",6}");
        foreach (var r in e4.OrderByDescending(r => r.ZScore))
            Console.WriteLine($"  {r.Asset,-16} {r.Label,-12} {r.Touches,5} {r.RealHold * 100,5:0}% " +
                              $"{r.SurrogateMean * 100,5:0}% {r.EdgePp,6:+0.0;-0.0;0}pp {r.ZScore,6:+0.0;-0.0;0} {r.PValue,6:0.000}");

        if (e4.Count > 0)
        {
            int sig = e4.Count(r => r.PValue <= 0.05);
            int pos = e4.Count(r => r.EdgePp > 0);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: {pos}/{e4.Count} pairs had a positive edge; {sig}/{e4.Count} were");
            Console.WriteLine($"  significant at p<=0.05. Mean edge {e4.Average(r => r.EdgePp):+0.0;-0.0;0} pp, " +
                              $"mean z {e4.Average(r => double.IsNaN(r.ZScore) ? 0 : r.ZScore):+0.00;-0.00;0}.");
            Console.WriteLine($"  At p<=0.05 you would expect ~{e4.Count * 0.05:0.0} false positives by chance alone.");

            Console.WriteLine();
            Console.WriteLine("  By line (mean across assets):");
            foreach (var g in e4.GroupBy(r => r.Label).OrderByDescending(g => g.Average(r => r.EdgePp)))
                Console.WriteLine($"    {g.Key,-12} assets={g.Count(),2}  edge={g.Average(r => r.EdgePp),6:+0.0;-0.0;0}pp  " +
                                  $"meanZ={g.Average(r => double.IsNaN(r.ZScore) ? 0 : r.ZScore),5:+0.00;-0.00;0}  " +
                                  $"sig={g.Count(r => r.PValue <= 0.05)}/{g.Count()}");
        }

        Console.WriteLine();
        Console.WriteLine("================ E1: multi-timeframe vs equal-span same-timeframe ================");
        Console.WriteLine("Same calendar span, different sampling. Positive edge = the higher-timeframe");
        Console.WriteLine("sampled average was held more often than its same-timeframe twin.");
        Console.WriteLine();
        Console.WriteLine($"  {"asset",-16} {"HTF line",-14} {"n",4} {"hold%",6} | {"equal-span",-12} {"n",4} {"hold%",6} | {"edge",7}");
        foreach (var r in e1.OrderByDescending(r => r.HtfHold - r.SameHold))
            Console.WriteLine($"  {r.Asset,-16} {r.HtfLabel,-14} {r.HtfTouches,4} {r.HtfHold * 100,5:0}% | " +
                              $"{r.SameLabel,-12} {r.SameTouches,4} {r.SameHold * 100,5:0}% | {(r.HtfHold - r.SameHold) * 100,6:+0.0;-0.0;0}pp");

        if (e1.Count > 0)
        {
            int wins = e1.Count(r => r.HtfHold > r.SameHold);
            double mean = e1.Average(r => r.HtfHold - r.SameHold);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: HTF beat its equal-span twin in {wins}/{e1.Count} pairs " +
                              $"({(double)wins / e1.Count * 100:0}%), mean edge {mean * 100:+0.0;-0.0;0} pp.");
        }

        Console.WriteLine();
        Console.WriteLine("================ E2: which lines beat their own shifted control ================");
        Console.WriteLine("Edge = real hold rate − hold rate of the SAME line displaced 2 ATR. A line that");
        Console.WriteLine("cannot beat its own shifted copy is decoration, however good its raw rate looks.");
        Console.WriteLine();

        var byLabel = e2.GroupBy(r => r.Label)
            .Where(g => g.Count() >= 3)
            .Select(g => new
            {
                Label = g.Key,
                Assets = g.Count(),
                Touches = g.Sum(x => x.Touches),
                Hold = g.Average(x => x.Hold),
                Ctrl = g.Average(x => x.Control),
                Edge = g.Average(x => x.Hold - x.Control),
                Pts = g.Average(x => x.MeanPoints),
                Wins = g.Count(x => x.Hold > x.Control),
            })
            .OrderByDescending(x => x.Edge)
            .ToList();

        Console.WriteLine($"  {"line",-16} {"assets",6} {"touches",8} {"hold%",6} {"ctrl%",6} {"edge",7} {"pts",5} {"beat ctrl",10}");
        foreach (var x in byLabel)
            Console.WriteLine($"  {x.Label,-16} {x.Assets,6} {x.Touches,8} {x.Hold * 100,5:0}% {x.Ctrl * 100,5:0}% " +
                              $"{x.Edge * 100,6:+0.0;-0.0;0}pp {x.Pts,5:0.00} {x.Wins + "/" + x.Assets,10}");

        if (e2.Count > 0)
        {
            double meanEdge = e2.Average(r => r.Hold - r.Control);
            int beat = e2.Count(r => r.Hold > r.Control);
            Console.WriteLine();
            Console.WriteLine($"  OVERALL: {beat}/{e2.Count} line-asset pairs beat their control " +
                              $"({(double)beat / e2.Count * 100:0}%), mean edge {meanEdge * 100:+0.0;-0.0;0} pp.");
        }

        Console.WriteLine();
        Console.WriteLine("================ E3: body-anchored vs wick-anchored horizontals ================");
        Console.WriteLine($"  {"asset",-16} {"body n",7} {"body%",6} {"wick n",7} {"wick%",6} {"edge",7}");
        foreach (var r in e3.OrderByDescending(r => r.BodyHold - r.WickHold))
            Console.WriteLine($"  {r.Asset,-16} {r.BodyTouches,7} {r.BodyHold * 100,5:0}% {r.WickTouches,7} " +
                              $"{r.WickHold * 100,5:0}% {(r.BodyHold - r.WickHold) * 100,6:+0.0;-0.0;0}pp");
        if (e3.Count > 0)
        {
            int wins = e3.Count(r => r.BodyHold > r.WickHold);
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: bodies beat wicks on {wins}/{e3.Count} assets, " +
                              $"mean edge {e3.Average(r => r.BodyHold - r.WickHold) * 100:+0.0;-0.0;0} pp.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The same line displaced by a fixed ATR multiple — the control twin.</summary>
    private static LineCandidate Shift(LineCandidate source, double[] atr, double atrMultiple, string tag)
    {
        var values = new double[source.Values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            double a = i < atr.Length ? atr[i] : double.NaN;
            values[i] = double.IsNaN(source.Values[i]) || double.IsNaN(a)
                ? double.NaN
                : source.Values[i] + a * atrMultiple;
        }
        return new LineCandidate($"CTRL{tag}:" + source.Id, "ctrl " + source.Label, source.Kind, values);
    }

    /// <summary>
    /// Pooled hold rate across BOTH control twins. Pooling touches (rather than averaging two
    /// rates) weights each twin by how much evidence it actually produced.
    /// </summary>
    private static double ControlHoldRate(Dictionary<string, RespectStats> stats,
        Dictionary<string, string> twinOf, string realId)
    {
        if (!twinOf.TryGetValue(realId, out var upId)) return double.NaN;
        string downId = upId.Replace("CTRLU:", "CTRLD:");

        int touches = 0, holds = 0;
        foreach (var id in new[] { upId, downId })
            if (stats.TryGetValue(id, out var t)) { touches += t.Touches; holds += t.Holds; }

        return touches > 0 ? (double)holds / touches : double.NaN;
    }

    /// <summary>
    /// Swing highs and lows, taken either from candle bodies (max/min of open and close) or from
    /// wicks (high/low). A level is emitted only for pivots confirmed by <paramref name="span"/>
    /// bars either side, and only from the FIRST 60% of history so there is room left to test it.
    /// </summary>
    private static List<double> SwingLevels(IReadOnlyList<Ohlcv> bars, bool useBodies, int span = 10)
    {
        var levels = new List<double>();
        int limit = (int)(bars.Count * 0.6);

        for (int i = span; i < limit - span; i++)
        {
            double hi = useBodies ? Math.Max(bars[i].Open, bars[i].Close) : bars[i].High;
            double lo = useBodies ? Math.Min(bars[i].Open, bars[i].Close) : bars[i].Low;

            bool isHigh = true, isLow = true;
            for (int j = i - span; j <= i + span && (isHigh || isLow); j++)
            {
                if (j == i) continue;
                double jh = useBodies ? Math.Max(bars[j].Open, bars[j].Close) : bars[j].High;
                double jl = useBodies ? Math.Min(bars[j].Open, bars[j].Close) : bars[j].Low;
                if (jh >= hi) isHigh = false;
                if (jl <= lo) isLow = false;
            }
            if (isHigh) levels.Add(hi);
            if (isLow) levels.Add(lo);
        }
        return levels;
    }

    /// <summary>Pools every horizontal's touches into one aggregate hold rate.</summary>
    private static (int n, double hold) HorizontalStats(
        ILevelRespectAnalyzer analyzer, IReadOnlyList<Ohlcv> bars, List<double> levels,
        string tag, RespectOptions opts)
    {
        if (levels.Count == 0) return (0, double.NaN);

        var candidates = new List<LineCandidate>(levels.Count);
        for (int k = 0; k < levels.Count; k++)
        {
            var values = new double[bars.Count];
            Array.Fill(values, levels[k]);
            candidates.Add(new LineCandidate($"{tag}:{k}", $"{tag} {levels[k]:G6}", LineKind.Horizontal, values));
        }

        var stats = analyzer.Analyze(bars, candidates, opts);
        int touches = stats.Sum(s => s.Touches);
        int holds = stats.Sum(s => s.Holds);
        return (touches, touches > 0 ? (double)holds / touches : double.NaN);
    }

    private record MtfRow(string Asset, string HtfLabel, int HtfTouches, double HtfHold, double HtfPoints,
        string SameLabel, int SameTouches, double SameHold, double SamePoints);

    private record MaRow(string Asset, string Label, LineKind Kind, int Touches, double Hold,
        double MeanPoints, double Control);

    private record BodyWickRow(string Asset, int BodyTouches, double BodyHold, int WickTouches, double WickHold);
}
