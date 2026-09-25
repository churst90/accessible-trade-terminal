using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The in-app Lab (walk-forward windows + battery comparison). Pins the window
    /// slicing (chronological, non-overlapping, covers the whole range), the
    /// survivor gate (95% CI lower bound positive in BOTH halves with ≥5 trades
    /// each — identical to the research harness), and the era-robustness ranking
    /// (weaker half first).
    /// </summary>
    public class LabRunnerTests
    {
        private static List<Ohlcv> Bars(int n) => Enumerable.Range(0, n)
            .Select(i => new Ohlcv(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                100, 101, 99, 100, 1000))
            .ToList();

        private static BacktestConfig Config() => new(
            StartingCapital: 10_000, CommissionRate: 0, SlippagePercent: 0, WarmupBars: 0,
            ReplayProfiles: false, PositionSizer: null, StartDate: null, EndDate: null,
            AllowReverseOnSignal: true);

        private static BacktestTrade WinningTrade(double r = 2.0) => new(
            EntryTime: DateTime.UtcNow, EntryPrice: 100, Side: OrderSide.Buy, Quantity: 1,
            ExitTime: DateTime.UtcNow, ExitPrice: 100 + 10 * r, PnL: 10 * r,
            ExitReason: "tp", StopPrice: 90);

        private static BacktestTrade LosingTrade() => new(
            EntryTime: DateTime.UtcNow, EntryPrice: 100, Side: OrderSide.Buy, Quantity: 1,
            ExitTime: DateTime.UtcNow, ExitPrice: 90, PnL: -10,
            ExitReason: "stop", StopPrice: 90);

        private static BacktestResult Result(params BacktestTrade[] trades) => new(
            Metrics: new StrategyMetrics(
                TotalSignals: trades.Length,
                WinningTrades: trades.Count(t => (t.PnL ?? 0) > 0),
                WinRate: trades.Length == 0 ? 0 : (double)trades.Count(t => (t.PnL ?? 0) > 0) / trades.Length,
                MaxDrawdown: 0.1,
                TotalPnL: trades.Sum(t => t.PnL ?? 0),
                SharpeRatio: 1.0),
            Trades: trades.ToList(),
            EquityCurve: new List<(DateTime, double)>(),
            SpeechSummary: "",
            AverageR: trades.Length == 0 ? double.NaN : 1.0,
            ProfitFactor: 2.0);

        [Fact]
        public async Task RunWindows_SlicesChronologically_AndCoversTheWholeRange()
        {
            var coordinator = Substitute.For<IStrategyModalCoordinator>();
            var configs = new List<BacktestConfig>();
            coordinator.RunBacktestAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Do<BacktestConfig>(c => configs.Add(c)), Arg.Any<WorkspaceState>())
                .Returns((Result(WinningTrade()), new StrategyCoordinatorResult(true, "ok")));
            var runner = new LabRunner(coordinator);
            var bars = Bars(100);

            var results = await runner.RunWindowsAsync("spec", bars, WorkspaceState.Initial, 4, Config());

            Assert.Equal(4, results.Count);
            Assert.Equal(4, configs.Count);
            // Non-overlapping, chronological, covering first→last.
            Assert.Equal(bars[0].Date, configs[0].StartDate);
            Assert.Equal(bars[^1].Date, configs[^1].EndDate);
            for (int i = 1; i < configs.Count; i++)
                Assert.Equal(configs[i - 1].EndDate, configs[i].StartDate);
        }

        [Fact]
        public async Task Compare_SurvivorRequiresBothHalvesPositive_WithEnoughTrades()
        {
            // Spec "good": 8 clean winners per half → CI-lo > 0 both halves → SURVIVOR.
            // Spec "thin": only 3 trades per half → not enough sample, never a survivor.
            // Spec "onehalf": winners in H1, losers in H2 → not a survivor.
            var coordinator = Substitute.For<IStrategyModalCoordinator>();
            coordinator.RunBacktestAsync("good", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns((Result(Enumerable.Repeat(0, 8).Select(_ => WinningTrade()).ToArray()),
                          new StrategyCoordinatorResult(true, "ok")));
            coordinator.RunBacktestAsync("thin", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns((Result(WinningTrade(), WinningTrade(), WinningTrade()),
                          new StrategyCoordinatorResult(true, "ok")));
            int oneHalfCalls = 0;
            coordinator.RunBacktestAsync("onehalf", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns(_ => ++oneHalfCalls == 1
                    ? (Result(Enumerable.Repeat(0, 8).Select(x => WinningTrade()).ToArray()),
                       new StrategyCoordinatorResult(true, "ok"))
                    : (Result(Enumerable.Repeat(0, 8).Select(x => LosingTrade()).ToArray()),
                       new StrategyCoordinatorResult(true, "ok")));

            var runner = new LabRunner(coordinator);
            var rows = await runner.CompareAsync(
                new[] { ("thin", "Thin"), ("onehalf", "OneHalf"), ("good", "Good") },
                Bars(100), WorkspaceState.Initial, Config());

            Assert.Equal(3, rows.Count);
            // Survivors rank first; "good" is the only one.
            Assert.Equal("Good", rows[0].Name);
            Assert.True(rows[0].Survivor);
            Assert.False(rows.First(r => r.Name == "Thin").Survivor);
            Assert.False(rows.First(r => r.Name == "OneHalf").Survivor);
        }

        /// <summary>
        /// A2l (2026-09-24): the "thin" fixture above is thin in BOTH halves, so it cannot tell
        /// "n1 &gt;= 5 &amp;&amp; n2 &gt;= 5" from "n1 &gt;= 5" — deleting the second-half check left
        /// the suite green. Three trades in the second half is a CI made of noise, whatever the
        /// first half did; the gate exists because a strategy is only as good as its weaker regime.
        /// </summary>
        [Fact]
        public async Task Compare_ASecondHalfWithTooFewTrades_IsNotASurvivor_HoweverGoodTheFirst()
        {
            var coordinator = Substitute.For<IStrategyModalCoordinator>();
            int calls = 0;
            coordinator.RunBacktestAsync("late-thin", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns(_ => ++calls == 1
                    ? (Result(Enumerable.Repeat(0, 8).Select(x => WinningTrade()).ToArray()),
                       new StrategyCoordinatorResult(true, "ok"))
                    : (Result(WinningTrade(), WinningTrade(), WinningTrade()),
                       new StrategyCoordinatorResult(true, "ok")));

            var runner = new LabRunner(coordinator);
            var rows = await runner.CompareAsync(new[] { ("late-thin", "LateThin") },
                Bars(100), WorkspaceState.Initial, Config());

            var row = Assert.Single(rows);
            Assert.Equal(8, row.TradesH1);
            Assert.Equal(3, row.TradesH2);
            Assert.True(row.CiLoH2 > 0, "The control: the second half is positive, so only the sample size can refuse it.");
            Assert.False(row.Survivor);
        }

        [Fact]
        public async Task Compare_RanksByTheWeakerHalf()
        {
            // Both survive; "steady" (2R/2R) must outrank "flashy" (5R H1, barely-positive H2)
            // because ranking uses the WEAKER half's CI lower bound.
            var coordinator = Substitute.For<IStrategyModalCoordinator>();
            int steadyCalls = 0, flashyCalls = 0;
            coordinator.RunBacktestAsync("steady", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns(_ => { steadyCalls++;
                    return (Result(Enumerable.Repeat(0, 10).Select(x => WinningTrade(2.0)).ToArray()),
                            new StrategyCoordinatorResult(true, "ok")); });
            coordinator.RunBacktestAsync("flashy", Arg.Any<IReadOnlyList<Ohlcv>>(),
                    Arg.Any<BacktestConfig>(), Arg.Any<WorkspaceState>())
                .Returns(_ => ++flashyCalls == 1
                    ? (Result(Enumerable.Repeat(0, 10).Select(x => WinningTrade(5.0)).ToArray()),
                       new StrategyCoordinatorResult(true, "ok"))
                    : (Result(Enumerable.Repeat(0, 10).Select(x => WinningTrade(0.1)).ToArray()),
                       new StrategyCoordinatorResult(true, "ok")));

            var runner = new LabRunner(coordinator);
            var rows = await runner.CompareAsync(
                new[] { ("flashy", "Flashy"), ("steady", "Steady") },
                Bars(100), WorkspaceState.Initial, Config());

            Assert.True(rows[0].Survivor && rows[1].Survivor);
            Assert.Equal("Steady", rows[0].Name);
        }

        [Fact]
        public async Task RunWindows_TooLittleData_ReturnsEmpty()
        {
            var runner = new LabRunner(Substitute.For<IStrategyModalCoordinator>());
            Assert.Empty(await runner.RunWindowsAsync("s", Bars(5), WorkspaceState.Initial, 4, Config()));
        }
    }
}
