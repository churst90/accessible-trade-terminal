namespace AccessibleTrader.Sdk.Analysis;

public enum CandleDirection { Bullish, Bearish, Neutral }

public enum CandleType
{
    Normal, Doji, DragonflyDoji, GravestoneDoji, LongLeggedDoji,
    Hammer, InvertedHammer, ShootingStar, HangingMan,
    MarubozuBullish, MarubozuBearish, SpinningTop
}

public enum CandlePattern
{
    None,
    BullishEngulfing, BearishEngulfing,
    BullishHarami, BearishHarami,
    PiercingLine, DarkCloudCover,
    TweezerBottom, TweezerTop,
    MorningStar, EveningStar,
    ThreeWhiteSoldiers, ThreeBlackCrows
}

public record CandleAnalysis
{
    public required CandleDirection Direction { get; init; }
    public required CandleType Type { get; init; }
    public required CandlePattern Pattern { get; init; }
    public required int PatternBarCount { get; init; }
    public required double BodyPercent { get; init; }
    public required double UpperWickPercent { get; init; }
    public required double LowerWickPercent { get; init; }
    public required double ChangePercent { get; init; }
    public required bool IsReversal { get; init; }
    public required bool IsContinuation { get; init; }
}
