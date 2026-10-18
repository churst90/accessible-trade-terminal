using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.StrategyLab;

/// <summary>Why a bar is or is not at Cipher SR support (pre-registration section 2.1 and 4.6).</summary>
internal static class LocWhy
{
    public const int At = 0;
    /// <summary>No support was live at the previous bar's close (no qualifying pivot low yet): undefined.</summary>
    public const int NoLevel = 1;
    /// <summary>The bar's low stayed more than 0.5 ATR above the level.</summary>
    public const int TooFar = 2;
    /// <summary>The bar closed at or below the level.</summary>
    public const int ClosedBelow = 3;
    /// <summary>ATR(14) undefined at the bar: undefined.</summary>
    public const int NoAtr = 4;

    public static string Name(int w) => w switch
    {
        At => "at support", NoLevel => "no level yet", TooFar => "low more than 0.5 ATR above the level",
        ClosedBelow => "closed at or below the level", NoAtr => "ATR undefined", _ => "?",
    };
}

/// <summary>
/// Cipher SR's support (and resistance) on one series, and the round-3 location read from it, per bar.
/// Every array is indexed by the entry-timeframe bar.
/// </summary>
internal sealed class Mc3Sr
{
    /// <summary>The engine's <c>Support Zone</c>: the level live at each bar's CLOSE.</summary>
    public required double[] Sup;
    /// <summary><c>Support Zone_replaced</c>: the old level on the bar a newer one takes over, NaN elsewhere.</summary>
    public required double[] SupReplaced;
    /// <summary><c>Support Zone_since</c>: Unix seconds of the bar the level became live.</summary>
    public required double[] SupSince;
    /// <summary>The engine's <c>Resistance Zone</c>, live at each bar's close.</summary>
    public required double[] Res;
    /// <summary>Per bar t: the bar at which the level in <c>Sup[t]</c> became live; -1 where none.</summary>
    public required int[] LiveBar;
    /// <summary>S*(t) = <c>Sup[t-1]</c>: the support the chart showed before bar t opened (section 2.1).</summary>
    public required double[] SStar;
    /// <summary>The bar S*(t) became live (<c>LiveBar[t-1]</c>); its pivot bar is the bar before. -1 where none.</summary>
    public required int[] SStarLive;
    /// <summary>R*(t) = <c>Res[t-1]</c>, the resistance live when bar t opened (section 4.5, XR).</summary>
    public required double[] RStar;
    /// <summary>L(t): 1 at support, 0 not, <see cref="SetupGraderMc2Core.Undef"/> where S*(t) or ATR(t) is undefined.</summary>
    public required int[] L;
    public required int[] Why;
    /// <summary>Our Cipher SR's own "test" of S*(t): low at or below it, close above it, no band (section 4.6, counts only).</summary>
    public required bool[] Tested;
}

/// <summary>
/// The pure parts of the round-3 Market Cipher setup grader (<c>docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md</c>,
/// committed 97def0cc before any result): Cipher SR read causally on the entry bars, the location rule,
/// the level's age and pivot bar, the placebo levels, the XR exit, and the 20,000-draw tests. Round 2's
/// definitions (setups, contexts, triggers, the R harness, the controls, the noise) are reused from
/// <see cref="SetupGraderMc2Core"/> and <see cref="SetupGraderMc2Command"/> unchanged.
///
/// <para>
/// THE TRAP THIS IS BUILT AROUND. A bar can confirm a new support: bar t-1 was a pivot low and bar t
/// did not undercut it. The new level is known at t's close, so it is in <c>Support Zone[t]</c>, but
/// bar t's own low cannot be a test of it. So S*(t) is always <c>Support Zone[t-1]</c>, the level live
/// when bar t opened; on a replacement bar that is the OLD level, which the indicator also hands back
/// as <c>Support Zone_replaced[t]</c> (asserted on every bar by <see cref="FromArrays"/>).
/// </para>
/// </summary>
internal static class SetupGraderMc3Core
{
    public const int Undef = SetupGraderMc2Core.Undef;
    /// <summary>The touch band, frozen (section 2.2): low within 0.5 ATR(14) above the level.</summary>
    public const double Band = 0.5;
    /// <summary>Placebo offset range in ATR (section 4.4, round 1's).</summary>
    public const double PlaceboLo = 3.0, PlaceboHi = 6.0;

