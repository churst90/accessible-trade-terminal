using System.Security.Cryptography;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.DependencyInjection;
using static AccessibleTrader.StrategyLab.SetupGraderMc2Core;
using Mc2 = AccessibleTrader.StrategyLab.SetupGraderMc2Command;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Stage 2 of the round-3 setup grader: the Market Cipher school with Cipher SR as the location
/// (<c>docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md</c>, committed 97def0cc before any result existed).
/// Every rule, threshold, test, family, gate and verdict rule is that document's; this class only
/// computes them. Command: <c>setup-grader-mc3 [--counts-only]</c>.
///
/// <para>
/// THE QUESTION. Round 2's setups (every non-vetoed Buy dot), its two entries (E0 at the next open
/// after the dot, E1 at the trigger wave), its contexts and its R harness are reused EXACTLY, by
/// calling round 2's own code. One variable is added: is the signal bar at the current Cipher SR
/// support? L1 compares at-support setups with the rest; L2 compares at-support setups with random
/// bars that are also at support, in the same context; L3 compares random bars at support with all
/// random bars in the same context.
/// </para>
///
/// <para>
/// THE TRAPS IT IS BUILT AROUND. (1) The level a bar creates is not a level the bar tested: S*(t) is
/// the support live at t-1's close (<see cref="SetupGraderMc3Core"/>). (2) Round 2 must be reproduced
/// first: the run stops if its development-era counts (334/158, 70/33, 1,423/721) do not come back
/// exactly. (3) Any line near price looks like a level (ledger row 20): L1 and L3 must also beat 1,000
/// placebo draws with every level shifted 3 to 6 ATR. (4) Three rounds on the same dots: significance
/// is a Holm over all 119 pre-registered tests of rounds 1-3, with rounds 1 and 2 entering at their
/// published raw p (p = 1 where untested or not printed). (5) Noise must rebuild Cipher SR too: the
/// noisy bars get their own levels. (6) The counts-only mode computes no outcome: no R array exists
/// before it returns.
/// </para>
///
/// <para>
/// LITERAL READINGS, fixed in this code before any count or outcome was computed (the findings repeat
/// them):
/// <list type="bullet">
/// <item>Populations are round 2's non-vetoed eligible setups. A position (E0 at the dot, E1 at the
/// trigger) whose L is undefined (no support live at the previous close, or ATR undefined) is excluded
/// from every location population and counted. L3's rows are bars with a defined context, a defined
/// R and a defined L; the stratum mean of section 4.1 ("over all its bars") is over those rows.</item>
/// <item>Eras: the setup's era is its dot's date (round 2); a trade's control stratum uses its signal
/// bar's era (round 2, deviation 1). L3 bars carry their own date's era.</item>
/// <item>L1 and Family A permute labels within each asset (one era at a time, so asset x era).</item>
/// <item>Exits: the tested statistic is the variant's mean net R MINUS the mean of its random books
/// (the skill the test measures); the p is section 4.5's. Breadth for an exit is per asset (10 entries
/// or more): the asset's mean R minus its own random-book mean. Exits have no pre-registered noise run
/// (section 7.3 names Families L, LS and A), so criterion 5 cannot be met for an exit and an exit
/// meeting criteria 1-4 is at most Fragile.</item>
/// <item>Held-out check (criterion 3): the held-out statistic is positive, at least half the
/// development statistic, at least 30 held-out events (the smaller group for L1 and A), and the same
/// test run on the held-out era alone (its own 20,000 draws, its own seed) has one-sided p at or below
/// 0.10.</item>
/// <item>Placebo gate: real statistic strictly above the nearest-rank 95th percentile of the usable
/// draws (at least 500). A level whose ATR(c) is undefined gets no placebo level, so its bars are
/// undefined in that draw. The placebo stratum means for L3 are the real ones (the rows do not change).</item>
/// <item>Not-testable round-3 tests enter the cross-round Holm at p = 1, as rounds 1 and 2's do.</item>
/// <item>Round 2's exit p-values are not printed in its findings (only Holm p), so its 16 exit tests
/// enter at p = 1 (section 7.2's rule).</item>
/// </list>
/// </para>
/// </summary>
public static class SetupGraderMc3Command
{
    private const int Draws = 20_000, PlaceboDraws = 1_000, MinPlaceboUsable = 500;
    private const int MinTestable = 15, MinConfirm = 30, MinHeld = 30, MinBreadth = 10;
    private const double Alpha = 0.05, MinEffect = 0.05, HeldAlpha = 0.10;
    private const string Seed = "setup-grader-mc3";

    /// <summary>Round 2's development-era non-vetoed setups and E1 trades (its findings, section 6.7).</summary>
    internal static readonly (string Cls, int Setups, int E1)[] Round2Dev =
        { ("crypto4h", 334, 158), ("crypto", 70, 33), ("equities", 1_423, 721) };

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

    /// <summary>One policy's position in a setup, with the location read at its signal bar.</summary>
    internal sealed class Pos
    {
        public required Mc2.Setup Setup;
        public required string Pol;
        public required int Sig, L, Why;
        public double Level = double.NaN;
        public int LiveBar = -1, Age = -1;
        public bool Older, Tested, OnReplace;
        public double LocCtl = double.NaN, LocCtlG = double.NaN;
        public Mc2.Trade? T => Pol == "E0" ? Setup.E0 : Setup.E1;
        public string Asset => Setup.Asset;
        /// <summary>The setup's (dot's) era.</summary>
        public int Era => Setup.Era;
        public double Ex => T!.Ex;
        public double ExG => T!.ExG;
        public double LocEx => T!.R - LocCtl;
        public double LocExG => T!.Rg - LocCtlG;
    }

    internal sealed class Run3
    {
        public required Mc2.Run R2;
        public required Mc3Sr Sr;
        public readonly List<Pos> E0 = new(), E1 = new();
        /// <summary>Positions left out because L is undefined at their signal bar, by policy and era.</summary>
        public readonly Dictionary<(string Pol, int Era, int Why), int> Undefined = new();
        /// <summary>(era, ctx) -> bars with defined R and L = 1: the location control (L2).</summary>
        public Dictionary<(int, int), SetupGraderCommand.Stratum> LocStrata = new();
        /// <summary>(era, ctx) -> bars with defined R and defined L: L3's rows.</summary>
        public Dictionary<(int, int), SetupGraderCommand.Stratum> AllStrata = new();
        public string Label => R2.Label;
        public Mc2Series S => R2.S;
        public List<Pos> Of(string pol) => pol == "E0" ? E0 : E1;
    }

