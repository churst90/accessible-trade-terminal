namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// How a strategy decides where to place its protective stop-loss when a setup fires.
/// Sources marked [Phase 4] are reserved for the support/resistance + volume profile
/// integration session — they parse and serialize correctly today but the resolver
/// returns null for them, surfacing a clear "not yet implemented" message.
/// </summary>
public enum StopSourceKind
{
    /// <summary>Stop = entry × (1 - PercentValue / 100) for longs (mirror for shorts).</summary>
    PercentOfPrice,
    /// <summary>Stop = entry - (ATR(period) × Multiple) for longs (mirror for shorts).</summary>
    AtrMultiple,
    /// <summary>Stop = lowest low (longs) / highest high (shorts) over the last LookbackBars bars.</summary>
    BelowSwingLow,
    /// <summary>User-provided fixed stop price (rare — usually only via UI override).</summary>
    Fixed,
    /// <summary>[Phase 4] Stop placed just beyond the nearest Cipher SR pivot below entry (longs).</summary>
    BelowSupport,
    /// <summary>[Phase 4] Stop placed below the Ichimoku Kijun-sen line (longs).</summary>
    BelowKijun,
    /// <summary>[Phase 4] Stop placed below the Ichimoku Kumo cloud (longs).</summary>
    BelowKumo,
    /// <summary>[Phase 4] Stop placed below the nearest Low-Volume Node from VPVR (longs).</summary>
    BelowLvn,
    /// <summary>Stop placed at an indicator component's latest value (e.g. EMA(20), Kijun-sen).
    /// Longs: stop goes BELOW the component value (minus buffer). Shorts: mirror ABOVE.
    /// Requires <see cref="StopSource.IndicatorCode"/> and <see cref="StopSource.ComponentName"/>.</summary>
    BelowComponent
}

/// <param name="Kind">Which placement algorithm to use.</param>
/// <param name="PercentValue">Used by PercentOfPrice (e.g. 0.5 = 0.5%).</param>
/// <param name="AtrPeriod">Used by AtrMultiple (typical: 14).</param>
/// <param name="AtrMultiple">Used by AtrMultiple (typical: 1.5).</param>
/// <param name="LookbackBars">Used by BelowSwingLow / BelowSupport-window (typical: 20).</param>
/// <param name="FixedPrice">Used by Fixed.</param>
/// <param name="BufferTicks">Optional extra room beyond the resolved level (in price units, not ticks).
/// E.g. 0.0002 places the stop 2 pips beyond the swing low instead of exactly on it.</param>
public record StopSource(
    StopSourceKind Kind,
    double PercentValue = 0.5,
    int AtrPeriod = 14,
    double AtrMultiple = 1.5,
    int LookbackBars = 20,
    double FixedPrice = 0.0,
    double BufferTicks = 0.0,
    // Indicator code for BelowComponent (e.g. "SPIDER_LINES", "ICHIMOKU", "EMA").
    string IndicatorCode = "",
    // Component name within the indicator (e.g. "EMA 20", "Kijun-sen").
    string ComponentName = ""
);

/// <summary>
/// How a strategy decides where its take-profit targets sit. The TP ladder is a list
/// of these — TP1, TP2, TP3 — each independently configurable.
/// </summary>
public enum TargetSourceKind
{
    /// <summary>Target = entry + (R × Multiple) for longs, where R = entry - stop.</summary>
    RiskRewardMultiple,
    /// <summary>Target = entry × (1 + PercentValue / 100) for longs.</summary>
    PercentOfPrice,
    /// <summary>Fixed target price.</summary>
    Fixed,
    /// <summary>[Phase 4] Next Cipher SR resistance pivot above entry (longs).</summary>
    NextResistance,
    /// <summary>[Phase 4] Next High-Volume Node from VPVR above entry (longs).</summary>
    NextHvn,
    /// <summary>[Phase 4] Volume profile POC (point of control).</summary>
    Poc,
    /// <summary>[Phase 4] Volume profile VAH (Value Area High).</summary>
    Vah,
    /// <summary>[Phase 4] Fibonacci extension level computed from recent swing.</summary>
    FibExtension,
    /// <summary>Target at an indicator component's latest value (e.g. "exit at EMA 50").
    /// Requires <see cref="TpLadderRung.IndicatorCode"/> and <see cref="TpLadderRung.ComponentName"/>.</summary>
    AtComponent
}

/// <summary>
/// One rung in a take-profit ladder. The default 3-rung ladder closes 1/3 of the
/// position at each rung; after TP1 fills the stop typically moves to breakeven.
/// </summary>
/// <param name="Kind">Target placement algorithm.</param>
/// <param name="Multiple">RR multiple for RiskRewardMultiple (e.g. 1.0, 2.0, 3.0).</param>
/// <param name="PercentValue">Percent for PercentOfPrice.</param>
/// <param name="FixedPrice">Price for Fixed.</param>
/// <param name="FibLevel">Fib extension level (1.272, 1.618, 2.0).</param>
/// <param name="ClosePortion">Fraction of position closed at this rung (0..1, ladder should sum to 1.0).</param>
public record TpLadderRung(
    TargetSourceKind Kind,
    double Multiple = 2.0,
    double PercentValue = 1.0,
    double FixedPrice = 0.0,
    double FibLevel = 1.618,
    double ClosePortion = 0.34,
    // Indicator code for AtComponent (e.g. "SPIDER_LINES", "ICHIMOKU").
    string IndicatorCode = "",
    // Component name within the indicator (e.g. "EMA 50", "Senkou Span A").
    string ComponentName = ""
);

