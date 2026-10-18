using System.Security.Cryptography;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace AccessibleTrader.StrategyLab;

/// <summary>
/// Stage 2 of the pre-registered setup grader for the Cipher B Buy dot
/// (<c>docs/SETUP_GRADER_PREREG.md</c>, committed 0597d7de before any result existed). Every rule,
/// threshold, test, family and verdict rule is that document's; this class only computes them.
///
/// <para>
/// THE TRAPS IT IS BUILT AROUND, all named in the pre-registration's section 2.2.
/// (1) The app's higher-timeframe path treats a weekly bar as closed once it OPENS, so Wednesday
/// sees Friday. Weekly (and, for S4, daily) bars are built here from the trigger bars, completed
/// buckets only (<see cref="SetupGraderCore.Aggregate"/>).
/// (2) The engine's per-exit R divides by the stop in force at that exit, so after a breakeven or
/// trail move the later rows have zero risk. R here is per POSITION from the INITIAL risk
/// (<see cref="SetupGraderCore.Ladder"/>).
/// (3) The engine has no condition exits or time stops, so the five exits are written here.
/// (4) A good context lifts any long. So a trigger is read as its EXCESS over the averaged random
/// entry: every bar on the same asset, in the same era, with the same context grade.
/// (5) Overlapping 20-bar outcomes make row-wise permutation far too narrow, so trigger labels
/// move in 20-bar blocks within each asset x era x context stratum.
/// (6) Undefined is never false: a trigger with any undefined input is excluded and counted.
/// </para>
///
/// <para>
/// DEVIATIONS AND LITERAL READINGS, all fixed in this code before the first outcome was computed
/// (see the commit that adds this file; the findings document repeats them):
/// <list type="bullet">
/// <item>Crypto weeks are calendar weeks (Monday 00:00 UTC) complete at the following Monday
/// 00:00, i.e. usable on the Sunday bar; equity weeks are complete on their last trading day,
/// which the exchange calendar fixes in advance. The final bucket of a series is kept only when it
/// is known finished (24/7: its calendar span has ended; equities: its last bar is a Friday).</item>
/// <item>The matched control for a bar requires only its context grade and its outcome to be
/// defined, as section 6.4 says, not every trigger input.</item>
/// <item>Section 6.5 gives no scheme for S1, S2 or the macro tests. S1 and the macro Δ use the
/// ordering scheme (label permutation within asset x era, each trigger's excess held fixed); S2's
/// p is (1 + placebo draws at or above the real ExA) / 201. With 200 draws S2 cannot clear Holm's
/// first step (0.05/12 &lt; 1/201); that is a property of the frozen numbers, reported, not changed.</item>
/// <item>The S2 placebo shifts every candidate level, the weekly EMA(10) of each week included,
/// each by its own draw, scaled by the daily ATR on the bar the level became known.</item>
/// <item>Exit fills: a stop that the bar opens through fills at the open (the engine's rule); a
/// target the bar opens beyond fills at the open. X1/X2 move the stop to the entry after the 2R
/// exit and then trail with the engine's own TrailByAtr arithmetic, as "the lab's current style".
/// X3/X4's "first bar after entry" includes the entry bar's close. The 60-bar cap and X5's 20 bars
/// end at the close of bar i+60 / i+20. The random-exit control keeps the variant's INITIAL stop
/// (targets and the trail are the exit being tested). Its p is the literal share of 1,000 books
/// with mean R at least the variant's.</item>
/// <item>Holm uses the full pre-registered family size even if a member is untestable.</item>
/// <item>"Suggestive" is read as: criterion 3 holds, P1 and P4 each have raw p &lt; 0.05, grade A
/// has at least 15 triggers, the held-out signs do not fail where they are checkable, and the
/// class is not Supported or Fragile. Section 8's no-edge
/// conditions are reported beside it, because a Suggestive class still meets them.</item>
/// <item>The A∪B fallback tests are reported as Suggestive and are not added to the primary
/// family's Holm correction (section 7.4 says they cannot change a verdict).</item>
/// <item>Noise injection uses <c>GateCommand.Perturb</c> exactly, seed = s + StableSeed(symbol) %
/// 1000 as the gate command does. That function sets open = close, so on noisy bars every candle
/// body is zero and the body pivots become close pivots. Literal reading of "use the gate
/// command's Perturb"; reported.</item>
/// </list>
/// </para>
/// </summary>
public static class SetupGraderCommand
{
    private const int Draws = 10_000, PlaceboDraws = 200, ExitDraws = 1_000, Block = 20;
    private const double Alpha = 0.05;

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
    private const string SpyFile = "yahoo_SPY_1d.json", TltFile = "yahoo_TLT_1d.json";
    private const string FundingFile = "xs_binancevision_btcusdt_funding_8h.json";

    // ── Per-asset state ──────────────────────────────────────────────────────

    internal sealed class Ev
    {
        public required string Asset;
        public required int Bar, Era;
        public required SetupGrade G;
        public required GradeInputs In;
        public required double LStar, R, Rg;
        public double Ctl = double.NaN, CtlG = double.NaN;
        public bool AboveSma;
        public double Ex => R - Ctl;
        public double ExG => Rg - CtlG;
    }

    internal sealed class Stratum
    {
        public readonly List<int> Rows = new();
        public double Mean, MeanG;
        public Dictionary<int, int> Pos = new();
    }

    internal sealed class AssetRun
    {
        public required string Label, Cls;
        public required SeriesInputs S;
        public required double[] RL, RLg, RS, RSg;
        public required int[] Era, CgL, CgS;
        public required List<Ev> Long, Short, Red;
        public required Dictionary<string, int> ExclLong, ExclShort, ExclRed;
        public required Dictionary<(int, int, int), Stratum> StrataL, StrataS;
    }

    // ── Entry point ──────────────────────────────────────────────────────────

