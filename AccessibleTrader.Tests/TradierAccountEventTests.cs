using System;
using System.Collections.Generic;
using AccessibleTrader.Plugins.Tradier;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the Tradier account-events → OrderUpdate mapping. The wire format is
    /// Tradier's documented order event (event/status/avg_fill_price/
    /// last_fill_quantity/…); the mapping decides what the user HEARS, so each
    /// status → announcement pairing is pinned, as is the silence on
    /// non-announceable statuses (open/pending) and on malformed payloads.
    /// </summary>
    public class TradierAccountEventTests
    {
        private static (TradierProvider provider, List<OrderUpdate> seen) Build()
        {
            var provider = new TradierProvider();
            var seen = new List<OrderUpdate>();
            provider.OrderUpdateStream.Subscribe(seen.Add);
            return (provider, seen);
        }

        [Fact]
        public void Filled_order_maps_to_Filled_with_price_and_quantity()
        {
            var (provider, seen) = Build();

            provider.HandleAccountEvent("""
                {"event":"order","id":229063,"symbol":"AAPL","side":"buy","type":"limit",
                 "status":"filled","avg_fill_price":231.55,"executed_quantity":10,
                 "last_fill_quantity":10,"remaining_quantity":0}
                """);

            var u = Assert.Single(seen);
            Assert.Equal("229063", u.OrderId);
            Assert.Equal("AAPL", u.Symbol);
            Assert.Equal(OrderSide.Buy, u.Side);
            Assert.Equal(OrderStatus.Filled, u.Status);
            Assert.Equal(231.55, u.FilledPrice);
            Assert.Equal(10, u.FilledQuantity);
            Assert.False(u.StopTriggered);
        }

        [Fact]
        public void Filled_stop_order_announces_as_stop_hit()
        {
            // No trigger flags on the wire — the order TYPE decides the wording.
            var (provider, seen) = Build();

            provider.HandleAccountEvent("""
                {"event":"order","id":1,"symbol":"AAPL","side":"sell","type":"stop",
                 "status":"filled","avg_fill_price":220.0,"executed_quantity":10,
                 "remaining_quantity":0}
                """);

            Assert.True(Assert.Single(seen).StopTriggered);
        }

        [Fact]
        public void Partial_fill_uses_the_last_fill_quantity()
        {
            var (provider, seen) = Build();

            provider.HandleAccountEvent("""
                {"event":"order","id":2,"symbol":"MSFT","side":"buy","type":"limit",
                 "status":"partially_filled","avg_fill_price":415.0,
                 "executed_quantity":7,"last_fill_quantity":3,"remaining_quantity":3}
                """);

            var u = Assert.Single(seen);
            Assert.Equal(OrderStatus.PartialFill, u.Status);
            Assert.Equal(3, u.FilledQuantity);   // this fill, not the running total
            Assert.Equal(3, u.RemainingQuantity);
        }

        [Theory]
        [InlineData("canceled", OrderStatus.Cancelled)]
        [InlineData("expired", OrderStatus.Cancelled)]
        [InlineData("rejected", OrderStatus.Rejected)]
        public void Terminal_statuses_map_to_their_order_status(string wire, OrderStatus expected)
        {
            var (provider, seen) = Build();

            provider.HandleAccountEvent(
                $$"""{"event":"order","id":3,"symbol":"AAPL","side":"buy","type":"limit","status":"{{wire}}"}""");

            Assert.Equal(expected, Assert.Single(seen).Status);
        }

        [Fact]
        public void Short_sale_sides_map_by_prefix()
        {
            // Tradier equity sides include sell_short and buy_to_cover.
            var (provider, seen) = Build();

            provider.HandleAccountEvent("""
                {"event":"order","id":4,"symbol":"AAPL","side":"sell_short","type":"limit",
                 "status":"filled","avg_fill_price":230.0,"executed_quantity":5,"remaining_quantity":0}
                """);
            provider.HandleAccountEvent("""
                {"event":"order","id":5,"symbol":"AAPL","side":"buy_to_cover","type":"limit",
                 "status":"filled","avg_fill_price":225.0,"executed_quantity":5,"remaining_quantity":0}
                """);

            Assert.Equal(OrderSide.Sell, seen[0].Side);
            Assert.Equal(OrderSide.Buy, seen[1].Side);
        }

        [Theory]
        [InlineData("""{"event":"order","id":6,"symbol":"AAPL","side":"buy","type":"limit","status":"open"}""")]
        [InlineData("""{"event":"order","id":7,"symbol":"AAPL","side":"buy","type":"limit","status":"pending"}""")]
        [InlineData("""{"event":"heartbeat"}""")]
        [InlineData("""not json at all""")]
        public void NonAnnounceable_and_malformed_payloads_stay_silent(string wire)
        {
            var (provider, seen) = Build();

            provider.HandleAccountEvent(wire);

            Assert.Empty(seen);
        }

        [Fact]
        public void Streaming_capability_is_false_until_the_socket_is_actually_up()
        {
            // The dynamic flag is the polling fallback's gate: a fresh provider
            // (no websocket yet) must report false so orders placed before the
            // stream connects still get watched by polling.
            var (provider, _) = Build();
            Assert.False(((ITradingProvider)provider).SupportsOrderEventStreaming);
        }
    }
}
