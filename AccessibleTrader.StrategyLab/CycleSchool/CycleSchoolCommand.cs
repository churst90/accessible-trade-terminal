using System.Globalization;
using System.Text;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab.CycleSchool;

/// <summary>
/// <c>cycle-school</c>: the frozen pre-registration <c>docs/CYCLE_SCHOOL_PREREG.md</c> (with its
/// 2026-10-18 addendum), built literally. Four schools of test, never pooled across classes:
/// (a) Camel's real-time low-calling checklist, (b) our capitulation score against its price-only
/// self, (c) Camel's macro theses, (d) a trading version of (a) and (b) that cannot rescue them.
///
/// <para><b>Modes.</b> <c>--counts-only</c> prints only how many calls, lows, events and signals each
/// test has, per class and era, so the minimum-event rules and the pre-registered fallbacks can be
/// settled before any outcome is computed. The full run computes every test, the development era
/// first and the held-out era after it, and writes a JSON record.</para>
///
/// <para><b>The traps it is built around.</b> Reference lows only from their confirming bar;
/// completed weeks only; expanding percentiles only; macro values only from their known-from day
/// (ALFRED vintages for revised data, the CFTC release for COT, NBER announcement dates, BAA10Y in
/// place of ICE spreads, NFCI not used); every random draw seeded; the sha256 of every input before
/// and after.</para>
/// </summary>
public static class CycleSchoolCommand
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed class TestRow
    {
        public string Id = "", Family = "", Class = "", Count = "", Status = "";
        public int Calls, Lows;
        public double Observed = double.NaN, NullMean = double.NaN, P = double.NaN, PHolm = double.NaN;
        public bool Significant => !double.IsNaN(PHolm) && PHolm < 0.05;
        public string Note = "";
    }

    private static readonly string[] Classes = { "crypto", "equity", "gold" };

    public static async Task<int> RunAsync(string dataRoot, bool countsOnly, string outDir, int draws, int surrogates, int tradeDraws)
    {
        await Task.Yield();
        var io = new CycleSchoolInputs(dataRoot);
        var report = new Dictionary<string, object>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"cycle-school ({(countsOnly ? "counts only" : "full run")}) — data {io.Root}");

        // ── Load ─────────────────────────────────────────────────────────────
        var defs = new (string Name, string Class, string File)[]
        {
            ("BTC", "crypto", "bitstamp_BTC_USDT_1d.json"), ("ETH", "crypto", "bitstamp_ETH_USDT_1d.json"),
            ("SPY", "equity", "yahoo_SPY_1d.json"), ("QQQ", "equity", "yahoo_QQQ_1d.json"),
            ("DIA", "equity", "yahoo_DIA_1d.json"), ("IWM", "equity", "yahoo_IWM_1d.json"),
            ("GLD", "gold", "yahoo_GLD_1d.json"),
        };
        var assets = defs.Select(d => new Asset(d.Name, d.Class, d.File, io.Bars(d.File))).ToList();
        var cryptoU = new[] { "BTC", "ETH", "LTC", "XRP", "BCH", "ADA", "SOL", "DOGE" }
            .Select(s => assets.FirstOrDefault(a => a.Name == s) ?? new Asset(s, "crypto", $"bitstamp_{s}_USDT_1d.json", io.Bars($"bitstamp_{s}_USDT_1d.json"))).ToList();
        var equityU = new[] { "XLB", "XLE", "XLF", "XLI", "XLK", "XLP", "XLU", "XLV", "XLY" }
            .Select(s => new Asset(s, "equity", $"yahoo_{s}_1d.json", io.Bars($"yahoo_{s}_1d.json"))).ToList();
        var tlt = io.Bars("yahoo_TLT_1d.json");
        IReadOnlyList<Asset>? UniverseOf(Asset a) => a.Crypto ? cryptoU : a.Equity ? equityU : null;
        var ext = CycleSchoolScore.LoadExternal(io);
        Console.WriteLine($"  loaded in {sw.Elapsed.TotalSeconds:F0}s");

        var runs = assets.Select(a => AssetRun.Build(a, UniverseOf(a), ext)).ToList();
        List<AssetRun> Cls(string c) => runs.Where(r => r.A.Class == c).ToList();
        Console.WriteLine($"  built in {sw.Elapsed.TotalSeconds:F0}s");

        var dets = Detectors();
        Det D(string id) => dets.First(d => d.Id == id);

        // ── Counts (every mode) ──────────────────────────────────────────────
        var counts = new List<Dictionary<string, object>>();
        foreach (var c in Classes)
            foreach (var e in new[] { Era.Dev, Era.Held })
            {
                var cr = Cls(c);
                var row = new Dictionary<string, object> { ["class"] = c, ["era"] = e.ToString() };
                row["reference_lows_GD"] = cr.Sum(r => r.GD.Count(l => r.EraOf(l.Low) == e));
                row["reference_lows_GW"] = cr.Sum(r => r.GW.Count(l => r.EraOf(l.Low) == e));
                foreach (var d in dets)
                {
                    int scored = 0, excl = 0;
                    foreach (var r in cr)
                    {
                        var h = r.Hits(d.Window);
                        foreach (var t in CycleSchoolStats.InEra(r, d.Calls(r), e)) { if (h[t] is null) excl++; else scored++; }
                    }
                    row[$"calls_{d.Id}"] = scored;
                    row[$"excluded_{d.Id}"] = excl;
                }
                counts.Add(row);
            }
        report["counts"] = counts;
        report["per_asset_counts"] = runs.Select(r => new Dictionary<string, object>
        {
            ["asset"] = r.A.Name, ["first_bar"] = r.B[0].Day.ToString("yyyy-MM-dd"), ["last_bar"] = r.B[^1].Day.ToString("yyyy-MM-dd"),
            ["bars"] = r.N, ["GD_dev"] = r.GD.Count(l => r.EraOf(l.Low) == Era.Dev), ["GW_dev"] = r.GW.Count(l => r.EraOf(l.Low) == Era.Dev),
            ["AP_dev"] = r.AP.Count(c => r.EraOf(c.I) == Era.Dev), ["ALERT_dev"] = r.Alert.Count(t => r.EraOf(t) == Era.Dev),
            ["CONF_dev"] = r.Conf.Count(c => r.EraOf(c.I) == Era.Dev), ["A7_dev"] = r.A7.Count(t => r.EraOf(t) == Era.Dev),
        }).ToList();

        // Macro event counts and the A8 / B7 / B3 / D counts.
        var nber = io.Nber();
        var spyRun = runs.First(r => r.A.Name == "SPY");
        var btcBars = assets.First(a => a.Name == "BTC").Bars;
        var macro = new List<MacroResult>
        {
            CycleSchoolMacro.C1(io, spyRun.B, "C1-SPY", spyRun.B[0].Day, draws, countsOnly),
            CycleSchoolMacro.C1(io, btcBars, "C1-BTC", btcBars[0].Day, draws, countsOnly),
            CycleSchoolMacro.C2(io, tlt, draws, countsOnly),
            CycleSchoolMacro.C3(io, spyRun.B, nber, draws, countsOnly),
            CycleSchoolMacro.C5(io, countsOnly),
            CycleSchoolMacro.C6(io, spyRun, draws, countsOnly),
        };
        report["macro_counts"] = macro.Select(m => new { m.Id, dev_thesis_group = m.DevA, dev_other_group = m.DevB, held_thesis_group = m.HeldA, held_other_group = m.HeldB, testable = m.Testable }).ToList();
        Console.WriteLine();
        Console.WriteLine("── COUNTS (no outcome) ──");
        PrintCounts(counts, dets);
        foreach (var m in macro)
            Console.WriteLine($"  {m.Id,-7} dev {m.DevA,4} vs {m.DevB,4}   held {m.HeldA,3} vs {m.HeldB,3}   {(m.Testable ? "testable" : "NOT testable (<20 per group)")}");

        var a8 = new Dictionary<string, A8Result>();
        var b7 = new Dictionary<string, (int W, int D, double Diff, double P)>();
        foreach (var c in Classes)
        {
            a8[c] = A8(Cls(c), Era.Dev, draws, countsOnly);
            Console.WriteLine($"  A8 {c,-6} dev candidates {a8[c].Candidates}, IN events {a8[c].In}, unresolved {a8[c].Unresolved}");
            if (c != "gold")
            {
                b7[c] = B7(Cls(c), draws, countsOnly);
                Console.WriteLine($"  B7 {c,-6} G_W lows with sentiment {b7[c].W}, other G_D lows with sentiment {b7[c].D}");
            }
        }
        var tradeCounts = new List<object>();
        foreach (var c in Classes)
            foreach (var (did, det) in new[] { ("D1", D("CONF")), ("D2", D("A-P")) })
            {
                int taken = 0, skipped = 0, past = 0;
                foreach (var r in Cls(c))
                    foreach (var (i, j) in Signals(r, did, Era.Dev))
                    {
                        var (_, st) = CycleSchoolCore.CamelTrade(r.B, r.Atr, r.Sma10, i, r.B[j].Low);
                        if (st == CycleSchoolCore.TradeStatus.Taken) taken++; else if (st == CycleSchoolCore.TradeStatus.Skipped) skipped++; else past++;
                    }
                Console.WriteLine($"  {did} {c,-6} dev signals {taken + skipped + past}: taken {taken}, skipped (open at or below the stop) {skipped}, past the data {past}");
                tradeCounts.Add(new { test = did, @class = c, taken, skipped, past });
            }
        report["trade_counts"] = tradeCounts;
        report["a8_counts"] = a8.ToDictionary(k => k.Key, k => new { k.Value.Candidates, k.Value.In, k.Value.Unresolved });
        report["b7_counts"] = b7.ToDictionary(k => k.Key, k => new { G_W = k.Value.W, other_G_D = k.Value.D });

        // Fallbacks (prereg 3.7), decided from counts alone.
        var fallbackA = Classes.Where(c => Calls(Cls(c), D("A-P"), Era.Dev) < 30).ToList();
        var fallbackB = Classes.Where(c => Calls(Cls(c), D("ALERT"), Era.Dev) < 30).ToList();
        Console.WriteLine($"  fallback 2-of-4 checklist needed in: {(fallbackA.Count == 0 ? "none" : string.Join(", ", fallbackA))}");
        Console.WriteLine($"  fallback ALERT at pctCAP >= 90 needed in: {(fallbackB.Count == 0 ? "none" : string.Join(", ", fallbackB))}");
        report["fallback_checklist_classes"] = fallbackA;
        report["fallback_alert_classes"] = fallbackB;

        if (countsOnly)
        {
            report["inputs"] = HashReport(io);
            Write(report, outDir, "cycle_school_counts.json");
            Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:F0}s");
            return 0;
        }

        // ══ Full run: development era ════════════════════════════════════════
        var tests = new List<TestRow>();
        var detail = new Dictionary<string, object>();

        foreach (var c in Classes)
        {
            var cr = Cls(c);
            var ap = CycleSchoolStats.Observe(cr, D("A-P"), Era.Dev);
            var a1 = CycleSchoolStats.N1(cr, D("A-P"), Era.Dev, "A1", ap.Precision, draws);
            var a2 = CycleSchoolStats.N2(cr, D("A-P"), Era.Dev, "A2", ap.Precision, draws);
            var a3 = N3(cr, Era.Dev, "A3", ap.Precision, surrogates, CycleSchoolCore.RulePrimary);
            tests.Add(Row("A1", "A-primary", c, ap, LowsGD(cr), a1.Mean, a1.P));
            tests.Add(Row("A2", "A-primary", c, ap, LowsGD(cr), a2.Mean, a2.P));
            tests.Add(Row("A3", "A-primary", c, ap, LowsGD(cr), a3.P95, a3.P, $"N3 mean {a3.Mean:F3}, 95th percentile {a3.P95:F3}"));
            detail[$"A-P/{c}"] = Describe(ap, a1, a2, a3);

            var v1 = CycleSchoolStats.Observe(cr, D("A-V1"), Era.Dev);
            var a4 = CycleSchoolStats.N2(cr, D("A-V1"), Era.Dev, "A4", v1.Precision, draws);
            tests.Add(Row("A4", "A-secondary", c, v1, LowsGD(cr), a4.Mean, a4.P));
            detail[$"A-V1/{c}"] = Describe(v1, null, a4, null);
            var v2 = CycleSchoolStats.Observe(cr, D("A-V2"), Era.Dev);
            var a5 = CycleSchoolStats.N2(cr, D("A-V2"), Era.Dev, "A5", v2.Precision, draws);
            tests.Add(Row("A5", "A-secondary", c, v2, LowsGD(cr), a5.Mean, a5.P));
            detail[$"A-V2/{c}"] = Describe(v2, null, a5, null);
            var nw = CycleSchoolStats.Observe(cr, D("A-NW"), Era.Dev);
            var (a6d, a6p) = CycleSchoolStats.YearBootstrap(cr, D("A-P"), D("A-NW"), Era.Dev, "A6", draws);
            var a6 = Row("A6", "A-secondary", c, ap, LowsGD(cr), nw.Precision, a6p, $"A-P {ap.Precision:F3} minus no-window {nw.Precision:F3} = {a6d:+0.000;-0.000}; no-window calls {nw.Calls}");
            a6.Observed = a6d; a6.Calls = Math.Min(ap.Calls, nw.Calls);
            a6.Status = CountStatus(a6.Calls, a6.Lows);
            tests.Add(a6);
            detail[$"A-NW/{c}"] = Describe(nw, null, null, null);
            var w7 = CycleSchoolStats.Observe(cr, D("A7"), Era.Dev);
            var a7 = CycleSchoolStats.N2(cr, D("A7"), Era.Dev, "A7", w7.Precision, draws);
            tests.Add(Row("A7", "A-secondary", c, w7, cr.Sum(r => r.GW.Count(l => r.EraOf(l.Low) == Era.Dev)), a7.Mean, a7.P));
            detail[$"A7/{c}"] = Describe(w7, null, a7, null);
            var r8 = a8[c];
            tests.Add(new TestRow
            {
                Id = "A8", Family = "A-secondary", Class = c, Calls = r8.In, Lows = r8.Candidates, Status = CountStatus(r8.In, r8.Candidates),
                Observed = r8.Share - r8.NullRate, NullMean = r8.NullRate, P = r8.P,
                Note = $"IN share {r8.Share:F3} vs survival-matched null {r8.NullRate:F3} (Camel's 80% is a reference, not the test)",
            });

            if (fallbackA.Contains(c))
            {
                var f2 = CycleSchoolStats.Observe(cr, D("A-F2"), Era.Dev);
                var f1n = CycleSchoolStats.N1(cr, D("A-F2"), Era.Dev, "A1-F2", f2.Precision, draws);
                var f2n = CycleSchoolStats.N2(cr, D("A-F2"), Era.Dev, "A2-F2", f2.Precision, draws);
                var f3n = N3(cr, Era.Dev, "A3-F2", f2.Precision, surrogates, CycleSchoolCore.RuleTwoOfFour);
                tests.Add(Row("A1-F2", "A-primary", c, f2, LowsGD(cr), f1n.Mean, f1n.P, "Suggestive whatever its p (prereg 3.7)"));
                tests.Add(Row("A2-F2", "A-primary", c, f2, LowsGD(cr), f2n.Mean, f2n.P, "Suggestive whatever its p (prereg 3.7)"));
                tests.Add(Row("A3-F2", "A-primary", c, f2, LowsGD(cr), f3n.P95, f3n.P, "Suggestive whatever its p (prereg 3.7)"));
                detail[$"A-F2/{c}"] = Describe(f2, f1n, f2n, f3n);
            }

            // (b)
            var al = CycleSchoolStats.Observe(cr, D("ALERT"), Era.Dev);
            var alp = CycleSchoolStats.Observe(cr, D("ALERT_P"), Era.Dev);
            var b1 = CycleSchoolStats.N1(cr, D("ALERT"), Era.Dev, "B1", al.Precision, draws);
            var (b2d, b2p) = CycleSchoolStats.YearBootstrap(cr, D("ALERT"), D("ALERT_P"), Era.Dev, "B2", draws);
            tests.Add(Row("B1", "B-primary", c, al, LowsGD(cr), b1.Mean, b1.P));
            var b2 = Row("B2", "B-primary", c, al, LowsGD(cr), alp.Precision, b2p, $"ALERT {al.Precision:F3} minus ALERT_P {alp.Precision:F3} = {b2d:+0.000;-0.000}; ALERT_P calls {alp.Calls}");
            b2.Observed = b2d; b2.Calls = Math.Min(al.Calls, alp.Calls); b2.Status = CountStatus(b2.Calls, b2.Lows);
            tests.Add(b2);
            var b3 = B3(cr, D("ALERT"), D("ALERT_P"), Era.Dev);
            tests.Add(new TestRow
            {
                Id = "B3", Family = "B-primary", Class = c, Calls = al.Calls, Lows = LowsGD(cr), Status = CountStatus(al.Calls, LowsGD(cr)),
                Observed = b3.Earlier, NullMean = b3.Pairs / 2.0, P = b3.P,
                Note = $"lows hit by both: {b3.Pairs + b3.Ties} ({b3.Ties} ties dropped); ALERT first on {b3.Earlier} of {b3.Pairs}; read only if B1 passes",
            });
            detail[$"ALERT/{c}"] = Describe(al, b1, null, null);
            detail[$"ALERT_P/{c}"] = Describe(alp, CycleSchoolStats.N1(cr, D("ALERT_P"), Era.Dev, "B1-P-descriptive", alp.Precision, draws), null, null);

            var cf = CycleSchoolStats.Observe(cr, D("CONF"), Era.Dev);
            var b4 = CycleSchoolStats.N2(cr, D("CONF"), Era.Dev, "B4", cf.Precision, draws);
            tests.Add(Row("B4", "B-secondary", c, cf, LowsGD(cr), b4.Mean, b4.P));
            detail[$"CONFIRMED/{c}"] = Describe(cf, null, b4, null);
            var nd = CycleSchoolStats.Observe(cr, D("ALERT_ND"), Era.Dev);
            var (b5d, b5p) = CycleSchoolStats.YearBootstrap(cr, D("ALERT"), D("ALERT_ND"), Era.Dev, "B5", draws);
            var b5 = Row("B5", "B-secondary", c, al, LowsGD(cr), nd.Precision, b5p, $"ALERT {al.Precision:F3} minus no-due {nd.Precision:F3} = {b5d:+0.000;-0.000}; no-due calls {nd.Calls}");
            b5.Observed = b5d; b5.Calls = Math.Min(al.Calls, nd.Calls); b5.Status = CountStatus(b5.Calls, b5.Lows);
            tests.Add(b5);
            var aw = CycleSchoolStats.Observe(cr, D("ALERT_W"), Era.Dev);
            var b6 = CycleSchoolStats.N1(cr, D("ALERT_W"), Era.Dev, "B6", aw.Precision, draws);
            tests.Add(Row("B6", "B-secondary", c, aw, cr.Sum(r => r.GW.Count(l => r.EraOf(l.Low) == Era.Dev)), b6.Mean, b6.P));
            detail[$"ALERT_W/{c}"] = Describe(aw, b6, null, null);
            if (c != "gold")
            {
                var r7 = b7[c];
                tests.Add(new TestRow
                {
                    Id = "B7", Family = "B-secondary", Class = c, Calls = r7.W, Lows = r7.W, Status = CountStatus(r7.W, r7.W),
                    Observed = r7.Diff, P = r7.P, Note = $"G_W lows {r7.W}, other G_D lows {r7.D}",
                });
            }
            if (fallbackB.Contains(c))
            {
                var a90 = CycleSchoolStats.Observe(cr, D("ALERT90"), Era.Dev);
                var n90 = CycleSchoolStats.N1(cr, D("ALERT90"), Era.Dev, "B1-90", a90.Precision, draws);
                var (d90, p90) = CycleSchoolStats.YearBootstrap(cr, D("ALERT90"), D("ALERT_P90"), Era.Dev, "B2-90", draws);
                var s90 = B3(cr, D("ALERT90"), D("ALERT_P90"), Era.Dev);
                tests.Add(Row("B1-90", "B-primary", c, a90, LowsGD(cr), n90.Mean, n90.P, "Suggestive whatever its p (prereg 3.7)"));
                var r90 = Row("B2-90", "B-primary", c, a90, LowsGD(cr), double.NaN, p90, $"difference {d90:+0.000;-0.000}; Suggestive whatever its p");
                r90.Observed = d90;
                tests.Add(r90);
                tests.Add(new TestRow { Id = "B3-90", Family = "B-primary", Class = c, Calls = a90.Calls, Lows = LowsGD(cr), Status = CountStatus(a90.Calls, LowsGD(cr)), Observed = s90.Earlier, P = s90.P, Note = $"pairs {s90.Pairs}; Suggestive whatever its p" });
            }
        }

        // (c)
        var cIds = new[] { "C1-SPY", "C1-BTC", "C2", "C3", "C5", "C6" };
        foreach (var m in macro)
            tests.Add(new TestRow
            {
                Id = m.Id, Family = "C", Class = m.Id == "C1-BTC" ? "crypto" : "equity", Calls = Math.Min(m.DevA, m.DevB), Lows = 0,
                Status = m.Testable ? "confirmatory" : "not testable", Observed = m.DevDiff, P = m.Testable ? m.P : double.NaN, Note = m.Statistic,
            });

        // (d)
        var trades = new Dictionary<string, TradeResult>();
        foreach (var c in Classes)
            foreach (var did in new[] { "D1", "D2" })
            {
                var tr = Trades(Cls(c), did, Era.Dev, tradeDraws);
                trades[$"{did}/{c}"] = tr;
                tests.Add(new TestRow
                {
                    Id = did, Family = "D", Class = c, Calls = tr.N, Lows = LowsGD(Cls(c)), Status = CountStatus(tr.N, LowsGD(Cls(c))),
                    Observed = tr.MeanNet, NullMean = tr.ControlMean, P = tr.P,
                    Note = $"net R {tr.MeanNet:+0.000;-0.000} (gross {tr.MeanGross:+0.000;-0.000}) vs control {tr.ControlMean:+0.000;-0.000}; standard harness {tr.StandardNet:+0.000;-0.000}" +
                           (did == "D1" ? $"; CAP_P CONFIRMED {tr.CapPNet:+0.000;-0.000} over {tr.CapPN}" : ""),
                });
            }

        // Holm inside each family; untestable tests count as p = 1 so the family size stays as registered.
        foreach (var fam in tests.Select(t => t.Family).Distinct())
        {
            var ft = tests.Where(t => t.Family == fam).ToList();
            var ps = ft.Select(t => t.Status == "not testable" || double.IsNaN(t.P) ? 1.0 : t.P).ToList();
            var adj = CycleSchoolCore.Holm(ps);
            for (int k = 0; k < ft.Count; k++) ft[k].PHolm = ft[k].Status == "not testable" ? double.NaN : adj[k];
        }

        // ══ Robustness: noise (development era) ═════════════════════════════
        Console.WriteLine();
        Console.WriteLine($"  development tests done in {sw.Elapsed.TotalSeconds:F0}s; noise runs next");
        var cleanA2 = Classes.ToDictionary(c => c, c => Excess(Cls(c), D("A-P"), Era.Dev, "A2", draws));
        var cleanB2 = Classes.ToDictionary(c => c, c => PrecDiff(Cls(c), D("ALERT"), D("ALERT_P"), Era.Dev));
        var cleanD = trades.ToDictionary(k => k.Key, k => k.Value.MeanNet - k.Value.ControlMean);
        var noisy = new List<Dictionary<string, double>>();
        for (int seed = 0; seed < 3; seed++)
        {
            var nr = NoisyRuns(assets, cryptoU, equityU, ext, seed);
            List<AssetRun> NC(string c) => nr.Where(r => r.A.Class == c).ToList();
            var row = new Dictionary<string, double>();
            foreach (var c in Classes)
            {
                row[$"A2/{c}"] = Excess(NC(c), D("A-P"), Era.Dev, $"A2-noise{seed}", draws);
                row[$"B2/{c}"] = PrecDiff(NC(c), D("ALERT"), D("ALERT_P"), Era.Dev);
                foreach (var did in new[] { "D1", "D2" })
                {
                    var t = Trades(NC(c), did, Era.Dev, tradeDraws, $"-noise{seed}");
                    row[$"{did}/{c}"] = t.MeanNet - t.ControlMean;
                }
            }
            noisy.Add(row);
            Console.WriteLine($"  noise seed {seed} done ({sw.Elapsed.TotalSeconds:F0}s)");
        }
        double Retained(string key, double clean)
        {
            var v = noisy.Select(r => r[key]).Where(x => !double.IsNaN(x)).ToList();
            return v.Count == 0 || clean == 0 || double.IsNaN(clean) ? double.NaN : v.Average() / clean;
        }

        // ══ Held-out era: read once, after every development test ═══════════
        var held = new Dictionary<string, object>();
        var heldA = new Dictionary<string, (int Calls, double Prec, double N2Mean)>();
        var heldB = new Dictionary<string, (int Calls, double Prec, double N1Mean, double PrecP)>();
        var heldD = new Dictionary<string, TradeResult>();
        foreach (var c in Classes)
        {
            var cr = Cls(c);
            var ap = CycleSchoolStats.Observe(cr, D("A-P"), Era.Held);
            var n2 = CycleSchoolStats.N2(cr, D("A-P"), Era.Held, "A2-held", ap.Precision, draws);
            heldA[c] = (ap.Calls, ap.Precision, n2.Mean);
            var al = CycleSchoolStats.Observe(cr, D("ALERT"), Era.Held);
            var alp = CycleSchoolStats.Observe(cr, D("ALERT_P"), Era.Held);
            var n1 = CycleSchoolStats.N1(cr, D("ALERT"), Era.Held, "B1-held", al.Precision, draws);
            heldB[c] = (al.Calls, al.Precision, n1.Mean, alp.Precision);
            held[$"A-P/{c}"] = Describe(ap, null, n2, null);
            held[$"ALERT/{c}"] = Describe(al, n1, null, null);
            held[$"ALERT_P/{c}"] = Describe(alp, null, null, null);
            foreach (var did in new[] { "D1", "D2" })
            {
                var t = Trades(cr, did, Era.Held, tradeDraws, "-held");
                heldD[$"{did}/{c}"] = t;
                held[$"{did}/{c}"] = t;
            }
            foreach (var did in new[] { "A-V1", "A-V2", "CONF", "A7", "ALERT_W" })
                held[$"{did}/{c}"] = Describe(CycleSchoolStats.Observe(cr, D(did), Era.Held), null, null, null);
        }
        var heldMacro = macro.ToDictionary(m => m.Id, m => new { m.HeldA, m.HeldB, m.HeldDiff, sameSign = !double.IsNaN(m.HeldDiff) && Math.Sign(m.HeldDiff) == Math.Sign(m.DevDiff) });

        // ══ Verdicts in the prereg's words ═══════════════════════════════════
        var verdicts = new Dictionary<string, string>();
        var verdictWhy = new Dictionary<string, List<string>>();
        foreach (var c in Classes)
        {
            var cr = Cls(c);
            // (a)
            var why = new List<string>();
            TestRow T(string id) => tests.First(t => t.Id == id && t.Class == c);
            var apDev = CycleSchoolStats.Observe(cr, D("A-P"), Era.Dev);
            bool a123 = T("A1").Significant && T("A2").Significant && T("A3").Significant;
            bool lead = apDev.MedianLead > 0;
            bool heldOkA = heldA[c].Calls >= 10 && heldA[c].Prec - heldA[c].N2Mean > 0;
            bool heldCheckA = heldA[c].Calls >= 10;
            var n2Dev = CycleSchoolStats.N2(cr, D("A-P"), Era.Dev, "A2", apDev.Precision, draws);
            int posAssets = cr.Count(r => apDev.PerAsset[r.A.Name].Calls > 0 &&
                (double)apDev.PerAsset[r.A.Name].Hits / apDev.PerAsset[r.A.Name].Calls - n2Dev.PerAssetMean[r.A.Name] > 0);
            bool breadthA = c == "gold" || posAssets * 2 > cr.Count;
            double retA = Retained($"A2/{c}", cleanA2[c]);
            bool noiseA = retA >= 0.5;
            why.Add($"A1/A2/A3 significant after Holm: {T("A1").Significant}/{T("A2").Significant}/{T("A3").Significant}");
            why.Add($"median lead over the confirmation {apDev.MedianLead:F1} bars (> 0: {lead})");
            why.Add($"held-out: {heldA[c].Calls} calls, precision {heldA[c].Prec:F3} vs N2 mean {heldA[c].N2Mean:F3} ({(heldCheckA ? (heldOkA ? "positive" : "not positive") : "not checkable")})");
            why.Add($"assets with precision above their N2 mean: {posAssets} of {cr.Count}");
            why.Add($"excess over N2 kept at 25% noise: {retA:P0}");
            bool countsSuggestive = T("A2").Status == "suggestive";
            verdicts[$"a/{c}"] = T("A2").Status == "not testable" ? "Not testable"
                : a123 && lead && heldOkA && breadthA && noiseA && !countsSuggestive ? "Supported"
                : a123 && lead && heldOkA && breadthA && !countsSuggestive ? "Fragile"
                : (T("A2").P < 0.05 && !T("A2").Significant) || countsSuggestive || (!heldCheckA && T("A2").P < 0.05) ? "Suggestive"
                : "No edge";
            verdictWhy[$"a/{c}"] = why;

            // (b)
            why = new List<string>();
            bool b12 = T("B1").Significant && T("B2").Significant;
            bool heldOkB = heldB[c].Calls >= 10 && heldB[c].Prec > heldB[c].N1Mean && heldB[c].Prec > heldB[c].PrecP;
            bool heldCheckB = heldB[c].Calls >= 10;
            var alDev = CycleSchoolStats.Observe(cr, D("ALERT"), Era.Dev);
            var alpDev = CycleSchoolStats.Observe(cr, D("ALERT_P"), Era.Dev);
            int posB = cr.Count(r => Prec(alDev.PerAsset[r.A.Name]) - Prec(alpDev.PerAsset[r.A.Name]) > 0);
            bool breadthB = c == "gold" || posB * 2 > cr.Count;
            double retB = Retained($"B2/{c}", cleanB2[c]);
            why.Add($"B1/B2 significant after Holm: {T("B1").Significant}/{T("B2").Significant}");
            why.Add($"held-out: {heldB[c].Calls} alerts, precision {heldB[c].Prec:F3} vs N1 mean {heldB[c].N1Mean:F3} and ALERT_P {heldB[c].PrecP:F3} ({(heldCheckB ? (heldOkB ? "both above" : "not both above") : "not checkable")})");
            why.Add($"assets where ALERT beats ALERT_P: {posB} of {cr.Count}");
            why.Add($"B2 difference kept at 25% price noise: {retB:P0}");
            bool sugB = T("B2").Status == "suggestive";
            verdicts[$"b/{c}"] = T("B1").Status == "not testable" ? "Not testable"
                : b12 && heldOkB && breadthB && retB >= 0.5 && !sugB ? "Supported"
                : b12 && heldOkB && breadthB && !sugB ? "Fragile"
                : T("B1").Significant && !T("B2").Significant ? "No edge from the added data"
                : (T("B2").P < 0.05 && !T("B2").Significant) || sugB || (!heldCheckB && T("B2").P < 0.05) ? "Suggestive"
                : "No edge";
            verdictWhy[$"b/{c}"] = why;
        }
        foreach (var id in cIds)
        {
            var t = tests.First(x => x.Id == id);
            var m = macro.First(x => x.Id == id);
            bool sameSign = !double.IsNaN(m.HeldDiff) && Math.Sign(m.HeldDiff) == Math.Sign(m.DevDiff);
            verdicts[$"c/{id}"] = t.Status == "not testable" ? "Not testable"
                : t.Significant && m.HeldCheckable && sameSign ? "Supported"
                : t.Significant && !m.HeldCheckable ? "Suggestive (held-out not checkable)"
                : "No support";
            verdictWhy[$"c/{id}"] = new() { $"dev {m.DevDiff:+0.0000;-0.0000} (groups {m.DevA}/{m.DevB}), p {t.P:F4}, Holm {t.PHolm:F4}; held-out {m.HeldDiff:+0.0000;-0.0000} (groups {m.HeldA}/{m.HeldB}), same sign {sameSign}" };
        }
        foreach (var c in Classes)
            foreach (var did in new[] { "D1", "D2" })
            {
                var t = tests.First(x => x.Id == did && x.Class == c);
                string gate = did == "D1" ? verdicts[$"b/{c}"] : verdicts[$"a/{c}"];
                bool gateOk = gate is "Supported" or "Fragile";
                var h = heldD[$"{did}/{c}"];
                bool heldOk = h.N >= 10 && h.MeanNet > h.ControlMean;
                double ret = Retained($"{did}/{c}", cleanD[$"{did}/{c}"]);
                verdicts[$"d/{did}/{c}"] = !gateOk ? "No edge (the detector failed)"
                    : t.Significant && heldOk && ret >= 0.5 ? "Supported" : "No edge";
                verdictWhy[$"d/{did}/{c}"] = new() { $"detector verdict {gate}; dev net R {t.Observed:+0.000;-0.000} vs control {t.NullMean:+0.000;-0.000}, p {t.P:F4}, Holm {t.PHolm:F4}; held-out {h.N} trades net R {h.MeanNet:+0.000;-0.000} vs control {h.ControlMean:+0.000;-0.000}; kept at noise {ret:P0}" };
            }

        // ══ Descriptives (prereg 5.3) ════════════════════════════════════════
        var desc = Descriptives(runs, assets, UniverseOf, ext, D("ALERT"));

        report["tests"] = tests;
        report["detail_dev"] = detail;
        report["held_out"] = held;
        report["held_out_macro"] = heldMacro;
        report["macro"] = macro;
        // Pre-registered descriptive rows of C5 (not tests): the 2-year yield in place of the
        // 3-month bill, and the unscheduled meetings on their own.
        report["c5_descriptive"] = new[]
        {
            CycleSchoolMacro.C5(io, false, "macro/fred/DGS2.csv", "C5-DGS2"),
            CycleSchoolMacro.C5(io, false, "macro/fred/DGS3MO.csv", "C5-unscheduled", unscheduled: true),
        };
        report["c4"] = CycleSchoolMacro.C4(new() { ["SPY"] = spyRun.B, ["GLD"] = assets.First(a => a.Name == "GLD").Bars, ["BTC"] = btcBars }, nber);
        report["trades_dev"] = trades;
        report["noise"] = new { clean_A2 = cleanA2, clean_B2 = cleanB2, clean_D = cleanD, seeds = noisy };
        report["verdicts"] = verdicts;
        report["verdict_reasons"] = verdictWhy;
        report["descriptive"] = desc;
        report["a8"] = a8;
        report["inputs"] = HashReport(io);
        Write(report, outDir, "cycle_school_full.json");
        PrintFull(tests, verdicts, verdictWhy);
        Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    private static double Prec((int Calls, int Hits) x) => x.Calls == 0 ? double.NaN : (double)x.Hits / x.Calls;

    // ── Detectors ────────────────────────────────────────────────────────────

    public static List<Det> Detectors() => new()
    {
        new("A-P", "D-post", r => r.AP.Select(c => c.I).ToList(), r => r.SwingElig),
        new("A-V1", "D-post", r => r.AV1.Select(c => c.I).ToList(), r => r.SwingElig),
        new("A-V2", "D-post", r => r.AV2.Select(c => c.I).ToList(), r => r.SwingElig),
        new("A-F2", "D-post", r => r.AF2.Select(c => c.I).ToList(), r => r.SwingElig),
        new("A-NW", "D-post", r => r.ANW.Select(c => c.I).ToList(), r => r.SwingElig),
        new("A7", "W-post", r => r.A7, r => r.A7Elig),
        new("ALERT", "D-alert", r => r.Alert),
        new("ALERT_P", "D-alert", r => r.AlertP),
        new("ALERT_ND", "D-alert", r => r.AlertND),
        new("ALERT90", "D-alert", r => r.Alert90),
        new("ALERT_P90", "D-alert", r => r.AlertP90),
        new("ALERT_W", "W-alert", r => r.AlertW),
        new("CONF", "D-post", r => r.Conf.Select(c => c.I).ToList(), r => r.ConfElig),
        new("CONF_P", "D-post", r => r.ConfP.Select(c => c.I).ToList(), r => r.ConfElig),
    };

    private static int Calls(List<AssetRun> cr, Det d, Era e) =>
        cr.Sum(r => { var h = r.Hits(d.Window); return CycleSchoolStats.InEra(r, d.Calls(r), e).Count(t => h[t] is not null); });

    private static int LowsGD(List<AssetRun> cr) => cr.Sum(r => r.GD.Count(l => r.EraOf(l.Low) == Era.Dev));

    /// <summary>Prereg 3.7: at least 30 calls and 30 lows confirmatory; 15 to 29 calls suggestive;
    /// below 15 calls not testable. Fewer than 30 lows only stops a test being confirmatory: the
    /// not-testable rule is on calls alone. (Run 2 also called a test with under 15 lows not
    /// testable; that was not the prereg's rule and run 3 fixed it. Both runs are reported.)</summary>
    public static string CountStatus(int calls, int lows) =>
        calls < 15 ? "not testable" : calls < 30 || lows < 30 ? "suggestive" : "confirmatory";

    private static TestRow Row(string id, string fam, string c, EraStats s, int lows, double nullMean, double p, string note = "") => new()
    {
        Id = id, Family = fam, Class = c, Calls = s.Calls, Lows = lows, Status = CountStatus(s.Calls, lows),
        Observed = s.Precision, NullMean = nullMean, P = p, Note = note,
    };

    private static object Describe(EraStats s, NullResult? n1, NullResult? n2, NullResult? n3) => new
    {
        calls = s.Calls, hits = s.Hits, excluded = s.Excluded, precision = s.Precision, recall = s.Recall,
        lows_in_era = s.LowsInEra, median_lag = s.MedianLag, median_lead = s.MedianLead, share_lead_positive = s.ShareLeadPositive,
        false_alarms_per_year = s.FalsePerYear, per_asset = s.PerAsset.ToDictionary(k => k.Key, k => new { k.Value.Calls, k.Value.Hits, precision = Prec(k.Value) }),
        n1_mean = n1?.Mean, n1_p = n1?.P, n2_mean = n2?.Mean, n2_p = n2?.P, n2_per_asset_mean = n2?.PerAssetMean,
        n3_mean = n3?.Mean, n3_p95 = n3?.P95, n3_p = n3?.P,
    };

    // ── N3 ───────────────────────────────────────────────────────────────────

    private static NullResult N3(List<AssetRun> cr, Era e, string test, double observed, int surrogates, Func<CycleSchoolCore.ChecklistBar, bool> rule)
    {
        var vals = new List<double>();
        for (int s = 0; s < surrogates; s++)
        {
            int hits = 0, calls = 0;
            foreach (var r in cr)
            {
                var dev = r.B.Where(b => b.Day < AssetRun.EraCut).ToArray();
                if (dev.Length < 300) continue;
                var sb = AssetRun.Surrogate(dev, CycleSchoolCore.Rng(test, r.A.Name, s));
                var atr = CycleSchoolCore.Atr(sb);
                var gd = CycleSchoolCore.ReferenceLows(sb, atr, 20, 3, out var un);
                double L = r.A.Name is "BTC" or "ETH" ? 60 : r.A.Name == "GLD" ? 25 : 40;
                var ci = CycleSchoolCore.DailyChecklistInputs(sb, gd, L);
                int from = AssetRun.FirstDefined(ci);
                foreach (var c in CycleSchoolCore.ChecklistCalls(ci, rule, from))
                {
                    var sc = CycleSchoolCore.Score(c.I, new CycleSchoolCore.HitWindow(15, 0), gd, un, sb.Length);
                    if (sc is null) continue;
                    calls++;
                    if (sc.Value.Hit) hits++;
                }
            }
            vals.Add(calls == 0 ? double.NaN : (double)hits / calls);
        }
        int ge = vals.Count(v => !double.IsNaN(v) && v >= observed);
        return new NullResult((1.0 + ge) / (1.0 + surrogates), vals.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average(),
            new(), surrogates, CycleSchoolCore.Quantile(vals, 0.95));
    }

    private static double Excess(List<AssetRun> cr, Det d, Era e, string test, int draws)
    {
        var s = CycleSchoolStats.Observe(cr, d, e);
        var n2 = CycleSchoolStats.N2(cr, d, e, test, s.Precision, draws);
        return s.Precision - n2.Mean;
    }

    private static double PrecDiff(List<AssetRun> cr, Det a, Det b, Era e) =>
        CycleSchoolStats.Observe(cr, a, e).Precision - CycleSchoolStats.Observe(cr, b, e).Precision;

    // ── B3 ───────────────────────────────────────────────────────────────────

    private static (int Earlier, int Pairs, int Ties, double P) B3(List<AssetRun> cr, Det a, Det b, Era e)
    {
        int earlier = 0, pairs = 0, ties = 0;
        foreach (var r in cr)
        {
            var w = new CycleSchoolCore.HitWindow(5, 20);
            var ha = r.Hits(a.Window);
            var ca = CycleSchoolStats.InEra(r, a.Calls(r), e).Where(t => ha[t] is not null).ToList();
            var cb = CycleSchoolStats.InEra(r, b.Calls(r), e).Where(t => ha[t] is not null).ToList();
            foreach (var low in r.GD.Where(l => r.EraOf(l.Low) == e))
            {
                int fa = ca.Where(t => low.Low >= t - w.Before && low.Low <= t + w.After).DefaultIfEmpty(-1).Min();
                int fb = cb.Where(t => low.Low >= t - w.Before && low.Low <= t + w.After).DefaultIfEmpty(-1).Min();
                if (fa < 0 || fb < 0) continue;
                if (fa == fb) { ties++; continue; }
                pairs++;
                if (fa < fb) earlier++;
            }
        }
        return (earlier, pairs, ties, CycleSchoolStats.BinomUpper(earlier, pairs));
    }

    // ── A8: the 80% rule ─────────────────────────────────────────────────────

    public sealed class A8Result
    {
        public int Candidates, In, Unresolved;
        public double Share = double.NaN, NullRate = double.NaN, P = double.NaN;
    }

    private static A8Result A8(List<AssetRun> cr, Era e, int draws, bool countsOnly)
    {
        var res = new A8Result();
        // Per candidate: asset, c(j), low, the IN delay (or −1), its outcome, and its survival length.
        var cands = new List<(string Asset, AssetRun R, RefLow L, int Delay, int Survive)>();
        foreach (var r in cr)
        {
            var b = r.B;
            foreach (var j in r.GD)
            {
                if (r.EraOf(j.Low) != e || j.Low < 130) continue;
                bool big = true;
                for (int k = j.Low - 130; k < j.Low && big; k++) if (b[k].Low < b[j.Low].Low) big = false;
                if (!big) continue;
                var prev = r.GD.Where(x => x.Low < j.Low).Select(x => x.Low).DefaultIfEmpty(-1).Max();
                if (prev < 0) continue;
                double hh = double.MinValue;
                for (int k = prev; k <= j.Low; k++) hh = Math.Max(hh, b[k].High);
                int delay = -1, survive = int.MaxValue;
                bool resolved = false;
                for (int k = j.Confirm + 1; k < b.Length; k++)
                {
                    if (b[k].Close < b[j.Low].Low) { survive = k - j.Confirm - 1; resolved = true; break; }
                    if (delay < 0 && b[k].Close > hh) { delay = k - j.Confirm; resolved = true; }
                }
                if (survive == int.MaxValue) survive = b.Length - 1 - j.Confirm;
                if (!resolved) { res.Unresolved++; continue; }
                cands.Add((r.A.Name, r, j, delay, survive));
            }
        }
        res.Candidates = cands.Count;
        res.In = cands.Count(c => c.Delay >= 0);
        if (countsOnly || res.In == 0) return res;

        // Outcome from bar s: no low below low(j) within 182 calendar days after it (NaN if the data end first).
        double Outcome(AssetRun r, RefLow j, int s)
        {
            var b = r.B;
            if (s >= b.Length || b[^1].Day < b[s].Day.AddDays(182)) return double.NaN;
            for (int k = s + 1; k < b.Length && b[k].Day <= b[s].Day.AddDays(182); k++) if (b[k].Low < b[j.Low].Low) return 0;
            return 1;
        }
        var inEv = cands.Where(c => c.Delay >= 0).ToList();
        var outcomes = inEv.Select(c => Outcome(c.R, c.L, c.L.Confirm + c.Delay)).ToList();
        var valid = Enumerable.Range(0, inEv.Count).Where(k => !double.IsNaN(outcomes[k])).ToList();
        double share = valid.Count == 0 ? double.NaN : valid.Average(k => outcomes[k]);
        double NullFor(int d)
        {
            var m = cands.Where(c => c.Survive >= d).Select(c => Outcome(c.R, c.L, c.L.Confirm + d)).Where(v => !double.IsNaN(v)).ToList();
            return m.Count == 0 ? double.NaN : m.Average();
        }
        var nulls = valid.Select(k => NullFor(inEv[k].Delay)).Where(v => !double.IsNaN(v)).ToList();
        res.Share = share;
        res.NullRate = nulls.Count == 0 ? double.NaN : nulls.Average();
        double observed = res.Share - res.NullRate;
        // Permute the IN label (with its delay) among candidates within asset; a candidate given a
        // delay it did not survive scores 0. The matched null depends only on the delays, which the
        // permutation keeps, so it is the same in every draw.
        var byAsset = cands.Select((c, k) => (c, k)).GroupBy(x => x.c.Asset).Select(g => g.ToList()).ToList();
        int ge = 0;
        for (int dr = 0; dr < draws; dr++)
        {
            double s = 0; int n = 0;
            foreach (var g in byAsset)
            {
                var delays = g.Select(x => x.c.Delay).ToArray();
                var rng = CycleSchoolCore.Rng("A8", g[0].c.Asset, dr);
                for (int i = delays.Length - 1; i > 0; i--) { int q = rng.Next(i + 1); (delays[i], delays[q]) = (delays[q], delays[i]); }
                for (int i = 0; i < g.Count; i++)
                {
                    int d = delays[i];
                    if (d < 0) continue;
                    var c = g[i].c;
                    double o = c.Survive >= d ? Outcome(c.R, c.L, c.L.Confirm + d) : 0;
                    if (double.IsNaN(o)) continue;
                    s += o; n++;
                }
            }
            if (n > 0 && s / n - res.NullRate >= observed) ge++;
        }
        res.P = (1.0 + ge) / (1.0 + draws);
        return res;
    }

    // ── B7 ───────────────────────────────────────────────────────────────────

    private static (int W, int D, double Diff, double P) B7(List<AssetRun> cr, int draws, bool countsOnly)
    {
        var items = new List<(string Asset, bool IsW, bool Flag)>();
        foreach (var r in cr)
        {
            if (r.Cap is null || !r.Cap.Families.TryGetValue("S", out var fs)) continue;
            var wset = r.GW.Select(l => l.Low).ToHashSet();
            void Add(int j, bool isW)
            {
                bool any = false, flag = false;
                for (int k = Math.Max(0, j - 10); k <= Math.Min(r.N - 1, j + 5); k++)
                {
                    if (double.IsNaN(fs[k])) continue;
                    any = true;
                    if (fs[k] >= 80) flag = true;
                }
                if (any) items.Add((r.A.Name, isW, flag));
            }
            foreach (var l in r.GW.Where(l => r.EraOf(l.Low) == Era.Dev)) Add(l.Low, true);
            foreach (var l in r.GD.Where(l => r.EraOf(l.Low) == Era.Dev && !wset.Contains(l.Low))) Add(l.Low, false);
        }
        int w = items.Count(i => i.IsW), d = items.Count(i => !i.IsW);
        if (countsOnly || w == 0 || d == 0) return (w, d, double.NaN, double.NaN);
        double Stat(IEnumerable<(bool IsW, bool Flag)> xs)
        {
            int fw = 0, nw = 0, fd = 0, nd = 0;
            foreach (var x in xs) { if (x.IsW) { nw++; if (x.Flag) fw++; } else { nd++; if (x.Flag) fd++; } }
            return nw == 0 || nd == 0 ? double.NaN : (double)fw / nw - (double)fd / nd;
        }
        double obs = Stat(items.Select(i => (i.IsW, i.Flag)));
        var byAsset = items.GroupBy(i => i.Asset).Select(g => g.ToList()).ToList();
        int ge = 0;
        for (int k = 0; k < draws; k++)
        {
            var all = new List<(bool, bool)>();
            foreach (var g in byAsset)
            {
                var labels = g.Select(i => i.IsW).ToArray();
                var rng = CycleSchoolCore.Rng("B7", g[0].Asset, k);
                for (int i = labels.Length - 1; i > 0; i--) { int q = rng.Next(i + 1); (labels[i], labels[q]) = (labels[q], labels[i]); }
                for (int i = 0; i < g.Count; i++) all.Add((labels[i], g[i].Flag));
            }
            if (Stat(all) >= obs) ge++;
        }
        return (w, d, obs, (1.0 + ge) / (1.0 + draws));
    }

    // ── (d) Trades ───────────────────────────────────────────────────────────

    public sealed class TradeResult
    {
        public int N, Skipped, PastData, ControlDraws;
        public double MeanNet = double.NaN, MeanGross = double.NaN, ControlMean = double.NaN, P = double.NaN, StandardNet = double.NaN, CapPNet = double.NaN;
        public int CapPN;
        public Dictionary<string, int> ExitReasons = new();
    }

    /// <summary>A detector's signals in an era as (signal bar, stop-anchor low bar).</summary>
    private static List<(int I, int J)> Signals(AssetRun r, string did, Era e) => did switch
    {
        "D1" => r.Conf.Where(c => r.EraOf(c.I) == e).Select(c => (c.I, c.J)).ToList(),
        "D1P" => r.ConfP.Where(c => r.EraOf(c.I) == e).Select(c => (c.I, c.J)).ToList(),
        _ => r.AP.Where(c => r.EraOf(c.I) == e).Select(c => (c.I, c.J)).ToList(),
    };

    private static TradeResult Trades(List<AssetRun> cr, string did, Era e, int draws, string tag = "")
    {
        var res = new TradeResult { ControlDraws = draws };
        var net = new List<double>(); var gross = new List<double>(); var std = new List<double>();
        var perAssetSignals = new Dictionary<string, int>();
        foreach (var r in cr)
        {
            int k = 0;
            foreach (var (i, j) in Signals(r, did, e))
            {
                var (t, st) = CycleSchoolCore.CamelTrade(r.B, r.Atr, r.Sma10, i, r.B[j].Low);
                if (st == CycleSchoolCore.TradeStatus.PastData) { res.PastData++; continue; }
                k++;
                if (st == CycleSchoolCore.TradeStatus.Skipped) { res.Skipped++; continue; }
                net.Add(t!.Value.RNet); gross.Add(t.Value.RGross);
                res.ExitReasons[t.Value.Why] = res.ExitReasons.GetValueOrDefault(t.Value.Why) + 1;
                var s = CycleSchoolCore.StandardTrade(r.B, r.Atr, i);
                if (s is { } sv) std.Add(sv.RNet);
            }
            perAssetSignals[r.A.Name] = k;
        }
        res.N = net.Count;
        res.MeanNet = net.Count == 0 ? double.NaN : net.Average();
        res.MeanGross = gross.Count == 0 ? double.NaN : gross.Average();
        res.StandardNet = std.Count == 0 ? double.NaN : std.Average();
        if (did == "D1")
        {
            var capP = new List<double>();
            foreach (var r in cr)
                foreach (var (i, j) in Signals(r, "D1P", e))
                {
                    var (t, st) = CycleSchoolCore.CamelTrade(r.B, r.Atr, r.Sma10, i, r.B[j].Low);
                    if (st == CycleSchoolCore.TradeStatus.Taken) capP.Add(t!.Value.RNet);
                }
            res.CapPN = capP.Count;
            res.CapPNet = capP.Count == 0 ? double.NaN : capP.Average();
        }
        // Control: the same number of signals per asset at random N2-eligible bars, the stop under
        // the lowest low of the 10 bars before the signal, the same floor, skip rule and exit.
        var elig = cr.ToDictionary(r => r.A.Name, r => (did == "D1" ? r.ConfElig : r.SwingElig).Values.SelectMany(v => v)
            .Distinct().Where(i => r.EraOf(i) == e && i >= 10 && i + 61 < r.N).OrderBy(i => i).ToArray());
        if (net.Count == 0) return res;
        int ge = 0;
        double sum = 0; int nOk = 0;
        for (int dr = 0; dr < draws; dr++)
        {
            var rs = new List<double>();
            foreach (var r in cr)
            {
                var pool = elig[r.A.Name];
                int k = Math.Min(perAssetSignals[r.A.Name], pool.Length);
                var rng = CycleSchoolCore.Rng($"{did}-control{tag}", r.A.Name, dr);
                var idx = (int[])pool.Clone();
                for (int q = 0; q < k; q++)
                {
                    int pick = q + rng.Next(idx.Length - q);
                    (idx[q], idx[pick]) = (idx[pick], idx[q]);
                    int i = idx[q];
                    double lo = double.MaxValue;
                    for (int z = i - 10; z <= i - 1; z++) lo = Math.Min(lo, r.B[z].Low);
                    var (t, st) = CycleSchoolCore.CamelTrade(r.B, r.Atr, r.Sma10, i, lo);
                    if (st == CycleSchoolCore.TradeStatus.Taken) rs.Add(t!.Value.RNet);
                }
            }
            if (rs.Count == 0) continue;
            double m = rs.Average();
            sum += m; nOk++;
            if (m >= res.MeanNet) ge++;
        }
        res.ControlMean = nOk == 0 ? double.NaN : sum / nOk;
        res.P = (1.0 + ge) / (1.0 + draws);
        return res;
    }

    // ── Noise ────────────────────────────────────────────────────────────────

    private static List<AssetRun> NoisyRuns(List<Asset> assets, List<Asset> cryptoU, List<Asset> equityU, CycleSchoolScore.External ext, int seed)
    {
        var cache = new Dictionary<string, (Bar[] Bars, double[] F)>();
        (Bar[] Bars, double[] F) N(Asset a)
        {
            if (!cache.TryGetValue(a.Name, out var v))
                cache[a.Name] = v = AssetRun.Noise(a.Bars, 0.25, CycleSchoolCore.Rng("noise", a.Name, seed));
            return v;
        }
        Asset Noisy(Asset a) => a with { Bars = N(a).Bars };
        var nc = cryptoU.Select(Noisy).ToList();
        var ne = equityU.Select(Noisy).ToList();
        double Factor(string sym, DateOnly d)
        {
            var src = assets.First(a => a.Name == sym);
            var (_, f) = N(src);
            int lo = 0, hi = src.Bars.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (src.Bars[m].Day <= d) lo = m + 1; else hi = m; }
            return lo == 0 ? 1 : f[lo - 1];
        }
        return assets.Select(a => AssetRun.Build(Noisy(a), a.Crypto ? nc : a.Equity ? ne : null, ext, Factor)).ToList();
    }

    // ── Descriptive output (prereg 5.3) ──────────────────────────────────────

    private static object Descriptives(List<AssetRun> runs, List<Asset> assets, Func<Asset, IReadOnlyList<Asset>?> universeOf, CycleSchoolScore.External ext, Det alert)
    {
        var timeline = new Dictionary<string, object>();
        var corr = new Dictionary<string, object>();
        var undefinedCap = new Dictionary<string, object>();
        var grades = new Dictionary<string, object>();
        foreach (var r in runs)
        {
            var cap = r.Cap!;
            timeline[r.A.Name] = cap.Families.ToDictionary(f => f.Key, f =>
            {
                var idx = Enumerable.Range(0, r.N).Where(i => !double.IsNaN(f.Value[i])).ToList();
                return (object)new
                {
                    first = idx.Count == 0 ? null : r.B[idx[0]].Day.ToString("yyyy-MM-dd"),
                    last = idx.Count == 0 ? null : r.B[idx[^1]].Day.ToString("yyyy-MM-dd"),
                    dev_share_defined = Enumerable.Range(0, r.N).Count(i => r.EraOf(i) == Era.Dev && !double.IsNaN(f.Value[i])) / (double)Math.Max(1, Enumerable.Range(0, r.N).Count(i => r.EraOf(i) == Era.Dev)),
                    metrics = cap.Metrics.Where(m => m.Family == f.Key).Select(m => m.Id).ToList(),
                };
            });
            var fams = cap.Families.Keys.ToList();
            var mat = new double[fams.Count, fams.Count];
            for (int a = 0; a < fams.Count; a++)
                for (int b = 0; b < fams.Count; b++)
                {
                    var pairs = Enumerable.Range(0, r.N).Where(i => r.EraOf(i) == Era.Dev && !double.IsNaN(cap.Families[fams[a]][i]) && !double.IsNaN(cap.Families[fams[b]][i]))
                        .Select(i => (cap.Families[fams[a]][i], cap.Families[fams[b]][i])).ToList();
                    mat[a, b] = a == b ? 1 : pairs.Count < 30 ? 0 : Spearman(pairs);
                }
            var eig = Eigen(mat);
            double pr = Math.Pow(eig.Sum(), 2) / eig.Sum(v => v * v);
            corr[r.A.Name] = new
            {
                families = fams,
                spearman = Enumerable.Range(0, fams.Count).Select(a => Enumerable.Range(0, fams.Count).Select(b => Math.Round(mat[a, b], 3)).ToArray()).ToArray(),
                effective_families = pr,
            };
            int devBars = Enumerable.Range(0, r.N).Count(i => r.EraOf(i) == Era.Dev);
            undefinedCap[r.A.Name] = new
            {
                dev_bars = devBars,
                cap_undefined_fewer_than_3_families = Enumerable.Range(0, r.N).Count(i => r.EraOf(i) == Era.Dev && cap.FamiliesDefined[i] < 3),
                pctcap_undefined_warmup = Enumerable.Range(0, r.N).Count(i => r.EraOf(i) == Era.Dev && !double.IsNaN(cap.Cap[i]) && double.IsNaN(cap.PctCap[i])),
            };
            var h = r.Hits("D-post");
            grades[r.A.Name] = r.Conf.Where(c => r.EraOf(c.I) == Era.Dev && h[c.I] is not null)
                .GroupBy(c => c.Grade >= 97 ? "high" : c.Grade >= 93 ? "mid" : "low")
                .ToDictionary(g => g.Key, g => (object)new { n = g.Count(), hits = g.Count(c => h[c.I]!.Value.Hit), cancelled = g.Count(c => c.Cancelled >= 0) });
        }
        // Leave one family out: ALERT's development precision per class with each family dropped.
        var lofo = new Dictionary<string, object>();
        foreach (var fam in CycleSchoolScore.FamilyOrder)
        {
            var lr = assets.Select(a =>
            {
                var run = AssetRun.Build(a, universeOf(a), null, withScore: false);
                var cap = CycleSchoolScore.Build(a, universeOf(a), ext, null, new HashSet<string> { fam });
                run.Cap = cap;
                run.Alert = CycleSchoolCore.Alerts(cap.PctCap, run.GD, 95, true);
                return run;
            }).ToList();
            lofo[$"without_{fam}"] = Classes.ToDictionary(c => c, c =>
            {
                var s = CycleSchoolStats.Observe(lr.Where(r => r.A.Class == c).ToList(), alert, Era.Dev);
                return (object)new { s.Calls, s.Hits, precision = s.Precision };
            });
        }
        return new { family_timeline = timeline, family_correlation = corr, cap_undefined = undefinedCap, grade_at_confirmed = grades, leave_one_family_out_alert = lofo };
    }

    private static double Spearman(List<(double, double)> p)
    {
        double[] Rank(double[] v)
        {
            var idx = Enumerable.Range(0, v.Length).OrderBy(i => v[i]).ToArray();
            var r = new double[v.Length];
            for (int i = 0; i < idx.Length;)
            {
                int j = i;
                while (j + 1 < idx.Length && v[idx[j + 1]] == v[idx[i]]) j++;
                double avg = (i + j) / 2.0;
                for (int k = i; k <= j; k++) r[idx[k]] = avg;
                i = j + 1;
            }
            return r;
        }
        var a = Rank(p.Select(x => x.Item1).ToArray());
        var b = Rank(p.Select(x => x.Item2).ToArray());
        double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
        return saa == 0 || sbb == 0 ? 0 : sab / Math.Sqrt(saa * sbb);
    }

    /// <summary>Eigenvalues of a symmetric matrix (cyclic Jacobi).</summary>
    private static double[] Eigen(double[,] m)
    {
        int n = m.GetLength(0);
        var a = (double[,])m.Clone();
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double off = 0;
            for (int p = 0; p < n; p++) for (int q = p + 1; q < n; q++) off += a[p, q] * a[p, q];
            if (off < 1e-14) break;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-15) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < n; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < n; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                    }
                }
        }
        return Enumerable.Range(0, n).Select(i => a[i, i]).ToArray();
    }

    // ── Output ───────────────────────────────────────────────────────────────

    public sealed record HashRow(string File, string? Sha256Before, string Sha256After, bool Unchanged);

    private static List<HashRow> HashReport(CycleSchoolInputs io) => io.FilesRead.Select(f =>
    {
        var before = io.HashBefore.GetValueOrDefault(f);
        var after = CycleSchoolInputs.Sha(Path.Combine(io.Root, f));
        return new HashRow(f, before, after, before == after);
    }).ToList();

    private static void Write(Dictionary<string, object> report, string outDir, string name)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, name);
        var settings = new JsonSerializerSettings { Formatting = Formatting.Indented, FloatFormatHandling = FloatFormatHandling.String };
        File.WriteAllText(path, JsonConvert.SerializeObject(report, settings));
        var inputs = (List<HashRow>)report["inputs"];
        Console.WriteLine($"  wrote {path}; {inputs.Count} input files hashed before and after, {inputs.Count(x => !x.Unchanged)} changed");
    }

    private static void PrintCounts(List<Dictionary<string, object>> counts, List<Det> dets)
    {
        foreach (var row in counts)
        {
            var sb = new StringBuilder($"  {row["class"],-6} {row["era"],-4} lows GD {row["reference_lows_GD"],4} GW {row["reference_lows_GW"],3} |");
            foreach (var d in dets) sb.Append($" {d.Id} {row[$"calls_{d.Id}"]}");
            Console.WriteLine(sb.ToString());
        }
    }

    private static void PrintFull(List<TestRow> tests, Dictionary<string, string> verdicts, Dictionary<string, List<string>> why)
    {
        Console.WriteLine();
        Console.WriteLine("── TESTS (development era) ──");
        foreach (var t in tests)
            Console.WriteLine($"  {t.Id,-6} {t.Class,-6} {t.Status,-13} n={t.Calls,4}  obs {F(t.Observed)}  null {F(t.NullMean)}  p {F(t.P)}  Holm {F(t.PHolm)}  {(t.Significant ? "SIG" : "")}  {t.Note}");
        Console.WriteLine();
        Console.WriteLine("── VERDICT ──");
        foreach (var (k, v) in verdicts)
        {
            Console.WriteLine($"  {k}: {v}");
            foreach (var w in why[k]) Console.WriteLine($"      {w}");
        }
    }

    private static string F(double v) => double.IsNaN(v) ? "  n/a " : v.ToString("0.0000", Inv);
}
