namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// Categorises a single signal source so the condition builder UI can pick the right
/// operator dropdown and so <c>ConditionEvaluator</c> can apply the right comparison
/// semantics. The catalog is built at startup by walking every <c>IIndicatorProvider</c>
/// and emitting one descriptor per <c>IndicatorComponentMetadata</c>.
/// </summary>
public enum SignalKind
{
    /// <summary>Marker dot/diamond/cross — fires only on bars where its value is non-NaN.
    /// Examples: Cipher A buy signal, Cipher B triple confluence dot.</summary>
    MarkerFire,
    /// <summary>Continuous oscillator (RSI, MACD histogram, WT). Compared against numeric thresholds.</summary>
    Oscillator,
    /// <summary>Continuous price-space line (EMA, BB middle, Kijun). Compared against price or other lines.</summary>
    Line,
    /// <summary>Cloud fill region (Ichimoku Kumo, EMA fill). Conditions test entry/exit/inside.</summary>
    Cloud,
    /// <summary>Reference level (Cipher SR pivot, drawn horizontal line, VWAP). Tests price reactions.</summary>
    Level,
    /// <summary>Detected candlestick or chart pattern (engulfing, hammer, etc.).</summary>
    Pattern
}

/// <summary>
/// One enumerable signal source the user can select in the strategy builder.
/// Catalog entries are created from <c>IndicatorComponentMetadata</c> at startup
/// and refreshed whenever a new indicator provider is registered.
/// </summary>
/// <param name="Id">Stable ID for persistence: <c>"{indicatorCode}.{componentName}"</c>.</param>
/// <param name="IndicatorCode">The owning indicator's registry code (e.g. "CIPHER_A").</param>
/// <param name="ComponentName">The component within the indicator (e.g. "Buy Signal").</param>
/// <param name="Kind">Determines which operators are valid for this signal source.</param>
/// <param name="DisplayLabel">Human-readable label spoken by TTS and shown in UI.</param>
/// <param name="MinValue">Lower bound of typical range — UI sliders, defaults for thresholds.</param>
/// <param name="MaxValue">Upper bound of typical range. NaN if unbounded (price-space lines).</param>
/// <param name="Unit">Optional unit hint ("%", "USD", "ratio") for speech.</param>
/// <param name="Causality">
/// What the component declared about reading later bars. Only
/// <see cref="AccessibleTrader.Sdk.Models.ComponentCausality.Causal"/> descriptors reach
/// <c>ISignalCatalog.All</c>; the rest are kept resolvable by ID so a strategy saved before the
/// contract existed reports "this leaf reads the future" instead of quietly evaluating false.
/// </param>
public record SignalDescriptor(
    string Id,
    string IndicatorCode,
    string ComponentName,
    SignalKind Kind,
    string DisplayLabel,
    double MinValue = double.NaN,
    double MaxValue = double.NaN,
    string? Unit = null,
    AccessibleTrader.Sdk.Models.ComponentCausality Causality = AccessibleTrader.Sdk.Models.ComponentCausality.Causal
);
