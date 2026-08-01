using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Does a bullish engulfing weekly candle predict the next week?
///
/// <para>
/// THE CLAIM, from the 2026-08-01 trader's-triangle interview: "if I see that the previous weekly
/// candle is bullish engulfing it's more likely than not that the next week is also going to have
/// more bullishness than bearishness … is there a chance the next candle is bearish? yes, but it's
/// way lower chance." It is the cheapest testable claim in that video — one direction, weekly bars,
/// a stated prior, and no discretionary input anywhere.
/// </para>
///
/// <para>
/// TWO CONTROLS, and the second is the one that decides it.
/// </para>
/// <list type="number">
///   <item>
///     <b>A random-week null.</b> Draw the same number of weeks at random from the same series and
///     measure their forward up-rate. Repeated many times this gives the distribution of up-rates a
///     rule with no information would produce, which is the honest comparison — not 50%. An asset
///     that closes up 55% of weeks unconditionally makes a 55% conditional hit rate worthless.
///   </item>
///   <item>
///     <b>A cheap alternative that does the same job:</b> "last week simply closed up". Engulfing is
///     a momentum pattern with extra conditions attached. If plain up-weeks predict just as well,
///     the engulfing part is decoration and the finding is momentum, which we have already measured
///     elsewhere. This is the control that has killed the most claims in this project.
///   </item>
/// </list>
///
/// <para>
/// Hit rate alone can mislead, so mean forward return is reported beside it: a rule can win more
/// often and lose more when it loses.
/// </para>
/// </summary>
public static class WeeklyPersistenceCommand
{
    private sealed record Weekly(DateTime Date, double Open, double High, double Low, double Close);

    private sealed record ArmResult(string Name, int N, double UpRate, double MeanFwd, double PRandom);

    private sealed record AssetResult(
        string Asset, string Class, int Weeks, double BaseUpRate, double BaseMeanFwd,
        ArmResult BullEngulf, ArmResult BullPlain, ArmResult BearEngulf, ArmResult BearPlain,
        bool[] Up, List<int> BearEngulfIdx, List<int> BullEngulfIdx);

    public static int Run(string snapshotDir, string? only, int permutations = 5000)
    {
        if (!Directory.Exists(snapshotDir))
        {
            Console.Error.WriteLine($"No snapshot directory at {snapshotDir}");
            return 1;
        }

        var files = Directory.GetFiles(snapshotDir, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .Where(f => Path.GetFileName(f).EndsWith("_1w.json") || Path.GetFileName(f).EndsWith("_1d.json"))
            .OrderBy(f => f)
            .ToList();

        // One instrument per line. SPY and QQQ exist under two providers; counting both would
        // double-weight them and narrow every p-value on correlated errors.
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            var parts = name.Split('_');
            string provider = parts[0];
            string tf = parts[^1];
            string symbol = string.Join('_', parts[1..^1]);
            if (only != null && !symbol.Contains(only, StringComparison.OrdinalIgnoreCase)) continue;

            // Prefer a native weekly series; fall back to aggregating a daily one.
            bool better = !chosen.TryGetValue(symbol, out var existing)
                          || (tf == "1w" && !existing.EndsWith("_1w.json"))
                          || (Path.GetFileName(existing).StartsWith("twelvedata") && provider == "yahoo");
            if (better) chosen[symbol] = f;
        }

        var results = new List<AssetResult>();
        foreach (var (symbol, file) in chosen.OrderBy(kv => kv.Key))
        {
            var snap = SnapshotCommand.Load(file);
            var weeks = Path.GetFileName(file).EndsWith("_1w.json")
                ? snap.Bars.Select(b => new Weekly(b.Date, b.Open, b.High, b.Low, b.Close)).ToList()
                : ToWeekly(snap.Bars);

            if (weeks.Count < 120) continue;     // ~2.5 years of weeks; below that the arms are noise
            results.Add(Analyse(symbol, ClassOf(file), weeks, permutations));
        }

        if (results.Count == 0)
        {
            Console.Error.WriteLine("No instrument had enough weekly history.");
            return 1;
        }

        Report(results, permutations);
        return 0;
    }

    // ── The test ────────────────────────────────────────────────────────────────

