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
}