    public static async Task<int> RunAsync(string snapshotDir, bool countsOnly)
    {
        Console.WriteLine("SETUP GRADER — pre-registered in docs/SETUP_GRADER_PREREG.md (0597d7de), stage 2");
        Console.WriteLine(LabCipherProfile.Describe());
        if (!LabCipherProfile.IsDefault)
        {
            Console.WriteLine("The pre-registration fixes the current (NEW) Cipher B. Refusing to run another profile.");
            return 2;
        }
        Console.WriteLine($"Snapshots: {Path.GetFullPath(snapshotDir)} (read only)");
        var files = CryptoDaily.Concat(EquityNames.Select(e => $"yahoo_{e}_1d.json")).Concat(Crypto4h)
            .Concat(new[] { SpyFile, TltFile, FundingFile }).ToList();
        var missing = files.Where(f => !File.Exists(Path.Combine(snapshotDir, f))).ToList();
        foreach (var m in missing) Console.WriteLine($"  ! missing (not fetched): {m}");
        files = files.Except(missing).ToList();
        var before = Hashes(snapshotDir, files);
        Console.WriteLine($"Inputs hashed before the run: {before.Count} files");
        Console.WriteLine();

        var engine = LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
        SnapshotFile Load(string f) => SnapshotCommand.Load(Path.Combine(snapshotDir, f));

        var cryptoSnaps = CryptoDaily.Where(f => !missing.Contains(f)).Select(f => (Label: LabelOf(f), Snap: Load(f))).ToList();
        var equitySnaps = EquityNames.Select(e => $"yahoo_{e}_1d.json").Where(f => !missing.Contains(f))
            .Select(f => (Label: LabelOf(f), Snap: Load(f))).ToList();
        var c4Snaps = Crypto4h.Where(f => !missing.Contains(f)).Select(f => (Label: LabelOf(f), Snap: Load(f))).ToList();
        var btcDaily = cryptoSnaps.First(x => x.Label == "BTC").Snap.Bars;

        var btcPhase = SetupGraderCore.BuildPhaseSource(btcDaily, GraderSeriesConfig.DailyCrypto);
        var crypto = await BuildClass(engine, "crypto", cryptoSnaps.Select(x => (x.Label, x.Snap.Symbol, x.Snap.Bars)).ToList(),
            GraderSeriesConfig.DailyCrypto, btcPhase);
        var equities = await BuildClass(engine, "equities", equitySnaps.Select(x => (x.Label, x.Snap.Symbol, x.Snap.Bars)).ToList(),
            GraderSeriesConfig.DailyEquity, null);
        var c4 = await BuildClass(engine, "crypto4h", c4Snaps.Select(x => (x.Label, x.Snap.Symbol, x.Snap.Bars)).ToList(),
            GraderSeriesConfig.FourHourCrypto, btcPhase);

        PrintCounts("CRYPTO DAILY", crypto);
        PrintCounts("EQUITIES DAILY", equities);
        PrintCounts("CRYPTO 4H (S4)", c4);
        if (countsOnly)
        {
            Console.WriteLine("--counts-only: no outcome was summarised. Stopping here.");
            return CheckHashes(snapshotDir, files, before);
        }

        var tests = new List<TestRow>();
        var classes = new[] { crypto, equities };
        string[] clsNames = { "crypto", "equities" };

        // ── Reference rows and descriptive tables ───────────────────────────
        for (int c = 0; c < 2; c++) Describe(clsNames[c], classes[c]);

        // ── Primary family ──────────────────────────────────────────────────
        var primary = new List<TestRow>();
        for (int c = 0; c < 2; c++)
        {
            var cls = classes[c]; string cn = clsNames[c];
            primary.Add(BlockTest($"P1-{cn}", $"{cn}: ExA > 0", cls, true, e => e.G == SetupGrade.A, null));
            primary.Add(BlockTest($"P2-{cn}", $"{cn}: ExB > 0", cls, true, e => e.G == SetupGrade.B, null));
            primary.Add(BlockTest($"P3-{cn}", $"{cn}: ExC > 0", cls, true, e => e.G == SetupGrade.C, null));
            primary.Add(OrderTest($"P4-{cn}", $"{cn}: ExA - ExC > 0", cls, e => e.Long, e => e.G == SetupGrade.A, e => e.G == SetupGrade.C));
        }
        ApplyHolm(primary);
        PrintFamily("PRIMARY FAMILY (8 tests, Holm inside the family, development era, net R)", primary);

        // ── A∪B fallback ────────────────────────────────────────────────────
        var fallback = new List<TestRow>();
        for (int c = 0; c < 2; c++)
        {
            int nA = Dev(classes[c], r => r.Long).Count(e => e.G == SetupGrade.A);
            if (nA >= 30) continue;
            var cls = classes[c]; string cn = clsNames[c];
            fallback.Add(BlockTest($"P1AB-{cn}", $"{cn}: Ex(A∪B) > 0 [fallback, Suggestive whatever its p]", cls, true,
                e => e.G is SetupGrade.A or SetupGrade.B, null));
            fallback.Add(OrderTest($"P4AB-{cn}", $"{cn}: Ex(A∪B) - ExC > 0 [fallback]", cls, e => e.Long,
                e => e.G is SetupGrade.A or SetupGrade.B, e => e.G == SetupGrade.C));
        }
        if (fallback.Count > 0) PrintFamily("A∪B FALLBACK (section 7.4; reported as Suggestive whatever its p; not in the Holm family)", fallback);

        // ── Secondary family ────────────────────────────────────────────────
        var secondary = new List<TestRow>();
        var placeboRows = new List<TestRow>();
        for (int c = 0; c < 2; c++)
        {
            var cls = classes[c]; string cn = clsNames[c];
            secondary.Add(OrderTest($"S1-{cn}", $"{cn}: ExC - ExV > 0 (vetoed do worse than C)", cls, e => e.Long,
                e => e.G == SetupGrade.C, e => e.G == SetupGrade.V));
            var s2 = PlaceboTest($"S2-{cn}", cn, cls);
            secondary.Add(s2);
            secondary.Add(BlockTest($"S3a-{cn}", $"{cn}: mirror short ExA > 0", cls, false, e => e.G == SetupGrade.A, null));
            secondary.Add(OrderTest($"S3b-{cn}", $"{cn}: mirror short ExA - ExC > 0", cls, e => e.Short,
                e => e.G == SetupGrade.A, e => e.G == SetupGrade.C));
            secondary.Add(BlockTest($"S5-{cn}", $"{cn}: ExA vs bars matched on CG AND Location", cls, true,
                e => e.G == SetupGrade.A, LocationKey));
        }
        secondary.Add(BlockTest("S4a-crypto4h", "crypto 4h: ExA > 0", c4, true, e => e.G == SetupGrade.A, null));
        secondary.Add(OrderTest("S4b-crypto4h", "crypto 4h: ExA - ExC > 0", c4, e => e.Long,
            e => e.G == SetupGrade.A, e => e.G == SetupGrade.C));
        ApplyHolm(secondary);
        PrintFamily("SECONDARY FAMILY (12 tests, Holm inside the family)", secondary);

        // ── Macro family ────────────────────────────────────────────────────
        var spy = Load(SpyFile).Bars; var tlt = Load(TltFile).Bars;
        var funding = File.Exists(Path.Combine(snapshotDir, FundingFile))
            ? JsonConvert.DeserializeObject<CrossSeriesSnapshot>(File.ReadAllText(Path.Combine(snapshotDir, FundingFile)))!.Points
            : new List<TimedValue>();
        var macro = new List<TestRow>
        {
            MacroTest("M1-crypto", "crypto: Ex(SPY > SMA200) - Ex(not) > 0", crypto, MacroSpy(spy), higherIsState1: true),
            MacroTest("M1-equities", "equities: Ex(SPY > SMA200) - Ex(not) > 0", equities, MacroSpy(spy), higherIsState1: true),
            MacroTest("M2-crypto", "crypto: Ex(TLT 126d > 0) - Ex(not) > 0", crypto, MacroTlt(tlt), higherIsState1: true),
            MacroTest("M2-equities", "equities: Ex(TLT 126d > 0) - Ex(not) > 0", equities, MacroTlt(tlt), higherIsState1: true),
            MacroTest("M3-crypto", "crypto: Ex(not crowded) - Ex(crowded) > 0", crypto, MacroFunding(funding), higherIsState1: false),
        };
        ApplyHolm(macro);
        PrintFamily("MACRO MODIFIER FAMILY (5 tests, Holm inside the family; population A+B+C, development era)", macro);

        // ── Exit family ─────────────────────────────────────────────────────
        var exits = new List<TestRow>();
        for (int c = 0; c < 2; c++) exits.AddRange(ExitTests(clsNames[c], classes[c]));
        ApplyHolm(exits);
        PrintFamily("EXIT FAMILY (10 tests, Holm inside the family; A+B long entries, development era)", exits);

        // ── Robustness: noise ───────────────────────────────────────────────
        var retained = new double[2];
        for (int c = 0; c < 2; c++)
        {
            double clean = Ex(Dev(classes[c], r => r.Long).Where(e => e.G == SetupGrade.A));
            var noisy = new List<double>();
            for (int seed = 0; seed <= 2; seed++)
            {
                List<AssetRun> nr;
                if (c == 0)
                {
                    var pb = cryptoSnaps.Select(x => (x.Label, x.Snap.Symbol, Bars: GateCommand.Perturb(x.Snap.Bars, 0.25, seed + StableSeed.From(x.Snap.Symbol) % 1000))).ToList();
                    var nPhase = SetupGraderCore.BuildPhaseSource(pb.First(x => x.Label == "BTC").Bars, GraderSeriesConfig.DailyCrypto);
                    nr = await BuildClass(engine, "crypto", pb, GraderSeriesConfig.DailyCrypto, nPhase);
                }
                else
                {
                    var pb = equitySnaps.Select(x => (x.Label, x.Snap.Symbol, Bars: GateCommand.Perturb(x.Snap.Bars, 0.25, seed + StableSeed.From(x.Snap.Symbol) % 1000))).ToList();
                    nr = await BuildClass(engine, "equities", pb, GraderSeriesConfig.DailyEquity, null);
                }
                var a = Dev(nr, r => r.Long).Where(e => e.G == SetupGrade.A).ToList();
                double ex = Ex(a);
                noisy.Add(ex);
                Console.WriteLine($"  noise {clsNames[c]} seed {seed}: grade A n={a.Count}, ExA {ex:+0.000;-0.000}");
            }
            retained[c] = noisy.Average() / clean;
            Console.WriteLine($"  {clsNames[c]}: clean ExA {clean:+0.000;-0.000}, mean noisy {noisy.Average():+0.000;-0.000}, retained {100 * retained[c]:0}%");
        }
        Console.WriteLine();

        // ── Verdicts ────────────────────────────────────────────────────────
        Console.WriteLine("── VERDICT ──");
        for (int c = 0; c < 2; c++) Verdict(clsNames[c], classes[c], primary, retained[c]);

        int code = CheckHashes(snapshotDir, files, before);
        return code;
    }

