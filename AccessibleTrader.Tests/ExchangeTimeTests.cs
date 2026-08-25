using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the shared US-Eastern conversion used by the vendors that express
    /// wall-clock times in America/New_York with no offset (FMP intraday, Tradier
    /// timesales request windows). The bug this guards: treating an Eastern
    /// timestamp as UTC, leaving every intraday bar 4-5h off.
    /// </summary>
    public class ExchangeTimeTests
    {
        private static bool TzAvailable => ExchangeTime.UsEastern != TimeZoneInfo.Utc;

        [Fact]
        public void EdtSummer_IsUtcMinus4()
        {
            if (!TzAvailable) return; // no tz database on this host — nothing to assert
            // 2021-10-08 is EDT (UTC-4): 16:00 ET → 20:00 UTC.
            var utc = ExchangeTime.EasternWallClockToUtc(new DateTime(2021, 10, 8, 16, 0, 0));
            Assert.Equal(new DateTime(2021, 10, 8, 20, 0, 0, DateTimeKind.Utc), utc);
        }

        [Fact]
        public void EstWinter_IsUtcMinus5()
        {
            if (!TzAvailable) return;
            // 2021-01-08 is EST (UTC-5): 16:00 ET → 21:00 UTC.
            var utc = ExchangeTime.EasternWallClockToUtc(new DateTime(2021, 1, 8, 16, 0, 0));
            Assert.Equal(new DateTime(2021, 1, 8, 21, 0, 0, DateTimeKind.Utc), utc);
        }

        [Fact]
        public void RoundTripThroughEasternString_IsStable()
        {
            if (!TzAvailable) return;
            var utc = new DateTime(2021, 6, 1, 13, 30, 0, DateTimeKind.Utc); // 09:30 EDT
            var s = ExchangeTime.ToEasternString(utc, "yyyy-MM-dd HH:mm");
            Assert.Equal("2021-06-01 09:30", s);
        }
    }
}
