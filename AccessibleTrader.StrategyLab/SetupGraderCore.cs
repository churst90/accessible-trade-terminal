using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.StrategyLab;

/// <summary>The grade a Cipher B dot gets under <c>docs/SETUP_GRADER_PREREG.md</c> section 3.6.</summary>
internal enum SetupGrade { A, B, C, V }

/// <summary>
/// A higher timeframe built from the trigger bars themselves, holding only COMPLETED buckets.
///
/// <para>
/// WHY THIS EXISTS. The app's <c>HtfLastClosedIndexExclusive</c> treats a weekly bar as closed
/// once it has OPENED, so a Wednesday daily bar sees that week's final values (pre-registration
/// section 2.2). That path is not used anywhere in the setup grader. Here a bucket is usable on a
/// lower bar only when (1) its last lower bar is that bar or an earlier one and (2) for a 24/7
/// market, its calendar span has ended by that bar's close. The final bucket of a series is
/// dropped unless the data shows it is finished (compare <c>WeeklyPersistenceCommand.ToWeekly</c>,
/// which always drops it): otherwise a grade at the series' last bar would differ from the same
/// grade computed with later bars present.
/// </para>
/// </summary>
internal sealed class HigherTf
{
    public required List<Ohlcv> Bars;
    public required DateTime[] BucketStart;
    /// <summary>The instant the bucket is complete.</summary>
    public required DateTime[] End;
    public required int[] LastLowerIndex;
    /// <summary>Per LOWER bar: the index of the newest higher bar usable at that bar's close, or -1.</summary>
    public required int[] UsableAt;
}

/// <summary>A confirmed body pivot on the higher timeframe.</summary>
internal readonly record struct BodyPivot(double Level, bool IsHigh, int Bucket, int ConfirmBucket);

/// <summary>The (a1) trend detector's output: a state per higher bar and every pivot it confirmed.</summary>
internal sealed class BodyTrendResult
{
    public required int[] State;           // +1 up, -1 down, 0 no trend, Undef before 2 highs and 2 lows
    public required List<BodyPivot> Pivots; // in confirmation order
}

/// <summary>How a series is cut into its context timeframe.</summary>
internal sealed record GraderSeriesConfig(string Tf, string HtfTf, TimeSpan LowerTf, Func<DateTime, DateTime> BucketOf,
    TimeSpan BucketLen, bool Calendar, int LevelLookback)
{
    public static GraderSeriesConfig DailyCrypto { get; } =
        new("1d", "1w", TimeSpan.FromDays(1), SetupGraderCore.WeekStart, TimeSpan.FromDays(7), true, 156);
    public static GraderSeriesConfig DailyEquity { get; } =
        new("1d", "1w", TimeSpan.FromDays(1), SetupGraderCore.WeekStart, TimeSpan.FromDays(7), false, 156);
    /// <summary>S4: 4h trigger, daily context built from the 4h bars, 156 daily bars of levels.</summary>
    public static GraderSeriesConfig FourHourCrypto { get; } =
        new("4h", "1d", TimeSpan.FromHours(4), d => d.Date, TimeSpan.FromDays(1), true, 156);
}

/// <summary>The cycle phase source: Bitcoin's weekly bars for crypto (section 3.2 a2).</summary>
internal sealed record PhaseSource(HigherTf Htf, int[] Phase);

/// <summary>Placebo shifts for the S2 location control: one offset per pivot and per bucket's EMA level.</summary>
internal sealed record LevelShift(double[] Pivot, double[] Ema);

/// <summary>Everything a bar needs to be graded, computed once per series.</summary>
internal sealed class SeriesInputs
{
    public required string Symbol;
    public required GraderSeriesConfig Cfg;
    public required List<Ohlcv> Bars;
    public required double[] Atr, Sma200, Mf, Wt2;
    public required bool[] BuyOnset, ShortTrig, RedOnset, CrossBear, BullMark, BearMark;
    public required HigherTf Htf;
    public required BodyTrendResult Trend;
    public required double[] HtfEma10, HtfMf;
    public required int[] PhaseAt;   // per lower bar, Undef where unknown
}

