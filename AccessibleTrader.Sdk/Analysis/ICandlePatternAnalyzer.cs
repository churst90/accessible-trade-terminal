using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Analysis;

public interface ISdkCandlePatternAnalyzer
{
    /// <summary>
    /// Classify <paramref name="current"/>, using the two preceding bars for multi-bar patterns.
    /// </summary>
    /// <param name="recent">
    /// Optional trailing context ending at <paramref name="current"/>, oldest first. Four candle
    /// TYPES — hammer, hanging man, inverted hammer and shooting star — are only distinguishable by
    /// the trend they interrupt: hammer and hanging man are the SAME SHAPE, and so are inverted
    /// hammer and shooting star. One is a bullish reversal and the other bearish, so getting it
    /// wrong inverts the meaning of the announcement.
    /// <para>
    /// Without this, the analyzer can only fall back to the colour of the single preceding candle,
    /// which is not a trend: one green bar inside a sustained decline turns a hammer into a hanging
    /// man. Callers that hold the bar list should always pass it —
    /// <see cref="CandlePatternThresholds.TrendLookbackBars"/> bars plus the current one is enough.
    /// </para>
    /// </param>
    CandleAnalysis Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null,
                           IReadOnlyList<Ohlcv>? recent = null);
}