/// <summary>
/// Once TP1 fills, optionally move the stop. Trailing strategies are richer than this
/// enum allows but the common cases (breakeven, ATR trail) are enough for Session A.
/// </summary>
public enum StopAdjustOnTp1
{
    /// <summary>Leave the stop where it was originally placed.</summary>
    None,
    /// <summary>Move the stop to entry price (lock in zero risk on the runner).</summary>
    MoveToBreakeven,
    /// <summary>Begin trailing the stop by ATR × multiple.</summary>
    TrailByAtr
}

/// <summary>
/// Position sizing rule. Default: risk a fixed percent of equity per trade — qty is
/// derived from <c>(equity × RiskPercent) / (entry - stop)</c>. This is the *only*
/// sizing model that scales sensibly across different stop distances and account sizes.
/// </summary>
public enum SizingMode
{
    /// <summary>Risk a fixed percent of equity per trade. Stop distance determines qty.</summary>
    FixedRiskPercent,
    /// <summary>Always trade a fixed quantity (legacy behaviour for the built-in strategies).</summary>
    FixedQuantity,
    /// <summary>Risk a fixed currency amount per trade.</summary>
    FixedRiskCash
}

/// <param name="Mode">Which sizing rule to apply.</param>
/// <param name="RiskPercent">For FixedRiskPercent — fraction of equity, e.g. 0.005 = 0.5%.</param>
/// <param name="FixedQuantity">For FixedQuantity.</param>
/// <param name="RiskCash">For FixedRiskCash — currency amount.</param>
public record PositionSizing(
    SizingMode Mode = SizingMode.FixedRiskPercent,
    double RiskPercent = 0.005,
    double FixedQuantity = 1.0,
    double RiskCash = 100.0
);

/// <summary>
/// When a setup's conditions are met, what triggers actual order placement.
/// Currently only <see cref="EntryTriggerKind.Immediate"/> is honoured by the resolver;
/// the others are reserved for Session B (entry-armed watchers).
/// </summary>
public enum EntryTriggerKind
{
    /// <summary>Place order immediately when conditions fire.</summary>
    Immediate,
    /// <summary>Wait for price to pull back to a specified level before entering.</summary>
    OnPullbackToLevel,
    /// <summary>Wait for price to break above/below a specified level before entering.</summary>
    OnBreakoutOf,
    /// <summary>Wait for the next N candles to close in the direction of the setup.</summary>
    OnNextNCandleClose
}

/// <param name="Kind">Trigger algorithm.</param>
/// <param name="LevelPrice">For OnPullbackToLevel / OnBreakoutOf.</param>
/// <param name="NCandles">For OnNextNCandleClose.</param>
public record EntryTrigger(
    EntryTriggerKind Kind = EntryTriggerKind.Immediate,
    double LevelPrice = 0.0,
    int NCandles = 1
);

/// <summary>
/// Combined risk specification for a strategy — stop, TP ladder, sizing, entry timing,
/// and the minimum acceptable reward/risk ratio that gates whether the setup ever
/// becomes a fired alert. A setup that doesn't clear <see cref="MinRewardRiskRatio"/>
/// based on its first TP is silently dropped — the bell never rings.
/// </summary>
public record RiskPlan(
    StopSource Stop,
    IReadOnlyList<TpLadderRung> TpLadder,
    PositionSizing Sizing,
    EntryTrigger Entry,
    double MinRewardRiskRatio = 1.5,
    StopAdjustOnTp1 StopAdjust = StopAdjustOnTp1.MoveToBreakeven,
    double NotionalEquity = 10000.0
);

/// <summary>
/// Output of <c>IRiskPlanResolver</c> — concrete prices and quantity for a single setup.
/// </summary>
/// <param name="EntryPrice">Resolved entry price (last close for Immediate triggers).</param>
/// <param name="StopPrice">Resolved stop price.</param>
/// <param name="TpPrices">TP ladder prices in order (TP1, TP2, TP3, ...).</param>
/// <param name="ClosePortions">Mirror of TpPrices — fraction of position closed at each rung.</param>
/// <param name="Quantity">Position size derived from sizing rule + stop distance + equity.</param>
/// <param name="RewardRiskRatio">First-target R:R for the resolved plan.</param>
/// <param name="RiskCash">Currency at risk if the stop is hit.</param>
/// <param name="Notes">Free-form notes — e.g. "stop placed below 20-bar swing low at 1.0795".</param>
public record ResolvedRiskPlan(
    double EntryPrice,
    double StopPrice,
    IReadOnlyList<double> TpPrices,
    IReadOnlyList<double> ClosePortions,
    double Quantity,
    double RewardRiskRatio,
    double RiskCash,
    string Notes
);
