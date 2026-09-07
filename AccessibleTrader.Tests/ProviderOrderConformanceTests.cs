using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using AccessibleTrader.Tests.OrderConformance;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The provider conformance suite</b> — `docs/PROVIDER_CONFORMANCE_SCOPE.md` §4, built.
    ///
    /// <para>
    /// Nine defects across eight venues were the same bug: the wrong value in the wrong field of an
    /// outgoing request, failing silently. Every one of them except the order stream is a
    /// payload-construction bug, and a payload can be asserted without a venue: a fake transport
    /// captures the request and a per-venue rig decodes it into a <see cref="WireOrder"/>. The
    /// theories below then state each property ONCE and run it across every trading plugin.
    /// </para>
    ///
    /// <para>
    /// Rules this file keeps: a capability a venue lacks is asserted as a REFUSAL before any
    /// request, never skipped — a skipped row is a row that cannot go red; every row set is
    /// enumerated from the rig list, and an anti-vacuity fact proves the rig list covers every
    /// trading provider the roster can find; and nothing here bypasses the plugin — the signal goes
    /// through the real <c>PlaceOrderAsync</c>, so the assertion is about what the venue would have
    /// received.
    /// </para>
    ///
    /// <para>
    /// Signals are handed to the plugin DIRECTLY, without <c>GeneralOrderService.NormaliseTrigger</c>
    /// in front — on purpose. The SDK contract says a plugin author should read
    /// <c>TriggerPrice ?? StopLoss</c>; the normaliser is the app's belt, this is the braces, and a
    /// strategy plugin or script calling the provider gets only the braces.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderOrderConformanceTests
    {
        private const string OrderId = "777";
        private const double LimitPx = 101.5, StopTrigger = 90.25, StopLimitPx = 90.0, TpTrigger = 120.5;

        public static IEnumerable<object[]> AllRigs() => OrderRigs.All.Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithStops() => OrderRigs.All.Where(r => r.Stops != StopSupport.None).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithoutStops() => OrderRigs.All.Where(r => r.Stops == StopSupport.None).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithTakeProfits() => OrderRigs.All.Where(r => r.TakeProfits == TakeProfitSupport.Native).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsRefusingTakeProfits() => OrderRigs.All.Where(r => r.TakeProfits == TakeProfitSupport.Refused).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithReduceOnly() => OrderRigs.All.Where(r => r.HasReduceOnlyFlag).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithPracticeHost() => OrderRigs.All.Where(r => r.PracticeHost != null).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWithoutPracticeHost() => OrderRigs.All.Where(r => r.PracticeHost == null).Select(r => new object[] { r.Name });
        public static IEnumerable<object[]> RigsWholeSharesOnly() => OrderRigs.All.Where(r => r.WholeSharesOnly).Select(r => new object[] { r.Name });

        private static double Qty(OrderRig r) => r.WholeSharesOnly ? 6 : 0.6;

        private static TradeSignal Sig(OrderRig r, OrderSide side, OrderType type, double? qty = null,
            double? price = null, double? trigger = null, double? stopLoss = null, double? takeProfit = null,
            bool reduceOnly = false, string? symbol = null) =>
            new(symbol ?? r.OrderSymbol, side, qty ?? Qty(r), type, Price: price, StopLoss: stopLoss,
                TakeProfit: takeProfit, SubType: r.SubType, TriggerPrice: trigger, ReduceOnly: reduceOnly);

        private static (ITradingProvider P, FakeHttpMessageHandler H) Live(OrderRig r, bool armSuccess = true)
        {
            var h = new FakeHttpMessageHandler();
            var p = r.Build(h, ProviderConfigKeys.Live);
            if (armSuccess) r.ArmSuccess(h, OrderId);
            return (p, h);
        }

        private static WireOrder Single(OrderRig r, FakeHttpMessageHandler h, string result)
        {
            var orders = r.Orders(h);
            Assert.True(orders.Count == 1,
                $"{r.Name}: expected exactly one order on the wire, found {orders.Count} (result '{result}'): "
              + string.Join(" | ", orders));
            return orders[0];
        }

        private static void Refused(OrderRig r, FakeHttpMessageHandler h, string result, string what)
        {
            Assert.True(result.StartsWith("ORDER_FAILED:", StringComparison.Ordinal),
                $"{r.Name}: {what} must be refused with an ORDER_FAILED sentinel, got '{result}'");
            Assert.True(r.Orders(h).Count == 0,
                $"{r.Name}: {what} was refused AND an order still left the process: " + string.Join(" | ", r.Orders(h)));
        }

        // ── 1. The plain limit order: symbol, side, quantity, price ─────────────

        [Theory]
        [MemberData(nameof(AllRigs))]
        public async Task Limit_order_carries_symbol_side_quantity_and_price(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.Equal(OrderId, result);
            var o = Single(r, h, result);
            Assert.Equal(r.ExpectedWireSymbol, o.Symbol);
            Assert.Equal("buy", o.Side);
            Assert.Equal(Qty(r), o.Quantity, 10);          // Tradier's (int) truncation: 9.7 → 9
            Assert.False(o.IsMarket, $"{r.Name}: a limit order left as a market order ({o.Type})");
            Assert.Equal(LimitPx, o.LimitPrice);
        }

        /// <summary>A venue that trades whole shares only must REFUSE a fraction, never round it.</summary>
        [Theory]
        [MemberData(nameof(RigsWholeSharesOnly))]
        public async Task Fractional_quantity_is_refused_on_a_whole_share_venue(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, qty: 9.7, price: LimitPx));

            Refused(r, h, result, "a fractional quantity");
        }

        // ── 2. The id comes back; a rejection does not look like one ────────────

        [Theory]
        [MemberData(nameof(AllRigs))]
        public async Task Venue_rejection_is_a_failure_not_an_order_id(string rig)
        {
            var r = OrderRigs.Named(rig);
            var h = new FakeHttpMessageHandler();
            var p = r.Build(h, ProviderConfigKeys.Live);
            r.ArmRejection(h);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.True(result.StartsWith("ORDER_FAILED:", StringComparison.Ordinal),
                $"{r.Name}: the venue rejected the order and the plugin reported '{result}'");
        }

        // ── 3. Stops: either spelling, always a stop, never a market ────────────

        [Theory]
        [MemberData(nameof(RigsWithStops))]
        public async Task Stop_with_only_TriggerPrice_is_a_stop_at_that_trigger(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(StopSignal(r, trigger: StopTrigger));

            Assert.Equal(OrderId, result);
            AssertStop(r, Single(r, h, result));
        }

        [Theory]
        [MemberData(nameof(RigsWithStops))]
        public async Task Stop_with_only_StopLoss_is_the_same_order(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p1, h1) = Live(r);
            string r1 = await p1.PlaceOrderAsync(StopSignal(r, trigger: StopTrigger));
            var viaTrigger = Single(r, h1, r1);

            var (p2, h2) = Live(r);
            string r2 = await p2.PlaceOrderAsync(StopSignal(r, stopLoss: StopTrigger));

            Assert.Equal(OrderId, r2);
            var viaStopLoss = Single(r, h2, r2);
            AssertStop(r, viaStopLoss);
            Assert.Equal(viaTrigger, viaStopLoss);
        }

        /// <summary>
        /// The app's normaliser sends BOTH spellings. A plugin that reads one as the trigger and the
        /// other as a protective leg places two orders at the same price.
        /// </summary>
        [Theory]
        [MemberData(nameof(RigsWithStops))]
        public async Task Stop_with_both_spellings_leaves_exactly_one_order(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(StopSignal(r, trigger: StopTrigger, stopLoss: StopTrigger));

            Assert.Equal(OrderId, result);
            AssertStop(r, Single(r, h, result));
        }

        [Theory]
        [MemberData(nameof(RigsWithoutStops))]
        public async Task Stop_types_are_refused_before_any_request_where_the_venue_has_none(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Sell, OrderType.StopMarket, trigger: StopTrigger, stopLoss: StopTrigger));

            Refused(r, h, result, "a stop order");
        }

        private static TradeSignal StopSignal(OrderRig r, double? trigger = null, double? stopLoss = null) =>
            r.Stops == StopSupport.StopLimitOnly
                ? Sig(r, OrderSide.Sell, OrderType.StopLimit, price: StopLimitPx, trigger: trigger, stopLoss: stopLoss)
                : Sig(r, OrderSide.Sell, OrderType.StopMarket, trigger: trigger, stopLoss: stopLoss);

        private static void AssertStop(OrderRig r, WireOrder o)
        {
            Assert.False(o.IsMarket, $"{r.Name}: a STOP left as a MARKET order ({o.Type}) — it would fill now");
            Assert.True(o.IsStop, $"{r.Name}: a stop left as '{o.Type}', which the venue does not read as a stop");
            Assert.True(o.Trigger.HasValue, $"{r.Name}: the stop carries no trigger price");
            Assert.Equal(StopTrigger, o.Trigger!.Value, 10);
            Assert.Equal("sell", o.Side);
            if (r.Stops == StopSupport.StopLimitOnly) Assert.Equal(StopLimitPx, o.LimitPrice);
        }

        // ── 4. Take-profits: never a market order ───────────────────────────────

        [Theory]
        [MemberData(nameof(RigsWithTakeProfits))]
        public async Task Take_profit_with_only_TriggerPrice_rests_at_the_trigger(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Sell, OrderType.TakeProfitMarket, trigger: TpTrigger));

            Assert.Equal(OrderId, result);
            AssertTakeProfit(r, Single(r, h, result));
        }

        [Theory]
        [MemberData(nameof(RigsWithTakeProfits))]
        public async Task Take_profit_with_both_spellings_leaves_exactly_one_order(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Sell, OrderType.TakeProfitMarket, trigger: TpTrigger, takeProfit: TpTrigger));

            Assert.Equal(OrderId, result);
            AssertTakeProfit(r, Single(r, h, result));
        }

        [Theory]
        [MemberData(nameof(RigsRefusingTakeProfits))]
        public async Task Take_profit_types_are_refused_before_any_request_where_the_venue_has_none(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Sell, OrderType.TakeProfitMarket, trigger: TpTrigger, takeProfit: TpTrigger));

            Refused(r, h, result, "a take-profit order");
        }

        private static void AssertTakeProfit(OrderRig r, WireOrder o)
        {
            Assert.False(o.IsMarket, $"{r.Name}: a TAKE-PROFIT left as a MARKET order ({o.Type}) — it would fill now");
            Assert.False(o.IsStop, $"{r.Name}: a take-profit left as a STOP ({o.Type}) — wrong side of the market");
            double? level = o.Trigger ?? o.LimitPrice;
            Assert.True(level.HasValue, $"{r.Name}: the take-profit carries neither a trigger nor a limit price");
            Assert.Equal(TpTrigger, level!.Value, 10);
            Assert.Equal("sell", o.Side);
        }

        // ── 5. Reduce-only ──────────────────────────────────────────────────────

        [Theory]
        [MemberData(nameof(RigsWithReduceOnly))]
        public async Task Reduce_only_sell_sets_the_venues_reduce_only_flag(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Sell, OrderType.Market, reduceOnly: true));

            Assert.Equal(OrderId, result);
            var o = Single(r, h, result);
            Assert.True(o.ReduceOnly, $"{r.Name}: a reduce-only sell left WITHOUT the venue's reduce-only marker — it can open a short");
            Assert.Equal("sell", o.Side);
        }

        // ── 6. The symbol is the ORDER's, not the chart's ───────────────────────

        [Theory]
        [MemberData(nameof(AllRigs))]
        public async Task Order_symbol_is_the_orders_not_the_charts(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);
            r.SeedChartedSymbol(p, r.ChartedSymbol);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.Equal(OrderId, result);
            Assert.Equal(r.ExpectedWireSymbol, Single(r, h, result).Symbol);
        }

        // ── 7. Environment: which host the credential signs against ─────────────

        [Theory]
        [MemberData(nameof(RigsWithPracticeHost))]
        public async Task Environment_Paper_signs_against_the_practice_host(string rig)
        {
            var r = OrderRigs.Named(rig);
            var h = new FakeHttpMessageHandler();
            var p = r.Build(h, ProviderConfigKeys.Paper);
            r.ArmSuccess(h, OrderId);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.Equal(OrderId, result);
            Assert.Equal(r.PracticeHost, Single(r, h, result).Host);
        }

        [Theory]
        [MemberData(nameof(AllRigs))]
        public async Task Environment_Live_signs_against_the_live_host(string rig)
        {
            var r = OrderRigs.Named(rig);
            var (p, h) = Live(r);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.Equal(OrderId, result);
            Assert.Equal(r.LiveHost, Single(r, h, result).Host);
        }

        /// <summary>
        /// The contract's polarity: anything not explicitly Live is practice. A legacy profile with
        /// no environment, or a third-party host that never sends one, must not reach real money.
        /// </summary>
        [Theory]
        [MemberData(nameof(RigsWithPracticeHost))]
        public async Task Configure_without_an_Environment_is_practice_not_live(string rig)
        {
            var r = OrderRigs.Named(rig);
            var h = new FakeHttpMessageHandler();
            var p = r.Build(h, environment: null);
            r.ArmSuccess(h, OrderId);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.Equal(OrderId, result);
            Assert.Equal(r.PracticeHost, Single(r, h, result).Host);
        }

        /// <summary>
        /// Where the venue has no practice environment, a Paper credential either refuses outright
        /// (Kraken Futures, whose demo is gone) or — today — signs against the live host. The second
        /// group is pinned BY NAME so that it cannot grow unnoticed; shrinking it is the decision
        /// recorded in docs/PROVIDER_PLACEMENT_AUDIT_2026-09-07.md.
        /// </summary>
        [Theory]
        [MemberData(nameof(RigsWithoutPracticeHost))]
        public async Task Paper_credential_on_a_venue_with_no_practice_environment(string rig)
        {
            var r = OrderRigs.Named(rig);
            var h = new FakeHttpMessageHandler();
            var p = r.Build(h, ProviderConfigKeys.Paper);
            r.ArmSuccess(h, OrderId);

            string result = await p.PlaceOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            if (r.PaperRefusesOutright)
            {
                Refused(r, h, result, "a Paper credential with no practice venue");
                Assert.Empty(h.Captured);
                return;
            }

            Assert.Equal(OrderId, result);
            Assert.Equal(r.LiveHost, Single(r, h, result).Host);
        }

        [Fact]
        public void Venues_that_route_a_Paper_credential_to_the_live_host_are_exactly_these()
        {
            var routeLive = OrderRigs.All.Where(r => r.PracticeHost == null && !r.PaperRefusesOutright)
                .Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(new[] { "Bitstamp", "Coinbase", "Interactive Brokers", "Kraken", "MEXC futures", "MEXC spot", "Schwab" }, routeLive);
        }

        // ── 8. Dry runs: the venue validates the SAME payload, and places nothing ──

        public static IEnumerable<object[]> RigsWithDryRun() => OrderRigs.All.Where(r => r.HasDryRun).Select(r => new object[] { r.Name });

        /// <summary>
        /// A dry run that builds its own payload validates nothing about the order that would be
        /// sent. The decoded order must be identical, and the ONLY difference on the wire the
        /// venue's validate flag (or endpoint).
        /// </summary>
        [Theory]
        [MemberData(nameof(RigsWithDryRun))]
        public async Task Dry_run_sends_the_placement_payload_plus_only_the_validate_flag(string rig)
        {
            var r = OrderRigs.Named(rig);
            var signal = Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx);

            var (placed, hPlace) = Live(r);
            string placedId = await placed.PlaceOrderAsync(signal);
            Assert.Equal(OrderId, placedId);
            var placedOrder = Single(r, hPlace, placedId);

            var hDry = new FakeHttpMessageHandler();
            var dry = (IOrderDryRunProvider)r.Build(hDry, ProviderConfigKeys.Live);
            r.ArmDryRunSuccess(hDry);
            var verdict = await dry.DryRunOrderAsync(signal);

            Assert.True(verdict.Accepted, $"{r.Name}: the venue validated the order and the plugin said '{verdict.Message}'");
            Assert.DoesNotContain(OrderId, verdict.Message);   // a dry run never yields an order id
            var dryOrder = Single(r, hDry, verdict.Message);
            Assert.Equal(placedOrder, dryOrder);

            int dryFlags = hDry.Captured.Zip(hDry.CapturedBodies).Count(x => r.IsDryRunRequest(x.First, x.Second));
            int placeFlags = hPlace.Captured.Zip(hPlace.CapturedBodies).Count(x => r.IsDryRunRequest(x.First, x.Second));
            Assert.Equal(1, dryFlags);
            Assert.Equal(0, placeFlags);
        }

        [Theory]
        [MemberData(nameof(RigsWithDryRun))]
        public async Task Dry_run_rejection_is_not_accepted_and_not_an_exception(string rig)
        {
            var r = OrderRigs.Named(rig);
            var h = new FakeHttpMessageHandler();
            var dry = (IOrderDryRunProvider)r.Build(h, ProviderConfigKeys.Live);
            r.ArmDryRunRejection(h);

            var verdict = await dry.DryRunOrderAsync(Sig(r, OrderSide.Buy, OrderType.Limit, price: LimitPx));

            Assert.False(verdict.Accepted, $"{r.Name}: the venue rejected the dry run and the plugin reported it accepted");
            Assert.False(string.IsNullOrWhiteSpace(verdict.Message), $"{r.Name}: a rejection with no reason");
        }

        [Fact]
        public void Rigs_with_a_dry_run_are_exactly_the_providers_implementing_the_capability()
        {
            var implementing = ProviderRoster.Trading().Where(p => p is IOrderDryRunProvider)
                .Select(p => p.GetType().Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
            var rigged = OrderRigs.All.Where(r => r.HasDryRun)
                .Select(r => r.ProviderTypeName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(implementing, rigged);
            Assert.Equal(new[] { "BinanceProvider", "KrakenProvider", "TradierProvider" }, implementing);
        }

        // ── 9. Anti-vacuity ─────────────────────────────────────────────────────

        [Fact]
        public void Every_trading_provider_in_the_roster_has_a_rig()
        {
            var roster = ProviderRoster.Trading().Select(p => p.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var rigged = OrderRigs.All.Select(r => r.ProviderTypeName).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();

            Assert.Equal(roster, rigged);
            Assert.True(OrderRigs.All.Count >= 14);
            Assert.NotEmpty(RigsWithStops()); Assert.NotEmpty(RigsWithoutStops());
            Assert.NotEmpty(RigsWithTakeProfits()); Assert.NotEmpty(RigsRefusingTakeProfits());
            Assert.NotEmpty(RigsWithReduceOnly()); Assert.NotEmpty(RigsWithPracticeHost());
            Assert.NotEmpty(RigsWithoutPracticeHost()); Assert.NotEmpty(RigsWholeSharesOnly());
        }
    }
}