    /// <summary>Section 2.1's rule for one bar against one level.</summary>
    public static int Location(Ohlcv bar, double level, double atr, out int why)
    {
        if (double.IsNaN(level)) { why = LocWhy.NoLevel; return Undef; }
        if (double.IsNaN(atr) || atr <= 0) { why = LocWhy.NoAtr; return Undef; }
        if (bar.Close <= level) { why = LocWhy.ClosedBelow; return 0; }
        if (bar.Low > level + Band * atr) { why = LocWhy.TooFar; return 0; }
        why = LocWhy.At;
        return 1;
    }

    public static async Task<Mc3Sr> BuildAsync(IIndicatorEngine engine, string symbol, IReadOnlyList<Ohlcv> bars, double[] atr, string tf)
    {
        int n = bars.Count;
        var p = new Dictionary<string, object> { ["__symbol"] = symbol, ["__timeframe"] = tf };
        var sr = await engine.CalculateAsync("CIPHER_SR", bars, p, default);
        double[] Get(string c) => sr.TryGetValue(c, out var a) && a.Length == n
            ? a : throw new InvalidOperationException($"CIPHER_SR returned no '{c}' array of length {n} for {symbol}");
        return FromArrays(bars, atr, Get(CipherSrProvider.CompSupportLine), Get(CipherSrProvider.CompSupportReplaced),
            Get(CipherSrProvider.CompSupportSince), Get(CipherSrProvider.CompResistanceLine));
    }

    public static double UnixSeconds(DateTime t)
    {
        var utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
        return (utc - DateTime.UnixEpoch).TotalSeconds;
    }

    /// <summary>
    /// Builds the per-bar location from the indicator's arrays. Throws if the replaced-level rule
    /// (section 2.3: S*(t) equals <c>Support Zone_replaced</c> wherever that is defined) or the
    /// live-bar bookkeeping (the bar a level became live carries that level's <c>_since</c> time)
    /// fails anywhere: either would mean the location reads a level other than the one the chart showed.
    /// </summary>
    public static Mc3Sr FromArrays(IReadOnlyList<Ohlcv> bars, double[] atr, double[] sup, double[] supRep, double[] supSince, double[] res)
    {
        int n = bars.Count;
        var live = new int[n];
        for (int t = 0; t < n; t++)
        {
            if (double.IsNaN(sup[t])) { live[t] = -1; continue; }
            bool fresh = t == 0 || double.IsNaN(sup[t - 1]) || supSince[t] != supSince[t - 1];
            live[t] = fresh ? t : live[t - 1];
            if (fresh && Math.Abs(UnixSeconds(bars[t].Date) - supSince[t]) > 0.5)
                throw new InvalidOperationException($"bar {t}: a support became live but its _since ({supSince[t]}) is not this bar's time");
        }
        var sStar = new double[n]; var sLive = new int[n]; var rStar = new double[n];
        var l = new int[n]; var why = new int[n]; var tested = new bool[n];
        for (int t = 0; t < n; t++)
        {
            sStar[t] = t >= 1 ? sup[t - 1] : double.NaN;
            sLive[t] = t >= 1 ? live[t - 1] : -1;
            rStar[t] = t >= 1 ? res[t - 1] : double.NaN;
            if (!double.IsNaN(supRep[t]) && supRep[t] != sStar[t])
                throw new InvalidOperationException($"bar {t}: replaced-level rule failed (S* {sStar[t]}, Support Zone_replaced {supRep[t]})");
            l[t] = Location(bars[t], sStar[t], atr[t], out why[t]);
            tested[t] = !double.IsNaN(sStar[t]) && bars[t].Low <= sStar[t] && bars[t].Close > sStar[t];
        }
        return new Mc3Sr
        {
            Sup = sup, SupReplaced = supRep, SupSince = supSince, Res = res, LiveBar = live,
            SStar = sStar, SStarLive = sLive, RStar = rStar, L = l, Why = why, Tested = tested,
        };
    }

