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
    /// <para>The study scripts (Python) are reproduced exactly: features, trees and calibration are
    /// pinned by CycleLowConfidenceTests on Bitcoin's daily history. Daily charts only.</para>
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

        /// <summary>Everything one run decides, per bar (NaN where it does not apply).</summary>
        internal sealed class Result
        {
            /// <summary>Low is in: the calibrated share, 0-1, on each bar a daily low is held.</summary>
            public double[] Chance = Array.Empty<double>();
            /// <summary>The model's twelve inputs on each held bar, for the tests.</summary>
            public double[][] Inputs = Array.Empty<double[]>();
            /// <summary>The reasons said with it: the random-walk touch chance (0-1), days left to the clock,
            /// percent above the low, average ranges risen, and the bars each confirmation has run.</summary>
            public double[] Touch = Array.Empty<double>(), Left = Array.Empty<double>(), Above = Array.Empty<double>(), Rise = Array.Empty<double>(),
                            Structure = Array.Empty<double>(), Sma = Array.Empty<double>(), Dss = Array.Empty<double>(), Sar = Array.Empty<double>(),
                            Break = Array.Empty<double>();
            /// <summary>Low expected: the share, 0-1, and k, the days it looks ahead.</summary>
            public double[] Expected = Array.Empty<double>(), ExpectedDays = Array.Empty<double>();
        }

        /// <summary>
        /// Both readings over a daily chart, from the daily tier <paramref name="d"/>, the shortest
        /// daily cycle <paramref name="cycle"/> (in bars) and the learned daily window on each bar.
        /// </summary>
        internal static Result Compute(ReadOnlySpan<Ohlcv> data, CipherCEngine.TierRun d, double cycle, double[] windowStart, double[] windowEnd)
        {
            int n = data.Length;
            var r = new Result
            {
                Chance = CipherCEngine.Nan(n), Inputs = new double[n][], Touch = CipherCEngine.Nan(n), Left = CipherCEngine.Nan(n), Above = CipherCEngine.Nan(n),
                Rise = CipherCEngine.Nan(n), Structure = CipherCEngine.Nan(n), Sma = CipherCEngine.Nan(n), Dss = CipherCEngine.Nan(n),
                Sar = CipherCEngine.Nan(n), Break = CipherCEngine.Nan(n), Expected = CipherCEngine.Nan(n), ExpectedDays = CipherCEngine.Nan(n),
            };
            if (n == 0 || d.HeldAt.Length != n || d.LastLowAt.Length != n || !(cycle > 0)) return r;
            var s = new Series(data);
            LowIsIn(r, s, d, cycle);
            LowExpected(r, s, d, cycle, windowStart, windowEnd);
            return r;
        }

        // ── Low is in ──────────────────────────────────────────────────────────

        private static void LowIsIn(Result r, Series s, CipherCEngine.TierRun d, double cycle)
        {
            int n = s.N;
            int whole = (int)cycle, half = (int)Math.Round(cycle / 2);
            // The running confirmations since the held low, carried bar to bar while it stays the same low.
            int c = -1, last = -1, sma = 0, st = 0, dss = 0, sar = 0, brk = 0;
            double reference = double.PositiveInfinity, maxClose = double.NegativeInfinity, minLow = double.PositiveInfinity;
            for (int t = 0; t < n; t++)
            {
                int held = d.HeldAt[t];
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
                int prev = d.LastLowAt[t] >= 0 && d.LastLowAt[t] < c ? d.LastLowAt[t] : -1;
                int due = (prev >= 0 ? Math.Max(c, prev + whole - 2) : c) + half;
                double left = Math.Max(1, due - t);
                double dlog = close > 0 && low > 0 ? Math.Log(close / low) : 0.0;
                double vol = s.Parkinson[t];
                if (!double.IsFinite(vol) || !double.IsFinite(dlog)) continue;
                double touch = Math.Min(1.0, Math.Max(0.0, 2 * 0.5 * (1 + Erf(-dlog / (Math.Max(1e-6, vol) * Math.Sqrt(left)) / Math.Sqrt(2)))));
                double u = Logit(1 - touch);
                double rise = t > c ? (maxClose - low) / Math.Max(1e-12, s.Atr[c]) : 0.0;
                double retest = t > c && maxClose > low ? (minLow - low) / Math.Max(1e-12, maxClose - low) : 0.0;
                int pSt = Points(st, cycle);
                var x = new[]
                {
                    u, rise, Math.Min(1, Math.Max(-1, retest)), Points(sma, cycle), pSt, Points(dss, cycle), Points(sar, cycle), Points(brk, cycle),
                    Points(t - c, cycle, 3, 8, 16), pSt > 0 ? u : 0.0, u * rise / 4, left / cycle,
                };
                if (x.Any(v => !double.IsFinite(v))) continue;
                r.Inputs[t] = x;
                r.Chance[t] = Calibrate(Sigmoid(Raw(x)));
                r.Touch[t] = touch; r.Left[t] = left; r.Rise[t] = rise; r.Above[t] = 100 * (close / low - 1);
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

        /// <summary>The trees' raw score: the baseline plus one leaf per tree (an input at or under a split's threshold goes left).</summary>
        internal static double Raw(double[] x)
        {
            double sum = Baseline;
            foreach (int root in TreeRoot)
            {
                int i = root;
                while (NodeFeature[i] >= 0) i = x[NodeFeature[i]] <= NodeThreshold[i] ? NodeLeft[i] : NodeRight[i];
                sum += NodeValue[i];
            }
            return sum;
        }

        /// <summary>The isotonic calibration: linear between its points, flat outside them.</summary>
        internal static double Calibrate(double p)
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
        private sealed class Series
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
                    double sum = 0;
                    for (int j = from; j <= i; j++) sum += lh2[j];
                    Parkinson[i] = Math.Sqrt(sum / (i - from + 1) / (4 * Math.Log(2)));
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

            /// <summary>The mean of the last <paramref name="p"/> values, NaN until there are that many.</summary>
            private static double[] Sma(double[] x, int p)
            {
                var o = CipherCEngine.Nan(x.Length);
                for (int i = p - 1; i < x.Length; i++)
                {
                    double sum = 0;
                    for (int j = i - p + 1; j <= i; j++) sum += x[j];
                    o[i] = sum / p;
                }
                return o;
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
