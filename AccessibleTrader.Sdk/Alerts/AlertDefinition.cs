using System;
using System.Collections.Immutable;
using AccessibleTrader.Sdk.Analysis;

namespace AccessibleTrader.Sdk.Alerts;

public enum AlertTarget { Candle, Price, Indicator }

public enum AlertCondition
{
    CrossesAbove, CrossesBelow, EntersZone, ExitsZone,
    ChangesDirection, PatternDetected, TrendChange
}

public enum AlertDelivery { Speech, Earcon, Both }

public enum AlertZone
{
    Overbought, Oversold, UpperBand, LowerBand,
    ValueArea, AbovePOC, BelowPOC
}

public record AlertDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required AlertTarget Target { get; init; }
    public string? IndicatorCode { get; init; }
    public string? ComponentName { get; init; }
    public required AlertCondition Condition { get; init; }
    public double? Threshold { get; init; }
    public AlertZone? Zone { get; init; }
    public CandlePattern? Pattern { get; init; }
    public required AlertDelivery Delivery { get; init; }
    public bool IsActive { get; init; } = true;
    public bool RepeatIfStillActive { get; init; } = false;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(30);
}
