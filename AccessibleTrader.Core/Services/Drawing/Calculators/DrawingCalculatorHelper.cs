using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing.Calculators
{
    internal static class DrawingCalculatorHelper
    {
        public static int FindIndex<T>(IReadOnlyList<T> list, Predicate<T> match)
        {
            for (int i = 0; i < list.Count; i++)
                if (match(list[i])) return i;
            return -1;
        }

        /// <summary>
        /// The straight line through two anchors, sampled once per bar, NaN where the line
        /// does not reach.
        ///
        /// <para><b><paramref name="extL"/> and <paramref name="extR"/> were accepted and
        /// never read.</b> Until 2026-09-03 this filled EVERY bar of the array, so a trend
        /// line drawn between bar 200 and bar 300 had a value at bar 0 — it was drawn across
        /// the whole chart and, worse for a speech user, SPOKEN there: arrowing to any bar in
        /// the chart read a price off a line that does not exist at that bar, and playback
        /// would have sonified a five-hundred-bar ramp for a hundred-bar line. Cody reported
        /// it as "it sounds like the trend line extends further left than I placed the start
        /// marker", which is exactly what it did.</para>
        ///
        /// <para>The extend flags are the drawing's own answer to how far the line runs. A
        /// keyboard- or mouse-placed drawing is created with <c>ExtendRight = true</c> and
        /// <c>ExtendLeft</c> false, which is the trader's convention: a trend line projects
        /// forward into the space where price has not happened yet and stops dead at the
        /// point it was anchored from.</para>
        ///
        /// <para>The loop is bounded by the ARRAY, not by the anchors: an anchor past the
        /// last bar resolves to a projected index beyond <c>count</c>
        /// (<see cref="ResolveAnchorIndex"/>), and using it as a loop bound would run off the
        /// end. Both anchors in future space leave the array entirely NaN, which is right —
        /// there is no bar the line crosses.</para>
        /// </summary>
        public static double[] CalculateLinearPoints(
            DateTime d1, double p1, DateTime d2, double p2,
            bool extL, bool extR, IReadOnlyList<Ohlcv> chartData)
        {
            int count = chartData.Count;
            var results = new double[count];
            Array.Fill(results, double.NaN);

            int i1 = ResolveAnchorIndex(chartData, d1);
            int i2 = ResolveAnchorIndex(chartData, d2);

            if (i1 == -1 || i2 == -1 || i1 == i2) return results;

            double m = (p2 - p1) / (i2 - i1);
            double b = p1 - (m * i1);

            int from = extL ? 0 : Math.Max(0, Math.Min(i1, i2));
            int to   = extR ? count - 1 : Math.Min(count - 1, Math.Max(i1, i2));

            for (int i = from; i <= to; i++)
                results[i] = (m * i) + b;
            return results;
        }

        /// <summary>
        /// Resolve a drawing anchor date to a (possibly synthetic) data index. Dates that
        /// fall inside the chart range map via <see cref="FindIndex"/> as before; dates
        /// beyond <c>chartData[^1].Date</c> are projected forward using the median inter-bar
        /// delta (mirrors <c>DrawingInteractionManager.ProjectFutureDate</c>). This lets
        /// trendline-style drawings anchor into the reserved right-margin without breaking
        /// the slope calculation when one anchor is past the last real bar.
        /// </summary>
        internal static int ResolveAnchorIndex(IReadOnlyList<Ohlcv> chartData, DateTime d)
        {
            if (chartData == null || chartData.Count == 0) return -1;
            int idx = FindIndex(chartData, bar => bar.Date >= d);
            if (idx != -1) return idx;

            // Date is strictly after every bar → treat as future-space anchor.
            TimeSpan step;
            int n = chartData.Count;
            if (n >= 3)
            {
                int samples = Math.Min(8, n - 1);
                var deltas = new TimeSpan[samples];
                for (int k = 0; k < samples; k++)
                {
                    int hi = n - 1 - k;
                    deltas[k] = chartData[hi].Date - chartData[hi - 1].Date;
                }
                Array.Sort(deltas);
                step = deltas[samples / 2];
            }
            else if (n == 2)
            {
                step = chartData[1].Date - chartData[0].Date;
            }
            else
            {
                step = TimeSpan.FromMinutes(1);
            }
            if (step.Ticks <= 0) step = TimeSpan.FromMinutes(1);

            long offsetTicks = (d - chartData[^1].Date).Ticks;
            if (offsetTicks <= 0) return n - 1;
            int offsetBars = (int)(offsetTicks / step.Ticks);
            if (offsetBars < 1) offsetBars = 1;
            return (n - 1) + offsetBars;
        }
    }
}
