using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Analysis;

public interface ISdkCandlePatternAnalyzer
{
    CandleAnalysis Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null);
}
