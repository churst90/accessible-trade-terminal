using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Smart money concepts, graded: fair value gaps, order blocks, equal highs and lows, liquidity
/// sweeps, premium and discount. Every rule, control and threshold here is the one written down in
/// <c>docs/archive/SMC_PREREG.md</c> before any of this ran; the zones and levels come from
/// <see cref="SmcMath"/>, the same code the chart draws them with.
///
/// <para>
/// <c>--era dev</c> (the default) decides every verdict. <c>--era holdout</c> is run once, after the
/// development verdicts are written down, and only its signs are read.
/// </para>
///
/// <para>
/// One reading the pre-registration leaves open, fixed in this code before it first ran: a bar that
/// takes two swing levels on the same side counts as one sweep (or one breakout), not two, because
/// it is one bar and one entry. That is how <see cref="SmcMath.Takes"/> reports them for the chart.
/// </para>
/// </summary>
public static class SmcCommand
{
    private const int Span = 5, Wait = 50, Horizon = 20, Draws = 20, Resamples = 10_000, MinEvents = 30;
    private const double Cost = 0.0015, MinGapAtr = 0.1, EqualAtr = 0.1;
    private static readonly DateTime Cut = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Classes = { "crypto 4h", "crypto 1d", "equities 1d" };
    private static readonly string[] Primaries = { "F1", "O1", "E1", "L1", "P1" };

    /// <summary>One outcome: <see cref="Group"/> 0 is the event (or the with-the-teaching side), 1 the other side of a two-group test.</summary>
    private readonly record struct Obs(int Year, double Value, int Group = 0);

    private sealed class Series
    {
        public required string File;
        public required string Cls;
        public required Ohlcv[] B;
        public required double[] Atr;
        public required List<SmcMath.Swing> Swings;
        public required int Seed;
        public List<int> ZoneBars = new();   // control anchors with room for the wait and the horizon
        public List<int> CloseBars = new();  // control entries with room for the horizon
        public bool InEra(int i, bool dev) => (B[i].Date < Cut) == dev;
        public bool AtrOk(int i) => i >= 0 && i < Atr.Length && !double.IsNaN(Atr[i]) && Atr[i] > 0;
    }

    private sealed class Test
    {
        public readonly List<Obs> Obs = new();
        public readonly List<double> EventGross = new(), EventNet = new();
        public readonly Dictionary<string, int> Counts = new();
        public bool TwoGroup;
        public void Count(string k) => Counts[k] = Counts.GetValueOrDefault(k) + 1;
    }

    public static int Run(string snapshotDir, string era)
    {
        bool dev = !string.Equals(era, "holdout", StringComparison.OrdinalIgnoreCase);
        var series = Load(snapshotDir, out var skipped);
        if (series.Count == 0) { Console.Error.WriteLine("No price files found."); return 1; }

        // Per class, per test.
        var t = Classes.ToDictionary(c => c, _ => new Dictionary<string, Test>());
        Test T(string cls, string id, bool two = false)
        {
            if (!t[cls].TryGetValue(id, out var x)) t[cls][id] = x = new Test { TwoGroup = two };
            return x;
        }

        foreach (var cls in Classes)
        {
            var mine = series.Where(s => s.Cls == cls).ToList();
            ZoneTests(mine, s => SmcMath.FairValueGaps(s.B, s.Atr, MinGapAtr), dev, T(cls, "F1"), T(cls, "F1 random anchor"), T(cls, "F2"), fillTest: true);
            ZoneTests(mine, s => SmcMath.Structure(s.B, s.Swings).Blocks, dev, T(cls, "O1"), T(cls, "O1 random anchor"), null, fillTest: false);
            EqualTest(mine, dev, T(cls, "E1"));
            SweepTests(mine, dev, T(cls, "L1"), T(cls, "L2", two: true));
            RangeTests(mine, dev, T(cls, "P1", two: true), T(cls, "P2", two: true));
        }

        Report(series, skipped, t, dev);
        return 0;
    }

