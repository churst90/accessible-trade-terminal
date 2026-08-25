using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Strategies;

public interface IStrategyBacktester
{
    /// <summary>
    /// Replays <paramref name="data"/> through <paramref name="strategy"/> bar-by-bar.
    /// </summary>
    /// <param name="strategy">The strategy under test.</param>
    /// <param name="data">Historical OHLCV bars (full evaluation window including warmup).</param>
    /// <param name="config">Backtest configuration (capital, commission, slippage, warmup, replay flags).</param>
    /// <param name="state">
    /// The workspace state to use during evaluation. Must be the live state when backtesting any
    /// strategy that reads <c>state.ActiveSeries</c> (e.g. <c>ConfigurableStrategy</c>) — without
    /// the live indicator series, condition evaluation falls through to NaN and the strategy
    /// produces no signals. Pass <c>WorkspaceState.Initial</c> only for self-contained strategies
    /// that compute their own indicators (legacy SMA / RSI / Bollinger built-ins).
    /// </param>
    Task<BacktestResult> RunAsync(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config,
        WorkspaceState? state = null);
}
