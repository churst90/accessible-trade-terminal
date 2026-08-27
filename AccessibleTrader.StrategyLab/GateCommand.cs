using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Tests the "gate, not stack" thesis: that the Trading Cross z-state is worth more as a
/// risk-on/risk-off CONTEXT for other signals than as an entry trigger of its own.
///
/// <para>
/// The thesis came out of two results. The Trading Cross cleared an exposure-matched timing null
/// at p = 0.001 while making only 70 trades in 15 years — an exposure decision wearing a trade's
/// clothes. And <see cref="ConfluenceCommand"/> found that stacking confirmations (Cipher B +
/// structure + SR level) added nothing. If layering signals does not pay but the exposure call
/// does, the natural architecture is one layer deciding whether the others are allowed to speak.
/// </para>
///
/// <para>
/// THE TRAP THIS COMMAND IS BUILT AROUND. The obvious test — "do Cipher B longs earn more when the
/// gate is open?" — is close to guaranteed to say yes, and to mean nothing. The gate is open when
/// price has been rising, and longs earn more when price is rising. That is not the gate carrying
/// information; it is the gate being a trend filter. So every result here is reported alongside
/// the same measurement using a plain 200-bar moving average as the gate instead. If the two lifts
/// match, the Trading Cross state is a moving average with extra steps and should not be shipped
/// as a regime layer. The MA is the control that makes the answer falsifiable.
/// </para>
///
/// <para>
/// The Trading Cross's OWN entry (z crossing +1) is deliberately not among the gated signals. Its
/// gate and its trigger are the same series, so the test would be asking whether a rule agrees
/// with itself.
/// </para>
/// </summary>
public static class GateCommand
{
    private const int HorizonBars = 20;
    private const double RiskAtrFraction = 1.0;
    private const double TargetR = 2.0;
    private const int MaGateBars = 200;

    /// <summary>Tuned Trading Cross parameters — the ones the cross-asset study settled on.</summary>
    private const int ZWindow = 50;
    private const double EntryZ = 1.0;
    private const double ExitZ = 0.5;

    private sealed record Trade(string Signal, bool Long, double R, bool CrossGate, bool MaGate, double Z,
        string Symbol, string Class, DateTime Date, double AtrPct);

    /// <summary>Fraction of all usable bars each gate was open on, pooled — the base rate a
    /// signal's own gate-open fraction has to be read against.</summary>
    private static long _barsSeen, _crossBarsOpen, _maBarsOpen;
    private static bool _noisy;

    public static async Task<int> RunAsync(string snapshotDir, string? only, string tf, int permutations,
        double noiseAlpha = 0, int noiseSeed = 0)
    {
        _noisy = noiseAlpha > 0;
        var services = LabHost.Build().Services;
        var engine = services.GetRequiredService<IIndicatorEngine>();

        var files = Directory.GetFiles(snapshotDir, $"*_{tf}.json")
            .Where(f => !Path.GetFileName(f).StartsWith("xs_", StringComparison.OrdinalIgnoreCase))
            .Where(f => only == null || Path.GetFileName(f).Contains(only, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f).ToList();

        var trades = new List<Trade>();
        int symbols = 0;

        foreach (var file in files)
        {
            SnapshotFile snap;
            try { snap = SnapshotCommand.Load(file); } catch { continue; }
            var bars = snap.Bars;
            if (bars.Count < 500) continue;

            // Varma's robustness test. Perturb every log return by gaussian noise scaled to this
            // series' own daily volatility and rebuild the path. A real edge should decay gradually;
            // one keyed to the exact price path collapses at the first perturbation.
            if (noiseAlpha > 0) bars = Perturb(bars, noiseAlpha, noiseSeed + StableSeed.From(snap.Symbol) % 1000);

            Dictionary<string, double[]> cipherB;
            try
            {
                cipherB = await engine.CalculateAsync("CIPHER_B", bars,
                    new Dictionary<string, object> { ["__symbol"] = snap.Symbol }, default);
            }
            catch (Exception ex) { Console.Error.WriteLine($"  ! {snap.Symbol}: {ex.Message}"); continue; }

            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);
            var crossGate = TradingCrossCommand.StatePerBar(bars, ZWindow, EntryZ, ExitZ);
            var maGate = MovingAverageGate(bars, MaGateBars);
            var z = TradingCrossCommand.ZScore(bars, ZWindow);

            var buys = Exact(cipherB, CipherBProvider.CompBlue);
            var sells = Exact(cipherB, CipherBProvider.CompRed);

            AddTrades(trades, "cipherB-long", bars, atr, buys, true, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));
            AddTrades(trades, "cipherB-short", bars, atr, sells, false, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));