    // ── Data ──────────────────────────────────────────────────────────────────

    private static List<Series> Load(string dir, out List<string> skipped)
    {
        skipped = new();
        var list = new List<Series>();
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            string name = Path.GetFileName(f);
            string? cls = name.EndsWith("_4h.json") && (name.StartsWith("bitstamp_") || name.StartsWith("mexc_")) ? "crypto 4h"
                : name.EndsWith("_1d.json") && (name.StartsWith("bitstamp_") || name.StartsWith("mexc_")) ? "crypto 1d"
                : name.EndsWith("_1d.json") && name.StartsWith("yahoo_") ? "equities 1d"
                : null;
            if (cls == null) continue;
            var bars = SnapshotCommand.Load(f).Bars.ToArray();
            if (bars.Length < 500) { skipped.Add($"{name} ({bars.Length} bars)"); continue; }
            var s = new Series
            {
                File = name, Cls = cls, B = bars, Atr = SmcMath.Atr(bars),
                Swings = SmcMath.Swings(bars, Span), Seed = StableSeed.From(name),
            };
            for (int j = 1; j < bars.Length; j++)
            {
                if (!s.AtrOk(j)) continue;
                if (j + Wait + Horizon < bars.Length) s.ZoneBars.Add(j);
                if (j + Horizon < bars.Length) s.CloseBars.Add(j);
            }
            list.Add(s);
        }
        return list;
    }

    // ── Shared pieces ─────────────────────────────────────────────────────────

    /// <summary>The first bar in (from, from + Wait] to reach the level, and the limit fill there.</summary>
    private static (int T, double Fill)? Touch(Ohlcv[] b, int from, double level, bool isLong)
    {
        for (int t = from + 1; t <= from + Wait && t < b.Length; t++)
        {
            if (isLong && b[t].Low <= level) return (t, b[t].Open < level ? b[t].Open : level);
            if (!isLong && b[t].High >= level) return (t, b[t].Open > level ? b[t].Open : level);
        }
        return null;
    }

    private static double R(Ohlcv[] b, int t, double entry, double risk, bool isLong)
    {
        double move = (b[t + Horizon].Close - entry) / risk;
        return isLong ? move : -move;
    }

    private static double CostR(double entry, double risk) => 2 * Cost * entry / risk;

    /// <summary>A limit at <paramref name="level"/> from <paramref name="from"/>: its 20-bar R, or null if it never filled.</summary>
    private static double? LimitR(Ohlcv[] b, int from, double level, double risk, bool isLong)
        => Touch(b, from, level, isLong) is { } f ? R(b, f.T, f.Fill, risk, isLong) : null;

    private static List<int> EraBars(Series s, List<int> pool, bool dev) => pool.Where(j => s.InEra(j, dev)).ToList();

    // ── F1, F2, O1: a zone as support on the first return ─────────────────────

    private static void ZoneTests(List<Series> mine, Func<Series, List<SmcMath.Zone>> zonesOf, bool dev,
        Test primary, Test anchor, Test? fill, bool fillTest)
    {
        // Each event: the series, the zone, its depth below (or above) the close in ATR.
        var events = new List<(Series S, SmcMath.Zone Z, double Depth)>();
        var pool = new Dictionary<bool, List<double>> { [true] = new(), [false] = new() };
        foreach (var s in mine)
            foreach (var z in zonesOf(s))
            {
                int i = z.KnownAt;
                if (!s.AtrOk(i)) continue;
                double a = s.Atr[i], c = s.B[i].Close;
                double depth = z.Bullish ? (c - z.Top) / a : (z.Bottom - c) / a;
                // The isolating control draws depths from the development era's zones, in either run.
                if (s.B[i].Date < Cut) pool[z.Bullish].Add(depth);
                if (!s.InEra(i, dev)) continue;
                if (i + Wait + Horizon >= s.B.Length) { primary.Count("past the last bar"); continue; }
                events.Add((s, z, depth));
            }

        foreach (var g in events.GroupBy(e => e.S))
        {
            var s = g.Key;
            var b = s.B;
            var anchors = EraBars(s, s.ZoneBars, dev);
            var rng = new Random(s.Seed ^ (fillTest ? 0x5F1 : 0x0B1));
            foreach (var (_, z, depth) in g)
            {
                int i = z.KnownAt;
                double a = s.Atr[i], c = b[i].Close;
                bool isLong = z.Bullish;
                int year = b[i].Date.Year;
                double edge = isLong ? z.Top : z.Bottom;
                primary.Count("events");

                // F2: does price reach the far edge within the wait, against random levels as deep?
                if (fill != null && anchors.Count > 0)
                {
                    double far = isLong ? z.Bottom : z.Top;
                    double farDepth = isLong ? (c - far) / a : (far - c) / a;
                    bool reached = Touch(b, i, far, isLong) != null;
                    int hits = 0;
                    for (int k = 0; k < Draws; k++)
                    {
                        int j = anchors[rng.Next(anchors.Count)];
                        double lvl = isLong ? b[j].Close - farDepth * s.Atr[j] : b[j].Close + farDepth * s.Atr[j];
                        if (Touch(b, j, lvl, isLong) != null) hits++;
                    }
                    fill.Count("events");
                    fill.Obs.Add(new Obs(year, (reached ? 1 : 0) - hits / (double)Draws));
                    fill.EventGross.Add(reached ? 1 : 0);
                }

                var own = Touch(b, i, edge, isLong);
                // The draws happen whether or not the event filled, so one event's fill cannot shift
                // the random numbers every later event gets.
                var iso = new List<double>();
                var side = pool[isLong];
                for (int k = 0; k < Draws && side.Count > 0; k++)
                {
                    double d = side[rng.Next(side.Count)];
                    double lvl = isLong ? c - d * a : c + d * a;
                    if (LimitR(b, i, lvl, a, isLong) is { } r) iso.Add(r);
                }
                var rnd = new List<double>();
                for (int k = 0; k < Draws && anchors.Count > 0; k++)
                {
                    int j = anchors[rng.Next(anchors.Count)];
                    double lvl = isLong ? b[j].Close - depth * s.Atr[j] : b[j].Close + depth * s.Atr[j];
                    if (LimitR(b, j, lvl, s.Atr[j], isLong) is { } r) rnd.Add(r);
                }

                if (own is not { } f) { primary.Count("never returned"); continue; }
                double er = R(b, f.T, f.Fill, a, isLong);
                primary.Count("filled");
                primary.EventGross.Add(er);
                primary.EventNet.Add(er - CostR(f.Fill, a));
                if (iso.Count == 0) primary.Count("no control draw filled");
                else primary.Obs.Add(new Obs(year, er - iso.Average()));
                if (rnd.Count == 0) anchor.Count("no anchor filled");
                else { anchor.Obs.Add(new Obs(year, er - rnd.Average())); anchor.Count("events"); }
            }
        }
    }

    // ── E1: equal highs draw price to them ────────────────────────────────────

    private static void EqualTest(List<Series> mine, bool dev, Test test)
    {
        const int Bins = 10;
        // [side][bin] = (breached, total) over the other swings.
        var ctl = new Dictionary<bool, (int Hit, int N)[]> { [true] = new (int, int)[Bins], [false] = new (int, int)[Bins] };
        var events = new List<(int Year, bool High, int Bin, bool Hit)>();

        static int? Bin(double d) => d < 0 || d > 5 ? null : Math.Min(9, (int)(d / 0.5));
        static bool Breached(Ohlcv[] b, int c, double level, bool high)
        {
            for (int t = c + 1; t <= c + Wait; t++)
                if (high ? b[t].High > level : b[t].Low < level) return true;
            return false;
        }

        foreach (var s in mine)
        {
            var b = s.B;
            var equal = SmcMath.EqualLevels(b, s.Swings, s.Atr, EqualAtr);
            var paired = equal.Select(e => (e.KnownAt, e.High)).ToHashSet();
            foreach (var e in equal)
            {
                int c = e.KnownAt;
                if (!s.InEra(c, dev) || !s.AtrOk(c)) continue;
                if (c + Wait >= b.Length) { test.Count("past the last bar"); continue; }
                double d = e.High ? (e.Level - b[c].Close) / s.Atr[c] : (b[c].Close - e.Level) / s.Atr[c];
                if (Bin(d) is not { } bin) { test.Count("distance outside 0 to 5 ATR"); continue; }
                events.Add((b[c].Date.Year, e.High, bin, Breached(b, c, e.Level, e.High)));
            }
            foreach (var w in s.Swings)
            {
                int c = w.KnownAt;
                if (paired.Contains((c, w.High)) || !s.InEra(c, dev) || !s.AtrOk(c) || c + Wait >= b.Length) continue;
                double d = w.High ? (w.Price - b[c].Close) / s.Atr[c] : (b[c].Close - w.Price) / s.Atr[c];
                if (Bin(d) is not { } bin) continue;
                var cell = ctl[w.High][bin];
                ctl[w.High][bin] = (cell.Hit + (Breached(b, c, w.Price, w.High) ? 1 : 0), cell.N + 1);
            }
        }

        foreach (var e in events)
        {
            test.Count("events");
            var cell = ctl[e.High][e.Bin];
            if (cell.N == 0) { test.Count("no control in its distance bin"); continue; }
            test.Obs.Add(new Obs(e.Year, (e.Hit ? 1 : 0) - cell.Hit / (double)cell.N));
            test.EventGross.Add(e.Hit ? 1 : 0);
        }
        test.Counts["control swings"] = ctl.Values.Sum(a => a.Sum(x => x.N));
    }

    // ── L1, L2: a sweep reverses ──────────────────────────────────────────────

    private static void SweepTests(List<Series> mine, bool dev, Test l1, Test l2)
    {
        foreach (var s in mine)
        {
            var b = s.B;
            var entries = EraBars(s, s.CloseBars, dev);
            var rng = new Random(s.Seed ^ 0x51E);
            foreach (var take in SmcMath.Takes(b, s.Swings))
            {
                int t = take.At;
                if (!s.InEra(t, dev) || !s.AtrOk(t)) continue;
                if (t + Horizon >= b.Length) { l1.Count("past the last bar"); continue; }
                bool isLong = !take.High;   // a high taken is shorted, a low taken is bought
                double a = s.Atr[t];
                double r = R(b, t, b[t].Close, a, isLong);
                int year = b[t].Date.Year;
                l2.Obs.Add(new Obs(year, r, take.Sweep ? 0 : 1));
                if (!take.Sweep) { l2.Count("breakouts"); continue; }
                l2.Count("sweeps");
                l1.Count("events");
                l1.EventGross.Add(r);
                l1.EventNet.Add(r - CostR(b[t].Close, a));
                var rnd = new List<double>();
                for (int k = 0; k < Draws && entries.Count > 0; k++)
                {
                    int j = entries[rng.Next(entries.Count)];
                    rnd.Add(R(b, j, b[j].Close, s.Atr[j], isLong));
                }
                if (rnd.Count > 0) l1.Obs.Add(new Obs(year, r - rnd.Average()));
            }
        }
    }

    // ── P1, P2: buy discount, sell premium ────────────────────────────────────

    private static void RangeTests(List<Series> mine, bool dev, Test p1, Test p2)
    {
        var provider = new PremiumDiscountProvider();
        var pars = new Dictionary<string, object> { ["swingBars"] = Span };
        foreach (var s in mine)
        {
            var b = s.B;
            var rd = new Dictionary<string, double[]>();
            provider.Calculate(PremiumDiscountProvider.Code, b, pars, new IndicatorResultBuffer(rd, b.Length));
            var zone = rd[PremiumDiscountProvider.CompZone];
            var structure = rd[PremiumDiscountProvider.CompStructure];
            for (int i = 0; i + Horizon < b.Length; i++)
            {
                if (!s.InEra(i, dev) || !s.AtrOk(i) || double.IsNaN(zone[i]) || zone[i] == 0) continue;
                double a = s.Atr[i];
                double longR = R(b, i, b[i].Close, a, true);
                int year = b[i].Date.Year;
                bool discount = zone[i] < 0;
                p2.Obs.Add(new Obs(year, longR, discount ? 0 : 1));
                p2.Count(discount ? "discount bars" : "premium bars");
                if (structure[i] == 0) continue;
                bool bull = structure[i] > 0;
                // With the teaching: discount in a rising structure (long), premium in a falling one (short).
                bool with = bull == discount;
                double r = bull ? longR : -longR;
                p1.Obs.Add(new Obs(year, r, with ? 0 : 1));
                p1.Count(with ? "with the teaching" : "against it");
                if (with) { p1.EventGross.Add(r); p1.EventNet.Add(r - CostR(b[i].Close, a)); }
            }
        }
    }

    // ── Statistics ────────────────────────────────────────────────────────────

    private static bool CanRun(string id, Test x)
        => x.TwoGroup
            ? x.Obs.Count(o => o.Group == 0) >= MinEvents && x.Obs.Count(o => o.Group == 1) >= MinEvents
            : x.Obs.Count >= MinEvents;

    private static double Stat(IEnumerable<Obs> obs, bool two)
    {
        double s0 = 0, s1 = 0; int n0 = 0, n1 = 0;
        foreach (var o in obs) { if (o.Group == 0) { s0 += o.Value; n0++; } else { s1 += o.Value; n1++; } }
        if (n0 == 0 || (two && n1 == 0)) return double.NaN;
        return two ? s0 / n0 - s1 / n1 : s0 / n0;
    }

    /// <summary>
    /// The calendar-year block bootstrap: every year's outcomes in the class move together. One-sided
    /// p that the statistic is above zero, and its mirror (below zero). A resample with an empty group
    /// counts against the claim in both directions.
    /// </summary>
    private static (double P, double Mirror) Boot(Test x, int seed)
    {
        var years = x.Obs.GroupBy(o => o.Year).Select(g =>
            (S0: g.Where(o => o.Group == 0).Sum(o => o.Value), N0: g.Count(o => o.Group == 0),
             S1: g.Where(o => o.Group == 1).Sum(o => o.Value), N1: g.Count(o => o.Group == 1))).ToArray();
        var rng = new Random(seed);
        int le = 0, ge = 0;
        for (int r = 0; r < Resamples; r++)
        {
            double s0 = 0, s1 = 0; int n0 = 0, n1 = 0;
            for (int k = 0; k < years.Length; k++)
            {
                var y = years[rng.Next(years.Length)];
                s0 += y.S0; n0 += y.N0; s1 += y.S1; n1 += y.N1;
            }
            if (n0 == 0 || (x.TwoGroup && n1 == 0)) { le++; ge++; continue; }
            double v = x.TwoGroup ? s0 / n0 - s1 / n1 : s0 / n0;
            if (v <= 0) le++;
            if (v >= 0) ge++;
        }
        return ((1.0 + le) / (1.0 + Resamples), (1.0 + ge) / (1.0 + Resamples));
    }

    // ── Report ────────────────────────────────────────────────────────────────

    private static void Report(List<Series> series, List<string> skipped, Dictionary<string, Dictionary<string, Test>> t, bool dev)
    {
        Console.WriteLine();
        Console.WriteLine($"═════ SMART MONEY CONCEPTS · {(dev ? "DEVELOPMENT ERA (bars before 2023-01-01)" : "HELD-OUT ERA (2023-01-01 on): SIGNS ONLY")} ═════");
        foreach (var cls in Classes)
            Console.WriteLine($"  {cls}: {series.Count(s => s.Cls == cls)} files ({string.Join(", ", series.Where(s => s.Cls == cls).Select(s => s.File.Replace(".json", "")))})");
        if (skipped.Count > 0) Console.WriteLine($"  skipped under 500 bars: {string.Join(", ", skipped)}");
        Console.WriteLine();

        var passed = Primaries.ToDictionary(id => id, _ => new List<string>());
        var opposite = Primaries.ToDictionary(id => id, _ => new List<string>());
        foreach (var cls in Classes)
        {
            Console.WriteLine($"── {cls} ──");
            var tests = t[cls];
            var raw = new Dictionary<string, (double P, double M)>();
            int salt = 0;
            foreach (var (id, x) in tests.OrderBy(k => Order(k.Key)))
            {
                salt++;
                if (dev && CanRun(id, x)) raw[id] = Boot(x, StableSeed.From(cls + id) ^ salt);
            }
            var holm = dev ? SetupGraderCore.Holm(Primaries.Select(id => raw.TryGetValue(id, out var r) ? r.P : double.NaN).ToList()) : null;

            foreach (var (id, x) in tests.OrderBy(k => Order(k.Key)))
            {
                double stat = Stat(x.Obs, x.TwoGroup);
                string counts = string.Join(", ", x.Counts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"));
                string arm = x.EventGross.Count == 0 ? ""
                    : $"  event arm gross {x.EventGross.Average():+0.000;-0.000}" + (x.EventNet.Count > 0 ? $", net {x.EventNet.Average():+0.000;-0.000}" : "");
                if (!CanRun(id, x))
                {
                    Console.WriteLine($"  {id,-18} could not run (fewer than {MinEvents}). {counts}");
                    continue;
                }
                if (!dev)
                {
                    Console.WriteLine($"  {id,-18} stat {stat:+0.0000;-0.0000}  sign {(stat > 0 ? "+" : stat < 0 ? "-" : "0")}  n {x.Obs.Count}{arm}");
                    continue;
                }
                var (p, m) = raw[id];
                int pi = Array.IndexOf(Primaries, id);
                string h = pi >= 0 ? $"  Holm {holm![pi]:0.0000}" : "  (unadjusted)";
                Console.WriteLine($"  {id,-18} stat {stat:+0.0000;-0.0000}  p {p:0.0000}{h}  mirror p {m:0.0000}  n {x.Obs.Count}{arm}");
                Console.WriteLine($"  {"",-18} {counts}");
                if (pi >= 0)
                {
                    if (holm![pi] < 0.05) passed[id].Add(cls);
                    if (m < 0.05) opposite[id].Add(cls);
                }
            }
            Console.WriteLine();
        }

        if (!dev) return;
        Console.WriteLine("── development-era verdicts (the held-out sign is still to be read) ──");
        foreach (var id in Primaries)
        {
            int n = passed[id].Count;
            // Section 4: information needs no class significant the other way; two passes with one
            // the other way is neither "has information" nor "fragile", so it is none.
            string v = opposite[id].Count > 0 && n >= 2 ? "no information: significant the other way in " + string.Join(", ", opposite[id])
                : n >= 2 ? "has information, if the held-out sign agrees in " + string.Join(", ", passed[id])
                : n == 1 ? "fragile (one class: " + passed[id][0] + ")"
                : "no information";
            string other = opposite[id].Count > 0 ? $"; mirror p < 0.05 in {string.Join(", ", opposite[id])}" : "";
            Console.WriteLine($"  {id}: passes Holm in {n} of 3 classes → {v}{other}");
        }
    }

    private static int Order(string id) => id switch
    {
        "F1" => 0, "F1 random anchor" => 1, "F2" => 2, "O1" => 3, "O1 random anchor" => 4,
        "E1" => 5, "L1" => 6, "L2" => 7, "P1" => 8, "P2" => 9, _ => 10,
    };
}
