using AccessibleTrader.Plugins.Hyperliquid;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Hyperliquid's live order stream. The frames are RECORDED: 75 seconds of a busy public
    /// market-making account on 2026-11-26, read without a key (orderUpdates, userFills and a
    /// candle feed on one socket). Each sequence is one order's frames in the order they came.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class HyperliquidOrderStreamTests
    {
        private static readonly JObject Frames = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hyperliquid", "recorded_ws_frames.json")));

        private sealed class Rig
        {
            public readonly List<OrderUpdate> Emitted = new();
            public readonly List<long> TypeLookups = new(), FillLookups = new();
            public string? Type = "Limit";
            public (double, double) Rest = (0, 0);
            public readonly HyperliquidOrderStream Stream;

            public Rig(int waitMs = 2000) =>
                Stream = new HyperliquidOrderStream(c => c + "-PERP",
                    oid => { TypeLookups.Add(oid); return Task.FromResult<string?>(Type); },
                    oid => { FillLookups.Add(oid); return Task.FromResult(Rest); },
                    u => { lock (Emitted) Emitted.Add(u); }, TimeSpan.FromMilliseconds(waitMs));

            public async Task Feed(string sequence)
            {
                foreach (var frame in (JArray)Frames[sequence]!)
                    await Stream.OnMessageAsync(frame.ToString());
            }
        }

        [Fact]
        public async Task An_order_filled_whole_is_one_fill_at_the_fills_price_with_no_partial_first()
        {
            var rig = new Rig();

            await rig.Feed("filled_whole");

            Assert.Equal(new[] { OrderStatus.New, OrderStatus.Filled }, rig.Emitted.Select(u => u.Status));
            var fill = rig.Emitted[1];
            Assert.Equal("617301465688", fill.OrderId);
            Assert.Equal("BTC-PERP", fill.Symbol);
            Assert.Equal(OrderSide.Sell, fill.Side);
            Assert.Equal(0.0003, fill.FilledQuantity);
            Assert.Equal(60768.0, fill.FilledPrice);
            Assert.Equal(0.0, fill.RemainingQuantity);
            Assert.Equal(0.13254, fill.RealizedPnL);
            Assert.False(fill.StopTriggered || fill.TakeProfitTriggered);
            Assert.Empty(rig.FillLookups);                       // the stream had the price
        }

        [Fact]
        public async Task Partial_fills_count_up_and_the_cancel_carries_what_filled()
        {
            var rig = new Rig();

            await rig.Feed("partial_then_canceled");

            var partials = rig.Emitted.Where(u => u.Status == OrderStatus.PartialFill).ToList();
            Assert.Equal(3, partials.Count);
            Assert.Equal(0.001, partials[0].FilledQuantity, 12);
            Assert.Equal(0.00896, partials[1].FilledQuantity);
            Assert.Equal(0.00916, partials[2].FilledQuantity);
            Assert.Equal(0.10884, partials[2].RemainingQuantity);
            Assert.Equal(3.166488, partials[1].RealizedPnL);   // that fill's own realized P&L
            var cancel = rig.Emitted.Last();
            Assert.Equal(OrderStatus.Cancelled, cancel.Status);
            Assert.Equal(0.00916, cancel.FilledQuantity, 12);
            Assert.Equal(60724.0, cancel.FilledPrice, 6);   // an average of three fills at one price
            Assert.Equal(0.10884, cancel.RemainingQuantity);
            Assert.Null(cancel.Reason);                          // a plain cancel: somebody asked
        }

        [Fact]
        public async Task A_fill_before_the_order_opened_is_not_called_partial_and_the_cancel_has_it()
        {
            var rig = new Rig();

            await rig.Feed("fill_before_open");

            Assert.DoesNotContain(rig.Emitted, u => u.Status == OrderStatus.PartialFill);
            var cancel = rig.Emitted.Last();
            Assert.Equal(OrderStatus.Cancelled, cancel.Status);
            Assert.Equal(0.00045, cancel.FilledQuantity);        // exactly: 0.0541 - 0.05365 is not, in doubles
            Assert.Equal(60748.0, cancel.FilledPrice);
        }

        [Fact]
        public async Task The_history_that_opens_the_subscription_is_not_announced()
        {
            var rig = new Rig();
            var snapshot = (JObject)Frames["snapshot"]!;
            Assert.Equal(2, ((JArray)snapshot["data"]!["fills"]!).Count);   // vacuity: it has fills

            await rig.Stream.OnMessageAsync(snapshot.ToString());
            // An order seen opening, then a snapshot fill for it: still nothing.
            long oid = snapshot["data"]!["fills"]![0]!["oid"]!.Value<long>();
            await rig.Stream.OnMessageAsync("{\"channel\":\"orderUpdates\",\"data\":[{\"order\":{\"coin\":\"BTC\",\"side\":\"A\",\"limitPx\":\"1\",\"sz\":\"9\",\"oid\":"
                + oid + ",\"timestamp\":1,\"origSz\":\"9\"},\"status\":\"open\",\"statusTimestamp\":1}]}");
            await rig.Stream.OnMessageAsync(snapshot.ToString());

            Assert.Equal(new[] { OrderStatus.New }, rig.Emitted.Select(u => u.Status));
        }

        [Fact]
        public async Task A_post_only_order_that_would_have_crossed_is_refused_in_words()
        {
            var rig = new Rig();

            await rig.Stream.OnMessageAsync(Frames["rejected"]!.ToString());

            var r = Assert.Single(rig.Emitted);
            Assert.Equal(OrderStatus.Rejected, r.Status);
            Assert.Equal("617301087359", r.OrderId);
            Assert.Equal("it is post-only and would have traded at once", r.Reason);
            Assert.Empty(rig.TypeLookups);                       // nothing filled: nothing to look up
        }

        [Fact]
        public async Task A_filled_status_that_comes_before_its_fill_waits_for_it()
        {
            var rig = new Rig();
            var seq = (JArray)Frames["filled_whole"]!;           // open, fill, filled

            await rig.Stream.OnMessageAsync(seq[0].ToString());
            var waiting = rig.Stream.OnMessageAsync(seq[2].ToString());
            await Task.Delay(100);
            Assert.False(waiting.IsCompleted);                   // held for the fill
            await rig.Stream.OnMessageAsync(seq[1].ToString());
            await waiting;

            var fill = rig.Emitted.Last();
            Assert.Equal(OrderStatus.Filled, fill.Status);
            Assert.Equal(60768.0, fill.FilledPrice);
            Assert.Empty(rig.FillLookups);
        }

        [Fact]
        public async Task A_fill_the_stream_never_saw_takes_its_price_from_the_fill_history()
        {
            var rig = new Rig(waitMs: 50) { Rest = (0.0003, 60770.5) };
            var seq = (JArray)Frames["filled_whole"]!;

            await rig.Stream.OnMessageAsync(seq[0].ToString());
            await rig.Stream.OnMessageAsync(seq[2].ToString());  // its fill frame was lost

            var fill = rig.Emitted.Last();
            Assert.Equal(OrderStatus.Filled, fill.Status);
            Assert.Equal(60770.5, fill.FilledPrice);
            Assert.Equal(new[] { 617301465688L }, rig.FillLookups);
        }

        [Theory]
        [InlineData("Stop Market", true, false)]
        [InlineData("Stop Limit", true, false)]
        [InlineData("Take Profit Market", false, true)]
        [InlineData("Limit", false, false)]
        [InlineData(null, false, false)]
        public async Task A_filled_stop_or_target_says_which_from_the_orders_type(string? type, bool stop, bool target)
        {
            var rig = new Rig { Type = type };

            await rig.Feed("filled_whole");

            var fill = rig.Emitted.Last();
            Assert.Equal(stop, fill.StopTriggered);
            Assert.Equal(target, fill.TakeProfitTriggered);
        }

        [Fact]
        public async Task Streaming_is_claimed_only_once_all_three_subscriptions_are_confirmed_and_not_after_a_reset()
        {
            var rig = new Rig();
            var subs = (JArray)Frames["subscriptionResponses"]!;
            Assert.Equal(2, subs.Count);
            var twaps = Twap["ws_subscriptionResponse"]!.ToString();

            Assert.False(rig.Stream.Subscribed);
            await rig.Stream.OnMessageAsync(subs[0].ToString());
            Assert.False(rig.Stream.Subscribed);
            await rig.Stream.OnMessageAsync(subs[1].ToString());
            Assert.False(rig.Stream.Subscribed);              // orders and fills, but a TWAP would go unheard
            await rig.Stream.OnMessageAsync(twaps);
            Assert.True(rig.Stream.Subscribed);
            rig.Stream.Reset();
            Assert.False(rig.Stream.Subscribed);
        }

        // ── TWAPs (recorded 2026-11-26, Fixtures/Hyperliquid/recorded_twap.json) ──

        private static readonly JObject Twap = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Hyperliquid", "recorded_twap.json")));

        private static JToken Entry(long id, string status) =>
            ((JArray)Twap["twapHistory"]!).First(e => e["twapId"]!.Value<long>() == id && e["status"]!["status"]!.ToString() == status);

        [Fact]
        public async Task The_first_TWAP_frame_is_the_whole_history_and_says_nothing()
        {
            var rig = new Rig();

            await rig.Stream.OnMessageAsync(Twap["ws_history_snapshot"]!.ToString());

            Assert.Empty(rig.Emitted);
            // Vacuity: the same entries, not marked as the snapshot, are heard.
            var live = (JObject)Twap["ws_history_snapshot"]!.DeepClone();
            ((JObject)live["data"]!).Remove("isSnapshot");
            await rig.Stream.OnMessageAsync(live.ToString());
            Assert.Equal(3, rig.Emitted.Count);
            Assert.All(rig.Emitted, u => Assert.StartsWith("twap:", u.OrderId));
        }

        [Theory]
        [InlineData(2851918, "activated", OrderStatus.New, 0.0, 1707.8203, null)]
        [InlineData(2732029, "waitingForTrigger", OrderStatus.New, 0.0, 62667.62, null)]
        [InlineData(2827524, "finished", OrderStatus.Filled, 64445.81, 0.0, null)]
        [InlineData(2833341, "terminated", OrderStatus.Cancelled, 15654.3, 63921.78, null)]
        [InlineData(2815388, "error", OrderStatus.Cancelled, 1030.01, 3733.0, "Insufficient margin to place order.")]
        public void Each_recorded_TWAP_entry_is_an_order_update(long id, string status, OrderStatus state, double done, double left, string? reason)
        {
            var u = HyperliquidOrderStream.TwapUpdate(Entry(id, status), c => c + "-PERP")!;

            Assert.Equal("twap:" + id, u.OrderId);
            Assert.Equal(state, u.Status);
            Assert.Equal(done, u.FilledQuantity, 6);
            Assert.Equal(left, u.RemainingQuantity, 6);
            Assert.Equal(reason, u.Reason);
        }

        [Fact]
        public void A_TWAP_that_errored_with_nothing_traded_is_rejected_and_the_rest_is_read_right()
        {
            var error = Entry(2815388, "error").DeepClone();
            error["state"]!["executedSz"] = "0.0";
            Assert.Equal(OrderStatus.Rejected, HyperliquidOrderStream.TwapUpdate(error, c => c)!.Status);

            var done = HyperliquidOrderStream.TwapUpdate(Entry(2827524, "finished"), c => c + "-PERP")!;
            Assert.Equal("xyz:SPCX-PERP", done.Symbol);
            Assert.Equal(OrderSide.Sell, done.Side);
            Assert.Equal(125.170054, done.FilledPrice, 6);
            Assert.Equal(new DateTime(2026, 11, 25, 1, 8, 6, DateTimeKind.Utc), done.Timestamp);

            var odd = Entry(2827524, "finished").DeepClone();
            odd["status"]!["status"] = "paused";
            Assert.Null(HyperliquidOrderStream.TwapUpdate(odd, c => c));      // a word not known is not guessed at
        }

        [Theory]
        [InlineData("marginCanceled", OrderStatus.Cancelled, "not enough margin")]
        [InlineData("siblingFilledCanceled", OrderStatus.Cancelled, "the other leg of its stop and target filled")]
        [InlineData("iocCancelRejected", OrderStatus.Rejected, "it is immediate-or-cancel and nothing on the book matched it")]
        [InlineData("someNewRejected", OrderStatus.Rejected, "Hyperliquid said someNewRejected")]
        [InlineData("somethingElse", OrderStatus.Unknown, "somethingElse")]
        public void Status_words_map_to_a_state_and_a_reason(string word, OrderStatus status, string reason)
        {
            var (s, r) = HyperliquidOrderStream.Map(word);
            Assert.Equal(status, s);
            Assert.Equal(reason, r);
        }

        [Fact]
        public void The_provider_has_a_stream_but_claims_none_before_its_socket_is_up()
        {
            var p = new HyperliquidProvider();
            Assert.True(((ITradingProvider)p).ProvidesOrderStream);
            Assert.False(p.SupportsOrderEventStreaming);
        }

        [Fact]
        public void Subscribe_messages_name_the_account_not_a_key()
        {
            Assert.Equal("{\"method\":\"subscribe\",\"subscription\":{\"type\":\"userFills\",\"user\":\"0xabc\"}}",
                HyperliquidOrderStream.SubscribeMessage("userFills", "0xabc"));
        }
    }
}
