using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Services;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The background monitors' bar fetch builds a timeframe the venue does not serve, the way the
/// chart does. Cody, 2026-10-31: a BTCUSDT 24-minute chart on Bitstamp went silent the moment
/// the browser closed, because the monitor asked Bitstamp for 24-minute bars and Bitstamp has
/// none. The fake provider below refuses a non-native step exactly as Bitstamp does.
/// </summary>
public class BackgroundBarFetchTests
{
    private static readonly List<string> BitstampNative = new()
        { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d", "3d", "1w", "1M" };

    private static readonly DateTime Start = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Bitstamp's behaviour: native steps answer, anything else is a 400.</summary>
    private static IMarketDataProvider Bitstamp(List<MarketDataRequest> asked)
    {
        var p = Substitute.For<IMarketDataProvider>();
        p.NativelySupportedTimeframes.Returns(BitstampNative);
        p.MaxBarsPerRequest.Returns(1000);
        p.FetchOhlcvAsync(Arg.Any<MarketDataRequest>()).Returns(ci =>
        {
            var r = ci.Arg<MarketDataRequest>();
            asked.Add(r);
            if (!BitstampNative.Contains(r.Timeframe))
                throw new HttpRequestException("Bitstamp OHLC request failed with status 400: Not a valid choice.");
            long ms = TimeframeUtility.ToMilliseconds(r.Timeframe);
            int n = r.Limit;
            // The venue's newest n bars, ending at a time that is NOT on a 24-minute boundary,
            // so the oldest one is mid-bucket, as it is live. Bar k's open is k: each built bar's
            // open names the first venue bar it was built from.
            var end = Start.AddMinutes(24 * 50 + 9);
            var bars = Enumerable.Range(0, n)
                .Select(k => new Ohlcv(end.AddMilliseconds(-(n - 1 - k) * ms), k, k + 0.5, k - 0.5, k + 0.25, 1))
                .ToList();
            return (bars, new List<(long, double)>());
        });
        return p;
    }

    [Fact]
    public async Task A_timeframe_the_venue_lacks_is_built_from_a_native_one_and_every_bar_is_whole()
    {
        var asked = new List<MarketDataRequest>();
        var bars = await BackgroundBarFetch.FetchAsync(
            Bitstamp(asked), new ResamplerService(), "Spot", "BTCUSDT", "24m", 10);

        // Asked for the largest native step that divides 24 minutes, never for 24m itself.
        var r = Assert.Single(asked);
        Assert.Equal("3m", r.Timeframe);

        Assert.Equal(10, bars.Count);
        for (int i = 0; i < bars.Count; i++)
        {
            Assert.Equal(0, (bars[i].Date - Start).Ticks % TimeSpan.FromMinutes(24).Ticks);
            if (i > 0) Assert.Equal(TimeSpan.FromMinutes(24), bars[i].Date - bars[i - 1].Date);
        }

        // No partial bar: every bar but the forming newest one holds all eight 3-minute bars,
        // so its open and close are eight venue bars apart.
        for (int i = 0; i < bars.Count - 1; i++)
            Assert.Equal(7.25, bars[i].Close - bars[i].Open);
    }

    /// <summary>A deep fetch hits the venue's per-request cap, so the oldest built bar is the
    /// one that starts mid-bucket. It is dropped rather than read aloud with a wrong open.</summary>
    [Fact]
    public async Task At_the_venue_cap_the_partial_oldest_bar_is_dropped()
    {
        var asked = new List<MarketDataRequest>();
        var bars = await BackgroundBarFetch.FetchAsync(
            Bitstamp(asked), new ResamplerService(), "Spot", "BTCUSDT", "24m", 200);

        Assert.Equal(1000, Assert.Single(asked).Limit);
        Assert.True(bars.Count > 100);
        for (int i = 0; i < bars.Count - 1; i++)
            Assert.Equal(7.25, bars[i].Close - bars[i].Open);
    }

    [Fact]
    public async Task A_native_timeframe_is_asked_for_as_it_is()
    {
        var asked = new List<MarketDataRequest>();
        var bars = await BackgroundBarFetch.FetchAsync(
            Bitstamp(asked), new ResamplerService(), "Spot", "BTCUSDT", "4h", 3);

        var r = Assert.Single(asked);
        Assert.Equal("4h", r.Timeframe);
        Assert.Equal(3, r.Limit);
        Assert.Equal(3, bars.Count);
    }
}
