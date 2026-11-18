using AccessibleTrader.Core.Services.MarketPulse;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Tests;

/// <summary>
/// The arithmetic behind one pulse item: the stated start, the last price, the closed rules and
/// the TNX ten-times quirk. Every test pins a clock, and none touches a network.
/// </summary>
public class MarketPulseMathTests
{
    private static readonly WatchlistEntry Sp =
        new("Fmp", "^GSPC", MarketType.Index, "Spot");

    private static readonly WatchlistEntry Btc =
        new("Coinbase", "BTC/USD", MarketType.Crypto, "Spot");

    private static readonly WatchlistEntry TenYear =
        new("Fmp", "TNX", MarketType.Index, "Spot");

    private static readonly WatchlistEntry EurUsd =
        new("Oanda", "EUR/USD", MarketType.Forex, "Spot");

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static Ohlcv Bar(DateTime date, double open, double close)
        => new(date, open, open, close, close, 1);

    private static MarketPulseQuote Quote(
        WatchlistEntry entry,
        IReadOnlyList<Ohlcv> daily,
        IReadOnlyList<Ohlcv> intraday,
        DateTime now,
        PulseCryptoStart start = PulseCryptoStart.MidnightUtc)
        => MarketPulseMath.Quote(entry, daily, intraday, now, start);

    private const double Tolerance = 1e-9;

