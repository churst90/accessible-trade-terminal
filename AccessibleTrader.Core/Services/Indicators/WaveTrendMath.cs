using AccessibleTrader.Sdk.Indicators;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// LazyBear's WaveTrend and the range stochastic, as fitted to Market Cipher B on 2026-10-17
    /// (hlc3 source, channel 9, average 12, slow wave = SMA 3 of the fast wave). Cipher A and Cipher B
    /// use this single implementation so their WaveTrend arithmetic cannot drift apart.
    ///
    /// <para>Lambert's CCI constant is 0.015, which puts about 70–80% of channel-index values inside
    /// ±100. The WaveTrend levels (±53 / ±60) are calibrated against it, so neither changes alone.</para>
    /// </summary>
    public static class WaveTrendMath
    {
        /// <summary>
        /// The fast wave (WT1) and the slow wave (WT2) of <paramref name="src"/>. WT1 is an
        /// <paramref name="average"/>-bar EMA of Lambert's channel index, using an
        /// <paramref name="channel"/>-bar EMA and mean deviation; WT2 is a signal-period SMA of WT1.
        /// NaN during warm-up and wherever the channel deviation is zero. Cipher B also uses this
        /// method for its multiplied-period anchor pair.
        /// </summary>
        public static (double[] Wt1, double[] Wt2) Compute(double[] src, int channel, int average, int signal)
        {
            int n = src.Length;
            var esa = IndicatorMath.Ema(src, channel);
            var absDiff = new double[n];
            for (int i = 0; i < n; i++)
                absDiff[i] = double.IsNaN(esa[i]) ? double.NaN : Math.Abs(src[i] - esa[i]);
            var d = IndicatorMath.Ema(absDiff, channel);
            var ci = new double[n];
            for (int i = 0; i < n; i++)
                ci[i] = (double.IsNaN(d[i]) || d[i] < 1e-10) ? double.NaN : (src[i] - esa[i]) / (0.015 * d[i]);
            var wt1 = IndicatorMath.Ema(ci, average);
            var wt2 = IndicatorMath.Sma(wt1, Math.Max(1, signal));
            return (wt1, wt2);
        }

        /// <summary>
        /// %K of a stochastic of <paramref name="src"/> against the highest high and lowest low of
        /// <paramref name="length"/> bars, smoothed by an SMA of <paramref name="smooth"/>. 0–100;
        /// NaN until <paramref name="length"/> bars exist.
        /// </summary>
        public static double[] SmoothedStochastic(double[] src, double[] high, double[] low, int length, int smooth)
        {
            int n = src.Length;
            var k = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i < length - 1) { k[i] = double.NaN; continue; }
                double hh = double.MinValue, ll = double.MaxValue;
                for (int j = i - length + 1; j <= i; j++) { if (high[j] > hh) hh = high[j]; if (low[j] < ll) ll = low[j]; }
                k[i] = hh - ll < 1e-12 ? 50.0 : 100.0 * (src[i] - ll) / (hh - ll);
            }
            return IndicatorMath.Sma(k, smooth);
        }

        /// <summary>Pine's <c>ta.crossover</c>: <paramref name="a"/> above <paramref name="b"/> now, at or below it on the bar before.</summary>
        public static bool CrossedOver(double[] a, double[] b, int i) =>
            i > 0 && Valid(a[i], b[i], a[i - 1], b[i - 1]) && a[i] > b[i] && a[i - 1] <= b[i - 1];

        /// <summary>Pine's <c>ta.crossunder</c>: <paramref name="a"/> below <paramref name="b"/> now, at or above it on the bar before.</summary>
        public static bool CrossedUnder(double[] a, double[] b, int i) =>
            i > 0 && Valid(a[i], b[i], a[i - 1], b[i - 1]) && a[i] < b[i] && a[i - 1] >= b[i - 1];

        private static bool Valid(double a, double b, double c, double d) =>
            !double.IsNaN(a) && !double.IsNaN(b) && !double.IsNaN(c) && !double.IsNaN(d);
    }
}
