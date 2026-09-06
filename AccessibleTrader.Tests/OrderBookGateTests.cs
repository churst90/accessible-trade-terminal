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
/// "Does this provider have an order book", and what the app does with the answer.
///
/// <para>
/// The SERVICE half is unchanged and is a good check: the interface first (a compiler-enforced
/// fact; nine providers implement <c>IOrderBookProvider</c>), then the declared L2 flag, for a
/// venue with a real snapshot and no stream — Interactive Brokers.
/// </para>
///
/// <para>
/// <b>The CONSUMER half moved, 2026-09-06.</b> From 2026-09-05 the answer gated the toolbar
/// button, so on Twelve Data or an index feed the button was absent — and Alt+B still opened the
/// dialog, making it the one shortcut in the app whose toolbar control could vanish underneath
/// it. Cody's rule is that a shortcut and a button come as a pair, and for a screen-reader user
/// a control that disappears is the worse of the two failures: an absence says nothing, while a
/// dialog can say "this venue publishes no depth" in a sentence. So the button is always there
/// and the ANSWER now shapes what the dialog says — "does not publish an order book" when the
/// venue has none, "no depth returned just now" when it has one and the book came back empty.
/// Those were one sentence before, and they are not the same fact.
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

    // ── The consumer ─────────────────────────────────────────────────────────

    private static IRenderedComponent<Cmp.Toolbar> RenderToolbar(bool hasBook)
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
        var cut = RenderToolbar(hasBook: true);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button[aria-label='Order book']")));
    }

    [Fact]
    public void TheButtonIsStillThere_WhenTheProviderHasNone()
    {
        // The reversal, stated as a test. Alt+B is bound unconditionally, so the button has to
        // be too — otherwise the shortcut has no visible counterpart and the user meets an
        // absence with nothing to explain it.
        var cut = RenderToolbar(hasBook: false);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button[aria-label='Order book']")));
    }

    [Fact]
    public void TheToolbarDoesNotAskTheProviderAtAll()
    {
        // The gate is gone, not merely inverted: the toolbar no longer makes a per-provider
        // capability call it does nothing with. Pins the deletion rather than the symptom, so
        // reintroducing the call to gate on it again fails here first.
        var h = new Blazor.BlazorTestHarness();
        h.Ctx.RenderComponent<Cmp.Toolbar>();

        h.OrderService.DidNotReceive().HasOrderBookAsync(Arg.Any<string>());
    }

    // ── The dialog says WHICH of the two things is true ──────────────────────

    private static IRenderedComponent<Cmp.OrderBookModal> OpenBook(
        Blazor.BlazorTestHarness h, bool hasBook)
    {
        // WorkspaceState.Initial carries ChartIdentity.Empty, whose provider is "Bitstamp" with
        // only the SYMBOL blank — and a blank symbol takes the dialog down its "No symbol
        // selected" branch before it ever asks about depth. Give it a real chart.
        h.WorkspaceStore.State.Returns(_ => AccessibleTrader.Sdk.Models.WorkspaceState.Initial with
        {
            Identity = new AccessibleTrader.Sdk.Models.ChartIdentity("Crypto", "kraken", "BTC/USD", "1h"),
        });
        h.OrderService.HasOrderBookAsync(Arg.Any<string>()).Returns(Task.FromResult(hasBook));
        h.OrderService.GetOrderBookAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(Task.FromResult((
                Bids: new List<AccessibleTrader.Sdk.Models.OrderBookEntry>(),
                Asks: new List<AccessibleTrader.Sdk.Models.OrderBookEntry>())));
        return h.OpenModal<Cmp.OrderBookModal>(
            bus => bus.Publish(new AccessibleTrader.Core.Models.OpenOrderBookEvent()));
    }

    [Fact]
    public void OnAProviderWithNoBook_TheDialogSaysTheVenuePublishesNone()
    {
        using var h = new Blazor.BlazorTestHarness();

        var cut = OpenBook(h, hasBook: false);

        cut.WaitForAssertion(() =>
            Assert.Contains("does not publish an order book", cut.Markup));
    }

    [Fact]
    public void OnAProviderWithABook_AnEmptyResultIsReportedAsTemporary()
    {
        // The other half of the split. A venue that HAS a book and returned nothing is a quiet
        // market, and telling that user the venue has no order book would be false.
        using var h = new Blazor.BlazorTestHarness();

        var cut = OpenBook(h, hasBook: true);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No depth returned", cut.Markup);
            Assert.DoesNotContain("does not publish", cut.Markup);
        });
    }

    [Fact]
    public void TheMessageIsAnnouncedRatherThanLeftForTheUserToFind()
    {
        // The load is async, so this sentence arrives after the dialog opened and focus landed
        // on the heading. Without a live region it is silent — and since the button no longer
        // hides itself, this sentence is the whole explanation for an empty panel.
        using var h = new Blazor.BlazorTestHarness();

        var cut = OpenBook(h, hasBook: false);

        cut.WaitForAssertion(() =>
            Assert.Equal("alert", cut.Find(".alert-box.warning").GetAttribute("role")));
    }
}
