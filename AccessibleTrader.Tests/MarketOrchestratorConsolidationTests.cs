using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the unified Trading/Analytics interface. The old Trading/Analytics mode toggle
/// is gone: analytics categories (Economic/OnChain/Derivatives/Sentiment) now live under
/// a single "Analytics" umbrella entry in the Market dropdown, and the concrete category
/// is resolved via EffectiveMarket for all data-service keys. TerminalMode is derived from
/// the market choice rather than toggled by the user.
/// </summary>
public sealed class MarketOrchestratorConsolidationTests
{
    private static (MarketOrchestrator orch, IDataService data, WorkspaceStore store) Make()
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string>
            { "Crypto", "Forex", "Stock", "Economic", "OnChain", "Derivatives", "Sentiment" }));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "SomeProvider" }));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "SYM" }));
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "1h" }));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        var orch = new MarketOrchestrator(
            data,
            Substitute.For<IDataManager>(),
            store,
            Substitute.For<IWorkspaceInitializer>(),
            new EventBus(),
            new DemoPolicy(isDemo: false));   // HostMode.Full → no whitelist filtering
        return (orch, data, store);
    }

    [Fact]
    public async Task Market_list_groups_analytics_under_one_umbrella_entry()
    {
        var (orch, _, _) = Make();
        await orch.RefreshPipelineAsync();

        Assert.Contains("Crypto", orch.AvailableMarkets);
        Assert.Contains("Stock", orch.AvailableMarkets);
        Assert.Contains(MarketOrchestrator.AnalyticsMarket, orch.AvailableMarkets);

        // The raw analytics categories no longer leak into the tradeable market list…
        Assert.DoesNotContain("Economic", orch.AvailableMarkets);
        Assert.DoesNotContain("OnChain", orch.AvailableMarkets);
        // …they live under the umbrella instead.
        Assert.Contains("Economic", orch.AvailableAnalyticsTypes);
        Assert.Contains("Sentiment", orch.AvailableAnalyticsTypes);
    }

    [Fact]
    public async Task Default_selection_is_a_tradeable_market_in_trading_mode()
    {
        var (orch, _, store) = Make();
        await orch.RefreshPipelineAsync();

        Assert.Equal("Crypto", orch.SelectedMarket);
        Assert.Equal(TerminalMode.Trading, store.State.Mode);
    }

    [Fact]
    public async Task Selecting_analytics_resolves_the_concrete_category_and_derives_analytics_mode()
    {
        var (orch, data, store) = Make();
        await orch.RefreshPipelineAsync();

        // Pick the umbrella; the analytics-type defaults to the first available (Economic).
        orch.SelectedMarket = MarketOrchestrator.AnalyticsMarket;
        await orch.RefreshProvidersAsync();

        Assert.Equal("Economic", orch.SelectedAnalyticsType);
        Assert.Equal(TerminalMode.Analytics, store.State.Mode);
        // Providers are loaded for the concrete category, never for the umbrella name.
        await data.Received().LoadProvidersByMarketTypeAsync("Economic");
        await data.DidNotReceive().LoadProvidersByMarketTypeAsync(MarketOrchestrator.AnalyticsMarket);
    }

    [Fact]
    public async Task Switching_analytics_type_reloads_providers_for_that_category()
    {
        var (orch, data, _) = Make();
        await orch.RefreshPipelineAsync();
        orch.SelectedMarket = MarketOrchestrator.AnalyticsMarket;
        await orch.RefreshProvidersAsync();

        orch.SelectedAnalyticsType = "OnChain";
        await orch.RefreshProvidersAsync();

        await data.Received().LoadProvidersByMarketTypeAsync("OnChain");
    }
}
