using System.Linq;
using AccessibleTrader.Core.Services;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the public-demo whitelist — the security boundary for the hosted demo (see
/// docs/PLATFORM_STRATEGY_AND_ROADMAP.md). It must be a strict allow-list in demo mode and a
/// complete no-op outside it, so the full app/local/MAUI heads are never restricted.
/// </summary>
public class DemoPolicyTests
{
    private static readonly DemoPolicy Demo = new(isDemo: true);
    private static readonly DemoPolicy Full = new(isDemo: false);
    private static readonly DemoPolicy Hosted = new(HostMode.Hosted);

    [Fact]
    public void OutsideDemo_EverythingIsAllowed()
    {
        Assert.True(Full.IsProviderAllowed("Binance"));
        Assert.True(Full.IsSymbolAllowed("Binance", "DOGE/USD"));
        Assert.True(Full.IsTimeframeAllowed("1m"));
        Assert.True(Full.IsIndicatorAllowed("Ichimoku"));
        Assert.True(Full.AllowsLiveStream("Twelve Data"));
        Assert.True(Full.AllowTrading && Full.AllowCustomScripts && Full.AllowSettingsModal);
    }

    [Fact]
    public void Demo_Providers_AreWhitelisted()
    {
        Assert.True(Demo.IsProviderAllowed("Bitstamp"));
        Assert.True(Demo.IsProviderAllowed("Twelve Data"));
        Assert.False(Demo.IsProviderAllowed("Binance"));
        Assert.False(Demo.IsProviderAllowed("Coinbase"));
    }

    [Theory]
    [InlineData("BTC/USD", true)]
    [InlineData("btcusd", true)]      // normalised: letters+digits only, case-insensitive
    [InlineData("BTC-USD", true)]
    [InlineData("ETH/USD", true)]
    [InlineData("DOGE/USD", false)]
    public void Demo_Symbols_AreNormalisedAndWhitelisted(string symbol, bool allowed)
    {
        Assert.Equal(allowed, Demo.IsSymbolAllowed("Bitstamp", symbol));
    }

    [Fact]
    public void Demo_Timeframes_AreOnlyTheTwoExposed()
    {
        Assert.True(Demo.IsTimeframeAllowed("4h"));
        Assert.True(Demo.IsTimeframeAllowed("1d"));
        Assert.False(Demo.IsTimeframeAllowed("5m"));
        Assert.False(Demo.IsTimeframeAllowed("1w"));
    }

    [Fact]
    public void Demo_Indicators_AreTheCuratedSet()
    {
        Assert.True(Demo.IsIndicatorAllowed("Rsi"));
        Assert.True(Demo.IsIndicatorAllowed("Macd"));
        Assert.True(Demo.IsIndicatorAllowed("VPVR"));
        Assert.False(Demo.IsIndicatorAllowed("Ichimoku"));   // premium/full-only
    }

    [Fact]
    public void Demo_PinsOneProviderPerMarket()
    {
        // Twelve Data also advertises crypto; it must NOT be selectable for crypto, where
        // its free stream fails — Bitstamp is the only crypto provider in the demo.
        Assert.Equal("Bitstamp", Demo.ProviderForMarket("Crypto"));
        Assert.Equal("Twelve Data", Demo.ProviderForMarket("Stock"));
        Assert.Equal("Twelve Data", Demo.ProviderForMarket("Forex"));
        Assert.Equal("", Demo.ProviderForMarket("OnChain"));   // not a demo market
    }

    [Fact]
    public void Demo_OnlyBitstampHasALiveStream()
    {
        Assert.True(Demo.AllowsLiveStream("Bitstamp"));
        Assert.False(Demo.AllowsLiveStream("Twelve Data"));   // no free WebSocket → historical-only
    }

    [Fact]
    public void Demo_FilterProviders_KeepsOnlyWhitelisted()
    {
        var filtered = Demo.FilterProviders(new[] { "Bitstamp", "Binance", "Twelve Data", "Coinbase" });
        Assert.Equal(new[] { "Bitstamp", "Twelve Data" }, filtered.ToArray());
    }

    [Fact]
    public void Demo_FilterMarkets_HidesMarketsWithoutAProvider()
    {
        var filtered = Demo.FilterMarkets(new[] { "Crypto", "Stock", "Forex", "OnChain", "Economic" });
        Assert.Equal(new[] { "Crypto", "Stock", "Forex" }, filtered.ToArray());
    }

    // ── Hosted (--accounts): server-keyed builds curate DATA (providers/markets/
    //    live-stream) but keep the FULL app breadth (timeframes/indicators/symbols).

    [Theory]
    [InlineData(HostMode.Full, false)]
    [InlineData(HostMode.Demo, true)]
    [InlineData(HostMode.Hosted, true)]
    public void RestrictsData_TrueForServerKeyedBuilds(HostMode mode, bool restricts)
    {
        Assert.Equal(restricts, new DemoPolicy(mode).RestrictsData);
    }

    [Fact]
    public void Hosted_OffersEveryProviderThatNeedsNoUserKey()
    {
        // Hosted used to reuse the DEMO whitelist, so a logged-in user saw two providers and no
        // analytics at all. The rule now is capability, not tier: if it works with no user key
        // (or the server seeds the key), hosted offers it.
        Assert.True(Hosted.IsProviderAllowed("Bitstamp"));
        Assert.True(Hosted.IsProviderAllowed("Twelve Data"));   // server-seeded key
        Assert.True(Hosted.IsProviderAllowed("FRED"));          // server-seeded key
        Assert.True(Hosted.IsProviderAllowed("MEXC"));          // keyless public data
        Assert.True(Hosted.IsProviderAllowed("Binance"));
        Assert.True(Hosted.IsProviderAllowed("Kraken"));
        Assert.True(Hosted.IsProviderAllowed("CoinGecko"));

        // Still excluded: anything that would send the user to the API-keys modal, which hosted
        // has switched off — it could only ever render as a dead-end "API key required".
        Assert.False(Hosted.IsProviderAllowed("Coinbase"));
        Assert.False(Hosted.IsProviderAllowed("Alpaca"));
        Assert.False(Hosted.IsProviderAllowed("Polygon"));
        Assert.False(Hosted.IsProviderAllowed("Glassnode"));

        Assert.Equal(new[] { "Bitstamp", "Binance", "MEXC" },
            Hosted.FilterProviders(new[] { "Bitstamp", "Binance", "MEXC", "Coinbase" }).ToArray());
        Assert.Equal(new[] { "Crypto", "Stock", "Forex", "OnChain", "Economic" },
            Hosted.FilterMarkets(new[] { "Crypto", "Stock", "Forex", "OnChain", "Economic" }).ToArray());
    }

    [Fact]
    public void Hosted_StreamsOnlyWhereAPublicWebSocketExists()
    {
        // The venues with a public WS feed stream live...
        Assert.True(Hosted.AllowsLiveStream("Bitstamp"));
        Assert.True(Hosted.AllowsLiveStream("MEXC"));
        Assert.True(Hosted.AllowsLiveStream("Binance"));
        Assert.True(Hosted.AllowsLiveStream("Kraken"));

        // ...everything else stays historical-only. Asking a provider with no stream to stream
        // just loops on reconnects — the failure Twelve Data's free tier taught us in the demo.
        Assert.False(Hosted.AllowsLiveStream("Twelve Data"));
        Assert.False(Hosted.AllowsLiveStream("FRED"));
        Assert.False(Hosted.AllowsLiveStream("Gemini"));
    }

    [Fact]
    public void Demo_StaysLockedDown_WhenHostedOpensUp()
    {
        // The anonymous demo is a guided taste and must NOT inherit the hosted breadth.
        Assert.False(Demo.IsProviderAllowed("MEXC"));
        Assert.False(Demo.IsProviderAllowed("Binance"));
        Assert.False(Demo.IsProviderAllowed("FRED"));
        Assert.Equal(new[] { "Bitstamp", "Twelve Data" },
            Demo.FilterProviders(new[] { "Bitstamp", "Binance", "MEXC", "Twelve Data" }).ToArray());
        Assert.False(Demo.AllowsLiveStream("MEXC"));
    }

    [Fact]
    public void Hosted_KeepsFullBreadth_ForTimeframesIndicatorsAndSymbols()
    {
        // Unlike the locked demo, hosted is NOT a tight whitelist for what you can chart —
        // the educational draw is the full indicator/timeframe suite + symbol search.
        Assert.True(Hosted.IsTimeframeAllowed("1m"));
        Assert.True(Hosted.IsTimeframeAllowed("1w"));
        Assert.True(Hosted.IsIndicatorAllowed("Ichimoku"));
        Assert.True(Hosted.IsSymbolAllowed("Twelve Data", "MSFT"));   // search any ticker
    }
}
