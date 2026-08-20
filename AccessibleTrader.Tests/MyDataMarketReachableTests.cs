using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// End-to-end reachability for the user's own imported CSV data: selecting the My Data market
/// has to land on a provider, or the Import button opens onto a chart that can never load.
///
/// <para>
/// The whole feature hangs off three separate filters agreeing, and each has failed
/// independently: <see cref="DataService.LoadProvidersByMarketTypeAsync"/> sorts providers by
/// declared <c>DataShape</c>, <see cref="DemoPolicy"/> whitelists provider names in the
/// server-keyed builds, and <see cref="MarketOrchestrator"/> applies that whitelist per market.
/// A dropdown that lists the market but no provider looks like a broken provider to the user
/// rather than a missing one.
/// </para>
/// </summary>
public sealed class MyDataMarketReachableTests
{
    private static async Task<IDataService> DataServiceWithMyDataAsync()
    {
        var loader = Substitute.For<IPluginLoaderService>();
        loader.LoadPlugins<IMarketDataProvider>(Arg.Any<string>()).Returns(_ => new List<IMarketDataProvider>());

        var data = new DataService(
            loader,
            NullLogger<DataService>.Instance,
            Substitute.For<ICacheService>(),
            Substitute.For<IApiKeyService>(),
            Substitute.For<IGlobalErrorCoordinator>());

        await data.InitializeAsync(loader);

        // Exactly what AppStartupService does after plugin init.
        data.RegisterProvider(new MyDataProvider(Substitute.For<IMyDataStore>()));
        return data;
    }

    /// <summary>
    /// MyData is a tradeable-side market by enum, but MyDataProvider declares
    /// SingleValueLine as its default shape (an imported budget column is a line; only an
    /// OHLCV import is candles, which is why the real answer is per-symbol). The shape filter
    /// must not read that default as "analytics provider in a tradeable dropdown" and drop the
    /// one provider the market has.
    /// </summary>
    [Fact]
    public async Task MyDataMarket_ListsItsProvider()
    {
        var data = await DataServiceWithMyDataAsync();

        var providers = await data.LoadProvidersByMarketTypeAsync("MyData");

        Assert.Contains(MyDataProvider.ProviderName, providers);
    }

    [Fact]
    public async Task MyDataMarket_IsOfferedAtAll()
    {
        var data = await DataServiceWithMyDataAsync();

        var markets = await data.LoadAvailableMarketsAsync();

        Assert.Contains("MyData", markets);
    }

    /// <summary>
    /// The question this whole class exists to answer: import a CSV, then chart it. Runs the
    /// real store, the real parser and the real toolbar cascade — no substitutes past the
    /// plugin loader — because every previous break here was a filter in the middle, not a
    /// component in isolation.
    /// </summary>
    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Full)]
    public async Task ImportedCsv_IsSelectableAndReturnsItsBars(HostMode mode)
    {
        var paths = new TempWorkspacePaths();
        var store = new MyDataStore(paths, NullLogger<MyDataStore>.Instance);

        var (dataset, _) = await store.ImportAsync("my-fund", string.Join('\n', new[]
        {
            "date,open,high,low,close,volume",
            "2026-01-02,100,105,99,104,1000",
            "2026-01-03,104,108,103,107,1200",
            "2026-01-04,107,110,106,109,900",
        }));
        Assert.Equal(MyDataShape.Ohlcv, dataset.Shape);

        var loader = Substitute.For<IPluginLoaderService>();
        loader.LoadPlugins<IMarketDataProvider>(Arg.Any<string>()).Returns(_ => new List<IMarketDataProvider>());
        var data = new DataService(
            loader, NullLogger<DataService>.Instance,
            Substitute.For<ICacheService>(), Substitute.For<IApiKeyService>(),
            Substitute.For<IGlobalErrorCoordinator>());
        await data.InitializeAsync(loader);
        data.RegisterProvider(new MyDataProvider(store));

        var wsStore = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), wsStore,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(mode));

        await orch.RefreshPipelineAsync();
        orch.SelectedMarket = "MyData";
        await orch.RefreshProvidersAsync();
        await orch.RefreshSymbolsAsync();

        // The dataset is offered by name, at the timeframe the parser inferred from its rows.
        Assert.Equal(MyDataProvider.ProviderName, orch.SelectedProvider);
        Assert.Contains("my-fund", orch.AvailableSymbols);
        Assert.Contains(dataset.Timeframe, orch.AvailableTimeframes);

        // …and charting it returns the imported bars, as candles rather than a line.
        var shape = await orch.GetSelectedProviderDataShapeAsync();
        Assert.Equal(AccessibleTrader.Sdk.Plugins.ProviderDataShape.Ohlcv, shape);

        var (bars, _) = await data.FetchOhlcvAsync(
            MyDataProvider.ProviderName,
            new AccessibleTrader.Sdk.Models.MarketDataRequest(
                Market: "MyData", Symbol: "my-fund",
                Timeframe: dataset.Timeframe, Limit: 500));

        Assert.Equal(3, bars.Count);
        Assert.Equal(104, bars[0].Close);
        Assert.Equal(109, bars[^1].Close);
    }

    /// <summary>The full toolbar cascade, on the build where this matters most.</summary>
    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Full)]
    public async Task SelectingMyData_LandsOnAProvider(HostMode mode)
    {
        var data = await DataServiceWithMyDataAsync();

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(mode));

        await orch.RefreshPipelineAsync();
        Assert.Contains("MyData", orch.AvailableMarkets);

        orch.SelectedMarket = "MyData";
        await orch.RefreshProvidersAsync();

        Assert.Contains(MyDataProvider.ProviderName, orch.AvailableProviders);
        Assert.Equal(MyDataProvider.ProviderName, orch.SelectedProvider);
    }
}
