using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// The rebuilt (2026-10-17) Cipher A, signal by signal, through a reconstruction of the 2026-07-13
/// cross-asset gate battery that retired the old one — plus one pre-registered pass of Cipher A
/// combined with Cipher B. Pre-registration: <c>reports/cipher_strategies_rerun_REPORT.md</c>,
/// committed before this command was first run.
///
/// <para>
/// WHY A RECONSTRUCTION. The July battery's code was never committed; its design survives only as
/// a summary (assets, "WaveTrend reversal trigger; Faber / COT / cycle-window / RSI /
/// variance-ratio gates; era-sliced", and a few cells as hit rate, mean %/20d and t). So this
/// rebuilds that design with every choice written down, and none of its numbers can be compared
/// to July's as a reproduction.
/// </para>
///
/// <para>
/// THE TRAPS IT IS BUILT AROUND.
/// (1) A gate that is open when price has been rising lifts ANY long, so every signal is read as its
/// EXCESS over the mean of all gate-open bars on the same asset — an averaged random entry under the
/// same gate, never a single draw.
/// (2) Forward 20-bar returns from consecutive bars overlap, so a row-wise permutation is far too
/// narrow; the labels are permuted in contiguous 20-bar blocks (the horizon), keeping the returns
/// fixed.
/// (3) Hundreds of cells will produce some p &lt; 0.05 by chance, so the verdict uses a single-step
/// max-|t| null over the whole family: a cell survives only if its |t| beats the family's maximum
/// |t| in 95% of the permuted draws. Cells sharing an asset and gate share each permuted draw, so
/// their correlation is kept; draws are independent across groups, which makes the family null a
/// little wider than the true one (conservative).
/// (4) Yellow Diamond is a STATE that holds for many bars; only its onset bar is an event, and the
/// same onset rule is applied to every symbol so none is treated differently.
/// Entry is at bar i+1's open — never bar i — and nothing reads a bar after the entry decision.
/// </para>
/// </summary>
public static class CipherABatteryCommand
{
    private const int Horizon = 20;
    private const int Warmup = 250;
    private const int Block = Horizon;

    /// <summary>The July asset list, where a snapshot exists. EUR and COIN have none.</summary>
    private static readonly (string Label, string File)[] Assets =
    {
        ("BTC", "bitstamp_BTC_USDT_1d.json"), ("ETH", "bitstamp_ETH_USDT_1d.json"),
        ("GOLD", "twelvedata_XAU_USD_1d.json"), ("SLV", "yahoo_SLV_1d.json"),
        ("SPY", "twelvedata_SPY_1d.json"), ("QQQ", "twelvedata_QQQ_1d.json"),
        ("AAPL", "yahoo_AAPL_1d.json"), ("MSFT", "yahoo_MSFT_1d.json"),
    };

    private sealed record Signal(string Name, bool Long, bool[] Fires, bool CountsAsCipherA);
    private sealed record Gate(string Name, bool[]? Long, bool[]? Short);

    private sealed class AssetData
    {
        public required string Label;
        public required List<Ohlcv> Bars;
        public required double[] Fwd;     // raw 20-bar forward return from open[i+1] to close[i+20]; NaN where unknown
        public required double[] RLong, RShort;  // standard R harness outcome per bar
        public required DateTime Median;
        public required List<Signal> Signals;
        public required List<Signal> Combos;
        public required List<Gate> Gates;
    }

    private sealed class Cell
    {
        public required string Family, Asset, GateName, SignalName;
        public required bool Long;
        public required int[] Rows;        // bar indices of the gate-open sequence, time order
        public required double[] Y;        // signed forward return per row
        public required bool[] Label;      // signal onset per row
        public required string GroupKey;
        public int N;
        public double Hit, Mean, Excess, T, P, PFamily, MeanR, ExH1, ExH2;
        public bool[] ExistsH1 = Array.Empty<bool>();
    }

