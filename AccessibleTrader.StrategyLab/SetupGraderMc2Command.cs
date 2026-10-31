using System.Security.Cryptography;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
using static AccessibleTrader.StrategyLab.SetupGraderMc2Core;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Stage 2 of the round-2 setup grader, the Market Cipher school only
/// (<c>docs/SETUP_GRADER_ROUND2_MC_PREREG.md</c>, committed 60a94a0d before any result existed).
/// Every rule, threshold, test, family and verdict rule is that document's; this class only
/// computes them. Command: <c>setup-grader-mc2 [--counts-only]</c>.
///
/// <para>
/// THE QUESTION. Every Market Cipher B Buy dot is a setup. E0 buys the next open after the dot;
/// E1 waits for the trigger wave (the next higher, sub-zero wave cross within 30 bars, money flow
/// turning up, void if a wave goes as deep as the anchor). Each is compared with a random bar in
/// the same higher-timeframe money-flow context, and waiting is compared with not waiting PER SETUP
/// SEEN, a setup that never confirms counting as zero.
/// </para>
///
/// <para>
/// THE TRAPS IT IS BUILT AROUND. (1) The app's higher-timeframe path; not used: weekly (and daily,
/// for 4h) bars are built here from completed buckets only, as round 1 did. (2) The engine's R; not
/// used: R is per position from the initial risk (round 1's <see cref="SetupGraderCore.Ladder"/> and
/// <see cref="SetupGraderCore.StopAndRule"/>, called on this series). (3) A good environment lifts any
/// long: every trade is read as its EXCESS over all bars on the same asset, era and context state at
/// the trade's signal bar. (4) Overlapping outcomes: labels move in 20-bar blocks; per-setup
/// differences flip by cluster (dots within 50 bars). (5) Undefined is never false: a setup with an
/// undefined context, no anchor, or too few bars after it is excluded and counted. (6) The counts-only
/// mode computes no outcome at all: no R array is built before it returns.
/// </para>
///
/// <para>
/// DEVIATIONS AND LITERAL READINGS, fixed in this code before the first outcome was computed (the
/// findings repeat them):
/// <list type="bullet">
/// <item>The trade's control and permutation stratum are keyed on the era of its SIGNAL bar (the bar
/// the control describes); the setup's era (development or held-out) is the dot's date, as section 6.2
/// says. They differ only for dots in the last 30 bars of 2022 whose wait ends in 2023.</item>
/// <item>The thickness median of an even count of values is the mean of the two middle values.</item>
/// <item>A setup is also excluded if the entry-timeframe money flow or ATR is undefined at the dot
/// (never binding: the weekly context needs far more history), or, at the outcome stage, if any of
/// its trades has no defined outcome (ATR zero) or an undefined context at its signal bar.</item>
/// <item>The random-exit p is (1 + books at or above the variant's mean) / (1 + 2,000), the share
/// counting the observed book, which is what section 6.5's "about 0.0005" smallest p implies.</item>
/// <item>P3 (and the ΔV halves of S2, S3, S4) needs 30 setups and 15 confirmed to be confirmatory; with
/// fewer than 15 confirmed it runs but is Suggestive only; below 15 setups it is not testable. Ordering
/// and group tests need 15 in each group (15-29: Suggestive only). Exit tests need 15 entries.</item>
/// <item>The A∪B fallback is triggered per class and policy when that policy's development-era grade A
/// trades number fewer than 30. It is A∪B against C (an ordering), Suggestive whatever its p, and not
/// added to Family G's Holm correction.</item>
/// <item>"Suggestive" = raw p below 0.05 and the result not Supported or Fragile, because Holm failed,
/// the count is 15-29, or the held-out check is not checkable. Holm-significant results whose held-out
/// sign or breadth fails are No edge.</item>
/// <item>Noise seeds are 0, 1, 2 plus StableSeed(symbol) mod 1000, as round 1 and the gate command
/// offset them, so assets do not share one noise path.</item>
/// <item>Two-sided tests (S1, S5) take the held-out check as "same sign as the development
/// estimate".</item>
/// </list>
/// </para>
/// </summary>
public static class SetupGraderMc2Command
{
    private const int Draws = 10_000, ExitDraws = 2_000;
    private const int MinTestable = 15, MinConfirm = 30, MinHeld = 10, MinBreadth = 10, MinBreadthA = 3;
    private const double Alpha = 0.05;
    private const string Seed = "setup-grader-mc";

    private static readonly string[] CryptoDaily =
    {
        "bitstamp_BTC_USDT_1d.json", "bitstamp_ETH_USDT_1d.json", "bitstamp_LTC_USDT_1d.json", "bitstamp_XRP_USDT_1d.json",
        "bitstamp_BCH_USDT_1d.json", "bitstamp_ADA_USDT_1d.json", "bitstamp_SOL_USDT_1d.json", "bitstamp_DOGE_USDT_1d.json",
        "mexc_KAS_USDT_1d.json", "mexc_TAO_USDT_1d.json",
    };
    private static readonly string[] EquityNames =
    {
        "AAPL", "CAT", "CVX", "DIA", "EEM", "EFA", "FXI", "IBM", "IWM", "JNJ", "JPM", "KO", "MCD", "MMM", "MSFT", "PFE", "PG",
        "QQQ", "SPY", "T", "VTI", "VZ", "WMT", "XLB", "XLE", "XLF", "XLI", "XLK", "XLP", "XLU", "XLV", "XLY", "XOM",
    };
    private static readonly string[] Crypto4h =
    {
        "bitstamp_BTC_USDT_4h.json", "bitstamp_ETH_USDT_4h.json", "bitstamp_XRP_USDT_4h.json", "bitstamp_SOL_USDT_4h.json",
        "bitstamp_DOGE_USDT_4h.json", "mexc_KAS_USDT_4h.json", "mexc_TAO_USDT_4h.json",
    };

    // ── State ────────────────────────────────────────────────────────────────

    internal sealed class Trade
    {
        public required string Asset;
        public required int Sig, Era, Ctx;
        public double R = double.NaN, Rg = double.NaN, Ctl = double.NaN, CtlG = double.NaN;
        public double Ex => R - Ctl;
        public double ExG => Rg - CtlG;
    }

    internal sealed class Setup
    {
        public required string Asset;
        public required int Dot, Era, Grade;
        public required Mc2Path Path;
        public required bool HtfDot;
        public Trade? E0, E1, E1h, E1m;
        /// <summary>V1(d) - V0(d) for the given wait (section 3.3): no trade counts as zero.</summary>
        public double Dv(Trade? wait) => (wait?.Ex ?? 0) - E0!.Ex;
        public bool NonVeto => Grade != CtxV;
    }

