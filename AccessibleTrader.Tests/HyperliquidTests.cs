using System.Net;
using System.Text;
using AccessibleTrader.Plugins.Hyperliquid;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Hyperliquid. The signature is the one thing that is exactly right or rejects every order
    /// with no clue why, so the first block pins it to the vectors in the Python SDK's own
    /// <c>tests/signing_test.py</c> (copied verbatim from github.com/hyperliquid-dex/hyperliquid-python-sdk,
    /// 2026-11-04). The rest pins the request shapes, rounding, asset ids and reply parsing against
    /// small fixtures: meta, spotMeta and candleSnapshot are TRIMMED COPIES OF LIVE REPLIES taken
    /// 2026-11-04; clearinghouseState and frontendOpenOrders are written from the documented shape
    /// and were NOT recorded from an account (each says so in its "_note").
    /// No test touches the network.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class HyperliquidTests
    {
        private const string Key = "0x0123456789012345678901234567890123456789012345678901234567890123";

        private static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hyperliquid", name));

        private static OrderedMap Dummy(long num) => new OrderedMap().Add("type", "dummy").Add("num", num);

        private static OrderedMap EthOrder(string? cloid = null) => HyperliquidWire.OrderAction(new[]
        {
            HyperliquidWire.OrderWire(1, true, "100", "100", false, HyperliquidWire.LimitType("Gtc"), cloid),
        });

        // ── 1. Signing: the Python SDK's vectors ─────────────────────────────

        public static IEnumerable<object[]> Vectors()
        {
            object[] V(string name, string action, string? vault, bool mainnet, string r, string s, int v) =>
                new object[] { name, vault!, mainnet, r, s, v };

            const string dummy = "dummy", order = "order", cloid = "order+cloid", tpsl = "tpsl";
            const string vault = "0x1719884eb866cb12b2287399b15f7db5e7d775ea";

            yield return V("dummy", dummy, null, true,  "0x53749d5b30552aeb2fca34b530185976545bb22d0b3ce6f62e31be961a59298", "0x755c40ba9bf05223521753995abb2f73ab3229be8ec921f350cb447e384d8ed8", 27);
            yield return V("dummy", dummy, null, false, "0x542af61ef1f429707e3c76c5293c80d01f74ef853e34b76efffcb57e574f9510", "0x17b8b32f086e8cdede991f1e2c529f5dd5297cbe8128500e00cbaf766204a613", 28);
            yield return V("order", order, null, true,  "0xd65369825a9df5d80099e513cce430311d7d26ddf477f5b3a33d2806b100d78e", "0x2b54116ff64054968aa237c20ca9ff68000f977c93289157748a3162b6ea940e", 28);
            yield return V("order", order, null, false, "0x82b2ba28e76b3d761093aaded1b1cdad4960b3af30212b343fb2e6cdfa4e3d54", "0x6b53878fc99d26047f4d7e8c90eb98955a109f44209163f52d8dc4278cbbd9f5", 27);
            yield return V("order+cloid", cloid, null, true,  "0x41ae18e8239a56cacbc5dad94d45d0b747e5da11ad564077fcac71277a946e3", "0x3c61f667e747404fe7eea8f90ab0e76cc12ce60270438b2058324681a00116da", 27);
            yield return V("order+cloid", cloid, null, false, "0xeba0664bed2676fc4e5a743bf89e5c7501aa6d870bdb9446e122c9466c5cd16d", "0x7f3e74825c9114bc59086f1eebea2928c190fdfbfde144827cb02b85bbe90988", 28);
            yield return V("dummy+vault", dummy, vault, true,  "0x3c548db75e479f8012acf3000ca3a6b05606bc2ec0c29c50c515066a326239", "0x4d402be7396ce74fbba3795769cda45aec00dc3125a984f2a9f23177b190da2c", 28);
            yield return V("dummy+vault", dummy, vault, false, "0xe281d2fb5c6e25ca01601f878e4d69c965bb598b88fac58e475dd1f5e56c362b", "0x7ddad27e9a238d045c035bc606349d075d5c5cd00a6cd1da23ab5c39d4ef0f60", 27);
            yield return V("tpsl", tpsl, null, true,  "0x98343f2b5ae8e26bb2587daad3863bc70d8792b09af1841b6fdd530a2065a3f9", "0x6b5bb6bb0633b710aa22b721dd9dee6d083646a5f8e581a20b545be6c1feb405", 27);
            yield return V("tpsl", tpsl, null, false, "0x971c554d917c44e0e1b6cc45d8f9404f32172a9d3b3566262347d0302896a2e4", "0x206257b104788f80450f8e786c329daa589aa0b32ba96948201ae556d5637eac", 28);
            yield return V("scheduleCancel", "scheduleCancel", null, true,  "0x6cdfb286702f5917e76cd9b3b8bf678fcc49aec194c02a73e6d4f16891195df9", "0x6557ac307fa05d25b8d61f21fb8a938e703b3d9bf575f6717ba21ec61261b2a0", 27);
            yield return V("scheduleCancel", "scheduleCancel", null, false, "0xc75bb195c3f6a4e06b7d395acc20bbb224f6d23ccff7c6a26d327304e6efaeed", "0x342f8ede109a29f2c0723bd5efb9e9100e3bbb493f8fb5164ee3d385908233df", 28);
            yield return V("scheduleCancel@t", "scheduleCancel@t", null, true,  "0x609cb20c737945d070716dcc696ba030e9976fcf5edad87afa7d877493109d55", "0x16c685d63b5c7a04512d73f183b3d7a00da5406ff1f8aad33f8ae2163bab758b", 28);
            yield return V("scheduleCancel@t", "scheduleCancel@t", null, false, "0x4e4f2dbd4107c69783e251b7e1057d9f2b9d11cee213441ccfa2be63516dc5bc", "0x706c656b23428c8ba356d68db207e11139ede1670481a9e01ae2dfcdb0e1a678", 27);
        }

        [Theory]
        [MemberData(nameof(Vectors))]
        public void Signature_matches_the_Python_SDKs_own_test_vector(
            string name, string? vault, bool mainnet, string r, string s, int v)
        {
            OrderedMap action = name switch
            {
                "dummy" or "dummy+vault" => Dummy(100000000000),
                "order" => EthOrder(),
                "order+cloid" => EthOrder("0x00000000000000000000000000000001"),
                "tpsl" => HyperliquidWire.OrderAction(new[] { HyperliquidWire.OrderWire(1, true, "100", "100", false, HyperliquidWire.TriggerType(true, "103", "sl"), null) }),
                "scheduleCancel" => HyperliquidWire.ScheduleCancelAction(null),
                _ => HyperliquidWire.ScheduleCancelAction(123456789),
            };
            var sig = HyperliquidSigner.SignL1Action(Key, action, vault, 0, mainnet);

            Assert.True(r == sig.R && s == sig.S && v == sig.V,
                $"{name} {(mainnet ? "mainnet" : "testnet")}: expected r={r} s={s} v={v}, signed r={sig.R} s={sig.S} v={sig.V}");
        }

        [Fact]
        public void Connection_id_of_the_Python_SDKs_production_order_matches()
        {
            var order = HyperliquidWire.OrderAction(new[]
            {
                HyperliquidWire.OrderWire(4, true, "1670.1", "0.0147", false, HyperliquidWire.LimitType("Ioc"), null),
            });

            var id = HyperliquidSigner.ActionHash(order, null, 1677777606040UL);

            Assert.Equal("0fcbeda5ae3c4950a548021552a4fea2226858c4453571bf3f24ba017eac2908", Convert.ToHexString(id).ToLowerInvariant());
        }

        [Fact]
        public void Mainnet_and_testnet_sign_differently_and_the_nonce_is_part_of_the_signature()
        {
            var a = HyperliquidSigner.SignL1Action(Key, EthOrder(), null, 1, true);
            Assert.NotEqual(a, HyperliquidSigner.SignL1Action(Key, EthOrder(), null, 1, false));
            Assert.NotEqual(a, HyperliquidSigner.SignL1Action(Key, EthOrder(), null, 2, true));
            Assert.Equal(a, HyperliquidSigner.SignL1Action(Key, EthOrder(), null, 1, true));
        }

        [Fact]
        public void Address_of_a_private_key_is_the_published_one()
        {
            // The first Hardhat/Anvil development account, whose address is published everywhere.
            Assert.Equal("0xf39fd6e51aad88f6f4ce6ab8827279cffFb92266".ToLowerInvariant(),
                HyperliquidSigner.AddressOf("0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80").ToLowerInvariant());
        }

        // ── 2. msgpack bytes ─────────────────────────────────────────────────

        [Fact]
        public void Msgpack_of_the_dummy_action_is_the_bytes_the_Python_msgpack_makes()
        {
            // {"type":"dummy","num":100000000000}: fixmap(2), fixstr keys, then uint64 for a value over 2^32.
            Assert.Equal("82a474797065a564756d6d79a3 6e756dcf000000174876e800".Replace(" ", ""),
                Convert.ToHexString(MsgPack.Pack(Dummy(100000000000))).ToLowerInvariant());
        }

        [Theory]
        [InlineData(0L, "00")]
        [InlineData(127L, "7f")]
        [InlineData(128L, "cc80")]
        [InlineData(255L, "ccff")]
        [InlineData(256L, "cd0100")]
        [InlineData(65536L, "ce00010000")]
        [InlineData(4294967296L, "cf0000000100000000")]
        [InlineData(-1L, "ff")]
        [InlineData(-32L, "e0")]
        [InlineData(-33L, "d0df")]
        public void Msgpack_integers_use_the_smallest_encoding(long value, string hex) =>
            Assert.Equal(hex, Convert.ToHexString(MsgPack.Pack(value)).ToLowerInvariant());

        [Fact]
        public void Msgpack_booleans_arrays_and_long_strings()
        {
            Assert.Equal("c3", Convert.ToHexString(MsgPack.Pack(true)).ToLowerInvariant());
            Assert.Equal("c2", Convert.ToHexString(MsgPack.Pack(false)).ToLowerInvariant());
            Assert.Equal("9201a161", Convert.ToHexString(MsgPack.Pack(new object[] { 1, "a" })).ToLowerInvariant());
            string longText = new('x', 40);   // over 31 bytes: str8, not fixstr
            Assert.Equal("d928" + new string('7', 0) + string.Concat(Enumerable.Repeat("78", 40)),
                Convert.ToHexString(MsgPack.Pack(longText)).ToLowerInvariant());
        }

        // ── 3. Number formatting and rounding ────────────────────────────────

        [Theory]
        [InlineData("1.50", "1.5")]
        [InlineData("100", "100")]
        [InlineData("100.000", "100")]
        [InlineData("0.00010000", "0.0001")]
        [InlineData("0", "0")]
        [InlineData("0.123456789", "0.12345679")]
        public void Wire_numbers_have_no_trailing_zeros_and_at_most_eight_decimals(string input, string expected) =>
            Assert.Equal(expected, HyperliquidWire.FormatWire(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

        [Theory]
        // price, szDecimals, spot, expected wire string. Rules: 5 significant figures, then at most
        // (6 perp / 8 spot) minus szDecimals decimals; a price of 100,000 or more is whole.
        [InlineData(1234.5678, 5, false, "1234.6")]     // 5 sig figs gives 1234.6, one decimal allowed
        [InlineData(1234.5678, 2, false, "1234.6")]     // 5 sig figs binds, not the 4 decimals
        [InlineData(0.0123456, 0, false, "0.012346")]   // 5 sig figs gives 0.012346, six decimals allowed
        [InlineData(0.0123456, 3, false, "0.012")]      // only 3 decimals allowed
        [InlineData(123456.789, 5, false, "123457")]    // whole-number price above 100k
        [InlineData(90.25, 5, false, "90.2")]           // BTC-like szDecimals 5 leaves ONE decimal
        [InlineData(0.000123456, 2, true, "0.000123")]  // spot: 8 - 2 = 6 decimals
        [InlineData(100, 1, false, "100")]
        public void Prices_round_to_what_the_exchange_accepts(double price, int szDecimals, bool spot, string expected) =>
            Assert.Equal(expected, HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(price, szDecimals, spot)));

        [Theory]
        [InlineData(0.123456, 5, "0.12345")]    // truncated, never rounded up
        [InlineData(0.999999, 2, "0.99")]
        [InlineData(1.9, 0, "1")]
        [InlineData(0.6, 5, "0.6")]
        [InlineData(100, 3, "100")]
        public void Sizes_are_truncated_never_rounded_up(double size, int szDecimals, string expected) =>
            Assert.Equal(expected, HyperliquidWire.FormatWire(HyperliquidWire.RoundSize(size, szDecimals)));

        [Fact]
        public void A_size_that_truncates_to_zero_is_zero()
        {
            Assert.Equal(0m, HyperliquidWire.RoundSize(0.000001, 5));
            Assert.Equal(0m, HyperliquidWire.RoundSize(-1, 5));
        }

        // ── 4. Asset ids ─────────────────────────────────────────────────────

        [Fact]
        public void Perp_asset_id_is_the_universe_position_and_a_delisted_entry_keeps_its_number()
        {
            var perps = HyperliquidWire.ParseMeta(JObject.Parse(Fixture("meta.json")));

            Assert.Equal(new[] { "BTC-PERP", "ETH-PERP", perps[2].Display }, perps.Select(p => p.Display));
            Assert.Equal(0, perps[0].AssetId);
            Assert.Equal(1, perps[1].AssetId);
            Assert.Equal(2, perps[2].AssetId);
            Assert.DoesNotContain(perps, p => p.Coin == "MATIC");    // delisted: not offered, but...
            Assert.Equal(3, JObject.Parse(Fixture("meta.json"))["universe"]!.Count() - 1);   // ...it still occupied slot 3
            Assert.Equal(5, perps[0].SzDecimals);
            Assert.Equal(40, perps[0].MaxLeverage);
        }

        [Fact]
        public void Spot_asset_id_is_10000_plus_the_pair_index_and_the_coin_is_the_pair_name()
        {
            var spot = HyperliquidWire.ParseSpotMeta(JObject.Parse(Fixture("spotMeta.json")));

            var purr = Assert.Single(spot, s => s.Display == "PURR-USDC");
            Assert.Equal(10000, purr.AssetId);
            Assert.Equal("PURR/USDC", purr.Coin);
            Assert.Equal(0, purr.SzDecimals);                       // the BASE token's
            var hype = Assert.Single(spot, s => s.Display == "HYPE-USDC");
            Assert.Equal(10107, hype.AssetId);
            Assert.Equal("@107", hype.Coin);
            Assert.Equal(2, hype.SzDecimals);
            Assert.All(spot, s => Assert.True(s.IsSpot));
        }

        [Theory]
        [InlineData("btc-perp")]
        [InlineData("BTC/PERP")]
        [InlineData("BTCPERP")]
        public void Symbol_spellings_are_one_market(string spelling) =>
            Assert.Equal("BTCPERP", HyperliquidWire.Normalise(spelling));

        // ── 5. Parsing recorded replies ──────────────────────────────────────

        [Fact]
        public void Candle_snapshot_rows_become_bars_with_the_volume()
        {
            var rows = JArray.Parse(Fixture("candleSnapshot_BTC_1d.json"));

            Assert.True(HyperliquidWire.TryParseCandle(rows[0], out var bar, out long t));

            Assert.Equal(1793404800000L, t);
            Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), bar.Date);
            Assert.Equal(77338.0, bar.Open); Assert.Equal(77750.0, bar.High);
            Assert.Equal(77072.0, bar.Low);  Assert.Equal(77535.0, bar.Close);
            Assert.Equal(8869.4156, bar.Volume, 6);
        }

        [Fact]
        public void A_candle_without_a_time_is_skipped()
        {
            Assert.False(HyperliquidWire.TryParseCandle(JObject.Parse("{\"o\":\"1\"}"), out _, out _));
        }

        [Fact]
        public void Positions_carry_sign_leverage_mode_and_the_venues_liquidation_price()
        {
            var perps = HyperliquidWire.ParseMeta(JObject.Parse(Fixture("meta.json"))).ToDictionary(a => a.Coin);

            var list = HyperliquidProvider.ParsePositions(JObject.Parse(Fixture("clearinghouseState.json")), perps);

            Assert.Equal(2, list.Count);   // the zero-size MATIC row is not a position
            var btc = list[0];
            Assert.Equal("BTC-PERP", btc.Symbol);
            Assert.Equal(0.02, btc.Quantity);
            Assert.Equal(95000.0, btc.AveragePrice);
            Assert.Equal(10.0, btc.Leverage);
            Assert.Equal(80000.0, btc.LiquidationPrice);
            Assert.Equal(MarginMode.Isolated, btc.MarginMode);
            var eth = list[1];
            Assert.Equal(-1.5, eth.Quantity);
            Assert.True(eth.MarketValue < 0);                 // a short is negative
            Assert.Equal(0.0, eth.LiquidationPrice);          // null from the venue reads as none
            Assert.Equal(MarginMode.Cross, eth.MarginMode);
        }

        [Fact]
        public void Open_orders_map_side_type_trigger_and_price()
        {
            var list = HyperliquidProvider.ParseOpenOrders(JArray.Parse(Fixture("frontendOpenOrders.json")), c => c + "-PERP");

            Assert.Equal(2, list.Count);
            Assert.Equal("12345", list[0].Id);
            Assert.Equal("BTC-PERP", list[0].Symbol);
            Assert.Equal(OrderSide.Buy, list[0].Side);
            Assert.Equal(OrderType.Limit, list[0].Type);
            Assert.Equal(90000.0, list[0].Price);
            Assert.Equal(OrderSide.Sell, list[1].Side);
            Assert.Equal(OrderType.StopMarket, list[1].Type);
            Assert.Equal(2900.0, list[1].Price);               // a stop market shows its trigger
        }

        [Fact]
        public void Book_levels_are_bids_then_asks_best_first_and_limited()
        {
            var book = JObject.Parse("""{"coin":"BTC","time":1,"levels":[[{"px":"99.0","sz":"1","n":1},{"px":"98.0","sz":"2","n":2}],[{"px":"100.0","sz":"3","n":1}]]}""");

            var (bids, asks) = HyperliquidProvider.ParseBook(book, 1);

            Assert.Single(bids); Assert.Equal(99.0, bids[0].Price); Assert.Equal(1.0, bids[0].Quantity);
            Assert.Single(asks); Assert.Equal(100.0, asks[0].Price);
        }

        [Fact]
        public void Order_replies_resting_filled_error_and_waiting()
        {
            var o = HyperliquidWire.ParseOrderReply(JObject.Parse("""{"status":"ok","response":{"type":"order","data":{"statuses":[{"resting":{"oid":77}},{"filled":{"totalSz":"0.02","avgPx":"1891.4","oid":78}},{"error":"Order must have minimum value of $10."},"waitingForTrigger"]}}}"""));

            Assert.Equal("77", o[0].Oid); Assert.False(o[0].Filled);
            Assert.Equal("78", o[1].Oid); Assert.True(o[1].Filled); Assert.Equal(0.02, o[1].FilledSize); Assert.Equal(1891.4, o[1].AvgPrice);
            Assert.True(o[2].IsError); Assert.Contains("minimum value", o[2].Error);
            Assert.False(o[3].IsError); Assert.Null(o[3].Oid);
        }

        [Fact]
        public void A_reply_with_err_status_or_an_unknown_shape_is_never_a_success()
        {
            var err = HyperliquidWire.ParseOrderReply(JObject.Parse("""{"status":"err","response":"User or API Wallet 0xabc does not exist."}"""));
            Assert.True(Assert.Single(err).IsError);
            Assert.Contains("does not exist", err[0].Error);

            var odd = HyperliquidWire.ParseOrderReply(JObject.Parse("""{"status":"ok","response":{"type":"order","data":{"statuses":[{"somethingNew":1}]}}}"""));
            Assert.True(Assert.Single(odd).IsError);
        }

        [Theory]
        [InlineData("open", "0.5", "0.5", PolledOrderState.Working)]
        [InlineData("open", "0.5", "0.2", PolledOrderState.PartiallyFilled)]
        [InlineData("filled", "0.5", "0", PolledOrderState.Filled)]
        [InlineData("canceled", "0.5", "0.5", PolledOrderState.Cancelled)]
        [InlineData("marginCanceled", "0.5", "0.5", PolledOrderState.Cancelled)]
        [InlineData("minTradeNtlRejected", "0.5", "0.5", PolledOrderState.Rejected)]
        public void Order_status_strings_map_to_polled_states(string status, string orig, string remaining, PolledOrderState expected)
        {
            var reply = JObject.Parse("{\"status\":\"order\",\"order\":{\"order\":{\"coin\":\"BTC\",\"side\":\"B\",\"limitPx\":\"1\",\"sz\":\"" + remaining
                + "\",\"oid\":5,\"origSz\":\"" + orig + "\"},\"status\":\"" + status + "\",\"statusTimestamp\":1}}");

            var snap = HyperliquidProvider.ParseOrderStatus(reply, c => c + "-PERP");

            Assert.NotNull(snap);
            Assert.Equal(expected, snap!.State);
            Assert.Equal("BTC-PERP", snap.Symbol);
        }

        [Fact]
        public void An_unknown_order_id_is_null_not_a_state()
        {
            Assert.Null(HyperliquidProvider.ParseOrderStatus(JObject.Parse("""{"status":"unknownOid"}"""), c => c));
        }

        [Fact]
        public void Asset_context_reads_funding_open_interest_and_prices_by_universe_position()
        {
            var reply = JArray.Parse("""[{"universe":[]},[{"funding":"0.0000125","openInterest":"31000.5","markPx":"100.5","oraclePx":"100.4","dayNtlVlm":"5000000"},{"funding":"-0.0001","openInterest":"9","markPx":"3.0","oraclePx":"3.1","dayNtlVlm":"1"}]]""");

            var ctx = HyperliquidProvider.ParseAssetContext(reply, 1, "ETH-PERP");

            Assert.NotNull(ctx);
            Assert.Equal(-0.0001, ctx!.FundingRate);
            Assert.Equal(9.0, ctx.OpenInterest);
            Assert.Null(HyperliquidProvider.ParseAssetContext(reply, 5, "X-PERP"));
        }

        [Fact]
        public void Websocket_candle_frames_parse_with_the_coin_and_interval_they_belong_to()
        {
            var bars = HyperliquidProvider.ParseSocketCandles(JObject.Parse(
                """{"channel":"candle","data":{"t":1793404800000,"T":1793491199999,"s":"BTC","i":"1d","o":"1","c":"2","h":"3","l":"0.5","v":"10","n":4}}"""),
                out var coin, out var interval);

            Assert.Single(bars);
            Assert.Equal("BTC", coin); Assert.Equal("1d", interval);
            Assert.Equal(10.0, bars[0].Volume);
            Assert.Empty(HyperliquidProvider.ParseSocketCandles(JObject.Parse("""{"channel":"pong"}"""), out _, out _));
        }

        [Fact]
        public void Subscribe_messages_have_the_documented_shape()
        {
            Assert.Equal("""{"method":"subscribe","subscription":{"type":"candle","coin":"BTC","interval":"1m"}}""",
                HyperliquidProvider.SubscribeMessage("subscribe", "candle", "BTC", "1m"));
            Assert.Equal("""{"method":"unsubscribe","subscription":{"type":"l2Book","coin":"BTC"}}""",
                HyperliquidProvider.SubscribeMessage("unsubscribe", "l2Book", "BTC", null));
        }

        // ── 6. Credentials ───────────────────────────────────────────────────

        private const string Main = "0x1719884eb866cb12b2287399b15f7db5e7d775ea";

        [Fact]
        public void The_main_wallets_own_key_is_refused()
        {
            const string mainKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";
            string address = HyperliquidSigner.AddressOf(mainKey);

            Assert.Contains("MAIN wallet", HyperliquidProvider.CheckCredential(address, mainKey));
            Assert.Null(HyperliquidProvider.CheckCredential(Main, Key));
        }

        [Theory]
        [InlineData("not an address", Key)]
        [InlineData(Main, "short")]
        [InlineData(Main, "")]
        public void Malformed_credentials_are_explained(string address, string secret) =>
            Assert.False(string.IsNullOrEmpty(HyperliquidProvider.CheckCredential(address, secret)));

        [Fact]
        public async Task The_main_key_sends_no_order_and_makes_no_request()
        {
            const string mainKey = "0xac0974bec39a17e36ba4a6b4d238ff944bacb478cbed5efcae784d7bf4f2ff80";
            var h = new FakeHttpMessageHandler { StrictMode = true };
            var p = Provider(h, address: HyperliquidSigner.AddressOf(mainKey), key: mainKey);

            string result = await p.PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.1, OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Empty(h.Captured);
        }

        [Fact]
        public async Task Without_credentials_trading_is_refused_but_market_data_needs_none()
        {
            var h = Armed();
            var p = new HyperliquidProvider();
            SetHttp(p, h);

            string result = await p.PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.1, OrderType.Limit, Price: 100));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.False(p.RequiresApiKey);
            Assert.DoesNotContain(h.Captured, r => r.RequestUri!.AbsolutePath == "/exchange");
        }

        [Fact]
        public void The_description_carries_the_US_and_Ontario_notice()
        {
            string d = new HyperliquidProvider().Description;
            Assert.Contains("United States", d);
            Assert.Contains("Ontario", d);
        }

        // ── 7. Request shapes, through the real provider ─────────────────────

        private static void SetHttp(HyperliquidProvider p, FakeHttpMessageHandler h) =>
            typeof(HyperliquidProvider).GetField("_http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(p, new HttpClient(h));

        private static HyperliquidProvider Provider(FakeHttpMessageHandler h, string? env = ProviderConfigKeys.Live,
            string address = Main, string key = Key)
        {
            var p = new HyperliquidProvider();
            var cfg = new Dictionary<string, string> { ["ApiKey"] = address, ["ApiSecret"] = key };
            if (env != null) cfg[ProviderConfigKeys.Environment] = env;
            p.Configure(cfg);
            SetHttp(p, h);
            return p;
        }

        private static string Reply(string type, string body) =>
            "{\"status\":\"ok\",\"response\":{\"type\":\"" + type + "\",\"data\":" + body + "}}";

        /// <summary>A handler serving the fixtures for /info and a scripted answer for /exchange.</summary>
        private static FakeHttpMessageHandler Armed(Func<JObject, string>? exchange = null, Func<JObject, string?>? info = null)
        {
            var h = new FakeHttpMessageHandler();
            h.Add(HttpMethod.Post, @"hyperliquid(-testnet)?\.xyz/info", req =>
            {
                var body = JObject.Parse(req.Content!.ReadAsStringAsync().Result);
                string text = info?.Invoke(body) ?? (body["type"]?.ToString() switch
                {
                    "meta" => Fixture("meta.json"),
                    "spotMeta" => Fixture("spotMeta.json"),
                    "allMids" => "{\"BTC\":\"100000.0\",\"ETH\":\"3000.0\",\"@107\":\"40.0\"}",
                    "frontendOpenOrders" => Fixture("frontendOpenOrders.json"),
                    "clearinghouseState" => Fixture("clearinghouseState.json"),
                    _ => "{}",
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            });
            h.Add(HttpMethod.Post, @"hyperliquid(-testnet)?\.xyz/exchange", req =>
            {
                var body = JObject.Parse(req.Content!.ReadAsStringAsync().Result);
                string text = exchange?.Invoke(body) ?? Reply("order", "{\"statuses\":[{\"resting\":{\"oid\":777}}]}");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
            });
            return h;
        }

        private static List<JObject> Exchange(FakeHttpMessageHandler h) =>
            h.Captured.Zip(h.CapturedBodies)
                .Where(x => x.First.RequestUri!.AbsolutePath == "/exchange")
                .Select(x => JObject.Parse(x.Second)).ToList();

        private static JObject Single(FakeHttpMessageHandler h) => Assert.Single(Exchange(h));

        [Fact]
        public async Task Limit_order_body_has_the_exact_wire_shape_and_a_signature_that_verifies()
        {
            var h = Armed();
            var p = Provider(h);

            string id = await p.PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 12345.678));

            Assert.Equal("777", id);
            var body = Single(h);
            Assert.Equal("""{"type":"order","orders":[{"a":0,"b":true,"p":"12346","s":"0.5","r":false,"t":{"limit":{"tif":"Gtc"}}}],"grouping":"na"}""",
                body["action"]!.ToString(Newtonsoft.Json.Formatting.None));
            ulong nonce = body["nonce"]!.Value<ulong>();
            Assert.True(nonce > 1_700_000_000_000UL);
            var expected = HyperliquidSigner.SignL1Action(Key, HyperliquidWire.OrderAction(new[]
            {
                HyperliquidWire.OrderWire(0, true, "12346", "0.5", false, HyperliquidWire.LimitType("Gtc"), null),
            }), null, nonce, isMainnet: true);
            Assert.Equal(expected.R, body["signature"]!["r"]!.ToString());
            Assert.Equal(expected.S, body["signature"]!["s"]!.ToString());
            Assert.Equal(expected.V, body["signature"]!["v"]!.Value<int>());
            Assert.Null(body["vaultAddress"]);
        }

        [Fact]
        public async Task Paper_signs_with_the_testnet_phantom_agent_to_the_testnet_host()
        {
            var h = Armed();
            var p = Provider(h, ProviderConfigKeys.Paper);

            await p.PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100));

            var req = h.Captured.Single(r => r.RequestUri!.AbsolutePath == "/exchange");
            Assert.Equal("api.hyperliquid-testnet.xyz", req.RequestUri!.Host);
            var body = Single(h);
            ulong nonce = body["nonce"]!.Value<ulong>();
            var asTestnet = HyperliquidSigner.SignL1Action(Key, HyperliquidWire.OrderAction(new[]
            {
                HyperliquidWire.OrderWire(0, true, "100", "0.5", false, HyperliquidWire.LimitType("Gtc"), null),
            }), null, nonce, isMainnet: false);
            Assert.Equal(asTestnet.R, body["signature"]!["r"]!.ToString());
        }

        [Fact]
        public async Task Market_order_is_an_IOC_limit_five_percent_past_the_mid()
        {
            var h = Armed();
            string id = await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Market));

            Assert.Equal("777", id);
            var o = Single(h)["action"]!["orders"]![0]!;
            Assert.Equal("105000", o["p"]!.ToString());                       // 100000 * 1.05
            Assert.Equal("Ioc", o["t"]!["limit"]!["tif"]!.ToString());

            var h2 = Armed();
            await Provider(h2).PlaceOrderAsync(new("BTC-PERP", OrderSide.Sell, 0.5, OrderType.Market));
            Assert.Equal("95000", Single(h2)["action"]!["orders"]![0]!["p"]!.ToString());   // 100000 * 0.95
        }

        [Fact]
        public async Task Post_only_is_ALO_ioc_is_ioc_and_unsupported_time_in_force_is_refused_before_any_request()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100, PostOnly: true));
            Assert.Equal("Alo", Single(h)["action"]!["orders"]![0]!["t"]!["limit"]!["tif"]!.ToString());

            var h2 = Armed();
            await Provider(h2).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100, TimeInForce: "IOC"));
            Assert.Equal("Ioc", Single(h2)["action"]!["orders"]![0]!["t"]!["limit"]!["tif"]!.ToString());

            var h3 = Armed();
            string result = await Provider(h3).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100, TimeInForce: "FOK"));
            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Empty(Exchange(h3));
        }

        [Fact]
        public async Task Reduce_only_sets_r_true()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Sell, 0.5, OrderType.Market, ReduceOnly: true));
            Assert.True(Single(h)["action"]!["orders"]![0]!["r"]!.Value<bool>());
        }

        [Fact]
        public async Task Stop_loss_order_is_a_trigger_market_sl_with_its_own_slipped_limit()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Sell, 0.5, OrderType.StopMarket, TriggerPrice: 90000, ReduceOnly: true));

            var o = Single(h)["action"]!["orders"]![0]!;
            Assert.Equal("""{"trigger":{"isMarket":true,"triggerPx":"90000","tpsl":"sl"}}""", o["t"]!.ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal("85500", o["p"]!.ToString());      // a sell stop may fill 5% under its trigger
            Assert.Equal("na", Single(h)["action"]!["grouping"]!.ToString());
        }

        [Fact]
        public async Task Take_profit_limit_uses_the_given_price_and_tp()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Sell, 0.5, OrderType.TakeProfitLimit, Price: 110000, TriggerPrice: 109000));

            var o = Single(h)["action"]!["orders"]![0]!;
            Assert.Equal("""{"trigger":{"isMarket":false,"triggerPx":"109000","tpsl":"tp"}}""", o["t"]!.ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal("110000", o["p"]!.ToString());
        }

        [Fact]
        public async Task An_entry_with_stop_and_target_is_one_normalTpsl_action_with_reduce_only_opposite_legs()
        {
            var h = Armed(exchange: _ => Reply("order", "{\"statuses\":[{\"resting\":{\"oid\":1}},{\"resting\":{\"oid\":2}},{\"resting\":{\"oid\":3}}]}"));

            string id = await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100000, StopLoss: 95000, TakeProfit: 110000));

            Assert.Equal("1", id);
            var action = Single(h)["action"]!;
            Assert.Equal("normalTpsl", action["grouping"]!.ToString());
            var orders = action["orders"]!.ToArray();
            Assert.Equal(3, orders.Length);
            Assert.True(orders[0]["b"]!.Value<bool>()); Assert.False(orders[0]["r"]!.Value<bool>());
            Assert.False(orders[1]["b"]!.Value<bool>()); Assert.True(orders[1]["r"]!.Value<bool>());
            Assert.Equal("tp", orders[1]["t"]!["trigger"]!["tpsl"]!.ToString());
            Assert.Equal("sl", orders[2]["t"]!["trigger"]!["tpsl"]!.ToString());
            Assert.Equal("95000", orders[2]["t"]!["trigger"]!["triggerPx"]!.ToString());
        }

        [Fact]
        public async Task A_refused_protective_leg_is_surfaced_loudly_while_the_entry_id_is_returned()
        {
            var h = Armed(exchange: _ => Reply("order", "{\"statuses\":[{\"resting\":{\"oid\":1}},{\"error\":\"Invalid TP/SL price.\"}]}"));
            var p = Provider(h);
            var errors = new List<string>();
            p.ErrorStream.Subscribe(errors.Add);

            string id = await p.PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100000, StopLoss: 95000));

            Assert.Equal("1", id);
            Assert.Contains(errors, e => e.Contains("protective order was refused"));
        }

        [Fact]
        public async Task A_spot_order_carrying_a_bracket_is_refused_before_any_request()
        {
            var h = Armed();
            string result = await Provider(h).PlaceOrderAsync(new("HYPE-USDC", OrderSide.Buy, 1, OrderType.Limit, Price: 40, StopLoss: 30, SubType: "Spot"));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Empty(Exchange(h));
        }

        [Fact]
        public async Task Spot_order_uses_asset_10107_and_the_base_tokens_size_decimals()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("HYPE-USDC", OrderSide.Buy, 1.239, OrderType.Limit, Price: 40.123456, SubType: "Spot"));

            var o = Single(h)["action"]!["orders"]![0]!;
            Assert.Equal(10107, o["a"]!.Value<int>());
            Assert.Equal("1.23", o["s"]!.ToString());     // szDecimals 2: truncated
            Assert.Equal("40.123", o["p"]!.ToString());   // 5 significant figures
        }

        [Fact]
        public async Task Leverage_is_set_first_clamped_to_the_assets_maximum_and_isolated_by_default()
        {
            int calls = 0;
            var h = Armed(exchange: body =>
            {
                calls++;
                return body["action"]!["type"]!.ToString() == "updateLeverage"
                    ? "{\"status\":\"ok\",\"response\":{\"type\":\"default\"}}"
                    : Reply("order", "{\"statuses\":[{\"resting\":{\"oid\":9}}]}");
            });

            string id = await Provider(h).PlaceOrderAsync(new("ETH-PERP", OrderSide.Buy, 1, OrderType.Limit, Price: 3000, Leverage: 100));

            Assert.Equal("9", id);
            var all = Exchange(h);
            Assert.Equal(2, all.Count);
            Assert.Equal("""{"type":"updateLeverage","asset":1,"isCross":false,"leverage":25}""", all[0]["action"]!.ToString(Newtonsoft.Json.Formatting.None));
            Assert.Equal("order", all[1]["action"]!["type"]!.ToString());
            Assert.True(all[1]["nonce"]!.Value<ulong>() > all[0]["nonce"]!.Value<ulong>());
        }

        [Fact]
        public async Task A_failed_leverage_update_stops_the_order()
        {
            var h = Armed(exchange: _ => "{\"status\":\"err\",\"response\":\"Cannot set leverage.\"}");

            string result = await Provider(h).PlaceOrderAsync(new("ETH-PERP", OrderSide.Buy, 1, OrderType.Limit, Price: 3000, Leverage: 5));

            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Equal("updateLeverage", Assert.Single(Exchange(h))["action"]!["type"]!.ToString());
        }

        [Fact]
        public async Task Cross_margin_is_sent_only_when_asked_for()
        {
            var h = Armed(exchange: body => body["action"]!["type"]!.ToString() == "updateLeverage"
                ? "{\"status\":\"ok\",\"response\":{\"type\":\"default\"}}"
                : Reply("order", "{\"statuses\":[{\"resting\":{\"oid\":9}}]}"));

            await Provider(h).PlaceOrderAsync(new("ETH-PERP", OrderSide.Buy, 1, OrderType.Limit, Price: 3000, Leverage: 3, MarginType: "Cross"));

            Assert.True(Exchange(h)[0]["action"]!["isCross"]!.Value<bool>());
        }

        [Fact]
        public async Task Venue_error_text_comes_back_in_the_failure()
        {
            var h = Armed(exchange: _ => Reply("order", "{\"statuses\":[{\"error\":\"Order must have minimum value of $10.\"}]}"));
            string result = await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.00001, OrderType.Limit, Price: 100));
            Assert.Contains("minimum value", result);
        }

        [Fact]
        public async Task A_size_that_rounds_to_zero_is_refused_not_sent()
        {
            var h = Armed();
            string result = await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.000001, OrderType.Limit, Price: 100));
            Assert.StartsWith("ORDER_FAILED:", result);
            Assert.Empty(Exchange(h));
        }

        [Fact]
        public async Task A_client_order_id_becomes_a_16_byte_cloid_and_is_stable()
        {
            var h = Armed();
            await Provider(h).PlaceOrderAsync(new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Limit, Price: 100, ClientOid: "my-order-1"));
            string c1 = Single(h)["action"]!["orders"]![0]!["c"]!.ToString();

            Assert.Matches("^0x[0-9a-f]{32}$", c1);
            Assert.Equal(c1, HyperliquidWire.ToCloid("my-order-1"));
            Assert.Equal("0x00000000000000000000000000000001", HyperliquidWire.ToCloid("0x00000000000000000000000000000001"));
        }

        [Fact]
        public async Task Cancel_sends_asset_and_oid_and_reports_success()
        {
            var h = Armed(exchange: _ => Reply("cancel", "{\"statuses\":[\"success\"]}"));

            bool ok = await Provider(h).CancelOrderAsync("12345", "BTC-PERP");

            Assert.True(ok);
            Assert.Equal("""{"type":"cancel","cancels":[{"a":0,"o":12345}]}""", Single(h)["action"]!.ToString(Newtonsoft.Json.Formatting.None));
        }

        [Fact]
        public async Task Cancel_of_a_missing_order_is_false()
        {
            var h = Armed(exchange: _ => Reply("cancel", "{\"statuses\":[{\"error\":\"Order was never placed, already canceled, or filled.\"}]}"));
            Assert.False(await Provider(h).CancelOrderAsync("12345", "BTC-PERP"));
        }

        [Fact]
        public async Task Cancel_all_cancels_every_open_order_in_one_action()
        {
            var h = Armed(exchange: _ => Reply("cancel", "{\"statuses\":[\"success\",\"success\"]}"));

            int n = await Provider(h).CancelAllAsync();

            Assert.Equal(2, n);
            Assert.Equal("""{"type":"cancel","cancels":[{"a":0,"o":12345},{"a":1,"o":12346}]}""", Single(h)["action"]!.ToString(Newtonsoft.Json.Formatting.None));
        }

        [Fact]
        public async Task Modify_sends_the_oid_and_the_rebuilt_order()
        {
            var h = Armed(exchange: _ => "{\"status\":\"ok\",\"response\":{\"type\":\"order\",\"data\":{\"statuses\":[{\"resting\":{\"oid\":12345}}]}}}");

            string id = await Provider(h).ModifyOrderAsync("12345", new("BTC-PERP", OrderSide.Buy, 0.25, OrderType.Limit, Price: 91000));

            Assert.Equal("12345", id);
            Assert.Equal("""{"type":"modify","oid":12345,"order":{"a":0,"b":true,"p":"91000","s":"0.25","r":false,"t":{"limit":{"tif":"Gtc"}}}}""",
                Single(h)["action"]!.ToString(Newtonsoft.Json.Formatting.None));
        }

        [Fact]
        public async Task Set_leverage_returns_the_applied_value_and_one_on_refusal()
        {
            var ok = Armed(exchange: _ => "{\"status\":\"ok\",\"response\":{\"type\":\"default\"}}");
            Assert.Equal(25.0, await Provider(ok).SetLeverageAsync("ETH-PERP", 40));
            Assert.Equal("""{"type":"updateLeverage","asset":1,"isCross":false,"leverage":25}""", Single(ok)["action"]!.ToString(Newtonsoft.Json.Formatting.None));

            var no = Armed(exchange: _ => "{\"status\":\"err\",\"response\":\"nope\"}");
            Assert.Equal(1.0, await Provider(no).SetLeverageAsync("ETH-PERP", 5));
        }

        [Fact]
        public async Task Schedule_cancel_refuses_a_time_under_five_seconds_ahead_and_sends_nothing()
        {
            var h = Armed();
            string? err = await Provider(h).ScheduleCancelAsync(DateTimeOffset.UtcNow.AddSeconds(1));
            Assert.NotNull(err);
            Assert.Empty(Exchange(h));
        }

        [Fact]
        public async Task Max_leverage_comes_from_meta_per_asset()
        {
            var p = Provider(Armed());
            Assert.Equal(40, await p.GetMaxLeverageAsync("BTC-PERP"));
            Assert.Equal(25, await p.GetMaxLeverageAsync("ETH"));
        }

        // ── 8. Candle paging ─────────────────────────────────────────────────

        [Fact]
        public async Task A_window_larger_than_the_cap_is_fetched_by_paging_backward_from_the_newest()
        {
            const long minute = 60_000;
            long start = 1_700_000_000_000 / minute * minute;
            const int total = 12_000;
            long last = start + (total - 1) * minute;
            var ends = new List<long>();
            var h = Armed(info: body =>
            {
                if (body["type"]!.ToString() != "candleSnapshot") return null;
                var req = body["req"]!;
                long s = req["startTime"]!.Value<long>(), e = req["endTime"]!.Value<long>();
                ends.Add(e);
                // The venue answers an over-cap range with the NEWEST rows (measured live, 2026-11-04).
                var rows = Enumerable.Range(0, total).Select(i => start + i * minute).Where(t => t >= s && t <= e)
                    .OrderByDescending(t => t).Take(5000).OrderBy(t => t)
                    .Select(t => $"{{\"t\":{t},\"T\":{t + minute - 1},\"s\":\"BTC\",\"i\":\"1m\",\"o\":\"1\",\"c\":\"1\",\"h\":\"1\",\"l\":\"1\",\"v\":\"1\",\"n\":1}}");
                return "[" + string.Join(",", rows) + "]";
            });
            var p = Provider(h);

            var (bars, vols) = await p.FetchOhlcvAsync(new("Crypto", "BTC-PERP", "1m", total, Since: start, Until: last));

            Assert.Equal(total, bars.Count);
            Assert.Equal(total, vols.Count);
            Assert.Equal(bars.Select(b => b.Date).Distinct().Count(), bars.Count);
            Assert.True(bars.Zip(bars.Skip(1)).All(x => x.First.Date < x.Second.Date), "bars must be oldest first");
            Assert.Equal(3, ends.Count);                    // 5000 + 5000 + 2000
            Assert.True(ends[1] < ends[0] && ends[2] < ends[1], "each page must end before the previous page began");
        }

        [Fact]
        public async Task A_limit_only_request_stops_paging_once_enough_bars_are_in_hand()
        {
            int calls = 0;
            var h = Armed(info: body =>
            {
                if (body["type"]!.ToString() != "candleSnapshot") return null;
                calls++;
                return Fixture("candleSnapshot_BTC_1d.json");
            });

            var (bars, _) = await Provider(h).FetchOhlcvAsync(new("Crypto", "BTC-PERP", "1d", 3));

            Assert.Equal(1, calls);
            Assert.Equal(3, bars.Count);
            Assert.Equal(78392.0, bars[^1].Close);          // the newest of the fixture's five
        }

        [Fact]
        public async Task Spot_candles_are_requested_by_the_pair_name_not_the_display_name()
        {
            string? coin = null;
            var h = Armed(info: body =>
            {
                if (body["type"]!.ToString() != "candleSnapshot") return null;
                coin = body["req"]!["coin"]!.ToString();
                return "[]";
            });

            await Provider(h).FetchOhlcvAsync(new("Crypto", "HYPE-USDC", "1h", 10));

            Assert.Equal("@107", coin);
        }

        [Fact]
        public async Task A_non_native_timeframe_is_requested_at_the_best_base_interval()
        {
            string? interval = null;
            var h = Armed(info: body =>
            {
                if (body["type"]!.ToString() != "candleSnapshot") return null;
                interval = body["req"]!["interval"]!.ToString();
                return "[]";
            });

            await Provider(h).FetchOhlcvAsync(new("Crypto", "BTC-PERP", "10m", 10));

            Assert.Contains(interval, new HyperliquidProvider().NativelySupportedTimeframes);
        }

        // ── 9. Account reads ─────────────────────────────────────────────────

        [Fact]
        public async Task Positions_and_open_orders_are_read_for_the_main_address_not_the_agent()
        {
            var h = Armed();
            var p = Provider(h);

            var positions = await p.GetPositionsAsync();
            var open = await p.GetOpenOrdersAsync();

            Assert.Equal(2, positions.Count);
            Assert.Equal(2, open.Count);
            var users = h.CapturedBodies.Select(b => JObject.Parse(b)["user"]?.ToString()).Where(u => u != null).Distinct().ToList();
            Assert.Equal(new[] { Main }, users);
            Assert.DoesNotContain(HyperliquidSigner.AddressOf(Key), users, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Open_orders_can_be_filtered_to_one_symbol()
        {
            var open = await Provider(Armed()).GetOpenOrdersAsync("ETH-PERP");
            Assert.Equal("12346", Assert.Single(open).Id);
        }

        [Fact]
        public async Task Validate_checks_the_agent_is_approved_for_the_account()
        {
            string agent = HyperliquidSigner.AddressOf(Key);
            var approved = Armed(info: b => b["type"]!.ToString() == "extraAgents" ? "[{\"address\":\"" + agent + "\",\"name\":\"terminal\",\"validUntil\":1}]" : null);
            var (ok, msg) = await Provider(approved).ValidateApiKeyAsync();
            Assert.True(ok, msg);

            var missing = Armed(info: b => b["type"]!.ToString() == "extraAgents" ? "[]" : null);
            var (ok2, msg2) = await Provider(missing).ValidateApiKeyAsync();
            Assert.False(ok2);
            Assert.Contains("not approved", msg2);
        }

        [Fact]
        public async Task A_non_success_http_reply_throws_with_the_status_and_never_the_key()
        {
            var h = new FakeHttpMessageHandler();
            h.Add(HttpMethod.Post, @"hyperliquid\.xyz/info", "rate limited", HttpStatusCode.TooManyRequests);
            var p = Provider(h);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => p.GetPositionsAsync());

            Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
            Assert.DoesNotContain(Key, ex.Message);
            Assert.DoesNotContain("0123456789012345", ex.Message);
        }
    }
}