    public static async Task<int> RunAsync(string snapshotDir, int permutations)
    {
        Console.WriteLine("CIPHER A BATTERY — rebuilt Cipher A (2026-10-17), reconstruction of the 2026-07-13 gate battery");
        Console.WriteLine(LabCipherProfile.Describe());
        Console.WriteLine($"Horizon {Horizon} bars, entry next open, block permutation (block {Block}), {permutations:N0} draws.");
        Console.WriteLine();

        var services = LabHost.Build().Services;
        var engine = services.GetRequiredService<IIndicatorEngine>();
        var assets = new List<AssetData>();
        foreach (var (label, file) in Assets)
        {
            var path = Path.Combine(snapshotDir, file);
            if (!File.Exists(path)) { Console.WriteLine($"  ! {label}: {file} missing — skipped (not fetched)"); continue; }
            var snap = SnapshotCommand.Load(path);
            var a = await BuildAsset(engine, label, snap);
            assets.Add(a);
            Console.WriteLine($"  {label,-5} {snap.Bars.Count,6} bars {snap.FirstDate:yyyy-MM-dd} → {snap.LastDate:yyyy-MM-dd}   " +
                              string.Join("  ", a.Signals.Select(s => $"{Short(s.Name)}={s.Fires.Count(f => f)}")));
        }
        Console.WriteLine();

        PrintOverlap(assets);

        var battery = BuildCells("battery", assets, a => a.Signals, gateFilter: null);
        Evaluate(battery, permutations, seed: 20261017);
        Report("SIGNAL BY SIGNAL (rebuilt Cipher A; Cipher B NEW rows are reference)", battery);

        var combos = BuildCells("combo", assets, a => a.Combos, gateFilter: g => g is "G0 none" or "G1 Faber");
        Evaluate(combos, permutations, seed: 20261018);
        Report("PRE-REGISTERED COMBINATIONS (Cipher A x Cipher B NEW)", combos);
        return 0;
    }

    private static string Short(string s) => s.Replace("Cipher A ", "A.").Replace("Cipher B ", "B.").Replace(" ", "");

