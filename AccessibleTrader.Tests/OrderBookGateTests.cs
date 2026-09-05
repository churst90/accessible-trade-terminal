using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests;

/// <summary>
/// The toolbar's Order book button is gated on the CURRENT provider, the way Deposit is gated
/// on <c>IWalletProvider</c>. Cody, 2026-09-05: "Gate the order book button on the provider in
/// the toolbar." Before this the button was gated on the host policy alone, so on Twelve Data
/// or an index feed it opened a dialog whose only content was "Order book is not available for
/// X on Y." — a button over nothing.
///
/// <para>
/// Two halves. The SERVICE answers "does this provider have a book": the interface first (a
/// compiler-enforced fact; nine providers implement it), then the declared L2 flag (for a venue
/// with a real snapshot and no stream — Interactive Brokers). The TOOLBAR renders the button
/// only when the answer is yes. Each half is pinned on its own, because a green service test
/// says nothing about whether the markup reads it.
/// </para>
/// </summary>
public class OrderBookGateTests
{
    // ── The service ──────────────────────────────────────────────────────────

    private static GeneralOrderService Service(IMarketDataProvider? provider)
    {
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(provider));
        var paper = Substitute.For<IPaperTradingProvider>();
        paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
        return new GeneralOrderService(
            data, Substitute.For<IGlobalErrorCoordinator>(), NullLogger<GeneralOrderService>.Instance,
            new EventBus(), paper, Substitute.For<ISettingsManager>(),
            new DemoPolicy(HostMode.Full), new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
    }

    /// <summary>
    /// A provider with no book. NSubstitute auto-fills interface-returning methods, so the
    /// capability lookup has to be told to say null — real providers implement it as
    /// <c>this as T</c>, which IS null for these.
    /// </summary>
    private static IMarketDataProvider Plain(ProviderCapabilities caps = ProviderCapabilities.None)
    {
        var p = Substitute.For<IMarketDataProvider>();
        p.GetCapability<IOrderBookProvider>().Returns((IOrderBookProvider?)null);
        p.Capabilities.Returns(caps);
        return p;
    }

    [Fact]
    public async Task AProviderImplementingTheStreamInterface_HasABook()
    {
        var p = Substitute.For<IMarketDataProvider, IOrderBookProvider>();
        p.Capabilities.Returns(ProviderCapabilities.None); // the interface alone is enough
        Assert.True(await Service(p).HasOrderBookAsync("Kraken"));
    }

    [Fact]
    public async Task AProviderDeclaringL2_HasABook_EvenWithoutTheStream()
    {
        // Interactive Brokers: a quote-level snapshot, no IOrderBookProvider, L2 declared.
        Assert.True(await Service(Plain(ProviderCapabilities.L2 | ProviderCapabilities.Shorting))
            .HasOrderBookAsync("InteractiveBrokers"));
    }

    [Fact]
    public async Task AProviderWithNeither_HasNoBook()
    {
        // Twelve Data, Finnhub, every analytics feed: the snapshot returns an empty book and
        // nothing declares otherwise.
        Assert.False(await Service(Plain(ProviderCapabilities.Brackets)).HasOrderBookAsync("TwelveData"));
    }

    [Fact]
    public async Task AnUnknownOrBlankProvider_HasNoBook()
    {
        Assert.False(await Service(null).HasOrderBookAsync("Nobody"));
        Assert.False(await Service(Plain(ProviderCapabilities.L2)).HasOrderBookAsync(""));
    }

    // ── The toolbar ──────────────────────────────────────────────────────────

    private static IRenderedComponent<Cmp.Toolbar> Render(bool hasBook)
    {
        var h = new Blazor.BlazorTestHarness();
        h.OrderService.HasOrderBookAsync(Arg.Any<string>()).Returns(Task.FromResult(hasBook));
        // WorkspaceState.Initial carries ChartIdentity.Empty, whose provider is "Bitstamp" —
        // so the toolbar has a provider name to ask about without any further setup.
        return h.Ctx.RenderComponent<Cmp.Toolbar>();
    }

    [Fact]
    public void TheButtonIsThere_WhenTheProviderHasABook()
    {
        var cut = Render(hasBook: true);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button[aria-label='Order book']")));
    }

    [Fact]
    public void TheButtonIsAbsent_WhenTheProviderHasNone()
    {
        var cut = Render(hasBook: false);
        // The Deposit button is the precedent and the vacuity floor: the toolbar rendered,
        // just without this one control.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button")));
        Assert.Empty(cut.FindAll("button[aria-label='Order book']"));
    }
}
