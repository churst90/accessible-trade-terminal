using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The final bar is reserved for closing an open position, not evaluating a signal.
    /// </summary>
    public class BacktestEvaluatedBarsTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        private sealed class EmitsNone : ITradingStrategy
        {
            public string Id => "EVALUATED-BARS";
            public string Name => "Evaluated bars";
            public string Description => "emits no signals";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameters) { }
            public StrategySignal? OnBar(Ohlcv bar, IReadOnlyList<Ohlcv> history, WorkspaceState state) => null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        private static Ohlcv Bar(int i) => new(T0.AddDays(i), 100, 101, 99, 100, 1000);

        [Fact]
        public async Task Ten_bar_run_excludes_final_close_out_bar_from_evaluated_count_and_speech()
        {
            var bars = Enumerable.Range(0, 10).Select(Bar).ToList();
            var config = new BacktestConfig(
                StartingCapital: 10_000,
                CommissionRate: 0,
                SlippagePercent: 0,
                WarmupBars: 3,
                ReplayProfiles: false,
                AllowReverseOnSignal: false);

            var result = await new StrategyBacktester().RunAsync(new EmitsNone(), bars, config);

            // The loop visits indexes 0–8; indexes 0–2 are warmup, leaving 3–8 (6 bars)
            // for signal evaluation. Index 9 is held out to close an open position.
            Assert.Equal(3, result.WarmupBars);
            Assert.Equal(6, result.EvaluatedBars);
            Assert.Contains("3 warmup bars, 6 bars evaluated", result.SpeechSummary);
        }
    }
}