    private static string LabelOf(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('_');
        return parts[1];
    }

    // ── Building a class ─────────────────────────────────────────────────────

    internal static async Task<List<AssetRun>> BuildClass(IIndicatorEngine engine, string cls,
        List<(string Label, string Symbol, List<Ohlcv> Bars)> series, GraderSeriesConfig cfg, PhaseSource? phase)
    {
        var runs = new List<AssetRun>();
        foreach (var (label, symbol, bars) in series)
        {
            var s = await SetupGraderCore.BuildAsync(engine, symbol, bars, cfg, phase);
            runs.Add(BuildRun(label, cls, s));
        }
        return runs;
    }

    internal static AssetRun BuildRun(string label, string cls, SeriesInputs s)
    {
        int n = s.Bars.Count;
        var rl = new double[n]; var rlg = new double[n]; var rs = new double[n]; var rsg = new double[n];
        var era = new int[n]; var cgl = new int[n]; var cgs = new int[n];
        for (int i = 0; i < n; i++)
        {
            era[i] = SetupGraderCore.EraOf(s.Bars[i].Date);
            cgl[i] = SetupGraderCore.CgAt(s, i, true);
            cgs[i] = SetupGraderCore.CgAt(s, i, false);
            double gl = SetupGraderCore.StandardR(s.Bars, i, s.Atr[i], true);
            double gs = SetupGraderCore.StandardR(s.Bars, i, s.Atr[i], false);
            double cost = double.IsNaN(gl) ? double.NaN : SetupGraderCore.CostR(s.Bars[i + 1].Open, s.Atr[i]);
            rlg[i] = gl; rl[i] = gl - cost; rsg[i] = gs; rs[i] = gs - cost;
        }
        var strataL = BuildStrata(era, cgl, rl, rlg, null);
        var strataS = BuildStrata(era, cgs, rs, rsg, null);
        var exL = new Dictionary<string, int>(); var exS = new Dictionary<string, int>(); var exR = new Dictionary<string, int>();
        var evL = Events(label, s, s.BuyOnset, true, rl, rlg, era, exL);
        var evS = Events(label, s, s.ShortTrig, false, rs, rsg, era, exS);
        var evR = Events(label, s, s.RedOnset, false, rs, rsg, era, exR);
        AssignControls(evL, strataL, cgl, null);
        AssignControls(evS, strataS, cgs, null);
        AssignControls(evR, strataS, cgs, null);
        return new AssetRun
        {
            Label = label, Cls = cls, S = s, RL = rl, RLg = rlg, RS = rs, RSg = rsg, Era = era, CgL = cgl, CgS = cgs,
            Long = evL, Short = evS, Red = evR, ExclLong = exL, ExclShort = exS, ExclRed = exR,
            StrataL = strataL, StrataS = strataS,
        };
    }

