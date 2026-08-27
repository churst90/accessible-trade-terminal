using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>How far price has stretched from established value, and in which direction.</summary>
    /// <param name="Tier">1..5, deeper = further from value. 0 means inside the innermost band.</param>
    /// <param name="BelowValue">True when price sits below the POC (the buy side).</param>
    /// <param name="DeviationVa">Signed deviation in value-area widths. Positive = above POC.</param>
    public readonly record struct ValueDeviation(int Tier, bool BelowValue, double DeviationVa)
    {
        public static ValueDeviation None => new(0, false, double.NaN);
    }

    /// <summary>
    /// Measures price against a rolling volume-profile Point of Control.
    ///
    /// <para>
    /// WHY THE POC. Every hand-drawn channel carries the bias of whoever drew it, which is why
    /// fitted line families look excellent in sample and collapse out of it. A POC is computed:
    /// it is the price at which the most volume actually changed hands over a defined window.
    /// Nobody chooses it, so deviation from it asks "how far is price from where business is
    /// being done" without an anchor to argue about.
    /// </para>
    ///
    /// <para>
    /// WHY THE SLOW WINDOW. Measured across 38 equities, sectors, metals and bonds over 348,000
    /// daily bars, anchoring on a slow profile beat anchoring on a fast one at every tier, and
    /// the deepest tier most of all (+1.83% vs +1.60% over five days). Scaling deviation by the
    /// GAP BETWEEN a fast and a slow POC — an appealing idea — was also tested and destroyed the
    /// gradient entirely, so it is deliberately not used.
    /// </para>
    ///
    /// <para>
    /// CAUSAL BY CONSTRUCTION. The profile at bar i is built from bars i-Window..i-1 only. A
    /// profile spanning the whole series would compute the POC partly from the bars whose
    /// behaviour it is supposed to anticipate. The window itself has to be independent of the
    /// array too — see the note on <see cref="IValueDeviationAnalyzer.Reference"/> for the two
    /// ways it was not.
    /// </para>
    /// </summary>
    public interface IValueDeviationAnalyzer
    {
        /// <summary>
        /// Per-bar deviation tiers. Bars before the window has filled return
        /// <see cref="ValueDeviation.None"/>.
        /// </summary>
        ValueDeviation[] Analyze(IReadOnlyList<Ohlcv> bars, int window, int tiers, double maxTierVa);

        /// <summary>The POC and value-area bounds per bar, for rendering the reference lines.</summary>
        (double[] Poc, double[] ValueHigh, double[] ValueLow) Reference(IReadOnlyList<Ohlcv> bars, int window);
    }

    /// <inheritdoc cref="IValueDeviationAnalyzer"/>
    public sealed class ValueDeviationAnalyzer : IValueDeviationAnalyzer
    {
        /// <summary>Price bins in the profile. 50 matches the chart's own volume-profile rendering.</summary>
        public const int BinCount = 50;

        /// <summary>Fraction of total volume that defines the value area, the standard 70%.</summary>
        private const double ValueAreaFraction = 0.70;

        /// <summary>
        /// Shortest profile that means anything. Below this the value area is a handful of bins and
        /// the deviation tiers are noise.
        /// </summary>
        public const int MinWindow = 40;

        public ValueDeviation[] Analyze(IReadOnlyList<Ohlcv> bars, int window, int tiers, double maxTierVa)
        {
            int n = bars.Count;
            var result = new ValueDeviation[n];
            for (int i = 0; i < n; i++) result[i] = ValueDeviation.None;
            if (n == 0 || tiers < 1) return result;

            var (poc, vaHigh, vaLow) = Reference(bars, window);

            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(poc[i]) || double.IsNaN(vaHigh[i]) || double.IsNaN(vaLow[i])) continue;
                double width = vaHigh[i] - vaLow[i];
                if (width <= 0) continue;

                double dev = (bars[i].Close - poc[i]) / width;
                // A collapsed value area (all volume in one bin) sends this into the tens or
                // hundreds; that is a profile artifact, not a stretched market.
                if (Math.Abs(dev) > 10) continue;

                double mag = Math.Abs(dev);
                double step = maxTierVa / tiers;
                int tier = (int)Math.Ceiling(mag / step);
                if (tier < 1) { result[i] = new ValueDeviation(0, dev < 0, dev); continue; }
                if (tier > tiers) tier = tiers;

                result[i] = new ValueDeviation(tier, dev < 0, dev);
            }

            return result;
        }

        /// <summary>
        /// THE WINDOW IS FIXED, AND EVERY BAR REBUILDS. Both halves are prepend-stability, and both
        /// were violations until 2026-08-27.
        ///
        /// <para>
        /// <b>The window.</b> It used to be <c>min(maxWindow, max(40, (barIndex + 1) / 3))</c> — a
        /// profile that grew with the bar's ARRAY INDEX and only reached the configured maximum at
        /// bar <c>3 * window - 1</c>, which is bar 719 at the default 240. Every bar left of that
        /// re-cut its profile the moment older bars arrived and its index moved. This is the same
        /// defect as the one-third-of-total-bar-count cap it replaced, one level down: the same
        /// bar, two answers, decided by how much history happened to be loaded around it. The only
        /// window that survives a scroll-back is one that depends on nothing but the parameter.
        /// </para>
        ///
        /// <para>
        /// <b>The rebuild.</b> The profile used to be rebuilt on <c>i % 5 == 0</c> and cached
        /// between, so the PHASE of the cache was pinned to array index 0 — prepend a number of
        /// bars that is not a multiple of five and every cached bar reads a profile built at a
        /// different bar. Rebuilding every bar is ~336k bar-visits on a 1400-bar series at the
        /// default window; the "makes the indicator quadratic" note that justified the cache was
        /// written about the UNCAPPED window, not about this.
        /// </para>
        ///
        /// <para>
        /// <b>What it costs.</b> A chart with fewer bars than the window shows nothing, and one
        /// with a few more shows nothing at its left edge. That is the honest outcome and the
        /// point of the change: scrolling back now ADDS readings instead of altering existing
        /// ones. Silence with no explanation is not acceptable here, so
        /// <c>ValueDeviationProvider.GetDetailFact</c> says how many bars the profile needs and
        /// how many the chart has.
        /// </para>
        /// </summary>
        public (double[] Poc, double[] ValueHigh, double[] ValueLow) Reference(
            IReadOnlyList<Ohlcv> bars, int window)
        {
            int n = bars.Count;
            var poc = new double[n];
            var hi = new double[n];
            var lo = new double[n];
            Array.Fill(poc, double.NaN);
            Array.Fill(hi, double.NaN);
            Array.Fill(lo, double.NaN);
            if (n == 0 || window < 10) return (poc, hi, lo);

            // A bar's profile is the `window` bars immediately behind it — no more, no fewer, and
            // never a partial one. `i >= window` is the whole warmup rule, and it is expressed in
            // the parameter only, so a bar's reading is a property of the bar and not of where it
            // happens to sit in the loaded array.
            for (int i = window; i < n; i++)
            {
                var (p, h, l) = BuildProfile(bars, i - window, i);
                poc[i] = p; hi[i] = h; lo[i] = l;
            }
            return (poc, hi, lo);
        }

        /// <summary>
        /// Volume-at-price over [from, toExclusive), returning the POC and the value-area bounds.
        /// Each bar's volume is spread evenly across the bins its range covers, which is the same
        /// approximation the chart's profile renderer uses — exact tick data would be better but
        /// is not available from an OHLCV feed.
        /// </summary>
        private static (double Poc, double High, double Low) BuildProfile(
            IReadOnlyList<Ohlcv> bars, int from, int toExclusive)
        {
            double min = double.MaxValue, max = double.MinValue;
            for (int i = from; i < toExclusive; i++)
            {
                if (bars[i].Low < min) min = bars[i].Low;
                if (bars[i].High > max) max = bars[i].High;
            }
            if (max <= min || double.IsInfinity(min) || double.IsInfinity(max))
                return (double.NaN, double.NaN, double.NaN);

            double binSize = (max - min) / BinCount;
            if (binSize <= 0) return (double.NaN, double.NaN, double.NaN);

            var volume = new double[BinCount];
            for (int i = from; i < toExclusive; i++)
            {
                int lo = Math.Clamp((int)((bars[i].Low - min) / binSize), 0, BinCount - 1);
                int hi = Math.Clamp((int)((bars[i].High - min) / binSize), 0, BinCount - 1);
                int span = hi - lo + 1;
                double share = bars[i].Volume / span;
                for (int b = lo; b <= hi; b++) volume[b] += share;
            }

            int pocBin = 0;
            double total = 0;
            for (int b = 0; b < BinCount; b++)
            {
                total += volume[b];
                if (volume[b] > volume[pocBin]) pocBin = b;
            }
            if (total <= 0) return (double.NaN, double.NaN, double.NaN);

            // Grow outward from the POC, always taking the richer neighbour, until 70% is covered.
            double target = total * ValueAreaFraction;
            double acc = volume[pocBin];
            int lowBin = pocBin, highBin = pocBin;
            while (acc < target && (lowBin > 0 || highBin < BinCount - 1))
            {
                double below = lowBin > 0 ? volume[lowBin - 1] : -1;
                double above = highBin < BinCount - 1 ? volume[highBin + 1] : -1;
                if (above >= below) { highBin++; acc += volume[highBin]; }
                else { lowBin--; acc += volume[lowBin]; }
            }

            // DEGENERATE PROFILE GUARD. If volume is spread near-uniformly across the range then
            // the "peak" bin is just whichever one won a tie on the way through the loop, and any
            // deviation measured from it is an artifact of that tie-break. A genuine point of
            // control stands out from the average bin; if it does not, report nothing.
            //
            // Testing whether the VALUE AREA is wide does NOT catch this: growing outward from
            // bin 0 of a uniform profile fills 70% in 35 of 50 bins and looks perfectly normal.
            // The peak-prominence test is the one that works.
            double meanBin = total / BinCount;
            if (volume[pocBin] < meanBin * 1.15) return (double.NaN, double.NaN, double.NaN);

            return (min + (pocBin + 0.5) * binSize,
                    min + (highBin + 1) * binSize,
                    min + lowBin * binSize);
        }
    }
}