    internal sealed class Row
    {
        public required string Id, Family, Cls, What;
        public int N, N2;
        public double Stat = double.NaN, StatG = double.NaN, P = double.NaN, HolmFam = double.NaN, HolmX = double.NaN;
        public string State = "";
        public string Note = "";
        public int HeldN; public double HeldStat = double.NaN, HeldP = double.NaN;
        public int BreadthPos, BreadthOf;
        public double Retained = double.NaN;
        public bool HasPlacebo; public double PlaceboP95 = double.NaN; public int PlaceboUsable, PlaceboDropped; public bool PlaceboPass;
        public string Verdict = "";
        public bool Testable => State != "not testable" && !double.IsNaN(P);
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string snapshotDir, bool countsOnly)
    {
        Console.WriteLine("SETUP GRADER ROUND 3, MARKET CIPHER WITH CIPHER SR LOCATION — pre-registered in docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md (97def0cc), stage 2");
        Console.WriteLine(LabCipherProfile.Describe());
        if (!LabCipherProfile.IsDefault)
        {
            Console.WriteLine("The pre-registration fixes the shipped (MCB) Cipher B defaults. Refusing to run another profile.");
            return 2;
        }
        Console.WriteLine($"Snapshots: {Path.GetFullPath(snapshotDir)} (read only)");
        var files = Crypto4h.Concat(CryptoDaily).Concat(EquityNames.Select(e => $"yahoo_{e}_1d.json")).ToList();
        var missing = files.Where(f => !File.Exists(Path.Combine(snapshotDir, f))).ToList();
        foreach (var m in missing) Console.WriteLine($"  ! missing (not fetched): {m}");
        files = files.Except(missing).ToList();
        var before = Hashes(snapshotDir, files);
        Console.WriteLine($"Inputs hashed before the run: {before.Count} files");
        Console.WriteLine();

        var engine = LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
        List<(string Label, string Symbol, List<Ohlcv> Bars)> Load(IEnumerable<string> names) => names.Where(f => !missing.Contains(f))
            .Select(f => { var s = SnapshotCommand.Load(Path.Combine(snapshotDir, f)); return (Path.GetFileNameWithoutExtension(f).Split('_')[1], s.Symbol, s.Bars); }).ToList();

        var classes = new List<(string Cn, string Title, List<(string Label, string Symbol, List<Ohlcv> Bars)> Snaps, GraderSeriesConfig Cfg)>
        {
            ("crypto4h", "CRYPTO 4H (PRIMARY, daily environment)", Load(Crypto4h), GraderSeriesConfig.FourHourCrypto),
            ("crypto", "CRYPTO DAILY (secondary, weekly environment)", Load(CryptoDaily), GraderSeriesConfig.DailyCrypto),
            ("equities", "EQUITIES DAILY (secondary, weekly environment)", Load(EquityNames.Select(e => $"yahoo_{e}_1d.json")), GraderSeriesConfig.DailyEquity),
        };

        var built = new Dictionary<string, List<Run3>>();
        foreach (var (cn, _, snaps, cfg) in classes) built[cn] = await BuildClass3(engine, cn, snaps, cfg);

        // ── Section 3: the plumbing check, before anything else ──────────────
        Console.WriteLine("══════ PLUMBING CHECK (section 3): round 2's development-era counts, reproduced by round 2's own code ══════");
        var mism = Reproduce(built, Round2Dev, out var lines);
        foreach (var l in lines) Console.WriteLine("  " + l);
        if (mism.Count > 0)
        {
            Console.WriteLine("  STOP: round 2's counts did not reproduce. A mismatch is a bug to be found, not a reason to change a definition.");
            return Math.Max(4, CheckHashes(snapshotDir, files, before));
        }
        Console.WriteLine("  Reproduced exactly. Location is added only now.");
        Console.WriteLine();

        foreach (var (cn, title, _, _) in classes) PrintCounts(title, cn, built[cn]);
        var states = PrintTestability(classes.Select(c => (c.Cn, built[c.Cn])).ToArray());
        foreach (var (cn, _, _, _) in classes) PrintPlaceboLabels(cn, built[cn]);
        if (countsOnly)
        {
            Console.WriteLine("--counts-only: no outcome was computed (no R array was built). Stopping here.");
            return CheckHashes(snapshotDir, files, before);
        }

        foreach (var cls in built.Values) foreach (var r in cls) AddOutcomes3(r);

        // ── Descriptive (section 4.6) ───────────────────────────────────────
        foreach (var (cn, _, _, _) in classes) Describe(cn, built[cn]);

        // ── Families ─────────────────────────────────────────────────────────
        var famL = LocationFamily("L", "crypto4h", built["crypto4h"]);
        var famLS = LocationFamily("LS", "crypto", built["crypto"]).Concat(LocationFamily("LS", "equities", built["equities"])).ToList();
        var famA = new List<Row> { OlderTest("crypto4h", built["crypto4h"], "E0"), OlderTest("crypto4h", built["crypto4h"], "E1") };
        var famX = new List<Row>();
        foreach (var (cn, _, _, _) in classes)
            foreach (var pol in new[] { "E0", "E1" })
                famX.AddRange(ExitTests(cn, built[cn], pol));
        foreach (var f in new[] { famL, famLS, famA, famX }) ApplyHolm(f);

        PrintFamily("FAMILY L — the headline, crypto 4h (5 tests; within-family Holm printed, decides nothing)", famL);
        PrintFamily("FAMILY LS — crypto daily and equities daily (10 tests)", famLS);
        PrintFamily("FAMILY A — an older level, crypto 4h (2 tests)", famA);
        PrintFamily("FAMILY X — exits on the at-support trades against random exits (12 tests; X5 reported above, not tested)", famX);
        foreach (var cn in new[] { "crypto", "equities" }) DescribeOlder(cn, built[cn]);

        // ── Placebo gate (section 4.4) ──────────────────────────────────────
        Console.WriteLine("══════ PLACEBO GATE (section 4.4): every support shifted 3-6 ATR, random sign, 1,000 draws ══════");
        foreach (var (cn, _, _, _) in classes)
            Placebo(cn, built[cn], famL.Concat(famLS).Where(t => t.Cls == cn && (t.Id.StartsWith("L1-") || t.Id.StartsWith("L3-"))).ToList());
        Console.WriteLine();

        // ── Held-out era (read once, after every development test) ──────────
        var all = famL.Concat(famLS).Concat(famA).Concat(famX).ToList();
        Console.WriteLine("══════ HELD-OUT ERA (dots 2023-01-01 on): the same test on the held-out era alone ══════");
        Console.WriteLine($"{"id",-18} {"dev",8} {"held n",7} {"held",8} {"held p",7}  check");
        foreach (var t in all)
            Console.WriteLine($"{t.Id,-18} {t.Stat,8:+0.000;-0.000} {t.HeldN,7} {t.HeldStat,8:+0.000;-0.000} {t.HeldP,7:0.0000}  {HeldText(t)}");
        Console.WriteLine();

        // ── Noise (section 7.3): everything rebuilt on the noisy bars, Cipher SR included ──
        Console.WriteLine("══════ NOISE (section 7.3): gap-keeping perturbation at 25%, seeds 0-2, Cipher SR rebuilt on the noisy bars ══════");
        foreach (var (cn, _, snaps, cfg) in classes)
        {
            var clean = NoiseStats(built[cn]);
            var noisy = new List<Dictionary<string, double>>();
            for (int seed = 0; seed <= 2; seed++)
            {
                var nr = await NoisyClass(engine, cn, snaps, cfg, seed);
                foreach (var r in nr) AddOutcomes3(r);
                var st = NoiseStats(nr);
                noisy.Add(st);
                Console.WriteLine($"  {cn} seed {seed}: " + string.Join(", ", st.Select(kv => $"{kv.Key} {kv.Value:+0.000;-0.000}")));
            }
            foreach (var k in clean.Keys)
            {
                double m = noisy.Average(d => d[k]);
                double ret = m / clean[k];
                string id = $"{k}-{cn}";
                var row = all.FirstOrDefault(x => x.Id == id);
                if (row != null) row.Retained = clean[k] > 0 ? ret : double.NaN;
                Console.WriteLine($"  {id}: clean {clean[k]:+0.000;-0.000}, mean noisy {m:+0.000;-0.000}, retained " +
                                  (clean[k] > 0 ? $"{100 * ret:0}%" : "not meaningful (clean statistic not above 0)"));
            }
        }
        Console.WriteLine();

        // ── Cross-round Holm (section 7.2) ──────────────────────────────────
        var cross = CrossRound(all);
        PrintCrossRound(cross, all);

        // ── Verdicts (sections 7.4 and 7.5) ─────────────────────────────────
        Console.WriteLine("── VERDICT ──");
        foreach (var t in all) t.Verdict = VerdictOf(t);
        foreach (var (cn, _, _, _) in classes) ClassVerdict(cn, built[cn], all);
        return CheckHashes(snapshotDir, files, before);
    }

    // ── Building ─────────────────────────────────────────────────────────────

    /// <summary>Round 2's class build, unchanged, then Cipher SR on the same entry bars and the positions.</summary>
    internal static async Task<List<Run3>> BuildClass3(IIndicatorEngine engine, string cn,
        List<(string Label, string Symbol, List<Ohlcv> Bars)> snaps, GraderSeriesConfig cfg)
    {
        var r2 = await Mc2.BuildClass(engine, cn, snaps, cfg);
        var outp = new List<Run3>();
        for (int k = 0; k < r2.Count; k++)
        {
            var sr = await SetupGraderMc3Core.BuildAsync(engine, snaps[k].Symbol, r2[k].S.Bars, r2[k].S.Atr, cfg.Tf);
            var run = new Run3 { R2 = r2[k], Sr = sr };
            BuildPositions(run);
            outp.Add(run);
        }
        return outp;
    }

    /// <summary>Section 7.3: the gap-keeping perturbation (round 2's), then EVERYTHING rebuilt on the noisy
    /// bars by the same path as the clean build — higher-timeframe bars, Cipher B, setups, contexts, and
    /// Cipher SR's levels.</summary>
    internal static Task<List<Run3>> NoisyClass(IIndicatorEngine engine, string cn,
        List<(string Label, string Symbol, List<Ohlcv> Bars)> snaps, GraderSeriesConfig cfg, int seed)
    {
        var pb = snaps.Select(x => (x.Label, x.Symbol, Bars: PerturbKeepGap(x.Bars, 0.25, seed + StableSeed.From(x.Symbol) % 1000))).ToList();
        return BuildClass3(engine, cn, pb, cfg);
    }

    internal static void BuildPositions(Run3 r)
    {
        r.E0.Clear(); r.E1.Clear(); r.Undefined.Clear();
        foreach (var su in r.R2.Setups.Where(s => s.NonVeto))
        {
            Add(r, su, "E0", su.Dot);
            if (su.Path.E1 == WaitStatus.Confirmed) Add(r, su, "E1", su.Path.E1Bar);
        }
    }

    private static void Add(Run3 r, Mc2.Setup su, string pol, int t)
    {
        var sr = r.Sr;
        var p = new Pos { Setup = su, Pol = pol, Sig = t, L = sr.L[t], Why = sr.Why[t], OnReplace = !double.IsNaN(sr.SupReplaced[t]) };
        if (p.L == Undef)
        {
            var key = (pol, su.Era, p.Why);
            r.Undefined[key] = r.Undefined.GetValueOrDefault(key) + 1;
            return;
        }
        p.Level = sr.SStar[t];
        p.LiveBar = sr.SStarLive[t];
        p.Age = SetupGraderMc3Core.Age(sr, t);
        p.Older = SetupGraderMc3Core.Older(sr, t, su.Path.AnchorStart);
        p.Tested = sr.Tested[t];
        r.Of(pol).Add(p);
    }