    private static AssetResult Analyse(string asset, string cls, List<Weekly> w, int permutations)
    {
        // Forward return of the week AFTER the signal week. Index i is the signal; i+1 is the outcome.
        int n = w.Count - 1;
        var fwd = new double[n];
        var up = new bool[n];
        for (int i = 0; i < n; i++)
        {
            fwd[i] = (w[i + 1].Close - w[i + 1].Open) / w[i + 1].Open;
            up[i] = w[i + 1].Close > w[i + 1].Open;
        }

        var bullEngulf = new List<int>();
        var bullPlain = new List<int>();
        var bearEngulf = new List<int>();
        var bearPlain = new List<int>();

        for (int i = 1; i < n; i++)
        {
            var prev = w[i - 1];
            var cur = w[i];
            bool curBull = cur.Close > cur.Open;
            bool prevBull = prev.Close > prev.Open;

            if (curBull) bullPlain.Add(i);
            else bearPlain.Add(i);

            // Engulfing: this body is the opposite colour to the last and completely covers it.
            bool bodyCovers = Math.Min(cur.Open, cur.Close) <= Math.Min(prev.Open, prev.Close)
                           && Math.Max(cur.Open, cur.Close) >= Math.Max(prev.Open, prev.Close);
            if (curBull && !prevBull && bodyCovers) bullEngulf.Add(i);
            if (!curBull && prevBull && bodyCovers) bearEngulf.Add(i);
        }

        double baseUp = up.Count(x => x) / (double)n;
        double baseFwd = fwd.Average();

        return new AssetResult(asset, cls, n, baseUp, baseFwd,
            Arm("bullish engulfing → up", bullEngulf, up, fwd, permutations, asset, wantUp: true),
            Arm("any up week → up", bullPlain, up, fwd, permutations, asset, wantUp: true),
            Arm("bearish engulfing → down", bearEngulf, up, fwd, permutations, asset, wantUp: false),
            Arm("any down week → down", bearPlain, up, fwd, permutations, asset, wantUp: false),
            up, bearEngulf, bullEngulf);
    }