    private static async Task<AssetData> BuildAsset(IIndicatorEngine engine, string label, SnapshotFile snap)
    {
        var bars = snap.Bars;
        int n = bars.Count;
        var p = new Dictionary<string, object> { ["__symbol"] = snap.Symbol };
        var a = await engine.CalculateAsync("CIPHER_A", bars, p, default);
        var b = await engine.CalculateAsync("CIPHER_B", bars, new Dictionary<string, object>(p), default);
        Dictionary<string, double[]>? cot = null;
        try { cot = await engine.CalculateAsync("COT_POSITIONING", bars, new Dictionary<string, object>(p), default); } catch { }

        bool[] Marker(Dictionary<string, double[]> d, string comp)
        {
            var v = d.TryGetValue(comp, out var arr) ? arr : new double[n];
            if (!d.ContainsKey(comp)) Array.Fill(v, double.NaN);
            var f = new bool[n];
            for (int i = 1; i < n; i++) f[i] = !double.IsNaN(v[i]) && double.IsNaN(v[i - 1]); // onset only
            if (n > 0) f[0] = false;
            return f;
        }
        double[] Arr(Dictionary<string, double[]> d, string comp)
        {
            if (d.TryGetValue(comp, out var arr)) return arr;
            var nan = new double[n]; Array.Fill(nan, double.NaN); return nan;
        }

        var close = bars.Select(x => x.Close).ToArray();
        var sma200 = IndicatorMath.Sma(close, 200);
        var rsi = IndicatorMath.Rsi(close, 14);
        var atr = IndicatorMath.Atr(bars.ToArray(), 14);
        var vr = VarianceRatio(close, 20, 252);

        var fwd = new double[n]; Array.Fill(fwd, double.NaN);
        var rl = new double[n]; var rs = new double[n]; Array.Fill(rl, double.NaN); Array.Fill(rs, double.NaN);
        for (int i = 0; i + Horizon < n; i++)
        {
            double entry = bars[i + 1].Open;
            if (entry <= 0) continue;
            fwd[i] = bars[i + Horizon].Close / entry - 1.0;
            if (!double.IsNaN(atr[i]) && atr[i] > 0)
            {
                rl[i] = RHarness(bars, i, atr[i], true);
                rs[i] = RHarness(bars, i, atr[i], false);
            }
        }

        var e11 = Arr(a, CipherAProvider.RibbonLine(1));
        var e33 = Arr(a, CipherAProvider.RibbonLine(7));
        var wt2 = Arr(b, CipherBProvider.CompWT2);
        var mf = Arr(b, CipherBProvider.CompMoneyFlowWave);
        var bGreen = Marker(b, CipherBProvider.CompBlue);
        var bBearX = Marker(b, CipherBProvider.CompCrossBear);

        var signals = new List<Signal>
        {
            new("Cipher A Green Dot", true, Marker(a, CipherAProvider.CompGreenDot), true),
            new("Cipher A Blue Triangle", true, Marker(a, CipherAProvider.CompBlueTriangle), true),
            new("Cipher A Yellow Diamond", true, Marker(a, CipherAProvider.CompYellowDiamond), true),
            new("Cipher A Red X", false, Marker(a, CipherAProvider.CompRedX), true),
            new("Cipher A Red Diamond", false, Marker(a, CipherAProvider.CompRedDiamond), true),
            new("Cipher A Blood Diamond", false, Marker(a, CipherAProvider.CompBloodDiamond), true),
            new("Cipher A Yellow X", false, Marker(a, CipherAProvider.CompYellowX), true),
            new("Cipher B Green Dot", true, bGreen, false),
            new("Cipher B WT Cross Bull", true, Marker(b, CipherBProvider.CompCrossBull), false),
            new("Cipher B WT Cross Bear", false, bBearX, false),
        };

        bool Within(bool[] f, int i, int before, int after)
        {
            for (int k = i - before; k <= i + after; k++) if (k >= 0 && k < n && f[k]) return true;
            return false;
        }
        var aGreen = signals[0].Fires; var aBlue = signals[1].Fires; var aBlood = signals[5].Fires; var aRedX = signals[3].Fires;
        var k1 = new bool[n]; var k2 = new bool[n]; var k3 = new bool[n]; var k4 = new bool[n]; var k5 = new bool[n]; var k6 = new bool[n];
        for (int i = 0; i < n; i++)
        {
            bool ribbonKnown = !double.IsNaN(e11[i]) && !double.IsNaN(e33[i]);
            k1[i] = bGreen[i] && ribbonKnown && e11[i] > e33[i];
            k2[i] = bGreen[i] && ribbonKnown && e11[i] < e33[i];
            k3[i] = aGreen[i] && !double.IsNaN(wt2[i]) && wt2[i] < 0;
            k4[i] = aBlue[i] && Within(bGreen, i, 5, 0);
            k5[i] = aBlood[i] && !double.IsNaN(mf[i]) && mf[i] < 0;
            // "within 2 bars" of a bear cross: only bars already printed may be used — the window
            // looks back, never forward (the Red X decision is made on bar i).
            k6[i] = aRedX[i] && Within(bBearX, i, 2, 0);
        }
        var combos = new List<Signal>
        {
            new("K1 B green dot + ribbon up", true, k1, true),
            new("K2 B green dot + ribbon down", true, k2, true),
            new("K3 A Green Dot + B wave<0", true, k3, true),
            new("K4 A Blue Triangle + B green dot <=5 back", true, k4, true),
            new("K5 A Blood Diamond + B MF<0", false, k5, true),
            new("K6 A Red X + B bear cross <=2 back", false, k6, true),
        };

        bool[] Map(Func<int, bool> f) { var r = new bool[n]; for (int i = 0; i < n; i++) r[i] = f(i); return r; }
        bool Known(double v) => !double.IsNaN(v);
        var cotZ = cot != null ? Arr(cot, "Positioning Z-Score") : null;
        bool hasCot = cotZ != null && cotZ.Count(Known) > 100;

        // A gate is NaN-aware by construction: where its input is undefined the bar is excluded
        // (false on BOTH sides), never counted as the gate's "off" state.
        var gates = new List<Gate>
        {
            new("G0 none", Map(_ => true), Map(_ => true)),
            new("G1 Faber", Map(i => Known(sma200[i]) && close[i] > sma200[i]), Map(i => Known(sma200[i]) && close[i] < sma200[i])),
            new("G4 RSI", Map(i => Known(rsi[i]) && rsi[i] < 50), Map(i => Known(rsi[i]) && rsi[i] > 50)),
            new("G5 VR<1", Map(i => Known(vr[i]) && vr[i] < 1), Map(i => Known(vr[i]) && vr[i] < 1)),
        };
        if (hasCot)
        {
            gates.Add(new("G2 COT", Map(i => Known(cotZ![i]) && cotZ[i] < 1.5), Map(i => Known(cotZ![i]) && cotZ[i] > -1.5)));
            gates.Add(new("G3 Faber+COT",
                Map(i => Known(sma200[i]) && close[i] > sma200[i] && Known(cotZ![i]) && cotZ[i] < 1.5),
                Map(i => Known(sma200[i]) && close[i] < sma200[i] && Known(cotZ![i]) && cotZ[i] > -1.5)));
        }
        // G6 (in a Loukas daily-cycle window) went with Loukas Cycle Detection, removed 2026-10-22.

        return new AssetData
        {
            Label = label, Bars = bars, Fwd = fwd, RLong = rl, RShort = rs,
            Median = bars[n / 2].Date, Signals = signals, Combos = combos, Gates = gates,
        };
    }

