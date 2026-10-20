using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The trade-level diagnostic export is opt-in and never overwrites an earlier run.
    /// </summary>
    public class BacktestDiagnosticCsvTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        private sealed class EmitsAt : ITradingStrategy
        {
            private int _bar;

            public string Id => "DIAGNOSTIC_CSV";
            public string Name => "Diagnostic CSV";
            public string Description => "emits one fixed signal";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameters) => _bar = 0;
            public StrategySignal? OnBar(Ohlcv bar, IReadOnlyList<Ohlcv> history, WorkspaceState state) =>
                _bar++ == 0 ? new StrategySignal(
                    Side: OrderSide.Buy,
                    OrderType: OrderType.Market,
                    Quantity: 1,
                    LimitPrice: null,
                    StopLoss: null,
                    TakeProfit: null,
                    Rationale: "diagnostic CSV fixture",
                    Confidence: 1) : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        private static IReadOnlyList<Ohlcv> Bars() => new[]
        {
            Bar(0),
            Bar(1),
            Bar(2),
        };

        private static Ohlcv Bar(int i) => new(T0.AddDays(i), 100, 101, 99, 100, 1000);

        private static BacktestConfig Free() => new(
            StartingCapital: 10_000,
            CommissionRate: 0,
            SlippagePercent: 0,
            WarmupBars: 0,
            ReplayProfiles: false,
            AllowReverseOnSignal: false);

        private static Task<BacktestResult> Run(BacktestConfig config) =>
            new StrategyBacktester().RunAsync(new EmitsAt(), Bars(), config);

        [Fact]
        public async Task Default_config_writes_no_diagnostic_csv()
        {
            int before = TestTemp.CountBacktestDiagnosticCsvFiles();

            var result = await Run(Free());

            int after = TestTemp.CountBacktestDiagnosticCsvFiles();
            Assert.NotEmpty(result.Trades);
            Assert.Equal(before, after);
        }

        [Fact]
        public async Task Requested_export_is_written_to_its_directory_with_one_row_per_trade_row()
        {
            string directory = TestTemp.NewDir("diagcsv-");
            var config = Free() with { DiagnosticCsvDirectory = directory };

            var result = await Run(config);

            var files = Directory.GetFiles(directory, "*.csv");
            Assert.Single(files);
            var lines = File.ReadAllLines(files[0]);
            Assert.StartsWith("EntryTime,EntryPrice,Side,Quantity,ExitTime,ExitPrice,PnL,R,BarsInTrade,ExitReason,StopPrice", lines[0]);
            Assert.Equal(result.Trades.Count + 1, lines.Length);
        }

        [Fact]
        public async Task Two_requested_exports_never_overwrite_each_other()
        {
            string directory = TestTemp.NewDir("diagcsv-");
            var config = Free() with { DiagnosticCsvDirectory = directory };

            await Run(config);
            await Run(config);

            Assert.Equal(2, Directory.GetFiles(directory, "*.csv").Length);
        }
    }
}
