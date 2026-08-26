// The trading dashboard, decoupled from the chart.
//
// The bug these guard: a user holding three resting BTC/USD paper orders could not
// cancel them, and could not see them either. Every action in the dialog resolved its
// venue from the FOCUSED CHART — `var provider = Store.State.Identity.Provider; if
// (string.IsNullOrEmpty(provider)) return;` at seven separate sites — and the orders
// tab was filtered by the chart's symbol with an exact string match. Their chart said
// BTCUSDT and their orders said BTC/USD, so the tab rendered empty, which on this
// screen is indistinguishable from "you have no orders". The positions tab takes no
// symbol and was not filtered, so it showed the position they could not act on.
//
// Everything below is written against the decoupled shape: the dialog enumerates
// ACCOUNTS, each row carries the account it came from, and the actions read the venue
// off the row.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class TradingDashboardDecouplingTests
{
    private static ApiKeyConfig Key(string provider, string nickname = "main", bool withdrawal = false) =>
        new(provider, nickname, "key", "secret", Environment: "Live", IsActive: true, AllowsWithdrawal: withdrawal);

    /// <summary>
    /// A harness with trading accounts and NO chart at all — the condition the reported
    /// bug was hit under, and the one every silent early return keyed on.
    /// </summary>
    private static BlazorTestHarness Harness(
        IEnumerable<ApiKeyConfig>? keys = null,
        IEnumerable<OpenOrder>? orders = null,
        IEnumerable<Position>? positions = null,
        bool seedChart = false)
    {
        var h = new BlazorTestHarness();
        if (seedChart)
        {
            ModalCatalog.SeedChartState(h);
        }
        else
        {
            // NOT the harness default. `WorkspaceState.Initial` uses
            // `ChartIdentity.Empty`, which is Bitstamp/Spot/1h with only the SYMBOL
            // blank — so "no chart seeded" still hands the dashboard a provider name,
            // and a test claiming to run without a chart would exercise the chart path
            // and pass for the wrong reason. It did: reintroducing the old
            // `if (string.IsNullOrEmpty(provider)) return;` into CancelOrder left this
            // file entirely green until this branch existed.
            var blank = WorkspaceState.Initial with { Identity = new ChartIdentity("", "", "", "") };
            h.WorkspaceStore.State.Returns(_ => blank);
        }

        // Loose mode: the harness's focusElement shim records the call but never
        // completes it, and ShowAsync awaits it — under the strict default the modal
        // parks on that line and nothing after it runs.
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();

        h.ApiKeyService.GetAllKeysAsync().Returns((keys ?? new[] { Key("kraken") }).ToList());
        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.MarginTrading);
        h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(false);
        h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(1.0);
        h.OrderService.CancelOrderAsync(default!, default!, default!).ReturnsForAnyArgs(true);
        h.OrderService.GetBalancesAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        h.OrderService.GetPositionsAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Position>>.Ok((positions ?? Array.Empty<Position>()).ToList()));
        h.OrderService.GetOpenOrdersAsync(default!, default)
            .ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok((orders ?? Array.Empty<OpenOrder>()).ToList()));
        h.OrderService.GetFillsAsync(default!, default, default)
            .ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
        return h;
    }

    private static IRenderedComponent<TradingDashboardModal> Open(BlazorTestHarness h) =>
        h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));

    /// <summary>
    /// Orders and history live behind their own tabs — the dialog opens on Positions,
    /// because "what am I actually holding?" is what it is opened to answer. A test
    /// that asserts on order rows without selecting the tab asserts on an unrendered
    /// branch and passes for the wrong reason once it is made to pass at all.
    /// </summary>
    private static void SelectTab(IRenderedComponent<TradingDashboardModal> cut, string id) =>
        cut.Find($"#acct-tab-{id}").Click();

    private static void WaitForAccountRead(BlazorTestHarness h) =>
        Assert.True(
            SpinWait.SpinUntil(
                () => h.OrderService.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "GetPositionsAsync"),
                TimeSpan.FromSeconds(10)),
            "The dashboard never read an account. Calls made: "
            + string.Join(", ", h.OrderService.ReceivedCalls().Select(c => c.GetMethodInfo().Name)));

    // ── The reported regression ──────────────────────────────────────────────

    [Fact]
    public void Cancelling_an_order_does_not_require_a_focused_chart()
    {
        // THE bug. With no chart, CancelOrder returned in silence: the user pressed a
        // button and the terminal did nothing and said nothing. Cancelling needs the
        // order id and nothing else — the paper broker looks it up and removes it, and
        // a live venue is named by the order's own row.
        using var h = Harness(orders: new[]
        {
            new OpenOrder("ord-1", "BTC/USD", OrderSide.Sell, OrderType.StopMarket, 1.0, 60_320, "NEW"),
        });
        var cut = Open(h);
        WaitForAccountRead(h);
        SelectTab(cut, "orders");

        cut.WaitForAssertion(() => Assert.NotEmpty(
            cut.FindAll("button").Where(b => b.TextContent.Contains("Cancel order"))));
        cut.FindAll("button").First(b => b.TextContent.Contains("Cancel order")).Click();

        cut.WaitForAssertion(() =>
            h.OrderService.Received().CancelOrderAsync("kraken", "ord-1", "BTC/USD"));
    }

    [Fact]
    public void The_orders_tab_is_not_filtered_by_the_focused_chart_symbol()
    {
        // The chart says BTC/USD (ModalCatalog's seed); the order says BTCUSDT. Under
        // the old exact-match filter the tab rendered empty, which reads as "you have
        // no orders" — the opposite of the truth, and a trader acts differently on each.
        using var h = Harness(seedChart: true, orders: new[]
        {
            new OpenOrder("ord-1", "BTCUSDT", OrderSide.Sell, OrderType.Limit, 0.5, 61_000, "NEW"),
        });
        var cut = Open(h);
        WaitForAccountRead(h);

        // The symbol argument must be null — not the chart's symbol, and not omitted
        // by luck at one of the four call sites.
        h.OrderService.Received().GetOpenOrdersAsync("kraken", null);
        h.OrderService.DidNotReceive().GetOpenOrdersAsync("kraken", "BTC/USD");

        SelectTab(cut, "orders");
        cut.WaitForAssertion(() => Assert.Contains("BTCUSDT", cut.Markup));
    }

    [Fact]
    public void The_dashboard_opens_and_shows_accounts_with_no_chart_at_all()
    {
        // ShowAsync used to return before it ever set _isVisible, so Alt+T with no
        // chart produced a dialog that never appeared and never said why.
        using var h = Harness(positions: new[]
        {
            new Position("BTCUSDT", -0.6875, 57_225.46, -39_342.5, 120.0, 1.0, 83_237.0, MarginMode.Isolated),
        });
        var cut = Open(h);
        WaitForAccountRead(h);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Trading Dashboard", cut.Markup);
            Assert.Contains("BTCUSDT", cut.Markup);
        });
    }

    // ── What the rows say ────────────────────────────────────────────────────

    [Fact]
    public void A_position_states_its_direction_mode_and_leverage_and_its_exchange()
    {
        using var h = Harness(positions: new[]
        {
            new Position("BTCUSDT", -0.6875, 57_225.46, -39_342.5, 120.0, 1.0, 83_237.0, MarginMode.Isolated),
        });
        var cut = Open(h);
        WaitForAccountRead(h);

        cut.WaitForAssertion(() =>
        {
            var cells = cut.FindAll("td").Select(c => c.TextContent.Trim()).ToList();
            Assert.Contains("BTCUSDT isolated 1x", cells);
            Assert.Contains("Short", cells);
            Assert.Contains("kraken", cells);
        });
    }

    [Fact]
    public void The_orders_table_names_the_symbol_and_the_exchange()
    {
        using var h = Harness(orders: new[]
        {
            new OpenOrder("ord-1", "BTC/USD", OrderSide.Sell, OrderType.Limit, 1.0, 60_550, "NEW"),
        });
        var cut = Open(h);
        WaitForAccountRead(h);
        SelectTab(cut, "orders");

        cut.WaitForAssertion(() =>
        {
            // Scoped to the orders table, not the whole dialog: the positions table
            // also has an Exchange column and would satisfy a naive markup search.
            var headers = cut.FindAll("th").Select(t => t.TextContent.Trim()).ToList();
            Assert.Contains("Symbol", headers);
            Assert.Contains("Exchange", headers);

            var cells = cut.FindAll("td").Select(c => c.TextContent.Trim()).ToList();
            Assert.Contains("BTC/USD", cells);
            Assert.Contains("kraken", cells);
        });
    }

    [Fact]
    public void Action_buttons_name_their_verb_and_their_subject()
    {
        // "✕" is announced as "times", as "ex", or skipped entirely depending on the
        // screen reader; a bare "Close" does not say whether it closes the position or
        // the dialog. Both are on the money path, where a control whose name might be
        // nothing at all is the worst thing on the screen.
        using var h = Harness(
            orders: new[] { new OpenOrder("ord-1", "BTC/USD", OrderSide.Sell, OrderType.Limit, 1.0, 60_550, "NEW") },
            positions: new[] { new Position("ETHUSDT", 2.0, 3_000, 6_100, 100, 1.0, 0, MarginMode.None) });
        var cut = Open(h);
        WaitForAccountRead(h);

        cut.WaitForAssertion(() =>
        {
            var close = Assert.Single(Labels(cut).Where(l => l.StartsWith("Close position", StringComparison.Ordinal)));
            Assert.Contains("ETHUSDT", close);
        });

        SelectTab(cut, "orders");
        cut.WaitForAssertion(() =>
        {
            var cancel = Assert.Single(Labels(cut).Where(l => l.StartsWith("Cancel order", StringComparison.Ordinal)));
            Assert.Contains("BTC/USD", cancel);
        });
    }

    private static List<string> Labels(IRenderedComponent<TradingDashboardModal> cut) =>
        cut.FindAll("button").Select(b => b.GetAttribute("aria-label") ?? b.TextContent.Trim()).ToList();

    // ── Which accounts exist ─────────────────────────────────────────────────

    [Fact]
    public void Withdrawal_profiles_are_never_enumerated_as_trading_accounts()
    {
        // Nothing on the trading path may touch a withdrawal credential. A key that
        // can move funds off the venue does not become an account here even when it is
        // active and its provider trades.
        using var h = Harness(keys: new[] { Key("binance", "payout", withdrawal: true), Key("kraken") });
        var cut = Open(h);
        WaitForAccountRead(h);

        h.OrderService.Received().GetPositionsAsync("kraken");
        h.OrderService.DidNotReceive().GetPositionsAsync("binance");
        h.OrderService.DidNotReceive().GetBalancesAsync("binance");
    }

    [Fact]
    public void With_nothing_connected_the_tabs_say_so_rather_than_showing_an_empty_table()
    {
        // "No open orders." and "no account is connected" are different facts and a
        // trader acts differently on each. An empty table with no note is the first one
        // asserted when the second is true.
        using var h = Harness(keys: Array.Empty<ApiKeyConfig>());
        var cut = Open(h);

        cut.WaitForAssertion(() =>
            Assert.Contains("No trading account is connected", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void One_venue_failing_does_not_cost_the_others_their_rows()
    {
        // Per-account failure isolation. A venue that times out must cost that venue's
        // section its data and nothing else — and must say which venue it was, because
        // an unattributed "could not be read" is not actionable.
        using var h = Harness(keys: new[] { Key("kraken"), Key("binance") });
        h.OrderService.GetPositionsAsync("binance")
            .Returns<Task<ProviderResult<List<Position>>>>(_ => throw new TimeoutException("venue unreachable"));
        h.OrderService.GetPositionsAsync("kraken").Returns(
            ProviderResult<List<Position>>.Ok(new List<Position>
            {
                new("BTCUSDT", 1.0, 100, 105, 5, 1.0, 0, MarginMode.None),
            }));

        var cut = Open(h);
        WaitForAccountRead(h);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("BTCUSDT", cut.Markup);
            Assert.Contains("binance", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }
}
