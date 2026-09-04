namespace AccessibleTrader.Sdk.Analysis;

public enum CandleDirection { Bullish, Bearish, Neutral }

public enum CandleType
{
    Normal, Doji, DragonflyDoji, GravestoneDoji, LongLeggedDoji,
    Hammer, InvertedHammer, ShootingStar, HangingMan,
    MarubozuBullish, MarubozuBearish, SpinningTop
}

/// <summary>
/// Every multi-bar candle pattern the terminal recognises.
///
/// <para>
/// APPEND ONLY, AND NEVER REORDER. <c>AlertDefinition.Pattern</c> is a persisted
/// <c>CandlePattern?</c> and alerts.json is written by Newtonsoft — which, left to itself, stores
/// an enum as its ORDINAL. Removing or reordering a member silently rebinds every saved alert
/// that named a later one onto whatever inherited its number, on a file the user cannot read.
/// The same trap was found and fixed in the shortcut profiles on 2026-09-04; alerts are now
/// written BY NAME for the same reason, but old ordinal files still load, so the numbering here
/// remains load-bearing for as long as any of them exist.
/// </para>
/// </summary>
public enum CandlePattern
{
    None,

    // ── Two-bar ────────────────────────────────────────────────────────────────────────────
    BullishEngulfing, BearishEngulfing,
    BullishHarami, BearishHarami,
    PiercingLine, DarkCloudCover,
    TweezerBottom, TweezerTop,

    // ── Three-bar ──────────────────────────────────────────────────────────────────────────
    MorningStar, EveningStar,
    ThreeWhiteSoldiers, ThreeBlackCrows,

    // Added 2026-09-04. The three-bar set was four patterns of the ten-odd in common use; these
    // are the rest of it, plus the four- and five-bar shapes the analyser had no reach for.
    ThreeInsideUp, ThreeInsideDown,
    ThreeOutsideUp, ThreeOutsideDown,
    MorningDojiStar, EveningDojiStar,
    AbandonedBabyBullish, AbandonedBabyBearish,

    // ── Four-bar ───────────────────────────────────────────────────────────────────────────
    ThreeLineStrikeBullish, ThreeLineStrikeBearish,

    // ── Five-bar ───────────────────────────────────────────────────────────────────────────
    RisingThreeMethods, FallingThreeMethods
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
