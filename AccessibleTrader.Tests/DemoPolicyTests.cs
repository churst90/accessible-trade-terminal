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
}