/// <summary>The layer values for one bar on one side.</summary>
internal readonly record struct GradeInputs(int Cg, bool Loc, bool D1, bool D2, bool D3, bool V1, bool V2, bool V3)
{
    public int Confirmations => (D1 ? 1 : 0) + (D2 ? 1 : 0) + (D3 ? 1 : 0);
}

internal readonly record struct ExitResult(double RNet, double RGross, double AtrNet, int Hold, bool Complete);

/// <summary>
/// The pure parts of the pre-registered setup grader (<c>docs/SETUP_GRADER_PREREG.md</c>): completed
/// higher-timeframe bars, the body-pivot trend, the cycle phase, location, confirmations, vetoes,
/// grades, the standard R harness and the five exit variants. Everything here reads bars up to and
/// including the bar being graded and nothing after it; <c>SetupGraderTests</c> holds that to
/// account by truncating the series and requiring identical grades.
/// </summary>
internal static class SetupGraderCore
{
    public const int Undef = int.MinValue;
    public const int Good = 1, Neutral = 0, Bad = -1;
    public const int Bull = 1, Warning = 0, Bear = -1;
    public const int Horizon = 20;
    public const double CostPerSide = 0.0015;
    public const int PivotSpan = 2;
    public const int TouchWindow = 5;        // bars i-4 .. i
    public const double TouchBand = 1.0;     // x ATR
    public const int DivWindow = 6;          // bars i-5 .. i
    public const double StopBuffer = 0.25, StopFloor = 0.5;
    public const int ExitCap = 60;
    public const double ShortTriggerLevel = 60.0;
    public static readonly DateTime EraCut = new(2023, 1, 1);

    public static DateTime WeekStart(DateTime d)
    {
        var x = d.Date;
        int back = ((int)x.DayOfWeek + 6) % 7;   // Monday = 0
        return x.AddDays(-back);
    }

    public static int EraOf(DateTime d) => d < EraCut ? 0 : 1;

    // ── Higher timeframe ─────────────────────────────────────────────────────

    public static HigherTf Aggregate(IReadOnlyList<Ohlcv> lower, Func<DateTime, DateTime> bucketOf, TimeSpan bucketLen,
        TimeSpan lowerTf, bool calendar)
    {
        var bars = new List<Ohlcv>();
        var starts = new List<DateTime>();
        var ends = new List<DateTime>();
        var lastIdx = new List<int>();
        int first = 0;
        for (int k = 1; k <= lower.Count; k++)
        {
            bool boundary = k == lower.Count || bucketOf(lower[k].Date) != bucketOf(lower[first].Date);
            if (!boundary) continue;
            // The final bucket is kept only when it is known to be finished: for a 24/7 market,
            // when its calendar span has ended by its last bar's close; for an exchange week,
            // when its last bar is a Friday. (A week cut short by a holiday Friday is known to be
            // over from the exchange calendar, which the data cannot show at the series' end, so
            // such a final week is dropped. Inside the series the next bar shows it.)
            if (k == lower.Count)
            {
                var st = bucketOf(lower[first].Date);
                var lastClose = lower[k - 1].Date + lowerTf;
                bool finished = calendar ? st + bucketLen <= lastClose : lower[k - 1].Date.DayOfWeek == DayOfWeek.Friday;
                if (!finished) break;
            }
            double hi = double.MinValue, lo = double.MaxValue, vol = 0;
            for (int j = first; j < k; j++) { hi = Math.Max(hi, lower[j].High); lo = Math.Min(lo, lower[j].Low); vol += lower[j].Volume; }
            var start = bucketOf(lower[first].Date);
            bars.Add(new Ohlcv(start, lower[first].Open, hi, lo, lower[k - 1].Close, vol));
            starts.Add(start);
            var barEnd = lower[k - 1].Date + lowerTf;
            ends.Add(calendar ? (start + bucketLen > barEnd ? start + bucketLen : barEnd) : barEnd);
            lastIdx.Add(k - 1);
            first = k;
        }
        var usable = new int[lower.Count];
        int b = -1;
        for (int k = 0; k < lower.Count; k++)
        {
            var close = lower[k].Date + lowerTf;
            while (b + 1 < bars.Count && lastIdx[b + 1] <= k && ends[b + 1] <= close) b++;
            usable[k] = b;
        }
        return new HigherTf
        {
            Bars = bars, BucketStart = starts.ToArray(), End = ends.ToArray(),
            LastLowerIndex = lastIdx.ToArray(), UsableAt = usable,
        };
    }

