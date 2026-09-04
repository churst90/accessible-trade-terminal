namespace AccessibleTrader.Sdk.Analysis;

public record CandlePatternThresholds
{
    public double DojiBodyMaxPercent { get; init; } = 5.0;
    public double WickMultiplierForHammer { get; init; } = 2.0;
    public double HammerBodyUpperZonePercent { get; init; } = 30.0;
    public double MarubozuBodyMinPercent { get; init; } = 95.0;
    public double SpinningTopBodyMaxPercent { get; init; } = 30.0;
    public double TweezerTolerancePercent { get; init; } = 0.1;
    public double EngulfingBodyOverlapRequired { get; init; } = 1.0;
    public int TrendLookbackBars { get; init; } = 3;

    // ── The three-bar patterns' own numbers ────────────────────────────────────────────────
    //
    // These were hard-coded literals inside SdkCandlePatternAnalyzer while every other number
    // it uses lived here, so "the values by which a pattern is defined" was only half true: a
    // caller could retune a doji and a marubozu and could not touch a morning star or three
    // white soldiers. Candlestick definitions have no standards body — every platform picks its
    // own cut-offs — which is exactly why the ones this app picks should all be visible and all
    // be reachable from one place.

    /// <summary>
    /// Body, as a percentage of the bar's range, at or above which a candle counts as
    /// LARGE-BODIED: the first and last bars of a star, and each of the three soldiers or crows.
    /// </summary>
    public double LargeBodyMinPercent { get; init; } = 50.0;

    /// <summary>
    /// Body, as a percentage of range, below which a candle counts as SMALL-BODIED — the star
    /// itself, the middle bar of a morning or evening star.
    /// </summary>
    public double SmallBodyMaxPercent { get; init; } = 30.0;

    /// <summary>
    /// How much of the first bar's body the star is allowed to overlap, as a fraction of that
    /// body. A textbook star GAPS clear of it (0), but 24/7 markets do not gap, so a small
    /// tolerance keeps the pattern findable on crypto while still requiring the star to sit
    /// clearly outside the body it is reversing. This is a deliberate deviation from the
    /// classical definition, not an approximation of it.
    /// </summary>
    public double StarBodyOverlapAllowed { get; init; } = 0.10;
}