    /// <summary>Round 2's outcomes (R on every bar, its context control), then the location strata and
    /// each at-support position's location control.</summary>
    internal static void AddOutcomes3(Run3 r)
    {
        Mc2.AddOutcomes(r.R2);
        BuildPositions(r);   // round 2 drops setups with no defined outcome; none were dropped in round 2
        Controls(r);
    }

    internal static void Controls(Run3 r)
    {
        var s = r.S; var L = r.Sr.L;
        r.LocStrata = Strata(r, t => L[t] == 1);
        r.AllStrata = Strata(r, t => L[t] != Undef);
        foreach (var p in r.E0.Concat(r.E1))
        {
            var t = p.T!;
            if (p.L != 1) continue;
            if (r.LocStrata.TryGetValue((t.Era, t.Ctx), out var st)) { p.LocCtl = st.Mean; p.LocCtlG = st.MeanG; }
        }
    }

    private static Dictionary<(int, int), SetupGraderCommand.Stratum> Strata(Run3 r, Func<int, bool> include)
    {
        var d = new Dictionary<(int, int), SetupGraderCommand.Stratum>();
        var s = r.S;
        for (int t = 0; t < s.Bars.Count; t++)
        {
            if (s.Ctx[t] == Undef || double.IsNaN(r.R2.R[t]) || !include(t)) continue;
            var key = (r.R2.Era[t], s.Ctx[t]);
            if (!d.TryGetValue(key, out var st)) d[key] = st = new SetupGraderCommand.Stratum();
            st.Pos[t] = st.Rows.Count;
            st.Rows.Add(t);
        }
        foreach (var st in d.Values) { st.Mean = st.Rows.Average(i => r.R2.R[i]); st.MeanG = st.Rows.Average(i => r.R2.Rg[i]); }
        return d;
    }

    /// <summary>Would round 2's harness give bar t a defined outcome? (StandardR's own conditions, read
    /// without computing it, so the counts-only stage can count L3's rows.)</summary>
    private static bool OutcomeDefinable(Mc2Series s, int t) =>
        t + SetupGraderCore.Horizon < s.Bars.Count && !double.IsNaN(s.Atr[t]) && s.Atr[t] > 0 && s.Bars[t + 1].Open > 0;

    // ── Section 3's plumbing check ───────────────────────────────────────────

    internal static (int Setups, int E1) Round2Counts(List<Run3> cls)
    {
        var dev = cls.SelectMany(r => r.R2.Setups).Where(s => s.Era == 0 && s.NonVeto).ToList();
        return (dev.Count, dev.Count(s => s.Path.E1 == WaitStatus.Confirmed));
    }

    internal static List<string> Reproduce(Dictionary<string, List<Run3>> built, (string Cls, int Setups, int E1)[] expected, out List<string> lines)
    {
        lines = new List<string>();
        var bad = new List<string>();
        foreach (var (cn, es, e1) in expected)
        {
            if (!built.TryGetValue(cn, out var cls)) { bad.Add(cn); lines.Add($"{cn}: not built"); continue; }
            var (s, e) = Round2Counts(cls);
            bool ok = s == es && e == e1;
            if (!ok) bad.Add(cn);
            lines.Add($"{cn}: setups {s} (round 2: {es}), E1 trades {e} (round 2: {e1}) -> {(ok ? "match" : "MISMATCH")}");
        }
        return bad;
    }

    // ── Selections ───────────────────────────────────────────────────────────

    private static IEnumerable<Pos> Positions(List<Run3> cls, string pol, int era) => cls.SelectMany(r => r.Of(pol)).Where(p => p.Era == era);
    private static double Mean(IEnumerable<double> x) { var l = x.ToList(); return l.Count == 0 ? double.NaN : l.Average(); }
    private static string State(int n) => n >= MinConfirm ? "confirmatory" : n >= MinTestable ? "Suggestive only" : "not testable";

    // ── The tests ────────────────────────────────────────────────────────────

    private static List<Row> LocationFamily(string fam, string cn, List<Run3> cls) => new()
    {
        AtVsNot(fam, cn, cls, "E0"), AtVsNot(fam, cn, cls, "E1"),
        AtVsRandomAt(fam, cn, cls, "E0"), AtVsRandomAt(fam, cn, cls, "E1"),
        LocationAlone(fam, cn, cls),
    };

    /// <summary>L1: mean context excess of at-support trades minus not-at-support trades; labels permuted
    /// within each asset x era stratum, each trade's value fixed.</summary>
    private static Row AtVsNot(string fam, string cn, List<Run3> cls, string pol)
    {
        string id = $"L1-{pol}-{cn}";
        var row = new Row { Id = id, Family = fam, Cls = cn, What = $"{cn} {pol}: at-support beat not-at-support (context excess)" };
        (double, double, int, int) Run(int era, string key)
        {
            var groups = cls.Select(a => (a.Label,
                F: a.Of(pol).Where(p => p.Era == era && p.L == 1).Select(p => p.Ex).ToArray(),
                S: a.Of(pol).Where(p => p.Era == era && p.L == 0).Select(p => p.Ex).ToArray())).ToList();
            int nf = groups.Sum(g => g.F.Length), ns = groups.Sum(g => g.S.Length);
            if (Math.Min(nf, ns) < MinTestable) return (double.NaN, double.NaN, nf, ns);
            var (st, p) = SetupGraderMc3Core.LabelPerm(key, groups, Draws);
            return (st, p, nf, ns);
        }
        var (stat, pv, n1, n2) = Run(0, $"{Seed}|{id}");
        row.N = n1; row.N2 = n2; row.State = State(Math.Min(n1, n2));
        row.Stat = stat; row.P = pv;
        var dev = cls.SelectMany(a => a.Of(pol)).Where(p => p.Era == 0).ToList();
        row.StatG = Mean(dev.Where(p => p.L == 1).Select(p => p.ExG)) - Mean(dev.Where(p => p.L == 0).Select(p => p.ExG));
        if (double.IsNaN(stat)) row.Stat = Mean(dev.Where(p => p.L == 1).Select(p => p.Ex)) - Mean(dev.Where(p => p.L == 0).Select(p => p.Ex));
        var (hs, hp, h1, h2) = Run(1, $"{Seed}|{id}|held");
        row.HeldN = Math.Min(h1, h2); row.HeldP = hp;
        var held = cls.SelectMany(a => a.Of(pol)).Where(p => p.Era == 1).ToList();
        row.HeldStat = double.IsNaN(hs) ? Mean(held.Where(p => p.L == 1).Select(p => p.Ex)) - Mean(held.Where(p => p.L == 0).Select(p => p.Ex)) : hs;
        Breadth(row, cls.Select(a =>
        {
            var f = a.Of(pol).Where(p => p.Era == 0 && p.L == 1).Select(p => p.Ex).ToList();
            var s = a.Of(pol).Where(p => p.Era == 0 && p.L == 0).Select(p => p.Ex).ToList();
            return (Math.Min(f.Count, s.Count), Mean(f) - Mean(s));
        }));
        return row;
    }

    /// <summary>L2: mean location excess of at-support trades, block permutation inside each asset x era x
    /// context x (L = 1) stratum.</summary>
    private static Row AtVsRandomAt(string fam, string cn, List<Run3> cls, string pol)
    {
        string id = $"L2-{pol}-{cn}";
        var row = new Row { Id = id, Family = fam, Cls = cn, What = $"{cn} {pol}: at-support beat at-support random bars (location excess)" };
        (double, double, int) Run(int era, string key)
        {
            var strata = new List<(string Asset, double[] Y, int[] Lab)>();
            int total = 0; double obs = 0;
            foreach (var a in cls)
            {
                var list = a.Of(pol).Where(p => p.Era == era && p.L == 1).ToList();
                foreach (var grp in list.GroupBy(p => (p.T!.Era, p.T!.Ctx)).OrderBy(g => g.Key))
                {
                    var st = a.LocStrata[grp.Key];
                    var y = st.Rows.Select(i => a.R2.R[i] - st.Mean).ToArray();
                    var lab = grp.Select(p => st.Pos[p.Sig]).ToArray();
                    strata.Add((a.Label, y, lab));
                    total += lab.Length; obs += lab.Sum(q => y[q]);
                }
            }
            if (total < MinTestable) return (total == 0 ? double.NaN : obs / total, double.NaN, total);
            double stat = obs / total;
            return (stat, SetupGraderMc3Core.BlockP(key, strata, stat, Draws), total);
        }
        var (s0, p0, n0) = Run(0, $"{Seed}|{id}");
        row.N = n0; row.State = State(n0); row.Stat = s0; row.P = p0;
        row.StatG = Mean(Positions(cls, pol, 0).Where(p => p.L == 1).Select(p => p.LocExG));
        var (s1, p1, n1) = Run(1, $"{Seed}|{id}|held");
        row.HeldN = n1; row.HeldStat = s1; row.HeldP = p1;
        Breadth(row, cls.Select(a =>
        {
            var l = a.Of(pol).Where(p => p.Era == 0 && p.L == 1).Select(p => p.LocEx).ToList();
            return (l.Count, Mean(l));
        }));
        return row;
    }

