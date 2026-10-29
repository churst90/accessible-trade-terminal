using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Cycle Position's two readings about daily lows (2026-10-28): "Low is in" and "Low expected".</b>
    /// Cody: "figure out anything that can help call and confirm lows with a confidence score so low
    /// confidence/high confidence ... the user doesn't need to know '15 points'". Both are fitted on
    /// Cycle Position's own lows on 30 daily charts and checked on Bitcoin, which they never saw
    /// (docs/CYCLE_POSITION_DESIGN.md, "Low is in").
    ///
    /// <para><b>Low is in.</b> On each day a daily low is held but not yet confirmed: how likely it is
    /// that Cycle Position confirms it. Twelve inputs, each read from that day and earlier ones:
    /// the chance a random walk at the day's range-based volatility (Parkinson, 20 days) touches the
    /// low before the confirmation clock runs out; how far the closes have risen off the low in
    /// average ranges; how far the lows since came back toward it; five confirmations counted in
    /// bars since the low (closes above the 10-day average, a higher swing low whose high was broken,
    /// the double-smoothed stochastic above its trigger, the parabolic SAR under price, a close above
    /// the 10 before it) and the days held, each as 0 to 3 points scaled to the shortest cycle; two
    /// products of the touch chance; and the days left to the clock. 200 small boosted trees, then a
    /// calibration, give a share; the words are its bands. On 15 assets it never saw, the first
    /// "very high" was right 96% of the time and came a median 7 days before the confirmation, 95%
    /// of real lows getting one; on Bitcoin 2020-2026, 98%, 9 days, all 39.</para>
    ///
    /// <para><b>Low expected.</b> On every day after a confirmed low: will Cycle Position draw a new
    /// daily low within the next k days, k a sixth of the shortest cycle (at least 2)? A logistic on
    /// how often past cycles this old ended within k days (this chart's own and 30 charts'), the day
    /// against the window, the fall from the cycle's top, the 10-day average, the oscillators and the
    /// held low. Weaker than "Low is in": on unseen charts "likely" came true about half the time
    /// (a new low within k days on 14% of days overall), "unlikely" 8%.</para>
    ///
    /// <para><b>Weekly and yearly "low is in" (2026-10-28).</b> Cody: "try and fit the weekly and
    /// yearly bottoms as well". The same reading for the weekly low being made (<see cref="WeeklySubject"/>:
    /// the held daily low that would become it, or the drawn candidate; the same twelve inputs on a
    /// clock of a cycle and a half, its own trees in CycleLowConfidence.Tiers.Model.cs) and for the
    /// yearly one (<see cref="YearlySubject"/>, as <see cref="CipherCEngine.YearlyFromPrice"/> finds
    /// it; four inputs). Every chart scored unseen: the first weekly "very high" right 94.6%, 72% of
    /// weekly lows caught, a median 12 days early; yearly 94.5%, 84%, about 40 days.</para>
    ///
    /// <para>The study scripts (Python) are reproduced exactly: features, trees and calibration are
    /// pinned by CycleLowConfidenceTests on Bitcoin's daily history (averages summed in numpy's
    /// pairwise order, as one Bitcoin close is exactly its 10-day average). Fitted on daily charts, in
    /// bars; Cycle Position reads them on 4-hour and weekly charts too, where they held unseen (the
    /// yearly one, in calendar days, not on 4-hour charts).</para>
    /// </summary>
    internal static partial class CycleLowConfidence
    {
        /// <summary>The bands "Low is in" is said in: below each edge, the word before it.</summary>
        internal static readonly (double From, string Word)[] Bands =
        {
            (0, "likely to fail"), (0.2, "low"), (0.6, "medium"), (0.8, "high"), (0.95, "very high"),
        };

        /// <summary>"Low expected" bands.</summary>
        internal static readonly (double From, string Word)[] ExpectedBands = { (0, "unlikely"), (0.15, "possible"), (0.35, "likely") };

        internal static string Word(double p) => Bands.Last(b => p >= b.From).Word;
        internal static string ExpectedWord(double p) => ExpectedBands.Last(b => p >= b.From).Word;

        /// <summary>One "low is in" reading, per bar (NaN where it does not apply).</summary>
        internal class Reading
        {
            /// <summary>The calibrated share, 0-1, on each bar the reading has a low to read.</summary>
            public double[] Chance = Array.Empty<double>();
            /// <summary>The model's inputs on each bar read, for the tests.</summary>
            public double[][] Inputs = Array.Empty<double[]>();
            /// <summary>The reasons said with it: the random-walk touch chance (0-1), days left to the clock,
            /// percent above the low, average ranges risen, and the bars each confirmation has run (NaN on the
            /// yearly reading, which does not read them); and the price of the low it reads.</summary>
            public double[] Touch = Array.Empty<double>(), Left = Array.Empty<double>(), Above = Array.Empty<double>(), Rise = Array.Empty<double>(),
                            Structure = Array.Empty<double>(), Sma = Array.Empty<double>(), Dss = Array.Empty<double>(), Sar = Array.Empty<double>(),
                            Break = Array.Empty<double>(), Low = Array.Empty<double>();

            internal void Size(int n)
            {
                Chance = CipherCEngine.Nan(n); Inputs = new double[n][]; Touch = CipherCEngine.Nan(n); Left = CipherCEngine.Nan(n); Above = CipherCEngine.Nan(n);
                Rise = CipherCEngine.Nan(n); Structure = CipherCEngine.Nan(n); Sma = CipherCEngine.Nan(n); Dss = CipherCEngine.Nan(n);
                Sar = CipherCEngine.Nan(n); Break = CipherCEngine.Nan(n); Low = CipherCEngine.Nan(n);
            }
        }

        /// <summary>Everything one run decides: the daily "Low is in" and "Low expected", and the weekly and yearly "low is in".</summary>
        internal sealed class Result : Reading
        {
            /// <summary>Low expected: the share, 0-1, and k, the days it looks ahead.</summary>
            public double[] Expected = Array.Empty<double>(), ExpectedDays = Array.Empty<double>();
            /// <summary>The weekly and yearly lows' readings.</summary>
            public Reading Weekly = new(), Yearly = new();
        }

        /// <summary>
        /// All the readings over a daily chart, from the daily tier <paramref name="d"/>, the shortest
        /// daily cycle <paramref name="cycle"/> (in bars) and the learned daily window on each bar;
        /// with the weekly tier <paramref name="w"/> found by time and its gap (bars), the weekly
        /// reading; with the yearly tier <paramref name="y"/> found from the price every
        /// <paramref name="years"/>, the yearly one.
        /// </summary>
        internal static Result Compute(ReadOnlySpan<Ohlcv> data, CipherCEngine.TierRun d, double cycle, double[] windowStart, double[] windowEnd,
            CipherCEngine.TierRun? w = null, double weeklyGap = double.NaN, CipherCEngine.TierRun? y = null, double years = double.NaN)
        {
            int n = data.Length;
            var r = new Result { Expected = CipherCEngine.Nan(n), ExpectedDays = CipherCEngine.Nan(n) };
            r.Size(n); r.Weekly.Size(n); r.Yearly.Size(n);
            if (n == 0 || d.HeldAt.Length != n || d.LastLowAt.Length != n || !(cycle > 0)) return r;
            var s = new Series(data);
            int whole = (int)cycle, half = (int)Math.Round(cycle / 2);
            // Daily: the held low, against the daily confirmation clock.
            Read(r, s, d.HeldAt, (t, c) =>
            {
                int prev = d.LastLowAt[t] >= 0 && d.LastLowAt[t] < c ? d.LastLowAt[t] : -1;
                int due = (prev >= 0 ? Math.Max(c, prev + whole - 2) : c) + half;
                return Math.Max(1, due - t);
            }, Daily, cycle);
            LowExpected(r, s, d, cycle, windowStart, windowEnd);
            // Weekly: the low that would be the next weekly one, against the next daily low's clock.
            if (w != null && weeklyGap > 0)
                Read(r.Weekly, s, WeeklySubject(s, d, w, weeklyGap), (t, c) => Math.Max(1, c + whole + half - t), Weekly, cycle);
            if (y != null && years > 0) ReadYearly(r.Yearly, data, s, y, years);
            return r;
        }

        // ── Low is in ──────────────────────────────────────────────────────────

        /// <summary>
        /// The low the weekly reading reads on each bar, or -1 (2026-10-28). With U the newest weekly
        /// low and C the drawn candidate (a daily low past the gap after U): the held daily low when
        /// it is past the gap and under C (or there is no C), as it would take C's place; else C;
        /// else the held daily low when it is inside the gap and under U, as it would take U's place.
        /// </summary>
        internal static int[] WeeklySubject(Series s, CipherCEngine.TierRun d, CipherCEngine.TierRun w, double gap)
        {
            int n = s.N;
            var o = Enumerable.Repeat(-1, n).ToArray();
            if (w.NewestAt.Length != n || w.HeldAt.Length != n || d.HeldAt.Length != n) return o;
            for (int t = 0; t < n; t++)
            {
                int u = w.NewestAt[t], h = d.HeldAt[t];
                if (u < 0) continue;
                int cand = w.HeldAt[t] > u ? w.HeldAt[t] : -1;
                if (h >= 0 && h - u >= gap && (cand < 0 || s.Low[h] < s.Low[cand])) o[t] = h;
                else if (cand >= 0) o[t] = cand;
                else if (h >= 0 && h - u < gap && s.Low[h] < s.Low[u]) o[t] = h;
            }
            return o;
        }

        /// <summary>
        /// One reading of twelve inputs (the daily and weekly ones): on each bar with a low in
        /// <paramref name="subject"/>, the chance it is in, against a clock of <paramref name="left"/>
        /// bars (from the bar and the low's bar).
        /// </summary>
        private static void Read(Reading r, Series s, int[] subject, Func<int, int, double> left, Forest model, double cycle)
        {
            int n = s.N;
            // The running confirmations since the low, carried bar to bar while it stays the same low.
            int c = -1, last = -1, sma = 0, st = 0, dss = 0, sar = 0, brk = 0;
            double reference = double.PositiveInfinity, maxClose = double.NegativeInfinity, minLow = double.PositiveInfinity;
            for (int t = 0; t < n; t++)
            {
                int held = subject[t];
                if (held < 0) { c = -1; continue; }
                if (held != c || last != t - 1)
                {
                    c = held; sma = st = dss = sar = brk = 0;
                    reference = double.PositiveInfinity; maxClose = double.NegativeInfinity; minLow = double.PositiveInfinity;
                    for (int i = c + 1; i < t; i++) Step(i);
                }
                if (t > c) Step(t);
                last = t;

                double low = s.Low[c], close = s.Close[t];
                double lf = left(t, c);
                double dlog = close > 0 && low > 0 ? Math.Log(close / low) : 0.0;
                double vol = s.Parkinson[t];
                if (!double.IsFinite(vol) || !double.IsFinite(dlog)) continue;
                double touch = Touch(dlog, vol, lf);
                double u = Logit(1 - touch);
                double rise = t > c ? (maxClose - low) / Math.Max(1e-12, s.Atr[c]) : 0.0;
                double retest = t > c && maxClose > low ? (minLow - low) / Math.Max(1e-12, maxClose - low) : 0.0;
                int pSt = Points(st, cycle);
                var x = new[]
                {
                    u, rise, Math.Min(1, Math.Max(-1, retest)), Points(sma, cycle), pSt, Points(dss, cycle), Points(sar, cycle), Points(brk, cycle),
                    Points(t - c, cycle, 3, 8, 16), pSt > 0 ? u : 0.0, u * rise / 4, lf / cycle,
                };
                if (x.Any(v => !double.IsFinite(v))) continue;
                r.Inputs[t] = x;
                r.Chance[t] = model.Chance(x);
                r.Touch[t] = touch; r.Left[t] = lf; r.Rise[t] = rise; r.Above[t] = 100 * (close / low - 1); r.Low[t] = low;
                r.Structure[t] = st; r.Sma[t] = sma; r.Dss[t] = dss; r.Sar[t] = sar; r.Break[t] = brk;
            }

            // One bar after the held low: each confirmation's run, as the study's run_len over the bars since the low.
            void Step(int i)
            {
                sma = s.Close[i] > s.Sma10[i] ? sma + 1 : 0;
                dss = s.Dss[i] > s.Trigger[i] ? dss + 1 : 0;
                sar = s.SarLong[i] ? sar + 1 : 0;
                // A swing low two bars back, above the held low: its high since the low is the level to break.
                int p = i - 2;
                if (p > c + 1 && s.Low[p] > s.Low[c] && s.Low[p] < s.Low[p - 1] && s.Low[p] < s.Low[p - 2] && s.Low[p] <= s.Low[p + 1] && s.Low[p] <= s.Low[p + 2])
                {
                    reference = double.NegativeInfinity;
                    for (int j = c; j <= p; j++) reference = Math.Max(reference, s.High[j]);
                }
                st = s.Close[i] > reference ? st + 1 : 0;
                // Bars since the first close above the 10 before it, counting its own.
                brk = brk > 0 ? brk + 1 : s.Breakout[i] ? 1 : 0;
                maxClose = Math.Max(maxClose, s.Close[i]);
                minLow = Math.Min(minLow, s.Low[i]);
            }
        }

        /// <summary>0 to 3 points for a run of <paramref name="bars"/>: the tiers are 1, 3 and 6 bars on a 48-bar shortest cycle, scaled.</summary>
        internal static int Points(int bars, double cycle, int a = 1, int b = 3, int c = 6)
        {
            int prev = 0, points = 0;
            foreach (int k in new[] { a, b, c })
            {
                int v = Math.Max(prev + 1, (int)Math.Round(k * cycle / 48));
                if (bars >= v) points++;
                prev = v;
            }
            return points;
        }

        internal static double Logit(double p) => Math.Log((p + 1e-4) / (1 - p + 1e-4));
        internal static double Sigmoid(double z) => 1 / (1 + Math.Exp(-z));

        /// <summary>The chance a random walk at volatility <paramref name="vol"/> (per bar) goes back down <paramref name="dlog"/> (log) within <paramref name="left"/> bars.</summary>
        internal static double Touch(double dlog, double vol, double left) =>
            Math.Min(1.0, Math.Max(0.0, 2 * 0.5 * (1 + Erf(-dlog / (Math.Max(1e-6, vol) * Math.Sqrt(left)) / Math.Sqrt(2)))));

        /// <summary>Boosted trees and the calibration fitted after them: a share from the inputs.</summary>
        internal sealed record Forest(double Baseline, int[] Root, int[] Feature, double[] Threshold, int[] Left, int[] Right, double[] Value,
            double[] CalibrationX, double[] CalibrationY)
        {
            /// <summary>The trees' raw score: the baseline plus one leaf per tree (an input at or under a split's threshold goes left).</summary>
            internal double Raw(double[] x)
            {
                double sum = Baseline;
                foreach (int root in Root)
                {
                    int i = root;
                    while (Feature[i] >= 0) i = x[Feature[i]] <= Threshold[i] ? Left[i] : Right[i];
                    sum += Value[i];
                }
                return sum;
            }

            /// <summary>The isotonic calibration: linear between its points, flat outside them.</summary>
            internal double Calibrate(double p)
            {
                var xs = CalibrationX; var ys = CalibrationY;
                if (p <= xs[0]) return ys[0];
                if (p >= xs[^1]) return ys[^1];
                int hi = Array.BinarySearch(xs, p);
                if (hi >= 0) return ys[hi];
                hi = ~hi;
                int lo = hi - 1;
                return ys[lo] + (p - xs[lo]) * (ys[hi] - ys[lo]) / (xs[hi] - xs[lo]);
            }

            internal double Chance(double[] x) => Calibrate(Sigmoid(Raw(x)));
        }

        private static Forest? _daily;
        /// <summary>The daily model (CycleLowConfidence.Model.cs).</summary>
        internal static Forest Daily => _daily ??= new Forest(Baseline, TreeRoot, NodeFeature, NodeThreshold, NodeLeft, NodeRight, NodeValue, CalibrationX, CalibrationY);

        // ── The yearly low ─────────────────────────────────────────────────────

        /// <summary>
        /// The yearly reading (2026-10-28): on each bar, the bar that would be the next yearly low as
        /// <see cref="CipherCEngine.YearlyFromPrice"/> finds it (the earliest at least the gap after
        /// the last yearly low, the lowest for its radius before it, not undercut since, not yet a
        /// radius old), and the chance it is: four inputs, the touch chance to the end of its radius
        /// (in bars, as the last radius of the chart ran), the rise and the retest as the daily
        /// reading reads them, and the time held in radii (at most 2).
        /// </summary>
        private static void ReadYearly(Reading r, ReadOnlySpan<Ohlcv> data, Series s, CipherCEngine.TierRun y, double years)
        {
            int n = s.N;
            if (y.LastLowAt.Length != n) return;
            double cycleDays = years * 365.25, radius = cycleDays * CipherCEngine.YearlyPivotCycles, gap = cycleDays * CipherCEngine.YearlyGapCycles;
            var day = Days(data);
            var subject = YearlySubject(day, s.Low, y.LastLowAt, radius, gap);
            int c = -1, last = -1;
            double maxClose = double.NegativeInfinity, minLow = double.PositiveInfinity;
            for (int t = 0; t < n; t++)
            {
                if (subject[t] < 0) continue;
                if (subject[t] != c || last != t - 1)
                {
                    c = subject[t]; maxClose = double.NegativeInfinity; minLow = double.PositiveInfinity;
                    for (int i = c + 1; i < t; i++) { maxClose = Math.Max(maxClose, s.Close[i]); minLow = Math.Min(minLow, s.Low[i]); }
                }
                if (t > c) { maxClose = Math.Max(maxClose, s.Close[t]); minLow = Math.Min(minLow, s.Low[t]); }
                last = t;

                double low = s.Low[c], close = s.Close[t];
                int radiusBars = t - First(day, day[t] - radius);
                double lf = Math.Max(1, c + radiusBars - t);
                double dlog = close > 0 && low > 0 ? Math.Log(close / low) : 0.0;
                double vol = s.Parkinson[t];
                if (!double.IsFinite(vol) || !double.IsFinite(dlog)) continue;
                double touch = Touch(dlog, vol, lf);
                double rise = t > c ? (maxClose - low) / Math.Max(1e-12, s.Atr[c]) : 0.0;
                double retest = t > c && maxClose > low ? (minLow - low) / Math.Max(1e-12, maxClose - low) : 0.0;
                double heldRadii = (day[t] - day[c]) / radius;
                var x = new[] { Logit(1 - touch), rise, Math.Min(1, Math.Max(-1, retest)), Math.Min(2, heldRadii) };
                if (x.Any(v => !double.IsFinite(v))) continue;
                r.Inputs[t] = x;
                r.Chance[t] = Yearly.Chance(x);
                r.Touch[t] = touch; r.Left[t] = lf; r.Rise[t] = rise; r.Above[t] = 100 * (close / low - 1); r.Low[t] = low;
            }
        }

        /// <summary>
        /// Each bar's yearly low to read, or -1: the earliest bar still standing that is the lowest
        /// for <paramref name="radius"/> days before it (the first of equal lows), from
        /// <paramref name="gap"/> days after the last yearly low (before the first, a radius after
        /// the first bar), and dropped when a later low is strictly under it.
        /// </summary>
        internal static int[] YearlySubject(double[] day, double[] low, int[] lastLowAt, double radius, double gap)
        {
            int n = day.Length;
            var leftValid = new bool[n];
            for (int i = 0; i < n; i++)
            {
                bool ok = true;
                for (int j = Math.Max(0, First(day, day[i] - radius)); j < i && ok; j++) ok = low[j] > low[i];
                leftValid[i] = ok;
            }
            var o = new int[n];
            var alive = new List<int>();
            for (int t = 0; t < n; t++)
            {
                o[t] = -1;
                int prev = lastLowAt[t];
                int from = First(day, prev >= 0 ? day[prev] + gap : day[0] + radius);
                if (from < 0 || t < from) { alive.Clear(); continue; }
                alive.RemoveAll(i => i <= prev || low[t] < low[i]);
                if (leftValid[t]) alive.Add(t);
                if (alive.Count > 0) o[t] = alive[0];
            }
            return o;
        }

        /// <summary>Each bar's time in whole calendar days, as the study counted it (a bar's hour,
        /// which moves with daylight saving on some charts, is not counted).</summary>
        internal static double[] Days(ReadOnlySpan<Ohlcv> data)
        {
            var day = new double[data.Length];
            for (int i = 0; i < data.Length; i++) day[i] = (data[i].Date.Date - DateTime.UnixEpoch.Date).TotalDays;
            return day;
        }

        /// <summary>The first bar on or after day <paramref name="d"/> (fractional), or -1.</summary>
        private static int First(double[] day, double d)
        {
            int lo = 0, hi = day.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (day[mid] < d - 1e-9) lo = mid + 1; else hi = mid; }
            return lo < day.Length ? lo : -1;
        }

        // ── Low expected ───────────────────────────────────────────────────────

        private static void LowExpected(Result r, Series s, CipherCEngine.TierRun d, double cycle, double[] windowStart, double[] windowEnd)
        {
            int n = s.N;
            int k = Math.Max(2, (int)Math.Round(cycle / 6));
            // The lows that stand, with their confirmation bars, oldest first.
            var lows = new List<(int Bar, int At)>();
            for (int i = 0; i < d.Lows.Count; i++) if (d.ConfirmedAt[i] >= 0) lows.Add((d.Lows[i], d.ConfirmedAt[i]));
            lows.Sort();
            var x = new double[ExpectedMean.Length];
            for (int t = 60; t < n; t++)
            {
                int last = d.LastLowAt[t];
                if (last < 0) continue;
                // This chart's past cycles known by today, the newest 40, in shortest cycles.
                var past = new List<double>();
                for (int i = 1; i < lows.Count; i++) if (lows[i].At <= t) past.Add((lows[i].Bar - lows[i - 1].Bar) / cycle);
                if (past.Count > 40) past.RemoveRange(0, past.Count - 40);
                double age = (t - last) / cycle, span = k / cycle;
                double h = Hazard(past, age, span);
                double start = windowStart.Length == n ? windowStart[t] : double.NaN, end = windowEnd.Length == n ? windowEnd[t] : double.NaN;
                double winpos = double.IsNaN(start) || double.IsNaN(end) ? 0.0 : (t - last - start) / Math.Max(1, end - start);
                int top = last;
                for (int i = last + 1; i <= t; i++) if (s.High[i] > s.High[top]) top = i;
                double minClose = double.PositiveInfinity;
                for (int i = last + 1; i <= t; i++) minClose = Math.Min(minClose, s.Close[i]);
                double low10 = double.PositiveInfinity;
                for (int i = Math.Max(0, t - 9); i <= t; i++) low10 = Math.Min(low10, s.Low[i]);
                double atr = Math.Max(1e-12, s.Atr[t]);
                int held = d.HeldAt[t];
                double heldDays = held >= 0 ? t - held : -1;
                int j = 0;
                x[j++] = h; x[j++] = Math.Log(h + 1e-3); x[j++] = winpos; x[j++] = winpos * winpos; x[j++] = age;
                x[j++] = (s.High[top] - s.Close[t]) / atr; x[j++] = (s.High[top] - s.Close[t]) / s.High[top];
                x[j++] = t > last && minClose < s.Low[last] ? 1 : 0;
                x[j++] = (s.Close[t] - s.Sma10[t]) / atr; x[j++] = s.SmaSlope[t];
                x[j++] = s.Low[t] <= low10 ? 1 : 0; x[j++] = (t - top) / cycle; x[j++] = (top - last) / Math.Max(1.0, t - last);
                x[j++] = s.Dss[t] / 100; x[j++] = s.Wt1[t] / 60; x[j++] = (double.IsNaN(s.Wt2[t]) ? 0.0 : s.Wt2[t]) / 60;
                x[j++] = (double.IsNaN(s.Stoch81[t]) ? 50.0 : s.Stoch81[t]) / 100;
                x[j++] = heldDays >= 0 ? 1 : 0; x[j++] = Math.Max(0, heldDays) / cycle;
                double z = ExpectedIntercept;
                for (int i = 0; i < x.Length; i++) z += (x[i] - ExpectedMean[i]) / ExpectedScale[i] * ExpectedCoef[i];
                if (!double.IsFinite(z)) continue;
                r.Expected[t] = Sigmoid(z);
                r.ExpectedDays[t] = k;
            }
        }

        /// <summary>
        /// Of past cycles that lasted past <paramref name="age"/> (in shortest cycles), the share that
        /// ended within <paramref name="span"/> more: this chart's own, plus 30 charts' worth 10 cycles.
        /// </summary>
        internal static double Hazard(List<double> own, double age, double span, double weight = 10)
        {
            int n1 = 0, e1 = 0, n0 = 0, e0 = 0;
            foreach (double v in own) { if (v > age) { n1++; if (v <= age + span) e1++; } }
            foreach (double v in PriorCycles) { if (v > age) { n0++; if (v <= age + span) e0++; } }
            double m = Math.Max(1, PriorCycles.Length);
            return (e1 + weight * e0 / m) / Math.Max(1e-9, n1 + weight * n0 / m);
        }

        // ── The price series both read ─────────────────────────────────────────

        /// <summary>The indicators the study computed, in its own way (its partial-window stochastics, its SAR).</summary>
        internal sealed class Series
        {
            public readonly int N;
            public readonly double[] High, Low, Close, Sma10, SmaSlope, Atr, Dss, Trigger, Wt1, Wt2, Stoch81, Parkinson;
            public readonly bool[] SarLong, Breakout;

            public Series(ReadOnlySpan<Ohlcv> data)
            {
                int n = N = data.Length;
                double[] open = new double[n];
                High = new double[n]; Low = new double[n]; Close = new double[n];
                for (int i = 0; i < n; i++) { open[i] = data[i].Open; High[i] = data[i].High; Low[i] = data[i].Low; Close[i] = data[i].Close; }
                Sma10 = Sma(Close, 10);
                // True range and Wilder's 14-bar average range, from the first bar's range.
                Atr = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double tr = i == 0 ? High[0] - Low[0]
                        : Math.Max(High[i] - Low[i], Math.Max(Math.Abs(High[i] - Close[i - 1]), Math.Abs(Low[i] - Close[i - 1])));
                    Atr[i] = i == 0 ? tr : Atr[i - 1] + (tr - Atr[i - 1]) / 14;
                }
                SmaSlope = new double[n];
                for (int i = 3; i < n; i++) SmaSlope[i] = (Sma10[i] - Sma10[i - 3]) / (Atr[i] > 0 ? Atr[i] : 1);
                // DSS Bressert 13/8, trigger 5.
                var mit = Ema(Stoch(Close, High, Low, 13), 8);
                Dss = Ema(Stoch(mit, mit, mit, 13), 8);
                Trigger = Ema(Dss, 5);
                // WaveTrend on hlc3: 9, 12, signal SMA 3.
                var src = new double[n];
                for (int i = 0; i < n; i++) src[i] = (High[i] + Low[i] + Close[i]) / 3;
                var esa = Ema(src, 9);
                var dev = new double[n];
                for (int i = 0; i < n; i++) dev[i] = Math.Abs(src[i] - esa[i]);
                var dd = Ema(dev, 9);
                var ci = new double[n];
                for (int i = 0; i < n; i++) ci[i] = dd[i] > 1e-10 ? (src[i] - esa[i]) / (0.015 * dd[i]) : 0.0;
                Wt1 = Ema(ci, 12);
                Wt2 = Sma(Wt1, 3);
                Stoch81 = Sma(Stoch(Close, High, Low, 81), 2);
                // Parkinson volatility over 20 bars: sqrt(mean(ln(H/L)^2) / (4 ln 2)).
                Parkinson = new double[n];
                var lh2 = new double[n];
                for (int i = 0; i < n; i++) { double v = Math.Log(High[i] / Low[i]); lh2[i] = v * v; }
                for (int i = 0; i < n; i++)
                {
                    int from = Math.Max(0, i - 19);
                    Parkinson[i] = Math.Sqrt(PairwiseSum(lh2, from, i - from + 1) / (i - from + 1) / (4 * Math.Log(2)));
                }
                // Parabolic SAR 0.02 / 0.2, long or short on each bar (the first bar short).
                SarLong = new bool[n];
                bool up = true; double af = 0.02, ep = n > 0 ? High[0] : 0, sar = n > 0 ? Low[0] : 0;
                for (int i = 1; i < n; i++)
                {
                    sar += af * (ep - sar);
                    if (up)
                    {
                        sar = Math.Min(sar, Math.Min(Low[i - 1], Low[Math.Max(0, i - 2)]));
                        if (Low[i] < sar) { up = false; sar = ep; ep = Low[i]; af = 0.02; }
                        else if (High[i] > ep) { ep = High[i]; af = Math.Min(0.2, af + 0.02); }
                    }
                    else
                    {
                        sar = Math.Max(sar, Math.Max(High[i - 1], High[Math.Max(0, i - 2)]));
                        if (High[i] > sar) { up = true; sar = ep; ep = High[i]; af = 0.02; }
                        else if (Low[i] < ep) { ep = Low[i]; af = Math.Min(0.2, af + 0.02); }
                    }
                    SarLong[i] = up;
                }
                // A close above each of the 10 before it.
                Breakout = new bool[n];
                for (int i = 10; i < n; i++)
                {
                    double m = double.NegativeInfinity;
                    for (int j = i - 10; j < i; j++) m = Math.Max(m, Close[j]);
                    Breakout[i] = Close[i] > m;
                }
            }

            /// <summary>The mean of the last <paramref name="p"/> values, NaN until there are that many; summed
            /// in numpy's order, as the study did, so a close exactly on its average falls the same side.</summary>
            private static double[] Sma(double[] x, int p)
            {
                var o = CipherCEngine.Nan(x.Length);
                for (int i = p - 1; i < x.Length; i++) o[i] = PairwiseSum(x, i - p + 1, p) / p;
                return o;
            }

            /// <summary>numpy's pairwise sum: under 8 values one by one; up to 128, eight running sums
            /// added in pairs and then the rest one by one; above, the two halves.</summary>
            internal static double PairwiseSum(double[] a, int from, int n)
            {
                if (n < 8)
                {
                    double res = 0;
                    for (int i = 0; i < n; i++) res += a[from + i];
                    return res;
                }
                if (n <= 128)
                {
                    var r = new double[8];
                    for (int j = 0; j < 8; j++) r[j] = a[from + j];
                    int i = 8;
                    for (; i < n - n % 8; i += 8)
                        for (int j = 0; j < 8; j++) r[j] += a[from + i + j];
                    double res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
                    for (; i < n; i++) res += a[from + i];
                    return res;
                }
                int n2 = n / 2;
                n2 -= n2 % 8;
                return PairwiseSum(a, from, n2) + PairwiseSum(a, from + n2, n - n2);
            }

            /// <summary>An EMA from the first value, carrying through NaN inputs.</summary>
            private static double[] Ema(double[] x, int p)
            {
                double a = 2.0 / (p + 1);
                var o = (double[])x.Clone();
                for (int i = 1; i < o.Length; i++)
                    o[i] = double.IsNaN(o[i]) ? o[i - 1] : double.IsNaN(o[i - 1]) ? o[i] : o[i - 1] + a * (o[i] - o[i - 1]);
                return o;
            }

            /// <summary>A stochastic over up to <paramref name="p"/> bars (fewer at the start); 50 when the range is flat.</summary>
            private static double[] Stoch(double[] src, double[] hi, double[] lo, int p)
            {
                var o = new double[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    double a = double.NegativeInfinity, b = double.PositiveInfinity;
                    for (int j = Math.Max(0, i - p + 1); j <= i; j++) { a = Math.Max(a, hi[j]); b = Math.Min(b, lo[j]); }
                    double d = a - b;
                    o[i] = d > 1e-12 ? 100 * (src[i] - b) / d : 50.0;
                }
                return o;
            }
        }

        /// <summary>
        /// The error function, from its everywhere-convergent series erf(x) = 2/sqrt(pi) e^(-x^2)
        /// sum 2^k x^(2k+1) / (1·3·…·(2k+1)), whose terms are all positive; 1 past 6 (as in doubles).
        /// </summary>
        internal static double Erf(double x)
        {
            if (double.IsNaN(x)) return double.NaN;
            if (x < 0) return -Erf(-x);
            if (x >= 6) return 1.0;
            double x2 = x * x, term = x, sum = x;
            for (int k = 1; k < 500; k++)
            {
                term *= 2 * x2 / (2 * k + 1);
                sum += term;
                if (term < 1e-17 * sum) break;
            }
            return Math.Min(1.0, 2 / Math.Sqrt(Math.PI) * Math.Exp(-x2) * sum);
        }
    }
}
