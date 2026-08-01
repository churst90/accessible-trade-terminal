using AccessibleTrader.Sdk.Models;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Event study on the scheduled US macro releases — CPI, Non-Farm Payrolls, PPI and GDP — using
/// REAL release dates pulled from the FRED API.
///
/// <para>
/// These are the events the FOMC study could not reach: BLS blocks automated requests and the FRED
/// web UI is unreachable from here, so the dates were unavailable until an API key existed. They
/// were never reconstructed from memory — <see cref="FomcCommand"/> records why that matters.
/// </para>
///
/// <para>
/// WHY EVENTS ARE WORTH THE EFFORT. A release date is exogenous: fixed months in advance, public,
/// and carrying no asymmetry about <i>when</i>. A calendar date cannot be a repackaged price
/// transform, which is the failure mode that killed crowding, volume-as-conditioner and MVRV.
/// </para>
///
/// <para>
/// THE CONTROL. CPI and PPI land mid-month on weekdays; NFP is almost always the first Friday.
/// Both facts would leak into a naive "event days vs all days" comparison, because this lab has
/// already measured a weekday effect in SPY and a turn-of-month effect in both SPY and BTC. The
/// random control therefore draws from the <b>same weekday distribution</b>, and the day-of-month
/// profile of each release is printed so a calendar artifact is visible rather than hidden.
/// </para>
///
/// <para>
/// The FOMC study found a real decision-day drift that had largely been arbitraged away since
/// publication. The same question applies here: if an effect exists, has it survived being known?
/// </para>
/// </summary>
public static class MacroEventCommand
{
    private sealed class EventRow { public string date { get; set; } = ""; }

    private const int RandomDraws = 2000;

