using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// First coverage for <see cref="PivotLevelsProvider"/> — classic floor-trader pivots, the
    /// Camarilla levels, and the <c>Pivot Zone</c> component strategies gate entries on.
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// A2d/D11: collapsing <c>atR &amp;&amp; !atS ? 1 : atS &amp;&amp; !atR ? -1 : 0</c> to
    /// <c>atR ? 1 : atS ? -1 : 0</c> left the full suite green. The type census says why —
    /// <c>PivotLevelsProvider</c> was named in neither test project, so an indicator that
    /// publishes seven support/resistance lines and a discrete zone flag had never been asked
    /// for a single value.
    /// </para>
    ///
    /// <para>
    /// The zone flag is the part that gets spoken and gated on: +1 means "price is at
    /// resistance", -1 means "at support". The conjunction exists because the two can be true at
    /// once — a wide tolerance, or a compressed session, puts R1 and Cam L3 inside the same band
    /// — and when they are, the honest answer is neither. Deleting it makes every such bar
    /// announce resistance, and a long gated on support would read a bar at both as a long into
    /// resistance.
    /// </para>
    ///
    /// <para>
    /// This is the A2c rule applied deliberately: <b>a guard over two conditions is untested
    /// until a fixture makes them disagree.</b> The zone test below runs the SAME bars twice and
    /// changes only the tolerance, so the two conditions agree in one run and disagree in the
    /// other, and the answer has to change.
    /// </para>
    /// </summary>
    public class PivotLevelsProviderTests
    {
        private static readonly PivotLevelsProvider Provider = new();

        /// <summary>
        /// Six hourly bars on 05 Jan with high 110, low 90, close 100 — so the pivots for the
        /// next day are round numbers: PP 100, R1 110, S1 90, R2 120, S2 80, R3 130, S3 70,
        /// Cam H3 105.5, H4 111, L3 94.5, L4 89. Then <paramref name="day2Bars"/> flat bars on
        /// 06 Jan at <paramref name="day2Close"/>, narrow enough that ATR settles near 0.2.
        /// </summary>
        private static Ohlcv[] Bars(double day2Close, int day2Bars = 12)
        {
            var bars = new List<Ohlcv>
            {
                new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100, 105, 95, 100, 1),
                new(new DateTime(2026, 1, 5, 1, 0, 0, DateTimeKind.Utc), 100, 110, 98, 105, 1),
                new(new DateTime(2026, 1, 5, 2, 0, 0, DateTimeKind.Utc), 105, 106, 90,  95, 1),
                new(new DateTime(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc),  95, 100, 94,  98, 1),
                new(new DateTime(2026, 1, 5, 4, 0, 0, DateTimeKind.Utc),  98, 102, 96, 100, 1),
                new(new DateTime(2026, 1, 5, 5, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1),
            };
            for (int i = 0; i < day2Bars; i++)
            {
                bars.Add(new Ohlcv(new DateTime(2026, 1, 6, i, 0, 0, DateTimeKind.Utc),
                                   day2Close, day2Close + 0.1, day2Close - 0.1, day2Close, 1));
            }
            return bars.ToArray();
        }

        private static Dictionary<string, double[]> Run(Ohlcv[] bars, double zoneAtrTolerance)
        {
            var rd = new Dictionary<string, double[]>();
            var buf = new IndicatorResultBuffer(rd, bars.Length);
            Provider.Calculate(PivotLevelsProvider.Code, bars.AsSpan(), new Dictionary<string, object>
            {
                { "Period", "Daily" },
                { "ZoneAtrTolerance", zoneAtrTolerance },
                { "AtrPeriod", 2 },
            }, buf);
            return rd;
        }

        [Fact]
        public void ThePriorSessionsHlcSetsEveryLevelForTheNextSession()
        {
            var bars = Bars(day2Close: 100);
            var r = Run(bars, zoneAtrTolerance: 1.0);
            int last = bars.Length - 1;

            Assert.Equal(100.0, r[PivotLevelsProvider.CompPP][last], 9);
            Assert.Equal(110.0, r[PivotLevelsProvider.CompR1][last], 9);
            Assert.Equal(120.0, r[PivotLevelsProvider.CompR2][last], 9);
            Assert.Equal(130.0, r[PivotLevelsProvider.CompR3][last], 9);
            Assert.Equal(90.0,  r[PivotLevelsProvider.CompS1][last], 9);
            Assert.Equal(80.0,  r[PivotLevelsProvider.CompS2][last], 9);
            Assert.Equal(70.0,  r[PivotLevelsProvider.CompS3][last], 9);
            Assert.Equal(105.5, r[PivotLevelsProvider.CompCamH3][last], 9);
            Assert.Equal(111.0, r[PivotLevelsProvider.CompCamH4][last], 9);
            Assert.Equal(94.5,  r[PivotLevelsProvider.CompCamL3][last], 9);
            Assert.Equal(89.0,  r[PivotLevelsProvider.CompCamL4][last], 9);
        }

        /// <summary>
        /// Weekly pivots hold for the whole ISO week, New Year included. The week key was
        /// <c>Calendar.GetWeekOfYear(FirstFourDayWeek, Monday)</c> with the calendar year, which is
        /// not ISO 8601 at a year's edge: Tuesday 31 Dec 2024 is week 53 of 2024 there and Wednesday
        /// 1 Jan 2025 week 1 of 2025, so the week "changed" on a Wednesday and the second half of
        /// the week's pivots came from its own first two days. Every year has such a seam.
        /// </summary>
        [Fact]
        public void WeeklyPivotsHoldForTheWholeIsoWeekAcrossNewYear()
        {
            var bars = new List<Ohlcv>();
            var t = new DateTime(2024, 12, 23, 0, 0, 0, DateTimeKind.Utc);   // a Monday
            for (int h = 0; h < 14 * 24; h += 6, t = t.AddHours(6))
            {
                double c = 100 + h / 6.0;   // a trend, so no two spans share a high, low and close
                bars.Add(new Ohlcv(t, c, c + 1, c - 1, c, 1));
            }
            var rd = new Dictionary<string, double[]>();
            Provider.Calculate(PivotLevelsProvider.Code, bars.ToArray().AsSpan(), new Dictionary<string, object>
            {
                { "Period", "Weekly" }, { "ZoneAtrTolerance", 0.5 }, { "AtrPeriod", 2 },
            }, new IndicatorResultBuffer(rd, bars.Count));

            var week = Enumerable.Range(0, bars.Count)
                .Where(i => bars[i].Date >= new DateTime(2024, 12, 30, 0, 0, 0, DateTimeKind.Utc)
                         && bars[i].Date < new DateTime(2025, 1, 6, 0, 0, 0, DateTimeKind.Utc))
                .Select(i => rd[PivotLevelsProvider.CompPP][i]).ToList();
            Assert.Equal(28, week.Count);
            Assert.False(double.IsNaN(week[0]));
            Assert.All(week, v => Assert.Equal(week[0], v));
        }

        [Fact]
        public void TheFirstSessionHasNoPriorSessionSoItPublishesNothing()
        {
            var bars = Bars(day2Close: 100);
            var r = Run(bars, zoneAtrTolerance: 1.0);

            // Bars 0..5 are the first day: there is no prior HLC to compute from, and a pivot
            // invented from the session it is meant to precede is look-ahead wearing a level.
            for (int i = 0; i < 6; i++)
                Assert.True(double.IsNaN(r[PivotLevelsProvider.CompPP][i]), $"bar {i} published a pivot");
        }

        [Fact]
        public void PriceSittingOnResistanceIsPlusOneAndOnSupportIsMinusOne()
        {
            var atR = Bars(day2Close: 110);   // exactly R1
            var atS = Bars(day2Close: 90);    // exactly S1

            Assert.Equal(1.0,  Run(atR, zoneAtrTolerance: 1.0)[PivotLevelsProvider.CompZone][^1], 9);
            Assert.Equal(-1.0, Run(atS, zoneAtrTolerance: 1.0)[PivotLevelsProvider.CompZone][^1], 9);

            // Mid-range, far from every level: neither.
            Assert.Equal(0.0, Run(Bars(day2Close: 100), zoneAtrTolerance: 1.0)[PivotLevelsProvider.CompZone][^1], 9);
        }

        /// <summary>
        /// The disagreement fixture. Same bars, same price sitting exactly on R1 — only the
        /// tolerance changes. Narrow, and R1 is the only level in reach, so the answer is
        /// resistance. Wide enough to also swallow Cam L3 fifteen and a half points below, and
        /// the two conditions now disagree: price is "at" a resistance AND "at" a support, which
        /// is not a resistance reading, it is no reading at all.
        /// </summary>
        [Fact]
        public void APriceInsideBothAResistanceAndASupportZoneIsNeither()
        {
            var bars = Bars(day2Close: 110);

            Assert.Equal(1.0, Run(bars, zoneAtrTolerance: 1.0)[PivotLevelsProvider.CompZone][^1], 9);
            Assert.Equal(0.0, Run(bars, zoneAtrTolerance: 200.0)[PivotLevelsProvider.CompZone][^1], 9);
        }
    }
}
