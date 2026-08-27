using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Core.Strategies.BuiltIn;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The backtester's cost model and trade accounting, and the live metrics that feed the
    /// position sizers.
    ///
    /// <para>All four defects here share a shape: they made the numbers <b>flattering</b>, and
    /// they did it in the part of the system a user reads to decide whether to trade something
    /// with real money.</para>
    /// </summary>
    public class BacktestCostAndPositionTests
    {
        // ── Per-position aggregation ─────────────────────────────────────────

        private static BacktestTrade Row(double pnl, int positionId) =>
            new(EntryTime: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EntryPrice: 100, Side: OrderSide.Buy, Quantity: 1,
                ExitTime: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                ExitPrice: 100 + pnl, PnL: pnl, ExitReason: "test",
                PositionId: positionId);

        [Fact]
        public void A_three_rung_ladder_is_one_position_not_three_trades()
        {
            // A ladder that fills all three rungs used to report three wins from one entry.
            var rows = new[] { Row(10, 1), Row(10, 1), Row(10, 1) };

            var positions = StrategyBacktester.PositionPnLs(rows);

            Assert.Single(positions);
            Assert.Equal(30, positions[0], 6);
        }

        [Fact]
        public void A_ladder_that_takes_TP1_then_stops_out_is_one_net_result()
        {
            // The audit's example: TP1 fills for +30, the rest stops out at breakeven for −10.
            // Counted per row that is 1 win / 1 loss = 50% win rate; it is one small net WIN.
            var rows = new[] { Row(30, 1), Row(-10, 1) };

            var positions = StrategyBacktester.PositionPnLs(rows);

            Assert.Single(positions);
            Assert.Equal(20, positions[0], 6);
            Assert.True(positions[0] > 0, "the position was profitable and must count as a win");
        }

        [Fact]
        public void Separate_positions_stay_separate()
        {
            var rows = new[] { Row(10, 1), Row(10, 1), Row(-5, 2) };

            var positions = StrategyBacktester.PositionPnLs(rows).OrderBy(p => p).ToList();

            Assert.Equal(2, positions.Count);
            Assert.Equal(-5, positions[0], 6);
            Assert.Equal(20, positions[1], 6);
        }

        [Fact]
        public void Unattributed_rows_are_scored_exactly_as_they_were_before()
        {
            // A hand-built row, or a result deserialised from before PositionId existed,
            // carries 0. Folding those into one position would score an old result as a
            // single enormous trade — worse than the defect being fixed.
            var rows = new[] { Row(10, 0), Row(-4, 0), Row(7, 0) };

            var positions = StrategyBacktester.PositionPnLs(rows);

            Assert.Equal(3, positions.Count);
        }

        // ── BaseStrategy position accounting ─────────────────────────────────

        private sealed class NeverSignals : BaseStrategy
        {
            public override string Id => "never";
            public override string Name => "never";
            public override string Description => "never signals";
            public override StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public override IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            protected override StrategySignal? ComputeSignal(
                Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) => null;
        }

        private static OrderUpdate Fill(OrderSide side, double qty, double price) =>
            new(OrderId: Guid.NewGuid().ToString(),
                Symbol: "BTCUSDT",
                Side: side,
                FilledQuantity: qty,
                FilledPrice: price,
                RemainingQuantity: 0,
                Status: OrderStatus.Filled,
                StopTriggered: false,
                TakeProfitTriggered: false,
                Timestamp: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        [Fact]
        public void A_partially_filled_entry_is_not_a_completed_round_trip()
        {
            // The filed defect: a partial fill booked a near-zero-PnL "closed trade" against
            // itself and cleared the position; the remainder then re-opened at the same price.
            //
            // The exit fill is the part that makes this visible, and the first draft of this
            // test did not have one — asserting only that GrossProfit and GrossLoss were zero
            // after two partials, which is ALSO true of the defect (a zero-PnL close is still
            // zero). Sabotage caught it. What actually moves is the win rate: the phantom
            // close counts as a completed trade that was not a win, and the real exit then
            // finds no position to close.
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 0.5, 100));
            s.OnOrderFilled(Fill(OrderSide.Buy, 0.5, 100));
            s.OnOrderFilled(Fill(OrderSide.Sell, 1.0, 110));

            var m = s.GetMetrics();
            Assert.Equal(10, m.GrossProfit, 6);
            Assert.Equal(0, m.GrossLoss, 6);
            Assert.Equal(1.0, m.WinRate, 6);
        }

        [Fact]
        public void Scaling_in_averages_the_entry_rather_than_booking_a_trade()
        {
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 1, 100));
            s.OnOrderFilled(Fill(OrderSide.Buy, 1, 200));   // average entry 150
            s.OnOrderFilled(Fill(OrderSide.Sell, 2, 160));  // +10 a unit on 2 units

            var m = s.GetMetrics();
            Assert.Equal(20, m.GrossProfit, 6);
            Assert.Equal(0, m.GrossLoss, 6);
        }

        [Fact]
        public void A_three_rung_exit_ladder_does_not_open_a_phantom_position()
        {
            // close / open / close was the old sequence — the middle rung opening a position
            // in the EXIT direction, which then booked against the third rung.
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 3, 100));
            s.OnOrderFilled(Fill(OrderSide.Sell, 1, 110));
            s.OnOrderFilled(Fill(OrderSide.Sell, 1, 120));
            s.OnOrderFilled(Fill(OrderSide.Sell, 1, 130));

            var m = s.GetMetrics();
            // 10 + 20 + 30, all against the 100 entry.
            Assert.Equal(60, m.GrossProfit, 6);
            Assert.Equal(0, m.GrossLoss, 6);
        }

        [Fact]
        public void An_opposite_fill_larger_than_the_position_reverses_into_the_remainder()
        {
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 1, 100));
            s.OnOrderFilled(Fill(OrderSide.Sell, 3, 110));   // close 1 (+10), open 2 short at 110
            s.OnOrderFilled(Fill(OrderSide.Buy, 2, 100));    // close the short (+20)

            var m = s.GetMetrics();
            Assert.Equal(30, m.GrossProfit, 6);
            Assert.Equal(0, m.GrossLoss, 6);
        }

        [Fact]
        public void A_losing_close_is_still_booked_as_a_loss()
        {
            // Vacuity check: everything above asserts GrossLoss is zero, which a strategy that
            // booked nothing at all would also satisfy.
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 1, 100));
            s.OnOrderFilled(Fill(OrderSide.Sell, 1, 90));

            var m = s.GetMetrics();
            Assert.Equal(10, m.GrossLoss, 6);
            Assert.Equal(0, m.GrossProfit, 6);
        }

        [Fact]
        public void A_zero_quantity_fill_changes_nothing()
        {
            var s = new NeverSignals();

            s.OnOrderFilled(Fill(OrderSide.Buy, 1, 100));
            s.OnOrderFilled(Fill(OrderSide.Sell, 0, 90));    // nothing filled
            s.OnOrderFilled(Fill(OrderSide.Sell, 1, 110));

            var m = s.GetMetrics();
            Assert.Equal(10, m.GrossProfit, 6);
            Assert.Equal(0, m.GrossLoss, 6);
        }
    }
}