            // Base rates, so a signal's gate-open fraction can be read against how often the gate
            // was open AT ALL on this data rather than against an assumption.
            int usable = 0, crossOn = 0, maOn = 0;
            for (int i = MaGateBars; i < bars.Count; i++)
            {
                usable++;
                if (crossGate[i]) crossOn++;
                if (maGate[i]) maOn++;
            }
            _barsSeen += usable; _crossBarsOpen += crossOn; _maBarsOpen += maOn;

            // z-DERIVED signal, kept only to demonstrate why it cannot answer the question: the
            // gate opens above z = +1 and this fires below z = −1, so the gate is closed at every
            // one of its signals BY CONSTRUCTION. Reported, then excluded from the verdict.
            AddTrades(trades, "z-reversion-long*", bars, atr, CrossDownSignal(z, -1.0), true, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));

            // Signals that are NOT functions of z, so the gate is free to be open or closed at
            // them. Without at least one of these the whole command measures its own arithmetic.
            var closes = bars.Select(b => b.Close).ToArray();
            AddTrades(trades, "breakout-long", bars, atr, DonchianBreakout(bars, 20), true, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));
            AddTrades(trades, "rsi-bounce-long", bars, atr,
                CrossUpSignal(AccessibleTrader.Sdk.Indicators.IndicatorMath.Rsi(closes, 14), 30.0), true, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));

            // THE CONTROL THAT MAKES THE MA RESULT MEAN SOMETHING. A gate that only says "the
            // market has been going up" will lift ANY long, signal or no signal. Random entries
            // measure exactly that baseline lift. A gate is only worth attaching to a signal if it
            // lifts that signal by MORE than it lifts a coin flip in the same bars.
            AddTrades(trades, "random-entry-long", bars, atr, RandomSignal(bars.Count, 250, symbols), true, crossGate, maGate, z, snap.Symbol, ClassOf(Path.GetFileName(file)));

            symbols++;
        }

        if (trades.Count < 200) { Console.WriteLine($"Too few trades ({trades.Count})."); return 1; }

        Report(trades, symbols, permutations);
        return 0;
    }

    private static List<Ohlcv> Perturb(List<Ohlcv> bars, double alpha, int seed)
    {
        double sum = 0, sumSq = 0; int n = 0;
        var rets = new double[bars.Count];
        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Close <= 0 || bars[i - 1].Close <= 0) continue;
            rets[i] = Math.Log(bars[i].Close / bars[i - 1].Close);
            sum += rets[i]; sumSq += rets[i] * rets[i]; n++;
        }
        if (n < 2) return bars;
        double mean = sum / n;
        double sigma = Math.Sqrt(Math.Max(1e-12, sumSq / n - mean * mean));

        var rng = new Random(seed);
        var outp = new List<Ohlcv>(bars.Count) { bars[0] };
        double px = bars[0].Close;
        for (int i = 1; i < bars.Count; i++)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            px = Math.Max(1e-9, px * Math.Exp(rets[i] + alpha * sigma * g));

            // Carry the bar's own high/low geometry across as a fraction of its close, so ATR and
            // the stop/target harness see a comparably shaped bar rather than a flat line.
            double refC = bars[i].Close > 0 ? bars[i].Close : px;
            outp.Add(new Ohlcv
            {
                Date = bars[i].Date,
                Open = px,
                High = px * Math.Max(1, bars[i].High / refC),
                Low = px * Math.Min(1, bars[i].Low / refC),
                Close = px,
                Volume = bars[i].Volume,
            });
        }
        return outp;
    }

    internal static string ClassOf(string fileName)
    {
        string f = fileName.ToLowerInvariant();
        if (f.StartsWith("bitstamp_") || f.StartsWith("mexc_")) return "crypto";
        if (f.Contains("xau") || f.Contains("_gld_") || f.Contains("_slv_") || f.Contains("_uso_")) return "commod";
        if (f.Contains("_tlt_") || f.Contains("_ief_")) return "bond";
        return "equity";
    }

    /// <summary>Marks bars where the series crosses down through <paramref name="level"/>.</summary>
    private static double[] CrossDownSignal(double[] z, double level)
    {
        var sig = new double[z.Length];
        Array.Fill(sig, double.NaN);
        for (int i = 1; i < z.Length; i++)
            if (!double.IsNaN(z[i]) && !double.IsNaN(z[i - 1]) && z[i - 1] >= level && z[i] < level)
                sig[i] = 1;
        return sig;
    }

    /// <summary>
    /// <paramref name="count"/> entry bars chosen uniformly at random. Seeded from the symbol
    /// index so a re-run reproduces the same control rather than a different one each time.
    /// </summary>
    private static double[] RandomSignal(int barCount, int count, int seed)
    {
        var sig = new double[barCount];
        Array.Fill(sig, double.NaN);
        var rng = new Random(1000 + seed);
        for (int k = 0; k < count; k++) sig[rng.Next(barCount)] = 1;
        return sig;
    }

    /// <summary>Marks bars where the series crosses up through <paramref name="level"/>.</summary>
    private static double[] CrossUpSignal(double[] v, double level)
    {
        var sig = new double[v.Length];
        Array.Fill(sig, double.NaN);
        for (int i = 1; i < v.Length; i++)
            if (!double.IsNaN(v[i]) && !double.IsNaN(v[i - 1]) && v[i - 1] <= level && v[i] > level)
                sig[i] = 1;
        return sig;
    }

    /// <summary>
    /// Close exceeds the highest high of the prior <paramref name="period"/> bars — the plainest
    /// momentum entry there is, and one built from highs rather than from a mean and a standard
    /// deviation, so it is not a restatement of the gate.
    /// </summary>
    private static double[] DonchianBreakout(IReadOnlyList<Ohlcv> bars, int period)
    {
        var sig = new double[bars.Count];
        Array.Fill(sig, double.NaN);
        for (int i = period; i < bars.Count; i++)
        {
            double hh = double.MinValue;
            for (int j = i - period; j < i; j++) hh = Math.Max(hh, bars[j].High);
            // Only the FIRST bar of a breakout, so one sustained advance is one signal rather than
            // twenty overlapping ones that would all resolve together and fake the sample size.
            if (bars[i].Close > hh && bars[i - 1].Close <= hh) sig[i] = 1;
        }
        return sig;
    }

    /// <summary>
    /// The control gate: close above its own trailing moving average. Uses only bars up to and
    /// including i, so it is knowable at the same moment the Trading Cross state is.
    /// </summary>
    private static bool[] MovingAverageGate(IReadOnlyList<Ohlcv> bars, int period)
    {
        var gate = new bool[bars.Count];
        double sum = 0;
        for (int i = 0; i < bars.Count; i++)
        {
            sum += bars[i].Close;
            if (i >= period) sum -= bars[i - period].Close;
            if (i >= period - 1) gate[i] = bars[i].Close > sum / period;
        }
        return gate;
    }

    private static void AddTrades(List<Trade> sink, string label, IReadOnlyList<Ohlcv> bars,
        double[] atr, double[]? signal, bool isLong, bool[] crossGate, bool[] maGate, double[] z,
        string symbol = "", string cls = "")
    {
        if (signal == null) return;

        for (int i = 1; i < bars.Count - HorizonBars - 1; i++)
        {
            if (double.IsNaN(signal[i])) continue;
            double a = atr[i];
            if (double.IsNaN(a) || a <= 0) continue;

            // Before the z window fills, the cross gate's state array is still at its default
            // false — which would be counted as "gate closed" when the truth is "gate undefined",
            // quietly loading every early trade onto one side of the comparison. Likewise the MA
            // gate is undefined for its first period. Skip until both are real.
            if (double.IsNaN(z[i]) || i < MaGateBars) continue;

            // Enter on the NEXT bar's open. Both gates are read at bar i, so the gate decision and
            // the entry share the same one-bar delay the Trading Cross itself uses.
            double entry = bars[i + 1].Open;
            double risk = a * RiskAtrFraction;
            double stop = isLong ? entry - risk : entry + risk;
            double target = isLong ? entry + risk * TargetR : entry - risk * TargetR;

            double r = 0;
            bool resolved = false;
            for (int j = i + 1; j <= i + HorizonBars; j++)
            {
                if (isLong ? bars[j].Low <= stop : bars[j].High >= stop) { r = -1; resolved = true; break; }
                if (isLong ? bars[j].High >= target : bars[j].Low <= target) { r = TargetR; resolved = true; break; }
            }
            if (!resolved)
            {
                double exit = bars[i + HorizonBars].Close;
                r = (isLong ? exit - entry : entry - exit) / risk;
            }

            // ATR as a fraction of price is what converts a basis-point cost into an R-multiple:
            // risking one ATR means the position is (1/atrPct) times the risk budget, so a spread
            // that looks tiny against notional can be large against R.
            sink.Add(new Trade(label, isLong, r, crossGate[i], maGate[i], z[i],
                symbol, cls, bars[i].Date, a / bars[i].Close));
        }
    }

    private static void Report(List<Trade> all, int symbols, int permutations)
    {
        Console.WriteLine();
        Console.WriteLine($"===== TRADING CROSS AS A GATE — {all.Count:N0} trades over {symbols} symbols =====");
        Console.WriteLine($"Entered next open, {TargetR}R target / 1R stop, {HorizonBars}-bar horizon.");
        Console.WriteLine($"Cross gate: z[{ZWindow}] state, entry {EntryZ}, exit {ExitZ}.   Control gate: close > {MaGateBars}-bar MA.");
        Console.WriteLine();

        Console.WriteLine($"Base rate over {_barsSeen:N0} bars: cross gate open {_crossBarsOpen / (double)_barsSeen:P0}, " +
                          $"MA gate open {_maBarsOpen / (double)_barsSeen:P0}.");
        Console.WriteLine();

        foreach (var g in all.GroupBy(t => t.Signal))
        {
            var set = g.ToList();
            Console.WriteLine($"  ── {g.Key}  (n={set.Count:N0}, ungated mean {set.Average(t => t.R):+0.000;-0.000;0}R, " +
                              $"win {set.Count(t => t.R > 0) / (double)set.Count:P0}) ──");
            Console.WriteLine($"    z at signal: mean {set.Average(t => t.Z),+6:+0.00;-0.00;0}   " +
                              $"min {set.Min(t => t.Z),+6:+0.00;-0.00;0}   max {set.Max(t => t.Z),+6:+0.00;-0.00;0}   " +
                              $"— the gate can only be open above z={ExitZ}");

            double crossLift = Lift(set, t => t.CrossGate, "cross gate", permutations);
            double maLift = Lift(set, t => t.MaGate, "MA gate   ", permutations);

            Console.WriteLine($"      lift difference (cross − MA): {crossLift - maLift,+7:+0.000;-0.000;0}R" +
                              (Math.Abs(crossLift - maLift) < 0.05
                                  ? "   → the two gates are the same filter"
                                  : crossLift > maLift
                                      ? "   → the cross gate adds something the MA does not"
                                      : "   → the MA gate is BETTER; the cross gate is not worth its complexity"));
            Console.WriteLine();
        }

        Verdict(all, permutations);

        if (!_noisy)
            GateRobustness.Run(all.Select(t => (t.Signal, t.R, t.MaGate, t.Symbol, t.Class, t.Date, t.AtrPct)).ToList(),
                               permutations);
    }

    /// <summary>
    /// Mean-R improvement from keeping only the trades the gate allows, with a permutation p-value
    /// on the open-vs-closed spread. The permutation reshuffles the GATE LABELS across trades and
    /// leaves realised R attached to its own trade, which holds the signal's own edge fixed and
    /// isolates the question of whether the label carries information.
    /// </summary>
    private static double Lift(List<Trade> set, Func<Trade, bool> gate, string label, int permutations)
    {
        var open = set.Where(gate).ToList();
        var shut = set.Where(t => !gate(t)).ToList();
        if (open.Count < 30 || shut.Count < 30)
        {
            Console.WriteLine($"    {label}: too lopsided (open {open.Count}, closed {shut.Count})");
            return 0;
        }

        double baseline = set.Average(t => t.R);
        double openMean = open.Average(t => t.R);
        double gap = openMean - shut.Average(t => t.R);
        double p = PermutationP(set.Select(t => t.R).ToArray(), open.Count, shut.Count, gap, permutations);

        Console.WriteLine($"    {label}: open {openMean,+6:+0.000;-0.000;0}R (n={open.Count,5:N0}, {open.Count / (double)set.Count,4:P0})   " +
                          $"closed {shut.Average(t => t.R),+6:+0.000;-0.000;0}R   gap {gap,+6:+0.000;-0.000;0}R   p = {p:0.0000}" +
                          (p <= 0.05 ? "  *" : ""));

        return openMean - baseline;
    }

    private static void Verdict(List<Trade> all, int permutations)
    {
        Console.WriteLine("  ── VERDICT ──");

        // Signals suffixed '*' are functions of z themselves. The gate's state at their signal bar
        // is fixed by arithmetic rather than by the market, so including them would let a
        // tautology vote.
        var rows = all.Where(t => !t.Signal.EndsWith('*')).GroupBy(t => t.Signal).Select(g =>
        {
            var s = g.ToList();
            double b = s.Average(t => t.R);
            var co = s.Where(t => t.CrossGate).ToList();
            var mo = s.Where(t => t.MaGate).ToList();
            return (Signal: g.Key,
                    Cross: co.Count >= 30 ? co.Average(t => t.R) - b : double.NaN,
                    Ma: mo.Count >= 30 ? mo.Average(t => t.R) - b : double.NaN);
        }).ToList();

        int crossWins = rows.Count(r => !double.IsNaN(r.Cross) && !double.IsNaN(r.Ma) && r.Cross > r.Ma + 0.05);
        int maWins = rows.Count(r => !double.IsNaN(r.Cross) && !double.IsNaN(r.Ma) && r.Ma > r.Cross + 0.05);

        Console.WriteLine($"    Signals where the cross gate beats a 200-bar MA: {crossWins} of {rows.Count}.");
        Console.WriteLine($"    Signals where the plain MA beats it:            {maWins} of {rows.Count}.");
        Console.WriteLine();

        // Is the MA gate's benefit signal-specific, or does it lift anything long?
        var rnd = all.Where(t => t.Signal == "random-entry-long").ToList();
        if (rnd.Count >= 100)
        {
            double rndBase = rnd.Average(t => t.R);
            var rndOpen = rnd.Where(t => t.MaGate).ToList();
            double rndLift = rndOpen.Count >= 30 ? rndOpen.Average(t => t.R) - rndBase : double.NaN;

            Console.WriteLine($"    MA-gate lift on RANDOM entries: {rndLift:+0.000;-0.000;0}R (n={rnd.Count:N0}) — the baseline any");
            Console.WriteLine("    long inherits from being in an uptrend. A signal's MA lift is only its own to");
            Console.WriteLine("    the extent it exceeds this:");
            foreach (var r in rows.Where(r => r.Signal != "random-entry-long").OrderByDescending(r => r.Ma))
                Console.WriteLine($"      {r.Signal,-18} MA lift {r.Ma,+6:+0.000;-0.000;0}R   excess over random {r.Ma - rndLift,+6:+0.000;-0.000;0}R");
            Console.WriteLine();
        }

        if (crossWins == 0)
        {
            Console.WriteLine("    The Trading Cross state is NOT worth shipping as a regime layer. Everything it");
            Console.WriteLine("    contributes as a gate is reproduced — or beaten — by 'close above its 200-bar");
            Console.WriteLine("    moving average', which is one line of arithmetic with no parameters to fit.");
            Console.WriteLine("    The z-state earns its keep as an EXPOSURE rule on its own book, where it cleared");
            Console.WriteLine("    an exposure-matched null at p = 0.001, and not as context for other signals.");
        }
        else if (crossWins > maWins)
        {
            Console.WriteLine("    The cross gate carries information a moving average does not, on the majority of");
            Console.WriteLine("    signals tested. The gate-not-stack architecture is supported.");
        }
        else
        {
            Console.WriteLine("    Split decision — the cross gate helps some signals and hurts others, with no");
            Console.WriteLine("    consistent advantage over the moving-average control. Not a foundation.");
        }
    }

    /// <summary>
    /// Two-sample permutation test — see <see cref="LabStats.PermutationP(double[], int, int, double, int, int, int?, out int)"/>. The seed lives here,
    /// not in the shared helper, because it is this command's research parameter.
    /// </summary>
    /// <summary>
    /// Two-sample permutation test over rows that OVERLAP in time.
    ///
    /// <para>Each row is a forward return over the horizon, emitted once per bar, so
    /// consecutive rows share all but one of their forward bars. Shuffling rows individually
    /// treats them as independent draws and inflates significance by roughly the square root of
    /// the horizon — see <see cref="LabStats.BlockPermutationP(double[], int, int, double, int, int, int, int?, out int)"/>.
    /// Blocks of one horizon are what make two of them genuinely non-overlapping.</para>
    /// </summary>
    private static double PermutationP(double[] pool, int nA, int nB, double observed, int runs) =>
        LabStats.BlockPermutationP(pool, nA, nB, observed, runs, seed: 4242,
            blockSize: HorizonBars);
    private static double[]? Exact(Dictionary<string, double[]> data, string name) =>
        data.TryGetValue(name, out var v) ? v : null;
}