    /// <summary>The level's age at bar t: bars from the bar S*(t) became live to t. -1 where undefined.</summary>
    public static int Age(Mc3Sr sr, int t) => sr.SStarLive[t] < 0 ? -1 : t - sr.SStarLive[t];

    /// <summary>The pivot bar of S*(t): the bar before it became live (section 2.3). -1 where undefined.</summary>
    public static int PivotBar(Mc3Sr sr, int t) => sr.SStarLive[t] < 0 ? -1 : sr.SStarLive[t] - 1;

    /// <summary>Section 4.3: the level is OLDER when its pivot bar is before b(i), the bear cross where the
    /// setup's anchor wave began.</summary>
    public static bool Older(Mc3Sr sr, int t, int anchorStart) => PivotBar(sr, t) < anchorStart;

    // ── Section 4.4: placebo levels ─────────────────────────────────────────

    /// <summary>
    /// One placebo draw: every support level, for its whole life, is shifted by u x ATR(c), c the bar
    /// it became live, u uniform on [3, 6), the sign random. Returns the offset of the level live at
    /// each bar's close (NaN where there is none, or ATR(c) is undefined). Draws are taken in the
    /// order the levels become live, u then the sign.
    /// </summary>
    public static double[] PlaceboOffsets(Mc3Sr sr, double[] atr, Random rng)
    {
        int n = sr.Sup.Length;
        var off = new double[n];
        double cur = double.NaN;
        for (int t = 0; t < n; t++)
        {
            if (sr.LiveBar[t] < 0) { off[t] = double.NaN; continue; }
            if (sr.LiveBar[t] == t)
            {
                double u = PlaceboLo + (PlaceboHi - PlaceboLo) * rng.NextDouble();
                double sign = rng.Next(2) == 0 ? -1.0 : 1.0;
                double a = atr[t];
                cur = double.IsNaN(a) || a <= 0 ? double.NaN : sign * u * a;
            }
            off[t] = cur;
        }
        return off;
    }

    /// <summary>L(t) rebuilt with section 2.1's rule against the shifted levels: S*(t) + the offset of
    /// the level live at t-1's close.</summary>
    public static int[] PlaceboL(Mc3Sr sr, IReadOnlyList<Ohlcv> bars, double[] atr, double[] off)
    {
        int n = bars.Count;
        var l = new int[n];
        for (int t = 0; t < n; t++)
        {
            double lev = t >= 1 ? sr.SStar[t] + off[t - 1] : double.NaN;
            l[t] = Location(bars[t], lev, atr[t], out _);
        }
        return l;
    }

    // ── Section 4.5: XR, Cipher SR's own exit ───────────────────────────────

    /// <summary>
    /// XR: from the entry bar (s + 1) on, the stop is checked first (a bar that opens through it fills
    /// at its open); then the exit fires on the first bar t where R*(t), the resistance live at t-1's
    /// close, is defined, above the entry price, and reached by High(t). It fills at R*(t), or at
    /// Open(t) if the bar opens at or above it. Cap: the close of bar <paramref name="capBar"/>. R is
    /// per position from the initial risk, net of 15 bps a side, as round 2's exits book it.
    /// </summary>
    public static ExitResult ResistanceExit(IReadOnlyList<Ohlcv> b, double[] atrs, double[] rStar, int s, double stop, int capBar)
    {
        int e = s + 1, n = b.Count;
        double entry = b[e].Open, risk = entry - stop, atr = atrs[s];
        int last = Math.Min(capBar, n - 1);
        for (int k = e; k <= last; k++)
        {
            if (b[k].Low <= stop) return Book(Math.Min(b[k].Open, stop) - entry, entry, risk, atr, k - e, true);
            double r = rStar[k];
            if (!double.IsNaN(r) && r > entry && b[k].High >= r) return Book(Math.Max(b[k].Open, r) - entry, entry, risk, atr, k - e, true);
            if (k == capBar) return Book(b[k].Close - entry, entry, risk, atr, k - e, true);
        }
        return Book(b[last].Close - entry, entry, risk, atr, last - e, false);
    }

