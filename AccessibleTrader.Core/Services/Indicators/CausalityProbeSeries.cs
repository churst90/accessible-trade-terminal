using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The synthetic price series the causality contract is proved against.
    ///
    /// <para>
    /// It lives in Core rather than in the test project because it now has two callers: the
    /// build-time sweep over every built-in provider (<c>IndicatorCausalityTests</c>), and the
    /// registration-time sweep over a user's compiled script
    /// (<see cref="CustomIndicatorCausalityProbe"/>). Two generators would drift, and the one that
    /// drifted would be the one nobody was watching — the same reason the provider list was pulled
    /// into a single fixture.
    /// </para>
    ///
    /// <para>
    /// Four flavours, and each earns its place. Deterministic (an xorshift seeded per flavour), so
    /// a failure reproduces exactly and the pinned blind-spot lists keep meaning what they say.
    /// </para>
    /// </summary>
    public static class CausalityProbeSeries
    {
        /// <summary>The length every pinned list in <c>IndicatorCausalityTests</c> was measured at.</summary>
        public const int DefaultLength = 400;

        /// <summary>
        /// Flavours 0–2 are hourly: a rolling swing series, a faster and more violent one, and a
        /// steady uptrend that alternates loud and quiet swings. The third earns its place — it
        /// caught SwingStructure reordering a bar that was both a pivot high and a pivot low.
        /// Flavour 3 is the irregular one; see <see cref="Stamp"/>.
        /// </summary>
        public static readonly int[] HourlyFlavours = { 0, 1, 2 };

        /// <summary>All four, including the irregularly spaced one.</summary>
        public static readonly int[] AllFlavours = { 0, 1, 2, 3 };

        /// <param name="length">Bars to generate. The generator is sequential, so a longer series is
        /// the same price path with more of it — the suffix sweep asks for a longer one so that the
        /// trailing windows of the widest built-in defaults (a 500-bar percentile rank) are full
        /// well before it starts comparing.</param>
        public static List<Ohlcv> Bars(int flavour, int length = DefaultLength)
        {
            var bars = new List<Ohlcv>(length);
            double price = 100;
            ulong s = flavour switch
            {
                0 => 0x9E3779B97F4A7C15UL,
                1 => 0xD1B54A32D192ED03UL,
                _ => 0xBF58476D1CE4E5B9UL,
            };
            double Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return (s % 10000) / 10000.0; }
            var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < length; i++)
            {
                // Flavour 2: a steady rise whose swings alternate loud and quiet. WaveTrend
                // normalises by its own trailing deviation, so a quiet stretch that follows a loud
                // one prints muted oscillator peaks while price keeps making higher highs — which
                // is a plain bearish divergence rather than an overbought one.
                double amp = (i % 80) < 40 ? 3.4 : 0.7;
                double drift = flavour switch
                {
                    0 => Math.Sin(i / 11.0) * 1.1 + Math.Sin(i / 37.0) * 2.4 + Math.Sin(i / 91.0) * 3.6,
                    1 => Math.Sin(i / 6.0) * 2.6 + Math.Sin(i / 23.0) * 1.2 + (i % 130 < 65 ? 0.9 : -1.1),
                    _ => 0.5 + Math.Sin(i / 9.0) * amp,
                };
                double shock = (Next() - 0.5) * (flavour == 1 ? 5.0 : 2.2);
                double open = price;
                price = Math.Max(1.0, price + drift + shock);
                double close = price;
                double hi = Math.Max(open, close) + Next() * 1.4;
                double lo = Math.Min(open, close) - Next() * 1.4;
                // Volume rises with the size of the move so turning points carry the confluence
                // volume that pivot detectors require before they will call a pivot.
                double vol = 1000 + Math.Abs(drift + shock) * 900 + Next() * 3000;
                bars.Add(new Ohlcv(Stamp(flavour, start, i), open, hi, lo, close, vol));
            }
            return bars;
        }

        /// <summary>
        /// Bar timestamps. Flavours 0–2 are exactly hourly, which is what every pinned list in
        /// <c>IndicatorCausalityTests</c> was measured against.
        ///
        /// <para>
        /// <b>Flavour 3 is not hourly, and that is its entire reason for existing.</b> Several
        /// indicators ask the data what timeframe they are drawn on and re-tune themselves from the
        /// answer — Cipher B swaps its whole parameter profile, the top/bottom detector scales its
        /// windows. Each of them used to answer that question from a fixed sample at the FRONT of
        /// the array, which is a scroll-back bug: prepend older bars and the sample lands on
        /// different bars. A perfectly regular series cannot see that bug, because every sample of
        /// it gives the same answer — the guard was green against a deliberately reintroduced
        /// version of the defect until this flavour existed.
        /// </para>
        ///
        /// <para>
        /// So flavour 3 puts a stitching artifact in the OLDEST ninety bars — the coarse, sparse
        /// history a provider hands back for the far end of a range — and runs cleanly hourly after
        /// it. A sample taken from the front reads four hours; the series is hourly. Drop the
        /// oldest bars, as a scroll-back does in reverse, and a front sample changes its mind while
        /// a whole-series median does not.
        /// </para>
        /// </summary>
        private static DateTime Stamp(int flavour, DateTime start, int i)
        {
            if (flavour != 3) return start.AddHours(i);

            const int artifact = 90;
            int inArtifact = Math.Min(i, artifact);
            int clean = i - inArtifact;
            // Two of every three of the oldest bars sit four hours apart, so the median of any
            // front-loaded sample is 240 minutes rather than the series' true 60.
            int artifactMinutes = inArtifact * 60 + (inArtifact - inArtifact / 3) * 180;
            return start.AddMinutes(artifactMinutes + clean * 60);
        }
    }
}
