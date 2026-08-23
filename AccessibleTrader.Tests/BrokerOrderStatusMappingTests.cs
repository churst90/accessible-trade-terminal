using AccessibleTrader.Plugins.Schwab;
using AccessibleTrader.Plugins.Tradier;
using AccessibleTrader.Sdk.Plugins;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the broker order-by-id → <see cref="OrderStatusSnapshot"/> mapping used
    /// by the order-service poller. This is the authoritative resolution that
    /// replaced the fills-match heuristic for Tradier/Schwab (whose fills don't
    /// carry the placed order id), so a filled order is no longer mis-announced as
    /// cancelled. The mapping decides what the user HEARS.
    /// </summary>
    public class BrokerOrderStatusMappingTests
    {
        // ── Tradier ──────────────────────────────────────────────────────────

        [Theory]
        [InlineData("filled",           PolledOrderState.Filled)]
        [InlineData("partially_filled", PolledOrderState.PartiallyFilled)]
        [InlineData("canceled",         PolledOrderState.Cancelled)]
        [InlineData("cancelled",        PolledOrderState.Cancelled)]
        // Expired is its own terminal state — a day order at the close is not a
        // cancel (nobody asked) and not a rejection (the venue accepted it).
        [InlineData("expired",          PolledOrderState.Expired)]
        [InlineData("rejected",         PolledOrderState.Rejected)]
        [InlineData("error",            PolledOrderState.Rejected)]
        [InlineData("open",             PolledOrderState.Working)]
        [InlineData("pending",          PolledOrderState.Working)]
        public void Tradier_status_maps_to_state(string wire, PolledOrderState expected)
        {
            var order = JObject.Parse($$"""
                {"id":1,"symbol":"AAPL","side":"buy","type":"limit","status":"{{wire}}",
                 "avg_fill_price":231.5,"exec_quantity":10,"remaining_quantity":0}
                """);
            Assert.Equal(expected, TradierProvider.MapOrderToSnapshot(order).State);
        }

        [Fact]
        public void Tradier_filled_carries_side_qty_price()
        {
            var order = JObject.Parse("""
                {"id":1,"symbol":"AAPL","side":"sell","type":"limit","status":"filled",
                 "avg_fill_price":231.55,"exec_quantity":10,"remaining_quantity":0}
                """);
            var s = TradierProvider.MapOrderToSnapshot(order);
            Assert.Equal(OrderSide.Sell, s.Side);
            Assert.Equal("AAPL", s.Symbol);
            Assert.Equal(10, s.FilledQuantity);
            Assert.Equal(231.55, s.FilledPrice);
        }

        [Fact]
        public void Tradier_reads_executed_quantity_fallback_field()
        {
            // The account-event endpoint spells it executed_quantity, not exec_quantity.
            var order = JObject.Parse("""
                {"id":1,"symbol":"AAPL","side":"buy","status":"filled",
                 "avg_fill_price":100,"executed_quantity":7,"remaining_quantity":0}
                """);
            Assert.Equal(7, TradierProvider.MapOrderToSnapshot(order).FilledQuantity);
        }

        [Fact]
        public void Tradier_filled_stop_sets_StopTriggered()
        {
            var order = JObject.Parse("""
                {"id":1,"symbol":"AAPL","side":"sell","type":"stop","status":"filled",
                 "avg_fill_price":220,"exec_quantity":10,"remaining_quantity":0}
                """);
            Assert.True(TradierProvider.MapOrderToSnapshot(order).StopTriggered);
        }

        // ── Schwab ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("FILLED",   PolledOrderState.Filled)]
        [InlineData("CANCELED", PolledOrderState.Cancelled)]
        [InlineData("EXPIRED",  PolledOrderState.Expired)]
        // REPLACED means the order is STILL LIVE under a new id. The old
        // REPLACED→Cancelled squash told the trader they were flat; they
        // re-entered and were double-sized with the original still resting.
        [InlineData("REPLACED", PolledOrderState.Replaced)]
        [InlineData("REJECTED", PolledOrderState.Rejected)]
        [InlineData("WORKING",  PolledOrderState.Working)]
        [InlineData("QUEUED",   PolledOrderState.Working)]
        public void Schwab_status_maps_to_state(string wire, PolledOrderState expected)
        {
            var order = JObject.Parse(
                "{\"status\":\"" + wire + "\",\"filledQuantity\":0,\"remainingQuantity\":10," +
                "\"orderLegCollection\":[{\"instruction\":\"BUY\",\"instrument\":{\"symbol\":\"AAPL\"}}]}");
            Assert.Equal(expected, SchwabProvider.MapOrderToSnapshot(order).State);
        }

        [Fact]
        public void Schwab_filled_computes_weighted_avg_price_and_side()
        {
            // Two partial executions at 100/10 and 102/10 → weighted avg 101.
            var order = JObject.Parse("""
                {"status":"FILLED","filledQuantity":20,"remainingQuantity":0,
                 "orderLegCollection":[{"instruction":"SELL_SHORT","instrument":{"symbol":"TSLA"}}],
                 "orderActivityCollection":[
                   {"executionLegs":[{"price":100,"quantity":10}]},
                   {"executionLegs":[{"price":102,"quantity":10}]}]}
                """);
            var s = SchwabProvider.MapOrderToSnapshot(order);
            Assert.Equal(OrderSide.Sell, s.Side);       // SELL_SHORT → Sell
            Assert.Equal("TSLA", s.Symbol);
            Assert.Equal(20, s.FilledQuantity);
            Assert.Equal(101, s.FilledPrice);
        }

        [Fact]
        public void Schwab_working_with_partial_fill_reports_PartiallyFilled()
        {
            var order = JObject.Parse("""
                {"status":"WORKING","filledQuantity":3,"remainingQuantity":7,
                 "orderLegCollection":[{"instruction":"BUY","instrument":{"symbol":"AAPL"}}]}
                """);
            Assert.Equal(PolledOrderState.PartiallyFilled,
                SchwabProvider.MapOrderToSnapshot(order).State);
        }
    }
}