    /// <summary>Maps another series' higher bars onto these bars by time: the newest one complete
    /// by each bar's close. Used for Bitcoin's phase on every crypto asset.</summary>
    public static int[] MapByTime(HigherTf h, IReadOnlyList<Ohlcv> target, TimeSpan targetTf)
    {
        var map = new int[target.Count];
        int b = -1;
        for (int k = 0; k < target.Count; k++)
        {
            var close = target[k].Date + targetTf;
            while (b + 1 < h.Bars.Count && h.End[b + 1] <= close) b++;
            // Targets are in time order, so b only moves forward.
            map[k] = b;
        }
        return map;
    }

    // ── (a1) trend from candle bodies ────────────────────────────────────────

    public static double BodyTop(Ohlcv b) => Math.Max(b.Open, b.Close);
    public static double BodyBottom(Ohlcv b) => Math.Min(b.Open, b.Close);

    public static BodyTrendResult BodyTrend(IReadOnlyList<Ohlcv> w, int span = PivotSpan)
    {
        int n = w.Count;
        var state = new int[n];
        var pivots = new List<BodyPivot>();
        double? lastHigh = null, lastLow = null;
        bool hh = false, hl = false;
        int highs = 0, lows = 0;
        for (int c = 0; c < n; c++)
        {
            // 1. Pivot confirmations: week j = c - span is confirmed at the close of week c.
            int j = c - span;
            if (j >= span)
            {
                double top = BodyTop(w[j]), bot = BodyBottom(w[j]);
                bool isHigh = true, isLow = true;
                for (int d = 1; d <= span; d++)
                {
                    if (!(top > BodyTop(w[j - d]) && top > BodyTop(w[j + d]))) isHigh = false;
                    if (!(bot < BodyBottom(w[j - d]) && bot < BodyBottom(w[j + d]))) isLow = false;
                }
                if (isHigh)
                {
                    if (lastHigh.HasValue) hh = top > lastHigh.Value;
                    lastHigh = top; highs++;
                    pivots.Add(new BodyPivot(top, true, j, c));
                }
                if (isLow)
                {
                    if (lastLow.HasValue) hl = bot > lastLow.Value;
                    lastLow = bot; lows++;
                    pivots.Add(new BodyPivot(bot, false, j, c));
                }
            }
            // 2. Close tests.
            if (lastHigh.HasValue && w[c].Close > lastHigh.Value) hh = true;
            if (lastLow.HasValue && w[c].Close < lastLow.Value) hl = false;

            state[c] = highs >= 2 && lows >= 2 ? (hh && hl ? 1 : (!hh && !hl ? -1 : 0)) : Undef;
        }
        return new BodyTrendResult { State = state, Pivots = pivots };
    }

    // ── (a2) cycle phase ─────────────────────────────────────────────────────

    public static int[] Phase(IReadOnlyList<Ohlcv> w)
    {
        var close = w.Select(x => x.Close).ToArray();
        var e10 = IndicatorMath.Ema(close, 10);
        var e21 = IndicatorMath.Ema(close, 21);
        var s10 = IndicatorMath.Sma(close, 10);
        var ph = new int[w.Count];
        for (int i = 0; i < w.Count; i++)
        {
            if (double.IsNaN(e10[i]) || double.IsNaN(e21[i]) || double.IsNaN(s10[i])) { ph[i] = Undef; continue; }
            ph[i] = e10[i] > e21[i] ? (s10[i] <= e10[i] ? Bull : Warning) : Bear;
        }
        return ph;
    }

    public static PhaseSource BuildPhaseSource(IReadOnlyList<Ohlcv> btcLower, GraderSeriesConfig btcCfg)
    {
        var h = Aggregate(btcLower, btcCfg.BucketOf, btcCfg.BucketLen, btcCfg.LowerTf, btcCfg.Calendar);
        return new PhaseSource(h, Phase(h.Bars));
    }

