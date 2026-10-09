using AccessibleTrader.Sdk.Analysis;

namespace AccessibleTrader.Sdk.Alerts;

public enum AlertTarget { Candle, Price, Indicator, Poc }

/// <summary>
/// What an alert waits for. PERSISTED: alerts.json writes these by name, but files written
/// before the name converter carry the ordinal, so new members go on the END and existing
/// ones are never reordered or removed.
/// </summary>
public enum AlertCondition
{
    CrossesAbove, CrossesBelow, EntersZone, ExitsZone,
    ChangesDirection, PatternDetected, TrendChange,

    /// <summary>
    /// The level is REACHED, from either side. For price and candles: the newest bar's range
    /// contains it (Low &lt;= level &lt;= High) — a wick through the level counts even when the
    /// close never gets there, which is what "price touched 64,000" means to a trader and what
    /// neither crossing could say. For an indicator: the value reaches or crosses it in either
    /// direction. Once per bar, like every simple condition.
    /// </summary>
    Touches,
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

    /// <summary>
    /// Which instance of <see cref="IndicatorCode"/> the alert reads, when the chart has more
    /// than one — an SMA 20 and an SMA 50 share the code "Sma", and an alert that names only
    /// the code reads whichever comes first. Null (every alert written before this existed)
    /// keeps that first-by-code rule; so does an id no longer on the chart.
    /// </summary>
    public string? SeriesId { get; init; }
    public required AlertCondition Condition { get; init; }
    public double? Threshold { get; init; }

    // ── A LINE instead of a number ───────────────────────────────────────────
    // Cody, 2026-10-09: "what if I want to know if price touches the 50 week". The comparison
    // value can be one of the chart's indicator lines rather than a fixed Threshold; when
    // LineIndicatorCode is set, Threshold is ignored and the line's value on the bar being
    // evaluated is the level. All three nullable and additive: alerts.json files written before
    // they existed load with them null and compare against Threshold exactly as before.
    /// <summary>Indicator code of the line compared against (e.g. "Sma"); null = compare against <see cref="Threshold"/>.</summary>
    public string? LineIndicatorCode { get; init; }
    /// <summary>The line's component (e.g. "Sma", "UpperBand").</summary>
    public string? LineComponentName { get; init; }
    /// <summary>Which instance of <see cref="LineIndicatorCode"/> — see <see cref="SeriesId"/>.</summary>
    public string? LineSeriesId { get; init; }

    /// <summary>Whether the comparison value is a chart line rather than a number. A method,
    /// not a property, so neither serializer writes it into alerts.json.</summary>
    public bool ComparesToLine() => !string.IsNullOrWhiteSpace(LineIndicatorCode) && !string.IsNullOrWhiteSpace(LineComponentName);
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
    /// <summary>
    /// Market sub-type of the chart the alert was created on ("Spot", "Futures", …
    /// — the <c>ChartIdentity.Market</c> string). The background monitors put this
    /// in their <c>MarketDataRequest</c>; before it existed they hardcoded "Spot",
    /// so an alert on a Futures/Derivatives chart silently watched the wrong
    /// market. Null (every pre-existing alerts.json entry) falls back to "Spot",
    /// which is what those alerts were — wrongly but consistently — getting.
    /// </summary>
    public string? Market { get; init; }

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