    internal sealed class Run
    {
        public required string Label, Cls;
        public required Mc2Series S;
        public required int[] Era;
        public required List<Setup> Setups;          // eligible
        public required Dictionary<string, int> Excl;
        public required int Dots;
        public double[] R = Array.Empty<double>(), Rg = Array.Empty<double>();
        public Dictionary<(int, int, int), SetupGraderCommand.Stratum> Strata = new();
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string snapshotDir, bool countsOnly)
    {
        Console.WriteLine("SETUP GRADER ROUND 2, MARKET CIPHER — pre-registered in docs/SETUP_GRADER_ROUND2_MC_PREREG.md (60a94a0d), stage 2");
        Console.WriteLine(LabCipherProfile.Describe());
        // Its pre-registered rules read Cipher B's divergences (exit X4, the bearish divergence), which were removed on
        // 2026-10-31 (not Market Cipher B's): a run today would be a different experiment
        // under the same name. Its results stand; to re-run it, build commit 2e86d377.
        Console.WriteLine("Refusing to run: the pre-registered rules read Cipher B parts removed on 2026-10-31 (exit X4's bearish divergence); build commit 2e86d377 to re-run it.");
        return 2;
#pragma warning disable CS0162 // the frozen run below, kept for the record
        // --cipher-candles ha alone is allowed: the exploratory part of
        // docs/EXACT_CIPHERS_RERUN_PREREG.md (section 6). It is never the pre-registered run.
        // (--cipherb old, the one other profile the pre-registration refused, is refused by the lab
        // itself since 2026-10-31.)
        if (LabCipherProfile.CiphersOnHeikinAshi)
            Console.WriteLine("EXPLORATORY: the Ciphers on Heikin-Ashi candles. Not the pre-registered run; its verdicts can say only \"worth its own pre-registration on new data\" or \"nothing\" (docs/EXACT_CIPHERS_RERUN_PREREG.md section 6).");
        Console.WriteLine($"Snapshots: {Path.GetFullPath(snapshotDir)} (read only)");
        var files = CryptoDaily.Concat(EquityNames.Select(e => $"yahoo_{e}_1d.json")).Concat(Crypto4h).ToList();
        var missing = files.Where(f => !File.Exists(Path.Combine(snapshotDir, f))).ToList();
        foreach (var m in missing) Console.WriteLine($"  ! missing (not fetched): {m}");
        files = files.Except(missing).ToList();
        var before = Hashes(snapshotDir, files);
        Console.WriteLine($"Inputs hashed before the run: {before.Count} files");
        Console.WriteLine();

        var engine = LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
        List<(string Label, string Symbol, List<Ohlcv> Bars)> Load(IEnumerable<string> names) => names.Where(f => !missing.Contains(f))
            .Select(f => { var s = SnapshotCommand.Load(Path.Combine(snapshotDir, f)); return (LabelOf(f), s.Symbol, s.Bars); }).ToList();

        var cryptoSnaps = Load(CryptoDaily);
        var equitySnaps = Load(EquityNames.Select(e => $"yahoo_{e}_1d.json"));
        var c4Snaps = Load(Crypto4h);

        var crypto = await BuildClass(engine, "crypto", cryptoSnaps, GraderSeriesConfig.DailyCrypto);
        var equities = await BuildClass(engine, "equities", equitySnaps, GraderSeriesConfig.DailyEquity);
        var c4 = await BuildClass(engine, "crypto4h", c4Snaps, GraderSeriesConfig.FourHourCrypto);

        PrintCounts("CRYPTO DAILY (weekly environment)", crypto);
        PrintCounts("EQUITIES DAILY (weekly environment)", equities);
        PrintCounts("CRYPTO 4H (S4, daily environment)", c4);
        PrintTestability(new[] { ("crypto", crypto), ("equities", equities), ("crypto4h", c4) });
        if (countsOnly)
        {
            Console.WriteLine("--counts-only: no outcome was computed (no R array was built). Stopping here.");
            return CheckHashes(snapshotDir, files, before);
        }

        foreach (var cls in new[] { crypto, equities, c4 }) foreach (var r in cls) AddOutcomes(r);
        var held = new Dictionary<string, (int N, double Stat)>();

        // ── Descriptive (section 3.4) ───────────────────────────────────────
        Describe("crypto", crypto);
        Describe("equities", equities);
        Describe("crypto4h", c4);

        var daily = new[] { ("crypto", crypto), ("equities", equities) };

        // ── Family P ────────────────────────────────────────────────────────
        var famP = new List<SetupGraderCommand.TestRow>();
        foreach (var (cn, cls) in daily) famP.AddRange(PolicyTests(cn, cls, "", held));
        SetupGraderCommand.ApplyHolm(famP);
        SetupGraderCommand.PrintFamily("FAMILY P — the entry policies (6 tests, Holm inside, development era, net R)", famP);

        // ── Family G ────────────────────────────────────────────────────────
        var famG = new List<SetupGraderCommand.TestRow>();
        var fallback = new List<SetupGraderCommand.TestRow>();
        foreach (var (cn, cls) in daily)
            foreach (var pol in new[] { "E0", "E1" })
            {
                Func<Setup, Trade?> pick = pol == "E0" ? s => s.E0 : s => s.E1;
                famG.Add(Held(held, BlockTest($"GA-{pol}-{cn}", $"{cn}: {pol} ExA > 0", cls, s => s.Grade == CtxA, pick),
                    cls, s => s.Grade == CtxA, pick));
                famG.Add(Held(held, OrderTest($"GO-{pol}-{cn}", $"{cn}: {pol} ExA - ExC > 0", cls, pick, s => s.Grade == CtxA, s => s.Grade == CtxC, false),
                    cls, pick, s => s.Grade == CtxA, s => s.Grade == CtxC));
                int nA = DevSetups(cls).Count(s => s.Grade == CtxA && pick(s) != null);
                if (nA < MinConfirm)
                {
                    var fb = OrderTest($"GOAB-{pol}-{cn}", $"{cn}: {pol} Ex(A∪B) - ExC > 0 [fallback, Suggestive whatever its p]", cls, pick,
                        s => s.Grade is CtxA or CtxB, s => s.Grade == CtxC, false);
                    fallback.Add(Held(held, fb, cls, pick, s => s.Grade is CtxA or CtxB, s => s.Grade == CtxC));
                }
            }
        SetupGraderCommand.ApplyHolm(famG);
        SetupGraderCommand.PrintFamily("FAMILY G — the grades (8 tests, Holm inside, development era, net R)", famG);
        if (fallback.Count > 0)
            SetupGraderCommand.PrintFamily("A∪B FALLBACK (section 6.7; Suggestive whatever its p; not in Family G's Holm)", fallback);

        // ── Family S ────────────────────────────────────────────────────────
        var famS = new List<SetupGraderCommand.TestRow>();
        foreach (var (cn, cls) in daily)
        {
            // S1: vetoed minus non-vetoed, E0, two-sided.
            famS.Add(Held(held, OrderTest($"S1-{cn}", $"{cn}: Ex0(vetoed) - Ex0(non-vetoed), two-sided", cls, s => s.E0,
                s => s.Grade == CtxV, s => s.Grade != CtxV, true, includeVetoed: true), cls, s => s.E0, s => s.Grade == CtxV, s => s.Grade != CtxV, includeVetoed: true));
        }
        foreach (var (cn, cls) in daily)
        {
            famS.Add(Held(held, BlockTest($"S2a-{cn}", $"{cn}: Ex1h > 0 (wait a candle)", cls, _ => true, s => s.E1h),
                cls, _ => true, s => s.E1h));
            famS.Add(Held(held, ClusterTest($"S2b-{cn}", $"{cn}: ΔV(E1h) > 0", cn, cls, s => s.Dv(s.E1h), s => s.E1h != null, false),
                cls, s => s.Dv(s.E1h)));
        }
        foreach (var (cn, cls) in daily)
        {
            famS.Add(Held(held, BlockTest($"S3a-{cn}", $"{cn}: Ex1m > 0 (wait for green money flow)", cls, _ => true, s => s.E1m),
                cls, _ => true, s => s.E1m));
            famS.Add(Held(held, ClusterTest($"S3b-{cn}", $"{cn}: ΔV(E1m) > 0", cn, cls, s => s.Dv(s.E1m), s => s.E1m != null, false),
                cls, s => s.Dv(s.E1m)));
        }
        famS.AddRange(PolicyTests("crypto4h", c4, "S4-", held));
        foreach (var (cn, cls) in daily)
        {
            var runs = cls.ToDictionary(r => r.Label);
            var curls = cls.ToDictionary(r => r.Label, r => MfCurl(r.S));
            double X3Atr(Setup s, Trade t)
            {
                var r = runs[s.Asset];
                double stop = SchoolStop(r.S.Bars, r.S.Atr, s.Path.AnchorStart, t.Sig);
                return SetupGraderCore.StopAndRule(r.S.Bars, r.S.Atr, t.Sig, stop, curls[s.Asset], t.Sig + SetupGraderCore.ExitCap).AtrNet;
            }
            Func<Setup, double> v = s => (s.E1 != null ? X3Atr(s, s.E1) : 0) - X3Atr(s, s.E0!);
            famS.Add(Held(held, ClusterTest($"S5-{cn}", $"{cn}: school plan (own stop, X3 exit) E1 - E0 per setup, ATR units, two-sided", cn, cls, v,
                s => s.E1 != null, true), cls, v, twoSided: true));
        }
        foreach (var (cn, cls) in daily)
            famS.Add(Held(held, OrderTest($"S6-{cn}", $"{cn}: Ex1(HTF Buy dot in last 3 HTF bars) - Ex1(rest) > 0", cls, s => s.E1,
                s => s.HtfDot, s => !s.HtfDot, false), cls, s => s.E1, s => s.HtfDot, s => !s.HtfDot));
        foreach (var (cn, cls) in daily)
            famS.Add(Held(held, ValueOrderTest($"S7-{cn}", $"{cn}: mean(V1 - V0) in C minus in A > 0 (Casper's regime rule)", cls,
                s => s.Dv(s.E1), s => s.Grade == CtxC, s => s.Grade == CtxA), cls, s => s.Dv(s.E1), s => s.Grade == CtxC, s => s.Grade == CtxA));
        SetupGraderCommand.ApplyHolm(famS);
        SetupGraderCommand.PrintFamily("FAMILY S — secondary (19 tests, Holm inside)", famS);

        // ── Family X ────────────────────────────────────────────────────────
        var famX = new List<SetupGraderCommand.TestRow>();
        foreach (var (cn, cls) in daily)
            foreach (var pol in new[] { "E0", "E1" })
                famX.AddRange(ExitTests(cn, cls, pol));
        SetupGraderCommand.ApplyHolm(famX);
        SetupGraderCommand.PrintFamily("FAMILY X — exits against random exits (16 tests, Holm inside; X5 reported, not tested)", famX);

        // ── Held-out signs (read once, after the development tests) ─────────
        Console.WriteLine("══════ HELD-OUT ERA (dots 2023-01-01 on): signs only ══════");
        Console.WriteLine($"{"id",-18} {"dev stat",9} {"held n",7} {"held stat",10}  agrees");
        foreach (var t in famP.Concat(famG).Concat(fallback).Concat(famS))
        {
            if (!held.TryGetValue(t.Id, out var h)) continue;
            bool two = t.Id.StartsWith("S1-") || t.Id.StartsWith("S5-");
            string agree = h.N < MinHeld ? "not checkable" : double.IsNaN(h.Stat) ? "n/a"
                : two ? (Math.Sign(h.Stat) == Math.Sign(t.Stat) ? "yes (same sign)" : "no") : (h.Stat > 0 ? "yes" : "no");
            Console.WriteLine($"{t.Id,-18} {t.Stat,9:+0.000;-0.000} {h.N,7} {h.Stat,10:+0.000;-0.000}  {agree}");
        }
        Console.WriteLine();

        // ── Noise (section 6.4) ─────────────────────────────────────────────
        var retained = new Dictionary<string, Dictionary<string, double>>();
        foreach (var (cn, cls, snaps, cfg) in new[] { ("crypto", crypto, cryptoSnaps, GraderSeriesConfig.DailyCrypto), ("equities", equities, equitySnaps, GraderSeriesConfig.DailyEquity) })
        {
            var clean = NoiseStats(cls);
            var noisy = new List<Dictionary<string, double>>();
            for (int seed = 0; seed <= 2; seed++)
            {
                var pb = snaps.Select(x => (x.Label, x.Symbol, Bars: PerturbKeepGap(x.Bars, 0.25, seed + StableSeed.From(x.Symbol) % 1000))).ToList();
                var nr = await BuildClass(engine, cn, pb, cfg);
                foreach (var r in nr) AddOutcomes(r);
                var st = NoiseStats(nr);
                noisy.Add(st);
                Console.WriteLine($"  noise {cn} seed {seed}: " + string.Join(", ", st.Select(kv => $"{kv.Key} {kv.Value:+0.000;-0.000}")));
            }
            var ret = new Dictionary<string, double>();
            foreach (var k in clean.Keys)
            {
                double m = noisy.Average(d => d[k]);
                ret[k] = m / clean[k];
                Console.WriteLine($"  {cn} {k}: clean {clean[k]:+0.000;-0.000}, mean noisy {m:+0.000;-0.000}, retained {100 * ret[k]:0}%");
            }
            retained[cn] = ret;
        }
        Console.WriteLine();

        // ── Verdicts ────────────────────────────────────────────────────────
        Console.WriteLine("── VERDICT ──");
        foreach (var (cn, cls) in daily) Verdict(cn, cls, famP, famG, held, retained[cn]);
        Verdict4h(c4, famS, held);

        return CheckHashes(snapshotDir, files, before);
    }

