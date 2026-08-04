using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
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
    /// A position does not stop existing because the user looked at another chart.
    ///
    /// <para>
    /// The paper fill engine was driven solely by <see cref="IWorkspaceStore"/>'s
    /// state stream — the FOCUSED chart. Every other tab was invisible to it, so a
    /// resting order there could never fill and an open position there priced
    /// itself off its own entry, showing a P&amp;L frozen at zero for as long as the
    /// user was elsewhere. The case this breaks is the one that matters most: a
    /// trader side tracked onto another chart with money still on this one.
    /// </para>
    ///
    /// <para>
    /// Background monitors were already fetching bars for unfocused tabs and
    /// spending them only on alerts and strategies. These tests pin that those
    /// bars now also reach the broker.
    /// </para>
    /// </summary>
    public sealed class PaperBackgroundFillTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private const string Eth = "ETH/USDT";
        private readonly string _tempDir;

        public PaperBackgroundFillTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "atc-paper-bg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store, out IEventBus bus, string? dir = null)
        {
            store = new MockWorkspaceStore();
            bus = new SpyEventBus();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir ?? _tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance, bus);
        }

        private static ChartIdentity Id(string symbol) => new("Spot", "Test", symbol, "1h");

        private static WorkspaceState StateWith(string symbol, double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = Id(symbol),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        private static Ohlcv Bar(double open, double high, double low, double close) =>
            new(DateTime.UtcNow, open, high, low, close, 1000);

        [Fact]
        public async Task A_stop_fires_on_a_chart_the_user_is_not_looking_at()
        {
            var paper = Make(out var store, out var bus);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);

            // Buy BTC with a stop, then navigate away to ETH.
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));
            store.EmitState(StateWith(Eth, 3000, 3010, 2990, 3005));
            updates.Clear();

            // BTC collapses while ETH is on screen. Before this, nothing happened
            // and the position rode all the way down unprotected.
            bus.Publish(new MonitoredBarEvent(Id(Btc), Bar(99, 99, 94, 94)));

            var fill = Assert.Single(updates, u => u.Status == OrderStatus.Filled);
            Assert.True(fill.StopTriggered);
            Assert.Equal(95, fill.FilledPrice);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task A_limit_order_fills_on_a_chart_the_user_is_not_looking_at()
        {
            var paper = Make(out var store, out var bus);

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 90));
            store.EmitState(StateWith(Eth, 3000, 3010, 2990, 3005));

            bus.Publish(new MonitoredBarEvent(Id(Btc), Bar(95, 96, 89, 91)));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(1.0, pos.Quantity, 6);
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task Unrealized_pnl_keeps_moving_while_the_chart_is_elsewhere()
        {
            // The old PriceFor fell back to the position's own average price for any
            // symbol that was not the focused chart, so an off-screen position
            // reported exactly zero P&L however far price had run.
            var paper = Make(out var store, out var bus);

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 2.0));
            store.EmitState(StateWith(Eth, 3000, 3010, 2990, 3005));

            Assert.Equal(0, (await paper.GetPositionsAsync()).Single().UnrealizedPnL, 6);

            bus.Publish(new MonitoredBarEvent(Id(Btc), Bar(100, 130, 100, 125)));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(50.0, pos.UnrealizedPnL, 6);   // 2 × (125 − 100)
            Assert.Equal(250.0, pos.MarketValue, 6);
        }

        [Fact]
        public async Task Exposure_is_reported_so_monitors_can_be_started_for_it()
        {
            var paper = Make(out var store, out _);

            Assert.Empty(paper.ExposedIdentities());

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            var exposed = Assert.Single(paper.ExposedIdentities());
            Assert.Equal(Btc, exposed.Symbol);
            Assert.Equal("1h", exposed.Timeframe);
            Assert.Equal("Test", exposed.Provider);
        }

        [Fact]
        public async Task Exposure_survives_a_restart_so_a_forgotten_position_is_still_watched()
        {
            // The tab is gone and the app has been closed and reopened. The identity
            // has to come off disk or there is no way to price the position at all —
            // and a position nobody remembers is precisely the one still open.
            var paperA = Make(out var storeA, out _);
            storeA.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paperA.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));
            paperA.Dispose();

            var paperB = Make(out _, out var busB);

            var exposed = Assert.Single(paperB.ExposedIdentities());
            Assert.Equal(Btc, exposed.Symbol);
            Assert.Equal("1h", exposed.Timeframe);

            // And it still fills, with no chart ever loaded in this session.
            busB.Publish(new MonitoredBarEvent(Id(Btc), Bar(99, 99, 94, 94)));
            Assert.Empty(await paperB.GetPositionsAsync());
        }

        [Fact]
        public async Task A_bar_for_an_unrelated_symbol_moves_nothing()
        {
            var paper = Make(out var store, out var bus);

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95));

            // An ETH crash must not fire a BTC stop.
            bus.Publish(new MonitoredBarEvent(Id(Eth), Bar(3000, 3000, 1, 1)));

            Assert.Single(await paper.GetPositionsAsync());
            Assert.Single(await paper.GetOpenOrdersAsync(Btc));
        }
    }
}
