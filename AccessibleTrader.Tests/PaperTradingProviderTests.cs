using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="PaperTradingProvider"/>, the simulated broker.
    /// Fills are driven by the live chart state (via <see cref="MockWorkspaceStore.EmitState"/>),
    /// so each test emits synthetic bars to move "live" price. State persists to
    /// paper_account.json under the injected <see cref="IPlatformPathService.AppDataDirectory"/> —
    /// each test class instance gets its own temp dir so tests never see each other's accounts.
    /// </summary>
    public sealed class PaperTradingProviderTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private readonly string _tempDir;

        public PaperTradingProviderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "atc-paper-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store, string? dir = null)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir ?? _tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        private static WorkspaceState StateWith(string symbol, double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", symbol, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(
                    new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        /// <summary>The quote-currency row. Balances also list held assets, so a
        /// cash assertion has to name the one it means.</summary>
        private static Balance Cash(List<Balance> balances) =>
            Assert.Single(balances, b => b.Asset == "USDT");

        private static List<OrderUpdate> Collect(PaperTradingProvider paper)
        {
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);
            return updates;
        }

        // ── Market fills ─────────────────────────────────────────────────────

        [Fact]
        public async Task Market_order_fills_immediately_at_live_close_price()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 2.0));

            Assert.StartsWith("paper-", id); // an order id, not a failure sentinel
            var fill = Assert.Single(updates);
            Assert.Equal(OrderStatus.Filled, fill.Status);
            Assert.Equal(100, fill.FilledPrice);   // fills at the live bar's Close
            Assert.Equal(2.0, fill.FilledQuantity);

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(2.0, pos.Quantity);
            Assert.Equal(100, pos.AveragePrice);
        }

        [Fact]
        public async Task Market_order_fails_when_symbol_has_no_live_price()
        {
            // No chart loaded → no live price → the paper broker refuses rather
            // than fabricating a fill price.
            var paper = Make(out _);

            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── Stop-loss trigger semantics ──────────────────────────────────────

        [Fact]
        public async Task Stop_loss_does_not_fire_while_price_stays_above_trigger()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));

            // Bar low 95.5 — above the stop, position must stay open.
            store.EmitState(StateWith(Btc, 100, 100.5, 95.5, 96));

            Assert.Single(await paper.GetPositionsAsync());
            Assert.Single(await paper.GetOpenOrdersAsync(Btc)); // resting stop still open
        }

        [Fact]
        public async Task Stop_loss_fires_on_exact_touch_of_trigger()
        {
            // Pins touch-equality semantics: the sell stop protecting a long fires
            // when bar.Low <= trigger — an EXACT touch (low == 95) is enough, no
            // penetration below required. Matches how real exchanges treat a stop.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));

            store.EmitState(StateWith(Btc, 100, 100.5, 95, 96)); // low touches 95 exactly

            var stopFill = updates.Single(u => u.StopTriggered);
            Assert.Equal(95, stopFill.FilledPrice);           // fills AT the trigger, no slippage (v1)
            Assert.Equal(-5.0, stopFill.RealizedPnL);         // (95 − 100) × 1
            Assert.Empty(await paper.GetPositionsAsync());    // position closed
        }

        [Fact]
        public async Task Stop_loss_fires_on_penetration_below_trigger()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));

            store.EmitState(StateWith(Btc, 100, 100.5, 92, 93)); // gaps through the stop

            var stopFill = updates.Single(u => u.StopTriggered);
            // v1 simplification pinned: the fill assumes the trigger price (95),
            // not the worse traded price — no gap slippage is simulated.
            Assert.Equal(95, stopFill.FilledPrice);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── Take-profit trigger ──────────────────────────────────────────────

        [Fact]
        public async Task Take_profit_fires_when_high_reaches_target()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, TakeProfit: 110));

            store.EmitState(StateWith(Btc, 100, 109.99, 100, 109)); // just short — no trigger
            Assert.DoesNotContain(updates, u => u.TakeProfitTriggered);

            store.EmitState(StateWith(Btc, 109, 110, 108, 109.5));  // high touches 110

            var tpFill = updates.Single(u => u.TakeProfitTriggered);
            Assert.Equal(110, tpFill.FilledPrice);
            Assert.Equal(10.0, tpFill.RealizedPnL);          // (110 − 100) × 1
            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── Resting limit orders ─────────────────────────────────────────────

        [Fact]
        public async Task Limit_buy_rests_until_price_trades_down_to_it()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 95));

            // Order rests; no fill emitted, shows in open orders.
            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
            var open = Assert.Single(await paper.GetOpenOrdersAsync(Btc));
            Assert.Equal(id, open.Id);

            store.EmitState(StateWith(Btc, 100, 100, 96, 97));   // low 96 > 95 — still resting
            Assert.Single(await paper.GetOpenOrdersAsync(Btc));

            store.EmitState(StateWith(Btc, 97, 97, 94, 94.5));   // low 94 crosses the limit

            var fill = updates.Single(u => u.Status == OrderStatus.Filled);
            Assert.Equal(95, fill.FilledPrice);                  // fills at the limit price
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(95, pos.AveragePrice);
        }

        // ── Position averaging ───────────────────────────────────────────────

        [Fact]
        public async Task Adding_to_a_position_averages_the_entry_price()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            store.EmitState(StateWith(Btc, 100, 111, 100, 110));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(2.0, pos.Quantity);
            // (100×1 + 110×1) / 2 — weighted average basis, not last price.
            Assert.Equal(105, pos.AveragePrice);
        }

        // ── Fees ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Taker_fee_is_deducted_from_cash_on_each_fill()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            var bal = Cash(await paper.GetBalancesAsync());
            // 100,000 − (1 × 100 notional) − (100 × 0.04% fee) = 99,899.96
            Assert.Equal(100_000.0 - 100.0 - 0.04, bal.Free, 8);

            var fill = Assert.Single(await paper.GetFillsAsync(Btc));
            Assert.Equal(0.04, fill.Fee, 8);
        }

        // ── Account reset ────────────────────────────────────────────────────

        [Fact]
        public async Task Reset_restores_starting_balance_and_wipes_positions_orders_history()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 90));

            paper.ResetAccount();

            var bal = Assert.Single(await paper.GetBalancesAsync());
            Assert.Equal(paper.StartingBalance, bal.Free);
            Assert.Equal(100_000.0, paper.StartingBalance);
            Assert.Empty(await paper.GetPositionsAsync());
            Assert.Empty(await paper.GetOpenOrdersAsync());
            Assert.Empty(await paper.GetFillsAsync());
        }

        // ── Persistence round-trip ───────────────────────────────────────────

        [Fact]
        public async Task Account_state_round_trips_through_paper_account_json()
        {
            // The state path comes from the injected IPlatformPathService, so a
            // temp dir gives a hermetic round-trip: trade in provider A, then a
            // fresh provider B on the same dir must load the identical account.
            var paperA = Make(out var storeA);
            storeA.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paperA.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 90));
            paperA.Dispose();
            Assert.True(File.Exists(Path.Combine(_tempDir, "paper_account.json")));

            var paperB = Make(out _);

            var bal = Cash(await paperB.GetBalancesAsync());
            Assert.Equal(100_000.0 - 100.0 - 0.04, bal.Free, 8);
            var pos = Assert.Single(await paperB.GetPositionsAsync());
            Assert.Equal(1.0, pos.Quantity);
            Assert.Equal(100, pos.AveragePrice);
            var stop = Assert.Single(await paperB.GetOpenOrdersAsync(Btc)); // resting stop survived
            Assert.Equal(OrderType.StopMarket, stop.Type);
            Assert.Single(await paperB.GetFillsAsync(Btc));
        }

        // ── Symbol scoping ───────────────────────────────────────────────────

        [Fact]
        public async Task Resting_order_for_another_symbol_never_fills_on_this_chart()
        {
            // v1 contract: resting orders only fill while THEIR symbol is the
            // loaded chart. A BTC bar crashing through an ETH limit price must
            // not fill the ETH order.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal("ETH/USDT", OrderSide.Buy, 1.0, OrderType.Limit, Price: 95));
            store.EmitState(StateWith(Btc, 100, 100, 50, 60)); // BTC low 50 « 95

            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
            var open = Assert.Single(await paper.GetOpenOrdersAsync("ETH/USDT"));
            Assert.Equal("ETH/USDT", open.Symbol);
            Assert.Empty(await paper.GetPositionsAsync());
        }
        // ── OCO pairs (2026-07-22, the OCO UI feature) ───────────────────────

        private async Task<(PaperTradingProvider Paper, MockWorkspaceStore Store, List<OrderUpdate> Updates,
            string LimitId, string StopId)> RestingOcoPairAsync()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            // Hold a position so the sell legs mean something; price 100.
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
            updates.Clear();

            string group = "oco-test1";
            string limitId = await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 1.0, OrderType.Limit, Price: 110, OcoGroupId: group));
            string stopId = await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 1.0, OrderType.StopMarket, TriggerPrice: 95, OcoGroupId: group));
            return (paper, store, updates, limitId, stopId);
        }

        [Fact]
        public async Task Oco_fill_of_one_leg_cancels_the_other()
        {
            var (paper, store, updates, limitId, stopId) = await RestingOcoPairAsync();

            // Price spikes to the limit: TP leg fills, stop leg must cancel.
            store.EmitState(StateWith(Btc, 100, 111, 99, 110));

            Assert.Contains(updates, u => u.OrderId == limitId && u.Status == OrderStatus.Filled);
            Assert.Contains(updates, u => u.OrderId == stopId && u.Status == OrderStatus.Cancelled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc)); // nothing left resting
        }

        [Fact]
        public async Task Oco_stop_leg_fill_cancels_the_limit_leg()
        {
            var (paper, store, updates, limitId, stopId) = await RestingOcoPairAsync();

            store.EmitState(StateWith(Btc, 100, 100, 94, 95)); // drops through the stop

            Assert.Contains(updates, u => u.OrderId == stopId && u.Status == OrderStatus.Filled);
            Assert.Contains(updates, u => u.OrderId == limitId && u.Status == OrderStatus.Cancelled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task Oco_manual_cancel_of_one_leg_cancels_the_pair()
        {
            var (paper, _, updates, limitId, stopId) = await RestingOcoPairAsync();

            Assert.True(await paper.CancelOrderAsync(limitId, Btc));

            Assert.Contains(updates, u => u.OrderId == stopId && u.Status == OrderStatus.Cancelled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task Oco_wide_bar_crossing_both_legs_fills_only_one()
        {
            // Regression fence: a single wide/gapping bar whose range spans BOTH the sell-limit
            // (110) and the sell-stop (95) must fill exactly ONE leg and cancel the other — not
            // fill both. Before the fix the snapshot loop filled both legs (long 1.0 → net short
            // 1.0, double fee) and emitted a spurious cancel on an already-filled order.
            var (paper, store, updates, limitId, stopId) = await RestingOcoPairAsync();

            store.EmitState(StateWith(Btc, 100, 111, 94, 100)); // high≥110 AND low≤95

            int filled = updates.Count(u => (u.OrderId == limitId || u.OrderId == stopId) && u.Status == OrderStatus.Filled);
            int cancelled = updates.Count(u => (u.OrderId == limitId || u.OrderId == stopId) && u.Status == OrderStatus.Cancelled);
            Assert.Equal(1, filled);
            Assert.Equal(1, cancelled);
            // The long 1.0 was closed by exactly one sell 1.0 → flat, NOT net short.
            Assert.Empty(await paper.GetPositionsAsync());
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task Oco_group_survives_a_restart()
        {
            var (paper, _, _, _, _) = await RestingOcoPairAsync();

            // Reload from disk into a fresh instance: the pair must still be linked.
            var reloaded = Make(out var store2);
            var updates2 = Collect(reloaded);
            store2.EmitState(StateWith(Btc, 100, 111, 99, 110)); // TP fills

            Assert.Contains(updates2, u => u.Status == OrderStatus.Filled);
            Assert.Contains(updates2, u => u.Status == OrderStatus.Cancelled);
            Assert.Empty(await reloaded.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task Ungrouped_orders_never_cancel_each_other()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
            updates.Clear();

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 0.5, OrderType.Limit, Price: 110));
            var stopId = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 0.5, OrderType.StopMarket, TriggerPrice: 95));

            store.EmitState(StateWith(Btc, 100, 111, 99, 110)); // limit fills

            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Cancelled);
            var open = await paper.GetOpenOrdersAsync(Btc);
            Assert.Single(open);
            Assert.Equal(stopId, open[0].Id); // the stop still rests
        }

        // ── Brackets: the two legs must cancel each other ────────────────────
        //
        // A bracket is an OCO pair whether or not the caller says so. Before this
        // was fixed the legs were attached with no group id, so the surviving leg
        // stayed live after its sibling closed the position — and then filled,
        // opening a brand new position in the opposite direction. On a demo
        // account left running that is the difference between a flat book and a
        // naked short nobody asked for.

        [Fact]
        public async Task Bracket_stop_fill_cancels_the_take_profit()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95, TakeProfit: 110));
            updates.Clear();

            store.EmitState(StateWith(Btc, 99, 99, 94, 94));    // stop hit — position closes

            Assert.Contains(updates, u => u.Status == OrderStatus.Cancelled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
            Assert.Empty(await paper.GetPositionsAsync());

            // The target must be gone. If it were still resting this bar would fill
            // it and leave a short.
            store.EmitState(StateWith(Btc, 108, 112, 107, 111));
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task Bracket_take_profit_fill_cancels_the_stop()
        {
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95, TakeProfit: 110));
            updates.Clear();

            store.EmitState(StateWith(Btc, 108, 112, 107, 111)); // target hit

            Assert.Contains(updates, u => u.Status == OrderStatus.Cancelled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
            Assert.Empty(await paper.GetPositionsAsync());

            store.EmitState(StateWith(Btc, 99, 99, 94, 94));     // would have hit the stop
            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── An account cannot spend or sell what it does not have ────────────

        [Fact]
        public async Task Selling_an_asset_the_account_does_not_hold_opens_a_collateralised_short()
        {
            // This USED to mint money: the sale credited cash for an asset that had
            // never been owned. It then briefly refused outright, because there is no
            // shorting without a borrow. Now the borrow is modelled — the proceeds and
            // an equal margin are locked — so the sell is a short, and it costs
            // collateral rather than paying out. Full arithmetic in PaperShortingTests.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            Assert.StartsWith("paper-", result);
            Assert.Equal(-1.0, (await paper.GetPositionsAsync()).Single().Quantity, 6);
            // Free cash FELL — the one thing that must never happen is it rising.
            Assert.True(Cash(await paper.GetBalancesAsync()).Free < 100_000.0);
        }

        [Fact]
        public async Task Selling_more_than_is_held_flips_into_a_short_for_the_excess()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 2.5));

            Assert.StartsWith("paper-", result);
            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(-1.5, pos.Quantity, 6);     // the long closed, the excess is short
        }

        [Fact]
        public async Task Resting_buy_that_the_account_can_no_longer_afford_is_cancelled_not_filled()
        {
            // Placement affordability is not fill affordability: the cash can be
            // spent elsewhere while the order rests. Filling anyway drove cash
            // negative, which no exchange would do.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            // Rest a buy needing 99,000, then spend the account down with a market buy.
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 990.0, OrderType.Limit, Price: 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 900.0));   // ~90,000 spent
            updates.Clear();

            store.EmitState(StateWith(Btc, 100, 101, 99, 100)); // limit is touched

            var balance = Cash(await paper.GetBalancesAsync());
            Assert.True(balance.Free >= 0, $"cash went negative: {balance.Free}");
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));  // dropped, not left resting
            Assert.Contains(updates, u => u.Status is OrderStatus.Cancelled or OrderStatus.Rejected);
        }

        [Fact]
        public async Task Balances_report_held_assets_and_not_only_quote_cash()
        {
            // The Balances tab is the demo's account view. A spot account holding
            // BTC has a BTC balance; reporting only the quote made half the
            // account invisible there.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 2.0));

            var balances = await paper.GetBalancesAsync();

            Assert.Contains(balances, b => b.Asset == "BTC" && Math.Abs(b.Free - 2.0) < 1e-9);
            Assert.Contains(balances, b => b.Asset == "USDT");
        }

        // ── The fee is part of what a fill costs ─────────────────────────────
        //
        // The taker fee used to be charged in RecordFill, after CanFill had already
        // approved the fill on the settlement alone. An order sized to exactly the
        // free cash therefore passed the check at 0 and then went negative by the
        // fee — and because CanFill refuses any order that would leave cash below
        // zero, the account was bricked for every trade afterwards. The fee now
        // settles inside Settle, so the number that is checked is the number that
        // is spent.

        [Fact]
        public async Task Maximal_market_buy_leaves_cash_non_negative_after_the_fee()
        {
            // 1,000 BTC at 100 is exactly the 100,000 account. Before the fix this
            // filled and left −40; now the fee is part of the cost, so it cannot.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1000.0));

            var cash = Cash(await paper.GetBalancesAsync());
            Assert.True(cash.Free >= 0, $"cash went negative: {cash.Free}");
        }

        [Fact]
        public async Task Rejects_an_order_whose_fee_alone_would_overdraw()
        {
            // The settlement leg fits to the cent; only the 40 fee does not. That is
            // the exact case the old check could not see.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1000.0));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Empty(await paper.GetPositionsAsync());
            Assert.Equal(100_000.0, Cash(await paper.GetBalancesAsync()).Free, 6);
        }

        [Fact]
        public async Task A_fill_that_fits_including_its_fee_is_still_accepted()
        {
            // The guard above must reject the unaffordable order, not the affordable
            // one one tick below it: 999 BTC at 100 costs 99,900 plus a 39.96 fee.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 999.0));

            Assert.StartsWith("paper-", id);
            Assert.Equal(100_000.0 - 99_900.0 - 39.96, Cash(await paper.GetBalancesAsync()).Free, 6);
        }

        [Fact]
        public async Task The_fee_is_charged_exactly_once_per_fill()
        {
            // Settle charges it and RecordFill only reports it. If a future edit
            // restores the subtraction in RecordFill, this is what catches it: the
            // cash movement and the fee on the history line must agree.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 10.0));

            var fill = Assert.Single(await paper.GetFillsAsync(Btc));
            Assert.Equal(0.4, fill.Fee, 6);                       // 10 × 100 × 0.04%
            Assert.Equal(100_000.0 - 1_000.0 - 0.4, Cash(await paper.GetBalancesAsync()).Free, 6);
        }

        [Fact]
        public async Task Liquidation_may_leave_cash_negative_and_reset_recovers_the_account()
        {
            // Liquidation does not consult CanFill — being wiped out is the lesson.
            // What must hold is that ruin is recoverable: ResetAccount restores a
            // working account rather than leaving a permanently unusable one.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 500.0));  // short at 100

            // Through the liquidation price (twice entry) — the collateral is gone.
            store.EmitState(StateWith(Btc, 190, 205, 189, 200));
            Assert.Empty(await paper.GetPositionsAsync());

            paper.ResetAccount();

            Assert.Equal(100_000.0, Cash(await paper.GetBalancesAsync()).Free, 6);
            var id = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
            Assert.StartsWith("paper-", id);
        }

        // ── A resting order cannot trade at a price the market has left ──────
        //
        // One defect with two signs. A buy stop below spot filled AT its trigger,
        // handing back the difference as risk-free profit; a buy limit above spot
        // filled AT its limit, charging the difference for nothing. Both came from
        // filling at the stated level without ever asking where the market was.

        [Fact]
        public async Task A_buy_stop_below_the_market_is_rejected_at_placement()
        {
            // Market 100, buy stop 50. This is not a slow order — it triggers next
            // bar and buys 50 under the asking price.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var result = await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.StopMarket, TriggerPrice: 50));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Contains("must be above", result);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task A_sell_stop_above_the_market_is_rejected_at_placement()
        {
            // The mirror. Placed above the market it shorts at a price above spot.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var result = await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Sell, 1.0, OrderType.StopMarket, TriggerPrice: 150));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Contains("must be below", result);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task A_stop_entry_on_the_correct_side_is_still_accepted_and_fills_at_its_trigger()
        {
            // The guard must reject the impossible order without breaking the real
            // one: a buy stop above the market, reached during a later bar, fills
            // exactly at its trigger.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.StopMarket, TriggerPrice: 120));
            Assert.StartsWith("paper-", id);

            // Opens below the trigger and trades up through it during the bar.
            store.EmitState(StateWith(Btc, 110, 125, 109, 120));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(120.0, pos.AveragePrice, 6);
        }

        [Fact]
        public async Task A_stop_the_market_gapped_through_fills_at_the_open_not_the_trigger()
        {
            // The bar opens past the trigger, so the market was never at 120 — it
            // was at 130. Filling at 120 would invent liquidity that never existed
            // and would teach that a stop caps a loss exactly, which it does not.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.StopMarket, TriggerPrice: 120));

            store.EmitState(StateWith(Btc, 130, 135, 129, 132));   // gapped straight through

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(130.0, pos.AveragePrice, 6);
        }

        [Fact]
        public async Task A_buy_limit_above_the_market_does_not_fill_above_the_market()
        {
            // The twin of the stop bug, opposite sign. A marketable limit is a legal
            // order — it is not rejected — but it fills at the market, not at the
            // generous price written on it. Before the fix this bought at 150 with
            // the market at 100 and charged the user 50 a coin for the privilege.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 150));
            Assert.StartsWith("paper-", id);

            store.EmitState(StateWith(Btc, 100, 101, 99, 100));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(100.0, pos.AveragePrice, 6);
        }

        [Fact]
        public async Task A_sell_limit_below_the_market_does_not_fill_below_the_market()
        {
            // The same mirror on the sell side: it sells at the market, not at the
            // low price it was written at.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 2.0));   // long at 100

            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Sell, 1.0, OrderType.Limit, Price: 50));
            store.EmitState(StateWith(Btc, 100, 101, 99, 100));

            var fill = Assert.Single(await paper.GetFillsAsync(Btc), f => f.Side == OrderSide.Sell);
            Assert.Equal(100.0, fill.Price, 6);
        }

        // ── The stop travels with the entry ──────────────────────────────────
        //
        // The bracket block used to sit inside the market-order branch, so a RESTING
        // entry carrying a stop dropped it silently. That is the documented primary
        // quick-trade workflow (Shift+Enter builds Type: Limit, Price: entry,
        // StopLoss: stop) and the position size is computed FROM the stop distance —
        // so the flagship flow placed a stop-derived size with no stop on it.

        [Fact]
        public async Task A_limit_entry_carrying_a_stop_attaches_it_when_the_entry_fills()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            // Exactly what QuickTradeExecutor builds on Shift+Enter.
            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 90, StopLoss: 80));

            // Nothing to protect yet — only the entry is resting.
            Assert.Single(await paper.GetOpenOrdersAsync(Btc));

            store.EmitState(StateWith(Btc, 100, 101, 85, 95));   // entry fills at 90

            Assert.Single(await paper.GetPositionsAsync());
            var stop = Assert.Single(await paper.GetOpenOrdersAsync(Btc));
            Assert.Equal(OrderSide.Sell, stop.Side);
            Assert.Equal(OrderType.StopMarket, stop.Type);
        }

        [Fact]
        public async Task A_stop_entry_carrying_a_target_attaches_it_when_the_entry_fills()
        {
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.StopMarket,
                TriggerPrice: 120, TakeProfit: 150));

            store.EmitState(StateWith(Btc, 110, 125, 109, 120));   // entry triggers

            Assert.Single(await paper.GetPositionsAsync());
            var target = Assert.Single(await paper.GetOpenOrdersAsync(Btc));
            Assert.Equal(OrderType.TakeProfitMarket, target.Type);
        }

        [Fact]
        public async Task A_stop_entry_using_StopLoss_as_its_trigger_does_not_also_stop_itself_out()
        {
            // StopLoss doubles as the entry trigger when no TriggerPrice is given.
            // Reading it a second time as a protective leg would place the stop
            // exactly at the entry — an instant round trip for a fee.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.StopMarket, StopLoss: 120));

            store.EmitState(StateWith(Btc, 110, 125, 109, 120));

            Assert.Single(await paper.GetPositionsAsync());
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));   // no self-cancelling stop
        }

        [Fact]
        public async Task A_pending_bracket_survives_a_restart()
        {
            // A resting entry now carries its protection as data. If that does not
            // round-trip through the account file, reopening the app turns every
            // pending bracket into an unprotected entry — the original bug wearing
            // the fix's clothes.
            var dir = Path.Combine(_tempDir, "restart");
            Directory.CreateDirectory(dir);

            var first = Make(out var store1, dir);
            store1.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await first.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 90, StopLoss: 80));
            first.Dispose();

            var reopened = Make(out var store2, dir);
            store2.EmitState(StateWith(Btc, 100, 101, 85, 95));   // entry fills after the restart

            Assert.Single(await reopened.GetPositionsAsync());
            var stop = Assert.Single(await reopened.GetOpenOrdersAsync(Btc));
            Assert.Equal(OrderType.StopMarket, stop.Type);
            reopened.Dispose();
        }

        // ── A protective leg is an exit, never an entry ──────────────────────

        [Fact]
        public async Task A_bracket_leg_left_behind_by_a_manual_close_is_cancelled_not_filled()
        {
            // Closing from the dashboard does not cancel the legs. Before reduce-only,
            // the stop later fired a sell with no position and OPENED A SHORT, then
            // cancelled its own target and announced "stop loss hit" for a trade it
            // had just created.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 90, TakeProfit: 110));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));   // closed by hand
            Assert.Empty(await paper.GetPositionsAsync());

            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 95, 96, 85, 88));      // the stop level is touched

            Assert.Empty(await paper.GetPositionsAsync());        // no short was opened
            Assert.Contains(updates, u => u.Status == OrderStatus.Cancelled);
            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));    // the target went too
        }

        [Fact]
        public async Task A_partial_close_clamps_the_leg_to_what_is_left()
        {
            // The legs still carry the ORIGINAL quantity. Filling all of it against a
            // half-sized position does not close the trade, it reverses it.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 2.0, StopLoss: 90));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));   // half closed
            Assert.Equal(1.0, Assert.Single(await paper.GetPositionsAsync()).Quantity, 6);

            store.EmitState(StateWith(Btc, 95, 96, 85, 88));      // stop fires, still sized 2

            Assert.Empty(await paper.GetPositionsAsync());        // flat, not short 1
        }

        [Fact]
        public async Task A_reduce_only_market_order_against_a_closed_position_does_not_reverse_it()
        {
            // The dashboard Close button's path. Arriving a moment late must not
            // become a fresh trade the other way.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            var result = await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Sell, 1.0, ReduceOnly: true));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public void Every_leg_AttachProtectiveLegs_can_emit_is_reduce_only()
        {
            // **The structural one.** Instance tests catch the legs that exist today;
            // this catches the leg somebody adds next year. It reaches into the open
            // book and asserts the property over EVERY resting order produced by two
            // entries that between them exercise all four leg branches — plain stop,
            // trailing stop, plain target, trailing target — so a new branch added
            // without ReduceOnly fails here rather than in production.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            // Branch 1 + 3: plain stop and plain target.
            paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0,
                StopLoss: 90, TakeProfit: 110)).GetAwaiter().GetResult();

            // Branch 2 + 4: trailing stop and trailing target (the trailing stop
            // branch is an `else` of the plain one, so it needs its own entry — and
            // the same symbol, since only this one has a live price).
            paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0,
                TrailStopMode: TrailMode.Percent, TrailStopValue: 5,
                TrailTpMode: TrailMode.Percent, TrailTpValue: 10)).GetAwaiter().GetResult();

            var open = OpenOrdersOf(paper);
            Assert.Equal(4, open.Count);   // all four branches fired; adjust only if a branch is added

            foreach (object leg in open)
            {
                var type = leg.GetType();
                bool reduceOnly = (bool)type.GetProperty("ReduceOnly")!.GetValue(leg)!;
                string id = (string)type.GetProperty("Id")!.GetValue(leg)!;
                Assert.True(reduceOnly,
                    $"protective leg {id} is not reduce-only — it can open a position instead of closing one");
            }
        }

        /// <summary>
        /// The provider's internal open book. Reached by reflection deliberately: the
        /// point of the test above is to assert something about every leg the code can
        /// construct, not about the ones the public surface happens to expose.
        /// </summary>
        private static List<object> OpenOrdersOf(PaperTradingProvider paper)
        {
            var field = typeof(PaperTradingProvider)
                .GetField("_open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            var list = (System.Collections.IEnumerable)field!.GetValue(paper)!;
            return list.Cast<object>().ToList();
        }

        [Fact]
        public async Task A_buy_limit_below_the_market_still_fills_at_its_limit()
        {
            // The ordinary case the fix must not disturb: a limit the market comes
            // down to fills at the limit, not at the bar's open.
            var paper = Make(out var store);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 90));
            store.EmitState(StateWith(Btc, 100, 101, 85, 95));   // trades down through 90

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(90.0, pos.AveragePrice, 6);
        }
    }
}
