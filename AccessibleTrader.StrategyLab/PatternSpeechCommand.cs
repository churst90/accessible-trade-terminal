using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// How often does chart-pattern narration actually speak, and what does it say?
///
/// <para>
/// This is an <b>instrument, not a study</b> — it tests nothing about the market. It exists because
/// the two real defects in this feature so far were both invisible to unit tests and both obvious
/// the moment the behaviour was measured across real bars:
/// </para>
/// <list type="number">
///   <item>The relevance window silently collapsed to a single bar, so every pattern was announced
///         once, on one bar, and never when the user panned into the region it described. Every
///         assertion passed; coverage came out at exactly one bar per pattern.</item>
///   <item>The readout re-fired every time the overlapping set churned, so the user heard a
///         different pile of formations every few bars with no way to tell which were new.</item>
/// </list>
///
/// <para>
/// Both are properties of a <i>rate</i>, and a rate is not something a unit test naturally asserts.
/// So this walks a real series bar by bar exactly as the arrow keys do, counts what would have been
/// spoken, and prints the distribution. The numbers to watch:
/// </para>
/// <list type="bullet">
///   <item><b>Speech rate</b> — the share of bars that produce an announcement. Near 0% means the
///         feature is dead. Above roughly 10% means it is chatter, and a user will switch it off,
///         at which point it protects nobody.</item>
///   <item><b>Announcements per pattern</b> — should be close to 2 by construction: one on entry,
///         one at the resolution. Materially above that means the edge detection is re-firing.</item>
///   <item><b>Outcome mix</b> — confirmed versus expired. If nothing ever expires, the bound on the
///         resolve scan is not working and unrelated breaks are being credited to old shapes.</item>
/// </list>
/// </summary>
internal static class PatternSpeechCommand
{
    public static int Run(string snapshotDir, string? only, string timeframe)
    {
        var files = Directory.GetFiles(snapshotDir, $"*_{timeframe}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("events_", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("fred_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
        {
            Console.Error.WriteLine($"No snapshots matched in '{snapshotDir}' (tf={timeframe}).");
            return 1;
        }

        var detector = new ChartPatternDetector(new SwingStructureAnalyzer());

        Console.WriteLine();
        Console.WriteLine("Chart-pattern narration, measured by walking every bar as the arrow keys do.");
        Console.WriteLine();
        Console.WriteLine($"{"symbol",-22}{"bars",8}{"patts",8}{"spoken",8}{"rate",8}{"per-pat",9}{"detect",10}   outcome mix");
        Console.WriteLine(new string('-', 100));

        int totalBars = 0, totalSpoken = 0, totalPatterns = 0;
        var outcomes = new Dictionary<ChartPatternState, int>();
        // Per-kind counts: a shape that is defined but never actually found is indistinguishable
        // from one that is not implemented, and the range was exactly that for a while.
        var kinds = new Dictionary<ChartPatternKind, int>();

        foreach (var path in files)
        {
            var snap = SnapshotCommand.Load(path);
            var bars = snap.Bars;
            if (bars.Count < 60) continue;

            // Timed separately from the narration walk below. Detection runs ONCE per loaded
            // dataset (it is cached), so this is the number that decides whether turning the
            // feature on stalls a chart load. The walk is a measurement artifact — it re-scans
            // every pattern on every bar, which nothing in the product ever does.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var found = detector.Detect(bars);
            clock.Stop();
            long detectMs = clock.ElapsedMilliseconds;

            foreach (var p in found)
            {
                outcomes[p.State] = outcomes.GetValueOrDefault(p.State) + 1;
                kinds[p.Kind] = kinds.GetValueOrDefault(p.Kind) + 1;
            }

            int spoken = 0;
            var samples = new List<string>();

            // Exactly the coordinator's edge diff: what changed between bar i-1 and bar i.
            for (int i = 1; i < bars.Count; i++)
            {
                var here = ChartPatternNarrator.AtBar(found, i);
                var beforeKeys = ChartPatternNarrator.AtBar(found, i - 1).Select(p => p.Key).ToHashSet();

                var entered = ChartPatternNarrator.ByDominance(here.Where(p => !beforeKeys.Contains(p.Key))).ToList();
                var enteredKeys = entered.Select(p => p.Key).ToHashSet();
                var resolved = here.Where(p => p.ResolvesAt == i && !enteredKeys.Contains(p.Key)).ToList();

                if (entered.Count == 0 && resolved.Count == 0) continue;
                spoken++;

                if (samples.Count < 3)
                {
                    if (entered.Count > 0)
                        samples.Add(ChartPatternNarrator.DescribeEntry(entered[0], i, Fmt));
                    else
                        samples.Add(ChartPatternNarrator.DescribeResolution(resolved[0], Fmt));
                }
            }

            totalBars += bars.Count;
            totalSpoken += spoken;
            totalPatterns += found.Count;

            double rate = 100.0 * spoken / bars.Count;
            double perPattern = found.Count == 0 ? 0 : (double)spoken / found.Count;
            string mix = string.Join(" ", found.GroupBy(p => p.State)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key.ToString()[..4].ToLowerInvariant()}={g.Count()}"));

            Console.WriteLine($"{snap.Symbol,-22}{bars.Count,8}{found.Count,8}{spoken,8}{rate,7:F1}%{perPattern,9:F2}{detectMs,8}ms   {mix}");

            foreach (var s in samples) Console.WriteLine($"        \"{s}\"");
        }

        Console.WriteLine(new string('-', 100));
        double overall = totalBars == 0 ? 0 : 100.0 * totalSpoken / totalBars;
        double per = totalPatterns == 0 ? 0 : (double)totalSpoken / totalPatterns;

        Console.WriteLine($"{"ALL",-22}{totalBars,8}{totalPatterns,8}{totalSpoken,8}{overall,7:F1}%{per,9:F2}");
        Console.WriteLine();
        Console.WriteLine("── READING ──");
        Console.WriteLine($"  Speech rate {overall:F1}% of bars. Near 0 means the feature is dead;");
        Console.WriteLine( "  much above 10% means it is chatter and will be switched off.");
        Console.WriteLine($"  {per:F2} announcements per pattern. Two is the design — entry and resolution.");
        foreach (var kv in outcomes.OrderBy(k => k.Key))
            Console.WriteLine($"  {kv.Key,-10} {kv.Value,6}");
        Console.WriteLine( "  No Expired at all would mean the resolve scan is unbounded again, and old");
        Console.WriteLine( "  shapes are being credited with unrelated breaks.");
        Console.WriteLine();
        Console.WriteLine("── SHAPES FOUND ──");
        foreach (ChartPatternKind k in Enum.GetValues<ChartPatternKind>())
        {
            int n = kinds.GetValueOrDefault(k);
            Console.WriteLine($"  {ChartPatternNarrator.Name(k),-28}{n,7}{(n == 0 ? "   <-- never found" : "")}");
        }
        Console.WriteLine();

        return 0;
    }

    private static string Fmt(double v) => v.ToString("0.####");
}
