using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the "favourability" thesis: that conditions are not binary signals but a CONTINUOUS
/// gradient — the further price moves in one direction, the more favourable a position in the
/// other direction becomes — so entries and exits should be scaled in and out over a period
/// rather than taken at a single moment.
///
/// <para>
/// This is a better-posed question than the binary cell comparisons that came before it, and the
/// test is correspondingly stronger. Rather than contrasting two hand-picked buckets, every bar
/// gets a score and the bars are sorted into deciles. The claim is a MONOTONIC dose-response:
/// forward returns should rise steadily across the deciles. Monotonicity is much harder to
/// produce by luck than a single favourable contrast, and it is exactly the property a scaling
/// plan needs in order to make sense — if returns are not ordered by score, sizing by score is
/// just noise with extra steps.
/// </para>
///
/// <para>
/// The statistic is the Spearman rank correlation between score and forward return across all
/// bars. Its null comes from a permutation test: forward returns are shuffled against the scores,
/// which preserves both distributions exactly and destroys only the pairing. Bars overlap heavily
/// (a 20-bar forward return at bar i shares 19 bars with bar i+1), so the permutation is run on
/// NON-OVERLAPPING samples to avoid the autocorrelation inflating significance.
/// </para>
/// </summary>
public static class FavourabilityCommand
{
    private const int ForwardBars = 20;
    private const int Deciles = 10;

    private sealed record Sample(double Score, double ForwardAtr, int BarIndex, string Asset);

