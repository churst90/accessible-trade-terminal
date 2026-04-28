// OrderBookModal — bUnit coverage for the Phase 5 v1 rework.
//
// What this guards against (per user spec, 2026-04-27):
//   - Each row is keyboard-focusable (tabindex="0") with a price+size aria-label.
//   - 20 levels per side rendered when data is present.
//   - GetOrderBookAsync called with depth=20 — not the previous hardcoded 15.
//   - SubscribeOrderBookAsync invoked when modal opens; live OrderBookUpdate ticks
//     replace the rendered rows silently (no aria-live, no speech).
//   - Modal publishes ModalStateChangedEvent on both open and close paths.

using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class OrderBookModalTests
{
    private static (List<OrderBookEntry> bids, List<OrderBookEntry> asks) BuildSnapshot(int levels = 20)
    {
        var bids = new List<OrderBookEntry>();
        var asks = new List<OrderBookEntry>();
        for (int i = 0; i < levels; i++)
        {
            bids.Add(new OrderBookEntry(67234.50 - i * 0.50, 1.0 + i * 0.10));
            asks.Add(new OrderBookEntry(67235.00 + i * 0.50, 1.2 + i * 0.10));
        }
        return (bids, asks);
    }

    private static WorkspaceState BuildStateWithSymbol()
        => WorkspaceState.Initial with
        {
            Identity = WorkspaceState.Initial.Identity with
            {
                Provider = "TestProvider",
                Symbol = "BTC/USDT",
                Timeframe = "1m",
            },
        };

    [Fact]
    public void OrderBookModal_HiddenByDefault_RendersNothing()
    {
        using var h = new BlazorTestHarness();
        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.OrderBookModal>();
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void OrderBookModal_OpenWithoutSymbol_ShowsErrorState()
    {
        using var h = new BlazorTestHarness();
        // WorkspaceState.Initial has Provider="Bitstamp" but Symbol="" — the modal's
        // string.IsNullOrEmpty(symbol) gate triggers the "No symbol selected" branch.
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        Assert.Contains("No symbol selected", cut.Markup);
    }

    [Fact]
    public void OrderBookModal_OpenWithSymbol_RequestsDepth20Snapshot()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BuildStateWithSymbol());
        var (bids, asks) = BuildSnapshot(20);
        h.OrderService.GetOrderBookAsync("TestProvider", "BTC/USDT", 20)
            .Returns(Task.FromResult((bids, asks)));

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        h.OrderService.Received().GetOrderBookAsync("TestProvider", "BTC/USDT", 20);
    }

    [Fact]
    public void OrderBookModal_RendersTwentyRowsPerSide_WhenSnapshotProvided()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BuildStateWithSymbol());
        var (bids, asks) = BuildSnapshot(20);
        h.OrderService.GetOrderBookAsync("TestProvider", "BTC/USDT", 20)
            .Returns(Task.FromResult((bids, asks)));

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        Assert.Equal(20, cut.FindAll("tr[aria-label^='Bid ']").Count);
        Assert.Equal(20, cut.FindAll("tr[aria-label^='Ask ']").Count);
    }

    [Fact]
    public void OrderBookModal_EachRow_IsKeyboardFocusable_WithPriceSizeLabel()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BuildStateWithSymbol());
        var bids = new List<OrderBookEntry> { new(67234.50, 0.85) };
        var asks = new List<OrderBookEntry> { new(67235.00, 1.20) };
        h.OrderService.GetOrderBookAsync("TestProvider", "BTC/USDT", 20)
            .Returns(Task.FromResult((bids, asks)));

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        // Each row has tabindex="0" so Tab visits it; aria-label conveys the price+size
        // contract the user demanded ("I only need price and volume"). The modal uses
        // "G6" formatting so the price renders as "67234.5" without trailing zero.
        var bidRow = cut.Find("tr[aria-label^='Bid ']");
        Assert.Equal("0", bidRow.GetAttribute("tabindex"));
        Assert.Contains("67234.5", bidRow.GetAttribute("aria-label"));
        Assert.Contains("0.85", bidRow.GetAttribute("aria-label"));

        var askRow = cut.Find("tr[aria-label^='Ask ']");
        Assert.Equal("0", askRow.GetAttribute("tabindex"));
        Assert.Contains("67235", askRow.GetAttribute("aria-label"));
        Assert.Contains("1.2", askRow.GetAttribute("aria-label"));
    }

    [Fact]
    public void OrderBookModal_OpenAndClose_PublishesModalStateChangedTwice()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BuildStateWithSymbol());

        var openEvents = new List<ModalStateChangedEvent>();
        h.EventBus.Subscribe<ModalStateChangedEvent>(e => openEvents.Add(e));

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        // Footer Close button is the only button under .modal-footer.
        cut.Find(".modal-footer button").Click();

        var orderBookEvents = openEvents.Where(e => e.ModalName == "Order book").ToList();
        Assert.Contains(orderBookEvents, e => e.IsOpen);
        Assert.Contains(orderBookEvents, e => !e.IsOpen);
    }

    [Fact]
    public async Task OrderBookModal_LiveStream_ReplacesRowsSilently_OnOrderBookUpdate()
    {
        using var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => BuildStateWithSymbol());

        // Initial snapshot: one row each side.
        var initialBids = new List<OrderBookEntry> { new(67234.50, 0.85) };
        var initialAsks = new List<OrderBookEntry> { new(67235.00, 1.20) };
        h.OrderService.GetOrderBookAsync("TestProvider", "BTC/USDT", 20)
            .Returns(Task.FromResult((initialBids, initialAsks)));

        // Live stream — Subject lets the test push updates after open.
        var stream = new Subject<OrderBookUpdate>();
        h.OrderService.SubscribeOrderBookAsync("TestProvider", "BTC/USDT")
            .Returns(Task.FromResult<IObservable<OrderBookUpdate>?>(stream.AsObservable()));

        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.OrderBookModal>(
            bus => bus.Publish(new OpenOrderBookEvent()));

        Assert.Single(cut.FindAll("tr[aria-label^='Bid ']"));

        // Push an update changing the size — the new value must show up in the row's
        // aria-label without a parallel aria-live announcement (we don't render any).
        var updatedBids = new List<OrderBookEntry> { new(67234.50, 5.55) };
        var updatedAsks = new List<OrderBookEntry> { new(67235.00, 1.20) };
        await cut.InvokeAsync(() =>
            stream.OnNext(new OrderBookUpdate("BTC/USDT", updatedBids, updatedAsks, 1, DateTime.UtcNow)));

        var bidLabel = cut.Find("tr[aria-label^='Bid ']").GetAttribute("aria-label");
        Assert.Contains("5.55", bidLabel);

        // No aria-live region on the bid row itself — silent updates are part of the
        // contract. The harness has no SpeechManager.OnSpeak hook so even if some
        // path tried to speak, nothing would observe it; this assertion is a guard
        // against accidentally re-introducing a per-update announcement attribute.
        Assert.Null(cut.Find("tr[aria-label^='Bid ']").GetAttribute("aria-live"));
    }
}