    /// <summary>L3 rows of one era: per asset, per context A/B/C stratum, y = R minus the stratum mean
    /// and the positions of the bars with L = 1.</summary>
    private static List<(string Asset, double[] Y, int[] Lab)> L3Strata(List<Run3> cls, int era, int[] ctxs, Func<Run3, int[]>? lOverride = null, bool gross = false)
    {
        var strata = new List<(string, double[], int[])>();
        foreach (var a in cls)
        {
            var L = lOverride?.Invoke(a) ?? a.Sr.L;
            foreach (var (key, st) in a.AllStrata.Where(kv => kv.Key.Item1 == era && ctxs.Contains(kv.Key.Item2)).OrderBy(kv => kv.Key))
            {
                var y = st.Rows.Select(i => gross ? a.R2.Rg[i] - st.MeanG : a.R2.R[i] - st.Mean).ToArray();
                var lab = st.Rows.Select((t, k) => (t, k)).Where(x => L[x.t] == 1).Select(x => x.k).ToArray();
                strata.Add((a.Label, y, lab));
            }
        }
        return strata;
    }

    private static readonly int[] Abc = { CtxA, CtxB, CtxC };

    /// <summary>L3: random bars at support against all random bars, in context A, B or C.</summary>
    private static Row LocationAlone(string fam, string cn, List<Run3> cls)
    {
        string id = $"L3-{cn}";
        var row = new Row { Id = id, Family = fam, Cls = cn, What = $"{cn}: at-support random bars beat all random bars (contexts A, B, C)" };
        (double, double, int) Run(int era, string key)
        {
            var strata = L3Strata(cls, era, Abc);
            int total = strata.Sum(s => s.Lab.Length);
            if (total == 0) return (double.NaN, double.NaN, 0);
            double stat = strata.Sum(s => s.Lab.Sum(q => s.Y[q])) / total;
            if (total < MinTestable) return (stat, double.NaN, total);
            return (stat, SetupGraderMc3Core.BlockP(key, strata, stat, Draws), total);
        }
        var (s0, p0, n0) = Run(0, $"{Seed}|{id}");
        row.N = n0; row.State = State(n0); row.Stat = s0; row.P = p0;
        var g = L3Strata(cls, 0, Abc, gross: true);
        row.StatG = g.Sum(s => s.Lab.Sum(q => s.Y[q])) / Math.Max(1, g.Sum(s => s.Lab.Length));
        var (s1, p1, n1) = Run(1, $"{Seed}|{id}|held");
        row.HeldN = n1; row.HeldStat = s1; row.HeldP = p1;
        Breadth(row, cls.Select(a =>
        {
            var st = L3Strata(new List<Run3> { a }, 0, Abc);
            int k = st.Sum(s => s.Lab.Length);
            return (k, k == 0 ? double.NaN : st.Sum(s => s.Lab.Sum(q => s.Y[q])) / k);
        }));
        return row;
    }

    /// <summary>Family A: location excess of older-level at-support trades minus newer-level ones, labels
    /// permuted within each asset x era stratum.</summary>
    private static Row OlderTest(string cn, List<Run3> cls, string pol)
    {
        string id = $"A-{pol}-{cn}";
        var row = new Row { Id = id, Family = "A", Cls = cn, What = $"{cn} {pol}: older-level at-support beat newer-level (location excess)" };
        (double, double, int, int) Run(int era, string key)
        {
            var groups = cls.Select(a => (a.Label,
                F: a.Of(pol).Where(p => p.Era == era && p.L == 1 && p.Older).Select(p => p.LocEx).ToArray(),
                S: a.Of(pol).Where(p => p.Era == era && p.L == 1 && !p.Older).Select(p => p.LocEx).ToArray())).ToList();
            int nf = groups.Sum(x => x.F.Length), ns = groups.Sum(x => x.S.Length);
            if (Math.Min(nf, ns) < MinTestable)
                return (nf > 0 && ns > 0 ? groups.SelectMany(x => x.F).Average() - groups.SelectMany(x => x.S).Average() : double.NaN, double.NaN, nf, ns);
            var (st, p) = SetupGraderMc3Core.LabelPerm(key, groups, Draws);
            return (st, p, nf, ns);
        }
        var (s0, p0, a0, b0) = Run(0, $"{Seed}|{id}");
        row.N = a0; row.N2 = b0; row.State = State(Math.Min(a0, b0)); row.Stat = s0; row.P = p0;
        var dev = Positions(cls, pol, 0).Where(p => p.L == 1).ToList();
        row.StatG = Mean(dev.Where(p => p.Older).Select(p => p.LocExG)) - Mean(dev.Where(p => !p.Older).Select(p => p.LocExG));
        var (s1, p1, a1, b1) = Run(1, $"{Seed}|{id}|held");
        row.HeldN = Math.Min(a1, b1); row.HeldStat = s1; row.HeldP = p1;
        Breadth(row, cls.Select(a =>
        {
            var f = a.Of(pol).Where(p => p.Era == 0 && p.L == 1 && p.Older).Select(p => p.LocEx).ToList();
            var s = a.Of(pol).Where(p => p.Era == 0 && p.L == 1 && !p.Older).Select(p => p.LocEx).ToList();
            return (Math.Min(f.Count, s.Count), Mean(f) - Mean(s));
        }));
        return row;
    }

    private static void Breadth(Row row, IEnumerable<(int N, double Stat)> perAsset)
    {
        var l = perAsset.Where(x => x.N >= MinBreadth && !double.IsNaN(x.Stat)).ToList();
        row.BreadthOf = l.Count; row.BreadthPos = l.Count(x => x.Stat > 0);
    }

    // ── Family X (section 4.5) ───────────────────────────────────────────────

    private static IEnumerable<Row> ExitTests(string cn, List<Run3> cls, string pol)
    {
        var runs = cls.ToDictionary(r => r.Label);
        Console.WriteLine($"══════ EXITS — {cn} {pol}: at-support trades, school stop ══════");
        Console.WriteLine($"{"variant",-7} {"era",-4} {"n",5} {"mean R",8} {"ATR u",7} {"hold",6} {"rand R",8} {"rand ATR",8} {"p",7}");
        var rows = new List<Row>();
        foreach (var v in new[] { "XR", "X4", "X5" })
        {
            var row = new Row { Id = $"{v}-{pol}-{cn}", Family = "X", Cls = cn, What = $"{cn} {pol}: {v} beats its random exits (mean R minus random mean)" };
            foreach (int era in new[] { 0, 1 })
            {
                var entries = Positions(cls, pol, era).Where(p => p.L == 1).ToList();
                if (entries.Count < MinTestable)
                {
                    Console.WriteLine($"{v,-7} {(era == 0 ? "dev" : "held"),-4} {entries.Count,5}  not testable (< {MinTestable} entries)");
                    if (era == 0) { row.N = entries.Count; row.State = "not testable"; }
                    else row.HeldN = entries.Count;
                    continue;
                }
                double StopOf(Pos p) { var r = runs[p.Asset]; return SchoolStop(r.S.Bars, r.S.Atr, p.Setup.Path.AnchorStart, p.Sig); }
                ExitResult Do(Pos p)
                {
                    var r = runs[p.Asset]; var b = r.S.Bars; var atr = r.S.Atr; double stop = StopOf(p);
                    return v switch
                    {
                        "XR" => SetupGraderMc3Core.ResistanceExit(b, atr, r.Sr.RStar, p.Sig, stop, p.Sig + SetupGraderCore.ExitCap),
                        "X4" => SetupGraderCore.StopAndRule(b, atr, p.Sig, stop, r.S.BearDiv, p.Sig + SetupGraderCore.ExitCap),
                        _ => SetupGraderCore.StopAndRule(b, atr, p.Sig, stop, null, p.Sig + SetupGraderCore.Horizon),
                    };
                }
                var res = entries.Select(Do).ToList();
                double mean = res.Average(x => x.RNet);
                var holds = res.Select(x => x.Hold).ToArray();
                var stops = entries.Select(StopOf).ToArray();
                var rng = new Random(StableSeed.From($"{Seed}|{row.Id}{(era == 1 ? "|held" : "")}|{cn}"));
                int ge = 0; double randSum = 0, randAtr = 0;
                var assetRand = new Dictionary<string, double>();
                for (int d = 0; d < Draws; d++)
                {
                    double sR = 0, sA = 0;
                    for (int k = 0; k < entries.Count; k++)
                    {
                        var q = entries[k]; var r = runs[q.Asset];
                        int h = holds[rng.Next(holds.Length)];
                        var rr = SetupGraderCore.StopAndRule(r.S.Bars, r.S.Atr, q.Sig, stops[k], null, q.Sig + 1 + h);
                        sR += rr.RNet; sA += rr.AtrNet;
                        if (era == 0) assetRand[q.Asset] = assetRand.GetValueOrDefault(q.Asset) + rr.RNet;
                    }
                    sR /= entries.Count; sA /= entries.Count;
                    randSum += sR; randAtr += sA;
                    if (sR >= mean - 1e-12) ge++;
                }
                double pv = (1.0 + ge) / (1.0 + Draws), randMean = randSum / Draws;
                Console.WriteLine($"{v,-7} {(era == 0 ? "dev" : "held"),-4} {res.Count,5} {mean,8:+0.000;-0.000} {res.Average(x => x.AtrNet),7:+0.000;-0.000} {holds.Average(),6:0.0} " +
                                  $"{randMean,8:+0.000;-0.000} {randAtr / Draws,8:+0.000;-0.000} {pv,7:0.0000}{(v == "X5" ? "   (reported, not tested)" : "")}");
                if (era == 0)
                {
                    row.N = res.Count; row.State = State(res.Count); row.Stat = mean - randMean; row.P = pv;
                    row.StatG = res.Average(x => x.RGross);
                    row.Note = $"mean R {mean:+0.000;-0.000}, random {randMean:+0.000;-0.000}; ATR units {res.Average(x => x.AtrNet):+0.000;-0.000} vs {randAtr / Draws:+0.000;-0.000}; mean hold {holds.Average():0.0}";
                    Breadth(row, entries.Select((q, k) => (q, k)).GroupBy(x => x.q.Asset)
                        .Select(g => (g.Count(), g.Average(x => res[x.k].RNet) - assetRand[g.Key] / Draws / g.Count())));
                }
                else { row.HeldN = res.Count; row.HeldStat = mean - randMean; row.HeldP = pv; }
            }
            if (v != "X5") rows.Add(row);
        }
        Console.WriteLine("(net of 15 bps a side, R per position from the initial risk, school stop; the tested statistic is mean R minus the random-exit mean)");
        Console.WriteLine();
        return rows;
    }

