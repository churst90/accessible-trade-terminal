using System;
using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Metadata for a single output component of an indicator.
    /// Providers may populate the optional Default* and Audio* fields to declare their own
    /// preferred visual and sonic appearance. When set, these values are applied directly
    /// by <see cref="AccessibleTrader.Core.Services.IndicatorModelFactory"/> instead of
    /// falling back to the role-based defaults in <c>IStylingService</c>.
    /// All optional fields are nullable — null means "use the global role/type-based default".
    /// </summary>
    public class IndicatorComponentMetadata
    {
        public string Name { get; set; } = string.Empty;
        /// <summary>Human-readable label spoken/displayed in the UI. Defaults to Name when null.</summary>
        public string? DisplayName { get; set; }
        public ComponentRole Role { get; set; } = ComponentRole.None;
        public ComponentDisplayType DisplayType { get; set; } = ComponentDisplayType.Line;
        public string? DataMapping { get; set; }
        /// <summary>For Cloud display type: name of the upper boundary component within the same series.</summary>
        public string? UpperComponentName { get; set; }
        /// <summary>For Cloud display type: name of the lower boundary component within the same series.</summary>
        public string? LowerComponentName { get; set; }
        /// <summary>Whether this component is visible by default. Defaults to true.</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Name of the sub-pane strip within the parent indicator pane that this component belongs to.
        /// Null = main area of the parent pane (default, existing behaviour for all current indicators).
        /// Must match a sub-pane key that can be discovered from sibling components in the same series.
        /// </summary>
        public string? SubPaneName { get; set; }

        /// <summary>
        /// Default height of the sub-pane as a fraction of the parent indicator pane height (0.0–1.0).
        /// Only meaningful when <see cref="SubPaneName"/> is set. Clamped to [0.05, 0.40] at render time.
        /// Example: 0.22 = sub-pane occupies 22% of the parent pane; main area gets the remaining 78%.
        /// </summary>
        public float? SubPaneHeightRatio { get; set; }

        // ── Visual hints (null = use role/type-based StylingService default) ──────────────

        /// <summary>Primary (bullish/positive) color hex. Overrides role-based color default when set.</summary>
        public string? DefaultColorHex { get; set; }
        /// <summary>Secondary (bearish/negative) color hex. Overrides role-based secondary color default when set.</summary>
        public string? DefaultColorHexSecondary { get; set; }
        /// <summary>Stroke/shape size in logical pixels. Overrides display-type thickness default when set.</summary>
        public float? DefaultThickness { get; set; }
        /// <summary>Zero-crossing threshold for directional bar/histogram coloring. Overrides global default (0) when set. Example: 50 for MFI.</summary>
        public double? ColorBaseline { get; set; }
        /// <summary>Dash pattern for line components. Overrides Solid default when set.</summary>
        public DashStyle? DefaultDashStyle { get; set; }
        /// <summary>Color source logic (Value sign vs PriceAction direction). Overrides role-based default when set.</summary>
        public ColorSource? DefaultColorSource { get; set; }

        // ── Audio hints (null = use display-type SonificationProfileProvider default) ─────

        /// <summary>Base waveform type: "sine", "square", "triangle", "sawtooth", "noise". Overrides profile default when set.</summary>
        public string? DefaultWaveform { get; set; }
        /// <summary>Envelope type: "Sustain" (gliding tone) or "Ping" (transient click). Overrides profile default when set.</summary>
        public string? DefaultEnvelopeType { get; set; }
        /// <summary>Pink-noise blend amount [0,1]. 0 = pure waveform. Overrides 0 default when set.</summary>
        public float? DefaultNoiseAmount { get; set; }
        /// <summary>How component amplitude maps to data values. Overrides profile default when set.</summary>
        public AmplitudeMapping? DefaultAmplitudeMapping { get; set; }
        /// <summary>How pitch frequency maps to data values. Overrides profile default when set.</summary>
        public PitchMapping? DefaultPitchMapping { get; set; }
        /// <summary>Base frequency in Hz. Overrides profile default (440 Hz) when set.</summary>
        public double? DefaultBaseFrequency { get; set; }
        /// <summary>
        /// Frequency multiplier applied to the base oscillator pitch.
        /// Null = use profile default (1.0). Example: 1.3 = raise pitch 30% for a "leading" feel.
        /// </summary>
        public double? DefaultFreqMultiplier { get; set; }
        /// <summary>
        /// Waveform used when the component value is above its reference level (typically 0).
        /// Null = use profile default. Overrides <see cref="DefaultWaveform"/> for above-zero region.
        /// Example: "triangle" above / "sawtooth" below for the WT1 cutter character.
        /// </summary>
        public string? DefaultAboveWaveform { get; set; }
        /// <summary>
        /// Waveform used when the component value is below its reference level (typically 0).
        /// Null = use profile default. Overrides <see cref="DefaultWaveform"/> for below-zero region.
        /// </summary>
        public string? DefaultBelowWaveform { get; set; }
        /// <summary>
        /// Bullish (positive/rising) pitch frequency in Hz for Direction pitch mapping.
        /// Null = use ComponentConfig default (440 Hz). Meaningful only when DefaultPitchMapping = Direction.
        /// </summary>
        public double? DefaultBullishFrequency { get; set; }
        /// <summary>
        /// Bearish (negative/falling) pitch frequency in Hz for Direction pitch mapping.
        /// Null = use ComponentConfig default (220 Hz). Meaningful only when DefaultPitchMapping = Direction.
        /// </summary>
        public double? DefaultBearishFrequency { get; set; }

        // ── Bell synthesis hints (null = use SoundPatchRegistry defaults) ──────

        /// <summary>
        /// Default bell decay in milliseconds for Ping-envelope voices.
        /// Applied as Layer 1 default; overridden by ComponentConfig.DecayMs (user edit) or patch DefaultDecayMs.
        /// Null = use patch DefaultDecayMs or existing envelope defaults.
        /// </summary>
        public int? DefaultDecayMs { get; set; }

        /// <summary>
        /// Playback layer controlling voice volume scaling during multi-series playback.
        /// Null = use Midground default.
        /// </summary>
        public PlaybackLayer? DefaultPlaybackLayer { get; set; }

        /// <summary>
        /// Optional ID of a SoundPatch to assign on component creation.
        /// Applied in <see cref="AccessibleTrader.Core.Services.IndicatorModelFactory.CreateComponentConfigFromMeta"/>.
        /// Null = no patch assignment (component uses per-field waveform/envelope settings).
        /// </summary>
        public string? DefaultSoundPatchId { get; set; }

        /// <summary>
        /// When true, navigation speech produces qualitative momentum language
        /// ("strong bullish momentum", "neutral momentum", etc.) instead of a raw value.
        /// Intended for gradient-dot components whose numeric value is an oscillator level
        /// that is more meaningfully expressed as a direction + intensity description.
        /// </summary>
        public bool UsesGradientSpeech { get; set; } = false;

        /// <summary>
        /// When true, this component is a carry-forward zone line (e.g. Resistance Zone, Support Zone).
        /// NavigationFeedbackManager will play a quiet proximity tone on audio slot 2 when the cursor
        /// bar's price range overlaps the zone value (within 0.5% tolerance).
        /// Propagated to <see cref="AccessibleTrader.Sdk.Models.ComponentConfig.IsZoneLine"/>.
        /// </summary>
        public bool DefaultIsZoneLine { get; set; } = false;

        /// <summary>
        /// When set on a marker-type component, used instead of the generic speech template
        /// when the component has a non-NaN value at the current bar (signal IS present).
        /// When the value IS NaN, <see cref="SpeechFormatter"/> returns an empty string (no speech).
        /// Supports <c>{price}</c> (formats the signal value as an integer price) and <c>{name}</c> tokens.
        /// Propagated to <see cref="AccessibleTrader.Sdk.Models.ComponentConfig.SignalSpeechTemplate"/>.
        /// </summary>
        public string? DefaultSignalSpeechTemplate { get; set; }

        /// <summary>
        /// Speech template for continuous line/oscillator components.
        /// Supports {value}, {value:F1}, {value:F2}, {name}, {date} tokens.
        /// When null, SpeechFormatter uses its generic fallback.
        /// Separate from DefaultSignalSpeechTemplate, which is for marker/dot signal events only.
        /// </summary>
        public string? SpeechTemplate { get; set; }

        /// <summary>
        /// When true, a transient click earcon fires each time the component value crosses its ReferenceLevel.
        /// Overrides the display-type profile default when set.
        /// Null = use the sonification profile's TriggerBoundaryClick value.
        /// </summary>
        public bool? DefaultTriggerBoundaryClick { get; set; }

        /// <summary>
        /// Explicit reference level for this component (e.g. -80 for Cipher B Money Flow Histogram).
        /// When set, takes priority over the StylingService hard-code and the Oscillator/ZeroArea 0.0 fallback.
        /// Null = use the existing chain (StylingService → type default → null).
        /// </summary>
        public double? DefaultReferenceLevel { get; set; }

        /// <summary>
        /// Maximum expected deviation from ReferenceLevel, used as the denominator when
        /// <see cref="AccessibleTrader.Sdk.Models.AmplitudeMapping.ReferenceDeviation"/> is active.
        /// Without this, the denominator is computed from the viewport range, which causes
        /// components whose value range is much smaller than the pane range to sound nearly silent.
        /// Example: Money Flow has values in −100..−60 anchored at −80, so max deviation is 20.
        ///   Set DefaultDeviationNorm = 20.0 → deviation 20/20 = full volume at extremes.
        /// Null = compute denominator from viewport range (default, correct for full-range oscillators).
        /// </summary>
        public double? DefaultDeviationNorm { get; set; }

        /// <summary>
        /// Whether this component renders as an area fill (shaded region between the line and its reference level).
        /// Null = defer to StylingService role/type default.
        /// </summary>
        public bool? DefaultIsAreaFill { get; set; }

        /// <summary>
        /// Whether this component's color flips based on value polarity (above/below reference level).
        /// Null = defer to StylingService role/type default.
        /// </summary>
        public bool? DefaultUsePolarityColoring { get; set; }

        /// <summary>
        /// Per-bar conditional color rules. Applied in order — first matching rule wins.
        /// Null or empty = no conditional coloring (use DefaultColorHex for all bars).
        /// When set, these rules are copied into <see cref="AccessibleTrader.Sdk.Models.ComponentConfig.ColorRules"/>
        /// by <see cref="AccessibleTrader.Core.Services.IndicatorModelFactory"/> at component creation time.
        /// </summary>
        public List<ColorRule>? DefaultColorRules { get; init; }

        /// <summary>
        /// Names of the series-level reference lines (LevelConfig.Name) that this component
        /// subscribes to for OB/OS zone noise and boundary earcons.
        /// Null = subscribe to all levels (default for most oscillators).
        /// Empty list = subscribe to none — no zone noise or crossing earcons for this component.
        /// Use this to prevent histogram or sub-range components from inheriting the parent
        /// series' OB/OS levels when their value range doesn't map to those thresholds.
        /// </summary>
        public IReadOnlyList<string>? SubscribedLevelNames { get; init; }
    }

    public class IndicatorMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty; // Method name in Skender

        /// <summary>
        /// When true, RecalculateLastAsync will run a full recalculation instead of the
        /// scalar incremental path. Required for pivot-based indicators that write values
        /// to historical bar indices (e.g. Cipher SR, divergence detectors).
        /// </summary>
        public bool RequiresFullRecalcOnTick { get; set; } = false;
        public string Category { get; set; } = "General";
        public string Description { get; set; } = string.Empty;
        public List<IndicatorParameterMetadata> Parameters { get; set; } = new();
        public List<IndicatorComponentMetadata> Components { get; set; } = new();
        /// <summary>
        /// Default cloud fills created with the series (separate from Components so they are
        /// visual-only and never appear in navigation or sonification).
        /// </summary>
        public List<CloudFillConfig> DefaultCloudFills { get; set; } = new();
        /// <summary>
        /// Default zone bands created with the series — thin horizontal shaded regions around
        /// a carry-forward level value (e.g. S/R zones). Visual-only, not navigable or audible.
        /// </summary>
        public List<ZoneBandConfig> DefaultZoneBands { get; set; } = new();
        public string DefaultPane { get; set; } = "Main";

        public override string ToString() => Name;
    }

    public class IndicatorParameterMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Type DataType { get; set; } = typeof(int);
        public object DefaultValue { get; set; } = 0;

        /// <summary>Minimum allowed value (null = no lower bound).</summary>
        public double? MinValue { get; set; }
        /// <summary>Maximum allowed value (null = no upper bound).</summary>
        public double? MaxValue { get; set; }
        /// <summary>Step increment for numeric inputs (null = use default step).</summary>
        public double? Step { get; set; }

        public override string ToString() => DisplayName;
    }
}
