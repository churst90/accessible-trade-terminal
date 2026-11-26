using System.Net;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Plugins.Kraken;
using AccessibleTrader.Plugins.KrakenFutures;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What had to be true before real money went into Kraken (Cody, 2026-11-10: BTC, TAO and
    /// KAS on Kraken spot).
    ///
    /// <para>
    /// Every fixture here is shaped like Kraken's own answers, read live on 2026-11-10: the
    /// AssetPairs rows are the real KAS/USD, TAO/USD and XBT/USD rows, and the balance answer
    /// uses the codes Kraken really sends (<c>ZUSD</c>, <c>XXBT</c>, suffixed holdings). What
    /// these cannot show is Kraken accepting the orders; that needs a funded account and is
    /// listed as unverified in docs/TODO.md.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KrakenLiveReadinessTests
    {
        // The three pairs as Kraken's AssetPairs endpoint described them on 2026-11-10.
        internal const string AssetPairsJson = """
        {"error":[],"result":{
          "KASUSD":{"altname":"KASUSD","wsname":"KAS/USD","base":"KAS","quote":"ZUSD","pair_decimals":5,"lot_decimals":5,
                    "leverage_buy":[2,3],"leverage_sell":[2,3],"ordermin":"170","costmin":"0.5","tick_size":"0.00001","status":"online"},
          "TAOUSD":{"altname":"TAOUSD","wsname":"TAO/USD","base":"TAO","quote":"ZUSD","pair_decimals":4,"lot_decimals":5,
                    "leverage_buy":[2,3,4,5],"leverage_sell":[2,3,4,5],"ordermin":"0.022","costmin":"0.5","tick_size":"0.0001","status":"online"},
          "XXBTZUSD":{"altname":"XBTUSD","wsname":"XBT/USD","base":"XXBT","quote":"ZUSD","pair_decimals":1,"lot_decimals":8,
                    "leverage_buy":[2,3,4,5,6,7,8,9,10],"leverage_sell":[2,3,4,5,6,7,8,9,10],"ordermin":"0.00006","costmin":"0.5","tick_size":"0.1","status":"online"},
          "XXBTZUSD.d":{"altname":"XBTUSD.d","base":"XXBT","quote":"ZUSD"},
          "FOOUSD":{"altname":"FOOUSD","wsname":"FOO/USD","base":"FOO","quote":"ZUSD","pair_decimals":4,"lot_decimals":2,
                    "leverage_buy":[],"leverage_sell":[],"ordermin":"1","costmin":"0.5","tick_size":"0.0001","status":"cancel_only"}
        }}
        """;

        private static Dictionary<string, KrakenPairRules> Pairs() =>
            KrakenPairRules.Parse((JObject)JObject.Parse(AssetPairsJson)["result"]!);

        // ── Asset names ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("ZUSD", "USD")]
        [InlineData("XXBT", "BTC")]
        [InlineData("XBT.M", "BTC.M")]
        [InlineData("XXDG", "DOGE")]
        [InlineData("XETH", "ETH")]
        [InlineData("ZEUR", "EUR")]
        [InlineData("TAO", "TAO")]
        [InlineData("KAS", "KAS")]
        [InlineData("USD.HOLD", "USD.HOLD")]
        [InlineData("KFEE", "FEE")]
        // The reason it is a table and not "strip a leading X or Z": these are real assets
        // whose codes start with one.
        [InlineData("XTZ", "XTZ")]
        [InlineData("ZRX", "ZRX")]
        [InlineData("ZEC", "ZEC")]
        public void Kraken_codes_become_the_names_the_terminal_uses(string code, string name) =>
            Assert.Equal(name, KrakenBalances.AssetName(code));

        // ── Balances ─────────────────────────────────────────────────────────

        private const string BalanceExJson = """
        {"error":[],"result":{
          "ZUSD":{"balance":"1000.0000","hold_trade":"250.0000"},
          "XXBT":{"balance":"0.0100000000","hold_trade":"0.0000000000"},
          "XBT.F":{"balance":"0.0020000000","hold_trade":"0"},
          "DOT.S":{"balance":"12.5","hold_trade":"0"},
          "USD.HOLD":{"balance":"40.00","hold_trade":"0"},
          "KAS":{"balance":200.5,"hold_trade":0},
          "TAO":{"balance":"0.0000000000","hold_trade":"0.0000000000"}
        }}
        """;

        [Fact]
        public void BalanceEx_reads_dollars_as_USD_with_what_open_orders_hold_under_Locked()
        {
            var rows = KrakenBalances.FromBalanceEx((JObject)JObject.Parse(BalanceExJson)["result"]!)
                .ToDictionary(b => b.Asset);

            // The defect: this row was "ZUSD", which nothing downstream knows is money.
            Assert.Equal(750, rows["USD"].Free, 6);
            Assert.Equal(250, rows["USD"].Locked, 6);

            // Auto-earn (.F) is traded as the base asset, so it is part of BTC's free amount.
            Assert.Equal(0.012, rows["BTC"].Free, 9);
            Assert.False(rows.ContainsKey("BTC.F"));

            // Staked and held amounts are the account's but cannot be traded from here.
            Assert.Equal(0, rows["DOT.S"].Free);
            Assert.Equal(12.5, rows["DOT.S"].Locked, 9);
            Assert.Equal(40, rows["USD.HOLD"].Locked, 9);

            // Bare numbers, as Kraken's own BalanceEx example shows them, read the same.
            Assert.Equal(200.5, rows["KAS"].Free, 9);

            // An emptied asset is not a row.
            Assert.False(rows.ContainsKey("TAO"));
        }

        [Fact]
        public async Task The_balance_read_asks_BalanceEx_and_a_second_read_inside_the_time_to_live_asks_nothing()
        {
            using var p = new KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Post("/0/private/BalanceEx$", BalanceExJson);
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            var first = await p.GetBalancesAsync();
            var second = await p.GetBalancesAsync();

            Assert.Contains(first, b => b.Asset == "USD" && b.Free == 750);
            Assert.Equal(first.Count, second.Count);
            Assert.Single(handler.Captured);
            // Callers get their own list: one changing it cannot change what the next one sees.
            Assert.NotSame(first, second);
        }

        [Fact]
        public async Task Deposit_history_names_the_asset_as_the_balances_do()
        {
            // Kraken's DepositStatus answers in its own codes, as its docs' example shows
            // ("asset": "XXBT"); the balance read was fixed for this, the deposit list was not.
            using var p = new KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Post("/0/private/DepositStatus$", """
                {"error":[],"result":[
                  {"method":"Bitcoin","aclass":"currency","asset":"XXBT","refid":"FTQcuak-V6Za8qrWnhzTx67yYHz8Tg","txid":"6544b41b607d8b2512baf801755a3a87b6890eacdb451be8a94059fb11f0a8d9","info":"2Myd4eaAW96ojk38A2uDK4FbioCayvkEgVq","amount":"0.78125000","fee":"0.0000000000","time":1688992722,"status":"Success"},
                  {"method":"Bittensor","aclass":"currency","asset":"TAO","refid":"FTQcuak-V6Za8qrWnhzTx67yYHz8Th","txid":"0x1","info":"x","amount":"2.5","fee":"0","time":1688992800,"status":"Settled"}]}
                """);
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            var rows = await p.GetDepositsAsync();

            Assert.Equal(new[] { "BTC", "TAO" }, rows.Select(r => r.Asset));
        }

        // ── The read cache ───────────────────────────────────────────────────

        [Fact]
        public async Task The_read_cache_shares_an_answer_until_it_ages_out()
        {
            var now = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
            var cache = new AccountReadCache(() => now);
            int loads = 0;
            Task<List<int>> Load() { loads++; return Task.FromResult(new List<int> { loads }); }

            await cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            now = now.AddSeconds(14);
            await cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            Assert.Equal(1, loads);

            now = now.AddSeconds(2);
            await cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            Assert.Equal(2, loads);

            cache.Invalidate();
            await cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            Assert.Equal(3, loads);
        }

        [Fact]
        public async Task Two_callers_at_the_same_moment_share_one_request()
        {
            var cache = new AccountReadCache();
            var gate = new TaskCompletionSource<List<int>>();
            int loads = 0;
            Task<List<int>> Load() { loads++; return gate.Task; }

            var a = cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            var b = cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);
            gate.SetResult(new List<int> { 1 });

            Assert.Same(await a, await b);
            Assert.Equal(1, loads);
        }

        [Fact]
        public async Task A_failed_read_is_not_remembered()
        {
            // A cached failure would answer "the venue refused" for fifteen seconds after it
            // had recovered — and a cached EMPTY list would be the account-reads-flat incident.
            var cache = new AccountReadCache();
            int loads = 0;
            Task<List<int>> Load()
            {
                loads++;
                return loads == 1 ? Task.FromException<List<int>>(new HttpRequestException("502")) : Task.FromResult(new List<int> { 1 });
            }

            await Assert.ThrowsAsync<HttpRequestException>(() => cache.GetAsync("k", TimeSpan.FromSeconds(15), Load));
            var ok = await cache.GetAsync("k", TimeSpan.FromSeconds(15), Load);

            Assert.Equal(2, loads);
            Assert.Single(ok);
        }

        // ── Pair rules ───────────────────────────────────────────────────────

        [Fact]
        public void Pair_rules_are_found_by_every_spelling_of_the_pair()
        {
            var pairs = Pairs();
            foreach (string s in new[] { "BTC/USD", "XBT/USD", "XBTUSD", "XXBTZUSD", "BTCUSD" })
                Assert.Equal("BTC/USD", pairs[KrakenProvider.CanonicalKrakenPair(s)].Name);
            Assert.Equal("BTC", pairs[KrakenProvider.CanonicalKrakenPair("BTC/USD")].Base);
            Assert.Equal(170, pairs[KrakenProvider.CanonicalKrakenPair("KAS/USD")].OrderMin);
        }

        [Fact]
        public void A_KAS_order_under_170_is_refused_in_words_before_it_is_sent()
        {
            var kas = Pairs()[KrakenProvider.CanonicalKrakenPair("KAS/USD")];

            string? why = kas.Refusal(new TradeSignal("KAS/USD", OrderSide.Buy, 100), null);

            Assert.NotNull(why);
            Assert.Contains("170", why);
            Assert.Contains("KAS", why);
            Assert.Null(kas.Refusal(new TradeSignal("KAS/USD", OrderSide.Buy, 170), null));
        }

        [Fact]
        public void An_order_worth_less_than_the_cost_minimum_is_refused()
        {
            var btc = Pairs()[KrakenProvider.CanonicalKrakenPair("BTC/USD")];
            // 0.00006 BTC meets the unit minimum but at 5,000 is worth 0.30, under 0.50.
            string? why = btc.Refusal(new TradeSignal("BTC/USD", OrderSide.Buy, 0.00006, OrderType.Limit, Price: 5000), 5000);
            Assert.NotNull(why);
            Assert.Contains("0.5", why);
        }

        [Fact]
        public void A_pair_in_cancel_only_mode_takes_no_new_orders()
        {
            var foo = Pairs()[KrakenProvider.CanonicalKrakenPair("FOO/USD")];
            Assert.Contains("cancel-only", foo.Refusal(new TradeSignal("FOO/USD", OrderSide.Buy, 10), null));
        }

        [Fact]
        public void Volumes_are_cut_down_to_the_pairs_decimals_and_prices_to_its_tick()
        {
            var pairs = Pairs();
            var tao = pairs[KrakenProvider.CanonicalKrakenPair("TAO/USD")];
            var btc = pairs[KrakenProvider.CanonicalKrakenPair("BTC/USD")];

            // Never rounded UP: that would buy more than asked.
            Assert.Equal("0.12345", tao.FormatVolume(0.123459));
            Assert.Equal("0.29000", tao.FormatVolume(0.29));
            Assert.Equal("412.3457", tao.FormatPrice(412.34567));
            Assert.Equal("65000.1", btc.FormatPrice(65000.123));
        }

        // ── The order payload ────────────────────────────────────────────────

        private static (Dictionary<string, string>? Post, string? Error) Build(TradeSignal s, int? closesMarginAt = null)
        {
            using var p = new KrakenProvider();
            return p.BuildAddOrderPayload(s, Pairs()[KrakenProvider.CanonicalKrakenPair(s.Symbol)], closesMarginAt);
        }

        [Fact]
        public void Leverage_is_sent_as_asked_not_clamped_to_five()
        {
            // The old payload sent (int)Math.Clamp(lev, 2, 5): 8 times on BTC/USD went out as 5.
            var (post, error) = Build(new TradeSignal("BTC/USD", OrderSide.Buy, 0.001, Leverage: 8));
            Assert.Null(error);
            Assert.Equal("8", post!["leverage"]);
        }

        [Fact]
        public void Leverage_the_pair_does_not_lend_at_is_refused_with_the_amounts_it_does()
        {
            var (post, error) = Build(new TradeSignal("KAS/USD", OrderSide.Buy, 200, Leverage: 5));
            Assert.Null(post);
            Assert.StartsWith("ORDER_FAILED:", error);
            Assert.Contains("2 or 3", error);
        }

        [Fact]
        public void Fractional_leverage_is_refused_not_rounded()
        {
            // 2.9 used to go out as 2 — a position a third smaller than the one asked for.
            var (post, error) = Build(new TradeSignal("BTC/USD", OrderSide.Buy, 0.001, Leverage: 2.9));
            Assert.Null(post);
            Assert.Contains("whole-number", error);
        }

        [Fact]
        public void The_payload_carries_the_pairs_own_decimals()
        {
            var (post, _) = Build(new TradeSignal("BTC/USD", OrderSide.Buy, 0.0012345678912, OrderType.Limit, Price: 65000.123, StopLoss: 61000.06));
            Assert.Equal("0.00123456", post!["volume"]);
            Assert.Equal("65000.1", post["price"]);
            Assert.Equal("61000.1", post["close[price]"]);
        }

        [Fact]
        public void Closing_a_margin_position_sends_its_leverage_and_reduce_only()
        {
            // Without leverage the same sell is a SPOT sell — of coins the account may not hold
            // — and the borrowed position stays open.
            var (post, _) = Build(new TradeSignal("BTC/USD", OrderSide.Sell, 0.001, ReduceOnly: true), closesMarginAt: 3);
            Assert.Equal("3", post!["leverage"]);
            Assert.Equal("true", post["reduce_only"]);
        }

        [Fact]
        public void Closing_a_spot_holding_sends_no_leverage()
        {
            var (post, _) = Build(new TradeSignal("BTC/USD", OrderSide.Sell, 0.001, ReduceOnly: true), closesMarginAt: null);
            Assert.False(post!.ContainsKey("leverage"));
            Assert.False(post.ContainsKey("reduce_only"));
        }

        [Fact]
        public void A_reduce_only_buy_with_no_margin_short_is_refused()
        {
            // Without leverage it is a spot buy, which only ever adds coins.
            var (post, error) = Build(new TradeSignal("BTC/USD", OrderSide.Buy, 0.001, ReduceOnly: true), closesMarginAt: null);
            Assert.Null(post);
            Assert.Contains("reduce-only buy", error);
        }

        [Fact]
        public void Kraken_declares_reduce_only_now_that_it_honours_it() =>
            Assert.True(new KrakenProvider().Capabilities.HasFlag(ProviderCapabilities.ReduceOnly));

        [Fact]
        public async Task A_too_small_order_never_reaches_AddOrder()
        {
            using var p = new KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Get("/0/public/AssetPairs", AssetPairsJson);
            handler.Post("/0/private/AddOrder", """{"error":[],"result":{"txid":["OAAAAA-BBBBB-CCCCCC"]}}""");
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            string result = await p.PlaceOrderAsync(new TradeSignal("KAS/USD", OrderSide.Buy, 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("170", result);
            Assert.DoesNotContain(handler.Captured, r => r.RequestUri!.AbsolutePath.EndsWith("AddOrder"));
        }

        [Fact]
        public async Task A_reduce_only_sell_against_an_open_margin_long_goes_out_as_a_margin_close()
        {
            using var p = new KrakenProvider();
            p.Configure(SymbolListHarness.Credentials());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Get("/0/public/AssetPairs", AssetPairsJson);
            // A 0.01 BTC long at 3 times: cost 650, margin 216.67.
            handler.Post("/0/private/OpenPositions$", """
                {"error":[],"result":{"TABC":{"pair":"XXBTZUSD","type":"buy","vol":"0.01","cost":"650","margin":"216.67","value":"655","net":"5"}}}
                """);
            handler.Post("/0/private/AddOrder$", """{"error":[],"result":{"txid":["OAAAAA-BBBBB-CCCCCC"]}}""");
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            string result = await p.PlaceOrderAsync(new TradeSignal("BTC/USD", OrderSide.Sell, 0.01, ReduceOnly: true));

            Assert.Equal("OAAAAA-BBBBB-CCCCCC", result);
            int i = handler.Captured.FindIndex(r => r.RequestUri!.AbsolutePath.EndsWith("AddOrder"));
            Assert.Contains("leverage=3", handler.CapturedBodies[i]);
            Assert.Contains("reduce_only=true", handler.CapturedBodies[i]);
        }

        [Theory]
        [InlineData(new[] { 2, 3 }, "2 or 3")]
        [InlineData(new[] { 2, 3, 4, 5 }, "2 to 5")]
        [InlineData(new[] { 2, 3, 5 }, "2, 3 or 5")]
        [InlineData(new[] { 4 }, "4")]
        public void Leverage_lists_are_said_the_way_a_person_would(int[] values, string said) =>
            Assert.Equal(said, KrakenProvider.SayList(values));

        // ── Portfolio value of suffixed holdings ─────────────────────────────

        [Theory]
        [InlineData("BTC.S", "BTC")]
        [InlineData("BTC.M", "BTC")]
        [InlineData("DOT.P", "DOT")]
        [InlineData("USD.HOLD", "USD")]
        [InlineData("BTC.F", "BTC")]
        // Hyperliquid's perps margin is USDC in another account on the same venue.
        [InlineData("USDC-PERPS", "USDC")]
        // And a builder's margin there (HIP-3); a name merely containing the letters is left alone.
        [InlineData("USDC-PERPS-XYZ", "USDC")]
        [InlineData("USDC-PERPSX", "USDC-PERPSX")]
        // A share class, not a staked Berkshire.
        [InlineData("BRK.B", "BRK.B")]
        [InlineData("BTC", "BTC")]
        public void Suffixed_holdings_are_priced_as_their_base_asset(string asset, string pricedAs) =>
            Assert.Equal(pricedAs, PortfolioValuationService.PricedAs(asset));

        // ── Kraken Futures leverage ──────────────────────────────────────────

        private static Dictionary<string, string> LiveFutures()
        {
            var c = SymbolListHarness.Credentials();
            c["Environment"] = "Live";
            return c;
        }

        [Fact]
        public async Task Futures_leverage_is_a_PUT_with_its_arguments_in_the_query()
        {
            var p = new KrakenFuturesProvider();
            p.Configure(LiveFutures());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Add(HttpMethod.Put, "/leveragepreferences\\?symbol=pf_xbtusd&maxLeverage=5$", """{"result":"success"}""");
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            double applied = await p.SetLeverageAsync("PF_XBTUSD", 5);

            Assert.Equal(5, applied);
            Assert.Single(handler.Captured);
        }

        [Fact]
        public async Task A_futures_order_with_leverage_sets_it_first_and_is_not_sent_when_that_fails()
        {
            // The order used to go out with the ticket's leverage silently dropped.
            var p = new KrakenFuturesProvider();
            p.Configure(LiveFutures());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Add(HttpMethod.Put, "/leveragepreferences", """{"result":"error","error":"marketUnavailable"}""");
            handler.Post("/sendorder", """{"result":"success","sendStatus":{"status":"placed","order_id":"x"}}""");
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            string result = await p.PlaceOrderAsync(new TradeSignal("PF_XBTUSD", OrderSide.Buy, 1, Leverage: 5));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Contains("5 times", result);
            // Vacuity: the refusal must be the venue's answer to the leverage call, not a
            // failure before any request left (a Paper profile throws before signing).
            Assert.Contains(handler.Captured, r => r.Method == HttpMethod.Put);
            Assert.DoesNotContain(handler.Captured, r => r.RequestUri!.AbsolutePath.EndsWith("sendorder"));
        }

        [Fact]
        public async Task A_futures_order_with_leverage_goes_out_once_the_venue_has_set_it()
        {
            var p = new KrakenFuturesProvider();
            p.Configure(LiveFutures());
            var handler = new FakeHttpMessageHandler { StrictMode = true };
            handler.Add(HttpMethod.Put, "/leveragepreferences", """{"result":"success"}""");
            handler.Post("/sendorder", """{"result":"success","sendStatus":{"status":"placed","order_id":"abc"}}""");
            SymbolListHarness.SwapEveryHttpClient(p, handler);

            string result = await p.PlaceOrderAsync(new TradeSignal("PF_XBTUSD", OrderSide.Buy, 1, Leverage: 5));

            Assert.Equal("abc", result);
            // The contract-rules read (a GET) comes first; what matters is leverage before the order.
            var writes = handler.Captured.Where(r => r.Method != HttpMethod.Get).ToList();
            Assert.Equal(HttpMethod.Put, writes[0].Method);
            Assert.EndsWith("sendorder", writes[1].RequestUri!.AbsolutePath);
        }
    }
}
