namespace AccessibleTrader.Sdk.Analysis;

public enum TrendDirection { Rising, Falling, Flat }

public enum ZoneStatus
{
    Normal, Overbought, Oversold, AtUpperBand, AtLowerBand,
    AbovePOC, BelowPOC, CrossingUp, CrossingDown
}

public enum CrossoverStatus { None, BullishCrossover, BearishCrossover }

public record IndicatorContext
{
    public required string IndicatorCode { get; init; }
    public required string ComponentName { get; init; }
    public required double CurrentValue { get; init; }
    public double? PreviousValue { get; init; }
    public required TrendDirection Trend { get; init; }
    public required int TrendBars { get; init; }
    public required ZoneStatus Zone { get; init; }
    public required CrossoverStatus Crossover { get; init; }
    public required string NarrativeHint { get; init; }
}
