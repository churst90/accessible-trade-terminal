namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// Causal rolling quantile helper used by adaptive-threshold indicators.
    ///
    /// Computes, for each bar, the p-th percentile of the previous N values using a
    /// straightforward sorted-window approach. NaN inputs are skipped without advancing
    /// the window position — this matches the convention used by Ema/Sma/Rsi helpers.
    ///
    /// Look-ahead safety: at bar i, only values from indices [max(0, i-N+1) .. i] are
    /// included. The same values that would have been visible at bar i in a live feed.
    /// Do not pass the whole series and sample in-sample — that would be look-ahead bias.
    ///
    /// Cost is O(N log N) per bar (insert + sort); acceptable for oscillator-sized N
    /// (typically 200–500) and the warmup overhead is dominated by the oscillator itself.
    /// If profiling shows this as a hot spot, upgrade to a P² estimator or sorted deque.
    /// </summary>
    public static class RollingQuantile
    {
        /// <summary>
        /// Computes a causal rolling quantile for each bar of <paramref name="src"/>.
        /// </summary>
        /// <param name="src">Input series. NaN values are skipped.</param>
        /// <param name="window">Lookback length in bars.</param>
        /// <param name="probability">Target quantile in [0, 1] (e.g., 0.95 for 95th percentile).</param>
        /// <param name="warmupMin">Minimum non-NaN samples required before a quantile is emitted.</param>
        /// <returns>Array of same length as <paramref name="src"/>; NaN before warmup.</returns>
        public static double[] Compute(double[] src, int window, double probability, int warmupMin)
        {
            int n = src.Length;
            var result = new double[n];
            Array.Fill(result, double.NaN);
            if (n == 0 || window < 2 || warmupMin < 1)
                return result;

            // Buffer of non-NaN values inside the window.
            var buf = new double[Math.Min(window, n)];
            var sorted = new double[buf.Length];

            for (int i = 0; i < n; i++)
            {
                int start = Math.Max(0, i - window + 1);
                int count = 0;
                for (int j = start; j <= i && count < buf.Length; j++)
                {
                    double v = src[j];
                    if (!double.IsNaN(v))
                        buf[count++] = v;
                }
                if (count < warmupMin) continue;

                Array.Copy(buf, sorted, count);
                Array.Sort(sorted, 0, count);
                result[i] = Percentile(sorted, count, probability);
            }
            return result;
        }

        /// <summary>
        /// Linear-interpolation percentile over a sorted buffer of <paramref name="count"/> values.
        /// </summary>
        public static double Percentile(double[] sorted, int count, double probability)
        {
            if (count <= 0) return double.NaN;
            if (count == 1) return sorted[0];
            double rank = probability * (count - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            if (lo == hi) return sorted[lo];
            double frac = rank - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
        }
    }
}