    // ── Placebo gate (section 4.4) ───────────────────────────────────────────

    private static void Placebo(string cn, List<Run3> cls, List<Row> rows)
    {
        var l1 = new Dictionary<string, List<double>> { ["E0"] = new(), ["E1"] = new() };
        var l3 = new List<double>();
        int[] dropped = new int[3];
        // L3's development rows per asset: (bar, y), the real stratum means.
        var rowsL3 = cls.ToDictionary(a => a.Label, a => a.AllStrata.Where(kv => kv.Key.Item1 == 0 && Abc.Contains(kv.Key.Item2))
            .SelectMany(kv => kv.Value.Rows.Select(t => (t, y: a.R2.R[t] - kv.Value.Mean))).ToList());
        for (int d = 0; d < PlaceboDraws; d++)
        {
            var lp = new Dictionary<string, int[]>();
            foreach (var a in cls)
            {
                var rng = new Random(StableSeed.From($"{Seed}-placebo|{d}|{a.Label}"));
                var off = SetupGraderMc3Core.PlaceboOffsets(a.Sr, a.S.Atr, rng);
                lp[a.Label] = SetupGraderMc3Core.PlaceboL(a.Sr, a.S.Bars, a.S.Atr, off);
            }
            int k = 0;
            foreach (var pol in new[] { "E0", "E1" })
            {
                var dev = Positions(cls, pol, 0).ToList();
                var at = dev.Where(p => lp[p.Asset][p.Sig] == 1).Select(p => p.Ex).ToList();
                var no = dev.Where(p => lp[p.Asset][p.Sig] == 0).Select(p => p.Ex).ToList();
                if (at.Count == 0 || no.Count == 0) dropped[k]++;
                else l1[pol].Add(at.Average() - no.Average());
                k++;
            }
            double sum = 0; int cnt = 0;
            foreach (var a in cls) foreach (var (t, y) in rowsL3[a.Label]) if (lp[a.Label][t] == 1) { sum += y; cnt++; }
            if (cnt == 0) dropped[2]++; else l3.Add(sum / cnt);
        }
        foreach (var row in rows)
        {
            List<double> dist; int drop;
            if (row.Id.StartsWith("L1-E0")) { dist = l1["E0"]; drop = dropped[0]; }
            else if (row.Id.StartsWith("L1-E1")) { dist = l1["E1"]; drop = dropped[1]; }
            else { dist = l3; drop = dropped[2]; }
            row.HasPlacebo = true; row.PlaceboUsable = dist.Count; row.PlaceboDropped = drop;
            if (dist.Count > 0) row.PlaceboP95 = SetupGraderMc3Core.Percentile95(dist);
            row.PlaceboPass = dist.Count >= MinPlaceboUsable && !double.IsNaN(row.Stat) && row.Stat > row.PlaceboP95;
            string q(double f) => dist.Count == 0 ? "n/a" : dist.OrderBy(x => x).ElementAt(Math.Clamp((int)Math.Ceiling(f * dist.Count) - 1, 0, dist.Count - 1)).ToString("+0.000;-0.000");
            Console.WriteLine($"  {row.Id,-18} real {row.Stat:+0.000;-0.000}; placebo usable {dist.Count}, dropped {drop}; placebo median {q(0.5)}, 95th percentile {row.PlaceboP95:+0.000;-0.000}, " +
                              $"share of draws at or above real {(dist.Count == 0 ? double.NaN : (double)dist.Count(x => x >= row.Stat) / dist.Count):0.000} -> gate {(row.PlaceboPass ? "PASSES" : "fails")}");
        }
    }

    // ── Noise statistics (development era) ───────────────────────────────────

    private static Dictionary<string, double> NoiseStats(List<Run3> cls)
    {
        var d = new Dictionary<string, double>();
        foreach (var pol in new[] { "E0", "E1" })
        {
            var dev = Positions(cls, pol, 0).ToList();
            d[$"L1-{pol}"] = Mean(dev.Where(p => p.L == 1).Select(p => p.Ex)) - Mean(dev.Where(p => p.L == 0).Select(p => p.Ex));
            d[$"L2-{pol}"] = Mean(dev.Where(p => p.L == 1).Select(p => p.LocEx));
            d[$"A-{pol}"] = Mean(dev.Where(p => p.L == 1 && p.Older).Select(p => p.LocEx)) - Mean(dev.Where(p => p.L == 1 && !p.Older).Select(p => p.LocEx));
        }
        var st = L3Strata(cls, 0, Abc);
        int n = st.Sum(s => s.Lab.Length);
        d["L3"] = n == 0 ? double.NaN : st.Sum(s => s.Lab.Sum(q => s.Y[q])) / n;
        return d;
    }

    // ── Holm ─────────────────────────────────────────────────────────────────

    private static void ApplyHolm(List<Row> fam)
    {
        var adj = SetupGraderCore.Holm(fam.Select(t => t.Testable ? t.P : double.NaN).ToList());
        for (int k = 0; k < fam.Count; k++) fam[k].HolmFam = adj[k];
    }

    /// <summary>Rounds 1 and 2's pre-registered tests with the raw p printed in their findings (p = 1
    /// where not testable, or where the findings print no raw p). Section 7.1's counts: 39 and 51.</summary>
    internal static List<(string Round, string Id, double P)> EarlierRounds()
    {
        var l = new List<(string, string, double)>();
        void A(string r, string id, double p) => l.Add((r, id, p));
        // Round 1 (docs/SETUP_GRADER_FINDINGS.md): primary 8, secondary 12, macro 5, exits 10, fallback 4.
        A("R1", "P1-crypto", 1); A("R1", "P2-crypto", 1); A("R1", "P3-crypto", 0.765); A("R1", "P4-crypto", 1);
        A("R1", "P1-equities", 1); A("R1", "P2-equities", 0.476); A("R1", "P3-equities", 0.0014); A("R1", "P4-equities", 1);
        A("R1", "S1-crypto", 0.923); A("R1", "S1-equities", 0.0024); A("R1", "S2-crypto", 1); A("R1", "S2-equities", 1);
        A("R1", "S3a-crypto", 1); A("R1", "S3b-crypto", 1); A("R1", "S3a-equities", 1); A("R1", "S3b-equities", 1);
        A("R1", "S4a-crypto4h", 1); A("R1", "S4b-crypto4h", 1); A("R1", "S5-crypto", 1); A("R1", "S5-equities", 1);
        A("R1", "M1-crypto", 0.730); A("R1", "M1-equities", 0.123); A("R1", "M2-crypto", 0.216); A("R1", "M2-equities", 0.774); A("R1", "M3-crypto", 1);
        foreach (var x in new[] { "X1", "X2", "X3", "X4", "X5" }) A("R1", $"{x}-crypto", 1);
        A("R1", "X1-equities", 0.242); A("R1", "X2-equities", 0.647); A("R1", "X3-equities", 0.376); A("R1", "X4-equities", 0.413); A("R1", "X5-equities", 0.488);
        A("R1", "AuB-equities", 0.53); A("R1", "AuB-C-equities", 0.84); A("R1", "AuB-crypto", 1); A("R1", "AuB-C-crypto", 1);
        // Round 2 (docs/SETUP_GRADER_ROUND2_MC_FINDINGS.md): P 6, G 8, S 19, X 16, fallback 2.
        A("R2", "P1-crypto", 0.719); A("R2", "P2-crypto", 0.067); A("R2", "P3-crypto", 0.075);
        A("R2", "P1-equities", 0.095); A("R2", "P2-equities", 0.063); A("R2", "P3-equities", 0.601);
        A("R2", "GA-E0-crypto", 1); A("R2", "GO-E0-crypto", 1); A("R2", "GA-E1-crypto", 1); A("R2", "GO-E1-crypto", 1);
        A("R2", "GA-E0-equities", 0.139); A("R2", "GO-E0-equities", 0.273); A("R2", "GA-E1-equities", 0.355); A("R2", "GO-E1-equities", 0.667);
        A("R2", "S1-crypto", 0.054); A("R2", "S1-equities", 0.270);
        A("R2", "S2a-crypto", 0.260); A("R2", "S2b-crypto", 0.042); A("R2", "S2a-equities", 0.147); A("R2", "S2b-equities", 0.605);
        A("R2", "S3a-crypto", 0.520); A("R2", "S3b-crypto", 0.212); A("R2", "S3a-equities", 0.150); A("R2", "S3b-equities", 0.744);
        A("R2", "S4-P1-crypto4h", 0.214); A("R2", "S4-P2-crypto4h", 0.014); A("R2", "S4-P3-crypto4h", 0.227);
        A("R2", "S5-crypto", 0.665); A("R2", "S5-equities", 0.508);
        A("R2", "S6-crypto", 1); A("R2", "S6-equities", 0.115); A("R2", "S7-crypto", 1); A("R2", "S7-equities", 0.203);
        foreach (var cn in new[] { "crypto", "equities" })
            foreach (var pol in new[] { "E0", "E1" })
                foreach (var x in new[] { "X1", "X2", "X3", "X4" }) A("R2", $"{x}-{pol}-{cn}", 1);   // only Holm p printed
        A("R2", "GOAB-E0-crypto", 0.247); A("R2", "GOAB-E1-crypto", 1);
        return l;
    }

