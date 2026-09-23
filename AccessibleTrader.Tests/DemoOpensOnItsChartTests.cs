using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The public demo opens on a chart.</b>
///
/// <para>
/// Reported by the server agent on 2026-09-22 (hosted notes §5p) and reproduced in a real browser
/// against a local <c>--demo</c> host the same day: <c>/app/</c> landed on an EMPTY symbol box —
/// options <c>BTCUSD</c> and <c>ETHUSD</c>, value <c>""</c> — and "Select a market and symbol to
/// begin". The Toolbar has always auto-loaded in the demo, gated on <c>CanLoad</c>, and
/// <c>CanLoad</c> was false because the symbol had been blanked.
/// </para>
///
/// <para>
/// <b>What blanked it was a race, and it was not demo-specific.</b> The store's first state
/// carries <see cref="ChartIdentity.Empty"/> — provider <c>"Bitstamp"</c>, symbol <c>""</c>. The
/// toolbar-follows-the-chart subscription skipped an identity only when BOTH fields were empty, so
/// it ran <c>SyncMarketToProviderAsync</c> on a background task, and that wrote the empty symbol
/// (and <c>"1h"</c>, a timeframe the demo does not offer) over whatever the Toolbar's own cascade
/// had just chosen. The existing blank-identity test dispatched <c>new ChartIdentity()</c>, whose
/// provider is empty too, and asserted before the background half could land — so it covered
/// neither the identity the app starts with nor the write that did the damage.
/// </para>
///
/// <para>
/// <b>Then the second half.</b> The market list comes back <c>Stock, Crypto, Forex</c>; the demo
/// was on Crypto only BECAUSE of the race, whose sync set the market from Bitstamp's. With the race
/// fixed and nothing reading <c>DemoPolicy.DefaultMarket/Provider/Symbol</c>, the demo would have
/// opened on AAPL. The defaults are now read.
/// </para>
/// </summary>
public sealed class DemoOpensOnItsChartTests
{
    private static IDataService Data(
        List<string> markets,
        Func<string, List<string>> providersFor,
        Func<string, List<string>> symbolsFor,
        Task<List<MarketType>>? marketsForProvider = null)
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string>(markets)));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(ci => Task.FromResult(providersFor(ci.Arg<string>())));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(ci => Task.FromResult(symbolsFor(ci.ArgAt<string>(1))));
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "1h", "4h", "1d" }));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));
        data.GetSupportedMarketsForProviderAsync(Arg.Any<string>())
            .Returns(_ => marketsForProvider ?? Task.FromResult(new List<MarketType> { MarketType.Crypto }));
        return data;
    }

    private static MarketOrchestrator Make(IDataService data, HostMode mode)
    {
        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        return new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(mode));
    }

    /// <summary>Waits for a background write that should NOT happen, long enough that it would have.</summary>
    private static async Task SettleAsync(Func<bool> changed)
    {
        for (int i = 0; i < 30 && !changed(); i++) await Task.Delay(50);
    }

    [Fact]
    public async Task The_empty_tab_identity_does_not_blank_the_toolbar_after_the_cascade_chose()
    {
        // Hold the background sync at its first await, let the Toolbar's cascade choose, then
        // release it — the ordering the browser hit.
        var gate = new TaskCompletionSource<List<MarketType>>();
        var data = Data(
            new List<string> { "Crypto" },
            _ => new List<string> { "Bitstamp" },
            _ => new List<string> { "BTC/USD", "ETH/USD" },
            gate.Task);
        var orch = Make(data, HostMode.Full);

        await orch.RefreshPipelineAsync();
        Assert.Equal("BTC/USD", orch.SelectedSymbol);
        string timeframe = orch.SelectedTimeframe;

        gate.SetResult(new List<MarketType> { MarketType.Crypto });
        await SettleAsync(() => orch.SelectedSymbol != "BTC/USD");

        Assert.Equal("BTC/USD", orch.SelectedSymbol);
        Assert.Equal(timeframe, orch.SelectedTimeframe);
    }

    [Fact]
    public async Task Opening_a_blank_tab_leaves_the_dropdowns_on_a_loadable_symbol()
    {
        // The OTHER caller of the sync: TabSwitchedEvent. A new tab's identity is
        // ChartIdentity.Empty too, and the switch path runs the sync without going through the
        // StateStream follower — so this is the case only the sync's own symbol guard covers.
        var bus = new EventBus();
        var store = new WorkspaceStore(
            bus, new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(
            Data(new List<string> { "Crypto" },
                 _ => new List<string> { "Bitstamp", "Binance" },
                 _ => new List<string> { "BTC/USDT", "ETH/USDT" }),
            Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), bus, new DemoPolicy(HostMode.Full));
        await orch.RefreshPipelineAsync();
        orch.SelectedSymbol = "ETH/USDT";

        store.Dispatch(new AddTabAction());
        await SettleAsync(() => orch.SelectedSymbol != "ETH/USDT");

        Assert.Equal("ETH/USDT", orch.SelectedSymbol);
    }

    [Fact]
    public async Task A_blank_identity_does_not_move_the_provider_either()
    {
        // The follower's own guard: without it AdoptIdentityIntoToolbar takes Empty's
        // "Bitstamp" as the provider while the symbol list is still Binance's — a provider and
        // a symbol that do not belong together, and Load would ask Bitstamp for it.
        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());
        var orch = new MarketOrchestrator(
            Data(new List<string> { "Crypto" },
                 _ => new List<string> { "Bitstamp", "Binance" },
                 _ => new List<string> { "BTC/USDT", "ETH/USDT" }),
            Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(HostMode.Full));
        store.Dispatch(new SetIdentityAction(new ChartIdentity
            { Market = "Crypto", Provider = "Binance", Symbol = "ETH/USDT", Timeframe = "1h" }));
        await SettleAsync(() => false);

        store.Dispatch(new SetIdentityAction(ChartIdentity.Empty));
        await SettleAsync(() => orch.SelectedProvider != "Binance");

        Assert.Equal("Binance", orch.SelectedProvider);
        Assert.Equal("ETH/USDT", orch.SelectedSymbol);
    }

    [Fact]
    public async Task The_demo_opens_on_its_declared_market_provider_symbol_and_timeframe()
    {
        var policy = new DemoPolicy(HostMode.Demo);
        // Stock listed FIRST and ETHUSD before BTCUSD, so neither default can be satisfied by
        // "take the first entry" — which is how Crypto and Bitstamp appeared to work before.
        var data = Data(
            new List<string> { "Stock", "Crypto", "Forex" },
            market => market == "Crypto" ? new List<string> { "Bitstamp" } : new List<string> { "Twelve Data" },
            provider => provider == "Bitstamp"
                ? new List<string> { "ETHUSD", "BTCUSD" }     // the provider's own spelling
                : new List<string> { "AAPL", "TSLA" });
        var orch = Make(data, HostMode.Demo);

        await orch.RefreshPipelineAsync();
        await SettleAsync(() => orch.SelectedSymbol != "BTCUSD");

        Assert.Equal(policy.DefaultMarket, orch.SelectedMarket);
        Assert.Equal(policy.DefaultProvider, orch.SelectedProvider);
        Assert.Equal("BTCUSD", orch.SelectedSymbol);              // "BTC/USD" as Bitstamp spells it
        Assert.Equal(policy.DefaultTimeframe, orch.SelectedTimeframe);
    }

    [Fact]
    public async Task The_default_symbol_only_steers_the_default_provider()
    {
        // Switching the demo to Stock must land on that market's first symbol, not stay empty
        // looking for a BTC/USD Twelve Data does not list.
        var data = Data(
            new List<string> { "Stock", "Crypto", "Forex" },
            market => market == "Crypto" ? new List<string> { "Bitstamp" } : new List<string> { "Twelve Data" },
            provider => provider == "Bitstamp"
                ? new List<string> { "ETHUSD", "BTCUSD" }
                : new List<string> { "AAPL", "TSLA" });
        var orch = Make(data, HostMode.Demo);
        await orch.RefreshPipelineAsync();

        orch.SelectedMarket = "Stock";
        await orch.RefreshProvidersAsync();

        Assert.Equal("Twelve Data", orch.SelectedProvider);
        Assert.Equal("AAPL", orch.SelectedSymbol);
    }
}
