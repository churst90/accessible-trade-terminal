using System;
using System.Collections.Immutable;
using AccessibleTrader.Sdk.Analysis;

namespace AccessibleTrader.Sdk.Alerts;

public enum AlertTarget { Candle, Price, Indicator, Poc }

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

    /// <summary>
    /// When true this alert pierces the Shift+F2 / Shift+F3 ambient mutes —
    /// for the handful of alerts that must never be missed (margin-call price
    /// levels, a stop-adjacent warning). Default false: alerts are ambient.
    /// </summary>
    public bool BreakThroughMutes { get; init; } = false;
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(30);

    // ── Symbol scoping (Part A) ──────────────────────────────────────────────
    // All nullable; null = "any / current chart" for back-compat. Existing
    // alerts.json entries deserialize these as null and evaluate exactly as
    // before. When Symbol is set, AlertOrchestrator only evaluates the alert
    // while the on-screen chart's SymbolDisplayName matches (case-insensitive),
    // so a "BTC" alert no longer fires against KAS when you switch tabs.
    /// <summary>Display-name of the symbol this alert is scoped to (e.g. "BTC/USD"); null = any / current chart.</summary>
    public string? Symbol { get; init; }
    /// <summary>Optional provider scope (informational / future multi-workspace routing); null = any.</summary>
    public string? Provider { get; init; }
    /// <summary>Optional timeframe scope (informational / future multi-workspace routing); null = any.</summary>
    public string? Timeframe { get; init; }

    // ── Per-asset webhook routing (Part B) ───────────────────────────────────
    /// <summary>Name of the configured webhook (see <c>alerts.webhooks</c>) this alert
    /// posts to; null = do not post to any webhook. Email/Telegram fan-out is unaffected.</summary>
    public string? WebhookTarget { get; init; }

    // ── Advanced condition tree (Part D) ──────────────────────────────────────
    /// <summary>
    /// Optional custom condition tree (the strategy composer's AND/OR/NOT/Score
    /// tree). When set it REPLACES the simple Target/Condition/Threshold rule:
    /// the alert fires on the bar where the whole tree first evaluates true
    /// (edge-triggered; <see cref="RepeatIfStillActive"/> + <see cref="Cooldown"/>
    /// re-arm it while it stays true). Leaves reference indicators by code, so
    /// the referenced indicators must be on the chart (or on the background
    /// tab's series) to evaluate. Serialized with System.Text.Json ($kind
    /// polymorphism, same as strategy specs); the Newtonsoft alerts.json path
    /// bridges via ConditionNodeNewtonsoftBridge.
    /// </summary>
    public AccessibleTrader.Sdk.Strategies.ConditionNode? ConditionTree { get; init; }
}