    internal static List<(string Round, string Id, double P, double Holm)> CrossRound(List<Row> round3)
    {
        var l = EarlierRounds().Select(x => (x.Round, x.Id, x.P)).ToList();
        l.AddRange(round3.Select(t => ("R3", t.Id, t.Testable ? t.P : 1.0)));
        var adj = SetupGraderCore.Holm(l.Select(x => x.P).ToList());
        var outp = l.Select((x, k) => (x.Round, x.Id, x.P, adj[k])).ToList();
        foreach (var t in round3) t.HolmX = outp.First(x => x.Round == "R3" && x.Id == t.Id).Item4;
        return outp;
    }

    private static void PrintCrossRound(List<(string Round, string Id, double P, double Holm)> cross, List<Row> r3)
    {
        Console.WriteLine($"══════ CROSS-ROUND HOLM (section 7.2): {cross.Count} tests — round 1 {cross.Count(x => x.Round == "R1")}, round 2 {cross.Count(x => x.Round == "R2")}, round 3 {cross.Count(x => x.Round == "R3")} ══════");
        Console.WriteLine($"  Bonferroni line 0.05 / {cross.Count} = {0.05 / cross.Count:0.00000}. Smallest possible round-3 p with {Draws:N0} draws: {1.0 / (Draws + 1):0.00000}.");
        Console.WriteLine("  The 12 smallest raw p over all three rounds:");
        foreach (var x in cross.OrderBy(x => x.P).Take(12))
            Console.WriteLine($"    {x.Round} {x.Id,-18} raw {x.P,8:0.00000}  cross-round Holm {x.Holm,7:0.0000}  {(x.Holm <= Alpha ? "PASSES" : "fails")}");
        Console.WriteLine("  Re-reading rounds 1 and 2 (not a test): their Holm-significant results under the cross-round Holm:");
        foreach (var (r, id) in new[] { ("R1", "P3-equities"), ("R1", "S1-equities") })
        {
            var x = cross.First(c => c.Round == r && c.Id == id);
            Console.WriteLine($"    {r} {id}: raw {x.P}, cross-round Holm {x.Holm:0.0000} -> {(x.Holm <= Alpha ? "still passes" : "no longer passes")}");
        }
        Console.WriteLine("    R2 X4-E0-equities, X4-E1-equities, X1-E0-equities (round-2 Holm 0.008, 0.008, 0.042): their raw p is not printed in round 2's findings, so they enter at 1.");
        Console.WriteLine($"      Even at the smallest p round 2's 2,000 random books allow (1/2,001 = 0.0005), the first Holm step here needs 0.05 / {cross.Count} = {0.05 / cross.Count:0.00000}, so none could pass.");
        Console.WriteLine();
        Console.WriteLine("  Round 3, raw p, within-family Holm (decides nothing), cross-round Holm (decides):");
        foreach (var t in r3)
            Console.WriteLine($"    {t.Id,-18} raw {t.P,8:0.00000}  family Holm {t.HolmFam,7:0.0000}  cross-round Holm {t.HolmX,7:0.0000}  {(t.HolmX <= Alpha ? "PASSES" : "fails")}");
        Console.WriteLine();
    }

    // ── Verdicts ─────────────────────────────────────────────────────────────

    private static bool HeldCheckable(Row t) => t.HeldN >= MinHeld && !double.IsNaN(t.HeldP);
    private static bool HeldAgrees(Row t) => HeldCheckable(t) && t.HeldStat > 0 && t.HeldStat >= 0.5 * t.Stat && t.HeldP <= HeldAlpha;
    private static string HeldText(Row t)
    {
        if (!HeldCheckable(t)) return $"not checkable ({t.HeldN} events; {MinHeld} needed)";
        if (HeldAgrees(t)) return "agrees";
        var why = new List<string>();
        if (!(t.HeldStat > 0)) why.Add("sign");
        else if (t.HeldStat < 0.5 * t.Stat) why.Add("under half the size");
        if (!(t.HeldP <= HeldAlpha)) why.Add("p above 0.10");
        return "does not agree: " + string.Join(", ", why);
    }

    /// <summary>Section 7.4, criterion by criterion.</summary>
    internal static string VerdictOf(Row t)
    {
        if (!t.Testable) return "No edge (not testable)";
        bool c1 = !double.IsNaN(t.HolmX) && t.HolmX <= Alpha;
        bool c2 = t.Stat >= MinEffect;
        bool chk = HeldCheckable(t), c3 = HeldAgrees(t);
        bool c4 = t.BreadthOf > 0 && 2 * t.BreadthPos > t.BreadthOf;
        bool c5 = t.Family != "X" && !double.IsNaN(t.Retained) && t.Retained >= 0.5;
        bool c6 = !t.HasPlacebo || t.PlaceboPass;
        bool conf = t.State == "confirmatory";
        if (c1 && (!c2 || !c4 || !c6 || (chk && !c3))) return "No edge";
        if (conf && c1 && c2 && c3 && c4 && c6) return c5 ? "Supported" : t.Family == "X" ? "Fragile (exits have no pre-registered noise run)" : "Fragile";
        if (t.P < Alpha) return "Suggestive";
        return "No edge";
    }

    private static bool Passed(List<Row> all, string id) => all.First(x => x.Id == id).Verdict.StartsWith("Supported") || all.First(x => x.Id == id).Verdict.StartsWith("Fragile");