    private static string LabelOf(string file) => Path.GetFileNameWithoutExtension(file).Split('_')[1];

    // ── Building ─────────────────────────────────────────────────────────────

    internal static async Task<List<Run>> BuildClass(IIndicatorEngine engine, string cls,
        List<(string Label, string Symbol, List<Ohlcv> Bars)> series, GraderSeriesConfig cfg)
    {
        var runs = new List<Run>();
        foreach (var (label, symbol, bars) in series)
            runs.Add(BuildRun(label, cls, await BuildAsync(engine, symbol, bars, cfg)));
        return runs;
    }

    /// <summary>Setups, eligibility and grades only. Computes no outcome.</summary>
    internal static Run BuildRun(string label, string cls, Mc2Series s)
    {
        int n = s.Bars.Count;
        var era = s.Bars.Select(b => SetupGraderCore.EraOf(b.Date)).ToArray();
        var excl = new Dictionary<string, int>();
        var setups = new List<Setup>();
        int dots = 0;
        for (int i = 0; i < n; i++)
        {
            if (!s.BuyDot[i]) continue;
            dots++;
            var path = Follow(s, i);
            string? why = null;
            if (s.Ctx[i] == Undef) why = "context undefined at the dot (higher-timeframe money flow, waves or RSI in warm-up, or < 26 thickness values)";
            else if (path.AnchorStart < 0) why = "no anchor wave (no earlier bear cross)";
            else if (i + EligibleAhead > n - 1) why = "fewer than 51 bars after the dot";
            else if (double.IsNaN(s.Atr[i]) || s.Atr[i] <= 0 || i < 1 || double.IsNaN(s.Mf[i]) || double.IsNaN(s.Mf[i - 1]))
                why = "entry-timeframe ATR or money flow undefined at the dot";
            if (why != null) { excl[why] = excl.GetValueOrDefault(why) + 1; continue; }
            setups.Add(new Setup
            {
                Asset = label, Dot = i, Era = era[i], Grade = s.Ctx[i], Path = path, HtfDot = s.HtfDotRecent[i],
            });
        }
        return new Run { Label = label, Cls = cls, S = s, Era = era, Setups = setups, Excl = excl, Dots = dots };
    }

    /// <summary>The standard R harness on every bar, the matched-control strata, and each setup's trades.</summary>
    internal static void AddOutcomes(Run run)
    {
        var s = run.S; int n = s.Bars.Count;
        var r = new double[n]; var rg = new double[n];
        for (int i = 0; i < n; i++)
        {
            double g = SetupGraderCore.StandardR(s.Bars, i, s.Atr[i], true);
            rg[i] = g;
            r[i] = double.IsNaN(g) ? double.NaN : g - SetupGraderCore.CostR(s.Bars[i + 1].Open, s.Atr[i]);
        }
        run.R = r; run.Rg = rg;
        run.Strata = SetupGraderCommand.BuildStrata(run.Era, s.Ctx, r, rg, null);
        Trade? Make(int sig)
        {
            if (sig < 0) return null;
            var t = new Trade { Asset = run.Label, Sig = sig, Era = run.Era[sig], Ctx = s.Ctx[sig], R = r[sig], Rg = rg[sig] };
            if (t.Ctx != Undef && run.Strata.TryGetValue((t.Era, t.Ctx, 0), out var st)) { t.Ctl = st.Mean; t.CtlG = st.MeanG; }
            return t;
        }
        var kept = new List<Setup>();
        foreach (var su in run.Setups)
        {
            var p = su.Path;
            su.E0 = Make(su.Dot);
            su.E1 = p.E1 == WaitStatus.Confirmed ? Make(p.E1Bar) : null;
            su.E1h = p.E1h == WaitStatus.Confirmed ? Make(p.E1hBar) : null;
            su.E1m = p.E1m == WaitStatus.Confirmed ? Make(p.E1mBar) : null;
            bool bad = new[] { su.E0, su.E1, su.E1h, su.E1m }.Any(t => t != null && (double.IsNaN(t.R) || double.IsNaN(t.Ctl)));
            if (bad)
            {
                const string why = "outcome stage: a trade with no defined outcome or control (ATR zero or context undefined at its signal bar)";
                run.Excl[why] = run.Excl.GetValueOrDefault(why) + 1;
                continue;
            }
            kept.Add(su);
        }
        run.Setups.Clear(); run.Setups.AddRange(kept);
    }