    [Fact]
    public void CryptoMidnightReferenceIsTodaysDailyOpen()
    {
        var now = Utc(2026, 11, 18, 13, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 100, 105),
            Bar(Utc(2026, 11, 18), 110, 120),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 12, 59), 124, 125) };

        var quote = Quote(Btc, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal("Bitcoin", quote.Name);
        Assert.Null(quote.MinutesOld);
        Assert.Equal((125d - 110d) / 110d * 100d, quote.Change, Tolerance);
    }

    [Fact]
    public void CryptoMidnightWithNoTodaysBarUsesTheNewestClose()
    {
        var now = Utc(2026, 11, 18, 13, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 16), 90, 95),
            Bar(Utc(2026, 11, 17), 100, 110),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 12, 59), 124, 125) };

        var quote = Quote(Btc, daily, intraday, now);

        // The newest daily close is the reference. Nothing has moved since that close, so a
        // later 1m print is measured against it.
        Assert.Equal((125d - 110d) / 110d * 100d, quote.Change, Tolerance);
    }

    [Fact]
    public void CryptoLast24HoursUsesTheOneHourBarEndingNearestTheTarget()
    {
        var now = Utc(2026, 11, 18, 12, 0);
        var bars = new[]
        {
            Bar(Utc(2026, 11, 17, 10), 98, 100),
            Bar(Utc(2026, 11, 17, 11), 101, 102),  // ends exactly at now - 24h
            Bar(Utc(2026, 11, 17, 12), 180, 200),  // a decoy an hour further away
            Bar(Utc(2026, 11, 18, 11), 110, 111),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 11, 59), 111, 112) };

        var quote = Quote(Btc, bars, intraday, now, PulseCryptoStart.Last24Hours);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal((112d - 102d) / 102d * 100d, quote.Change, Tolerance);
    }

    [Fact]
    public void StockReferenceIsThePreviousSessionClose()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 99, 100),
            Bar(Utc(2026, 11, 18), 101, 102),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 59), 102, 103) };

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal(3d, quote.Change, Tolerance);
    }

    [Fact]
    public void StockClosedOnSaturdayReportsFridaysChangeWithFridaysDate()
    {
        var now = Utc(2026, 11, 21, 14, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 19), 98, 100),
            Bar(Utc(2026, 11, 20), 99, 97),
        };

        var quote = Quote(Sp, daily, Array.Empty<Ohlcv>(), now);

        Assert.Equal(MarketPulseKind.Closed, quote.Kind);
        Assert.Equal(-3d, quote.Change, Tolerance);
        Assert.Equal(new DateOnly(2026, 11, 20), quote.SessionDate);
    }

    [Fact]
    public void HolidayDuringHoursWithAStaleMinuteBarIsClosed()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 99, 100),
            Bar(Utc(2026, 11, 18), 101, 102),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 19), 102, 103) }; // ends 14:20, 40m old

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Closed, quote.Kind);
        Assert.Equal(2d, quote.Change, Tolerance);
        Assert.Equal(new DateOnly(2026, 11, 18), quote.SessionDate);
    }

    [Fact]
    public void OpenWithAFourMinuteOldMinuteBarHasMinutesOldFour()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 99, 100),
            Bar(Utc(2026, 11, 18), 101, 102),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 55), 102, 103) }; // ends 14:56, 4m old

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal(4, quote.MinutesOld);
    }

    [Fact]
    public void ANinetySecondOldBarIsFresh()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 99, 100),
            Bar(Utc(2026, 11, 18), 101, 102),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 57, 30), 102, 103) }; // ends 14:58:30

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Null(quote.MinutesOld);
    }

    [Fact]
    public void TnxReadsAsAYieldAndChangesInBasisPoints()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 17), 42.6, 42.8),
            Bar(Utc(2026, 11, 18), 42.9, 43.0),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 59), 43.0, 43.1) };

        var quote = Quote(TenYear, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Yield, quote.Kind);
        Assert.True(quote.IsYield);
        Assert.Equal(4.31, quote.YieldLevel!.Value, Tolerance);
        Assert.Equal(3d, quote.Change, Tolerance);
    }

    [Fact]
    public void ForexIsClosedOnSaturday()
    {
        var now = Utc(2026, 11, 21, 14, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 19), 1.10, 1.10),
            Bar(Utc(2026, 11, 20), 1.10, 1.09),
        };

        var quote = Quote(EurUsd, daily, Array.Empty<Ohlcv>(), now);

        Assert.Equal(MarketPulseKind.Closed, quote.Kind);
        Assert.Equal(new DateOnly(2026, 11, 20), quote.SessionDate);
    }

    [Fact]
    public void ForexIsOpenOnSundayAtSixPmEastern()
    {
        // 22 November 2026 is EST: 18:00 Eastern is 23:00 UTC.
        var now = Utc(2026, 11, 22, 23, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 11, 19), 1.10, 1.10),
            Bar(Utc(2026, 11, 20), 1.10, 1.09),
        };
        var intraday = new[] { Bar(Utc(2026, 11, 22, 22, 59), 1.094, 1.095) };

        var quote = Quote(EurUsd, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Null(quote.MinutesOld);
        Assert.Equal((1.095d - 1.09d) / 1.09d * 100d, quote.Change, Tolerance);
    }

    [Fact]
    public void OneDailyBarOnlyIsUnavailable()
    {
        var now = Utc(2026, 11, 18, 15, 0);
        var daily = new[] { Bar(Utc(2026, 11, 18), 101, 102) };
        var intraday = new[] { Bar(Utc(2026, 11, 18, 14, 59), 102, 103) };

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Unavailable, quote.Kind);
    }

    [Fact]
    public void TheEasternOpenHoursHoldAcrossTheJulyDstChange()
    {
        // 14:00 UTC is 10:00 EDT.
        var now = Utc(2026, 7, 15, 14, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 7, 14), 99, 100),
            Bar(Utc(2026, 7, 15), 101, 101),
        };
        var intraday = new[] { Bar(Utc(2026, 7, 15, 13, 59), 101, 102) };

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal(2d, quote.Change, Tolerance);
    }

    [Fact]
    public void TheEasternOpenHoursHoldAcrossTheJanuaryDstChange()
    {
        // 15:00 UTC is 10:00 EST.
        var now = Utc(2026, 1, 14, 15, 0);
        var daily = new[]
        {
            Bar(Utc(2026, 1, 13), 99, 100),
            Bar(Utc(2026, 1, 14), 101, 101),
        };
        var intraday = new[] { Bar(Utc(2026, 1, 14, 14, 59), 101, 102) };

        var quote = Quote(Sp, daily, intraday, now);

        Assert.Equal(MarketPulseKind.Change, quote.Kind);
        Assert.Equal(2d, quote.Change, Tolerance);
    }

    [Fact]
    public void TheCryptoSettingDefaultsToMidnightAndAnUnknownValueReadsAsMidnight()
    {
        Assert.Equal(PulseCryptoStart.MidnightUtc, MarketPulseSettings.Parse(null));
        Assert.Equal(PulseCryptoStart.MidnightUtc, MarketPulseSettings.Parse("midnightUtc"));
        Assert.Equal(PulseCryptoStart.MidnightUtc, MarketPulseSettings.Parse("bogus"));
        Assert.Equal(PulseCryptoStart.Last24Hours, MarketPulseSettings.Parse("last24h"));
    }
}