    public static int Run(string snapshotDir, string tf, int permutations)
    {
        var releases = new (string File, string Label)[]
        {
            ("events_cpi.json", "CPI"),
            ("events_nfp.json", "NFP"),
            ("events_ppi.json", "PPI"),
            ("events_gdp.json", "GDP"),
        };

        var targets = new (string Pat, string Label)[]
        {
            ("yahoo_SPY", "SPY"), ("yahoo_QQQ", "QQQ"), ("yahoo_TLT", "TLT"),
            ("yahoo_GLD", "GLD"), ("bitstamp_BTC_USDT", "BTC"),
        };

        Console.WriteLine();
        Console.WriteLine("===== MACRO RELEASE EVENT STUDY (real FRED dates) =====");
        Console.WriteLine("Random control samples the SAME weekday distribution as the real release dates.");
        Console.WriteLine();

        var grid = new List<(string Rel, string Sym, double D0, double P0, double D1, double P1)>();

        foreach (var (file, rel) in releases)
        {
            var path = Path.Combine(snapshotDir, file);
            if (!File.Exists(path)) { Console.WriteLine($"  {rel}: no {file} — fetch it first."); continue; }

            var dates = (JsonConvert.DeserializeObject<List<EventRow>>(File.ReadAllText(path)) ?? new())
                .Select(r => DateTime.Parse(r.date)).Where(d => d.Year >= 1990).OrderBy(d => d).ToHashSet();
            if (dates.Count < 60) continue;

            Console.WriteLine($"  ══════ {rel} — {dates.Count} releases {dates.Min():yyyy-MM} → {dates.Max():yyyy-MM} ══════");
            var dow = dates.GroupBy(d => d.DayOfWeek).OrderByDescending(g => g.Count());
            Console.WriteLine($"    weekday: {string.Join(", ", dow.Select(g => $"{g.Key.ToString()[..3]} {g.Count()}"))}");
            Console.WriteLine($"    day of month: median {dates.Select(d => d.Day).OrderBy(x => x).ElementAt(dates.Count / 2)}, " +
                              $"range {dates.Min(d => d.Day)}–{dates.Max(d => d.Day)}");
            Console.WriteLine($"    {"asset",6} {"t−1",9} {"p",7} {"t 0",9} {"p",7} {"t+1",9} {"p",7}");

            foreach (var (pat, sym) in targets)
            {
                var f = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
                    .Where(x => !Path.GetFileName(x).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(x => Path.GetFileName(x).Contains(pat, StringComparison.OrdinalIgnoreCase));
                if (f == null) continue;

                SnapshotFile snap;
                try { snap = SnapshotCommand.Load(f); } catch { continue; }
                var bars = snap.Bars;
                if (bars.Count < 500) continue;

                var r = Measure(bars, dates, permutations);
                if (r == null) continue;
                Console.WriteLine($"    {sym,6} {r.Value.M1,9:+0.000%;-0.000%;0} {r.Value.P1,7:0.000}{(r.Value.P1 <= 0.05 ? "*" : " ")} " +
                                  $"{r.Value.M0,9:+0.000%;-0.000%;0} {r.Value.P0,7:0.000}{(r.Value.P0 <= 0.05 ? "*" : " ")} " +
                                  $"{r.Value.Mp1,9:+0.000%;-0.000%;0} {r.Value.Pp1,7:0.000}{(r.Value.Pp1 <= 0.05 ? "*" : " ")}");
                grid.Add((rel, sym, r.Value.M0, r.Value.P0, r.Value.M1, r.Value.P1));
            }
            Console.WriteLine();
        }

        Verdict(grid, releases.Length, targets.Length);
        return 0;
    }

    private static (double M1, double P1, double M0, double P0, double Mp1, double Pp1)?
        Measure(List<Ohlcv> bars, HashSet<DateTime> dates, int permutations)
    {
        var rets = new double[bars.Count];
        for (int i = 1; i < bars.Count; i++)
            rets[i] = bars[i].Close > 0 && bars[i - 1].Close > 0 ? Math.Log(bars[i].Close / bars[i - 1].Close) : 0;

        var idxOf = new Dictionary<DateTime, int>();
        for (int i = 0; i < bars.Count; i++) idxOf[bars[i].Date.Date] = i;

        // A release on a market holiday maps forward to the next trading bar rather than vanishing.
        int Locate(DateTime d)
        {
            for (int k = 0; k < 6; k++) if (idxOf.TryGetValue(d.Date.AddDays(k), out int i)) return i;
            return -1;
        }

        var ev = dates.Select(Locate).Where(i => i > 5 && i < bars.Count - 5).ToList();
        if (ev.Count < 40) return null;

        var wd = ev.GroupBy(i => bars[i].Date.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());
        var pool = Enumerable.Range(6, bars.Count - 12)
            .GroupBy(i => bars[i].Date.DayOfWeek).ToDictionary(g => g.Key, g => g.ToList());

        (double Mean, double P) At(int off)
        {
            var vals = ev.Where(i => i + off >= 1 && i + off < bars.Count).Select(i => rets[i + off]).ToList();
            double observed = vals.Average();
            var rng = new Random(1618 + off);
            int runs = Math.Min(permutations, RandomDraws), extreme = 0;
            double acc = 0;
            for (int r = 0; r < runs; r++)
            {
                double sum = 0; int n = 0;
                foreach (var (day, count) in wd)
                {
                    if (!pool.TryGetValue(day, out var p) || p.Count == 0) continue;
                    for (int k = 0; k < count; k++)
                    {
                        int i = p[rng.Next(p.Count)];
                        if (i + off >= 1 && i + off < bars.Count) { sum += rets[i + off]; n++; }
                    }
                }
                if (n == 0) continue;
                double m = sum / n;
                acc += m;
                if (Math.Abs(m) >= Math.Abs(observed)) extreme++;
            }
            double rnd = acc / runs;
            return (observed - rnd, (extreme + 1.0) / (runs + 1));
        }

        var a = At(-1); var b = At(0); var c = At(1);
        return (a.Mean, a.P, b.Mean, b.P, c.Mean, c.P);
    }

    private static void Verdict(List<(string Rel, string Sym, double D0, double P0, double D1, double P1)> grid,
        int releases, int assets)
    {
        Console.WriteLine("  ── VERDICT ──");
        if (grid.Count == 0) { Console.WriteLine("    nothing measured"); return; }

        int sig0 = grid.Count(g => g.P0 <= 0.05);
        int sig1 = grid.Count(g => g.P1 <= 0.05);
        int tests = grid.Count * 3;

        Console.WriteLine($"    Significant at p≤0.05: release day {sig0}/{grid.Count}, day before {sig1}/{grid.Count}.");
        Console.WriteLine($"    {tests} tests run ({grid.Count} release×asset pairs × 3 offsets); ~{tests * 0.05:0.0} false");
        Console.WriteLine($"    positives expected by chance. Bonferroni threshold ≈ {0.05 / Math.Max(1, tests):0.00000}.");
        Console.WriteLine();

        foreach (var g in grid.Where(g => g.P0 <= 0.05 || g.P1 <= 0.05).OrderBy(g => Math.Min(g.P0, g.P1)))
            Console.WriteLine($"      {g.Rel} / {g.Sym}: day0 {g.D0:+0.000%;-0.000%;0} (p={g.P0:0.000})   " +
                              $"day−1 {g.D1:+0.000%;-0.000%;0} (p={g.P1:0.000})");

        Console.WriteLine();
        Console.WriteLine("    What would count as real: the SAME offset significant across several assets that");
        Console.WriteLine("    do not share a sampling error — as the FOMC decision day was, on four US equity");
        Console.WriteLine("    vehicles at once. Scattered single cells at p≈0.04 are what chance looks like.");
    }
}
