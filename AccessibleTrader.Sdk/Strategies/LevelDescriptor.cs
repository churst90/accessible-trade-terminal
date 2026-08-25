namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// Categorises a single price level so condition operators and risk-plan stop/target
/// resolvers can decide which levels to consider for which purpose. A drawn horizontal
/// line might be Support OR Resistance depending on whether price is currently above
/// or below it; level providers should return the kind that best matches the level's
/// current relationship to price (or <see cref="LevelKind.Pivot"/> when ambiguous).
/// </summary>
public enum LevelKind
{
    /// <summary>Price level expected to attract buyers (e.g. swing low, drawn line below price).</summary>
    Support,
    /// <summary>Price level expected to attract sellers (e.g. swing high, drawn line above price).</summary>
    Resistance,
    /// <summary>Pivot point — direction-neutral, used for range trading.</summary>
    Pivot,
    /// <summary>Volume Profile Point of Control — the highest-volume bin price.</summary>
    Poc,
    /// <summary>Volume Profile Value Area High.</summary>
    Vah,
    /// <summary>Volume Profile Value Area Low.</summary>
    Val,
    /// <summary>Volume Profile High-Volume Node — local volume peak.</summary>
    Hvn,
    /// <summary>Volume Profile Low-Volume Node — local volume trough (often acts as a magnet/breakout zone).</summary>
    Lvn,
    /// <summary>VWAP / anchored VWAP line.</summary>
    Vwap,
    /// <summary>Ichimoku Kijun-sen line.</summary>
    Kijun,
    /// <summary>Ichimoku Kumo (cloud) upper boundary — Senkou A or B, whichever is higher.</summary>
    KumoTop,
    /// <summary>Ichimoku Kumo lower boundary — Senkou A or B, whichever is lower.</summary>
    KumoBottom
}

/// <summary>
/// One price level surfaced by an <c>ILevelProvider</c>.
/// Used by:
///   - <c>RiskPlanResolver</c> for the Phase-4 stop/target sources (BelowSupport, BelowKijun,
///     BelowKumo, NextResistance, etc.).
///   - <c>ConditionEvaluator</c> for the Phase-4 leaf operators (PriceRejectsLevel,
///     PriceBreaksLevel).
/// </summary>
/// <param name="Price">The level's price coordinate.</param>
/// <param name="Kind">Categorisation that determines which resolvers / operators consider it.</param>
/// <param name="Strength">
/// Relative importance hint, 0..1. A weekly pivot with three retests outranks a single-touch
/// intraday pivot. Resolvers prefer higher-strength levels when multiple levels qualify.
/// </param>
/// <param name="Source">Free-form label of the originating provider, for speech narration
/// (e.g. "Cipher SR Pivot", "Drawn Horizontal", "Ichimoku Kijun").</param>
public record PriceLevel(
    double Price,
    LevelKind Kind,
    double Strength = 0.5,
    string Source = ""
);
