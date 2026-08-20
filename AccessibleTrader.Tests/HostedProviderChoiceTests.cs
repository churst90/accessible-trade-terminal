using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// End-to-end cover for the 2026-08-20 report: "I don't see all the options in the market and
/// provider dropdowns when I log in — I should see MEXC but I don't."
///
/// The cause was that <c>DemoPolicy.RestrictsData</c> is true for BOTH server-keyed builds, and
/// <see cref="MarketOrchestrator"/> used it to collapse the provider list to the single entry
/// <c>ProviderForMarket</c> names. That is right for the anonymous demo (one guided choice per
/// market) and wrong for hosted, which is the full app: it left a logged-in user with Bitstamp
/// for crypto, Twelve Data for stocks/forex, and no analytics markets at all.
///
/// <see cref="DemoPolicyTests"/> pins the policy lists; these pin what the dropdowns actually
/// end up containing, which is the part the user sees.
/// </summary>
public sealed class HostedProviderChoiceTests
{
    private static MarketOrchestrator Make(HostMode mode, List<string> providersPerMarket)
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string>
            { "Crypto", "Forex", "Stock", "Economic", "OnChain", "Derivatives", "Sentiment" }));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string>(providersPerMarket)));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "BTC/USD" }));
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "1h", "1d" }));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        return new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(mode));
    }

    [Fact]
    public async Task Hosted_CryptoOffersEveryKeylessVenue_IncludingMexc()
    {
        var orch = Make(HostMode.Hosted,
            new List<string> { "Bitstamp", "Binance", "MEXC", "Kraken", "Coinbase", "Alpaca" });
        await orch.RefreshPipelineAsync();

        Assert.Contains("MEXC", orch.AvailableProviders);
        Assert.Contains("Binance", orch.AvailableProviders);
        Assert.Contains("Kraken", orch.AvailableProviders);
        Assert.Contains("Bitstamp", orch.AvailableProviders);

        // Key-required venues stay out — hosted has the API-keys modal switched off, so they
        // could only ever render as a dead-end "API key required".
        Assert.DoesNotContain("Coinbase", orch.AvailableProviders);
        Assert.DoesNotContain("Alpaca", orch.AvailableProviders);
    }

    [Fact]
    public async Task Hosted_KeepsTheAnalyticsUmbrella()
    {
        var orch = Make(HostMode.Hosted, new List<string> { "FRED", "CoinGecko", "Glassnode" });
        await orch.RefreshPipelineAsync();

        // Analytics markets survived the market filter, so the umbrella entry is offered…
        Assert.Contains(MarketOrchestrator.AnalyticsMarket, orch.AvailableMarkets);
        Assert.Contains("Economic", orch.AvailableAnalyticsTypes);
        Assert.Contains("OnChain", orch.AvailableAnalyticsTypes);
        Assert.Contains("Sentiment", orch.AvailableAnalyticsTypes);
    }

    [Fact]
    public async Task Hosted_MyDataMarket_StillHasItsProvider()
    {
        // HostedMarkets offers "MyData", but the provider is named "My Data" (with a space) and
        // the hosted branch runs every market through FilterProviders. Leave it out of
        // HostedProviders and the market is selectable with an EMPTY provider dropdown — offered
        // and then a dead end, which is worse than not offering it.
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string>
            { "Crypto", "MyData" }));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "My Data" }));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "my-import" }));
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "1d" }));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(HostMode.Hosted));

        await orch.RefreshPipelineAsync();
        Assert.Contains("MyData", orch.AvailableMarkets);

        orch.SelectedMarket = "MyData";
        await orch.RefreshProvidersAsync();

        Assert.Contains("My Data", orch.AvailableProviders);
        Assert.Equal("My Data", orch.SelectedProvider);
    }

    [Fact]
    public async Task Demo_StillCollapsesToOneProviderPerMarket()
    {
        // The anonymous demo must NOT inherit the hosted breadth — one correct choice per market
        // is the whole design of the guided taste.
        var orch = Make(HostMode.Demo,
            new List<string> { "Bitstamp", "Binance", "MEXC", "Kraken" });
        await orch.RefreshPipelineAsync();

        Assert.Equal(new[] { "Bitstamp" }, orch.AvailableProviders);
        Assert.DoesNotContain(MarketOrchestrator.AnalyticsMarket, orch.AvailableMarkets);
    }

    [Fact]
    public async Task Full_LocalDesktop_FiltersNothing()
    {
        var orch = Make(HostMode.Full,
            new List<string> { "Bitstamp", "MEXC", "Coinbase", "Alpaca" });
        await orch.RefreshPipelineAsync();

        // The desktop user supplies their own keys, so every provider is theirs to pick.
        Assert.Contains("Coinbase", orch.AvailableProviders);
        Assert.Contains("Alpaca", orch.AvailableProviders);
        Assert.Contains("MEXC", orch.AvailableProviders);
    }
}
