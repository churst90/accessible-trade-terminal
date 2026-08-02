using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every provider's <c>NativelySupportedTimeframes</c> list must parse cleanly
    /// through <see cref="TimeframeUtility.ToSeconds"/>. A single typo ("1H"
    /// instead of "1h", "1D" instead of "1d") silently breaks the provider's
    /// fetch path because the historical fetcher uses <c>ToSeconds</c> to build
    /// the bar step — a zero/negative result falls through to an empty result
    /// with no error, so users just see "no data" for that provider.
    ///
    /// Each row below mirrors the verbatim <c>NativelySupportedTimeframes</c>
    /// list declared in the corresponding plugin. Any drift in either direction
    /// breaks the test — which is the point: the test enforces that plugin
    /// declarations stay within the shared vocabulary that TimeframeUtility and
    /// the fetch pipeline understand.
    /// </summary>
    public class ProviderTimeframeContractTests
    {
        public static IEnumerable<object[]> ProviderTimeframes()
        {
            // Trading providers.
            yield return new object[] { "Binance",  new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" } };
            yield return new object[] { "Mexc",     new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w", "1M" } };
            yield return new object[] { "Coinbase", new[] { "1m", "5m", "15m", "1h", "6h", "1d" } };
            yield return new object[] { "Kraken",   new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w" } };
            yield return new object[] { "Bitstamp", new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "12h", "1d", "3d" } };
            yield return new object[] { "Alpaca",   new[] { "1m", "5m", "15m", "1h", "1d", "1w", "1M" } };
            yield return new object[] { "Polygon",  new[] { "1m", "5m", "15m", "1h", "1d", "1w", "1M" } };
            yield return new object[] { "Tradier",  new[] { "1m", "5m", "15m", "1d", "1w", "1M" } };
            yield return new object[] { "Schwab",   new[] { "1m", "5m", "10m", "15m", "30m", "1d", "1w", "1M" } };
            yield return new object[] { "Oanda",    new[] { "1m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "1w", "1M" } };
            yield return new object[] { "Finnhub",  new[] { "1m", "5m", "15m", "30m", "1h", "1d", "1w", "1M" } };
            yield return new object[] { "InteractiveBrokers", new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w", "1M" } };
            yield return new object[] { "TwelveData", new[] { "1m", "5m", "15m", "30m", "1h", "2h", "4h", "1d", "1w", "1M" } };
            yield return new object[] { "Fmp",      new[] { "1m", "5m", "15m", "30m", "1h", "4h", "1d" } };

            // Analytics providers.
            yield return new object[] { "AlternativeMe",      new[] { "1d" } };
            // Wikipedia per-article pageviews serves daily and monthly only — hourly is a 400.
            yield return new object[] { "Wikipedia",          new[] { "1d", "1M" } };
            yield return new object[] { "SEC EDGAR",          new[] { "1d" } };
            yield return new object[] { "BGeometrics",        new[] { "1d" } };
            yield return new object[] { "CoinGecko",          new[] { "1d" } };
            yield return new object[] { "CoinMetrics",        new[] { "1d" } };
            yield return new object[] { "DefiLlama",          new[] { "1d" } };
            yield return new object[] { "Etherscan",          new[] { "1d" } };
            yield return new object[] { "Glassnode",          new[] { "1d" } };
            yield return new object[] { "Mempool",            new[] { "1d" } };
            yield return new object[] { "BinanceVision",      new[] { "1h", "4h", "1d" } };
            yield return new object[] { "BinanceDerivatives", new[] { "5m", "15m", "30m", "1h", "4h", "1d" } };
            yield return new object[] { "OkxDerivatives",     new[] { "5m", "15m", "30m", "1h", "4h", "1d" } };
            yield return new object[] { "Fred",               new[] { "1d", "1w", "1m", "3m" } };
            yield return new object[] { "FmpAnalytics",       new[] { "1d" } };
        }

        [Theory]
        [MemberData(nameof(ProviderTimeframes))]
        public void EveryDeclaredTimeframe_ParsesToPositiveSeconds(string providerName, string[] timeframes)
        {
            foreach (var tf in timeframes)
            {
                int seconds = TimeframeUtility.ToSeconds(tf);
                Assert.True(seconds > 0,
                    $"{providerName}: timeframe '{tf}' failed to parse (ToSeconds returned {seconds}). "
                    + "A zero / negative result silently disables the provider's fetch path.");
            }
        }

        [Fact]
        public void EveryDeclaredTimeframe_HasNoDuplicates()
        {
            foreach (var row in ProviderTimeframes())
            {
                var providerName = (string)row[0];
                var timeframes = (string[])row[1];
                var distinct = timeframes.Distinct().Count();
                Assert.True(distinct == timeframes.Length,
                    $"{providerName}: duplicate timeframes in NativelySupportedTimeframes.");
            }
        }

        [Theory]
        [InlineData("1m", 60)]
        [InlineData("5m", 300)]
        [InlineData("1h", 3600)]
        [InlineData("4h", 14400)]
        [InlineData("1d", 86400)]
        [InlineData("1w", 604800)]
        [InlineData("1M", 2592000)]
        public void TimeframeUtility_KnownValues_Roundtrip(string tf, int expectedSeconds)
        {
            // Core pin for the utility itself — every plugin relies on this.
            Assert.Equal(expectedSeconds, TimeframeUtility.ToSeconds(tf));
        }

        [Theory]
        [InlineData("1H")]       // wrong case
        [InlineData("1hour")]    // not a single-letter unit
        [InlineData("1y")]       // unsupported unit
        [InlineData("5s")]       // sub-minute units not declared by any provider
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("abc")]
        public void TimeframeUtility_Unknown_ReturnsZero(string tf)
        {
            // Pin the "unknown → 0" contract. Providers rely on this: a zero
            // result in their fetch path is the signal to abort with an empty
            // result and log a warning.
            Assert.Equal(0, TimeframeUtility.ToSeconds(tf));
        }
    }
}
