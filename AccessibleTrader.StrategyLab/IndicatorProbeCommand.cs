using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Runs an indicator against a snapshot at several BAR COUNTS and reports how many non-NaN
/// values each component actually produces.
///
/// <para>
/// This exists because of a real failure: Value Deviation shipped with a 480-bar profile window
/// chosen from the research dataset, while a fresh chart loads about 200 bars. Every component
/// was therefore entirely NaN and every component read "no data" — which looks identical to a
/// broken indicator, and for a screen-reader user there is no chart to glance at that would say
/// otherwise. Sizing a default against research data instead of against what the app actually
/// loads is an easy mistake to repeat, so this makes it checkable in one command.
/// </para>
/// </summary>
public static class IndicatorProbeCommand
{
    public static Task<int> RunAsync(string snapshotPath, string code, string? paramSpec)
    {
        SnapshotFile snap;
        try { snap = SnapshotCommand.Load(snapshotPath); }
        catch (Exception ex) { Console.Error.WriteLine($"Load failed: {ex.Message}"); return Task.FromResult(1); }

        IIndicatorProvider provider = code.ToUpperInvariant() switch
        {
            ValueDeviationProvider.Code => new ValueDeviationProvider(),
            SwingStructureProvider.Code => new SwingStructureProvider(),
            _ => new ValueDeviationProvider()
        };

        var parameters = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(paramSpec))
            foreach (var pair in paramSpec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=');
                if (kv.Length == 2 && double.TryParse(kv[1], out double v)) parameters[kv[0].Trim()] = v;
            }

        var meta = provider.GetIndicators()[0];
        Console.WriteLine($"===== {meta.Name} on {snap.Symbol} {snap.Timeframe} =====");
        Console.WriteLine($"Snapshot has {snap.Bars.Count:N0} bars. " +
                          (parameters.Count > 0 ? $"Params: {string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}" : "Defaults."));
        Console.WriteLine();

        // Bar counts a real chart is plausibly showing, from the default fetch upward.
        int[] counts = { 200, 300, 500, 600, 1000, 2000 };
        var names = meta.Components.Select(c => c.Name).ToList();

        Console.Write($"  {"bars",6}");
        foreach (var n in names) Console.Write($" {Short(n),12}");
        Console.WriteLine();

        foreach (int count in counts)
        {
            if (count > snap.Bars.Count) continue;
            var slice = snap.Bars.Skip(snap.Bars.Count - count).Take(count).ToArray();

            var buffer = new ProbeBuffer(count);
            provider.Calculate(meta.Code, slice, new Dictionary<string, object>(parameters), buffer);

            Console.Write($"  {count,6}");
            foreach (var n in names)
            {
                int nonNan = buffer.Data.TryGetValue(n, out var arr)
                    ? arr.Count(v => !double.IsNaN(v)) : 0;
                Console.Write($" {nonNan,12}");
            }
            Console.WriteLine();
        }

        // Stage-by-stage funnel so a zero can be attributed to a specific filter rather than guessed at.
        if (code.ToUpperInvariant() == ValueDeviationProvider.Code)
        {
            var fb = snap.Bars.Skip(Math.Max(0, snap.Bars.Count - 2000)).ToList();
            var an = new AccessibleTrader.Core.Services.Analysis.ValueDeviationAnalyzer();
            var devs = an.Analyze(fb, 480, 5, 2.0);

            int withTier = devs.Count(d => d.Tier > 0);
            int below = devs.Count(d => d.Tier > 0 && d.BelowValue);
            int above = devs.Count(d => d.Tier > 0 && !d.BelowValue);
            int bullRev = 0, bearRev = 0, bullBoth = 0, bearBoth = 0;
            for (int i = 1; i < fb.Count; i++)
            {
                double r = fb[i].High - fb[i].Low;
                if (r <= 0) continue;
                bool bull = fb[i].Low < fb[i - 1].Low && fb[i].Close > fb[i].Open && (fb[i].Close - fb[i].Low) / r > 0.5;
                bool bear = fb[i].High > fb[i - 1].High && fb[i].Close < fb[i].Open && (fb[i].High - fb[i].Close) / r > 0.5;
                if (bull) bullRev++;
                if (bear) bearRev++;
                if (bull && devs[i].Tier > 0 && devs[i].BelowValue) bullBoth++;
                if (bear && devs[i].Tier > 0 && !devs[i].BelowValue) bearBoth++;
            }

            Console.WriteLine();
            Console.WriteLine("  FUNNEL (last 2000 bars, window 480):");
            Console.WriteLine($"    bars with a tier                : {withTier}");
            Console.WriteLine($"      below value                   : {below}");
            Console.WriteLine($"      above value                   : {above}");
            Console.WriteLine($"    bullish reversal bars           : {bullRev}");
            Console.WriteLine($"    bearish reversal bars           : {bearRev}");
            Console.WriteLine($"    below-value AND bullish reversal : {bullBoth}");
            Console.WriteLine($"    above-value AND bearish reversal : {bearBoth}");
        }

        Console.WriteLine();
        Console.WriteLine("  A column of zeros at a realistic bar count means the indicator is silent");
        Console.WriteLine("  in normal use, which reads to the user as broken rather than as 'no signal'.");
        return Task.FromResult(0);
    }

    private static string Short(string s) => s.Length <= 12 ? s : s[..12];

    private sealed class ProbeBuffer : IIndicatorResultBuffer
    {
        public readonly Dictionary<string, double[]> Data = new();
        private readonly int _n;
        public ProbeBuffer(int n) => _n = n;

        public Span<double> GetComponentSpan(string name)
        {
            if (!Data.TryGetValue(name, out var a)) { a = new double[_n]; Data[name] = a; }
            return a;
        }
        public void SetValue(string name, int i, double v) => GetComponentSpan(name)[i] = v;
        public void WriteZoneBands(string code, List<ZoneBandConfig> z) { }
        public IReadOnlyList<ZoneBandConfig> ReadZoneBands(string code) => Array.Empty<ZoneBandConfig>();
    }
}