    private static void ClassVerdict(string cn, List<Run3> cls, List<Row> all)
    {
        Console.WriteLine($"  ── {cn}{(cn == "crypto4h" ? " (PRIMARY: the headline)" : " (secondary)")} ──");
        foreach (var t in all.Where(x => x.Cls == cn))
        {
            Console.WriteLine($"    {t.Id}: {t.Verdict}");
            Console.WriteLine($"      n {t.N}{(t.N2 > 0 ? " vs " + t.N2 : "")} ({t.State}); stat {t.Stat:+0.000;-0.000} (gross {t.StatG:+0.000;-0.000}); raw p {t.P:0.00000}; family Holm {t.HolmFam:0.0000}; cross-round Holm {t.HolmX:0.0000}");
            Console.WriteLine($"      1 cross-round Holm {(t.HolmX <= Alpha ? "yes" : "no")}; 2 at least +0.05R {(t.Stat >= MinEffect ? "yes" : "no")}; 3 held-out {HeldText(t)} (n {t.HeldN}, {t.HeldStat:+0.000;-0.000}, p {t.HeldP:0.0000}); " +
                              $"4 breadth {t.BreadthPos} of {t.BreadthOf}; 5 noise {(t.Family == "X" ? "not pre-registered for exits" : double.IsNaN(t.Retained) ? "not meaningful" : $"{100 * t.Retained:0}%")}" +
                              (t.HasPlacebo ? $"; 6 placebo gate {(t.PlaceboPass ? "passes" : "fails")} (95th percentile {t.PlaceboP95:+0.000;-0.000}, {t.PlaceboUsable} usable draws)" : ""));
        }
        foreach (var pol in new[] { "E0", "E1" })
        {
            var dev = Positions(cls, pol, 0).Where(p => p.L == 1).ToList();
            var held = Positions(cls, pol, 1).Where(p => p.L == 1).ToList();
            double dEx = Mean(dev.Select(p => p.Ex)), hEx = Mean(held.Select(p => p.Ex));
            bool method = Passed(all, $"L2-{pol}-{cn}") && all.First(x => x.Id == $"L2-{pol}-{cn}").Verdict.StartsWith("Supported") && dEx > 0 && hEx > 0;
            Console.WriteLine($"    method verdict {pol} (environment, then Cipher SR support, then the {(pol == "E0" ? "dot" : "trigger")}): {(method ? "Supported" : "No edge")}; " +
                              $"at-support context excess dev {dEx:+0.000;-0.000} (n {dev.Count}), held-out {hEx:+0.000;-0.000} (n {held.Count})");
        }
        Console.WriteLine("    Section 7.5, in its own words:");
        bool l2e0 = Passed(all, $"L2-E0-{cn}"), l2e1 = Passed(all, $"L2-E1-{cn}"), l3 = Passed(all, $"L3-{cn}");
        bool l1e0 = Passed(all, $"L1-E0-{cn}"), l1e1 = Passed(all, $"L1-E1-{cn}");
        if (!l2e0) Console.WriteLine("      at Cipher SR support, a Buy dot bought at the next open is no better than a random bar that is also at Cipher SR support, in the same environment.");
        if (!l2e1) Console.WriteLine("      at Cipher SR support, the trigger-wave entry is no better than a random bar that is also at support, in the same environment.");
        if (!l1e0) Console.WriteLine("      E0: setups at Cipher SR support do no better than setups away from it.");
        if (!l1e1) Console.WriteLine("      E1: setups at Cipher SR support do no better than setups away from it.");
        if (!l3) Console.WriteLine("      being at Cipher SR support, by itself, is no better than a random bar in the same environment.");
        if ((l1e0 && !l2e0) || (l1e1 && !l2e1)) Console.WriteLine("      the location helps, the dot does not. Any bar at support would have done as well.");
        if (!l2e0 && !l2e1)
        {
            Console.WriteLine("      the Market Cipher method as written here (environment, then Cipher SR support, then the dot or the trigger) has no edge in this class, whatever L1, L3, Family A or the exits say.");
            if (cn == "crypto4h") Console.WriteLine("      Across three pre-registered rounds, no Market Cipher B entry rule tested in this lab has beaten a matched random entry.");
        }
        var l3row = all.First(x => x.Id == $"L3-{cn}");
        if (l3row.HolmX <= Alpha && l3row.Stat >= MinEffect && !l3row.PlaceboPass)
            Console.WriteLine("      the placebo gate failed while L3 passed: the lift belongs to \"near a line the price came down to\", not to Cipher SR's levels.");
        foreach (var pol in new[] { "E0", "E1" })
        {
            var r = all.First(x => x.Id == $"L1-{pol}-{cn}");
            if (r.Testable && r.Stat < 0) Console.WriteLine($"      {pol}: at-support setups did WORSE than setups away from support ({r.Stat:+0.000;-0.000}R). That is not a \"buy away from levels\" rule; it would need its own pre-registration.");
        }
        Console.WriteLine();
    }

    // ── Printing ─────────────────────────────────────────────────────────────

    private static void PrintFamily(string title, List<Row> fam)
    {
        Console.WriteLine($"══════ {title} ══════");
        Console.WriteLine($"{"id",-18} {"n",6} {"n2",6} {"stat",8} {"gross",8} {"p raw",8} {"famHolm",8}  state / test");
        foreach (var t in fam)
            Console.WriteLine($"{t.Id,-18} {t.N,6} {(t.N2 > 0 ? t.N2.ToString() : ""),6} {t.Stat,8:+0.000;-0.000} {t.StatG,8:+0.000;-0.000} {t.P,8:0.00000} {t.HolmFam,8:0.0000}  " +
                              $"[{t.State}] {t.What}{(t.Note.Length > 0 ? "  (" + t.Note + ")" : "")}; breadth {t.BreadthPos} of {t.BreadthOf}");
        Console.WriteLine();
    }

    private static void PrintCounts(string title, string cn, List<Run3> cls)
    {
        Console.WriteLine($"══════ COUNTS — {title} ══════");
        Console.WriteLine($"{"asset",-6} {"bars",6} {"first",10} {"last",10} {"sup from",10} {"levels",6} {"dev E0",6} {"at",4} {"dev E1",6} {"at",4} {"held E0",7} {"at",4} {"held E1",7} {"at",4}");
        foreach (var a in cls)
        {
            int first = Array.FindIndex(a.Sr.Sup, v => !double.IsNaN(v));
            int levels = Enumerable.Range(0, a.Sr.Sup.Length).Count(t => a.Sr.LiveBar[t] == t);
            string C(string pol, int era) => $"{a.Of(pol).Count(p => p.Era == era)}";
            string At(string pol, int era) => $"{a.Of(pol).Count(p => p.Era == era && p.L == 1)}";
            Console.WriteLine($"{a.Label,-6} {a.S.Bars.Count,6} {a.S.Bars[0].Date,10:yyyy-MM-dd} {a.S.Bars[^1].Date,10:yyyy-MM-dd} " +
                              $"{(first < 0 ? "never" : a.S.Bars[first].Date.ToString("yyyy-MM-dd")),10} {levels,6} {C("E0", 0),6} {At("E0", 0),4} {C("E1", 0),6} {At("E1", 0),4} {C("E0", 1),7} {At("E0", 1),4} {C("E1", 1),7} {At("E1", 1),4}");
        }
        foreach (int era in new[] { 0, 1 })
        {
            string en = era == 0 ? "DEV (<2023)" : "HELD-OUT (2023+)";
            Console.WriteLine($"  {en}:");
            foreach (var pol in new[] { "E0", "E1" })
            {
                var l = cls.SelectMany(a => a.Of(pol)).Where(p => p.Era == era).ToList();
                int undef = cls.Sum(a => a.Undefined.Where(kv => kv.Key.Pol == pol && kv.Key.Era == era).Sum(kv => kv.Value));
                int at = l.Count(p => p.L == 1);
                Console.WriteLine($"    {pol}: {l.Count + undef} non-vetoed positions; L undefined {undef} (excluded); defined {l.Count}: at support {at} ({(l.Count == 0 ? 0 : 100.0 * at / l.Count):0.0}%), " +
                                  $"not {l.Count - at} (low too far above {l.Count(p => p.Why == LocWhy.TooFar)}, closed at or below {l.Count(p => p.Why == LocWhy.ClosedBelow)})" +
                                  (l.Count > 0 && (at < 0.1 * l.Count || at > 0.9 * l.Count) ? "  [EXTREME SHARE: below 10% or above 90%; nothing changes]" : ""));
                foreach (var g in new[] { CtxA, CtxB, CtxC })
                {
                    var gl = l.Where(p => p.Setup.Grade == g).ToList();
                    Console.WriteLine($"      grade {CtxName(g)}: {gl.Count}, at support {gl.Count(p => p.L == 1)}, not {gl.Count(p => p.L == 0)}");
                }
                var atl = l.Where(p => p.L == 1).ToList();
                var ages = atl.Select(p => (double)p.Age).OrderBy(x => x).ToList();
                Console.WriteLine($"      at support: older level {atl.Count(p => p.Older)}, newer {atl.Count(p => !p.Older)}; on a replacement bar {l.Count(p => p.OnReplace)} (of which at support {atl.Count(p => p.OnReplace)}); " +
                                  $"\"tested\" in Cipher SR's own sense {atl.Count(p => p.Tested)}; level age in bars q1 {Q(ages, 0.25)}, median {Q(ages, 0.5)}, q3 {Q(ages, 0.75)}");
                if (undef > 0)
                    Console.WriteLine("      undefined: " + string.Join(", ", cls.SelectMany(a => a.Undefined).Where(kv => kv.Key.Pol == pol && kv.Key.Era == era)
                        .GroupBy(kv => kv.Key.Why).Select(g => $"{LocWhy.Name(g.Key)} {g.Sum(kv => kv.Value)}")));
            }
            // At-support bars per context, the L2 and L3 strata (bars whose outcome round 2's harness can define).
            var line = new List<string>();
            int undefBars = 0;
            foreach (var c in new[] { CtxA, CtxB, CtxC, CtxV })
            {
                int all = 0, at = 0;
                foreach (var a in cls)
                    for (int t = 0; t < a.S.Bars.Count; t++)
                    {
                        if (a.R2.Era[t] != era || a.S.Ctx[t] != c || !OutcomeDefinable(a.S, t)) continue;
                        if (a.Sr.L[t] == Undef) { undefBars++; continue; }
                        all++; if (a.Sr.L[t] == 1) at++;
                    }
                line.Add($"{CtxName(c)} {at} of {all} ({(all == 0 ? 0 : 100.0 * at / all):0.0}%)");
            }
            Console.WriteLine($"    random bars at support, by context: {string.Join("; ", line)}; bars with L undefined (excluded) {undefBars}");
        }
        Console.WriteLine();
    }

    private static string Q(List<double> sorted, double f) => sorted.Count == 0 ? "n/a"
        : sorted[Math.Clamp((int)Math.Ceiling(f * sorted.Count) - 1, 0, sorted.Count - 1)].ToString("0");

