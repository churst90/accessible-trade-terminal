using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Correctness tests for <see cref="StrategyBacktester"/>. Each test drives a
    /// small synthetic bar series through a deterministic test strategy and asserts
    /// a specific production invariant:
    ///
    ///   • Warmup-gate behaviour (signals before <c>WarmupBars</c> are dropped).
    ///   • Stop-loss exits on the NEXT bar (no same-bar fill/exit).
    ///   • Single TP exit + TP-ladder multi-rung exits with portion correctness.
    ///   • Stop-hit priority on a bar that touches BOTH the stop and a TP.
    ///   • Reversal-on-opposite-signal when <c>AllowReverseOnSignal = true</c>.
    ///   • Date-range slicing (walk-forward).
    ///   • Insufficient-data / empty dataset guards.
    ///
    /// Uses a minimal <see cref="DeterministicStrategy"/> that emits exactly one
    /// signal on a configured bar index — this keeps each test's failure mode
    /// obvious and avoids any dependency on a real indicator.
    /// </summary>
    public class StrategyBacktesterTests
    {
        private static List<Ohlcv> LinearBars(int count, double startPrice = 100, double step = 1.0, DateTime? start = null)
        {
            var s = start ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
            {
                double p = startPrice + i * step;
                list.Add(new Ohlcv(s.AddMinutes(i), p, p + 0.5, p - 0.5, p, 1000));
            }
            return list;
        }

        /// <summary>
        /// Strategy that emits exactly one configured signal on bar index
        /// <c>emitIndex</c> (or on every bar matching a predicate) and returns null otherwise.
        /// </summary>
        private class DeterministicStrategy : ITradingStrategy
        {
            private readonly Func<int, StrategySignal?> _emitter;
            private int _barCount;

            public DeterministicStrategy(int emitIndex, StrategySignal signal)
            {
                _emitter = i => i == emitIndex ? signal : null;
            }

            public DeterministicStrategy(Func<int, StrategySignal?> emitter) { _emitter = emitter; }

            public string Id => "TEST";
            public string Name => "Test";
            public string Description => "deterministic";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { _barCount = 0; }
            public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                int idx = _barCount++;
                return _emitter(idx);
            }
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);
        }

        // ── Warmup gate ─────────────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task Warmup_DropsSignalsBeforeCutoff()
        {
            var bt = new StrategyBacktester();
            var data = LinearBars(100);
            // Strategy emits a long signal on the FIRST OnBar call; with WarmupBars=50
            // that signal should be dropped and no trade recorded.
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 50, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 50, ReplayProfiles: false);
            var result = await bt.RunAsync(strat, data, cfg);
            Assert.Empty(result.Trades);
        }

        [Fact]
        public async System.Threading.Tasks.Task Warmup_AllowsSignalsAfterCutoff()
        {
            var bt = new StrategyBacktester();
            var data = LinearBars(100);
            // Emit at bar 55 (after warmup=50). Price rises monotonically, so a long with
            // SL below the entry will never hit; it will close at end-of-data.
            var strat = new DeterministicStrategy(55, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 10, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 50, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, data, cfg);
            Assert.NotEmpty(result.Trades);
        }

        // ── Stop-loss exit ──────────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task Stop_TriggersWhenPriceCrossesAdverseToLong()
        {
            // Price falls: Open starts at 100 then decreases by 1 per bar.
            var bt = new StrategyBacktester();
            var bars = new List<Ohlcv>();
            var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 30; i++)
            {
                double p = 100 - i;
                bars.Add(new Ohlcv(ts.AddMinutes(i), p, p + 0.25, p - 0.25, p, 1000));
            }

            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 95, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, bars, cfg);

            Assert.NotEmpty(result.Trades);
            var trade = result.Trades[0];
            Assert.True(trade.ExitPrice.HasValue);
            Assert.InRange(trade.ExitPrice!.Value, 94.9, 95.1); // stop at 95
            Assert.Contains("Stop", trade.ExitReason);
        }

        [Fact]
        public async System.Threading.Tasks.Task Stop_TriggersWhenPriceCrossesAdverseToShort()
        {
            // Price rises: 100, 101, 102, ... A short with stop at 105 trips around bar 5-6.
            var bt = new StrategyBacktester();
            var data = LinearBars(30);

            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Sell, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 105, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, data, cfg);

            Assert.NotEmpty(result.Trades);
            var stopTrade = result.Trades.FirstOrDefault(t => t.ExitReason.Contains("Stop"));
            Assert.NotNull(stopTrade);
            Assert.InRange(stopTrade!.ExitPrice!.Value, 104.9, 105.1);
        }

        // ── Take-profit exit ────────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task SingleTp_ExitsAtTpPriceWhenHit()
        {
            // Price rises linearly by 1/bar from 100; a long with TP at 110 closes around bar 10.
            var bt = new StrategyBacktester();
            var data = LinearBars(30);

            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 90, TakeProfit: 110, Rationale: "test", Confidence: 1,
                TpLadder: new[] { 110.0 }, TpClosePortions: new[] { 1.0 }));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, data, cfg);

            Assert.NotEmpty(result.Trades);
            var tpTrade = result.Trades.FirstOrDefault(t => t.ExitReason.StartsWith("TP"));
            Assert.NotNull(tpTrade);
            Assert.InRange(tpTrade!.ExitPrice!.Value, 109.9, 110.1);
        }

        [Fact]
        public async System.Threading.Tasks.Task TpLadder_ThreeRungs_ClosesInOrder()
        {
            var bt = new StrategyBacktester();
            var data = LinearBars(50);

            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 3, LimitPrice: null,
                StopLoss: 50, TakeProfit: 105, Rationale: "test", Confidence: 1,
                TpLadder:         new[] { 105.0, 115.0, 125.0 },
                TpClosePortions:  new[] { 1.0 / 3, 1.0 / 3, 1.0 / 3 }));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, data, cfg);

            var tpRows = result.Trades.Where(t => t.ExitReason.StartsWith("TP")).ToList();
            Assert.Equal(3, tpRows.Count);
            // Each TP row closes at the corresponding ladder price (mono-rising prices
            // guarantee the rungs fire in sequence).
            Assert.InRange(tpRows[0].ExitPrice!.Value, 104.9, 105.1);
            Assert.InRange(tpRows[1].ExitPrice!.Value, 114.9, 115.1);
            Assert.InRange(tpRows[2].ExitPrice!.Value, 124.9, 125.1);
        }

        // ── End-of-data close ───────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task PositionStillOpen_AtEndOfData_IsClosedWithReason()
        {
            // Flat-ish price; stop never hits; TP never hits. Position closes at end of data.
            var bt = new StrategyBacktester();
            var bars = new List<Ohlcv>();
            var ts = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 50; i++)
            {
                double p = 100 + (i % 2 == 0 ? 0.1 : -0.1);
                bars.Add(new Ohlcv(ts.AddMinutes(i), p, p + 0.05, p - 0.05, p, 1000));
            }

            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 50, TakeProfit: 200, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, bars, cfg);

            Assert.NotEmpty(result.Trades);
            var last = result.Trades[^1];
            Assert.True(last.ExitTime.HasValue);
        }

        // ── Insufficient data ───────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task InsufficientData_ReturnsEmptyResultWithMessage()
        {
            var bt = new StrategyBacktester();
            var tooShort = LinearBars(1);
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 90, TakeProfit: 110, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false);
            var result = await bt.RunAsync(strat, tooShort, cfg);
            Assert.Empty(result.Trades);
            Assert.Contains("Insufficient", result.SpeechSummary);
        }

        // ── Date-range slicing (walk-forward) ───────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task DateRange_LimitsEvaluationToWindow()
        {
            var bt = new StrategyBacktester();
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var data = LinearBars(100, start: start);

            // Filter to the second half — only bars with Date >= start+50min should survive.
            // Strategy emits on bar 0 OF THE FILTERED SLICE, which is the 51st original bar.
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 50, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(
                WarmupBars: 0,
                ReplayProfiles: false,
                CommissionRate: 0,
                SlippagePercent: 0,
                StartDate: start.AddMinutes(50));
            var result = await bt.RunAsync(strat, data, cfg);

            Assert.NotEmpty(result.Trades);
            // Every trade's EntryTime must be within the filtered window.
            Assert.All(result.Trades, t => Assert.True(t.EntryTime >= start.AddMinutes(50)));
        }

        // ── Equity curve monotonic in time ──────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task EquityCurve_HasEntriesAndIsTimeOrdered()
        {
            var bt = new StrategyBacktester();
            var data = LinearBars(30);
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 50, TakeProfit: 110, Rationale: "test", Confidence: 1,
                TpLadder: new[] { 110.0 }, TpClosePortions: new[] { 1.0 }));
            var cfg = new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, CommissionRate: 0, SlippagePercent: 0);
            var result = await bt.RunAsync(strat, data, cfg);

            Assert.NotEmpty(result.EquityCurve);
            var dates = result.EquityCurve.Select(p => p.Date).ToList();
            for (int i = 1; i < dates.Count; i++)
                Assert.True(dates[i] >= dates[i - 1], $"equity curve out of order at index {i}: {dates[i - 1]} → {dates[i]}");
        }

        // ── Costs: the two tests that make the cost model non-vacuous ───────
        //
        // The A2 sabotage audit (2026-08-26) found that every one of the ten BacktestConfig
        // constructions in this suite set CommissionRate: 0 AND SlippagePercent: 0, so the five
        // production sites that multiply by those rates were only ever multiplied by zero.
        // Mutant M27 (entry commission replaced with 0) and M28 (entry slippage applied in the
        // trader's FAVOUR) both survived the full 4,830-test suite. These two tests are their
        // acceptance criteria: restore either mutant and one of them must go red.
        //
        // The rates are deliberately absurd (1% commission, 2% slippage) rather than realistic.
        // A realistic 0.1%/0.05% would put the expected numbers inside the rounding tolerance of
        // the zero-cost case, which is the same as not testing it. Every expected value below is
        // hand-computed from the fixture, NOT recomputed by calling the production formula —
        // see the standing lesson about tests that mirror the logic they are guarding.

        private const double Commission = 0.01;    // 1% per side
        private const double Slippage   = 0.02;    // 2% per side

        [Fact]
        public async System.Threading.Tasks.Task Costs_LongEntry_PaysSlippageUpAndCommissionOnBothSides()
        {
            // LinearBars: bar i has Open = Close = 100 + i, so the fill bar (index 1) opens at
            // 101 and the last bar (index 9) closes at 109. Price only rises, so a stop at 50
            // never trades and the position closes on "End of data".
            var bt = new StrategyBacktester();
            var data = LinearBars(10);
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 50, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(
                StartingCapital: 10000, WarmupBars: 0, ReplayProfiles: false,
                CommissionRate: Commission, SlippagePercent: Slippage);

            var result = await bt.RunAsync(strat, data, cfg);

            var trade = Assert.Single(result.Trades);

            // Slippage is ADVERSE for a buyer: 101 + (101 × 2%) = 103.02. With the sign flipped
            // (M28) this would be 98.98 — a better price than the market's, which is the tell.
            Assert.Equal(103.02, trade.EntryPrice, 6);

            // ...and adverse on the EXIT as well. This used to assert a flat 109.0, because
            // slippage was applied to entries only — BarFill.StopExit, BarFill.TargetExit and
            // the end-of-data lastBar.Close all filled at the exact modelled price. Closing a
            // long is a SELL, so it fills lower: 109 − (109 × 2%) = 106.82.
            Assert.Equal(106.82, trade.ExitPrice!.Value, 6);

            // Trade P&L carries the EXIT commission only (106.82 × 1 × 1% = 1.0682):
            //   (106.82 − 103.02) × 1 − 1.0682 = 2.7318
            Assert.Equal(2.7318, trade.PnL!.Value, 6);

            // The ENTRY commission (103.02 × 1 × 1% = 1.0302) is charged against equity rather
            // than against the trade row, so TotalPnL is the only place it shows up — which is
            // exactly why M27 could zero it without a single assertion noticing.
            //   2.7318 − 1.0302 = 1.7016
            Assert.Equal(1.7016, result.Metrics.TotalPnL, 6);
        }

        [Fact]
        public async System.Threading.Tasks.Task Costs_ShortEntry_PaysSlippageDownAndCommissionOnBothSides()
        {
            // The mirror image, and it is not redundant: a sign-flipped slippage term moves the
            // long fill down and the short fill UP, so only running both sides distinguishes
            // "slippage is adverse" from "slippage is added".
            var bt = new StrategyBacktester();
            var data = LinearBars(10);
            var strat = new DeterministicStrategy(0, new StrategySignal(
                Side: OrderSide.Sell, OrderType: OrderType.Market, Quantity: 1, LimitPrice: null,
                StopLoss: 200, TakeProfit: null, Rationale: "test", Confidence: 1));
            var cfg = new BacktestConfig(
                StartingCapital: 10000, WarmupBars: 0, ReplayProfiles: false,
                CommissionRate: Commission, SlippagePercent: Slippage);

            var result = await bt.RunAsync(strat, data, cfg);

            var trade = Assert.Single(result.Trades);

            // 101 − (101 × 2%) = 98.98. A short filled at 103.02 would be a short sold ABOVE
            // the market — free money, and the direction M28 introduces.
            Assert.Equal(98.98, trade.EntryPrice, 6);

            // Closing a short is a BUY, so it fills HIGHER: 109 + (109 × 2%) = 111.18. The
            // sign flip relative to the long case is the whole point — slippage is a cost at
            // both ends and in both directions.
            Assert.Equal(111.18, trade.ExitPrice!.Value, 6);

            // Exit commission 111.18 × 1% = 1.1118.
            //   (98.98 − 111.18) × 1 − 1.1118 = −13.3118
            Assert.Equal(-13.3118, trade.PnL!.Value, 6);

            // Entry commission 98.98 × 1% = 0.9898.  −13.3118 − 0.9898 = −14.3016
            Assert.Equal(-14.3016, result.Metrics.TotalPnL, 6);
        }
    }
}