    /// <summary>Context grade for a long (or, mirrored, a short).</summary>
    public static int ContextGrade(int trend, int phase, bool isLong)
    {
        if (trend == Undef || phase == Undef) return Undef;
        if (isLong)
        {
            if (trend == 1 && phase == Bull) return Good;
            if (trend == -1 && phase == Bear) return Bad;
            return Neutral;
        }
        if (trend == -1 && phase == Bear) return Good;
        if (trend == 1 && phase == Bull) return Bad;
        return Neutral;
    }

    // ── Building a series ────────────────────────────────────────────────────

    public static async Task<SeriesInputs> BuildAsync(IIndicatorEngine engine, string symbol, List<Ohlcv> bars,
        GraderSeriesConfig cfg, PhaseSource? phaseSource)
    {
        int n = bars.Count;
        var p = new Dictionary<string, object> { ["__symbol"] = symbol, ["__timeframe"] = cfg.Tf };
        var cb = await engine.CalculateAsync("CIPHER_B", bars, p, default);
        double[] Arr(Dictionary<string, double[]> d, string c)
        {
            if (d.TryGetValue(c, out var a)) return a;
            var nan = new double[d.Values.FirstOrDefault()?.Length ?? 0]; Array.Fill(nan, double.NaN); return nan;
        }
        bool[] Mark(double[] a) => a.Select(v => !double.IsNaN(v)).ToArray();
        bool[] Onset(double[] a)
        {
            var f = new bool[a.Length];
            for (int i = 1; i < a.Length; i++) f[i] = !double.IsNaN(a[i]) && double.IsNaN(a[i - 1]);
            return f;
        }
        var wt2 = Arr(cb, CipherBProvider.CompWT2);
        var crossBear = Mark(Arr(cb, CipherBProvider.CompCrossBear));
        var shortTrig = new bool[n];
        for (int i = 0; i < n; i++) shortTrig[i] = crossBear[i] && !double.IsNaN(wt2[i]) && wt2[i] >= ShortTriggerLevel;

        var htf = Aggregate(bars, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);
        var trend = BodyTrend(htf.Bars);
        var hClose = htf.Bars.Select(x => x.Close).ToArray();
        var ema10 = IndicatorMath.Ema(hClose, 10);
        double[] hMf;
        if (htf.Bars.Count >= 10)
        {
            var hp = new Dictionary<string, object> { ["__symbol"] = symbol, ["__timeframe"] = cfg.HtfTf };
            var hcb = await engine.CalculateAsync("CIPHER_B", htf.Bars, hp, default);
            hMf = hcb.TryGetValue(CipherBProvider.CompMoneyFlowWave, out var m) ? m : Nan(htf.Bars.Count);
        }
        else hMf = Nan(htf.Bars.Count);

        var phaseAt = new int[n];
        if (phaseSource == null)
        {
            var ph = Phase(htf.Bars);
            for (int i = 0; i < n; i++) phaseAt[i] = htf.UsableAt[i] >= 0 ? ph[htf.UsableAt[i]] : Undef;
        }
        else
        {
            var map = MapByTime(phaseSource.Htf, bars, cfg.LowerTf);
            for (int i = 0; i < n; i++) phaseAt[i] = map[i] >= 0 ? phaseSource.Phase[map[i]] : Undef;
        }

        var close = bars.Select(x => x.Close).ToArray();
        return new SeriesInputs
        {
            Symbol = symbol, Cfg = cfg, Bars = bars,
            Atr = IndicatorMath.Atr(bars.ToArray(), 14),
            Sma200 = IndicatorMath.Sma(close, 200),
            Mf = Arr(cb, CipherBProvider.CompMoneyFlowWave),
            Wt2 = wt2,
            BuyOnset = Onset(Arr(cb, CipherBProvider.CompBlue)),
            ShortTrig = shortTrig,
            // The red dot (Overbought Crossover) and the regular and hidden divergences were
            // removed from Cipher B on 2026-10-31; the graders that read them refuse to run.
            RedOnset = new bool[n],
            CrossBear = crossBear,
            BullMark = new bool[n],
            BearMark = new bool[n],
            Htf = htf, Trend = trend, HtfEma10 = ema10, HtfMf = hMf, PhaseAt = phaseAt,
        };
    }

    private static double[] Nan(int n) { var a = new double[n]; Array.Fill(a, double.NaN); return a; }

    // ── Layers at one bar ────────────────────────────────────────────────────