    /// <summary>Round 1's booking (SetupGraderCore's private Book), repeated: net and gross R from the
    /// initial risk, and net ATR units.</summary>
    public static ExitResult Book(double pnl, double entry, double risk, double atr, int hold, bool complete) =>
        new(pnl / risk - SetupGraderCore.CostR(entry, risk), pnl / risk, pnl / atr - SetupGraderCore.CostR(entry, atr), hold, complete);

    // ── Statistics (20,000 draws, section 7.2) ──────────────────────────────

    /// <summary>
    /// Group difference by label permutation within each asset (round 2's GO method; round 1's
    /// <c>LabelPerm</c> arithmetic, with the draw count as a parameter so round 1 and 2 are untouched).
    /// Statistic: pooled mean of the first group minus pooled mean of the second. One-sided.
    /// </summary>
    public static (double Stat, double P) LabelPerm(string seedKey, List<(string Asset, double[] F, double[] S)> groups, int draws)
    {
        int nf = groups.Sum(g => g.F.Length), ns = groups.Sum(g => g.S.Length);
        if (nf == 0 || ns == 0) return (double.NaN, double.NaN);
        double total = groups.Sum(g => g.F.Sum() + g.S.Sum());
        double sf = groups.Sum(g => g.F.Sum());
        double stat = sf / nf - (total - sf) / ns;
        var nullF = new double[draws];
        foreach (var g in groups.OrderBy(g => g.Asset, StringComparer.Ordinal))
        {
            if (g.F.Length + g.S.Length == 0) continue;
            var pool = g.F.Concat(g.S).ToArray();
            int k = g.F.Length;
            var rng = new Random(StableSeed.From($"{seedKey}|{g.Asset}"));
            for (int d = 0; d < draws; d++)
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
        for (int d = 0; d < draws; d++)
        {
            double v = nullF[d] / nf - (total - nullF[d]) / ns;
            if (v >= stat - 1e-12) ge++;
        }
        return (stat, (1.0 + ge) / (1.0 + draws));
    }

    /// <summary>
    /// Round 2's block permutation: inside each stratum (rows in time order, y = value minus the
    /// stratum mean) the rows are cut into 20-row blocks whose order is permuted, the labels travelling
    /// with their blocks (<see cref="SetupGraderCommand.BlockNull"/>, unchanged). One-sided p of the
    /// pooled mean of y at the labels.
    /// </summary>
    public static double BlockP(string seedKey, List<(string Asset, double[] Y, int[] Lab)> strata, double stat, int draws)
    {
        int total = strata.Sum(s => s.Lab.Length);
        var nullSum = new double[draws];
        foreach (var grp in strata.GroupBy(x => x.Asset).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rng = new Random(StableSeed.From($"{seedKey}|{grp.Key}"));
            foreach (var (_, y, lab) in grp) SetupGraderCommand.BlockNull(y, lab, rng, nullSum);
        }
        int ge = nullSum.Count(v => v / total >= stat - 1e-12);
        return (1.0 + ge) / (1.0 + draws);
    }

    /// <summary>Nearest-rank 95th percentile (the value at rank ceil(0.95 n) of the sorted draws).</summary>
    public static double Percentile95(IReadOnlyList<double> x)
    {
        var s = x.OrderBy(v => v).ToList();
        int rank = (int)Math.Ceiling(0.95 * s.Count);
        return s[Math.Clamp(rank - 1, 0, s.Count - 1)];
    }
}
