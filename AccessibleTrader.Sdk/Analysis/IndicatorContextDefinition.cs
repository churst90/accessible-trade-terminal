namespace AccessibleTrader.Sdk.Analysis;

public record IndicatorContextDefinition
{
    public required string IndicatorCode { get; init; }
    public required string ComponentName { get; init; }
    public double? OverboughtThreshold { get; init; }
    public double? OversoldThreshold { get; init; }
    public string? CrossoverComponentA { get; init; }
    public string? CrossoverComponentB { get; init; }
    public int TrendLookbackBars { get; init; } = 3;
    public required string SpeechTemplate { get; init; }
}
