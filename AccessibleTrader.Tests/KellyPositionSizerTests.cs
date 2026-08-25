using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins for the 2026-06-12 KellyPositionSizer repair. The old implementation
    /// fabricated avgLoss = $1 when the strategy was net-profitable, and its
    /// clamp floor (applied BEFORE half-Kelly) forced negative-edge strategies
    /// up to a positive position size. These tests guard the corrected math:
    /// f* = (p·b − q)/b from real gross profit/loss, halved, clamped to
    /// [0, 0.125], with negative edge falling back to the 1% minimum.
    /// </summary>
    public class KellyPositionSizerTests
    {
        private const double Balance = 10_000.0;

        private static StrategySignal Signal(double? limit = 100.0, double? stop = null) =>
            new(OrderSide.Buy, OrderType.Limit, Quantity: null, LimitPrice: limit,
                StopLoss: stop, TakeProfit: null, Rationale: "test", Confidence: 1.0);

        private static StrategyMetrics Metrics(
            int total = 100, int wins = 60,
            double grossProfit = 6000, double grossLoss = 2000) =>
            new(TotalSignals: total, WinningTrades: wins,
                WinRate: (double)wins / total, MaxDrawdown: 0.1,
                TotalPnL: grossProfit - grossLoss, SharpeRatio: 1.0,
                GrossProfit: grossProfit, GrossLoss: grossLoss);

        [Fact]
        public void PositiveEdge_ComputesHalfKellyFromGrossFigures()
        {
            // p=0.6, avgWin=100, avgLoss=50 → b=2 → f* = (0.6*2 − 0.4)/2 = 0.4
            // half-Kelly = 0.2, clamped to 0.125 → allocation $1250 at price 100 → 12.5
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(Signal(), Balance, Metrics());

            Assert.Equal(12.5, qty, 3);
        }

        [Fact]
        public void ModestEdge_IsNotForcedUpByAnyFloor()
        {
            // p=0.5, avgWin=60 (3000/50), avgLoss=50 (2500/50) → b=1.2
            // f* = (0.5*1.2 − 0.5)/1.2 = 0.0833…, half = 0.04167 — well under the
            // old 0.02-after-halving floor's reach but must NOT be pushed up.
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(
                Signal(), Balance, Metrics(total: 100, wins: 50, grossProfit: 3000, grossLoss: 2500));

            double expectedFraction = ((0.5 * 1.2 - 0.5) / 1.2) / 2.0;
            Assert.Equal(Balance * expectedFraction / 100.0, qty, 3);
        }

        [Fact]
        public void NegativeEdge_FallsBackToMinimumSize_NotForcedPositive()
        {
            // p=0.3, avgWin=50, avgLoss=100 → b=0.5 → f* = (0.15 − 0.7)/0.5 < 0.
            // Old code clamped this UP to 2% then halved → 1% of balance as Kelly;
            // new code routes to the explicit 1% fallback — and crucially never
            // sizes ABOVE that on a losing strategy.
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(
                Signal(), Balance, Metrics(total: 100, wins: 30, grossProfit: 1500, grossLoss: 7000));

            Assert.Equal(Balance * 0.01 / 100.0, qty, 6); // 1% fallback
        }

        [Fact]
        public void MissingGrossFigures_FallsBackTo1Percent()
        {
            // Metrics from an older producer (6-arg constructor): gross fields zero.
            var legacy = new StrategyMetrics(100, 60, 0.6, 0.1, 4000, 1.0);
            var sizer = new KellyPositionSizer();

            double qty = sizer.CalculateSize(Signal(), Balance, legacy);

            Assert.Equal(Balance * 0.01 / 100.0, qty, 6);
        }

        [Fact]
        public void InsufficientHistory_FallsBackTo1Percent()
        {
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(Signal(), Balance, Metrics(total: 5, wins: 3));

            Assert.Equal(Balance * 0.01 / 100.0, qty, 6);
        }

        [Fact]
        public void FractionIsCappedAtEighthOfBalance()
        {
            // Absurdly good metrics: p=0.9, b=9 → f* ≈ 0.889, half ≈ 0.444 → cap 0.125.
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(
                Signal(), Balance, Metrics(total: 100, wins: 90, grossProfit: 9000, grossLoss: 100));

            Assert.Equal(Balance * 0.125 / 100.0, qty, 3);
        }

        [Fact]
        public void WithStop_SizesOnRiskPerUnit_CappedAtFullNotional()
        {
            // Entry 100, stop 99 → risk/unit 1. Allocation 1250 → raw qty 1250,
            // but notional cap = balance/entry = 100 units.
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(Signal(stop: 99.0), Balance, Metrics());

            Assert.Equal(100.0, qty, 3);
        }

        [Fact]
        public void WithWideStop_RiskSizingApplies()
        {
            // Entry 100, stop 50 → risk/unit 50. Allocation 1250 → qty 25
            // (notional 2500 < balance, no cap).
            var sizer = new KellyPositionSizer();
            double qty = sizer.CalculateSize(Signal(stop: 50.0), Balance, Metrics());

            Assert.Equal(25.0, qty, 3);
        }
    }
}
