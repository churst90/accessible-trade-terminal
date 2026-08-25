using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// Optional per-symbol rendering + sonification hints that a market data provider can
    /// declare for single-value analytics metrics. Lets providers describe the *semantic*
    /// properties of their data (range, zones, reference levels, speech/audio profile) so
    /// the chart can render the metric as a proper bounded oscillator instead of a flat,
    /// auto-scaled line with no context.
    ///
    /// ── When to return hints ────────────────────────────────────────────────────────
    /// Analytics providers should return hints for every symbol they expose. OHLCV
    /// providers typically return null because candles already carry their own
    /// semantics via the OHLC buffer. The default interface implementation is null
    /// (no hints), so providers that don't care pay nothing.
    ///
    /// ── How hints are applied ───────────────────────────────────────────────────────
    /// WorkspaceInitializer reads the hints when seeding the Price series on a
    /// SingleValueLine provider load. Each non-null field is copied onto the Price
    /// series config + its line component:
    ///
    ///   • <see cref="RangeMin"/> / <see cref="RangeMax"/> clamp the main pane's
    ///     auto-scale so e.g. FNG always shows 0–100 even if current data is 10–90.
    ///     ViewportRangeCalculator reads these from SeriesConfig and honors them.
    ///
    ///   • <see cref="ReferenceLevels"/> are injected into series.Config.Levels as
    ///     LevelConfig instances. They become horizontal lines in the pane and drive
    ///     OB/OS sonification (AudioZoneHelper matches by level name "Overbought" /
    ///     "Oversold" and applies the ZoneNoiseAmount).
    ///
    ///   • <see cref="DisplayType"/> overrides the component display type. Default is
    ///     Line; bounded metrics (FNG, dominance) should use Oscillator which renders
    ///     identically but triggers oscillator-flavored audio routing.
    ///
    ///   • <see cref="ColorHex"/> overrides the default white line color.
    ///
    ///   • <see cref="SpeechTemplate"/> replaces the default "{name}. {value:F2}."
    ///     template. Use "{zone}" to have AudioZoneHelper describe which zone the
    ///     current value sits in (e.g. "Oversold" for FNG below 25).
    ///
    /// ── Example ─────────────────────────────────────────────────────────────────────
    /// <code>
    /// public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => symbol switch
    /// {
    ///     "FNG_INDEX" => new SymbolRenderHints(
    ///         RangeMin: 0, RangeMax: 100,
    ///         DisplayType: ComponentDisplayType.Oscillator,
    ///         SpeechTemplate: "{name}. {value:F0}. {zone}.",
    ///         ReferenceLevels: new[] {
    ///             new LevelDescriptor("Oversold (Extreme Fear)",  25, "#26A69A", DashStyle.Dash,
    ///                                 ZoneNoiseAmount: 0.3f),
    ///             new LevelDescriptor("Neutral",                  50, "#888888", DashStyle.Dot),
    ///             new LevelDescriptor("Overbought (Extreme Greed)", 75, "#EF5350", DashStyle.Dash,
    ///                                 ZoneNoiseAmount: 0.3f),
    ///         }),
    ///     _ => null
    /// };
    /// </code>
    /// </summary>
    /// <param name="RangeMin">
    /// Hard lower bound for the main pane's auto-scale. When set, overrides data-driven
    /// min. Null = use data min. Use for bounded metrics (FNG=0, dominance=0, funding
    /// can be negative so omit, MVRV=0).
    /// </param>
    /// <param name="RangeMax">
    /// Hard upper bound for the main pane's auto-scale. Null = use data max.
    /// </param>
    /// <param name="ReferenceLevels">
    /// Horizontal reference lines with optional earcon/zone-noise audio behavior.
    /// Levels named "Overbought" / "Oversold" (case-insensitive) trigger
    /// AudioZoneHelper's OB/OS zone audio. Null = no reference levels.
    /// </param>
    /// <param name="DisplayType">
    /// Component display type override. Null = keep the PRICE metadata default (Line).
    /// Use <see cref="ComponentDisplayType.Oscillator"/> for bounded metrics.
    /// </param>
    /// <param name="SpeechTemplate">
    /// Per-value speech template applied to the line component. Null = keep the PRICE
    /// metadata default ("{name}. {type}. {value:F2}."). Supports placeholders
    /// {name}, {value}, {value:F0}, {value:F1}, {value:F2}, {trend}, {zone}.
    /// </param>
    /// <param name="ColorHex">
    /// Line color hex override (e.g. "#FF9800" for orange). Null = keep default white.
    /// </param>
    public record SymbolRenderHints(
        double? RangeMin = null,
        double? RangeMax = null,
        IReadOnlyList<LevelDescriptor>? ReferenceLevels = null,
        ComponentDisplayType? DisplayType = null,
        string? SpeechTemplate = null,
        string? ColorHex = null
    );
}
