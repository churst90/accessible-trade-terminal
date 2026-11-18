using AccessibleTrader.Core.Services.MarketPulse;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Tests;

/// <summary>
/// The nine default items and the rules that pick a provider. Pure: every provider here answers
/// from a dictionary, so no test reaches the network.
/// </summary>
public class MarketPulseDefaultsTests
{
    private static MarketPulseDefaultItem Item(string spoken)
        => MarketPulseDefaults.Items.Single(i => i.Name == spoken);

    private static MarketPulseProvider Provider(
        string name,
        bool usable,
        IReadOnlyCollection<MarketType> markets,
        Func<MarketType, IReadOnlyList<string>?> symbols,
        string subType = "Spot")
        => new(name, usable, markets, symbols, _ => subType);

    private static MarketPulseProvider AllFmp(params string[] symbols)
        => Provider(
            "Fmp",
            usable: true,
            new[] { MarketType.Index, MarketType.Stock, MarketType.Crypto, MarketType.Forex, MarketType.Commodity },
            market => market switch
            {
                MarketType.Index => symbols.Where(s => s.StartsWith('^')).ToList(),
                MarketType.Stock => symbols.Where(s => !s.StartsWith('^')).ToList(),
                _ => symbols.ToList(),
            });

    [Fact]
    public void ResolutionStoresTheProvidersOwnSpelling()
    {
        var fmp = AllFmp("^GSPC", "^IXIC", "^VIX", "^TNX", "BTCUSD", "ETHUSD", "GCUSD", "WTICOUSD", "XAUUSD");
        var entry = MarketPulseDefaults.Resolve(Item("S&P"), new[] { fmp });

        Assert.NotNull(entry);
        Assert.Equal("Fmp", entry!.Provider);
        Assert.Equal("^GSPC", entry.Symbol);
        Assert.Equal(MarketType.Index, entry.Market);
        Assert.Equal("S&P", MarketPulseDefaults.SpokenName(entry));
    }

    [Fact]
    public void ACandidateIsMatchedAfterNormalisingBothSides()
    {
        var fmp = Provider(
            "Fmp",
            usable: true,
            new[] { MarketType.Crypto },
            _ => new[] { "BTC-USD" });

        var entry = MarketPulseDefaults.Resolve(Item("Bitcoin"), new[] { fmp });

        Assert.NotNull(entry);
        Assert.Equal("BTC-USD", entry!.Symbol);
        Assert.Equal("BTCUSD", MarketPulseDefaults.Normalize(entry.Symbol));
    }

    [Fact]
    public void CandidateOrderBeatsSymbolListOrderWithinOneProvider()
    {
        var fmp = Provider(
            "Fmp",
            usable: true,
            new[] { MarketType.Index },
            _ => new[] { "GSPC", "SPX" });

        var entry = MarketPulseDefaults.Resolve(Item("S&P"), new[] { fmp });

        Assert.Equal("SPX", entry!.Symbol);
    }

    [Fact]
    public void AProviderThatIsNotConfiguredAndNeedsAKeyIsSkipped()
    {
        var configured = Provider(
            "Fmp", usable: true, new[] { MarketType.Index }, _ => new[] { "SPX" });
        var unconfigured = Provider(
            "TwelveData", usable: false, new[] { MarketType.Index }, _ => new[] { "SPX" });

        var entry = MarketPulseDefaults.Resolve(Item("S&P"), new[] { unconfigured, configured });

        Assert.Equal("Fmp", entry!.Provider);
    }

    [Fact]
    public void BinanceIsNeverPickedEvenWhenItIsTheOnlyCryptoSource()
    {
        var binance = Provider(
            "Binance", usable: true, new[] { MarketType.Crypto }, _ => new[] { "BTCUSDT", "ETHUSDT" });

        Assert.Null(MarketPulseDefaults.Resolve(Item("Bitcoin"), new[] { binance }));

        var all = MarketPulseDefaults.ResolveAll(new[] { binance });
        Assert.Contains("Bitcoin", all.LeftOut);
        Assert.Contains("Ethereum", all.LeftOut);
    }

    [Fact]
    public void ProviderPreferenceIsRespected()
    {
        var coinbase = Provider(
            "Coinbase", usable: true, new[] { MarketType.Crypto },
            _ => new[] { "BTC/USD", "ETH/USD" });
        var kraken = Provider(
            "Kraken", usable: true, new[] { MarketType.Crypto },
            _ => new[] { "BTC/USD", "ETH/USD" });

        var entry = MarketPulseDefaults.Resolve(Item("Bitcoin"), new[] { kraken, coinbase });

        Assert.Equal("Coinbase", entry!.Provider);

        var fmp = Provider("Fmp", usable: true, new[] { MarketType.Index }, _ => new[] { "SPX" });
        var twelve = Provider("Twelve Data", usable: true, new[] { MarketType.Index }, _ => new[] { "SPX" });
        Assert.Equal("Fmp", MarketPulseDefaults.Resolve(Item("S&P"), new[] { twelve, fmp })!.Provider);
    }

    [Fact]
    public void AnItemWithNoSourceIsReportedLeftOut()
    {
        var fmp = Provider(
            "Fmp", usable: true, new[] { MarketType.Index }, _ => new[] { "SPX", "IXIC", "TNX", "DXY" });

        var result = MarketPulseDefaults.ResolveAll(new[] { fmp });

        Assert.Contains(result.Entries, e => e.Symbol == "SPX");
        Assert.Contains("Bitcoin", result.LeftOut);
        Assert.Contains("VIX", result.LeftOut);
        Assert.DoesNotContain("S&P", result.LeftOut);
    }

    [Fact]
    public void SpokenNamesUseTheTableThenTheCryptoBaseAssetThenTheSymbol()
    {
        Assert.Equal("S&P", MarketPulseDefaults.SpokenName(
            new WatchlistEntry("Fmp", "^GSPC", MarketType.Index)));
        Assert.Equal("Bitcoin", MarketPulseDefaults.SpokenName(
            new WatchlistEntry("Coinbase", "BTC/USD", MarketType.Crypto)));
        Assert.Equal("SOL", MarketPulseDefaults.SpokenName(
            new WatchlistEntry("Kraken", "SOLUSDT", MarketType.Crypto)));
        Assert.Equal("DOGE", MarketPulseDefaults.SpokenName(
            new WatchlistEntry("Kraken", "DOGE/USDC", MarketType.Crypto)));
        Assert.Equal("AAPL", MarketPulseDefaults.SpokenName(
            new WatchlistEntry("Fmp", "AAPL", MarketType.Stock)));
    }

    [Fact]
    public void YieldSymbolsAreRecognisedAfterNormalising()
    {
        Assert.True(MarketPulseDefaults.IsYield(new WatchlistEntry("Fmp", "TNX", MarketType.Index)));
        Assert.True(MarketPulseDefaults.IsYield(new WatchlistEntry("Fmp", "^TNX", MarketType.Index)));
        Assert.True(MarketPulseDefaults.IsYield(new WatchlistEntry("Fmp", "US10Y", MarketType.Index)));
        Assert.False(MarketPulseDefaults.IsYield(new WatchlistEntry("Fmp", "^GSPC", MarketType.Index)));
    }

    [Fact]
    public void TheNineItemsAreInTheDecidedOrder()
    {
        Assert.Equal(
            new[] { "S&P", "Nasdaq", "Bitcoin", "Ethereum", "ten-year",
                    "dollar index", "gold", "oil", "VIX" },
            MarketPulseDefaults.Items.Select(i => i.Name));
    }
}
