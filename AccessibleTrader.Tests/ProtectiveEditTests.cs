using System.Reactive.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Moving a stop or target must not sound an alarm, and must not take the other one away.</b>
    ///
    /// <para>
    /// Reported 2026-11-05 from a paper position opened with both a stop loss and a take profit:
    /// editing the stop on the Positions tab "worked", but the error sound played and an error
    /// was shown. Two defects were behind it.
    /// </para>
    ///
    /// <para>
    /// <b>The alarm.</b> <c>NormaliseTrigger</c> copies a stop's trigger into <c>StopLoss</c>, so
    /// the new stop looked like an entry carrying a bracket, and two seconds later the bracket
    /// check — which counts only OPPOSITE-side protective orders — found nothing and reported
    /// "no stop loss or take profit found ... may be unprotected" at High severity. On every venue.
    /// </para>
    ///
    /// <para>
    /// <b>The missing target.</b> Legs placed together are one-cancels-other, and the link cannot
    /// tell "the stop filled" from "the user moved the stop": cancelling the old stop cancelled
    /// the take profit too, silently.
    /// </para>
    ///
    /// <para>
    /// Both are driven here through the real <see cref="GeneralOrderService"/> over the real
    /// <see cref="PaperTradingProvider"/>, the path the Positions tab takes.
    /// </para>
    /// </summary>
    public sealed class ProtectiveEditTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private readonly string _tempDir;

        public ProtectiveEditTests()
        {
            _tempDir = TestTemp.NewPath("atc-protective-edit-");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private static WorkspaceState Bar(double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        /// <summary>The order service in paper mode over a real paper broker, errors captured.</summary>
        private (GeneralOrderService Svc, PaperTradingProvider Paper, MockWorkspaceStore Store, IGlobalErrorCoordinator Err)
            PaperService()
        {
            var store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_tempDir);
            var paper = new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var svc = new GeneralOrderService(Substitute.For<IDataService>(), err,
                NullLogger<GeneralOrderService>.Instance, new EventBus(), paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(HostMode.Hosted), new QuickTradeEquity());
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            return (svc, paper, store, err);
        }

        /// <summary>A long of 1 at 100 with a stop at 95 and a target at 110, as one order.</summary>
        private static async Task<(OpenOrder Stop, OpenOrder Target)> OpenBracketedLong(
            GeneralOrderService svc, PaperTradingProvider paper, MockWorkspaceStore store)
        {
            store.EmitState(Bar(100, 101, 99, 100));
            var entry = await svc.PlaceOrderAsync("Paper",
                new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market, StopLoss: 95, TakeProfit: 110));
            Assert.True(entry.Succeeded, entry.FailureMessage);
            var open = await paper.GetOpenOrdersAsync(Btc);
            return (open.Single(o => o.Type == OrderType.StopMarket),
                    open.Single(o => o.Type == OrderType.TakeProfitMarket));
        }

        private static Task<ProtectiveEditResult> Move(GeneralOrderService svc, ProtectiveLevel level, double price,
                                                       OpenOrder existing, OpenOrder? other) =>
            ProtectiveOrderEditor.ReplaceAsync(svc, "Paper", Btc, isLong: true, quantity: 1.0,
                level, price, existing, other, $"{ProtectiveOrderEditor.NameOf(level)} set to {price}.");

        private static bool SaidUnprotected(IGlobalErrorCoordinator err) =>
            err.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IGlobalErrorCoordinator.ReportError)
                && ((string)c.GetArguments()[0]!).Contains("no stop loss or take profit found"));

        // ── The alarm ────────────────────────────────────────────────────────

        [Fact]
        public async Task Moving_the_stop_does_not_report_the_position_as_unprotected()
        {
            var (svc, paper, store, err) = PaperService();
            var (stop, target) = await OpenBracketedLong(svc, paper, store);

            var result = await Move(svc, ProtectiveLevel.StopLoss, 96, stop, target);
            Assert.True(result.Ok, result.Message);

            // The scan is fire-and-forget; give it far longer than its (zeroed) delay.
            await Task.Delay(500);
            Assert.False(SaidUnprotected(err),
                "A successful stop edit was followed by \"no stop loss or take profit found\" — the "
              + "new stop was read as an entry with a bracket and checked for protection against itself.");
        }

        [Fact]
        public async Task The_unprotected_alarm_still_fires_for_an_entry_whose_bracket_never_arrived()
        {
            // The control: the test above waits a fixed 500 ms for an alarm that must not come,
            // which proves nothing unless an alarm that MUST come arrives inside that time.
            var (svc, tp, err) = SubstituteVenue(openOrders: new List<OpenOrder>());

            var placed = await svc.PlaceOrderAsync("Binance",
                new TradeSignal(Btc, OrderSide.Buy, 0.01, OrderType.Market, StopLoss: 95));
            Assert.True(placed.Succeeded);

            await Task.Delay(500);
            Assert.True(SaidUnprotected(err), "the bracket check did not run inside the wait the tests above rely on");
            _ = tp;
        }

        [Fact]
        public async Task A_reduce_only_stop_on_a_live_venue_is_not_checked_for_a_bracket()
        {
            // The same edit as it reaches a real venue: the exchange shows the new stop and
            // nothing else, which the bracket check counts as "no protection".
            var (svc, _, err) = SubstituteVenue(openOrders: new List<OpenOrder>
            {
                new("SL2", Btc, OrderSide.Sell, OrderType.StopMarket, 1.0, 96, "open"),
            });

            await svc.PlaceOrderAsync("Binance", ProtectiveOrderEditor.SignalFor(Btc, true, 1.0, ProtectiveLevel.StopLoss, 96));
            await svc.PlaceOrderAsync("Binance", ProtectiveOrderEditor.SignalFor(Btc, true, 1.0, ProtectiveLevel.TakeProfit, 111));

            await Task.Delay(500);
            Assert.False(SaidUnprotected(err));
        }

        [Theory]
        [InlineData(OrderType.Market, false, 95.0, null, null, true)]           // an entry with a stop
        [InlineData(OrderType.Limit, false, null, 110.0, null, true)]           // an entry with a target
        [InlineData(OrderType.Market, false, null, null, null, false)]          // a bare entry
        [InlineData(OrderType.StopMarket, true, 96.0, null, 96.0, false)]       // a moved stop
        [InlineData(OrderType.TakeProfitMarket, true, null, 111.0, 111.0, false)] // a moved target
        [InlineData(OrderType.StopMarket, false, 96.0, null, 96.0, false)]      // a stop entry, no bracket
        [InlineData(OrderType.StopMarket, false, 90.0, null, 96.0, true)]       // a stop entry with its own stop
        [InlineData(OrderType.StopMarket, false, 96.0, 120.0, 96.0, true)]      // a stop entry with a target
        [InlineData(OrderType.TakeProfitMarket, false, 90.0, 111.0, 111.0, true)]
        [InlineData(OrderType.Market, true, 95.0, null, null, false)]           // reduce-only closes: nothing to protect
        public void Only_an_order_that_asks_for_protective_legs_carries_a_bracket(
            OrderType type, bool reduceOnly, double? stopLoss, double? takeProfit, double? trigger, bool expected)
        {
            var s = new TradeSignal(Btc, OrderSide.Sell, 1.0, type, StopLoss: stopLoss, TakeProfit: takeProfit,
                                    TriggerPrice: trigger, ReduceOnly: reduceOnly);
            Assert.Equal(expected, GeneralOrderService.CarriesBracket(s));
            Assert.Equal(expected, GeneralOrderService.CarriesBracket(GeneralOrderService.NormaliseTrigger(s)));
        }

        // ── The other leg ────────────────────────────────────────────────────

        [Fact]
        public async Task Moving_the_stop_keeps_the_take_profit()
        {
            var (svc, paper, store, _) = PaperService();
            var (stop, target) = await OpenBracketedLong(svc, paper, store);

            var result = await Move(svc, ProtectiveLevel.StopLoss, 96, stop, target);

            var open = await paper.GetOpenOrdersAsync(Btc);
            Assert.Contains(open, o => o.Type == OrderType.StopMarket && o.Price == 96);
            Assert.DoesNotContain(open, o => o.Type == OrderType.StopMarket && o.Price == 95);
            Assert.True(open.Any(o => o.Type == OrderType.TakeProfitMarket && o.Price == 110),
                "Moving the stop removed the take profit: the old stop's one-cancels-other link took it along.");
            Assert.True(result.Ok, result.Message);
            Assert.Contains("take profit at 110", result.Message);
            Assert.Contains("placed again", result.Message);
        }

        [Fact]
        public async Task Moving_the_take_profit_keeps_the_stop()
        {
            var (svc, paper, store, _) = PaperService();
            var (stop, target) = await OpenBracketedLong(svc, paper, store);

            var result = await Move(svc, ProtectiveLevel.TakeProfit, 112, target, stop);

            var open = await paper.GetOpenOrdersAsync(Btc);
            Assert.Contains(open, o => o.Type == OrderType.TakeProfitMarket && o.Price == 112);
            Assert.True(open.Any(o => o.Type == OrderType.StopMarket && o.Price == 95),
                "Moving the take profit removed the stop loss, leaving the position unprotected.");
            Assert.Equal(2, open.Count);
            Assert.True(result.Ok, result.Message);
        }

        [Fact]
        public async Task The_kept_target_still_closes_the_position_and_retires_the_moved_stop()
        {
            // The re-placed target is a real protective order, not a display: price reaching it
            // closes the position, and the stop is then retired as protecting nothing.
            var (svc, paper, store, _) = PaperService();
            var (stop, target) = await OpenBracketedLong(svc, paper, store);
            await Move(svc, ProtectiveLevel.StopLoss, 96, stop, target);

            store.EmitState(Bar(100, 112, 100, 111));

            Assert.Empty(await paper.GetPositionsAsync());
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        [Fact]
        public async Task An_unlinked_target_is_left_alone_and_nothing_extra_is_said()
        {
            // Stop and target set by hand, separately: no link, so the cancel takes nothing with it
            // and the target must not be placed a second time.
            var (svc, paper, store, _) = PaperService();
            store.EmitState(Bar(100, 101, 99, 100));
            await svc.PlaceOrderAsync("Paper", new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            await svc.PlaceOrderAsync("Paper", ProtectiveOrderEditor.SignalFor(Btc, true, 1.0, ProtectiveLevel.StopLoss, 95));
            await svc.PlaceOrderAsync("Paper", ProtectiveOrderEditor.SignalFor(Btc, true, 1.0, ProtectiveLevel.TakeProfit, 110));
            var before = await paper.GetOpenOrdersAsync(Btc);
            var stop = before.Single(o => o.Type == OrderType.StopMarket);
            var target = before.Single(o => o.Type == OrderType.TakeProfitMarket);

            var result = await Move(svc, ProtectiveLevel.StopLoss, 96, stop, target);

            var after = await paper.GetOpenOrdersAsync(Btc);
            Assert.Equal(2, after.Count);
            Assert.Contains(after, o => o.Id == target.Id);
            Assert.Equal("stop loss set to 96.", result.Message);
        }

        // ── What is said when it does not go to plan ─────────────────────────

        [Fact]
        public async Task A_refused_replacement_cancels_nothing_and_says_the_old_stop_stands()
        {
            var orders = Substitute.For<IOrderExecutionService>();
            orders.PlaceOrderAsync(default!, default!).ReturnsForAnyArgs(OrderPlacement.Parse("ORDER_FAILED: venue said no"));
            var stop = new OpenOrder("S1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 95, "open");

            var result = await ProtectiveOrderEditor.ReplaceAsync(orders, "Kraken", Btc, true, 1,
                ProtectiveLevel.StopLoss, 96, stop, null, "ok");

            Assert.False(result.Ok);
            Assert.Contains("existing stop loss at 95", result.Message);
            await orders.DidNotReceiveWithAnyArgs().CancelOrderAsync(default!, default!, default!);
        }

        [Fact]
        public async Task An_old_stop_that_will_not_cancel_is_reported_as_two_stops_resting()
        {
            var orders = Substitute.For<IOrderExecutionService>();
            orders.PlaceOrderAsync(default!, default!).ReturnsForAnyArgs(OrderPlacement.Parse("NEW-1"));
            orders.CancelOrderAsync(default!, default!, default!).ReturnsForAnyArgs(false);
            var stop = new OpenOrder("S1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 95, "open");

            var result = await ProtectiveOrderEditor.ReplaceAsync(orders, "Kraken", Btc, true, 1,
                ProtectiveLevel.StopLoss, 96, stop, null, "Stop loss set to 96.");

            Assert.False(result.Ok);
            Assert.Contains("both are resting", result.Message);
        }

        [Fact]
        public async Task A_target_that_cannot_be_put_back_is_said_to_be_gone()
        {
            var orders = Substitute.For<IOrderExecutionService>();
            orders.PlaceOrderAsync(default!, default!).ReturnsForAnyArgs(
                OrderPlacement.Parse("NEW-1"), OrderPlacement.Parse("ORDER_FAILED: rate limited"));
            orders.CancelOrderAsync(default!, default!, default!).ReturnsForAnyArgs(true);
            orders.GetOpenOrdersAsync(default!, default).ReturnsForAnyArgs(
                ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>
                    { new("NEW-1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 96, "open") }));
            var stop = new OpenOrder("S1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 95, "open");
            var target = new OpenOrder("T1", Btc, OrderSide.Sell, OrderType.TakeProfitMarket, 1, 110, "open");

            var result = await ProtectiveOrderEditor.ReplaceAsync(orders, "Kraken", Btc, true, 1,
                ProtectiveLevel.StopLoss, 96, stop, target, "Stop loss set to 96.");

            Assert.False(result.Ok);
            Assert.Contains("This position has no take profit now", result.Message);
        }

        [Fact]
        public async Task Another_take_profit_on_the_symbol_is_not_mistaken_for_the_one_that_was_lost()
        {
            // The leg is looked for by its order id. A second, smaller target resting on the same
            // symbol is not this position's target, and finding "a take profit" is not finding it.
            var orders = Substitute.For<IOrderExecutionService>();
            orders.PlaceOrderAsync(default!, default!).ReturnsForAnyArgs(OrderPlacement.Parse("NEW-1"));
            orders.CancelOrderAsync(default!, default!, default!).ReturnsForAnyArgs(true);
            orders.GetOpenOrdersAsync(default!, default).ReturnsForAnyArgs(
                ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>
                {
                    new("NEW-1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 96, "open"),
                    new("T7", Btc, OrderSide.Sell, OrderType.TakeProfitMarket, 0.2, 130, "open"),
                }));
            var stop = new OpenOrder("S1", Btc, OrderSide.Sell, OrderType.StopMarket, 1, 95, "open");
            var target = new OpenOrder("T1", Btc, OrderSide.Sell, OrderType.TakeProfitMarket, 1, 110, "open");

            var result = await ProtectiveOrderEditor.ReplaceAsync(orders, "Kraken", Btc, true, 1,
                ProtectiveLevel.StopLoss, 96, stop, target, "Stop loss set to 96.");

            Assert.Contains("placed again", result.Message);
            await orders.Received(2).PlaceOrderAsync("Kraken", Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task The_put_back_target_carries_its_own_client_id_so_the_duplicate_gate_lets_it_through()
        {
            // A target moved ten seconds ago and then cancelled by a stop edit is the same order
            // again inside the duplicate gate's 30 seconds; without its own id it is refused as a
            // double-fire and the position is left without it.
            var (svc, tp, err) = SubstituteVenue(openOrders: new List<OpenOrder>
            {
                new("SL2", Btc, OrderSide.Sell, OrderType.StopMarket, 1.0, 96, "open"),   // the target is gone
            });
            tp.CancelOrderAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);

            // The target was moved to 111 moments ago...
            var moved = await svc.PlaceOrderAsync("Binance",
                ProtectiveOrderEditor.SignalFor(Btc, true, 1.0, ProtectiveLevel.TakeProfit, 111));
            Assert.True(moved.Succeeded);
            var stop = new OpenOrder("SL1", Btc, OrderSide.Sell, OrderType.StopMarket, 1.0, 95, "open");
            var target = new OpenOrder(moved.OrderId!, Btc, OrderSide.Sell, OrderType.TakeProfitMarket, 1.0, 111, "open");

            // ...and the stop edit's cancel took it along.
            var result = await ProtectiveOrderEditor.ReplaceAsync(svc, "Binance", Btc, true, 1.0,
                ProtectiveLevel.StopLoss, 96, stop, target, "Stop loss set to 96.");

            Assert.True(result.Ok, result.Message);
            Assert.Contains("placed again", result.Message);
            Assert.DoesNotContain(err.ReceivedCalls(), c =>
                c.GetArguments().FirstOrDefault() is string m && m.StartsWith("Duplicate order suppressed"));
        }

        private static (GeneralOrderService Svc, ITradingProvider Tp, IGlobalErrorCoordinator Err)
            SubstituteVenue(List<OpenOrder> openOrders)
        {
            var data = Substitute.For<IDataService>();
            var provider = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp = (ITradingProvider)provider;
            tp.IsConnected.Returns(true);
            tp.HasPracticeEnvironment.Returns(true);
            tp.SupportsOrderEventStreaming.Returns(true);
            tp.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK-" + Guid.NewGuid().ToString("N")[..6]));
            tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(openOrders));
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(provider));
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            paper.HasPracticeEnvironment.Returns(true);
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var svc = new GeneralOrderService(data, err, NullLogger<GeneralOrderService>.Instance, new EventBus(), paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            return (svc, tp, err);
        }
    }
}