    private static List<Ev> Events(string label, SeriesInputs s, bool[] trig, bool isLong, double[] r, double[] rg, int[] era,
        Dictionary<string, int> excl)
    {
        var list = new List<Ev>();
        for (int i = 0; i < trig.Length; i++)
        {
            if (!trig[i]) continue;
            var why = SetupGraderCore.InputsAt(s, i, isLong, out var g, out var lStar);
            if (why == null && double.IsNaN(r[i])) why = "no outcome (fewer than 20 bars after, or ATR 0)";
            if (why != null) { excl[why] = excl.GetValueOrDefault(why) + 1; continue; }
            list.Add(new Ev
            {
                Asset = label, Bar = i, Era = era[i], G = SetupGraderCore.GradeOf(g), In = g, LStar = lStar,
                R = r[i], Rg = rg[i],
                AboveSma = !double.IsNaN(s.Sma200[i]) && s.Bars[i].Close > s.Sma200[i],
            });
        }
        return list;
    }

    /// <summary>
    /// The matched random control (section 6.4): every bar on the asset with the same era, the
    /// same context grade and (optionally) the same extra key, whose context and outcome are
    /// defined. Rows stay in time order for the block permutation.
    /// </summary>
    internal static Dictionary<(int, int, int), Stratum> BuildStrata(int[] era, int[] cg, double[] r, double[] rg, Func<int, int>? extra)
    {
        var d = new Dictionary<(int, int, int), Stratum>();
        for (int i = 0; i < r.Length; i++)
        {
            if (cg[i] == SetupGraderCore.Undef || double.IsNaN(r[i])) continue;
            int x = extra?.Invoke(i) ?? 0;
            if (x == SetupGraderCore.Undef) continue;
            var key = (era[i], cg[i], x);
            if (!d.TryGetValue(key, out var st)) d[key] = st = new Stratum();
            st.Pos[i] = st.Rows.Count;
            st.Rows.Add(i);
        }
        foreach (var st in d.Values)
        {
            st.Mean = st.Rows.Average(i => r[i]);
            st.MeanG = st.Rows.Average(i => rg[i]);
        }
        return d;
    }

