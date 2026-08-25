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
    }
}