    /// <summary>1-ATR stop, 2R target, 20-bar horizon, entry at the next open; a bar that touches
    /// both counts as the stop (the pessimistic reading of an unknown intrabar path).</summary>
    private static double RHarness(List<Ohlcv> bars, int i, double atr, bool isLong)
    {
        double entry = bars[i + 1].Open, risk = atr;
        double stop = isLong ? entry - risk : entry + risk;
        double target = isLong ? entry + 2 * risk : entry - 2 * risk;
        for (int k = i + 1; k <= i + Horizon && k < bars.Count; k++)
        {
            var bk = bars[k];
            bool hitStop = isLong ? bk.Low <= stop : bk.High >= stop;
            bool hitTarget = isLong ? bk.High >= target : bk.Low <= target;
            if (hitStop) return -1.0;
            if (hitTarget) return 2.0;
        }
        double exit = bars[Math.Min(i + Horizon, bars.Count - 1)].Close;
        return (isLong ? exit - entry : entry - exit) / risk;
    }

    /// <summary>Trailing variance ratio: variance of overlapping q-bar log returns over the last
    /// <paramref name="win"/> bars, divided by q x the variance of 1-bar log returns over the same
    /// bars. Below 1 = mean-reverting over that window. Uses bars up to and including i only.</summary>
    internal static double[] VarianceRatio(double[] close, int q, int win)
    {
        int n = close.Length;
        var vr = new double[n]; Array.Fill(vr, double.NaN);
        var r1 = new double[n];
        for (int i = 1; i < n; i++) r1[i] = close[i] > 0 && close[i - 1] > 0 ? Math.Log(close[i] / close[i - 1]) : double.NaN;
        for (int i = win + q; i < n; i++)
        {
            double s = 0, s2 = 0; int c = 0;
            for (int k = i - win + 1; k <= i; k++) { if (double.IsNaN(r1[k])) continue; s += r1[k]; s2 += r1[k] * r1[k]; c++; }
            if (c < win / 2) continue;
            double m1 = s / c, v1 = s2 / c - m1 * m1;
            double sq = 0, sq2 = 0; int cq = 0;
            for (int k = i - win + q; k <= i; k++)
            {
                if (close[k] <= 0 || close[k - q] <= 0) continue;
                double rq = Math.Log(close[k] / close[k - q]);
                sq += rq; sq2 += rq * rq; cq++;
            }
            if (cq < 10 || v1 <= 0) continue;
            double mq = sq / cq, vq = sq2 / cq - mq * mq;
            vr[i] = vq / (q * v1);
        }
        return vr;
    }

    private static List<Cell> BuildCells(string family, List<AssetData> assets, Func<AssetData, List<Signal>> pick,
        Func<string, bool>? gateFilter)
    {
        var cells = new List<Cell>();
        foreach (var a in assets)
        foreach (var g in a.Gates)
        {
            if (gateFilter != null && !gateFilter(g.Name)) continue;
            foreach (var dir in new[] { true, false })
            {
                var open = dir ? g.Long : g.Short;
                if (open == null) continue;
                var rows = new List<int>();
                for (int i = Warmup; i < a.Bars.Count; i++)
                    if (open[i] && !double.IsNaN(a.Fwd[i])) rows.Add(i);
                if (rows.Count < 4 * Block) continue;
                foreach (var s in pick(a).Where(s => s.Long == dir))
                {
                    var r = rows.ToArray();
                    cells.Add(new Cell
                    {
                        Family = family, Asset = a.Label, GateName = g.Name, SignalName = s.Name, Long = dir,
                        Rows = r,
                        Y = r.Select(i => dir ? a.Fwd[i] : -a.Fwd[i]).ToArray(),
                        Label = r.Select(i => s.Fires[i]).ToArray(),
                        GroupKey = $"{a.Label}|{g.Name}|{dir}",
                        MeanR = r.Where(i => s.Fires[i]).Select(i => dir ? a.RLong[i] : a.RShort[i]).Where(v => !double.IsNaN(v))
                                 .DefaultIfEmpty(double.NaN).Average(),
                        ExistsH1 = r.Select(i => a.Bars[i].Date < a.Median).ToArray(),
                    });
                }
            }
        }
        return cells;
    }