    /// <summary>Context grade at bar i, Undef where the weekly trend or the phase is unknown.</summary>
    public static int CgAt(SeriesInputs s, int i, bool isLong)
    {
        int w = s.Htf.UsableAt[i];
        if (w < 0) return Undef;
        return ContextGrade(s.Trend.State[w], s.PhaseAt[i], isLong);
    }

    /// <summary>
    /// Location (section 3.3): a candidate level within 1 ATR of the lowest low of bars i-4..i that
    /// bar i closes at or above (long), mirrored for a short. Returns null where undefined (no
    /// usable higher bar, ATR or EMA(10) unknown, fewer than 5 bars). lStar is the qualifying level
    /// nearest the extreme, NaN when none qualifies.
    /// </summary>
    public static (bool Loc, double LStar)? LocationAt(SeriesInputs s, int i, bool isLong, LevelShift? shift = null)
    {
        int w = s.Htf.UsableAt[i];
        if (w < 0 || i < TouchWindow - 1) return null;
        double atr = s.Atr[i];
        double ema = s.HtfEma10[w];
        if (double.IsNaN(atr) || atr <= 0 || double.IsNaN(ema)) return null;
        if (shift != null) ema += shift.Ema[w];
        double ext = isLong ? double.MaxValue : double.MinValue;
        for (int k = i - TouchWindow + 1; k <= i; k++)
            ext = isLong ? Math.Min(ext, s.Bars[k].Low) : Math.Max(ext, s.Bars[k].High);
        double close = s.Bars[i].Close;
        bool any = false; double best = double.NaN, bestD = double.MaxValue;
        void Try(double l)
        {
            double d = Math.Abs(ext - l);
            if (d > TouchBand * atr) return;
            if (isLong ? close < l : close > l) return;
            any = true;
            if (d < bestD) { bestD = d; best = l; }
        }
        Try(ema);
        int lo = w - s.Cfg.LevelLookback + 1;
        var pv = s.Trend.Pivots;
        for (int k = 0; k < pv.Count; k++)
        {
            var q = pv[k];
            if (q.ConfirmBucket > w) break;              // pivots are in confirmation order
            if (q.ConfirmBucket < lo) continue;
            Try(shift != null ? q.Level + shift.Pivot[k] : q.Level);
        }
        return (any, best);
    }

    /// <summary>
    /// All layers for bar i. Returns the reason the bar is excluded, or null with the inputs filled.
    /// Undefined is never read as false (section 3).
    /// </summary>
    public static string? InputsAt(SeriesInputs s, int i, bool isLong, out GradeInputs g, out double lStar, LevelShift? shift = null)
    {
        g = default; lStar = double.NaN;
        int w = s.Htf.UsableAt[i];
        int cg = CgAt(s, i, isLong);
        if (cg == Undef) return "context undefined (weekly trend or phase)";
        if (w < 1 || double.IsNaN(s.HtfMf[w]) || double.IsNaN(s.HtfMf[w - 1])) return "higher-timeframe money flow undefined";
        if (i < 2 || double.IsNaN(s.Mf[i]) || double.IsNaN(s.Mf[i - 1]) || double.IsNaN(s.Mf[i - 2])) return "daily money flow undefined";
        var loc = LocationAt(s, i, isLong, shift);
        if (loc == null) return "location undefined (ATR or EMA10)";
        lStar = loc.Value.LStar;

        double m0 = s.Mf[i], m1 = s.Mf[i - 1], m2 = s.Mf[i - 2];
        double w0 = s.HtfMf[w], w1 = s.HtfMf[w - 1];
        bool d1 = false;
        var marks = isLong ? s.BullMark : s.BearMark;
        for (int k = Math.Max(0, i - DivWindow + 1); k <= i; k++) if (marks[k]) { d1 = true; break; }
        bool d2 = isLong ? m0 > m1 && m1 > m2 : m0 < m1 && m1 < m2;
        bool d3 = isLong ? w0 > w1 : w0 < w1;
        bool v1 = cg == Bad;
        bool v2 = isLong ? m0 < 0 && m0 < m1 : m0 > 0 && m0 > m1;
        bool v3 = isLong ? w0 < 0 && w0 < w1 : w0 > 0 && w0 > w1;
        g = new GradeInputs(cg, loc.Value.Loc, d1, d2, d3, v1, v2, v3);
        return null;
    }