    /// <summary>Which inputs feed the score. "price" = the original structure/position/stretch
    /// mix; "sentiment" = Fear &amp; Greed contrarian; "funding" = perpetual funding contrarian;
    /// "oi" = open-interest change; "all" = every available input averaged.</summary>
    public static Task<int> RunAsync(string snapshotDir, string? only, string tf, int permutations,
        string mode = "price")
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var analyzer = new SwingStructureAnalyzer();
        var samples = new List<Sample>();

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 400) continue;

            var structure = analyzer.Analyze(bars, new SwingOptions(Span: 5, MinSwingAtr: 1.0));
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            var fng = LoadSeries(snapshotDir, "xs_alternativeme_fng_index_1d.json");
            var funding = LoadSeries(snapshotDir, $"xs_binancevision_{Base(snap.Symbol)}usdt_funding_8h.json");
            var oi = LoadSeries(snapshotDir, $"xs_binancevision_{Base(snap.Symbol)}usdt_oi_1d.json");

            for (int i = 60; i < bars.Count - ForwardBars; i++)
            {
                double a = atr[i];
                if (double.IsNaN(a) || a <= 0) continue;

                double score = BuildScore(mode, bars, structure, i, fng, funding, oi);
                if (double.IsNaN(score)) continue;

                // Forward move normalised by ATR so assets and eras are comparable.
                double fwd = (bars[i + ForwardBars].Close - bars[i].Close) / a;
                samples.Add(new Sample(score, fwd, i, snap.Symbol));
            }
        }

        Report(samples, tf, permutations, mode);
        return Task.FromResult(0);
    }

    /// <summary>
    /// Combines the requested inputs into one 0..1 long-favourability score. Every input is
    /// oriented the same way: higher = more favourable for a long. Inputs that have no data at
    /// this bar are simply skipped rather than defaulted, so an asset without funding history
    /// contributes its price terms honestly instead of a fabricated 0.5.
    /// </summary>
    private static double BuildScore(string mode, IReadOnlyList<Ohlcv> bars, SwingStructure s, int i,
        List<(long Ts, double V)>? fng, List<(long Ts, double V)>? funding, List<(long Ts, double V)>? oi)
    {
        var parts = new List<double>();
        long ts = new DateTimeOffset(DateTime.SpecifyKind(bars[i].Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        if (mode is "price" or "all") parts.Add(LongFavourability(bars, s, i));

        // Sentiment is CONTRARIAN: Fear & Greed near 0 (extreme fear) is the favourable end.
        if (mode is "sentiment" or "all")
        {
            double v = At(fng, ts);
            if (!double.IsNaN(v)) parts.Add(1.0 - Math.Clamp(v / 100.0, 0, 1));
        }

        // Funding is CONTRARIAN too: deeply positive funding means crowded longs paying to stay
        // in, which is the unfavourable end for a fresh long.
        if (mode is "funding" or "all")
        {
            double v = At(funding, ts);
            if (!double.IsNaN(v)) parts.Add(1.0 - Math.Clamp((v + 0.05) / 0.10, 0, 1));
        }

        // Open interest: falling OI after a decline = positions flushed = more favourable.
        if (mode is "oi" or "all")
        {
            double now = At(oi, ts);
            double then = At(oi, ts - 7L * 86400_000L);
            if (!double.IsNaN(now) && !double.IsNaN(then) && then > 0)
                parts.Add(1.0 - Math.Clamp((now / then - 0.9) / 0.4, 0, 1));
        }

        return parts.Count == 0 ? double.NaN : parts.Average();
    }

    /// <summary>Most recent series value at or before <paramref name="ts"/>, or NaN.</summary>
    private static double At(List<(long Ts, double V)>? series, long ts)
    {
        if (series == null || series.Count == 0) return double.NaN;
        int lo = 0, hi = series.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (series[mid].Ts <= ts) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best < 0 ? double.NaN : series[best].V;
    }

    private static string Base(string symbol) =>
        symbol.Split('/')[0].ToLowerInvariant();

    private static List<(long Ts, double V)>? LoadSeries(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Points", out var pts)) return null;
            var list = new List<(long, double)>(pts.GetArrayLength());
            foreach (var p in pts.EnumerateArray())
                list.Add((p.GetProperty("Ts").GetInt64(), p.GetProperty("Value").GetDouble()));
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
        catch { return null; }
    }

    /// <summary>
    /// Long favourability in 0..1. Built only from information available at bar i.
    ///
    /// Three ingredients, all expressing the same idea in different registers: the lower price
    /// sits inside its own recent structure, the more favourable a long becomes.
    ///   • Position in the confirmed swing range — 1 at the swing low, 0 at the swing high.
    ///   • Structure state — a downtrend scores higher, because that is where the exhaustion
    ///     signal has something to revert from (see the confluence study).
    ///   • Stretch below the 20-period mean in ATR — how far price has actually travelled.
    /// </summary>
    private static double LongFavourability(IReadOnlyList<Ohlcv> bars, SwingStructure s, int i)
    {
        double hi = s.LastHighPrice[i], lo = s.LastLowPrice[i];
        double close = bars[i].Close;

        double posScore;
        if (double.IsNaN(hi) || double.IsNaN(lo) || hi <= lo) posScore = 0.5;
        else posScore = 1.0 - Math.Clamp((close - lo) / (hi - lo), 0, 1);

        double stateScore = s.StatePerBar[i] switch
        {
            StructureState.Downtrend => 1.0,
            StructureState.Range => 0.5,
            StructureState.Uptrend => 0.0,
            _ => 0.5
        };

        double mean = 0;
        for (int k = i - 19; k <= i; k++) mean += bars[k].Close;
        mean /= 20;
        double atrLocal = Math.Abs(bars[i].High - bars[i].Low) + 1e-9;
        double stretch = Math.Clamp((mean - close) / (atrLocal * 3), -1, 1);
        double stretchScore = (stretch + 1) / 2;

        return (posScore + stateScore + stretchScore) / 3.0;
    }

    private static void Report(List<Sample> samples, string tf, int permutations, string mode)
    {
        Console.WriteLine();
        Console.WriteLine($"===== FAVOURABILITY GRADIENT ({tf}, mode={mode}) — {samples.Count:N0} bars =====");
        Console.WriteLine($"Score 0..1 (higher = more favourable for a long). Forward return measured");
        Console.WriteLine($"{ForwardBars} bars ahead in ATR units. A scaling plan needs this to be MONOTONIC.");
        Console.WriteLine();

        if (samples.Count < 500) { Console.WriteLine("Too few samples."); return; }

        var ordered = samples.OrderBy(s => s.Score).ToList();
        int per = ordered.Count / Deciles;

        Console.WriteLine($"  {"decile",7} {"score",12} {"n",7} {"mean fwd ATR",14} {"win%",7}");
        for (int d = 0; d < Deciles; d++)
        {
            var g = ordered.Skip(d * per).Take(d == Deciles - 1 ? int.MaxValue : per).ToList();
            if (g.Count == 0) continue;
            Console.WriteLine($"  {d + 1,7} {g.Min(x => x.Score):0.00}–{g.Max(x => x.Score):0.00}   {g.Count,7} " +
                              $"{g.Average(x => x.ForwardAtr),14:+0.0000;-0.0000;0} {g.Count(x => x.ForwardAtr > 0) / (double)g.Count,6:P0}");
        }

        // Non-overlapping subsample: one bar per ForwardBars, so forward windows never share bars.
        var independent = samples.Where(s => s.BarIndex % ForwardBars == 0).ToList();
        double rho = Spearman(independent.Select(x => x.Score).ToArray(),
                              independent.Select(x => x.ForwardAtr).ToArray());

        var rng = new Random(777);
        var scores = independent.Select(x => x.Score).ToArray();
        var fwds = independent.Select(x => x.ForwardAtr).ToArray();
        int extreme = 0;
        for (int p = 0; p < permutations; p++)
        {
            var shuffled = (double[])fwds.Clone();
            for (int k = shuffled.Length - 1; k > 0; k--)
            {
                int j = rng.Next(k + 1);
                (shuffled[k], shuffled[j]) = (shuffled[j], shuffled[k]);
            }
            if (Math.Abs(Spearman(scores, shuffled)) >= Math.Abs(rho)) extreme++;
        }
        double pValue = (extreme + 1.0) / (permutations + 1.0);

        Console.WriteLine();
        Console.WriteLine($"  Non-overlapping samples: {independent.Count:N0} (one per {ForwardBars} bars)");
        Console.WriteLine($"  Spearman rank correlation score vs forward return: {rho:+0.0000;-0.0000;0}");
        Console.WriteLine($"  Permutation p = {pValue:0.0000}");
        Console.WriteLine(pValue <= 0.05
            ? "  → the gradient is real: scaling by score is justified."
            : "  → no reliable gradient: scaling by this score is not justified.");
    }

    private static double Spearman(double[] a, double[] b)
    {
        if (a.Length < 3) return double.NaN;
        var ra = Rank(a); var rb = Rank(b);
        double ma = ra.Average(), mb = rb.Average();
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < ra.Length; i++)
        {
            double x = ra[i] - ma, y = rb[i] - mb;
            num += x * y; da += x * x; db += y * y;
        }
        return da <= 0 || db <= 0 ? 0 : num / Math.Sqrt(da * db);
    }

    private static double[] Rank(double[] v)
    {
        var idx = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
        var r = new double[v.Length];
        for (int k = 0; k < idx.Length; k++) r[idx[k]] = k;
        return r;
    }
}