    /// <summary>X3's exit: money flow above zero and falling.</summary>
    internal static bool[] MfCurl(Mc2Series s)
    {
        var f = new bool[s.Bars.Count];
        for (int t = 1; t < f.Length; t++) f[t] = s.Mf[t] > 0 && s.Mf[t] < s.Mf[t - 1];
        return f;
    }

    // ── Selections ───────────────────────────────────────────────────────────

    private static IEnumerable<Setup> DevSetups(List<Run> cls, bool includeVetoed = false) =>
        cls.SelectMany(r => r.Setups).Where(s => s.Era == 0 && (includeVetoed || s.NonVeto));
    private static IEnumerable<Setup> HeldSetups(List<Run> cls, bool includeVetoed = false) =>
        cls.SelectMany(r => r.Setups).Where(s => s.Era == 1 && (includeVetoed || s.NonVeto));

    private static double Mean(IEnumerable<double> x) { var l = x.ToList(); return l.Count == 0 ? double.NaN : l.Average(); }

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>P1, P2 and P3 for a class (Family P) or with an id prefix (S4).</summary>
    private static IEnumerable<SetupGraderCommand.TestRow> PolicyTests(string cn, List<Run> cls, string prefix,
        Dictionary<string, (int, double)> held)
    {
        yield return Held(held, BlockTest($"{prefix}P1-{cn}", $"{cn}: Ex0 > 0 (Buy dot at the next open)", cls, _ => true, s => s.E0),
            cls, _ => true, s => s.E0);
        yield return Held(held, BlockTest($"{prefix}P2-{cn}", $"{cn}: Ex1 > 0 (trigger-wave entry)", cls, _ => true, s => s.E1),
            cls, _ => true, s => s.E1);
        yield return Held(held, ClusterTest($"{prefix}P3-{cn}", $"{cn}: ΔV > 0 (waiting beats not waiting, per setup)", cn, cls,
            s => s.Dv(s.E1), s => s.E1 != null, false), cls, s => s.Dv(s.E1));
    }

    /// <summary>
    /// Ex &gt; 0 by block permutation (section 6.5): in each asset x era x context stratum the bars are
    /// cut into 20-bar blocks whose order is permuted, the trade labels travelling with their blocks.
    /// The population is the development-era, non-vetoed setups passing <paramref name="sel"/> whose
    /// policy (<paramref name="pick"/>) traded.
    /// </summary>
    private static SetupGraderCommand.TestRow BlockTest(string id, string what, List<Run> cls, Func<Setup, bool> sel, Func<Setup, Trade?> pick)
    {
        var strata = new List<(string Asset, double[] Y, int[] Lab)>();
        int total = 0; double obs = 0;
        foreach (var a in cls)
        {
            var list = a.Setups.Where(s => s.Era == 0 && s.NonVeto && sel(s)).Select(pick).Where(t => t != null).Select(t => t!).ToList();
            foreach (var grp in list.GroupBy(t => (t.Era, t.Ctx)).OrderBy(g => g.Key))
            {
                var st = a.Strata[(grp.Key.Era, grp.Key.Ctx, 0)];
                var y = st.Rows.Select(i => a.R[i] - st.Mean).ToArray();
                var lab = grp.Select(t => st.Pos[t.Sig]).ToArray();
                strata.Add((a.Label, y, lab));
                total += lab.Length;
                obs += lab.Sum(p => y[p]);
            }
        }
        var row = new SetupGraderCommand.TestRow { Id = id, What = what, N = total };
        if (total < MinTestable) { row.Note = $"not testable: {total} trades (< {MinTestable}); count only"; return row; }
        if (total < MinConfirm) row.Note = "Suggestive only (15-29 trades)";
        row.Stat = obs / total;
        var nullSum = new double[Draws];
        foreach (var grp in strata.GroupBy(x => x.Asset).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rng = new Random(StableSeed.From($"{Seed}|{id}|{grp.Key}"));
            foreach (var (_, y, lab) in grp) SetupGraderCommand.BlockNull(y, lab, rng, nullSum);
        }
        int ge = nullSum.Count(v => v / total >= row.Stat - 1e-12);
        row.P = (1.0 + ge) / (1.0 + Draws);
        return row;
    }

