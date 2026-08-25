using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Indicators
{
    /// <summary>
    /// Shared math helpers for custom indicator providers.
    /// All methods operate on <c>double[]</c> arrays and return new arrays of the same length,
    /// filling leading values with <see cref="double.NaN"/> during the warmup period.
    ///
    /// <para>
    /// Plugin authors can reference these helpers instead of re-implementing common primitives.
    /// Each method is a pure function — no state is kept between calls.
    /// </para>
    /// </summary>
    public static class IndicatorMath
    {
        /// <summary>
        /// Exponential Moving Average (EMA) with standard 2/(period+1) smoothing factor.
        /// Warmup: first <c>period-1</c> values are NaN; the EMA starts seeding from the first
        /// valid (non-NaN) input value.
        /// </summary>
        public static double[] Ema(double[] src, int period)
        {
            var r = new double[src.Length];
            double k = 2.0 / (period + 1.0);
            double ema = double.NaN;
            int warmup = 0;
            for (int i = 0; i < src.Length; i++)
            {
                double v = src[i];
                if (double.IsNaN(v)) { r[i] = double.NaN; continue; }
                if (double.IsNaN(ema)) { ema = v; warmup = 1; }
                else { ema = v * k + ema * (1.0 - k); warmup++; }
                r[i] = warmup < period ? double.NaN : ema;
            }
            return r;
        }

        /// <summary>
        /// Simple Moving Average (SMA).
        /// Returns NaN for the first <c>period-1</c> bars and for any window that contains
        /// a NaN input value.
        /// </summary>
        public static double[] Sma(double[] src, int period)
        {
            var r = new double[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                if (i < period - 1) { r[i] = double.NaN; continue; }
                double sum = 0; int cnt = 0;
                for (int j = i - period + 1; j <= i; j++)
                    if (!double.IsNaN(src[j])) { sum += src[j]; cnt++; }
                r[i] = cnt == period ? sum / period : double.NaN;
            }
            return r;
        }

        /// <summary>
        /// Wilder/RSI-style Relative Strength Index.
        /// Uses the standard Wilder smoothing (RMA) for avg gain and avg loss.
        /// Returns NaN for the first <c>period</c> bars.
        /// </summary>
        public static double[] Rsi(double[] src, int period)
        {
            var r = new double[src.Length];
            Array.Fill(r, double.NaN);
            if (src.Length < period + 1) return r;
            double avgGain = 0, avgLoss = 0;
            for (int i = 1; i <= period; i++)
            {
                double ch = src[i] - src[i - 1];
                if (ch > 0) avgGain += ch; else avgLoss -= ch;
            }
            avgGain /= period; avgLoss /= period;
            r[period] = avgLoss < 1e-10 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
            for (int i = period + 1; i < src.Length; i++)
            {
                double ch = src[i] - src[i - 1];
                double gain = ch > 0 ? ch : 0;
                double loss = ch < 0 ? -ch : 0;
                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
                r[i] = avgLoss < 1e-10 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
            }
            return r;
        }

        /// <summary>
        /// Ehlers Laguerre RSI with a 4-element recursive Laguerre filter.
        /// Output is normalised from 0–1 to a ±35 range: <c>(value − 0.5) × 70</c>.
        /// <paramref name="gamma"/> controls smoothness vs lag (0 = no smoothing, 1 = max smoothing).
        /// </summary>
        public static double[] LaguerreRsi(double[] src, double gamma)
        {
            int n = src.Length;
            var r = new double[n];
            Array.Fill(r, double.NaN);
            double L0 = double.NaN, L1 = double.NaN, L2 = double.NaN, L3 = double.NaN;

            for (int i = 0; i < n; i++)
            {
                double p = src[i];
                if (double.IsNaN(p)) continue;

                // Seed NaN states with current price on first valid bar.
                double l0p = double.IsNaN(L0) ? p : L0;
                double l1p = double.IsNaN(L1) ? p : L1;
                double l2p = double.IsNaN(L2) ? p : L2;
                double l3p = double.IsNaN(L3) ? p : L3;

                L0 = (1 - gamma) * p  + gamma * l0p;
                L1 = -gamma * L0 + l0p + gamma * l1p;
                L2 = -gamma * L1 + l1p + gamma * l2p;
                L3 = -gamma * L2 + l2p + gamma * l3p;

                double cu = (L0 > L1 ? L0 - L1 : 0) + (L1 > L2 ? L1 - L2 : 0) + (L2 > L3 ? L2 - L3 : 0);
                double cd = (L0 < L1 ? L1 - L0 : 0) + (L1 < L2 ? L2 - L1 : 0) + (L2 < L3 ? L3 - L2 : 0);
                double laRsi = (cu + cd < 1e-10) ? 0.5 : cu / (cu + cd);

                // Scale to WT range: 0.5 → 0, 0 → −35, 1 → +35
                r[i] = (laRsi - 0.5) * 70.0;
            }
            return r;
        }

        /// <summary>
        /// Stochastic RSI: applies the Stochastic formula to an RSI series, then smooths
        /// %K with SMA(<paramref name="kSmooth"/>) and %D with SMA(<paramref name="dSmooth"/>).
        /// Both outputs are normalised to ±35: <c>(value / 100 − 0.5) × 70</c>.
        /// </summary>
        public static (double[] K, double[] D) ComputeStochRsi(
            double[] rsiSrc, int stochPeriod, int kSmooth, int dSmooth)
        {
            int n = rsiSrc.Length;
            var raw = new double[n];
            Array.Fill(raw, double.NaN);

            for (int i = stochPeriod - 1; i < n; i++)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (int j = i - stochPeriod + 1; j <= i; j++)
                {
                    if (double.IsNaN(rsiSrc[j])) continue;
                    if (rsiSrc[j] < lo) lo = rsiSrc[j];
                    if (rsiSrc[j] > hi) hi = rsiSrc[j];
                }
                double range = hi - lo;
                raw[i] = range < 1e-10
                    ? 50.0
                    : (rsiSrc[i] - lo) / range * 100.0;
            }

            var kArr = Sma(raw, kSmooth);
            var dArr = Sma(kArr, dSmooth);

            // Normalise to ±35 (same scale as Laguerre RSI — subdued vs WT)
            for (int i = 0; i < n; i++)
            {
                if (!double.IsNaN(kArr[i])) kArr[i] = (kArr[i] / 100.0 - 0.5) * 70.0;
                if (!double.IsNaN(dArr[i])) dArr[i] = (dArr[i] / 100.0 - 0.5) * 70.0;
            }
            return (kArr, dArr);
        }

        /// <summary>
        /// Returns a copy of a sparse marker array with every non-NaN value moved forward by
        /// <paramref name="lag"/> bars — from the bar a pivot sits on to the bar it could first be
        /// confirmed. Markers whose confirmation bar falls past the end of the data are dropped:
        /// they could not have been acted on in-sample.
        ///
        /// <para>
        /// This is the shared form of the divergence look-ahead fix. A pivot at bar p is only known
        /// after seeing p+1..p+pivotBars, but the marker is drawn at p, so a backtest reading it
        /// enters at the exact pivot extreme with hindsight while live never sees the marker at the
        /// current bar at all — backtest and live disagree by construction. It lived as a private
        /// method on Cipher B for two months while Cipher A, which has the identical pivot loop,
        /// went on stamping divergences at the pivot bar. Anything with a symmetric pivot window
        /// should call this.
        /// </para>
        /// </summary>
        public static double[] ShiftMarkersForward(double[] src, int lag, int n)
        {
            var dst = new double[n];
            Array.Fill(dst, double.NaN);
            for (int i = 0; i < n && i < src.Length; i++)
            {
                if (double.IsNaN(src[i])) continue;
                int j = i + lag;
                if (j < n) dst[j] = src[i];
            }
            return dst;
        }

        /// <summary>
        /// Creates an array of length <paramref name="n"/> filled entirely with <see cref="double.NaN"/>.
        /// Convenience method used when initialising output arrays before a warmup period completes.
        /// </summary>
        public static double[] NanArray(int n)
        {
            var arr = new double[n];
            for (int i = 0; i < n; i++) arr[i] = double.NaN;
            return arr;
        }

        /// <summary>
        /// Median spacing between consecutive bars, in minutes — an indicator's answer to "what
        /// timeframe am I drawn on?" when nothing has told it. Returns 0 when the series is too
        /// short or carries no positive spacing at all; callers treat that as "adaptation off".
        ///
        /// <para>
        /// <b>Every delta in the series is sampled, deliberately.</b> The two detectors this
        /// replaced each took a fixed sample from the FRONT of the array — the first 11 deltas in
        /// one case, the first 100 in the other — which makes the answer a function of where the
        /// array happens to start. Scrolling back prepends two hundred older bars, the sample
        /// window slides onto different bars, and an indicator that had been tuned for "daily"
        /// re-tunes itself for "4-hour" on bars the user was already looking at. A median over the
        /// whole series has no such window: for a regularly spaced feed it is exactly the bar
        /// interval no matter which slice of it you hold, and weekend gaps and halts are a
        /// minority of the deltas, so they cannot move it.
        /// </para>
        ///
        /// <para>
        /// The remaining case it cannot be invariant for is a feed whose spacing genuinely changes
        /// across the range — older history stored at a coarser resolution than recent bars. There
        /// the median moves when enough of the coarse region is loaded, and it should: the series
        /// really is two timeframes stitched together.
        /// </para>
        /// </summary>
        public static double MedianBarIntervalMinutes(ReadOnlySpan<Ohlcv> data)
        {
            if (data.Length < 2) return 0.0;

            var deltas = new double[data.Length - 1];
            int filled = 0;
            for (int i = 1; i < data.Length; i++)
            {
                double mins = (data[i].Date - data[i - 1].Date).TotalMinutes;
                if (mins > 0) deltas[filled++] = mins;
            }
            if (filled == 0) return 0.0;

            Array.Sort(deltas, 0, filled);
            return deltas[filled / 2];
        }

        /// <summary>
        /// Returns (highest_high + lowest_low) / 2 over the <paramref name="period"/> bars ending at
        /// <paramref name="endIdx"/> (inclusive).  Used by Ichimoku and other midpoint-line indicators.
        /// </summary>
        public static double Midpoint(ReadOnlySpan<Ohlcv> data, int endIdx, int period)
        {
            int start = endIdx - period + 1;
            double hi = data[endIdx].High;
            double lo = data[endIdx].Low;
            for (int i = start; i <= endIdx; i++)
            {
                if (data[i].High > hi) hi = data[i].High;
                if (data[i].Low  < lo) lo = data[i].Low;
            }
            return (hi + lo) / 2.0;
        }

        /// <summary>
        /// Wilder True Range series. Returns NaN on the first bar (no prior close).
        /// </summary>
        public static double[] TrueRange(ReadOnlySpan<Ohlcv> data)
        {
            int n = data.Length;
            var r = new double[n];
            if (n == 0) return r;
            r[0] = double.NaN;
            for (int i = 1; i < n; i++)
            {
                double hl  = data[i].High - data[i].Low;
                double hpc = Math.Abs(data[i].High - data[i - 1].Close);
                double lpc = Math.Abs(data[i].Low  - data[i - 1].Close);
                r[i] = Math.Max(hl, Math.Max(hpc, lpc));
            }
            return r;
        }

        /// <summary>
        /// Wilder Average True Range (RMA-smoothed). Returns NaN for the first <paramref name="period"/> bars.
        /// </summary>
        public static double[] Atr(ReadOnlySpan<Ohlcv> data, int period)
        {
            int n = data.Length;
            var r = new double[n];
            Array.Fill(r, double.NaN);
            if (n <= period) return r;

            var tr = TrueRange(data);
            double sum = 0;
            for (int i = 1; i <= period; i++) sum += tr[i];
            double atr = sum / period;
            r[period] = atr;
            for (int i = period + 1; i < n; i++)
            {
                atr = (atr * (period - 1) + tr[i]) / period;
                r[i] = atr;
            }
            return r;
        }

        /// <summary>
        /// Wilder Average Directional Index (ADX). Returns NaN during warmup (first 2×period bars).
        /// Produces a non-directional trend-strength measure in 0..100; ADX &gt; 20 is typically
        /// interpreted as a directional trend, &lt; 20 as ranging.
        /// </summary>
        public static double[] Adx(ReadOnlySpan<Ohlcv> data, int period)
        {
            int n = data.Length;
            var r = new double[n];
            Array.Fill(r, double.NaN);
            if (n <= period * 2) return r;

            var tr    = new double[n];
            var plusDm  = new double[n];
            var minusDm = new double[n];
            tr[0] = plusDm[0] = minusDm[0] = 0;
            for (int i = 1; i < n; i++)
            {
                double up   = data[i].High - data[i - 1].High;
                double down = data[i - 1].Low - data[i].Low;
                plusDm[i]  = (up > down && up > 0)   ? up   : 0;
                minusDm[i] = (down > up && down > 0) ? down : 0;

                double hl  = data[i].High - data[i].Low;
                double hpc = Math.Abs(data[i].High - data[i - 1].Close);
                double lpc = Math.Abs(data[i].Low  - data[i - 1].Close);
                tr[i] = Math.Max(hl, Math.Max(hpc, lpc));
            }

            // Wilder-smoothed TR, +DM, -DM over period
            double trSum = 0, pdmSum = 0, mdmSum = 0;
            for (int i = 1; i <= period; i++)
            {
                trSum  += tr[i];
                pdmSum += plusDm[i];
                mdmSum += minusDm[i];
            }

            var dx = new double[n];
            Array.Fill(dx, double.NaN);
            for (int i = period + 1; i < n; i++)
            {
                trSum  = trSum  - trSum  / period + tr[i];
                pdmSum = pdmSum - pdmSum / period + plusDm[i];
                mdmSum = mdmSum - mdmSum / period + minusDm[i];
                if (trSum < 1e-10) continue;
                double plusDi  = 100.0 * pdmSum / trSum;
                double minusDi = 100.0 * mdmSum / trSum;
                double sumDi   = plusDi + minusDi;
                dx[i] = sumDi < 1e-10 ? 0.0 : 100.0 * Math.Abs(plusDi - minusDi) / sumDi;
            }

            // Wilder-smoothed DX → ADX
            double adxSum = 0;
            int adxStart = period * 2;
            int valid = 0;
            for (int i = period + 1; i <= adxStart && i < n; i++)
            {
                if (!double.IsNaN(dx[i])) { adxSum += dx[i]; valid++; }
            }
            if (valid == 0) return r;
            double adx = adxSum / valid;
            if (adxStart < n) r[adxStart] = adx;
            for (int i = adxStart + 1; i < n; i++)
            {
                if (double.IsNaN(dx[i])) { r[i] = r[i - 1]; continue; }
                adx = (adx * (period - 1) + dx[i]) / period;
                r[i] = adx;
            }
            return r;
        }

        /// <summary>
        /// Rolling VWAP deviation oscillator — approximation of Market Cipher's VWAP~ line.
        /// Computes the deviation of close from the N-bar volume-weighted average price,
        /// normalised by the rolling standard deviation and scaled to ±15.
        /// </summary>
        public static double[] RollingVwapOscillator(
            double[] hlc3, double[] close, double[] volume, int period)
        {
            int n = hlc3.Length;
            var result = new double[n];
            Array.Fill(result, double.NaN);

            for (int i = period - 1; i < n; i++)
            {
                double sumPV = 0, sumV = 0;
                for (int j = i - period + 1; j <= i; j++)
                {
                    sumPV += hlc3[j] * volume[j];
                    sumV  += volume[j];
                }
                double vwap = sumV < 1e-10 ? close[i] : sumPV / sumV;

                // Rolling standard deviation of close
                double mean = 0;
                for (int j = i - period + 1; j <= i; j++) mean += close[j];
                mean /= period;
                double variance = 0;
                for (int j = i - period + 1; j <= i; j++) variance += (close[j] - mean) * (close[j] - mean);
                double stdDev = Math.Sqrt(variance / period);

                result[i] = stdDev < 1e-10 ? 0 : (close[i] - vwap) / stdDev * 15.0;
            }
            return result;
        }
    }
}