    /// <summary>Section 3.6. A hierarchy, not a sum: A needs good context first.</summary>
    public static SetupGrade GradeOf(GradeInputs g)
    {
        if (g.V1 || g.V2 || g.V3) return SetupGrade.V;
        bool conf2 = g.Confirmations >= 2;
        if (g.Cg == Good && g.Loc && conf2) return SetupGrade.A;
        if ((g.Cg == Good && (g.Loc ^ conf2)) || (g.Cg == Neutral && g.Loc && conf2)) return SetupGrade.B;
        return SetupGrade.C;
    }

    /// <summary>Section 4's "sell setup for exit": the mirror trigger plus resistance location or at
    /// least 2 bearish confirmations; context and vetoes ignored. Undefined parts cannot fire it.</summary>
    public static bool[] SellSetupForExit(SeriesInputs s)
    {
        int n = s.Bars.Count;
        var f = new bool[n];
        for (int k = 0; k < n; k++)
        {
            if (!s.ShortTrig[k]) continue;
            var loc = LocationAt(s, k, false);
            int conf = 0;
            for (int j = Math.Max(0, k - DivWindow + 1); j <= k; j++) if (s.BearMark[j]) { conf++; break; }
            if (k >= 2 && s.Mf[k] < s.Mf[k - 1] && s.Mf[k - 1] < s.Mf[k - 2]) conf++;
            int w = s.Htf.UsableAt[k];
            if (w >= 1 && s.HtfMf[w] < s.HtfMf[w - 1]) conf++;   // NaN compares false
            f[k] = (loc?.Loc ?? false) || conf >= 2;
        }
        return f;
    }

    // ── Outcomes ─────────────────────────────────────────────────────────────

    /// <summary>The lab's standard R harness (section 2.2): next open, 1 ATR stop, 2R target, 20
    /// bars, the stop wins a bar that touches both. Gross R; NaN where it cannot be computed.</summary>
    public static double StandardR(IReadOnlyList<Ohlcv> bars, int i, double atr, bool isLong)
    {
        if (i + Horizon >= bars.Count || double.IsNaN(atr) || atr <= 0) return double.NaN;
        double entry = bars[i + 1].Open;
        if (entry <= 0) return double.NaN;
        double risk = atr;
        double stop = isLong ? entry - risk : entry + risk;
        double target = isLong ? entry + 2 * risk : entry - 2 * risk;
        for (int k = i + 1; k <= i + Horizon; k++)
        {
            var b = bars[k];
            if (isLong ? b.Low <= stop : b.High >= stop) return -1.0;
            if (isLong ? b.High >= target : b.Low <= target) return 2.0;
        }
        double exit = bars[i + Horizon].Close;
        return (isLong ? exit - entry : entry - exit) / risk;
    }

    /// <summary>Round-trip cost in R: 2 x 15 bps of the entry, over the risk.</summary>
    public static double CostR(double entry, double risk) => 2 * CostPerSide * entry / risk;

    /// <summary>S_inv (section 5): the lowest low of bars i-4..i, or l* if lower and Location is
    /// yes, less 0.25 ATR; floored at 0.5 ATR below the entry.</summary>
    public static double InvalidationStop(SeriesInputs s, int i, bool loc, double lStar)
    {
        double low = double.MaxValue;
        for (int k = Math.Max(0, i - TouchWindow + 1); k <= i; k++) low = Math.Min(low, s.Bars[k].Low);
        if (loc && !double.IsNaN(lStar) && lStar < low) low = lStar;
        double atr = s.Atr[i];
        double stop = low - StopBuffer * atr;
        double entry = s.Bars[i + 1].Open;
        if (entry - stop < StopFloor * atr) stop = entry - StopFloor * atr;
        return stop;
    }

    private static ExitResult Book(double pnl, double entry, double risk, double atr, int hold, bool complete) =>
        new(pnl / risk - CostR(entry, risk), pnl / risk, pnl / atr - CostR(entry, atr), hold, complete);