    private static ArmResult Arm(string name, List<int> idx, bool[] up, double[] fwd,
                                 int permutations, string asset, bool wantUp)
    {
        if (idx.Count < 10) return new ArmResult(name, idx.Count, double.NaN, double.NaN, double.NaN);

        double rate = idx.Count(i => up[i] == wantUp) / (double)idx.Count;
        double mean = idx.Average(i => wantUp ? fwd[i] : -fwd[i]);

        // The random-week null: same number of weeks, drawn at random from the same series. Seeded
        // per asset so a re-run reproduces exactly — a p-value that moves between runs is not a
        // p-value.
        var rng = new Random(StableSeed(asset));
        int atLeastAsGood = 0;
        for (int p = 0; p < permutations; p++)
        {
            int hits = 0;
            for (int k = 0; k < idx.Count; k++)
                if (up[rng.Next(up.Length)] == wantUp) hits++;
            if (hits / (double)idx.Count >= rate) atLeastAsGood++;
        }

        return new ArmResult(name, idx.Count, rate, mean, (atLeastAsGood + 1.0) / (permutations + 1.0));
    }

/// <summary>
    /// A DETERMINISTIC seed from a string. string.GetHashCode() is randomised per process in .NET,
    /// so seeding a control with it makes the control resample on every run — and a p-value that
    /// moves between runs is not a p-value. This bit us: the same bucket read -5.6 and then -1.8 on
    /// two consecutive runs of the same code. FNV-1a, fixed forever.
    /// </summary>
    private static int StableSeed(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────

    private static List<Weekly> ToWeekly(IReadOnlyList<Ohlcv> bars)
    {
        var outp = new List<Weekly>();
        List<Ohlcv> cur = new();
        int curWeek = -1, curYear = -1;

        foreach (var b in bars)
        {
            var cal = System.Globalization.ISOWeek.GetWeekOfYear(b.Date);
            int year = System.Globalization.ISOWeek.GetYear(b.Date);
            if (cur.Count > 0 && (cal != curWeek || year != curYear))
            {
                outp.Add(Fold(cur));
                cur = new List<Ohlcv>();
            }
            curWeek = cal; curYear = year;
            cur.Add(b);
        }
        // The final partial week is dropped: its close is not a weekly close, and including it
        // would let an unfinished bar decide the newest signal.
        return outp;
    }

    private static Weekly Fold(List<Ohlcv> week) => new(
        week[0].Date, week[0].Open, week.Max(x => x.High), week.Min(x => x.Low), week[^1].Close);

    private static string ClassOf(string file)
    {
        string n = Path.GetFileName(file);
        if (n.StartsWith("bitstamp") || n.StartsWith("mexc")) return "crypto";
        return "equities";
    }

    private static void Report(List<AssetResult> r, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine("═════ WEEKLY ENGULFING → NEXT WEEK ═════");
        Console.WriteLine($"{r.Count} instruments · {r.Sum(x => x.Weeks):N0} weeks · {permutations:N0} random-week draws per arm");
        Console.WriteLine();
        Console.WriteLine("The claim: after a bullish engulfing weekly candle the next week is more likely up.");
        Console.WriteLine("Control 1: the same number of weeks drawn at RANDOM from the same series.");
        Console.WriteLine("Control 2: 'last week simply closed up' — the cheap alternative doing the same job.");
        Console.WriteLine();

        foreach (var cls in r.Select(x => x.Class).Distinct().OrderBy(x => x))
        {
            var g = r.Where(x => x.Class == cls).ToList();
            Console.WriteLine($"── {cls.ToUpperInvariant()} ({g.Count} instruments) " + new string('─', 40));
            Console.WriteLine($"{"instrument",-14}{"weeks",7}{"base up",9}{"engulf n",9}{"engulf up",11}{"lift",8}{"p",8}{"plain up",10}{"plain lift",11}");

            foreach (var a in g.OrderBy(x => x.Asset))
            {
                string lift = double.IsNaN(a.BullEngulf.UpRate) ? "  n/a" : $"{(a.BullEngulf.UpRate - a.BaseUpRate) * 100,+6:0.0}";
                string plainLift = double.IsNaN(a.BullPlain.UpRate) ? "  n/a" : $"{(a.BullPlain.UpRate - a.BaseUpRate) * 100,+6:0.0}";
                Console.WriteLine($"{a.Asset,-14}{a.Weeks,7}{a.BaseUpRate * 100,8:0.0}%{a.BullEngulf.N,9}"
                                + $"{(double.IsNaN(a.BullEngulf.UpRate) ? "n/a" : (a.BullEngulf.UpRate * 100).ToString("0.0") + "%"),11}"
                                + $"{lift,8}{(double.IsNaN(a.BullEngulf.PRandom) ? "n/a" : a.BullEngulf.PRandom.ToString("0.000")),8}"
                                + $"{(double.IsNaN(a.BullPlain.UpRate) ? "n/a" : (a.BullPlain.UpRate * 100).ToString("0.0") + "%"),10}{plainLift,11}");
            }
            Console.WriteLine();

            Pooled(g, cls, permutations);
        }

        Console.WriteLine("Reading it: 'lift' is the conditional up-rate minus that instrument's own unconditional");
        Console.WriteLine("up-rate. p is against the random-week null. 'plain lift' is the same lift for the cheap");
        Console.WriteLine("alternative — if it matches or beats the engulfing lift, the pattern adds nothing.");
    }

    /// <summary>
    /// Pooled random-week null. Each asset contributes a draw of its own signal count from its own
    /// week series, so the null preserves both the sample sizes and the per-asset base rates — the
    /// two things that would otherwise manufacture a pooled lift out of nothing.
    /// </summary>
    private static double PooledP(List<AssetResult> assets, bool bull, int permutations)
    {
        double observed = assets.Average(a => bull
            ? a.BullEngulf.UpRate - a.BaseUpRate
            : a.BearEngulf.UpRate - (1 - a.BaseUpRate));

        var rng = new Random(20260801);
        int atLeastAsGood = 0;
        for (int p = 0; p < permutations; p++)
        {
            double sum = 0;
            foreach (var a in assets)
            {
                int n = bull ? a.BullEngulf.N : a.BearEngulf.N;
                int hits = 0;
                for (int k = 0; k < n; k++)
                    if (a.Up[rng.Next(a.Up.Length)] == bull) hits++;
                sum += hits / (double)n - (bull ? a.BaseUpRate : 1 - a.BaseUpRate);
            }
            if (sum / assets.Count >= observed) atLeastAsGood++;
        }
        return (atLeastAsGood + 1.0) / (permutations + 1.0);
    }

    /// <summary>
    /// Lift recomputed on the first and second half of each asset's history, pooled. A finding that
    /// holds in one half and not the other is an era description, not an edge.
    /// </summary>
    private static string EraSplit(List<AssetResult> assets, bool bull)
    {
        double L1 = 0, L2 = 0; int n1 = 0, n2 = 0, count = 0;
        foreach (var a in assets)
        {
            var idx = bull ? a.BullEngulfIdx : a.BearEngulfIdx;
            int mid = a.Up.Length / 2;
            var first = idx.Where(i => i < mid).ToList();
            var second = idx.Where(i => i >= mid).ToList();
            if (first.Count < 5 || second.Count < 5) continue;

            double baseFirst = a.Up.Take(mid).Count(x => x == bull) / (double)mid;
            double baseSecond = a.Up.Skip(mid).Count(x => x == bull) / (double)(a.Up.Length - mid);

            L1 += first.Count(i => a.Up[i] == bull) / (double)first.Count - baseFirst;
            L2 += second.Count(i => a.Up[i] == bull) / (double)second.Count - baseSecond;
            n1 += first.Count; n2 += second.Count; count++;
        }
        if (count == 0) return "too few signals per half";
        return $"H1 {L1 / count * 100,+5:0.0} pts (n={n1}) · H2 {L2 / count * 100,+5:0.0} pts (n={n2}) · {count} instruments";
    }

    private static void Pooled(List<AssetResult> g, string cls, int permutations)
    {
        var withData = g.Where(a => !double.IsNaN(a.BullEngulf.UpRate)).ToList();
        if (withData.Count == 0) return;

        double engulfLift = withData.Average(a => a.BullEngulf.UpRate - a.BaseUpRate);
        double plainLift = withData.Where(a => !double.IsNaN(a.BullPlain.UpRate))
                                    .Average(a => a.BullPlain.UpRate - a.BaseUpRate);
        int positive = withData.Count(a => a.BullEngulf.UpRate > a.BaseUpRate);
        int sig = withData.Count(a => a.BullEngulf.PRandom < 0.05);

        var bearWith = g.Where(a => !double.IsNaN(a.BearEngulf.UpRate)).ToList();
        double bearLift = bearWith.Count == 0 ? double.NaN
            : bearWith.Average(a => a.BearEngulf.UpRate - (1 - a.BaseUpRate));

        Console.WriteLine($"  POOLED {cls}:");
        Console.WriteLine($"    bullish engulfing lift : {engulfLift * 100,+6:0.00} pts   ({positive}/{withData.Count} instruments positive)");
        Console.WriteLine($"    plain up-week lift     : {plainLift * 100,+6:0.00} pts   ← the control that matters");
        if (!double.IsNaN(bearLift))
        {
            double bearPlainLift = bearWith
                .Where(a => !double.IsNaN(a.BearPlain.UpRate))
                .Average(a => a.BearPlain.UpRate - (1 - a.BaseUpRate));
            Console.WriteLine($"    bearish engulfing lift : {bearLift * 100,+6:0.00} pts   (predicting a DOWN week)");
            Console.WriteLine($"    plain down-week lift   : {bearPlainLift * 100,+6:0.00} pts   ← its cheap alternative");
        }
        Console.WriteLine($"    instruments with p<0.05: {sig} of {withData.Count}   (expect ~{withData.Count * 0.05:0.#} by chance)");

        // A pooled null, because "N of M instruments positive" is not a test and the per-asset
        // p-values are individually underpowered. Redraw every asset's signal weeks at random,
        // recompute the SAME pooled average lift, and ask how often chance beats what we saw.
        Console.WriteLine($"    pooled p (bull engulf)  : {PooledP(withData, bull: true, permutations):0.000}");
        if (bearWith.Count > 0)
            Console.WriteLine($"    pooled p (bear engulf)  : {PooledP(bearWith, bull: false, permutations):0.000}");

        // ERA SPLIT. The control that has killed more findings in this project than any other:
        // a real effect should survive being cut in half chronologically. Anything that lives in
        // one era is a description of that era.
        Console.WriteLine($"    era split (bear engulf) : {EraSplit(bearWith, bull: false)}");
        Console.WriteLine($"    era split (bull engulf) : {EraSplit(withData, bull: true)}");
        Console.WriteLine();
    }
}
