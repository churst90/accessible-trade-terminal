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

            var bal = Assert.Single(await paper.GetBalancesAsync());
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

            var bal = Assert.Single(await paperB.GetBalancesAsync());
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

    }
}