    /// <summary>What the counts decide (section 5), and nothing else.</summary>
    private static Dictionary<string, string> PrintTestability((string Cn, List<Run3> Cls)[] classes)
    {
        Console.WriteLine("══════ WHAT THE COUNTS DECIDE (section 5: confirmatory 30+, Suggestive only 15-29, not testable < 15; held-out needs 30) ══════");
        var d = new Dictionary<string, string>();
        string H(int n) => n >= MinHeld ? "checkable" : "NOT checkable";
        foreach (var (cn, cls) in classes)
        {
            Console.WriteLine($"  {cn}:");
            foreach (var pol in new[] { "E0", "E1" })
            {
                int at = Positions(cls, pol, 0).Count(p => p.L == 1), no = Positions(cls, pol, 0).Count(p => p.L == 0);
                int hat = Positions(cls, pol, 1).Count(p => p.L == 1), hno = Positions(cls, pol, 1).Count(p => p.L == 0);
                int old = Positions(cls, pol, 0).Count(p => p.L == 1 && p.Older), nw = at - old;
                int hold = Positions(cls, pol, 1).Count(p => p.L == 1 && p.Older), hnw = hat - hold;
                d[$"L1-{pol}-{cn}"] = State(Math.Min(at, no)); d[$"L2-{pol}-{cn}"] = State(at); d[$"A-{pol}-{cn}"] = State(Math.Min(old, nw));
                Console.WriteLine($"    L1-{pol}: {at} at vs {no} not -> {State(Math.Min(at, no))}; held-out {hat} vs {hno} -> {H(Math.Min(hat, hno))}");
                Console.WriteLine($"    L2-{pol}: {at} at-support trades -> {State(at)}; held-out {hat} -> {H(hat)}");
                Console.WriteLine($"    {(cn == "crypto4h" ? "A" : "A (reported, not tested)")}-{pol}: older {old} vs newer {nw} -> {State(Math.Min(old, nw))}; held-out {hold} vs {hnw} -> {H(Math.Min(hold, hnw))}");
                Console.WriteLine($"    XR/X4-{pol}: {at} entries -> {(at >= MinTestable ? "testable" : "not testable")}; held-out {hat} -> {H(hat)}");
            }
            int bars = 0, hbars = 0;
            foreach (var a in cls)
                for (int t = 0; t < a.S.Bars.Count; t++)
                {
                    if (!Abc.Contains(a.S.Ctx[t]) || !OutcomeDefinable(a.S, t) || a.Sr.L[t] != 1) continue;
                    if (a.R2.Era[t] == 0) bars++; else hbars++;
                }
            d[$"L3-{cn}"] = State(bars);
            Console.WriteLine($"    L3: {bars} at-support bars in contexts A-C -> {State(bars)}; held-out {hbars} -> {H(hbars)}");
        }
        Console.WriteLine();
        return d;
    }

    private static void PrintPlaceboLabels(string cn, List<Run3> cls)
    {
        var lp = new Dictionary<string, int[]>();
        foreach (var a in cls)
        {
            var rng = new Random(StableSeed.From($"{Seed}-placebo|0|{a.Label}"));
            lp[a.Label] = SetupGraderMc3Core.PlaceboL(a.Sr, a.S.Bars, a.S.Atr, SetupGraderMc3Core.PlaceboOffsets(a.Sr, a.S.Atr, rng));
        }
        var parts = new List<string>();
        foreach (var pol in new[] { "E0", "E1" })
        {
            var dev = Positions(cls, pol, 0).ToList();
            parts.Add($"{pol} at placebo support {dev.Count(p => lp[p.Asset][p.Sig] == 1)}, not {dev.Count(p => lp[p.Asset][p.Sig] == 0)}, undefined {dev.Count(p => lp[p.Asset][p.Sig] == Undef)}");
        }
        int bars = 0;
        foreach (var a in cls)
            for (int t = 0; t < a.S.Bars.Count; t++)
                if (a.R2.Era[t] == 0 && Abc.Contains(a.S.Ctx[t]) && OutcomeDefinable(a.S, t) && a.Sr.L[t] != Undef && lp[a.Label][t] == 1) bars++;
        Console.WriteLine($"  PLACEBO DRAW 0 LABELS (labels only, no outcome) — {cn}, development era: {string.Join("; ", parts)}; random bars at placebo support (A-C) {bars}");
    }

    private static void Describe(string cn, List<Run3> cls)
    {
        Console.WriteLine($"══════ DESCRIPTIVE — {cn} (net R; gross beside it) ══════");
        Console.WriteLine($"{"row",-40} {"era",-5} {"n",5} {"meanR",8} {"ctxEx",8} {"locEx",8} {"ctxExG",8}");
        void Line(string name, string era, List<Pos> l)
        {
            if (l.Count == 0) { Console.WriteLine($"{name,-40} {era,-5} {0,5}"); return; }
            Console.WriteLine($"{name,-40} {era,-5} {l.Count,5} {l.Average(p => p.T!.R),8:+0.000;-0.000} {l.Average(p => p.Ex),8:+0.000;-0.000} " +
                              $"{Mean(l.Where(p => p.L == 1).Select(p => p.LocEx)),8:+0.000;-0.000} {l.Average(p => p.ExG),8:+0.000;-0.000}");
        }
        foreach (var (en, era) in new[] { ("dev", 0), ("held", 1) })
            foreach (var pol in new[] { "E0", "E1" })
            {
                var l = Positions(cls, pol, era).ToList();
                Line($"{pol} at support", en, l.Where(p => p.L == 1).ToList());
                Line($"{pol} not at support", en, l.Where(p => p.L == 0).ToList());
                foreach (var g in new[] { CtxA, CtxB, CtxC })
                    Line($"  {pol} at support, grade {CtxName(g)}", en, l.Where(p => p.L == 1 && p.Setup.Grade == g).ToList());
                Line($"  {pol} at support, older level", en, l.Where(p => p.L == 1 && p.Older).ToList());
                Line($"  {pol} at support, newer level", en, l.Where(p => p.L == 1 && !p.Older).ToList());
                Line($"  {pol} at support and \"tested\" (no band)", en, l.Where(p => p.L == 1 && p.Tested).ToList());
            }
        foreach (var (en, era) in new[] { ("dev", 0), ("held", 1) })
        {
            var v = L3Strata(cls, era, new[] { CtxV });
            int n = v.Sum(s => s.Lab.Length);
            Console.WriteLine($"  L3 in context V (reported, not tested), {en}: {n} at-support bars, mean value {(n == 0 ? double.NaN : v.Sum(s => s.Lab.Sum(q => s.Y[q])) / n):+0.000;-0.000}");
        }
        Console.WriteLine("  per asset, development era (L1: at minus not, context excess; L2: location excess; L3: at-support bars):");
        Console.WriteLine($"    {"asset",-6} {"E0 at",5} {"not",5} {"L1-E0",7} {"L2-E0",7} {"E1 at",5} {"not",5} {"L1-E1",7} {"L2-E1",7} {"L3 n",6} {"L3",7}");
        foreach (var a in cls)
        {
            string F(double x) => double.IsNaN(x) ? "n/a" : x.ToString("+0.000;-0.000");
            var e0 = a.E0.Where(p => p.Era == 0).ToList(); var e1 = a.E1.Where(p => p.Era == 0).ToList();
            var st = L3Strata(new List<Run3> { a }, 0, Abc); int k = st.Sum(s => s.Lab.Length);
            Console.WriteLine($"    {a.Label,-6} {e0.Count(p => p.L == 1),5} {e0.Count(p => p.L == 0),5} {F(Mean(e0.Where(p => p.L == 1).Select(p => p.Ex)) - Mean(e0.Where(p => p.L == 0).Select(p => p.Ex))),7} " +
                              $"{F(Mean(e0.Where(p => p.L == 1).Select(p => p.LocEx))),7} {e1.Count(p => p.L == 1),5} {e1.Count(p => p.L == 0),5} " +
                              $"{F(Mean(e1.Where(p => p.L == 1).Select(p => p.Ex)) - Mean(e1.Where(p => p.L == 0).Select(p => p.Ex))),7} {F(Mean(e1.Where(p => p.L == 1).Select(p => p.LocEx))),7} " +
                              $"{k,6} {F(k == 0 ? double.NaN : st.Sum(s => s.Lab.Sum(q => s.Y[q])) / k),7}");
        }
        Console.WriteLine();
    }

    private static void DescribeOlder(string cn, List<Run3> cls)
    {
        foreach (var pol in new[] { "E0", "E1" })
            foreach (int era in new[] { 0, 1 })
            {
                var at = Positions(cls, pol, era).Where(p => p.L == 1).ToList();
                var o = at.Where(p => p.Older).ToList(); var w = at.Where(p => !p.Older).ToList();
                Console.WriteLine($"  Family A split in {cn} (reported, not tested), {pol} {(era == 0 ? "dev" : "held")}: older {o.Count} {Mean(o.Select(p => p.LocEx)):+0.000;-0.000}, newer {w.Count} {Mean(w.Select(p => p.LocEx)):+0.000;-0.000}");
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
