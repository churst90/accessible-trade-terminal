using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Computes the recommended warmup-bar count for a strategy spec by walking its
    /// condition tree, collecting referenced indicator codes, and asking each
    /// <c>IIndicatorProvider</c> for its stability window. The maximum across all referenced
    /// indicators (with a floor) is the value the backtester should set
    /// <see cref="BacktestConfig.WarmupBars"/> to so that signals during indicator
    /// settling are silently dropped (matches Session A's warmup gate semantics).
    ///
    /// Strategies that don't reference indicators (or whose indicator providers don't
    /// implement <c>GetStabilityWindow</c> meaningfully) get the floor value.
    /// </summary>
    public interface IBacktestWarmupAnalyzer
    {
        int RecommendedWarmup(StrategySpec spec, int floor = 50);
        IReadOnlyList<string> ReferencedIndicators(StrategySpec spec);
    }
}