    private static double TStat(double[] y, bool[] lab)
    {
        double sa = 0, sb = 0, qa = 0, qb = 0; int na = 0, nb = 0;
        for (int i = 0; i < y.Length; i++)
        {
            if (lab[i]) { sa += y[i]; qa += y[i] * y[i]; na++; } else { sb += y[i]; qb += y[i] * y[i]; nb++; }
        }
        if (na < 2 || nb < 2) return 0;
        double ma = sa / na, mb = sb / nb;
        double va = Math.Max(1e-12, qa / na - ma * ma), vb = Math.Max(1e-12, qb / nb - mb * mb);
        return (ma - mb) / Math.Sqrt(va / na + vb / nb);
    }

    private static void Evaluate(List<Cell> cells, int perms, int seed)
    {
        // Observed statistics.
        foreach (var c in cells)
        {
            int n = c.Label.Count(x => x);
            c.N = n;
            if (n == 0) continue;
            var ys = c.Y.Where((_, i) => c.Label[i]).ToArray();
            c.Hit = ys.Count(v => v > 0) / (double)n;
            c.Mean = ys.Average();
            c.Excess = c.Mean - c.Y.Average();
            c.T = TStat(c.Y, c.Label);
            double Half(bool first)
            {
                var idx = Enumerable.Range(0, c.Y.Length).Where(i => c.ExistsH1[i] == first).ToArray();
                var sig = idx.Where(i => c.Label[i]).Select(i => c.Y[i]).ToArray();
                if (sig.Length == 0 || idx.Length == 0) return double.NaN;
                return sig.Average() - idx.Select(i => c.Y[i]).Average();
            }
            c.ExH1 = Half(true); c.ExH2 = Half(false);
        }

        var tested = cells.Where(c => c.N >= 5).ToList();
        var exceed = new int[cells.Count];
        var index = new Dictionary<Cell, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < cells.Count; i++) index[cells[i]] = i;
        var familyMax = new double[perms];
        // One permuted block order per group per draw, shared by every cell in the group.
        foreach (var grp in tested.GroupBy(c => c.GroupKey))
        {
            var list = grp.ToList();
            int len = list[0].Y.Length;
            int nBlocks = (len + Block - 1) / Block;
            var rng = new Random(seed ^ StableSeed.From(grp.Key));
            var order = new int[nBlocks];
            var permLab = new bool[len];
            for (int p = 0; p < perms; p++)
            {
                for (int i = 0; i < nBlocks; i++) order[i] = i;
                for (int i = nBlocks - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
                foreach (var c in list)
                {
                    // Labels move in blocks; returns stay where they are. The tail block may be short.
                    int w = 0;
                    foreach (int bi in order)
                    {
                        int from = bi * Block, to = Math.Min(from + Block, len);
                        for (int k = from; k < to && w < len; k++) permLab[w++] = c.Label[k];
                    }
                    double t = Math.Abs(TStat(c.Y, permLab));
                    if (t >= Math.Abs(c.T)) exceed[index[c]]++;
                    if (t > familyMax[p]) familyMax[p] = t;
                }
            }
        }
        foreach (var c in tested)
        {
            c.P = (exceed[index[c]] + 1.0) / (perms + 1.0);
            c.PFamily = (familyMax.Count(m => m >= Math.Abs(c.T)) + 1.0) / (perms + 1.0);
        }
        foreach (var c in cells.Where(c => c.N < 5)) { c.P = double.NaN; c.PFamily = double.NaN; }
    }

