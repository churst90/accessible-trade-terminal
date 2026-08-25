using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.MyData;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The hand-written provider-name literals in <see cref="MarketOrchestrator"/> and
/// <see cref="DemoPolicy"/> must match what a provider actually calls itself.
///
/// <para>
/// This class of bug has now bitten three times. <c>"Fred"</c> vs <c>"FRED"</c> put a second, dead
/// entry in the Economic dropdown that no provider lookup could resolve; making
/// <c>EnsureContains</c> case-insensitive fixed that one but not <c>"FMPAnalytics"</c> vs
/// <c>"FMP Analytics"</c>, which differs by a space; and <c>DemoPolicy.HostedProviders</c> omitted
/// <c>"My Data"</c> entirely, leaving the hosted MyData market with an empty provider list.
/// A dead entry is invisible in review and looks like a broken provider to the user.
/// </para>
/// </summary>
public sealed class ProviderNameLiteralTests
{
    /// <summary>
    /// The real <c>Name</c> of every provider MarketOrchestrator hard-codes as a fallback for a
    /// market, stated independently of the orchestrator's own literals — that is the whole point.
    /// </summary>
    public static TheoryData<string, string[]> RealProviderNamesPerMarket => new()
    {
        { "Crypto",      new[] { "Binance", "Bitstamp", "Coinbase", "FMP" } },
        { "Economic",    new[] { "FRED", "FMP Analytics", "SEC EDGAR" } },
        { "OnChain",     new[] { "Glassnode", "CoinGecko", "BGeometrics", "DefiLlama",
                                 "CoinMetrics", "Mempool", "Etherscan" } },
        { "Derivatives", new[] { "OkxDerivatives", "BinanceDerivatives" } },
        { "Sentiment",   new[] { "AlternativeMe", "Wikipedia" } },
        { "Stock",       new[] { "Twelve Data", "Alpaca", "Polygon", "FMP" } },
        { "Forex",       new[] { "Twelve Data", "Alpaca", "Polygon", "FMP" } },
        { "Commodity",   new[] { "FMP" } },
        { "Index",       new[] { "FMP" } },
    };

    /// <summary>
    /// Feed the orchestrator the REAL names for a market. Its fallback literals should then add
    /// nothing at all — every one of them already matches something present. An extra entry means
    /// a literal that no provider answers to, which is exactly what a typo produces.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealProviderNamesPerMarket))]
    public async Task OrchestratorFallbackLiterals_MatchRealProviderNames(string market, string[] realNames)
    {
        var orch = Make(HostMode.Full, new List<string> { market }, realNames.ToList());
        await orch.RefreshPipelineAsync();
        orch.SelectedMarket = market;
        await orch.RefreshProvidersAsync();

        var unexpected = orch.AvailableProviders.Except(realNames).ToList();
        Assert.True(unexpected.Count == 0,
            $"{market}: MarketOrchestrator added provider name(s) no provider answers to: " +
            $"[{string.Join(", ", unexpected)}]. Compare against the real Name property.");
    }

    /// <summary>
    /// MyData is in <see cref="DemoPolicy.HostedMarkets"/>, and hosted runs every market through
    /// the provider whitelist — so the provider has to be in it too, spelled its way.
    /// </summary>
    [Fact]
    public void HostedProviders_IncludesMyDataProviderByItsRealName()
    {
        var hosted = new DemoPolicy(HostMode.Hosted);

        Assert.Contains(MyDataProvider.ProviderName, hosted.HostedProviders);
        Assert.True(hosted.IsProviderAllowed(MyDataProvider.ProviderName));
    }

    /// <summary>
    /// Streaming is a subset of what hosted offers at all: a venue that streams but isn't offered
    /// is unreachable, and the mismatch would only show up as a chart that never goes live.
    /// </summary>
    [Fact]
    public void HostedStreamingProviders_AreAllOfferedInHosted()
    {
        var hosted = new DemoPolicy(HostMode.Hosted);

        foreach (var streaming in hosted.HostedStreamingProviders)
            Assert.True(hosted.IsProviderAllowed(streaming),
                $"'{streaming}' can stream but is not in HostedProviders, so it is never offered.");
    }

    private static MarketOrchestrator Make(HostMode mode, List<string> markets, List<string> providers)
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string>(markets)));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string>(providers)));
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
}
