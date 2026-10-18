using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A COT value is visible from the Friday it was published, not the Tuesday it describes.</b>
    ///
    /// <para>
    /// Defect demonstrated 2026-10-18. CFTC positions are as of Tuesday and released Friday
    /// afternoon. The app's CFTC plugin stamps each value at the release date (report + 3 days).
    /// The lab's <c>cftc-cot</c> downloader stamped the REPORT date, and
    /// <see cref="SnapshottingCrossSeriesCache"/> served those stamps as they were — so in the
    /// lab, every COT read (CFTC_COT, and the app's own COT_POSITIONING, which reads the same
    /// files: bitcoin_cot, gold_cot, euro_fx_cot) saw each week's positioning three days before
    /// anyone could. The snapshot files on disk are not rewritten; the cache moves a report-dated
    /// point to its release date as it loads it.
    /// </para>
    /// </summary>
    public class CotReleaseDateTests
    {
        private static long Ms(DateTime d) => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

        private static IReadOnlyList<(long Ts, double Value)> Load(string provider, string symbol, params (DateTime At, double V)[] points)
        {
            var snap = new CrossSeriesSnapshot
            {
                Provider = provider, Symbol = symbol, Timeframe = "1w",
                Points = points.Select(p => new TimedValue { Ts = Ms(p.At), Value = p.V }).ToList(),
                PointCount = points.Length,
            };
            string dir = TestTemp.NewPath("at-cot-test-");
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(snap.FilePath(dir), Newtonsoft.Json.JsonConvert.SerializeObject(snap));
                return new SnapshottingCrossSeriesCache(dir).GetOrFetch(new CrossSeriesRequest("Derivatives", provider, symbol, "1w", 1));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void A_report_dated_COT_point_is_not_visible_before_its_Friday_release()
        {
            // Tue 3 Mar 2026 positions (released Fri 6 Mar) and Tue 10 Mar (released Fri 13 Mar).
            var ticks = Load("CFTC", "gold_COT", (new DateTime(2026, 3, 3), 10.0), (new DateTime(2026, 3, 10), 20.0));

            var days = Enumerable.Range(0, 14).Select(i => new DateTime(2026, 3, 2).AddDays(i))
                .Select(d => new Ohlcv(d, 1, 1, 1, 1, 0)).ToArray();
            var seen = new double[days.Length];
            Array.Fill(seen, double.NaN);
            CrossSeriesForwardFill.Fill(ticks, days, seen);

            double On(int month, int day) => seen[Array.FindIndex(days, b => b.Date == new DateTime(2026, month, day))];
            Assert.True(double.IsNaN(On(3, 3)), "Tue 3 Mar: the 3 Mar report is not out yet");
            Assert.True(double.IsNaN(On(3, 5)), "Thu 5 Mar: still not out");
            Assert.Equal(10.0, On(3, 6));          // Fri 6 Mar: released
            Assert.Equal(10.0, On(3, 12));         // Thu 12 Mar: the 10 Mar report is not out yet
            Assert.Equal(20.0, On(3, 13));         // Fri 13 Mar: released
        }

        [Fact]
        public void A_holiday_week_report_dated_Monday_is_released_three_days_later_too()
        {
            var ticks = Load("CFTC", "gold_COT", (new DateTime(2025, 12, 29), 5.0));   // a Monday
            Assert.Equal(Ms(new DateTime(2026, 1, 1)), Assert.Single(ticks).Ts);
        }

        [Fact]
        public void A_file_already_stamped_at_release_is_not_lagged_twice()
        {
            // The app's CFTC plugin writes release dates (Fridays; Thursday after a Monday report).
            var ticks = Load("CFTC", "SP500_COT", (new DateTime(2026, 3, 6, 5, 0, 0), 1.0), (new DateTime(2026, 1, 1), 2.0));
            Assert.Equal(new[] { Ms(new DateTime(2026, 1, 1)), Ms(new DateTime(2026, 3, 6, 5, 0, 0)) }, ticks.Select(t => t.Ts));
        }

        [Fact]
        public void Only_CFTC_series_are_moved()
        {
            // A Tuesday point in any other feed (funding, open interest) is already its own time.
            var ticks = Load("BinanceVision", "btcusdt_oi", (new DateTime(2026, 3, 3), 7.0));
            Assert.Equal(Ms(new DateTime(2026, 3, 3)), Assert.Single(ticks).Ts);
        }
    }
}
