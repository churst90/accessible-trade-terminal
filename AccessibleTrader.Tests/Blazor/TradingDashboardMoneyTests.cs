using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Stage 6c: a typed BUY, SELL or FLAT only fills in the dashboard or finds a position. These
/// tests pin the fields, the sentence, the OrderEvent channel, and the focus rule that makes
/// the Enter key that opened the dashboard unable to send anything on its own.
/// </summary>
public sealed class TradingDashboardMoneyTests
{
    private static readonly string[] ForbiddenFocusMarkers =
    {
        "order-confirm-live", "submit", "close",
    };

    private static BlazorTestHarness Harness(
        bool paper = false,
        IReadOnlyList<string>? providers = null,
        Func<string, IReadOnlyList<Position>>? positionsFor = null)
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        var seeded = h.WorkspaceStore.State;
        h.WorkspaceStore.State.Returns(_ => seeded with
        {
            Identity = new ChartIdentity("Crypto", "Kraken", "BTCUSDT", "1h"),
        });

        if (paper)
            h.With(new DemoPolicy(isDemo: true));

        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        if (!paper)
        {
            var live = (providers ?? new[] { "Kraken" })
                .Select(p => new ApiKeyConfig(p, "main", "key", "secret", Environment: "Live", IsActive: true))
                .ToList();
            h.ApiKeyService.GetAllKeysAsync().Returns(live);
        }

        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.None);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(Task.FromResult(false));
        h.OrderService.VenueSupportsOcoPairsAsync(default!).ReturnsForAnyArgs(Task.FromResult(false));
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(Task.FromResult(1.0));
        h.OrderService.GetAllowedLeverageAsync(default!, default!)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<int>?>(null));
        h.OrderService.GetBalancesAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        h.OrderService.GetPositionsAsync(default!)
            .ReturnsForAnyArgs(call =>
            {
                string provider = call.ArgAt<string>(0) ?? "";
                var rows = positionsFor?.Invoke(provider) ?? Array.Empty<Position>();
                return ProviderResult<List<Position>>.Ok(rows.ToList());
            });
        h.OrderService.GetOpenOrdersAsync(default!, default)
            .ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>()));
        h.OrderService.GetFillsAsync(default!, default, default)
            .ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
        return h;
    }

    private static void WaitForAccountRead(BlazorTestHarness h) =>
        Assert.True(SpinWait.SpinUntil(
            () => h.OrderService.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "GetPositionsAsync"),
            TimeSpan.FromSeconds(10)),
            "the dashboard never read an account");

    private static void WaitForSpoken(List<FeedbackRequestEvent> spoken, string sentence) =>
        Assert.True(SpinWait.SpinUntil(
            () => spoken.Any(e => e.Message == sentence), TimeSpan.FromSeconds(10)),
            $"never heard \"{sentence}\". Heard: "
            + string.Join(" || ", spoken.Select(e => e.Message)));

    private static IRenderedComponent<TradingDashboardModal> Open(
        BlazorTestHarness h, OpenTradingDashboardEvent request) =>
        h.OpenModal<TradingDashboardModal>(bus => bus.Publish(request));

    private static string FocusIdForFirstPosition(string slug, string symbol) =>
        "position-" + slug + "-" + new string(symbol.Where(char.IsLetterOrDigit).ToArray());

    private static void AssertNoForbiddenFocus(BlazorTestHarness h)
    {
        var ids = h.FocusedElementIds;
        foreach (string id in ids)
            Assert.DoesNotContain(ForbiddenFocusMarkers,
                marker => id.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    // ── BUY / SELL ─────────────────────────────────────────────────────────

    [Fact]
    public void BuyWithAQuantityFillsMarketSizeAndFocusesTheSizeField()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Buy", Quantity: 0.5, Market: true));

        WaitForAccountRead(h);
        WaitForSpoken(spoken,
            "Buy 0.5 BTCUSDT at market, filled in. Nothing was sent. "
            + "Live account, Submit reads it back first.");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button.side-btn.buy").GetAttribute("aria-pressed"));
            Assert.Equal("Market", cut.Find("select#order-type").GetAttribute("value"));
            Assert.Equal("0.5", cut.Find("input#order-qty").GetAttribute("value"));
        });

        h.WaitForFocus("order-qty");
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void BuyWithALimitPriceFillsLimitAndPrice()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Buy", 95000, Quantity: 0.5));

        WaitForAccountRead(h);
        WaitForSpoken(spoken,
            "Buy 0.5 BTCUSDT at limit 95000, filled in. Nothing was sent. "
            + "Live account, Submit reads it back first.");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Limit", cut.Find("select#order-type").GetAttribute("value"));
            Assert.Equal("95000", cut.Find("input#limit-price").GetAttribute("value"));
            Assert.Equal("0.5", cut.Find("input#order-qty").GetAttribute("value"));
        });

        h.WaitForFocus("order-qty");
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    /// <summary>
    /// The chart context menu's "limit order here" sends a side and a price with no quantity.
    /// It filled the ticket silently before stage 6c and must keep doing so: it is not a typed
    /// money request, so it must not say "filled in" over the menu's own words or pull focus
    /// from the dialog title to the size field.
    /// </summary>
    [Fact]
    public void TheChartMenusSideAndPriceStillFillSilently()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Buy", 95000));

        WaitForAccountRead(h);
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Limit", cut.Find("select#order-type").GetAttribute("value"));
            Assert.Equal("95000", cut.Find("input#limit-price").GetAttribute("value"));
        });
        h.WaitForFocus("trade-title");

        bool typedPathRan = SpinWait.SpinUntil(
            () => spoken.ToList().Any(s => (s.Message ?? "").Contains("filled in", StringComparison.Ordinal))
                || h.FocusedElementIds.Contains("order-qty"),
            TimeSpan.FromSeconds(2));
        Assert.False(typedPathRan,
            "the chart menu's request took the typed-money path. Said: "
            + string.Join(" | ", spoken.ToList().Select(s => s.Message)));
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void SellWithoutAQuantityLeavesTheTypeAndQuantityAlone()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Sell"));

        WaitForAccountRead(h);
        WaitForSpoken(spoken,
            "Sell BTCUSDT, filled in; the ticket's type and quantity are unchanged. "
            + "Nothing was sent. Live account, Submit reads it back first.");

        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("button.side-btn.sell").GetAttribute("aria-pressed")));
        h.WaitForFocus("order-qty");
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void AStaleStopLossIsNamed()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent());
        WaitForAccountRead(h);
        cut.Find("input#stop-loss").Change("90000");

        h.EventBus.Publish(new OpenTradingDashboardEvent("Buy", Quantity: 0.25, Market: true));

        WaitForSpoken(spoken,
            "Buy 0.25 BTCUSDT at market, filled in. Nothing was sent. "
            + "Live account, Submit reads it back first. "
            + "A stop loss at 90000 is still on the ticket.");
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void DemoForcesPaperAndSaysPaperAccount()
    {
        using var h = Harness(paper: true);
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Buy", Quantity: 0.5, Market: true));

        WaitForAccountRead(h);
        WaitForSpoken(spoken,
            "Buy 0.5 BTCUSDT at market, filled in. Nothing was sent. Paper account.");
        cut.WaitForAssertion(() =>
            Assert.Contains("PAPER TRADING", cut.Markup, StringComparison.Ordinal));
        h.WaitForFocus("order-qty");
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void AVenueThatCannotTradeSaysSoAndFillsNothing()
    {
        using var h = Harness();
        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(false);
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent("Buy", Quantity: 0.5, Market: true));

        WaitForSpoken(spoken,
            "Kraken does not support order execution, so the ticket was left alone. Nothing was sent.");
        Assert.DoesNotContain(spoken, e => e.Message!.Contains("filled in", StringComparison.OrdinalIgnoreCase));
        AssertNoForbiddenFocus(h);
    }

    // ── FLAT ───────────────────────────────────────────────────────────────

    [Fact]
    public void FlatWithNoPositionSaysThereIsNothingToClose()
    {
        using var h = Harness();
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent(Flat: true));
        WaitForAccountRead(h);

        WaitForSpoken(spoken, "No open position in BTCUSDT. Nothing to close.");
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("button#acct-tab-positions").GetAttribute("aria-selected")));
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void FlatWithOnePositionFocusesItsFirstCell()
    {
        using var h = Harness(positionsFor: _ => new[]
        {
            new Position("BTCUSDT", 0.5, 100, 50, 5),
        });
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent(Flat: true));
        WaitForAccountRead(h);

        WaitForSpoken(spoken,
            "Long 0.5 BTCUSDT on Kraken. Tab reaches Close position. Nothing was sent.");

        string id = FocusIdForFirstPosition("kraken", "BTCUSDT");
        h.WaitForFocus(id);
        cut.WaitForAssertion(() =>
        {
            var cell = cut.Find("#" + id);
            Assert.Equal("-1", cell.GetAttribute("tabindex"));
        });
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    [Fact]
    public void FlatWithTwoPositionsNamesTheCountAndFocusesTheFirst()
    {
        using var h = Harness(providers: new[] { "Binance", "Kraken" }, positionsFor: provider => new[]
        {
            new Position("BTCUSDT", provider == "Binance" ? 2.0 : 0.5, 100, 50, 5),
        });
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var cut = Open(h, new OpenTradingDashboardEvent(Flat: true));
        WaitForAccountRead(h);

        WaitForSpoken(spoken,
            "2 positions in BTCUSDT; the first is focused. "
            + "Long 2 BTCUSDT on Binance. Tab reaches Close position. Nothing was sent.");

        string id = FocusIdForFirstPosition("binance", "BTCUSDT");
        h.WaitForFocus(id);
        Assert.Equal("-1", cut.Find("#" + id).GetAttribute("tabindex"));
        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }

    /// <summary>
    /// The reason the focus rule exists. Every money request must end with focus on a reading
    /// target — a number field or a table cell — never on Submit, Confirm, Close position or
    /// Close at limit, because the Go bar runs on Enter's keydown and a held Enter repeats.
    /// </summary>
    [Theory]
    [InlineData("buy-market")]
    [InlineData("buy-limit")]
    [InlineData("buy-side-only")]
    [InlineData("flat")]
    public void EveryMoneyRequestLeavesFocusOffEverySendOrCloseControl(string shape)
    {
        using var h = Harness(positionsFor: _ => new[] { new Position("BTCUSDT", 0.5, 100, 50, 5) });
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);

        var request = shape switch
        {
            "buy-market" => new OpenTradingDashboardEvent("Buy", Quantity: 0.5, Market: true),
            "buy-limit" => new OpenTradingDashboardEvent("Buy", 95000, Quantity: 0.5),
            "buy-side-only" => new OpenTradingDashboardEvent("Buy"),
            _ => new OpenTradingDashboardEvent(Flat: true),
        };

        var cut = Open(h, request);
        WaitForAccountRead(h);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='dialog']")));
        Assert.True(SpinWait.SpinUntil(
            () => h.FocusedElementIds.Any(id => id == "order-qty" || id.StartsWith("position-", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10)),
            "no reading target ever took focus. Sent: " + string.Join(", ", h.FocusedElementIds));

        AssertNoForbiddenFocus(h);
        h.OrderService.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!, default!);
    }
}
