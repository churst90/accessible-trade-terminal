using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The backtest equity curve records account equity at each bar close.
    /// </summary>
    public class BacktestEquityCurveTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Emits the given signal on each listed bar index (0-based, as OnBar sees them).</summary>
        private sealed class EmitsAt : ITradingStrategy
        {
            private readonly Dictionary<int, StrategySignal> _at;
            private int _bar;

            public EmitsAt(Dictionary<int, StrategySignal> at) { _at = at; }
            public string Id => "EQUITY-CURVE";
            public string Name => "Equity curve";
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

        private static StrategySignal Position(
            OrderSide side,
            double quantity = 100,
            double? takeProfit = null,
            IReadOnlyList<double>? tpLadder = null,
            IReadOnlyList<double>? tpClosePortions = null) =>
            new(Side: side, OrderType: OrderType.Market, Quantity: quantity, LimitPrice: null,
                StopLoss: null, TakeProfit: takeProfit, Rationale: "equity curve fixture", Confidence: 1,
                TpLadder: tpLadder, TpClosePortions: tpClosePortions);

        private static Task<BacktestResult> Run(IReadOnlyList<Ohlcv> bars, Dictionary<int, StrategySignal> at) =>
            new StrategyBacktester().RunAsync(new EmitsAt(at), bars, Free());

        [Fact]
        public async Task EquityCurve_HasOnePointPerBar_ForLadderedPosition()
        {
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 106, 99, 105), // first half closes at 105
                Bar(3, 105, 111, 104, 110), // second half closes at 110
                Bar(4, 110, 111, 109, 110),
            };
            var signal = Position(OrderSide.Buy, quantity: 2,
                tpLadder: new[] { 105.0, 110.0 }, tpClosePortions: new[] { 0.5, 0.5 });

            var result = await Run(bars, new() { [0] = signal });

            // Five input bars require five points: the initial point, bars 1 through 3,
            // and the final-bar point. The dates must be the bars' five dates in that order.
            Assert.Equal(bars.Count, result.EquityCurve.Count);
            Assert.Equal(bars.Select(bar => bar.Date), result.EquityCurve.Select(point => point.Date));
            Assert.Collection(result.Trades, _ => { }, _ => { });
        }

        [Fact]
        public async Task EquityCurve_ShowsAnOpenLossAtTheBarClose()
        {
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // long enters 100 shares at 100
                Bar(2, 100, 101, 59, 60),  // open loss
                Bar(3, 100, 101, 99, 100), // recovery
                Bar(4, 100, 102, 99, 101), // end of data
            };

            var result = await Run(bars, new() { [0] = Position(OrderSide.Buy) });

            // $10,000 starting equity minus 100 shares × ($100 − $60) is $6,000.
            Assert.Equal(6_000, result.EquityCurve[2].EquityValue, 6);
            Assert.Equal(bars[2].Date, result.EquityCurve[2].Date);
        }

        [Fact]
        public async Task EquityCurve_EndsAtFinalEquityOnTheLastBar()
        {
            var bars = new List<Ohlcv>
            {
                Bar(0, 100, 101, 99, 100),
                Bar(1, 100, 101, 99, 100), // entry at 100
                Bar(2, 100, 106, 99, 105), // target closes at 105
                Bar(3, 105, 106, 104, 105),
                Bar(4, 105, 106, 104, 105), // flat through the end of data
            };
            var config = Free();
            var strategy = new EmitsAt(new() { [0] = Position(OrderSide.Buy, quantity: 1, takeProfit: 105) });

            var result = await new StrategyBacktester().RunAsync(strategy, bars, config);

            // One share bought at 100 and sold at 105 earns $5 with no costs, so final
            // equity is $10,005 even though the position closed before the final bar.
            var lastPoint = result.EquityCurve[^1];
            Assert.Equal(bars[^1].Date, lastPoint.Date);
            Assert.Equal(config.StartingCapital + result.Metrics.TotalPnL, lastPoint.EquityValue, 6);
            Assert.Equal(10_005, lastPoint.EquityValue, 6);
        }

        [Fact]
        public async Task Sharpe_ReflectsAnOpenPositionSwing()
        {
            var flatBars = new List<Ohlcv>
            {
                Bar(0, 100, 100, 100, 100),
                Bar(1, 100, 100, 100, 100), // entry at 100
                Bar(2, 100, 100, 100, 100),
                Bar(3, 100, 100, 100, 100),
                Bar(4, 110, 110, 110, 110), // both runs exit at 110
            };
            var volatileBars = new List<Ohlcv>
            {
                Bar(0, 100, 100, 100, 100),
                Bar(1, 100, 100, 100, 100), // entry at 100
                Bar(2, 100, 100, 59, 60),   // $4,000 open loss
                Bar(3, 100, 100, 99, 100),  // recovers to entry price
                Bar(4, 110, 110, 110, 110), // same $1,000 realised gain
            };

            var flat = await Run(flatBars, new() { [0] = Position(OrderSide.Buy) });
            var volatileRun = await Run(volatileBars, new() { [0] = Position(OrderSide.Buy) });

            // Both trades enter at 100 and exit at 110 for $1,000. On the flat path the
            // account values are $10,000, $10,000, $10,000, $10,000, $11,000; the other
            // path is $10,000, $10,000, $6,000, $10,000, $11,000. The same dates and final
            // gain leave the open swing as the reason its annualised Sharpe must be lower.
            var flatTrade = Assert.Single(flat.Trades);
            var volatileTrade = Assert.Single(volatileRun.Trades);
            Assert.Equal(flatTrade.EntryPrice, volatileTrade.EntryPrice);
            Assert.Equal(flatTrade.ExitPrice, volatileTrade.ExitPrice);
            Assert.Equal(flatTrade.PnL, volatileTrade.PnL);
            Assert.True(volatileRun.Metrics.SharpeRatio < flat.Metrics.SharpeRatio,
                $"volatile Sharpe {volatileRun.Metrics.SharpeRatio} was not below flat Sharpe {flat.Metrics.SharpeRatio}");
        }
    }
}
