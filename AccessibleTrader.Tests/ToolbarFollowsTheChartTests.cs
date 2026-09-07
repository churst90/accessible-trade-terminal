using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The toolbar dropdowns describe the chart on screen — including after a restore.</b>
///
/// <para>
/// Reported 2026-09-07: "if I restore a tab with BTC/USDT on Bitstamp, the market / provider /
/// asset dropdowns should remember the chart that was originally selected." They did not. There
/// were two sources of truth for "what am I looking at": <c>WorkspaceState.Identity</c>, which is
/// saved and restored, and <c>MarketOrchestrator.Selected*</c>, which is per-circuit in-memory
/// state with exactly two writers — the dropdowns themselves and the watchlist. Nothing seeded it
/// from a restored workspace.
/// </para>
///
/// <para>
/// A sync DID exist, on <c>TabSwitchedEvent</c> — and <c>WorkspaceStore</c> publishes that only
/// for a <c>SwitchTabAction</c>/<c>AddTabAction</c> that actually CHANGED state. A restore reaches
/// neither: single-tab sets the identity and dispatches no switch, and multi-tab restoring to its
/// saved active index 0 switches 0 to 0, which is not a change. <b>The guard existed and the
/// restore path walked around it</b> — the same shape as everything else found this week.
/// </para>
///
/// <para>
/// Why it is not merely cosmetic is written on the sync itself: pressing Load would have loaded
/// the symbol named in the dropdown rather than the one being looked at.
/// </para>
/// </summary>
public sealed class ToolbarFollowsTheChartTests
{
    private static (MarketOrchestrator Orch, WorkspaceStore Store) Make()
    {
        var data = Substitute.For<IDataService>();
        data.LoadAvailableMarketsAsync().Returns(_ => Task.FromResult(new List<string> { "Crypto" }));
        data.LoadProvidersByMarketTypeAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Bitstamp", "Binance" }));
        data.GetSupportedSubTypesAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "Spot" }));
        data.LoadSymbolsAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "BTC/USDT", "ETH/USDT" }));
        data.GetSupportedTimeframesAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult(new List<string> { "1h", "1d" }));
        data.ProviderRequiresApiKeyAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(false));
        data.IsProviderConfiguredAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(true));

        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        var orch = new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(),
            new DemoPolicy(isDemo: false));

        return (orch, store);
    }

    /// <summary>Exactly what a single-tab session restore does: set the identity, no tab switch.</summary>
    private static void RestoreChart(WorkspaceStore store, string provider, string symbol,
                                     string market = "Crypto", string timeframe = "1h") =>
        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = market, Provider = provider, Symbol = symbol, Timeframe = timeframe,
        }));

    [Fact]
    public void A_restored_chart_is_what_the_dropdowns_describe()
    {
        var (orch, store) = Make();

        RestoreChart(store, "Bitstamp", "BTC/USDT");

        Assert.Equal("Bitstamp", orch.SelectedProvider);
        Assert.Equal("BTC/USDT", orch.SelectedSymbol);
        Assert.Equal("1h", orch.SelectedTimeframe);
    }

    [Fact]
    public void Switching_the_chart_moves_the_dropdowns_with_it()
    {
        // Every other way a chart changes underneath the toolbar — a workspace load, a
        // watchlist jump into a different tab — is the same event.
        var (orch, store) = Make();
        RestoreChart(store, "Bitstamp", "BTC/USDT");

        RestoreChart(store, "Binance", "ETH/USDT", timeframe: "1d");

        Assert.Equal("Binance", orch.SelectedProvider);
        Assert.Equal("ETH/USDT", orch.SelectedSymbol);
        Assert.Equal("1d", orch.SelectedTimeframe);
    }

    [Fact]
    public void A_selection_the_user_is_part_way_through_is_not_clobbered()
    {
        // The hazard of following the store: choosing in a dropdown does NOT move the chart
        // until Load, so a state emission that does not change the identity must leave the
        // half-made choice alone. Otherwise picking a provider and reaching for the symbol
        // list would snap the provider back on the next tick.
        var (orch, store) = Make();
        RestoreChart(store, "Bitstamp", "BTC/USDT");

        orch.SelectedProvider = "Binance";       // user picks, has not pressed Load
        orch.SelectedSymbol = "ETH/USDT";

        // Something unrelated updates the workspace — a bar arrives, a setting changes.
        store.Dispatch(new SetIdentityAction(new ChartIdentity
        {
            Market = "Crypto", Provider = "Bitstamp", Symbol = "BTC/USDT", Timeframe = "1h",
        }));

        Assert.Equal("Binance", orch.SelectedProvider);
        Assert.Equal("ETH/USDT", orch.SelectedSymbol);
    }

    [Fact]
    public void A_blank_identity_does_not_wipe_the_dropdowns()
    {
        // A partially-populated or empty identity must not blank a dropdown that is currently
        // right — a blank chart is a state the app starts in, not an instruction.
        var (orch, store) = Make();
        RestoreChart(store, "Bitstamp", "BTC/USDT");

        store.Dispatch(new SetIdentityAction(new ChartIdentity()));

        Assert.Equal("Bitstamp", orch.SelectedProvider);
        Assert.Equal("BTC/USDT", orch.SelectedSymbol);
    }
}