    /// <summary>
    /// X1 and X2: 50% off at 2R, 50% at 4R; after the 2R exit the stop moves to the entry and then
    /// trails at 3 x ATR(14) exactly as the backtest engine's TrailByAtr does
    /// (<see cref="ManagedExitRules.AtrTrailStop"/>). R is PER POSITION from the INITIAL risk:
    /// sum over exits of portion x (exit - entry) / (entry - initial stop). The engine's per-row R,
    /// which divides by the stop in force at each exit, is not used (section 2.2).
    /// </summary>
    public static ExitResult Ladder(SeriesInputs s, int i, double stop0) => Ladder(s.Bars, s.Atr, i, stop0);

    /// <summary>The same ladder on any bar series and its ATR(14). Round 2's harness
    /// (<c>SetupGraderMc2Command</c>) calls this, so both rounds book exits identically.</summary>
    public static ExitResult Ladder(IReadOnlyList<Ohlcv> b, double[] atrs, int i, double stop0)
    {
        int e = i + 1, n = b.Count;
        double entry = b[e].Open, risk = entry - stop0, atr = atrs[i];
        double t1 = entry + 2 * risk, t2 = entry + 4 * risk;
        double stop = stop0, rem = 1.0, pnl = 0;
        bool tp1 = false;
        for (int k = e; k < n; k++)
        {
            if (b[k].Low <= stop)
            {
                // A bar that opens through the stop fills at its open, as the engine books it.
                pnl += rem * (Math.Min(b[k].Open, stop) - entry);
                return Book(pnl, entry, risk, atr, k - e, true);
            }
            if (!tp1 && b[k].High >= t1)
            {
                pnl += 0.5 * (Math.Max(b[k].Open, t1) - entry);
                rem = 0.5; tp1 = true;
                stop = Math.Max(stop, entry);
            }
            if (tp1 && b[k].High >= t2)
            {
                pnl += rem * (Math.Max(b[k].Open, t2) - entry);
                return Book(pnl, entry, risk, atr, k - e, true);
            }
            if (tp1) stop = ManagedExitRules.AtrTrailStop(b, k, 14, 3.0, OrderSide.Buy, stop);
        }
        pnl += rem * (b[n - 1].Close - entry);
        return Book(pnl, entry, risk, atr, n - 1 - e, false);
    }

    /// <summary>X3, X4, X5 and every random-exit control: one stop, an optional rule exit at a
    /// bar's close, and a cap bar whose close ends the trade.</summary>
    public static ExitResult StopAndRule(SeriesInputs s, int i, double stop, bool[]? exitAt, int capBar) =>
        StopAndRule(s.Bars, s.Atr, i, stop, exitAt, capBar);

    /// <summary>The same stop-and-rule exit on any bar series and its ATR(14).</summary>
    public static ExitResult StopAndRule(IReadOnlyList<Ohlcv> b, double[] atrs, int i, double stop, bool[]? exitAt, int capBar)
    {
        int e = i + 1, n = b.Count;
        double entry = b[e].Open, risk = entry - stop, atr = atrs[i];
        int last = Math.Min(capBar, n - 1);
        for (int k = e; k <= last; k++)
        {
            if (b[k].Low <= stop) return Book(Math.Min(b[k].Open, stop) - entry, entry, risk, atr, k - e, true);
            if ((exitAt != null && exitAt[k]) || k == capBar) return Book(b[k].Close - entry, entry, risk, atr, k - e, true);
        }
        return Book(b[last].Close - entry, entry, risk, atr, last - e, false);
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    /// <summary>Holm step-down adjusted p-values, in the input order. The multiplier uses the
    /// FULL family size even when a member is untestable (NaN): the family was fixed in advance,
    /// and shrinking it because a test could not run would loosen the others.</summary>
    public static double[] Holm(IReadOnlyList<double> p)
    {
        int m = p.Count;
        var idx = Enumerable.Range(0, m).Where(k => !double.IsNaN(p[k])).OrderBy(k => p[k]).ToList();
        int mm = idx.Count;
        var adj = Enumerable.Repeat(double.NaN, m).ToArray();
        double run = 0;
        for (int r = 0; r < mm; r++)
        {
            double v = Math.Min(1.0, (m - r) * p[idx[r]]);
            run = Math.Max(run, v);
            adj[idx[r]] = run;
        }
        return adj;
    }
}