    private static void Report(string title, List<Cell> cells)
    {
        var tested = cells.Where(c => c.N >= 5).ToList();
        var aCells = tested.Where(c => !c.SignalName.StartsWith("Cipher B")).ToList();
        Console.WriteLine($"══════ {title} ══════");
        Console.WriteLine($"cells attempted (variantsTried): {cells.Count}   with >=5 events (tested): {tested.Count}   " +
                          $"of which Cipher-A-dependent: {aCells.Count}");
        Console.WriteLine("asset gate          dir signal                                     n   hit%  mean%  exc%     t      p   pFW  meanR  excH1  excH2");
        foreach (var c in cells.OrderBy(c => c.Asset).ThenBy(c => c.GateName).ThenBy(c => c.SignalName))
        {
            string s = c.N < 5
                ? $"{c.N,4}   (fewer than 5 events — not tested)"
                : $"{c.N,4} {100 * c.Hit,5:0}% {100 * c.Mean,+6:0.00} {100 * c.Excess,+6:0.00} {c.T,+5:0.00} {c.P,6:0.000} {c.PFamily,5:0.00} {c.MeanR,+6:0.00} {100 * c.ExH1,+6:0.00} {100 * c.ExH2,+6:0.00}";
            Console.WriteLine($"{c.Asset,-5} {c.GateName,-13} {(c.Long ? "L" : "S")}  {c.SignalName,-40} {s}");
        }
        int uncorrected = aCells.Count(c => c.P <= 0.05);
        var fw = tested.Where(c => c.PFamily <= 0.05).ToList();
        Console.WriteLine();
        Console.WriteLine("── VERDICT ──");
        Console.WriteLine($"  Cipher-A-dependent cells with uncorrected p <= 0.05: {uncorrected} of {aCells.Count} " +
                          $"(about {0.05 * aCells.Count:0} expected by chance alone).");
        Console.WriteLine($"  Cells surviving the family-wise max-|t| null (pFW <= 0.05): {fw.Count}");
        foreach (var c in fw)
            Console.WriteLine($"    {c.Asset} {c.GateName} {c.SignalName}: excess {100 * c.Excess:+0.00;-0.00}% per 20 bars, " +
                              $"t {c.T:+0.00;-0.00}, halves {100 * c.ExH1:+0.00;-0.00} / {100 * c.ExH2:+0.00;-0.00}" +
                              (Math.Sign(c.ExH1) == Math.Sign(c.ExH2) ? " (same sign)" : " (SIGN FLIPS between halves)"));
        if (fw.Count == 0)
            Console.WriteLine("  Nothing survives the family-wise null. Read every row above as noise around the gate-open mean.");
        Console.WriteLine();
    }

    private static void PrintOverlap(List<AssetData> assets)
    {
        // The July retirement said Cipher A "is B's signals projected onto the price pane". For the
        // rebuilt indicator that is a measurable claim: how often does each Cipher A symbol land
        // within one bar of a Cipher B event? Pooled over assets.
        Console.WriteLine("── OVERLAP: share of each Cipher A onset with a Cipher B event within ±1 bar (pooled) ──");
        var bNames = new[] { "Cipher B Green Dot", "Cipher B WT Cross Bull", "Cipher B Red Dot", "Cipher B WT Cross Bear" };
        Console.WriteLine($"{"",-26}" + string.Join("", bNames.Select(b => $"{b.Replace("Cipher B ", "B."),16}")));
        foreach (var aName in assets[0].Signals.Where(s => s.CountsAsCipherA).Select(s => s.Name))
        {
            var cols = new List<string>();
            int total = 0;
            var hits = new int[bNames.Length];
            foreach (var a in assets)
            {
                var af = a.Signals.First(s => s.Name == aName).Fires;
                for (int i = 0; i < af.Length; i++)
                {
                    if (!af[i]) continue;
                    total++;
                    for (int j = 0; j < bNames.Length; j++)
                    {
                        var bf = a.Signals.First(s => s.Name == bNames[j]).Fires;
                        for (int k = Math.Max(0, i - 1); k <= Math.Min(af.Length - 1, i + 1); k++)
                            if (bf[k]) { hits[j]++; break; }
                    }
                }
            }
            Console.WriteLine($"{aName.Replace("Cipher A ", "A."),-22} n={total,5}" +
                              string.Join("", hits.Select(h => $"{(total == 0 ? 0 : 100.0 * h / total),15:0}%")));
        }
        Console.WriteLine();
    }
}