    /// <summary>Per-setup differences by cluster sign-flip (section 6.5), dev-era non-vetoed setups.</summary>
    private static SetupGraderCommand.TestRow ClusterTest(string id, string what, string cn, List<Run> cls,
        Func<Setup, double> value, Func<Setup, bool> confirmed, bool twoSided)
    {
        var sums = new List<double>();
        int nSet = 0, nConf = 0; double tot = 0;
        foreach (var a in cls)
        {
            var l = a.Setups.Where(s => s.Era == 0 && s.NonVeto).OrderBy(s => s.Dot).ToList();
            var cl = Clusters(l.Select(s => s.Dot).ToList());
            var bySum = new Dictionary<int, double>();
            for (int k = 0; k < l.Count; k++)
            {
                double v = value(l[k]);
                bySum[cl[k]] = bySum.GetValueOrDefault(cl[k]) + v;
                tot += v; nSet++;
                if (confirmed(l[k])) nConf++;
            }
            sums.AddRange(bySum.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        }
        var row = new SetupGraderCommand.TestRow { Id = id, What = what, N = nSet, N2 = nConf };
        if (nSet < MinTestable) { row.Note = $"not testable: {nSet} setups (< {MinTestable}); count only"; return row; }
        if (nSet < MinConfirm) row.Note = "Suggestive only (15-29 setups)";
        else if (nConf < MinTestable) row.Note = $"Suggestive only (only {nConf} confirmed, < {MinTestable})";
        row.Stat = tot / nSet;
        row.P = SignFlipP(sums, nSet, row.Stat, twoSided, Draws, new Random(StableSeed.From($"{Seed}|{id}|{cn}")));
        row.Note = (row.Note.Length > 0 ? row.Note + "; " : "") + $"{sums.Count} clusters; n2 = confirmed";
        return row;
    }

    /// <summary>Group difference of trade excess by label permutation within each asset (dev era).</summary>
    private static SetupGraderCommand.TestRow OrderTest(string id, string what, List<Run> cls, Func<Setup, Trade?> pick,
        Func<Setup, bool> first, Func<Setup, bool> second, bool twoSided, bool includeVetoed = false)
    {
        var groups = cls.Select(a => (a.Label,
                F: a.Setups.Where(s => s.Era == 0 && (includeVetoed || s.NonVeto) && first(s) && pick(s) != null).Select(s => pick(s)!.Ex).ToArray(),
                S: a.Setups.Where(s => s.Era == 0 && (includeVetoed || s.NonVeto) && second(s) && pick(s) != null).Select(s => pick(s)!.Ex).ToArray()))
            .ToList();
        return GroupPerm(id, what, groups, twoSided);
    }

    /// <summary>Group difference of a per-setup value (S7), label permutation within each asset.</summary>
    private static SetupGraderCommand.TestRow ValueOrderTest(string id, string what, List<Run> cls, Func<Setup, double> value,
        Func<Setup, bool> first, Func<Setup, bool> second)
    {
        var groups = cls.Select(a => (a.Label,
                F: a.Setups.Where(s => s.Era == 0 && s.NonVeto && first(s)).Select(value).ToArray(),
                S: a.Setups.Where(s => s.Era == 0 && s.NonVeto && second(s)).Select(value).ToArray()))
            .ToList();
        return GroupPerm(id, what, groups, false);
    }

    private static SetupGraderCommand.TestRow GroupPerm(string id, string what, List<(string Asset, double[] F, double[] S)> groups, bool twoSided)
    {
        int nf = groups.Sum(g => g.F.Length), ns = groups.Sum(g => g.S.Length);
        if (nf < MinTestable || ns < MinTestable)
            return new SetupGraderCommand.TestRow { Id = id, What = what, N = nf, N2 = ns,
                Note = $"not testable: a group has fewer than {MinTestable} ({nf} vs {ns}); counts only" };
        var row = SetupGraderCommand.LabelPerm(id, what, groups, Seed, twoSided);
        if (nf < MinConfirm || ns < MinConfirm) row.Note = "Suggestive only (a group has 15-29)";
        return row;
    }

    // ── Held-out statistics (computed beside each test, printed only after every dev test) ──

    private static SetupGraderCommand.TestRow Held(Dictionary<string, (int, double)> held, SetupGraderCommand.TestRow row,
        List<Run> cls, Func<Setup, bool> sel, Func<Setup, Trade?> pick)
    {
        var l = HeldSetups(cls).Where(sel).Select(pick).Where(t => t != null).Select(t => t!.Ex).ToList();
        held[row.Id] = (l.Count, Mean(l));
        return row;
    }

    private static SetupGraderCommand.TestRow Held(Dictionary<string, (int, double)> held, SetupGraderCommand.TestRow row,
        List<Run> cls, Func<Setup, double> value, bool twoSided = false)
    {
        var l = HeldSetups(cls).Select(value).ToList();
        held[row.Id] = (l.Count, Mean(l));
        return row;
    }

    private static SetupGraderCommand.TestRow Held(Dictionary<string, (int, double)> held, SetupGraderCommand.TestRow row,
        List<Run> cls, Func<Setup, Trade?> pick, Func<Setup, bool> first, Func<Setup, bool> second, bool includeVetoed = false)
    {
        var h = HeldSetups(cls, includeVetoed).ToList();
        var f = h.Where(s => first(s) && pick(s) != null).Select(s => pick(s)!.Ex).ToList();
        var sc = h.Where(s => second(s) && pick(s) != null).Select(s => pick(s)!.Ex).ToList();
        held[row.Id] = (Math.Min(f.Count, sc.Count), Mean(f) - Mean(sc));
        return row;
    }

    private static SetupGraderCommand.TestRow Held(Dictionary<string, (int, double)> held, SetupGraderCommand.TestRow row,
        List<Run> cls, Func<Setup, double> value, Func<Setup, bool> first, Func<Setup, bool> second)
    {
        var h = HeldSetups(cls).ToList();
        var f = h.Where(first).Select(value).ToList();
        var sc = h.Where(second).Select(value).ToList();
        held[row.Id] = (Math.Min(f.Count, sc.Count), Mean(f) - Mean(sc));
        return row;
    }

    // ── Exits (section 5) ────────────────────────────────────────────────────

    private static IEnumerable<SetupGraderCommand.TestRow> ExitTests(string cn, List<Run> cls, string pol)
    {
        var runs = cls.ToDictionary(r => r.Label);
        var entries = DevSetups(cls).Select(s => (s, t: pol == "E0" ? s.E0 : s.E1)).Where(x => x.t != null).Select(x => (x.s, t: x.t!)).ToList();
        var curl = cls.ToDictionary(r => r.Label, r => MfCurl(r.S));
        Console.WriteLine($"══════ EXITS — {cn} {pol}: {entries.Count} non-vetoed entries, development era ══════");
        Console.WriteLine($"{"variant",-8} {"n",5} {"mean R",8} {"ATR u",8} {"hold",6} {"rand R",8} {"rand ATR",9} {"p",7} {"open",5}");
        var rows = new List<SetupGraderCommand.TestRow>();
        foreach (var v in new[] { "X1", "X2", "X3", "X4", "X5" })
        {
            var row = new SetupGraderCommand.TestRow { Id = $"{v}-{pol}-{cn}", What = $"{cn} {pol}: {v} mean R beats its random-exit control", N = entries.Count };
            if (entries.Count < MinTestable)
            {
                row.Note = $"not testable: {entries.Count} entries (< {MinTestable}); count only";
                if (v != "X5") rows.Add(row);
                continue;
            }
            double StopOf(Setup s, Trade t)
            {
                var r = runs[s.Asset];
                return v == "X1" ? r.S.Bars[t.Sig + 1].Open - 3 * r.S.Atr[t.Sig] : SchoolStop(r.S.Bars, r.S.Atr, s.Path.AnchorStart, t.Sig);
            }
            ExitResult Do(Setup s, Trade t)
            {
                var r = runs[s.Asset]; var b = r.S.Bars; var atr = r.S.Atr; double stop = StopOf(s, t);
                return v switch
                {
                    "X1" => SetupGraderCore.Ladder(b, atr, t.Sig, stop),
                    "X2" => SetupGraderCore.StopAndRule(b, atr, t.Sig, stop, r.S.BearX, t.Sig + SetupGraderCore.ExitCap),
                    "X3" => SetupGraderCore.StopAndRule(b, atr, t.Sig, stop, curl[s.Asset], t.Sig + SetupGraderCore.ExitCap),
                    "X4" => SetupGraderCore.StopAndRule(b, atr, t.Sig, stop, r.S.BearDiv, t.Sig + SetupGraderCore.ExitCap),
                    _ => SetupGraderCore.StopAndRule(b, atr, t.Sig, stop, null, t.Sig + SetupGraderCore.Horizon),
                };
            }
            var res = entries.Select(x => Do(x.s, x.t)).ToList();
            double mean = res.Average(x => x.RNet);
            var holds = res.Select(x => x.Hold).ToArray();
            var stops = entries.Select(x => StopOf(x.s, x.t)).ToArray();
            var rng = new Random(StableSeed.From($"{Seed}|{row.Id}|{cn}"));
            int ge = 0; double randSum = 0, randAtr = 0;
            for (int d = 0; d < ExitDraws; d++)
            {
                double sR = 0, sA = 0;
                for (int k = 0; k < entries.Count; k++)
                {
                    var (s, t) = entries[k];
                    var r = runs[s.Asset];
                    int h = holds[rng.Next(holds.Length)];
                    var rr = SetupGraderCore.StopAndRule(r.S.Bars, r.S.Atr, t.Sig, stops[k], null, t.Sig + 1 + h);
                    sR += rr.RNet; sA += rr.AtrNet;
                }
                sR /= entries.Count; sA /= entries.Count;
                randSum += sR; randAtr += sA;
                if (sR >= mean - 1e-12) ge++;
            }
            row.Stat = mean;
            row.P = (1.0 + ge) / (1.0 + ExitDraws);
            double randMean = randSum / ExitDraws;
            row.Note = $"random-exit mean {randMean:+0.000;-0.000}R; ATR units {res.Average(x => x.AtrNet):+0.000;-0.000} vs {randAtr / ExitDraws:+0.000;-0.000}";
            Console.WriteLine($"{v,-8} {res.Count,5} {mean,8:+0.000;-0.000} {res.Average(x => x.AtrNet),8:+0.000;-0.000} {holds.Average(),6:0.0} " +
                              $"{randMean,8:+0.000;-0.000} {randAtr / ExitDraws,9:+0.000;-0.000} {row.P,7:0.0000} {res.Count(x => !x.Complete),5}" +
                              (v == "X5" ? "   (reported, not tested)" : ""));
            if (v != "X5") rows.Add(row);
        }
        Console.WriteLine("(mean R net of 15 bps a side, per position from the initial risk; X1 is on a 3 ATR stop, X2-X5 on the school's stop, so compare across variants in ATR units)");
        Console.WriteLine();
        return rows;
    }

    // ── Noise (section 6.4) ──────────────────────────────────────────────────

    private static Dictionary<string, double> NoiseStats(List<Run> cls)
    {
        var dev = DevSetups(cls).ToList();
        var d = new Dictionary<string, double>
        {
            ["Ex0"] = Mean(dev.Select(s => s.E0!.Ex)),
            ["Ex1"] = Mean(dev.Where(s => s.E1 != null).Select(s => s.E1!.Ex)),
            ["dV"] = Mean(dev.Select(s => s.Dv(s.E1))),
        };
        var a = dev.Where(s => s.Grade == CtxA).ToList();
        var c = dev.Where(s => s.Grade == CtxC).ToList();
        d["ExA-E0"] = Mean(a.Select(s => s.E0!.Ex));
        d["ExA-ExC-E0"] = Mean(a.Select(s => s.E0!.Ex)) - Mean(c.Select(s => s.E0!.Ex));
        d["ExA-E1"] = Mean(a.Where(s => s.E1 != null).Select(s => s.E1!.Ex));
        d["ExA-ExC-E1"] = Mean(a.Where(s => s.E1 != null).Select(s => s.E1!.Ex)) - Mean(c.Where(s => s.E1 != null).Select(s => s.E1!.Ex));
        return d;
    }

    // ── Counts (section 6.7, no outcome) ─────────────────────────────────────

    private static void PrintCounts(string title, List<Run> cls)
    {
        Console.WriteLine($"══════ COUNTS — {title} ══════");
        Console.WriteLine($"{"asset",-6} {"bars",6} {"first",10} {"last",10} {"htf",5} {"ctx from",10} {"dots",5} {"dev",5} {"held",5} {"excl",5}");
        foreach (var a in cls)
        {
            int firstCtx = Array.FindIndex(a.S.Ctx, c => c != Undef);
            Console.WriteLine($"{a.Label,-6} {a.S.Bars.Count,6} {a.S.Bars[0].Date,10:yyyy-MM-dd} {a.S.Bars[^1].Date,10:yyyy-MM-dd} {a.S.Htf.Bars.Count,5} " +
                              $"{(firstCtx < 0 ? "never" : a.S.Bars[firstCtx].Date.ToString("yyyy-MM-dd")),10} {a.Dots,5} " +
                              $"{a.Setups.Count(s => s.Era == 0),5} {a.Setups.Count(s => s.Era == 1),5} {a.Excl.Values.Sum(),5}");
        }
        var agg = new Dictionary<string, int>();
        foreach (var a in cls) foreach (var (k, c) in a.Excl) agg[k] = agg.GetValueOrDefault(k) + c;
        Console.WriteLine("  excluded: " + (agg.Count == 0 ? "none" : string.Join("; ", agg.OrderByDescending(x => x.Value).Select(x => $"{x.Key}: {x.Value}"))));
        foreach (int era in new[] { 0, 1 })
        {
            var l = cls.SelectMany(a => a.Setups).Where(s => s.Era == era).ToList();
            string en = era == 0 ? "DEV (<2023)" : "HELD-OUT (2023+)";
            Console.WriteLine($"  {en}: eligible setups {l.Count}; by grade A {l.Count(s => s.Grade == CtxA)}, B {l.Count(s => s.Grade == CtxB)}, " +
                              $"C {l.Count(s => s.Grade == CtxC)}, V {l.Count(s => s.Grade == CtxV)}");
            foreach (var (gname, sel) in new (string, Func<Setup, bool>)[]
                     { ("non-vetoed", s => s.NonVeto), ("A", s => s.Grade == CtxA), ("B", s => s.Grade == CtxB), ("C", s => s.Grade == CtxC), ("V", s => s.Grade == CtxV) })
            {
                var g = l.Where(sel).ToList();
                if (g.Count == 0) { Console.WriteLine($"    {gname,-10} n=0"); continue; }
                string W(Func<Setup, WaitStatus> st) => $"conf {g.Count(s => st(s) == WaitStatus.Confirmed)}, void {g.Count(s => st(s) == WaitStatus.Void)}, " +
                                                        $"expired {g.Count(s => st(s) == WaitStatus.Expired)}, pending {g.Count(s => st(s) == WaitStatus.Pending)}";
                var waits = g.Where(s => s.Path.E1 == WaitStatus.Confirmed).Select(s => (double)(s.Path.E1Bar - s.Dot)).OrderBy(x => x).ToList();
                var mwaits = g.Where(s => s.Path.E1m == WaitStatus.Confirmed).Select(s => (double)(s.Path.E1mBar - s.Dot)).OrderBy(x => x).ToList();
                Console.WriteLine($"    {gname,-10} n={g.Count,5} | E1 {W(s => s.Path.E1)}, median wait {Median(waits)} bars | " +
                                  $"E1h {W(s => s.Path.E1h)} | E1m {W(s => s.Path.E1m)} (green at the dot {g.Count(s => s.Path.E1m == WaitStatus.Confirmed && s.Path.E1mBar == s.Dot)}), median wait {Median(mwaits)}");
            }
            var nv = l.Where(s => s.NonVeto).ToList();
            int clusters = cls.Sum(a => { var d = a.Setups.Where(s => s.Era == era && s.NonVeto).Select(s => s.Dot).OrderBy(x => x).ToList(); return d.Count == 0 ? 0 : Clusters(d).Max() + 1; });
            Console.WriteLine($"    clusters (non-vetoed setups, dots within 50 bars chained): {clusters}; " +
                              $"E1 confirmed with the HTF Buy dot in its last 3 bars: {nv.Count(s => s.Path.E1 == WaitStatus.Confirmed && s.HtfDot)}");
        }
        Console.WriteLine();
    }

    private static string Median(List<double> x) => x.Count == 0 ? "n/a" : (x.Count % 2 == 1 ? x[x.Count / 2] : 0.5 * (x[x.Count / 2 - 1] + x[x.Count / 2])).ToString("0.#");

    /// <summary>What the counts decide (section 6.7), and nothing else.</summary>
    private static void PrintTestability((string Cn, List<Run> Cls)[] classes)
    {
        Console.WriteLine("══════ WHAT THE COUNTS DECIDE (section 6.7: testable / Suggestive only / not testable / fallback) ══════");
        string State(int n) => n >= MinConfirm ? "confirmatory" : n >= MinTestable ? "Suggestive only (15-29)" : "not testable (<15)";
        foreach (var (cn, cls) in classes)
        {
            var dev = DevSetups(cls).ToList();
            var held = HeldSetups(cls).ToList();
            int e1 = dev.Count(s => s.Path.E1 == WaitStatus.Confirmed);
            Console.WriteLine($"  {cn}: P1 n={dev.Count} {State(dev.Count)}; P2 n={e1} {State(e1)}; P3 setups {dev.Count}, confirmed {e1} -> " +
                              $"{(dev.Count < MinTestable ? "not testable" : dev.Count < MinConfirm || e1 < MinTestable ? "Suggestive only" : "confirmatory")}");
            Console.WriteLine($"     held-out: E0 {held.Count}, E1 {held.Count(s => s.Path.E1 == WaitStatus.Confirmed)} (10 needed for the sign check)");
            if (cn == "crypto4h") continue;
            foreach (var pol in new[] { "E0", "E1" })
            {
                Func<Setup, bool> has = pol == "E0" ? _ => true : s => s.Path.E1 == WaitStatus.Confirmed;
                int a = dev.Count(s => s.Grade == CtxA && has(s)), b = dev.Count(s => s.Grade == CtxB && has(s)), c = dev.Count(s => s.Grade == CtxC && has(s));
                int ha = held.Count(s => s.Grade == CtxA && has(s));
                Console.WriteLine($"     {pol}: grade A {a} ({State(a)}), B {b}, C {c}; GO A vs C {(Math.Min(a, c) < MinTestable ? "not testable" : Math.Min(a, c) < MinConfirm ? "Suggestive only" : "confirmatory")}; " +
                                  $"A∪B fallback {(a < MinConfirm ? "TRIGGERED" : "not triggered")}; held-out grade A {ha}");
            }
            var h1 = dev.Count(s => s.Path.E1h == WaitStatus.Confirmed); var m1 = dev.Count(s => s.Path.E1m == WaitStatus.Confirmed);
            var vet = DevSetups(cls, true).Count(s => s.Grade == CtxV);
            Console.WriteLine($"     S1 vetoed {vet} vs non-vetoed {dev.Count}; S2 E1h trades {h1}; S3 E1m trades {m1}; " +
                              $"S6 E1 with HTF dot {dev.Count(s => s.Path.E1 == WaitStatus.Confirmed && s.HtfDot)} vs without {dev.Count(s => s.Path.E1 == WaitStatus.Confirmed && !s.HtfDot)}; " +
                              $"S7 setups C {dev.Count(s => s.Grade == CtxC)} vs A {dev.Count(s => s.Grade == CtxA)}");
        }
        Console.WriteLine();
    }

    // ── Descriptive (section 3.4) ────────────────────────────────────────────

    private static void Describe(string cn, List<Run> cls)
    {
        Console.WriteLine($"══════ DESCRIPTIVE — {cn} (net R; gross beside it; Ex = excess over the context-matched random bar) ══════");
        Console.WriteLine($"{"row",-44} {"era",-5} {"n",5} {"meanR",8} {"Ex",8} {"meanRg",8} {"ExG",8} {"ctl",8}");
        void Line(string name, string era, List<Trade> l)
        {
            if (l.Count == 0) { Console.WriteLine($"{name,-44} {era,-5} {0,5}"); return; }
            Console.WriteLine($"{name,-44} {era,-5} {l.Count,5} {l.Average(t => t.R),8:+0.000;-0.000} {l.Average(t => t.Ex),8:+0.000;-0.000} " +
                              $"{l.Average(t => t.Rg),8:+0.000;-0.000} {l.Average(t => t.ExG),8:+0.000;-0.000} {l.Average(t => t.Ctl),8:+0.000;-0.000}");
        }
        foreach (var (en, era) in new[] { ("dev", 0), ("held", 1) })
        {
            var all = cls.SelectMany(a => a.Setups).Where(s => s.Era == era).ToList();
            var nv = all.Where(s => s.NonVeto).ToList();
            Line("all Buy dots incl. vetoed, E0 (reference)", en, all.Select(s => s.E0!).ToList());
            foreach (var (pn, pick) in new (string, Func<Setup, Trade?>)[] { ("E0", s => s.E0), ("E1", s => s.E1), ("E1h", s => s.E1h), ("E1m", s => s.E1m) })
            {
                Line($"{pn} non-vetoed", en, nv.Select(pick).Where(t => t != null).Select(t => t!).ToList());
                foreach (var g in new[] { CtxA, CtxB, CtxC, CtxV })
                    Line($"  {pn} grade {CtxName(g)}", en, all.Where(s => s.Grade == g).Select(pick).Where(t => t != null).Select(t => t!).ToList());
            }
            Line("E0 on setups that went on to confirm (E1)", en, nv.Where(s => s.E1 != null).Select(s => s.E0!).ToList());
            Line("E0 on setups that did not confirm", en, nv.Where(s => s.E1 == null).Select(s => s.E0!).ToList());
            var conf = nv.Where(s => s.E1 != null).ToList();
            if (conf.Count > 0)
                Console.WriteLine($"  paired on {conf.Count} confirmed setups: mean Ex1 - Ex0 {conf.Average(s => s.E1!.Ex - s.E0!.Ex):+0.000;-0.000}; " +
                                  $"ΔV over all {nv.Count} non-vetoed setups {nv.Average(s => s.Dv(s.E1)):+0.000;-0.000}");
            if (conf.Count >= 2)
            {
                var depth = conf.Select(s => s.Path.TriggerTrough - s.Path.AnchorTrough).OrderBy(x => x).ToList();
                double med = depth.Count % 2 == 1 ? depth[depth.Count / 2] : 0.5 * (depth[depth.Count / 2 - 1] + depth[depth.Count / 2]);
                var hi = conf.Where(s => s.Path.TriggerTrough - s.Path.AnchorTrough > med).ToList();
                var lo = conf.Where(s => s.Path.TriggerTrough - s.Path.AnchorTrough <= med).ToList();
                Console.WriteLine($"  anchor-to-trigger depth difference: median {med:0.0} WT points; E1 Ex above median {Mean(hi.Select(s => s.E1!.Ex)):+0.000;-0.000} (n {hi.Count}), at or below {Mean(lo.Select(s => s.E1!.Ex)):+0.000;-0.000} (n {lo.Count})");
            }
            foreach (var g in new[] { CtxA, CtxB, CtxC, CtxV })
            {
                var gs = all.Where(s => s.Grade == g).ToList();
                if (gs.Count == 0) continue;
                Console.WriteLine($"  grade {CtxName(g)}: {gs.Count} setups, E1 confirmed {100.0 * gs.Count(s => s.Path.E1 == WaitStatus.Confirmed) / gs.Count:0}%, " +
                                  $"void {100.0 * gs.Count(s => s.Path.E1 == WaitStatus.Void) / gs.Count:0}%, expired {100.0 * gs.Count(s => s.Path.E1 == WaitStatus.Expired) / gs.Count:0}%");
            }
        }
        Console.WriteLine("  per asset, development era, non-vetoed (P1: Ex0, P2: Ex1, P3: ΔV; breadth lists need 10 trades or setups):");
        Console.WriteLine($"    {"asset",-6} {"n0",5} {"Ex0",8} {"n1",5} {"Ex1",8} {"ΔV",8} {"nA",4} {"ExA0",8} {"nA1",4} {"ExA1",8}");
        foreach (var a in cls)
        {
            var l = a.Setups.Where(s => s.Era == 0 && s.NonVeto).ToList();
            if (l.Count == 0) { Console.WriteLine($"    {a.Label,-6} {0,5}"); continue; }
            var c1 = l.Where(s => s.E1 != null).ToList();
            var aa = l.Where(s => s.Grade == CtxA).ToList();
            var a1 = aa.Where(s => s.E1 != null).ToList();
            Console.WriteLine($"    {a.Label,-6} {l.Count,5} {l.Average(s => s.E0!.Ex),8:+0.000;-0.000} {c1.Count,5} {Mean(c1.Select(s => s.E1!.Ex)),8:+0.000;-0.000} " +
                              $"{l.Average(s => s.Dv(s.E1)),8:+0.000;-0.000} {aa.Count,4} {Mean(aa.Select(s => s.E0!.Ex)),8:+0.000;-0.000} {a1.Count,4} {Mean(a1.Select(s => s.E1!.Ex)),8:+0.000;-0.000}");
        }
        Console.WriteLine();
    }

    // ── Verdicts (sections 6.9 and 6.10) ─────────────────────────────────────

    private static (int Pos, int Of) Breadth(List<Run> cls, Func<Run, List<double>> values, int min)
    {
        var lists = cls.Select(values).Where(l => l.Count >= min).ToList();
        return (lists.Count(l => l.Average() > 0), lists.Count);
    }

    private static string Label(SetupGraderCommand.TestRow t, int n, bool heldCheckable, bool heldOk, bool breadth, double retained, bool confirmatory)
    {
        if (n < MinTestable || double.IsNaN(t.P)) return "No edge (not testable)";
        bool c1 = t.Sig, c2 = heldCheckable && heldOk, c3 = breadth, c4 = !double.IsNaN(retained) && retained >= 0.5;
        if (confirmatory && c1 && c2 && c3 && c4) return "Supported";
        if (confirmatory && c1 && c2 && c3 && !c4) return "Fragile";
        if (t.P < Alpha && (!c1 || !confirmatory || !heldCheckable)) return "Suggestive";
        return "No edge";
    }

    private static void Verdict(string cn, List<Run> cls, List<SetupGraderCommand.TestRow> famP, List<SetupGraderCommand.TestRow> famG,
        Dictionary<string, (int N, double Stat)> held, Dictionary<string, double> ret)
    {
        Console.WriteLine($"  ── {cn} ──");
        var dev = DevSetups(cls).ToList();
        int nConf = dev.Count(s => s.E1 != null);
        var labels = new Dictionary<string, string>();
        foreach (var (id, key, name, breadthOf, n, conf) in new (string, string, string, Func<Run, List<double>>, int, bool)[]
                 {
                     ($"P1-{cn}", "Ex0", "the Buy dot at the next open has an edge",
                         r => r.Setups.Where(s => s.Era == 0 && s.NonVeto).Select(s => s.E0!.Ex).ToList(), dev.Count, dev.Count >= MinConfirm),
                     ($"P2-{cn}", "Ex1", "the trigger-wave entry has an edge",
                         r => r.Setups.Where(s => s.Era == 0 && s.NonVeto && s.E1 != null).Select(s => s.E1!.Ex).ToList(), nConf, nConf >= MinConfirm),
                     ($"P3-{cn}", "dV", "waiting beats not waiting",
                         r => r.Setups.Where(s => s.Era == 0 && s.NonVeto).Select(s => s.Dv(s.E1)).ToList(), dev.Count, dev.Count >= MinConfirm && nConf >= MinTestable),
                 })
        {
            var t = famP.First(x => x.Id == id);
            var h = held[id];
            bool chk = h.N >= MinHeld, ok = h.Stat > 0;
            var (pos, of) = Breadth(cls, breadthOf, MinBreadth);
            bool br = of > 0 && pos * 2 > of;
            string lab = Label(t, n, chk, ok, br, ret[key], conf);
            labels[id] = lab;
            Console.WriteLine($"    {id}: \"{name}\" -> {lab}");
            Console.WriteLine($"      1 Holm-significant {t.Sig} (stat {t.Stat:+0.000;-0.000}, p {t.P:0.0000}, Holm {t.PHolm:0.0000}, n {t.N}); " +
                              $"2 held-out > 0 {(chk ? ok.ToString() : "not checkable")} (n {h.N}, {h.Stat:+0.000;-0.000}); 3 breadth {br} ({pos} of {of}); " +
                              $"4 noise retained {100 * ret[key]:0}% {(ret[key] >= 0.5 ? "(>= 50%)" : "(< 50%)")}");
        }
        foreach (var pol in new[] { "E0", "E1" })
        {
            var ga = famG.First(x => x.Id == $"GA-{pol}-{cn}"); var go = famG.First(x => x.Id == $"GO-{pol}-{cn}");
            Func<Setup, Trade?> pick = pol == "E0" ? s => s.E0 : s => s.E1;
            var A = dev.Where(s => s.Grade == CtxA && pick(s) != null).Select(s => pick(s)!).ToList();
            var B = dev.Where(s => s.Grade == CtxB && pick(s) != null).Select(s => pick(s)!).ToList();
            var C = dev.Where(s => s.Grade == CtxC && pick(s) != null).Select(s => pick(s)!).ToList();
            bool order = A.Count > 0 && B.Count > 0 && C.Count > 0 && A.Average(x => x.R) > B.Average(x => x.R) && B.Average(x => x.R) > C.Average(x => x.R)
                         && A.Average(x => x.Ex) > B.Average(x => x.Ex) && B.Average(x => x.Ex) > C.Average(x => x.Ex);
            var hA = held[ga.Id]; var hO = held[go.Id];
            bool chk = hA.N >= MinHeld, hok = hA.Stat > 0 && hO.Stat > 0;
            var (pos, of) = Breadth(cls, r => r.Setups.Where(s => s.Era == 0 && s.Grade == CtxA && pick(s) != null).Select(s => pick(s)!.Ex).ToList(), MinBreadthA);
            bool br = of > 0 && pos * 2 > of;
            double rA = ret[$"ExA-{pol}"], rO = ret[$"ExA-ExC-{pol}"];
            bool noise = rA >= 0.5 && rO >= 0.5;
            bool conf = A.Count >= MinConfirm && C.Count >= MinConfirm;
            string lab;
            if (A.Count < MinTestable || double.IsNaN(ga.P)) lab = $"No edge (grade A not testable: {A.Count} trades)";
            else if (conf && ga.Sig && go.Sig && order && chk && hok && br && noise) lab = "Supported";
            else if (conf && ga.Sig && go.Sig && order && chk && hok && br) lab = "Fragile";
            else if (ga.P < Alpha && go.P < Alpha && (!ga.Sig || !go.Sig || !conf || !chk)) lab = "Suggestive";
            else lab = "No edge";
            string F(List<Trade> l, Func<Trade, double> f) => l.Count == 0 ? "n=0" : l.Average(f).ToString("+0.000;-0.000");
            Console.WriteLine($"    grades {pol}: -> {lab}. A {A.Count}, B {B.Count}, C {C.Count}; GA {ga.Stat:+0.000;-0.000} (p {ga.P:0.0000}, Holm {ga.PHolm:0.0000}); " +
                              $"GO {go.Stat:+0.000;-0.000} (p {go.P:0.0000}, Holm {go.PHolm:0.0000}); mean R A/B/C {F(A, x => x.R)} / {F(B, x => x.R)} / {F(C, x => x.R)}; " +
                              $"Ex {F(A, x => x.Ex)} / {F(B, x => x.Ex)} / {F(C, x => x.Ex)}; A > B > C {order}; held-out A n {hA.N} ExA {hA.Stat:+0.000;-0.000}, A-C {hO.Stat:+0.000;-0.000}; " +
                              $"breadth {pos} of {of}; noise ExA {100 * rA:0}%, A-C {100 * rO:0}%");
        }
        bool Passed(string id) => labels[id] is "Supported" or "Fragile";
        Console.WriteLine("    Section 6.10, in its own words:");
        if (!Passed($"P1-{cn}")) Console.WriteLine("      a Buy dot, bought at the next open, is no better than a random bar in the same higher-timeframe environment.");
        if (!Passed($"P2-{cn}")) Console.WriteLine("      waiting for the trigger wave with money flow turning up gives an entry no better than a random bar in the same environment.");
        if (!Passed($"P3-{cn}")) Console.WriteLine("      waiting is not better than buying the dot.");
        if (Passed($"P3-{cn}") && !Passed($"P2-{cn}")) Console.WriteLine("      waiting is better only because buying the dot loses, or because waiting trades less. Neither entry is an edge.");
        if (!Passed($"P1-{cn}") && !Passed($"P2-{cn}")) Console.WriteLine("      the Market Cipher entry, as written here, has no edge in this class, whatever P3, the grades, the secondary tests or the exits say.");
        Console.WriteLine();
    }

    private static void Verdict4h(List<Run> c4, List<SetupGraderCommand.TestRow> famS, Dictionary<string, (int N, double Stat)> held)
    {
        Console.WriteLine("  ── crypto 4h (S4, secondary: supports or contradicts the daily verdict in words only) ──");
        foreach (var id in new[] { "S4-P1-crypto4h", "S4-P2-crypto4h", "S4-P3-crypto4h" })
        {
            var t = famS.First(x => x.Id == id); var h = held[id];
            Console.WriteLine($"    {id}: stat {t.Stat:+0.000;-0.000}, n {t.N}, p {t.P:0.0000}, Holm {t.PHolm:0.0000}, held-out n {h.N} {h.Stat:+0.000;-0.000}{(t.Note.Length > 0 ? "  [" + t.Note + "]" : "")}");
        }
        Console.WriteLine();
    }

    // ── Data proof ───────────────────────────────────────────────────────────

    private static Dictionary<string, string> Hashes(string dir, List<string> files)
    {
        var d = new Dictionary<string, string>();
        foreach (var f in files)
        {
            using var s = File.OpenRead(Path.Combine(dir, f));
            d[f] = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        return d;
    }

    private static int CheckHashes(string dir, List<string> files, Dictionary<string, string> before)
    {
        var after = Hashes(dir, files);
        var changed = files.Where(f => after[f] != before[f]).ToList();
        Console.WriteLine($"Inputs re-hashed after the run: {after.Count} files, {changed.Count} changed.");
        foreach (var f in files) Console.WriteLine($"  sha256 {before[f]}  {f}");
        foreach (var f in changed) Console.WriteLine($"  ! CHANGED: {f}");
        return changed.Count == 0 ? 0 : 3;
    }
}
