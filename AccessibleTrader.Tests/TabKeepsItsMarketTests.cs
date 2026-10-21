using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A tab keeps its own market.</b> Alpaca serves Crypto AND Stock. Switching from a Crypto tab
/// to an Alpaca DIA tab whose identity reads <c>"Stock|Spot"</c> kept the toolbar on "Crypto",
/// because the toolbar sync only asked whether the provider supported the CURRENT market. The
/// symbol list then reloaded as Alpaca's crypto pairs, DIA was not in it, the list default
/// (AAVE/USD) took its place, and the session saved the DIA tab back as Crypto AAVE/USD (Cody,
/// 2026-10-21, the Camel cycles workspace).
/// </summary>
public sealed class TabKeepsItsMarketTests
{
    private static (MarketOrchestrator orch, WorkspaceStore store) Make()
    {
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(), new ViewportNavigationService(), new VolumeStateService());

        var ds = Substitute.For<IDataService>();
        ds.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(false);
        ds.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(true);
        ds.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new List<string> { "Spot" });
        ds.LoadSymbolsAsync("Crypto", "Alpaca").Returns(new List<string> { "AAVE/USD", "BTC/USD" });
        ds.LoadSymbolsAsync("Stock", "Alpaca").Returns(new List<string> { "DIA", "SPY", "GLD" });
        ds.GetSupportedTimeframesAsync(Arg.Any<string>()).Returns(new List<string> { "1d" });
        // Crypto FIRST, as Alpaca declares it: the fallback to the provider's first market would
        // also land on Crypto, so only adopting the tab's own category can pass.
        ds.GetSupportedMarketsForProviderAsync("Alpaca")
          .Returns(new List<MarketType> { MarketType.Crypto, MarketType.Stock });
        ds.LoadProvidersByMarketTypeAsync(Arg.Any<string>()).Returns(new List<string> { "Alpaca" });
        ds.LoadAvailableMarketsAsync().Returns(new List<string> { "Crypto", "Stock" });
        ds.GetProviderAsync(Arg.Any<string>()).Returns((IMarketDataProvider?)null);

        var orch = new MarketOrchestrator(
            ds, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(isDemo: false));
        return (orch, store);
    }

    [Fact]
    public async Task A_stock_tab_on_a_provider_that_also_serves_crypto_stays_on_stock()
    {
        var (orch, store) = Make();
        orch.SelectedMarket = "Crypto";
        orch.SelectedProvider = "Alpaca";
        await orch.RefreshSymbolsAsync();

        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = "Stock|Spot", Provider = "Alpaca", Symbol = "DIA", Timeframe = "1d"
        }));

        // The sync runs on a background task; give it time, then read.
        for (int i = 0; i < 60 && orch.SelectedMarket != "Stock"; i++) await Task.Delay(50);

        Assert.Equal("Stock", orch.SelectedMarket);
        Assert.Equal("DIA", orch.SelectedSymbol);
        Assert.Contains("DIA", orch.AvailableSymbols);
    }

    [Fact]
    public async Task An_identity_without_a_category_keeps_the_current_supported_market()
    {
        var (orch, store) = Make();
        orch.SelectedMarket = "Stock";
        orch.SelectedProvider = "Alpaca";
        await orch.RefreshSymbolsAsync();

        // A bare sub-type, from before composite keys: nothing names the category, so the
        // current market stands because the provider supports it.
        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = "Spot", Provider = "Alpaca", Symbol = "SPY", Timeframe = "1d"
        }));
        await Task.Delay(500);

        Assert.Equal("Stock", orch.SelectedMarket);
    }
}