    internal static void AssignControls(List<Ev> evs, Dictionary<(int, int, int), Stratum> strata, int[] cg, Func<int, int>? extra)
    {
        foreach (var e in evs)
        {
            var key = (e.Era, cg[e.Bar], extra?.Invoke(e.Bar) ?? 0);
            if (strata.TryGetValue(key, out var st)) { e.Ctl = st.Mean; e.CtlG = st.MeanG; }
        }
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    internal sealed class TestRow
    {
        public required string Id, What;
        public int N, N2;
        public double Stat = double.NaN, P = double.NaN, PHolm = double.NaN;
        public string Note = "";
        public bool Sig => !double.IsNaN(PHolm) && PHolm <= Alpha;
    }

    private static double Ex(IEnumerable<Ev> evs)
    {
        var l = evs.ToList();
        return l.Count == 0 ? double.NaN : l.Average(e => e.Ex);
    }
    private static double ExG(IEnumerable<Ev> evs)
    {
        var l = evs.ToList();
        return l.Count == 0 ? double.NaN : l.Average(e => e.ExG);
    }
    private static double MeanR(IEnumerable<Ev> evs)
    {
        var l = evs.ToList();
        return l.Count == 0 ? double.NaN : l.Average(e => e.R);
    }

    private static IEnumerable<Ev> Dev(List<AssetRun> runs, Func<AssetRun, List<Ev>> side) =>
        runs.SelectMany(side).Where(e => e.Era == 0);
    private static IEnumerable<Ev> Held(List<AssetRun> runs, Func<AssetRun, List<Ev>> side) =>
        runs.SelectMany(side).Where(e => e.Era == 1);

    private static int LocationKey(AssetRun a, int i)
    {
        var loc = SetupGraderCore.LocationAt(a.S, i, true);
        return loc == null ? SetupGraderCore.Undef : (loc.Value.Loc ? 1 : 0);
    }

    /// <summary>
    /// Ex &gt; 0 by block permutation (section 6.5): within each asset x era x CG stratum (dev era),
    /// the stratum's bars are cut into consecutive 20-bar blocks whose order is permuted, the trigger
    /// labels travelling with their blocks; the pooled mean excess is recomputed. One-sided.
    /// </summary>
    private static TestRow BlockTest(string id, string what, List<AssetRun> cls, bool isLong, Func<Ev, bool> label,
        Func<AssetRun, int, int>? extraKey)
    {
        var strata = new List<(string Asset, double[] Y, int[] Lab)>();
        int total = 0; double obsSum = 0;
        foreach (var a in cls)
        {
            var evs = (isLong ? a.Long : a.Short).Where(e => e.Era == 0 && label(e)).ToList();
            Dictionary<(int, int, int), Stratum> st;
            double[] r = isLong ? a.RL : a.RS;
            int[] cg = isLong ? a.CgL : a.CgS;
            if (extraKey == null) st = isLong ? a.StrataL : a.StrataS;
            else st = BuildStrata(a.Era, cg, r, isLong ? a.RLg : a.RSg, i => extraKey(a, i));
            foreach (var (key, s) in st.Where(kv => kv.Key.Item1 == 0).OrderBy(kv => kv.Key))
            {
                var lab = evs.Where(e => cg[e.Bar] == key.Item2 && (extraKey?.Invoke(a, e.Bar) ?? 0) == key.Item3)
                    .Select(e => s.Pos[e.Bar]).ToArray();
                if (lab.Length == 0) continue;
                var y = s.Rows.Select(i => r[i] - s.Mean).ToArray();
                strata.Add((a.Label, y, lab));
                total += lab.Length;
                obsSum += lab.Sum(p => y[p]);
            }
        }
        var row = new TestRow { Id = id, What = what, N = total };
        if (total == 0) { row.Note = "no triggers"; return row; }
        row.Stat = obsSum / total;
        var nullSum = new double[Draws];
        foreach (var grp in strata.GroupBy(x => x.Asset).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rng = new Random(StableSeed.From($"setup-grader|{id}|{grp.Key}"));
            foreach (var (_, y, lab) in grp) BlockNull(y, lab, rng, nullSum);
        }
        int ge = nullSum.Count(v => v / total >= row.Stat - 1e-12);
        row.P = (1.0 + ge) / (1.0 + Draws);
        return row;
    }

    /// <summary>Adds, for each draw, the sum of y at the labelled positions after a block shuffle.</summary>
    internal static void BlockNull(double[] y, int[] lab, Random rng, double[] nullSum)
    {
        int len = y.Length;
        int nBlocks = (len + Block - 1) / Block;
        int tail = nBlocks - 1, tailLen = len - tail * Block;
        var offsets = new List<int>[nBlocks];
        foreach (int p in lab) (offsets[p / Block] ??= new List<int>()).Add(p % Block);
        var order = new int[nBlocks];
        var slotOf = new int[nBlocks];
        for (int d = 0; d < nullSum.Length; d++)
        {
            for (int i = 0; i < nBlocks; i++) order[i] = i;
            for (int i = nBlocks - 1; i > 0; i--) { int j = rng.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            int tailSlot = 0;
            for (int sIdx = 0; sIdx < nBlocks; sIdx++) { slotOf[order[sIdx]] = sIdx; if (order[sIdx] == tail) tailSlot = sIdx; }
            double sum = 0;
            for (int b = 0; b < nBlocks; b++)
            {
                var off = offsets[b];
                if (off == null) continue;
                int slot = slotOf[b];
                int start = slot * Block - (slot > tailSlot ? Block - tailLen : 0);
                foreach (int o in off) sum += y[start + o];
            }
            nullSum[d] += sum;
        }
    }

    /// <summary>
    /// Ordering (section 6.5): permute the two labels among that pair's triggers within each asset
    /// (dev era), each trigger's excess held fixed. Statistic: mean excess of the first group minus
    /// the second, pooled over the class. One-sided.
    /// </summary>
    private static TestRow OrderTest(string id, string what, List<AssetRun> cls, Func<AssetRun, List<Ev>> side,
        Func<Ev, bool> first, Func<Ev, bool> second)
    {
        var groups = cls.Select(a => (a.Label,
                F: side(a).Where(e => e.Era == 0 && first(e)).Select(e => e.Ex).ToArray(),
                S: side(a).Where(e => e.Era == 0 && second(e)).Select(e => e.Ex).ToArray()))
            .ToList();
        return LabelPerm(id, what, groups);
    }

    internal static TestRow LabelPerm(string id, string what, List<(string Asset, double[] F, double[] S)> groups)
    {
        int nf = groups.Sum(g => g.F.Length), ns = groups.Sum(g => g.S.Length);
        var row = new TestRow { Id = id, What = what, N = nf, N2 = ns };
        if (nf == 0 || ns == 0) { row.Note = "a group is empty"; return row; }
        double total = groups.Sum(g => g.F.Sum() + g.S.Sum());
        double sf = groups.Sum(g => g.F.Sum());
        row.Stat = sf / nf - (total - sf) / ns;
        var nullF = new double[Draws];
        foreach (var g in groups.OrderBy(g => g.Asset, StringComparer.Ordinal))
        {
            if (g.F.Length + g.S.Length == 0) continue;
            var pool = g.F.Concat(g.S).ToArray();
            int k = g.F.Length;
            var rng = new Random(StableSeed.From($"setup-grader|{id}|{g.Asset}"));
            for (int d = 0; d < Draws; d++)
            {
                double s = 0;
                for (int i = 0; i < k; i++)
                {
                    int j = i + rng.Next(pool.Length - i);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                    s += pool[i];
                }
                nullF[d] += s;
            }
        }
        int ge = 0;
        for (int d = 0; d < Draws; d++)
            if (nullF[d] / nf - (total - nullF[d]) / ns >= row.Stat - 1e-12) ge++;
        row.P = (1.0 + ge) / (1.0 + Draws);
        return row;
    }

    private static void ApplyHolm(List<TestRow> fam)
    {
        var adj = SetupGraderCore.Holm(fam.Select(t => t.P).ToList());
        for (int k = 0; k < fam.Count; k++) fam[k].PHolm = adj[k];
    }

    private static void PrintFamily(string title, List<TestRow> fam)
    {
        Console.WriteLine($"══════ {title} ══════");
        Console.WriteLine($"{"id",-14} {"n",5} {"n2",5} {"stat",8} {"p raw",8} {"p Holm",8}  sig  test");
        foreach (var t in fam)
            Console.WriteLine($"{t.Id,-14} {t.N,5} {(t.N2 > 0 ? t.N2.ToString() : ""),5} {t.Stat,8:+0.000;-0.000} {t.P,8:0.0000} {t.PHolm,8:0.0000}  {(t.Sig ? "YES" : "no "),-3}  {t.What}{(t.Note.Length > 0 ? "  [" + t.Note + "]" : "")}");
        Console.WriteLine();
    }

    // ── S2 placebo ───────────────────────────────────────────────────────────

    private static TestRow PlaceboTest(string id, string cn, List<AssetRun> cls)
    {
        var real = Dev(cls, r => r.Long).Where(e => e.G == SetupGrade.A).ToList();
        var row = new TestRow { Id = id, What = $"{cn}: real-level ExA beats 200 placebo-level draws", N = real.Count };
        if (real.Count == 0) { row.Note = "no grade A"; return row; }
        row.Stat = Ex(real);
        var rngs = cls.ToDictionary(a => a.Label, a => new Random(StableSeed.From($"setup-grader|{id}|{a.Label}")));
        var placebo = new double[PlaceboDraws];
        var nA = new int[PlaceboDraws];
        for (int d = 0; d < PlaceboDraws; d++)
        {
            double sum = 0; int cnt = 0;
            foreach (var a in cls)
            {
                var s = a.S; var rng = rngs[a.Label];
                double Off(int lowerIdx)
                {
                    double atr = s.Atr[Math.Min(lowerIdx, s.Atr.Length - 1)];
                    double mag = 3 + 3 * rng.NextDouble();
                    double sign = rng.Next(2) == 0 ? -1 : 1;
                    return double.IsNaN(atr) ? double.NaN : sign * mag * atr;
                }
                var pv = s.Trend.Pivots.Select(q => Off(s.Htf.LastLowerIndex[q.ConfirmBucket])).ToArray();
                var em = Enumerable.Range(0, s.Htf.Bars.Count).Select(w => Off(s.Htf.LastLowerIndex[w])).ToArray();
                var shift = new LevelShift(pv, em);
                foreach (var e in a.Long.Where(e => e.Era == 0))
                {
                    if (SetupGraderCore.InputsAt(s, e.Bar, true, out var g, out _, shift) != null) continue;
                    if (SetupGraderCore.GradeOf(g) != SetupGrade.A) continue;
                    sum += e.Ex; cnt++;
                }
            }
            placebo[d] = cnt == 0 ? double.NegativeInfinity : sum / cnt;
            nA[d] = cnt;
        }
        int ge = placebo.Count(v => v >= row.Stat - 1e-12);
        row.P = (1.0 + ge) / (1.0 + PlaceboDraws);
        var finite = placebo.Where(double.IsFinite).OrderBy(v => v).ToArray();
        row.Note = $"real beats {PlaceboDraws - ge} of {PlaceboDraws} draws (needs > 190); placebo ExA median " +
                   $"{(finite.Length > 0 ? finite[finite.Length / 2] : double.NaN):+0.000;-0.000}, 95th pct " +
                   $"{(finite.Length > 0 ? finite[(int)(0.95 * (finite.Length - 1))] : double.NaN):+0.000;-0.000}, placebo grade-A n median {nA.OrderBy(x => x).ElementAt(PlaceboDraws / 2)}";
        return row;
    }

    // ── Macro ────────────────────────────────────────────────────────────────

    /// <summary>A macro state per asset bar: 1, 0, or Undef.</summary>
    private delegate int[] MacroState(AssetRun a);

    private static int LastOnOrBefore(List<Ohlcv> bars, DateTime day)
    {
        int lo = 0, hi = bars.Count - 1, ans = -1;
        while (lo <= hi) { int mid = (lo + hi) / 2; if (bars[mid].Date.Date <= day) { ans = mid; lo = mid + 1; } else hi = mid - 1; }
        return ans;
    }

    private static MacroState MacroSpy(List<Ohlcv> spy)
    {
        var sma = IndicatorMath.Sma(spy.Select(b => b.Close).ToArray(), 200);
        return a => a.S.Bars.Select(b =>
        {
            int j = LastOnOrBefore(spy, b.Date.Date);
            if (j < 0 || double.IsNaN(sma[j])) return SetupGraderCore.Undef;
            return spy[j].Close > sma[j] ? 1 : 0;
        }).ToArray();
    }

    private static MacroState MacroTlt(List<Ohlcv> tlt) => a => a.S.Bars.Select(b =>
    {
        int j = LastOnOrBefore(tlt, b.Date.Date);
        if (j < 126) return SetupGraderCore.Undef;
        return tlt[j].Close / tlt[j - 126].Close - 1 > 0 ? 1 : 0;
    }).ToArray();

    /// <summary>M3: mean of the last 21 BTC 8-hour funding prints above the 80th percentile
    /// (linear interpolation) of the trailing 1,095 prints, as of the bar's date.</summary>
    private static MacroState MacroFunding(List<TimedValue> pts)
    {
        var p = pts.OrderBy(x => x.Ts).ToList();
        var state = new int[p.Count];
        for (int j = 0; j < p.Count; j++)
        {
            if (j + 1 < 1095) { state[j] = SetupGraderCore.Undef; continue; }
            double m21 = 0; for (int k = j - 20; k <= j; k++) m21 += p[k].Value; m21 /= 21;
            var w = new double[1095]; for (int k = 0; k < 1095; k++) w[k] = p[j - 1094 + k].Value;
            Array.Sort(w);
            double h = (w.Length - 1) * 0.80; int lo = (int)Math.Floor(h);
            double q = w[lo] + (h - lo) * (w[Math.Min(lo + 1, w.Length - 1)] - w[lo]);
            state[j] = m21 > q ? 1 : 0;
        }
        var ts = p.Select(x => x.Ts).ToArray();
        return a => a.S.Bars.Select(b =>
        {
            // "on or before that date": prints stamped before the next UTC midnight.
            long cut = new DateTimeOffset(DateTime.SpecifyKind(b.Date.Date.AddDays(1), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            int idx = Array.BinarySearch(ts, cut - 1);
            if (idx < 0) idx = ~idx - 1;
            return idx < 0 ? SetupGraderCore.Undef : state[idx];
        }).ToArray();
    }

    private static TestRow MacroTest(string id, string what, List<AssetRun> cls, MacroState macro, bool higherIsState1)
    {
        var groups = new List<(string, double[], double[])>();
        int excluded = 0;
        foreach (var a in cls)
        {
            var st = macro(a);
            var strata = BuildStrata(a.Era, a.CgL, a.RL, a.RLg, i => st[i]);
            var f = new List<double>(); var s = new List<double>();
            foreach (var e in a.Long.Where(e => e.Era == 0 && e.G != SetupGrade.V))
            {
                int m = st[e.Bar];
                if (m == SetupGraderCore.Undef || !strata.TryGetValue((e.Era, a.CgL[e.Bar], m), out var stt)) { excluded++; continue; }
                double ex = e.R - stt.Mean;
                bool inFirst = higherIsState1 ? m == 1 : m == 0;
                (inFirst ? f : s).Add(ex);
            }
            groups.Add((a.Label, f.ToArray(), s.ToArray()));
        }
        var row = LabelPerm(id, what, groups);
        row.Note = (row.Note.Length > 0 ? row.Note + "; " : "") + $"{excluded} triggers with the macro state undefined excluded";
        return row;
    }

    // ── Exits ────────────────────────────────────────────────────────────────

    private static IEnumerable<TestRow> ExitTests(string cn, List<AssetRun> cls)
    {
        var entries = cls.SelectMany(a => a.Long.Where(e => e.Era == 0 && e.G is SetupGrade.A or SetupGrade.B).Select(e => (a, e))).ToList();
        var sell = cls.ToDictionary(a => a.Label, a => SetupGraderCore.SellSetupForExit(a.S));
        Console.WriteLine($"══════ EXITS — {cn}: {entries.Count} A+B long entries (development era) ══════");
        Console.WriteLine($"{"variant",-8} {"n",5} {"mean R",8} {"A only",8} {"ATR u",8} {"hold",6} {"rand R",8} {"rand ATR",9} {"ratio",7} {"p",7} {"open",5}");
        var rows = new List<TestRow>();
        foreach (var v in new[] { "X1", "X2", "X3", "X4", "X5" })
        {
            double StopOf(AssetRun a, Ev e) => v == "X1"
                ? a.S.Bars[e.Bar + 1].Open - 3 * a.S.Atr[e.Bar]
                : SetupGraderCore.InvalidationStop(a.S, e.Bar, e.In.Loc, e.LStar);
            ExitResult Run((AssetRun a, Ev e) x) => v switch
            {
                "X1" or "X2" => SetupGraderCore.Ladder(x.a.S, x.e.Bar, StopOf(x.a, x.e)),
                "X3" => SetupGraderCore.StopAndRule(x.a.S, x.e.Bar, StopOf(x.a, x.e), x.a.S.CrossBear, x.e.Bar + SetupGraderCore.ExitCap),
                "X4" => SetupGraderCore.StopAndRule(x.a.S, x.e.Bar, StopOf(x.a, x.e), sell[x.a.Label], x.e.Bar + SetupGraderCore.ExitCap),
                _ => SetupGraderCore.StopAndRule(x.a.S, x.e.Bar, StopOf(x.a, x.e), null, x.e.Bar + SetupGraderCore.Horizon),
            };
            var res = entries.Select(Run).ToList();
            var row = new TestRow { Id = $"{v}-{cn}", What = $"{cn}: {v} mean R beats its random-exit control", N = res.Count };
            if (res.Count == 0) { row.Note = "no entries"; rows.Add(row); continue; }
            double mean = res.Average(r => r.RNet);
            double aOnly = entries.Zip(res).Where(z => z.First.e.G == SetupGrade.A).Select(z => z.Second.RNet).DefaultIfEmpty(double.NaN).Average();
            var holds = res.Select(r => r.Hold).ToArray();
            var rng = new Random(StableSeed.From($"setup-grader|{row.Id}|{cn}"));
            int ge = 0; double randSum = 0, randAtr = 0;
            for (int d = 0; d < ExitDraws; d++)
            {
                double s = 0, sa = 0;
                foreach (var x in entries)
                {
                    int h = holds[rng.Next(holds.Length)];
                    var rr = SetupGraderCore.StopAndRule(x.a.S, x.e.Bar, StopOf(x.a, x.e), null, x.e.Bar + 1 + h);
                    s += rr.RNet; sa += rr.AtrNet;
                }
                s /= entries.Count; sa /= entries.Count;
                randSum += s; randAtr += sa;
                if (s >= mean) ge++;
            }
            double randMean = randSum / ExitDraws;
            row.Stat = mean;
            row.P = ge / (double)ExitDraws;
            row.Note = $"random-exit mean {randMean:+0.000;-0.000}R, ratio {mean / randMean:0.00}, ATR units {res.Average(r => r.AtrNet):+0.000;-0.000} vs {randAtr / ExitDraws:+0.000;-0.000}";
            Console.WriteLine($"{v,-8} {res.Count,5} {mean,8:+0.000;-0.000} {aOnly,8:+0.000;-0.000} {res.Average(r => r.AtrNet),8:+0.000;-0.000} " +
                              $"{holds.Average(),6:0.0} {randMean,8:+0.000;-0.000} {randAtr / ExitDraws,9:+0.000;-0.000} {mean / randMean,7:0.00} {row.P,7:0.000} {res.Count(r => !r.Complete),5}");
            rows.Add(row);
        }
        Console.WriteLine("(mean R is net of 15 bps a side, per position from the initial risk; X1's R is on a 3 ATR stop, so compare X1 with X2-X5 in ATR units)");
        Console.WriteLine();
        return rows;
    }

    // ── Descriptive tables ───────────────────────────────────────────────────

    private static void PrintCounts(string title, List<AssetRun> cls)
    {
        Console.WriteLine($"══════ COUNTS — {title} ══════");
        Console.WriteLine($"{"asset",-6} {"bars",6} {"first",10} {"weeks",5}  long dev A/B/C/V    long held A/B/C/V   short dev A/B/C/V  excluded(long)");
        foreach (var a in cls)
        {
            string G(List<Ev> l, int era) => string.Join("/", new[] { SetupGrade.A, SetupGrade.B, SetupGrade.C, SetupGrade.V }
                .Select(g => l.Count(e => e.Era == era && e.G == g)));
            Console.WriteLine($"{a.Label,-6} {a.S.Bars.Count,6} {a.S.Bars[0].Date,10:yyyy-MM-dd} {a.S.Htf.Bars.Count,5}  {G(a.Long, 0),-18} {G(a.Long, 1),-18}  {G(a.Short, 0),-18} {a.ExclLong.Values.Sum()}");
        }
        foreach (int era in new[] { 0, 1 })
        {
            var l = cls.SelectMany(a => a.Long).Where(e => e.Era == era).ToList();
            var s = cls.SelectMany(a => a.Short).Where(e => e.Era == era).ToList();
            var r = cls.SelectMany(a => a.Red).Where(e => e.Era == era).ToList();
            string C(List<Ev> x) => string.Join("  ", new[] { SetupGrade.A, SetupGrade.B, SetupGrade.C, SetupGrade.V }.Select(g => $"{g}={x.Count(e => e.G == g)}"));
            Console.WriteLine($"  {(era == 0 ? "DEV (<2023)" : "HELD-OUT (2023+)")}: long {C(l)} | mirror short {C(s)} | shipped red dot {C(r)}");
            var v = l.Where(e => e.G == SetupGrade.V).ToList();
            Console.WriteLine($"    long vetoes fired (not exclusive): V1 {v.Count(e => e.In.V1)}, V2 {v.Count(e => e.In.V2)}, V3 {v.Count(e => e.In.V3)}");
        }
        foreach (var (name, pick) in new (string, Func<AssetRun, Dictionary<string, int>>)[] { ("long", a => a.ExclLong), ("mirror short", a => a.ExclShort), ("red dot", a => a.ExclRed) })
        {
            var agg = new Dictionary<string, int>();
            foreach (var a in cls) foreach (var (k, c) in pick(a)) agg[k] = agg.GetValueOrDefault(k) + c;
            Console.WriteLine($"  excluded {name}: " + (agg.Count == 0 ? "none" : string.Join("; ", agg.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))));
        }
        Console.WriteLine();
    }

    private static void Describe(string cn, List<AssetRun> cls)
    {
        Console.WriteLine($"══════ DESCRIPTIVE — {cn} (net R; gross beside it; Ex = excess over the CG-matched random entry) ══════");
        Console.WriteLine($"{"row",-30} {"era",-5} {"n",5} {"meanR",8} {"Ex",8} {"meanRg",8} {"ExG",8} {"ctl",8}");
        void Line(string name, string era, List<Ev> l) =>
            Console.WriteLine($"{name,-30} {era,-5} {l.Count,5} {MeanR(l),8:+0.000;-0.000} {Ex(l),8:+0.000;-0.000} " +
                              $"{(l.Count == 0 ? double.NaN : l.Average(e => e.Rg)),8:+0.000;-0.000} {ExG(l),8:+0.000;-0.000} " +
                              $"{(l.Count == 0 ? double.NaN : l.Average(e => e.Ctl)),8:+0.000;-0.000}");
        foreach (var (eraName, era) in new[] { ("dev", 0), ("held", 1) })
        {
            var l = cls.SelectMany(a => a.Long).Where(e => e.Era == era).ToList();
            foreach (var g in new[] { SetupGrade.A, SetupGrade.B, SetupGrade.C, SetupGrade.V })
                Line($"long grade {g}", eraName, l.Where(e => e.G == g).ToList());
            Line("R1 all Buy dots (ungraded)", eraName, l);
            Line("R0 Buy dot + close > SMA200", eraName, l.Where(e => e.AboveSma).ToList());
            var s = cls.SelectMany(a => a.Short).Where(e => e.Era == era).ToList();
            foreach (var g in new[] { SetupGrade.A, SetupGrade.B, SetupGrade.C, SetupGrade.V })
                Line($"mirror short grade {g}", eraName, s.Where(e => e.G == g).ToList());
            var r = cls.SelectMany(a => a.Red).Where(e => e.Era == era).ToList();
            foreach (var g in new[] { SetupGrade.A, SetupGrade.C })
                Line($"shipped red dot grade {g} (alt)", eraName, r.Where(e => e.G == g).ToList());
        }
        Console.WriteLine("  per-asset ExA, development era, assets with >= 3 grade A triggers (breadth list):");
        foreach (var a in cls)
        {
            var l = a.Long.Where(e => e.Era == 0 && e.G == SetupGrade.A).ToList();
            if (l.Count >= 3) Console.WriteLine($"    {a.Label,-6} n={l.Count,3} ExA {Ex(l):+0.000;-0.000}");
        }
        Console.WriteLine();
    }

    private static void Verdict(string cn, List<AssetRun> cls, List<TestRow> primary, double retained)
    {
        var dev = Dev(cls, r => r.Long).ToList();
        var held = Held(cls, r => r.Long).ToList();
        var A = dev.Where(e => e.G == SetupGrade.A).ToList();
        var B = dev.Where(e => e.G == SetupGrade.B).ToList();
        var C = dev.Where(e => e.G == SetupGrade.C).ToList();
        var hA = held.Where(e => e.G == SetupGrade.A).ToList();
        var hC = held.Where(e => e.G == SetupGrade.C).ToList();
        var p1 = primary.First(t => t.Id == $"P1-{cn}");
        var p4 = primary.First(t => t.Id == $"P4-{cn}");
        bool c1 = p1.Sig, c2 = p4.Sig;
        bool c3 = MeanR(A) > MeanR(B) && MeanR(B) > MeanR(C) && Ex(A) > Ex(B) && Ex(B) > Ex(C);
        bool heldCheckable = hA.Count >= 10 && hC.Count >= 10;
        bool c4 = heldCheckable && Ex(hA) > 0 && Ex(hA) - Ex(hC) > 0;
        var breadth = cls.Select(a => a.Long.Where(e => e.Era == 0 && e.G == SetupGrade.A).ToList()).Where(l => l.Count >= 3).ToList();
        int pos = breadth.Count(l => Ex(l) > 0);
        bool c5 = breadth.Count > 0 && pos * 2 > breadth.Count;
        bool c6 = !double.IsNaN(retained) && retained >= 0.5;
        bool conf = A.Count >= 30 && C.Count >= 30;
        string countState = A.Count >= 30 ? "confirmatory" : A.Count >= 15 ? "Suggestive only (15-29)" : "not testable (<15)";

        string label;
        if (c1 && c2 && c3 && c4 && c5 && c6 && conf && heldCheckable) label = "Supported";
        else if (c1 && c2 && c3 && c4 && c5 && !c6 && conf && heldCheckable) label = "Fragile";
        else if (c3 && A.Count >= 15 && p1.P < 0.05 && p4.P < 0.05 && !(heldCheckable && !c4)) label = "Suggestive";
        else label = "No edge";
        bool sec8 = !c1 || !c2 || !c3 || (heldCheckable && !(Ex(hA) > 0 && Ex(hA) - Ex(hC) > 0));

        Console.WriteLine($"  {cn}: grade A dev n={A.Count} ({countState}), B {B.Count}, C {C.Count}; held-out A {hA.Count}, C {hC.Count}");
        Console.WriteLine($"    1 P1 Holm-significant: {c1} (p {p1.P:0.0000}, Holm {p1.PHolm:0.0000}, ExA {p1.Stat:+0.000;-0.000})");
        Console.WriteLine($"    2 P4 Holm-significant: {c2} (p {p4.P:0.0000}, Holm {p4.PHolm:0.0000}, ExA-ExC {p4.Stat:+0.000;-0.000})");
        Console.WriteLine($"    3 A > B > C in mean net R and Ex: {c3} (R {MeanR(A):+0.000;-0.000} / {MeanR(B):+0.000;-0.000} / {MeanR(C):+0.000;-0.000}; Ex {Ex(A):+0.000;-0.000} / {Ex(B):+0.000;-0.000} / {Ex(C):+0.000;-0.000})");
        Console.WriteLine($"    4 held-out ExA > 0 and ExA-ExC > 0: {(heldCheckable ? c4.ToString() : "not checkable")} (ExA {Ex(hA):+0.000;-0.000}, ExA-ExC {Ex(hA) - Ex(hC):+0.000;-0.000})");
        Console.WriteLine($"    5 ExA > 0 on more than half the breadth list: {c5} ({pos} of {breadth.Count})");
        Console.WriteLine($"    6 retained >= 50% at 25% noise: {c6} ({100 * retained:0}%)");
        Console.WriteLine($"    section 7.1 label: {label}. Section 8 no-edge condition met: {sec8}.");
        if (sec8)
            Console.WriteLine("    => the hierarchy adds nothing to the bare Buy dot that this test can detect, and the Buy dot inside good context is no better than a random bar in the same context.");
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
