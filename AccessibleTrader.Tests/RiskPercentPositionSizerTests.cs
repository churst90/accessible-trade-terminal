using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Direct unit tests for <see cref="RiskPercentPositionSizer"/> — the fixed
    /// fractional risk sizer. Money math: size = (balance × riskPct/100) / |entry − stop|,
    /// with a limit-price fallback when no usable stop distance exists, and the
    /// signal's own Quantity (or 1.0) as the last resort.
    /// </summary>
    public class RiskPercentPositionSizerTests
    {
        private const double Balance = 10_000.0;

        // The sizer never reads metrics; any instance satisfies the signature.
        private static readonly StrategyMetrics NoMetrics = new(0, 0, 0, 0, 0, 0);

        private static StrategySignal Signal(double? quantity = null, double? limit = null, double? stop = null) =>
            new(OrderSide.Buy, OrderType.Limit, Quantity: quantity, LimitPrice: limit,
                StopLoss: stop, TakeProfit: null, Rationale: "test", Confidence: 1.0);

        [Fact]
        public void Sizes_from_stop_distance_when_stop_and_limit_present()
        {
            // 1% of 10,000 = $100 at risk; entry 100, stop 95 → $5 risk/unit → 20 units.
            var sizer = new RiskPercentPositionSizer(riskPercent: 1.0);

            double qty = sizer.CalculateSize(Signal(limit: 100, stop: 95), Balance, NoMetrics);

            Assert.Equal(20.0, qty, 6);
        }

        [Fact]
        public void Zero_stop_distance_falls_through_to_limit_fallback_without_dividing_by_zero()
        {
            // limit == stop → risk distance 0. The guard (risk > 0) must skip the
            // stop-based branch and use the price fallback: $100 / $100 = 1 unit —
            // never Infinity/NaN from a divide-by-zero.
            var sizer = new RiskPercentPositionSizer(riskPercent: 1.0);

            double qty = sizer.CalculateSize(Signal(limit: 100, stop: 100), Balance, NoMetrics);

            Assert.Equal(1.0, qty, 6);
            Assert.True(double.IsFinite(qty));
        }

        [Fact]
        public void No_limit_price_falls_back_to_signal_quantity_then_one()
        {
            // Without any price reference the sizer cannot compute risk — it must
            // defer to the signal's own quantity, and 1.0 when even that is absent.
            var sizer = new RiskPercentPositionSizer(riskPercent: 1.0);

            Assert.Equal(3.0, sizer.CalculateSize(Signal(quantity: 3.0), Balance, NoMetrics), 6);
            Assert.Equal(1.0, sizer.CalculateSize(Signal(quantity: null), Balance, NoMetrics), 6);
        }

        [Fact]
        public void RiskPercent_below_floor_is_clamped_up_to_001_percent()
        {
            // Constructor clamps to [0.01, 50]. 0.0001% would size essentially zero;
            // the floor keeps a degenerate config from producing dust orders.
            var sizer = new RiskPercentPositionSizer(riskPercent: 0.0001);

            // Clamped 0.01% of 10,000 = $1 at risk; $5 risk/unit → 0.2 units.
            double qty = sizer.CalculateSize(Signal(limit: 100, stop: 95), Balance, NoMetrics);

            Assert.Equal(0.2, qty, 6);
        }

        [Fact]
        public void RiskPercent_above_ceiling_is_clamped_down_to_50_percent()
        {
            // 500% per trade is a config error, not a strategy: clamp to 50%.
            var sizer = new RiskPercentPositionSizer(riskPercent: 500.0);

            // Clamped 50% of 10,000 = $5,000 at risk; $5 risk/unit → 1,000 units.
            double qty = sizer.CalculateSize(Signal(limit: 100, stop: 95), Balance, NoMetrics);

            Assert.Equal(1000.0, qty, 6);
        }
    }
}
