using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Max drawdown follows account equity, including positions marked at bar closes.
    /// </summary>
    public class BacktestDrawdownTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Emits the given signal on each listed bar index (0-based, as OnBar sees them).</summary>
        private sealed class EmitsAt : ITradingStrategy
        {
            private readonly Dictionary<int, StrategySignal> _at;
            private int _bar;

            public EmitsAt(Dictionary<int, StrategySignal> at) { _at = at; }
            public string Id => "DRAWDOWN";
            public string Name => "Drawdown";
            public string Description => "emits fixed signals on fixed bars";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameters) { _bar = 0; }
            public StrategySignal? OnBar(Ohlcv bar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
                => _at.TryGetValue(_bar++, out var signal) ? signal : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        private static Ohlcv Bar(int i, double o, double h, double l, double c) =>
            new(T0.AddDays(i), o, h, l, c, 1000);

        private static BacktestConfig Free() =>
            new(StartingCapital: 10_000, CommissionRate: 0.0, SlippagePercent: 0.0,
                WarmupBars: 0, ReplayProfiles: false, AllowReverseOnSignal: false);

        private static StrategySignal Position(OrderSide side) =>
            new(Side: side, OrderType: OrderType.Market, Quantity: 100, LimitPrice: null,
                StopLoss: null, TakeProfit: null, Rationale: "drawdown fixture", Confidence: 1);

        private static Task<BacktestResult> Run(IReadOnlyList<Ohlcv> bars, Dictionary<int, StrategySignal> at) =>
            new StrategyBacktester().RunAsync(new EmitsAt(at), bars, Free());

        [Fact]
        public async Task An_open_loss_that_recovers_is_drawdown()
        {
            // Quantity 100 at 100 uses the full $10,000 account. At 60 its unrealised loss is
            // $4,000, so the drawdown is $4,000 / $10,000 = 40%, even though it closes at 101
            // for a $100 gain. The old realized-only samples reported zero.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 101, 59, 60),  // open loss
                Bar(3, 100, 101, 99, 100), // recovery
                Bar(4, 100, 102, 99, 101), // end of data: small gain
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Buy) });

            Assert.Equal(0.4, result.Metrics.MaxDrawdown, 9);
        }

        [Fact]
        public async Task A_loss_on_the_final_bar_is_drawdown()
        {
            // The loop never visits the final bar. Its close at 60 realizes a $4,000 loss on
            // 100 shares entered at 100; $4,000 / the $10,000 peak = 40%. This pins the
            // end-of-data sample, since the per-bar mark cannot see that final close.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 101, 99, 100), // flat; last bar visited by the loop
                Bar(3, 100, 101, 59, 60),  // final bar, not visited by the loop
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Buy) });

            Assert.Equal(0.4, result.Metrics.MaxDrawdown, 9);
        }

        [Fact]
        public async Task An_open_gain_given_back_is_drawdown_from_the_marked_peak()
        {
            // The long's $3,000 open gain at 130 makes marked equity $13,000. Closing at 100
            // returns equity to $10,000, so the giveback is $3,000 / $13,000 = 3/13.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 131, 99, 130), // open gain
                Bar(3, 100, 101, 99, 100), // end of data gives it back
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Buy) });

            Assert.Equal(3.0 / 13.0, result.Metrics.MaxDrawdown, 9);
        }

        [Fact]
        public async Task A_short_marks_a_rising_price_as_a_loss()
        {
            // A short of 100 at 100 loses $4,000 when the close rises to 140. Against the
            // $10,000 peak that is 40%; recovering to 100 at the end does not erase the drawdown.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 141, 99, 140), // adverse rise while short
                Bar(3, 100, 101, 99, 100), // end of data recovers
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Sell) });

            Assert.Equal(0.4, result.Metrics.MaxDrawdown, 9);
        }

        [Fact]
        public async Task No_position_means_no_drawdown()
        {
            // This pin is allowed to be green before the fix: no signal means no position or
            // equity swing, so MaxDrawdown must remain exactly zero.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 110, 90, 105),
                Bar(1, 105, 115, 95, 100),
                Bar(2, 100, 110, 90, 105),
            };

            var result = await Run(bars, new());

            Assert.Equal(0.0, result.Metrics.MaxDrawdown);
        }

        [Fact]
        public async Task Speech_summary_uses_the_open_loss_drawdown()
        {
            // This uses test 1's $4,000 fall from a $10,000 peak (40%) and pins that the spoken
            // result summary carries the same corrected metric.
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100),
                Bar(2, 100, 101, 59, 60),
                Bar(3, 100, 101, 99, 100),
                Bar(4, 100, 102, 99, 101),
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Buy) });
            string drawdownSpeech = $"maximum drawdown {result.Metrics.MaxDrawdown * 100:F1} percent";

            Assert.Contains("maximum drawdown 40.0 percent", result.SpeechSummary);
            Assert.Contains(drawdownSpeech, result.SpeechSummary);
        }
    }
}
