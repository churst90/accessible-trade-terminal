using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>R is measured against the stop the position was OPENED with, once per position.</b>
    ///
    /// <para>
    /// Defect demonstrated 2026-10-18. Every exit row the backtester wrote carried the CURRENT
    /// stop. After take-profit 1 the stop moves to breakeven (or starts an ATR trail), so every
    /// later row — the rest of the ladder and the runner's stop — had a risk of zero (breakeven)
    /// or a negative/tiny one (a trail above the entry). Both R paths then mis-scored it:
    /// <c>StrategyBacktester</c>'s Average R dropped those rows, and <c>BootstrapCi.TradeR</c>
    /// dropped the zero-risk ones and divided the trailed ones by the trail's tiny distance. On
    /// top of that, both averaged over exit ROWS, so a two-rung position counted twice. On a
    /// 2R/4R ladder with breakeven, a position that reached 4R scored +2R, and one that took
    /// TP1 and came back to breakeven also scored +2R instead of +1R.
    /// </para>
    ///
    /// <para>
    /// The definition pinned here is the one <c>docs/SETUP_GRADER_PREREG.md</c> section 2.2 sets
    /// out: per position, the sum over its exits of (portion × (exit − entry)) ÷ (entry − initial
    /// stop). The expected values below are worked by hand from the fixture, not recomputed with
    /// the production formula.
    /// </para>
    /// </summary>
    public class BacktestRAccountingTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Emits the given signal on each listed bar index (0-based, as OnBar sees them).</summary>
        private sealed class EmitsAt : ITradingStrategy
        {
            private readonly Dictionary<int, StrategySignal> _at;
            private int _bar;
            public EmitsAt(Dictionary<int, StrategySignal> at) { _at = at; }
            public string Id => "R-ACCOUNTING";
            public string Name => "R accounting";
            public string Description => "emits fixed signals on fixed bars";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> h, WorkspaceState s, IDictionary<string, object> p) { _bar = 0; }
            public StrategySignal? OnBar(Ohlcv b, IReadOnlyList<Ohlcv> h, WorkspaceState s)
                => _at.TryGetValue(_bar++, out var sig) ? sig : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        /// <summary>Long, entry fills at the next bar's open (100 in every fixture), stop 90 = 10 of
        /// risk, TP1 120 (2R) closes half, TP2 140 (4R) closes the rest.</summary>
        private static StrategySignal Ladder(StopAdjustOnTp1 adjust, double tp2 = 140, int trailPeriod = 14, double trailMult = 1.5) =>
            new(Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1.0, LimitPrice: null,
                StopLoss: 90, TakeProfit: 120, Rationale: "ladder", Confidence: 1,
                TpLadder: new[] { 120.0, tp2 }, TpClosePortions: new[] { 0.5, 0.5 },
                StopAdjust: adjust, TrailAtrPeriod: trailPeriod, TrailAtrMultiple: trailMult);

        private static Ohlcv Bar(int i, double o, double h, double l, double c) =>
            new(T0.AddDays(i), o, h, l, c, 1000);

        private static BacktestConfig Free() =>
            new(StartingCapital: 10_000, CommissionRate: 0.0, SlippagePercent: 0.0,
                WarmupBars: 0, ReplayProfiles: false, AllowReverseOnSignal: false);

        private static Task<BacktestResult> Run(IReadOnlyList<Ohlcv> bars, Dictionary<int, StrategySignal> at) =>
            new StrategyBacktester().RunAsync(new EmitsAt(at), bars, Free());

        [Fact]
        public async Task A_ladder_that_reaches_TP2_after_a_breakeven_move_scores_3R()
        {
            // Half at +2R, half at +4R: 0.5 × 2 + 0.5 × 4 = +3R. The defect scored it +2R — the
            // TP2 row carried the breakeven stop, so its risk was zero and it was dropped.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),   // signal
                Bar(1, 100, 101, 99, 100),   // entry at 100
                Bar(2, 100, 121, 100, 120),  // TP1 120; stop to breakeven
                Bar(3, 120, 141, 119, 140),  // TP2 140
                Bar(4, 140, 141, 139, 140),
            };

            var r = await Run(bars, new() { [0] = Ladder(StopAdjustOnTp1.MoveToBreakeven) });

            Assert.Equal(2, r.Trades.Count);
            Assert.Equal(3.0, r.AverageR, 9);
            Assert.Equal(3.0, Assert.Single(BootstrapCi.ExtractRs(r.Trades)), 9);
            // Every row names the stop the position was opened with — the documented meaning of
            // BacktestTrade.StopPrice ("original stop price ... for R-multiple math").
            Assert.All(r.Trades, t => Assert.Equal(90.0, t.StopPrice!.Value, 9));
        }

        [Fact]
        public async Task TP1_then_a_breakeven_stop_scores_1R_not_2R()
        {
            // Half at +2R, the runner back at entry: 0.5 × 2 + 0.5 × 0 = +1R.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100),   // entry at 100
                Bar(2, 100, 121, 100, 120),  // TP1 120; stop to 100
                Bar(3, 110, 111, 98, 99),    // breakeven stop at 100
                Bar(4, 99, 100, 98, 99),
            };

            var r = await Run(bars, new() { [0] = Ladder(StopAdjustOnTp1.MoveToBreakeven) });

            Assert.Equal(2, r.Trades.Count);
            Assert.Equal("Breakeven stop", r.Trades[1].ExitReason);
            Assert.Equal(1.0, r.AverageR, 9);
            Assert.Equal(1.0, Assert.Single(BootstrapCi.ExtractRs(r.Trades)), 9);
            // Every row names the stop the position was opened with — the documented meaning of
            // BacktestTrade.StopPrice ("original stop price ... for R-multiple math").
            Assert.All(r.Trades, t => Assert.Equal(90.0, t.StopPrice!.Value, 9));
        }

        [Fact]
        public async Task Each_exit_counts_by_the_share_of_the_position_it_closed()
        {
            // A quarter at +2R, three quarters back at entry: 0.25 × 2 + 0.75 × 0 = +0.5R. An
            // unweighted mean of the two exits would say +1R.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100),
                Bar(2, 100, 121, 100, 120),  // TP1 closes 25%
                Bar(3, 110, 111, 98, 99),    // breakeven stop on the other 75%
                Bar(4, 99, 100, 98, 99),
            };
            var signal = Ladder(StopAdjustOnTp1.MoveToBreakeven) with { TpClosePortions = new[] { 0.25, 0.75 } };

            var r = await Run(bars, new() { [0] = signal });

            Assert.Equal(2, r.Trades.Count);
            Assert.Equal(0.5, r.AverageR, 9);
            Assert.Equal(0.5, Assert.Single(BootstrapCi.ExtractRs(r.Trades)), 9);
        }

        [Fact]
        public async Task A_runner_stopped_on_an_ATR_trail_is_scored_against_the_initial_stop()
        {
            // Trail period 2, multiple 1. At bar 2's close (120) the 2-bar ATR is (2 + 21) / 2 =
            // 11.5, so the trail sits at 108.5 and holds there on bar 3. Bar 4 trades through it:
            // the runner exits at 108.5 = +0.85R. Position: 0.5 × 2 + 0.5 × 0.85 = +1.425R.
            // The defect dropped the runner row from Average R (risk 100 − 108.5 < 0) and scored
            // it +1R in the bootstrap (|100 − 108.5| of "risk").
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100),   // entry at 100
                Bar(2, 100, 121, 100, 120),  // TP1 120; trail starts
                Bar(3, 120, 122, 118, 121),
                Bar(4, 115, 116, 105, 106),  // trail at 108.5 hit
                Bar(5, 106, 107, 105, 106),
            };

            var r = await Run(bars, new() { [0] = Ladder(StopAdjustOnTp1.TrailByAtr, tp2: 200, trailPeriod: 2, trailMult: 1.0) });

            Assert.Equal(2, r.Trades.Count);
            Assert.Equal(108.5, r.Trades[1].ExitPrice!.Value, 9);   // the trail really moved
            Assert.Equal(1.425, r.AverageR, 9);
            Assert.Equal(1.425, Assert.Single(BootstrapCi.ExtractRs(r.Trades)), 9);
            // Every row names the stop the position was opened with — the documented meaning of
            // BacktestTrade.StopPrice ("original stop price ... for R-multiple math").
            Assert.All(r.Trades, t => Assert.Equal(90.0, t.StopPrice!.Value, 9));
        }

        [Fact]
        public async Task A_two_rung_position_weighs_the_same_as_a_one_row_loser()
        {
            // Position 1 stops out whole at −1R (one row). Position 2 runs the ladder to +3R (two
            // rows). Two positions, mean +1R. Averaging rows would give three samples.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),   // signal 1
                Bar(1, 100, 101, 99, 100),   // entry 1 at 100
                Bar(2, 95, 96, 89, 90),      // stop 90: −1R
                Bar(3, 95, 101, 95, 100),    // signal 2
                Bar(4, 100, 101, 99, 100),   // entry 2 at 100
                Bar(5, 100, 121, 100, 120),  // TP1
                Bar(6, 120, 141, 119, 140),  // TP2
                Bar(7, 140, 141, 139, 140),
            };

            var r = await Run(bars, new()
            {
                [0] = Ladder(StopAdjustOnTp1.MoveToBreakeven),
                [3] = Ladder(StopAdjustOnTp1.MoveToBreakeven),
            });

            Assert.Equal(3, r.Trades.Count);
            Assert.Equal(2, r.Metrics.TotalSignals);
            var rs = BootstrapCi.ExtractRs(r.Trades).OrderBy(x => x).ToList();
            Assert.Equal(new[] { -1.0, 3.0 }, rs.Select(x => Math.Round(x, 9)));
            Assert.Equal(1.0, r.AverageR, 9);
        }
    }
}
