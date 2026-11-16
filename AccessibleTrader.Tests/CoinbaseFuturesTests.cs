using System.Net;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Plugins.Coinbase;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coinbase US futures (2026-11-14): the same key and order endpoint as spot, sized in
    /// contracts. The product fixture is the public BIP-20DEC30-CDE ("BTC Perp") answer read live
    /// on 2026-11-14, trimmed; the position and balance shapes are from Coinbase's API reference
    /// of the same day. None of this has been seen against a real futures account.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class CoinbaseFuturesTests
    {
        internal const string BtcPerp = """
        {"product_id":"BIP-20DEC30-CDE","price":"66815","base_increment":"1","quote_increment":"0.01",
         "quote_min_size":"0","quote_max_size":"100000000","base_min_size":"1","base_max_size":"5000",
         "base_name":"","quote_name":"US Dollar","is_disabled":false,"status":"","cancel_only":false,
         "limit_only":false,"post_only":false,"trading_disabled":false,"auction_mode":false,"product_type":"FUTURE",
         "quote_currency_id":"USD","base_currency_id":"",
         "fcm_trading_session_details":{"is_session_open":true,"open_time":"2026-11-13T23:00:00Z","close_time":"2026-11-16T22:00:00Z",
           "session_state":"FCM_TRADING_SESSION_STATE_OPEN","after_hours_order_entry_disabled":false},
         "base_display_symbol":"","quote_display_symbol":"USD","view_only":false,"price_increment":"5",
         "display_name":"BTC Perp","product_venue":"FCM",
         "future_product_details":{"venue":"cde","contract_code":"BIP","contract_expiry":"2089-12-30T16:00:00Z",
           "contract_size":"0.01","contract_root_unit":"BTC","contract_expiry_type":"EXPIRING",
           "contract_display_name":"BTC Perp","display_name":"Bitcoin Perpetual"}}
        """;

        private const string Sym          = "BIP-20DEC30-CDE";
        private const string ProductRoute = @"api\.coinbase\.com/api/v3/brokerage/market/products/BIP-20DEC30-CDE$";
        private const string OrderRoute   = @"api\.coinbase\.com/api/v3/brokerage/orders$";
        private const string PosRoute     = @"api\.coinbase\.com/api/v3/brokerage/cfm/positions$";
        private const string Accepted     = """{"success":true,"success_response":{"order_id":"cb-f1"}}""";

        private static string Positions(string side, string contracts) => $$"""
        {"positions":[{"product_id":"BIP-20DEC30-CDE","expiration_time":"2030-12-20T16:00:00Z","side":"{{side}}",
          "number_of_contracts":"{{contracts}}","current_price":"67000","avg_entry_price":"66000",
          "unrealized_pnl":"{{(side == "LONG" ? "30" : "-30")}}","daily_realized_pnl":"0"}]}
        """;

        private static string Product(Action<JObject> change)
        {
            var o = JObject.Parse(BtcPerp);
            change(o);
            return o.ToString();
        }

        private static (CoinbaseProvider P, FakeHttpMessageHandler H) Rig(string product = BtcPerp, string? positions = null)
        {
            var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(ProductRoute, product);
            if (positions != null) h.Get(PosRoute, positions);
            h.Post(OrderRoute, Accepted);
            SymbolListHarness.SwapEveryHttpClient(p, h);
            return (p, h);
        }

        private static JObject? Sent(FakeHttpMessageHandler h)
        {
            int i = h.Captured.FindIndex(r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/api/v3/brokerage/orders");
            return i < 0 ? null : JObject.Parse(h.CapturedBodies[i]);
        }

        // ── The contract ────────────────────────────────────────────────────

        [Fact]
        public void The_BTC_Perp_reads_as_whole_contracts_of_a_hundredth_of_a_bitcoin()
        {
            var r = CoinbaseProductRules.Parse(JObject.Parse(BtcPerp))!;

            Assert.Equal("BTC Perp", r.Rules.Symbol);           // not "BIP/20DEC30", the spot reading
            Assert.Equal("contracts", r.Rules.QuantityUnit);
            Assert.Equal(1m, r.Rules.QuantityStep);
            Assert.Equal(1m, r.Rules.MinQuantity);
            Assert.Equal(5m, r.Rules.PriceTick);
            Assert.Null(r.Rules.NotTrading);
            Assert.Equal(0.01m, r.Contract!.Size);
            Assert.Equal("BTC", r.Contract.Unit);
            Assert.Equal(5000m, r.Contract.MaxContracts);
        }

        [Theory]
        [InlineData(true,  true,  "session is closed")]
        [InlineData(true,  false, null)]                    // closed, but after-hours entry is open
        [InlineData(false, false, null)]
        public void A_closed_session_refuses_only_when_Coinbase_says_it_takes_no_orders(bool closed, bool entryOff, string? words)
        {
            var r = CoinbaseProductRules.Parse(JObject.Parse(Product(o =>
            {
                o["fcm_trading_session_details"]!["is_session_open"] = !closed;
                o["fcm_trading_session_details"]!["after_hours_order_entry_disabled"] = entryOff;
            })))!;

            if (words == null) Assert.Null(r.Rules.NotTrading);
            else Assert.Contains(words, r.Rules.NotTrading);
        }

        [Fact]
        public void An_expired_contract_takes_no_orders()
        {
            var r = CoinbaseProductRules.Parse(JObject.Parse(Product(o => o["view_only"] = true)))!;
            Assert.Equal("Coinbase lists BTC Perp as expired; it takes no new orders", r.Rules.NotTrading);
        }

        // ── Orders ──────────────────────────────────────────────────────────

        [Fact]
        public async Task A_futures_order_goes_out_in_whole_contracts_on_the_contract_tick()
        {
            var (p, h) = Rig();

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Sell, 3, OrderType.Limit, Price: 66_817.4, SubType: "Futures"));

            Assert.Equal("cb-f1", r);
            var body = Sent(h)!;
            Assert.Equal(Sym, body["product_id"]!.ToString());
            Assert.Equal("SELL", body["side"]!.ToString());
            Assert.Equal("3", body["order_configuration"]!["limit_limit_gtc"]!["base_size"]!.ToString());
            Assert.Equal("66815", body["order_configuration"]!["limit_limit_gtc"]!["limit_price"]!.ToString());
            Assert.Null(body["leverage"]);
        }

        // 0.03 is what someone thinking in bitcoin types for three contracts.
        [Fact]
        public async Task A_size_in_bitcoin_is_refused_with_what_a_contract_is_and_not_sent()
        {
            var (p, h) = Rig();

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Buy, 0.03, SubType: "Futures"));

            Assert.Equal("ORDER_FAILED:Coinbase's smallest BTC Perp order is 1 contract; this one is 0.03 contracts (one contract is 0.01 BTC)", r);
            Assert.Null(Sent(h));
        }

        [Fact]
        public async Task More_contracts_than_one_order_may_carry_are_refused()
        {
            var (p, h) = Rig();

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Buy, 5001, SubType: "Futures"));

            Assert.StartsWith("ORDER_FAILED:Coinbase's largest BTC Perp order is 5000 contracts", r);
            Assert.Null(Sent(h));
            Assert.Equal("cb-f1", await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Buy, 5000, SubType: "Futures")));
        }

        [Fact]
        public async Task Leverage_is_refused_in_words_because_Coinbase_takes_no_setting()
        {
            var (p, h) = Rig();

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Buy, 2, Leverage: 5, SubType: "Futures"));

            Assert.StartsWith("ORDER_FAILED:Coinbase takes no leverage setting", r);
            Assert.Null(Sent(h));
            Assert.Equal("cb-f1", await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Buy, 2, Leverage: 1, SubType: "Futures")));
        }

        // ── Closing: no reduce-only flag at Coinbase, so the position is checked first ──

        [Theory]
        [InlineData("LONG",  "2", OrderSide.Sell, 5,  "would open the rest the other way")]
        [InlineData("LONG",  "2", OrderSide.Buy,  1,  "no BTC Perp position for this to close")]
        [InlineData("SHORT", "2", OrderSide.Sell, 1,  "no BTC Perp position for this to close")]
        public async Task A_close_larger_than_or_against_the_position_is_refused_unsent(string side, string held, OrderSide closeSide, double qty, string words)
        {
            var (p, h) = Rig(positions: Positions(side, held));

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, closeSide, qty, SubType: "Futures", ReduceOnly: true));

            Assert.StartsWith("ORDER_FAILED:", r);
            Assert.Contains(words, r);
            Assert.Null(Sent(h));
        }

        [Theory]
        [InlineData("LONG",  OrderSide.Sell, 2)]
        [InlineData("LONG",  OrderSide.Sell, 1)]
        [InlineData("SHORT", OrderSide.Buy,  2)]
        public async Task A_close_within_the_position_is_sent(string side, OrderSide closeSide, double qty)
        {
            var (p, h) = Rig(positions: Positions(side, "2"));

            Assert.Equal("cb-f1", await p.PlaceOrderAsync(new TradeSignal(Sym, closeSide, qty, SubType: "Futures", ReduceOnly: true)));
            Assert.NotNull(Sent(h));
        }

        [Fact]
        public async Task A_close_is_refused_when_the_position_cannot_be_read()
        {
            // A first read that worked, so the later failure is a failure, not "no futures account".
            var (p, _) = Rig(positions: Positions("LONG", "2"));
            await p.GetPositionsAsync();
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(ProductRoute, BtcPerp);
            h.Get(PosRoute, """{"error":"internal"}""", HttpStatusCode.InternalServerError);
            h.Post(OrderRoute, Accepted);
            SymbolListHarness.SwapEveryHttpClient(p, h);

            string r = await p.PlaceOrderAsync(new TradeSignal(Sym, OrderSide.Sell, 1, SubType: "Futures", ReduceOnly: true));

            Assert.Contains("could not be read", r);
            Assert.Null(Sent(h));
        }

        // ── Positions and balance ───────────────────────────────────────────

        [Fact]
        public async Task A_short_reads_as_negative_contracts_worth_their_bitcoin()
        {
            var (p, _) = Rig(positions: Positions("SHORT", "3"));

            var pos = Assert.Single(await p.GetPositionsAsync());

            Assert.Equal(Sym, pos.Symbol);
            Assert.Equal(-3, pos.Quantity);
            Assert.Equal(66_000, pos.AveragePrice);
            Assert.Equal(0.01, pos.ContractSize);
            Assert.Equal(3 * 0.01 * 67_000, pos.MarketValue, 6);
            Assert.Equal(-30, pos.UnrealizedPnL);
            Assert.Equal(0, pos.LiquidationPrice);              // Coinbase gives none per position
            Assert.Equal(MarginMode.None, pos.MarginMode);
        }

        [Fact]
        public void The_percent_move_counts_the_contract_size()
        {
            // 3 contracts of 0.01 BTC from 66,000, up 198: 1,980 dollars of BTC up 10%.
            var p = new Position(Sym, 3, 66_000, 2_178, 198, ContractSize: 0.01);
            Assert.Equal(10.0, TradeStatusLine.ReturnPercent(p)!.Value, 6);
            Assert.Null(TradeStatusLine.ReturnPercent(p with { ContractSize = double.NaN }));
            Assert.Equal(0.1, TradeStatusLine.ReturnPercent(p with { ContractSize = 1 })!.Value, 6);
        }

        [Fact]
        public async Task An_account_without_futures_reads_as_none_once_said_then_a_refusal_after_a_good_read_fails()
        {
            var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var errors = new List<string>();
            using var sub = p.ErrorStream.Subscribe(errors.Add);
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(PosRoute, """{"error":"PERMISSION_DENIED"}""", HttpStatusCode.Forbidden);
            SymbolListHarness.SwapEveryHttpClient(p, h);

            Assert.Empty(await p.GetPositionsAsync());
            Assert.Empty(await p.GetPositionsAsync());
            Assert.Single(errors, e => e.Contains("did not show the futures account (HTTP 403)"));

            var good = new FakeHttpMessageHandler { StrictMode = true };
            good.Get(PosRoute, """{"positions":[]}""");
            SymbolListHarness.SwapEveryHttpClient(p, good);
            Assert.Empty(await p.GetPositionsAsync());

            SymbolListHarness.SwapEveryHttpClient(p, h);
            await Assert.ThrowsAsync<HttpRequestException>(() => p.GetPositionsAsync());
        }

        [Fact]
        public void The_futures_balance_is_the_futures_cash_less_what_open_orders_hold()
        {
            var b = CoinbaseProvider.ReadFuturesBalance(JObject.Parse("""
                {"futures_buying_power":{"value":"900","currency":"USD"},"cfm_usd_balance":{"value":"250.5","currency":"USD"},
                 "total_open_orders_hold_amount":{"value":"50","currency":"USD"},"unrealized_pnl":{"value":"0","currency":"USD"}}
                """))!;
            Assert.Equal(new Balance("USD futures", 200.5, 50), b);
            Assert.Null(CoinbaseProvider.ReadFuturesBalance(JObject.Parse("""{"cfm_usd_balance":{"value":"0","currency":"USD"}}""")));
        }

        [Fact]
        public async Task The_futures_list_asks_for_futures_and_spot_stays_spot()
        {
            var p = new CoinbaseProvider();
            p.Configure(SymbolListHarness.Credentials());
            var h = new FakeHttpMessageHandler { StrictMode = true };
            h.Get(@"/api/v3/brokerage/products\?product_type=FUTURE$", """{"products":[{"product_id":"BIP-20DEC30-CDE"}]}""");
            h.Get(@"/api/v3/brokerage/products$", """{"products":[{"product_id":"BTC-USD"}]}""");
            SymbolListHarness.SwapEveryHttpClient(p, h);

            Assert.Equal(new[] { "BIP-20DEC30-CDE" }, await p.GetAvailableSymbolsAsync(MarketType.Crypto, "Futures"));
            Assert.Equal(new[] { "BTC-USD" }, await p.GetAvailableSymbolsAsync(MarketType.Crypto, "Spot"));
            Assert.Equal(new[] { "Spot", "Futures" }, await p.GetSupportedSubTypesAsync(MarketType.Crypto));
        }
    }
}
