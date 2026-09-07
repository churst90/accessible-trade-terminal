using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
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
    /// <b>A stop you set by hand from the positions table must be PROTECTION, not a new trade.</b>
    ///
    /// <para>
    /// Reported from paper trading 2026-09-06: setting a stop on a long "switches the position to
    /// a sell position and cancels the order". It was not a display bug and the side was not
    /// wrong — a stop on a long IS a sell. The defect is that the order carried no
    /// <c>ReduceOnly</c> flag, so it was not a protective order at all; it was a plain sell stop
    /// resting under a long. Fire it when the position is already closed — or for more than
    /// remains after a partial — and it opens a SHORT.
    /// </para>
    ///
    /// <para>
    /// <b>The broker has guarded this since the bracket work</b> ("a sell with no position became
    /// a short", <c>PaperTradingProvider</c> line ~815), and the guard runs on
    /// <c>ReduceOnly</c> orders only. The legs attached at ENTRY set the flag. The positions
    /// table's inline editor never did. One rule, two call sites, applied at one of them — the
    /// same shape as the N08/N09 mutation survivors, where a guard copied to four sites was
    /// killed by a test that knew about one.
    /// </para>
    ///
    /// <para>
    /// These tests drive the BROKER with the signal the editor builds, rather than the Razor
    /// component, because what matters is the order that reaches the venue. A bUnit test of the
    /// dialog would pass with the flag missing.
    /// </para>
    /// </summary>
    public sealed class HandSetProtectiveOrderTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private readonly string _tempDir;

        public HandSetProtectiveOrderTests()
        {
            _tempDir = TestTemp.NewPath("atc-protective-tests-");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        private static WorkspaceState At(double price) => WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
            Data = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, price, price, price, price, 1000)),
        };

        private static WorkspaceState Bar(double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(
                    new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        /// <summary>
        /// Exactly the signal <c>TradingDashboardModal.CommitProtectiveAsync</c> builds for a
        /// hand-typed stop on a long. Kept here in one place so a change to the editor that
        /// dropped the flag again would have to change this line too.
        /// </summary>
        private static TradeSignal HandSetStopOnLong(double trigger, double quantity) => new(
            Symbol: Btc,
            Side: OrderSide.Sell,
            Quantity: quantity,
            Type: OrderType.StopMarket,
            Price: null,
            TriggerPrice: trigger,
            ReduceOnly: true);

        // ── The reversal ─────────────────────────────────────────────────────

        [Fact]
        public async Task A_hand_set_stop_does_not_open_a_short_when_the_position_is_already_closed()
        {
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            // Long 1 BTC, then a stop under it — the exact sequence from the report.
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            string stopId = await paper.PlaceOrderAsync(HandSetStopOnLong(79_000, 1.0));
            Assert.DoesNotContain("ORDER_FAILED", stopId);

            // The position is closed by hand — which does NOT cancel the resting stop.
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0, OrderType.Market));
            Assert.Empty(await paper.GetPositionsAsync());

            // Now the market falls through the stop.
            store.EmitState(Bar(80_000, 80_000, 78_000, 78_500));

            var positions = await paper.GetPositionsAsync();
            Assert.True(positions.Count == 0,
                "The stop fired with no position to protect and OPENED A SHORT. A protective order "
              + "must be reduce-only; without the flag it is just a sell stop, and this is the "
              + "'my stop flipped me into a short' defect.");
        }

        [Fact]
        public async Task A_hand_set_stop_larger_than_the_remaining_position_does_not_flip_it_through_flat()
        {
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            await paper.PlaceOrderAsync(HandSetStopOnLong(79_000, 1.0));

            // Half the position is closed by hand; the stop still carries the ORIGINAL size.
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 0.5, OrderType.Market));

            store.EmitState(Bar(80_000, 80_000, 78_000, 78_500));

            var positions = await paper.GetPositionsAsync();
            double held = positions.FirstOrDefault(p =>
                p.Symbol.Equals(Btc, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0.0;

            Assert.True(held >= -1e-9,
                $"The oversized stop sold through flat into a short of {held}. A reduce-only order "
              + "may close a position and may never reverse it.");
        }

        [Fact]
        public async Task A_hand_set_stop_still_protects_the_position_it_was_set_for()
        {
            // The control. Every assertion above is about what must NOT happen, and a reduce-only
            // flag that refused everything would satisfy all of them — so this pins that the stop
            // still does its job.
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            await paper.PlaceOrderAsync(HandSetStopOnLong(79_000, 1.0));

            store.EmitState(Bar(80_000, 80_000, 78_000, 78_500));

            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── The trigger price ────────────────────────────────────────────────

        [Fact]
        public async Task The_level_is_carried_as_a_TRIGGER_and_reaches_the_venue_as_one()
        {
            // It used to be passed as StopLoss, which on a StopMarket order means "attach a
            // protective stop to this stop". The paper broker's TriggerPrice ?? StopLoss ?? Price
            // fallback hid that; a plugin reading TriggerPrice — the documented field — would have
            // received null and rested a stop with no trigger at all.
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            await paper.PlaceOrderAsync(HandSetStopOnLong(79_000, 1.0));

            var resting = await paper.GetOpenOrdersAsync();
            var stop = Assert.Single(resting, o => o.Type == OrderType.StopMarket);
            Assert.Equal(79_000, stop.Price, 6);
        }

        [Fact]
        public async Task A_hand_set_stop_carries_no_bracket_of_its_own()
        {
            // "StopLoss on a StopMarket" attaches a protective leg TO THE STOP — a bracket on a
            // bracket, at the same price. Checking the resting orders BEFORE the fill cannot see
            // it: a bracket spec rides along on the order and only becomes a resting leg when
            // that order executes. So the stop has to actually fire.
            //
            // (This test was green against the defect until the sabotage pass said so. It is the
            // repo's own lesson twice over — assert the artifact, and prove the guard fails.)
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));
            await paper.PlaceOrderAsync(HandSetStopOnLong(79_000, 1.0));
            Assert.Single(await paper.GetOpenOrdersAsync());

            // The market falls through the stop: it fills and closes the position.
            store.EmitState(Bar(80_000, 80_000, 78_000, 78_500));

            Assert.Empty(await paper.GetPositionsAsync());

            var left = await paper.GetOpenOrdersAsync();
            Assert.True(left.Count == 0,
                $"The stop attached a protective leg to ITSELF and left {left.Count} order(s) "
              + "resting after the position was closed — a stop on a stop, at the same price.");
        }

        [Fact]
        public async Task A_stop_ENTRY_does_not_acquire_a_protective_stop_at_its_own_entry_price()
        {
            // The case normalisation could have broken, and the one the original comment in
            // PaperTradingProvider warned about: "reusing it as a protective leg would put the
            // stop exactly at the entry".
            //
            // Before normalisation a stop entry arrived with StopLoss set and TriggerPrice null,
            // and the broker nulled StopLoss so it could not double as a bracket. Now BOTH fields
            // are populated, so a test of `TriggerPrice == null` no longer recognises the case —
            // the rule has to be "on a stop-type order the level IS the trigger", regardless of
            // which field carried it. An entry that fills and immediately rests a stop at its own
            // fill price is a position that closes itself on the next bar having paid two fees.
            //
            // Not reduce-only, deliberately: the fill path already refuses to attach a bracket to
            // a reduce-only order, so a protective stop could never show this. Only an ENTRY can.
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: Btc, Side: OrderSide.Buy, Quantity: 1.0, Type: OrderType.StopMarket,
                TriggerPrice: 81_000, StopLoss: 81_000));   // as GeneralOrderService normalises it

            // The breakout happens: the entry triggers and fills.
            store.EmitState(Bar(80_000, 82_000, 80_000, 81_500));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.True(pos.Quantity > 0, "the stop entry should have opened a long");

            var resting = await paper.GetOpenOrdersAsync();
            Assert.True(resting.Count == 0,
                $"The stop entry attached a protective stop AT ITS OWN ENTRY PRICE ({resting.Count} "
              + "order(s) resting). The next bar that trades through 81,000 closes the position "
              + "the entry just opened.");

            // ── And it must not COMPLAIN about a leg it was never asked to attach ──────
            // A downstream guard (ReportIfCrossed) already refuses a stop sitting at the entry,
            // so the leg never rests either way — which is why an "is anything resting?"
            // assertion alone stays green against the defect. What differs is the ANNOUNCEMENT:
            // the refusal is emitted as a Rejected update carrying a reason, and the speech layer
            // says it. Without the fix, every stop entry that fills tells the trader their stop
            // loss was not attached, about a stop loss they never set.
            Assert.DoesNotContain(updates, u =>
                u.Status == OrderStatus.Rejected && (u.Reason ?? "").Contains("stop loss"));
        }

        // ── Orphaned protection: the "stop I never set" ──────────────────────

        [Fact]
        public async Task Closing_a_position_retires_the_stop_that_was_protecting_it()
        {
            // From Cody's real paper account, 2026-09-06: a stop set in AUGUST under a position
            // closed weeks earlier was still resting, and the positions table — which finds a
            // symbol's stop by matching SYMBOL and order type — displayed it as the stop for a
            // position opened that morning at a completely different price. Hence "it puts an
            // arbitrary stop way below the current price that I never set".
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, OrderType.Market, StopLoss: 79_000, TakeProfit: 81_000));
            Assert.Equal(2, (await paper.GetOpenOrdersAsync()).Count);

            // Close it by hand, the way the dashboard's Close position button does.
            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 1.0, OrderType.Market, ReduceOnly: true));

            Assert.Empty(await paper.GetPositionsAsync());
            var left = await paper.GetOpenOrdersAsync();
            Assert.True(left.Count == 0,
                $"{left.Count} protective order(s) outlived the position they were attached to. "
              + "The next trade in this symbol inherits them: the table shows a stop the trader "
              + "never set for it, and editing that cell edits the OLD order.");
        }

        [Fact]
        public async Task A_pending_ENTRY_in_the_same_symbol_survives_the_close()
        {
            // The discrimination that makes the rule safe. Only reduce-only orders are
            // protection; a resting entry waiting to open a NEW trade is not, and cancelling a
            // trader's pending entry because an unrelated position closed would be its own bug.
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, OrderType.Market, StopLoss: 79_000));

            // A limit buy well below the market — a separate trade the user is waiting on.
            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, OrderType.Limit, Price: 70_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 1.0, OrderType.Market, ReduceOnly: true));

            var left = await paper.GetOpenOrdersAsync();
            var kept = Assert.Single(left);
            Assert.Equal(OrderType.Limit, kept.Type);
            Assert.Equal(70_000, kept.Price, 6);
        }

        [Fact]
        public async Task A_partial_close_keeps_the_protection_on_what_is_still_held()
        {
            // Flat is the trigger, not "a sell happened". Retiring the stop on a partial close
            // would leave the remainder unprotected — the failure this whole area is about.
            var paper = Make(out var store);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, OrderType.Market, StopLoss: 79_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 0.4, OrderType.Market, ReduceOnly: true));

            Assert.NotEmpty(await paper.GetPositionsAsync());
            Assert.Single(await paper.GetOpenOrdersAsync());
        }

        [Fact]
        public async Task The_retirement_says_WHY_rather_than_the_stop_just_vanishing()
        {
            // A protective order disappearing with no explanation is indistinguishable from one
            // that was never placed — and on a screen nobody can see, that is the whole story.
            var paper = Make(out var store);
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);
            store.EmitState(At(80_000));

            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, OrderType.Market, StopLoss: 79_000));
            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Sell, 1.0, OrderType.Market, ReduceOnly: true));

            Assert.Contains(updates, u =>
                u.Status == OrderStatus.Cancelled
                && (u.Reason ?? "").Contains("position it was protecting is closed"));
        }

        // ── What the PLUGIN receives — the half paper trading cannot show ────

        /// <summary>
        /// <b>Kraken and Coinbase read <c>StopLoss</c> and nothing else</b> for a stop order
        /// (<c>KrakenProvider.cs:832</c>, <c>CoinbaseProvider.cs:619</c>); Gemini and Binance
        /// futures prefer <c>TriggerPrice</c> and fall back. So the field a caller happens to fill
        /// in decides whether the venue receives a stop price at all — and the paper broker's own
        /// fallback means NONE of that is visible in paper trading.
        ///
        /// <para>
        /// These two tests drive <see cref="GeneralOrderService"/> over a substitute provider and
        /// assert what actually arrives at the plugin boundary, because that is the artifact. The
        /// first version of this fix set <c>TriggerPrice</c> alone and would have silently broken
        /// stops on two live venues.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData(true)]   // caller filled in TriggerPrice (the dashboard editor)
        [InlineData(false)]  // caller filled in StopLoss (older callers, Kraken/Coinbase's field)
        public async Task A_stop_order_reaches_the_plugin_with_BOTH_spellings_of_its_trigger(bool viaTriggerPrice)
        {
            var (svc, provider, captured) = OrderServiceOverSubstitute();

            await svc.PlaceOrderAsync("Binance", new TradeSignal(
                Symbol: Btc, Side: OrderSide.Sell, Quantity: 1.0, Type: OrderType.StopMarket,
                TriggerPrice: viaTriggerPrice ? 79_000 : null,
                StopLoss: viaTriggerPrice ? null : 79_000,
                ReduceOnly: true));

            var sent = Assert.Single(captured);
            Assert.Equal(79_000, sent.TriggerPrice);
            Assert.Equal(79_000, sent.StopLoss);
            _ = provider;
        }

        [Fact]
        public async Task A_take_profit_order_reaches_the_plugin_with_both_spellings_too()
        {
            var (svc, _, captured) = OrderServiceOverSubstitute();

            await svc.PlaceOrderAsync("Binance", new TradeSignal(
                Symbol: Btc, Side: OrderSide.Sell, Quantity: 1.0, Type: OrderType.TakeProfitMarket,
                TriggerPrice: 88_000, ReduceOnly: true));

            var sent = Assert.Single(captured);
            Assert.Equal(88_000, sent.TriggerPrice);
            Assert.Equal(88_000, sent.TakeProfit);
        }

        [Fact]
        public async Task A_market_ENTRY_carrying_a_bracket_is_not_turned_into_a_stop_order()
        {
            // The guard on the normalisation. An entry's StopLoss is a protective leg to attach
            // AFTER the fill; copying it into TriggerPrice would make a market buy look like a
            // stop order to every plugin that reads that field.
            var (svc, _, captured) = OrderServiceOverSubstitute();

            await svc.PlaceOrderAsync("Binance", new TradeSignal(
                Symbol: Btc, Side: OrderSide.Buy, Quantity: 1.0, Type: OrderType.Market,
                StopLoss: 79_000, TakeProfit: 88_000));

            var sent = Assert.Single(captured);
            Assert.Null(sent.TriggerPrice);
            Assert.Equal(79_000, sent.StopLoss);
            Assert.Equal(88_000, sent.TakeProfit);
        }

        [Fact]
        public async Task Two_DISAGREEING_trigger_fields_are_passed_through_rather_than_reconciled()
        {
            // Picking a winner would hide a caller bug behind an order that looks fine. The
            // signal goes out as written.
            var (svc, _, captured) = OrderServiceOverSubstitute();

            await svc.PlaceOrderAsync("Binance", new TradeSignal(
                Symbol: Btc, Side: OrderSide.Sell, Quantity: 1.0, Type: OrderType.StopMarket,
                TriggerPrice: 79_000, StopLoss: 70_000, ReduceOnly: true));

            var sent = Assert.Single(captured);
            Assert.Equal(79_000, sent.TriggerPrice);
            Assert.Equal(70_000, sent.StopLoss);
        }

        private static (GeneralOrderService Svc, ITradingProvider Provider, List<TradeSignal> Captured)
            OrderServiceOverSubstitute()
        {
            var captured = new List<TradeSignal>();

            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp = (ITradingProvider)tpSub;
            tp.IsConnected.Returns(true);
            tp.SupportsOrderEventStreaming.Returns(true);
            tp.OrderUpdateStream.Returns(System.Reactive.Linq.Observable.Empty<OrderUpdate>());
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(ci =>
            {
                captured.Add(ci.Arg<TradeSignal>());
                return Task.FromResult("EX-1");
            });

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new System.Reactive.Subjects.Subject<OrderUpdate>());

            var svc = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, new EventBus(), paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false),
                new Core.Services.Trading.QuickTradeEquity());

            return (svc, tp, captured);
        }

        // ── The refusal, when the market has already passed the level ────────

        [Fact]
        public async Task A_protective_stop_on_the_wrong_side_is_refused_in_the_language_of_PROTECTION()
        {
            // Same RULE as a stop entry — a sell stop above the market fires on the next tick
            // either way — but not the same EXPLANATION. Telling someone adjusting the stop on an
            // open position to "use a market order to sell here" is advice about an order they are
            // not placing, and it reads as the terminal having misread which side they are on.
            var paper = Make(out var store);
            store.EmitState(At(63_000));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, OrderType.Market));

            // Long from 63,000; a stop at 79,790 is ABOVE the market and would fire at once.
            string result = await paper.PlaceOrderAsync(HandSetStopOnLong(79_790, 1.0));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Contains("wrong side of the market price", result);
            Assert.Contains("not your entry", result);
            Assert.DoesNotContain("use a market order", result);
        }

        [Fact]
        public async Task A_stop_ENTRY_on_the_wrong_side_still_gets_the_entry_wording()
        {
            // The other half of the pair: the entry advice must survive for actual entries, or
            // this change would have traded one wrong message for another.
            var paper = Make(out var store);
            store.EmitState(At(63_000));

            string result = await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: Btc, Side: OrderSide.Sell, Quantity: 1.0,
                Type: OrderType.StopMarket, TriggerPrice: 79_790));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Contains("A sell stop must be below the current price", result);
        }
    }
}
