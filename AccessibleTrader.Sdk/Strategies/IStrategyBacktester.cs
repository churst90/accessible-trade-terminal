using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Strategies;

public interface IStrategyBacktester
{
    Task<BacktestResult> RunAsync(
        ITradingStrategy strategy,
        IReadOnlyList<Ohlcv> data,
        BacktestConfig config);
}
