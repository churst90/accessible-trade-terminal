using CommunityToolkit.Mvvm.ComponentModel;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Controls voice volume scaling during playback. Applied by AudioSequencer to
    /// scale individual voice volumes within multi-series (Chart scope) and single-series
    /// (Series/Component scope) playback.
    /// </summary>
    public enum PlaybackLayer
    {
        /// <summary>60% volume — continuous wave textures (anchor waves, background tone series).</summary>
        Background,
        /// <summary>80% volume — default for most indicator components (lines, oscillators).</summary>
        Midground,
        /// <summary>100% volume — discrete signal events that must cut through (dots, arrows, bells).</summary>
        Foreground
    }

    /// <summary>
    /// User-controlled visual and acoustic properties for a single data component.
    /// PURE CONFIGURATION: Does not hold the actual data arrays anymore (Phase 1 Segregation).
    /// </summary>
    public partial class ComponentConfig : ObservableObject
    {
        public string Name { get; set; } = ""; 
        public string DisplayName { get; set; } = ""; 
        
        [ObservableProperty] private ComponentDisplayType _displayType = ComponentDisplayType.Line;
        /// <summary>Vertical anchoring for marker shapes. See <see cref="MarkerAnchor"/>.</summary>
        [ObservableProperty] private MarkerAnchor _markerAnchor = MarkerAnchor.Value;
        [ObservableProperty] private ComponentRole _role = ComponentRole.None;
        [ObservableProperty] private AmplitudeMapping _amplitudeMapping = AmplitudeMapping.None;
        [ObservableProperty] private PitchMapping _pitchMapping = PitchMapping.Value;
        [ObservableProperty] private ColorSource _colorSource = ColorSource.Value;
        
        [ObservableProperty] private string _colorHex = "#FFFFFF";
        [ObservableProperty] private string _colorHexSecondary = "#FF0000";

        /// <summary>
        /// True once the user has picked this component's colour by hand.
        ///
        /// <para>
        /// Price action — candle bodies, wicks, volume bars — takes its colour from the active
        /// THEME, not from indicator metadata, because a theme that cannot change what a candle
        /// looks like is not really a theme. But a user who has deliberately recoloured a
        /// component must keep that choice across theme switches, and the renderer cannot tell a
        /// deliberate choice from a metadata default by looking at the hex. This flag is that
        /// distinction, set by the properties dialog at the moment of the edit.
        /// </para>
        /// </summary>
        [ObservableProperty] private bool _isUserStyled = false;
        [ObservableProperty] private string _waveform = "sine";
        [ObservableProperty] private string _aboveReferenceWaveform = "sine";
        [ObservableProperty] private string _belowReferenceWaveform = "sine";
        [ObservableProperty] private string _secondaryWaveform = "";
        [ObservableProperty] private double _freqMultiplier = 1.0;
        [ObservableProperty] private bool _triggerBoundaryClick = false;
        [ObservableProperty] private string _envelopeType = "Sustain";
        [ObservableProperty] private float _volume = 1.0f;
        [ObservableProperty] private double _baseFrequency = 440;
        [ObservableProperty] private double _bullishFrequency = 440;
        [ObservableProperty] private double _bearishFrequency = 220;
        [ObservableProperty] private string _speechTemplate = "{name}, {type}, {value}";
        [ObservableProperty] private float _thickness = 2.0f;
        [ObservableProperty] private DashStyle _dashStyle = DashStyle.Solid;
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private bool _isMuted;
        [ObservableProperty] private bool _isVisible = true;
        
        [ObservableProperty] private string _dataMapping = "";
        
        [ObservableProperty] private double? _referenceLevel = null;
        [ObservableProperty] private bool _isAreaFill = false;
        [ObservableProperty] private bool _usePolarityColoring = false;

        /// <summary>
        /// Denominator used when <see cref="AmplitudeMapping.ReferenceDeviation"/> is active.
        /// Overrides the viewport-derived max deviation so components with a compressed value
        /// range (e.g. Money Flow at −100..−60) are not made artificially quiet by the large
        /// surrounding pane range. Null = compute from viewport (correct for full-range oscillators).
        /// </summary>
        public double? DeviationNorm { get; set; }

        /// <summary>
        /// For Cloud display type: name of the upper-bound component in the same series.
        /// </summary>
        [ObservableProperty] private string _upperComponentName = "";

        /// <summary>
        /// For Cloud display type: name of the lower-bound component in the same series.
        /// </summary>
        [ObservableProperty] private string _lowerComponentName = "";

        /// <summary>
        /// Blends white/pink noise into the waveform output. 0 = pure waveform, 1 = pure noise.
        /// </summary>
        [ObservableProperty] private float _noiseAmount = 0f;

        /// <summary>
        /// Baseline value used by directional bar/histogram coloring.
        /// Values &gt;= ColorBaseline render green; values &lt; ColorBaseline render red.
        /// Default 0 preserves existing zero-crossing behaviour for MACD etc.
        /// Set to 50 for MFI (0–100 bounded oscillator midpoint).
        /// </summary>
        [ObservableProperty] private double _colorBaseline = 0.0;

        /// <summary>
        /// Per-bar color rules evaluated in order; first match overrides the static ColorHex.
        /// Empty list = use static ColorHex for all bars (default behaviour).
        /// </summary>
        public List<ColorRule> ColorRules { get; set; } = new();

        /// <summary>
        /// Optional ID of a <see cref="AccessibleTrader.Sdk.Models.SoundPatch"/> to use for this
        /// component's sonification instead of the individual waveform/envelope/freq fields.
        /// Null = use per-component fields directly (existing behaviour).
        /// </summary>
        [ObservableProperty] private string? _soundPatchId = null;

        /// <summary>
        /// Optional patch used when the bar is bullish (green: close &gt;= open). When set it takes
        /// precedence over <see cref="SoundPatchId"/> on up-bars, letting a component sound different
        /// on green vs red bars. Null = fall back to <see cref="SoundPatchId"/> / manual fields.
        /// </summary>
        [ObservableProperty] private string? _bullishSoundPatchId = null;

        /// <summary>
        /// Optional patch used when the bar is bearish (red: close &lt; open). See <see cref="BullishSoundPatchId"/>.
        /// </summary>
        [ObservableProperty] private string? _bearishSoundPatchId = null;

        /// <summary>
        /// Optional configurable bell decay in milliseconds.
        /// Null = use the patch's DefaultDecayMs or existing envelope defaults.
        /// When set, overrides patch DefaultDecayMs for Ping-envelope voices.
        /// </summary>
        public int? DecayMs { get; set; }

        /// <summary>
        /// Controls voice volume scaling during multi-series and single-series playback.
        /// Background = 60%, Midground = 80% (default), Foreground = 100%.
        /// </summary>
        public PlaybackLayer PlaybackLayer { get; set; } = PlaybackLayer.Midground;

        /// <summary>
        /// When true, navigation speech produces qualitative momentum language
        /// ("strong bullish momentum", "neutral momentum", etc.) instead of a raw numeric value.
        /// Propagated from <see cref="AccessibleTrader.Sdk.Models.IndicatorComponentMetadata.UsesGradientSpeech"/>.
        /// </summary>
        public bool UsesGradientSpeech { get; set; } = false;

        /// <summary>
        /// Sub-pane this component renders in. Null = main area of the parent indicator pane.
        /// Set by IndicatorModelFactory from IndicatorComponentMetadata.SubPaneName.
        /// </summary>
        public string? SubPaneName { get; set; }

        /// <summary>
        /// When true, NavigationFeedbackManager checks zone proximity on each navigation step:
        /// if the current candle's price range (High/Low) overlaps this component's value at that
        /// bar (within a 0.5% tolerance), a quiet 100ms proximity tone plays on audio slot 2.
        /// Intended for carry-forward S/R zone lines (Resistance Zone / Support Zone in Cipher SR).
        /// </summary>
        public bool IsZoneLine { get; set; } = false;

        /// <summary>
        /// When set on a marker-type component (Dot, Diamond, Cross, Arrow, TriangleUp, TriangleDown, Square),
        /// used instead of the generic <see cref="SpeechTemplate"/> when the component has a non-NaN value
        /// at the current bar (signal IS present). Returns an empty string when value IS NaN (no speech).
        /// Supports <c>{price}</c> (formats value as integer price) and <c>{name}</c> tokens.
        /// Null = use standard template path.
        /// Propagated from <see cref="AccessibleTrader.Sdk.Models.IndicatorComponentMetadata.DefaultSignalSpeechTemplate"/>.
        /// </summary>
        public string? SignalSpeechTemplate { get; set; }

        /// <summary>
        /// Preferred sub-pane height as a fraction of the parent indicator pane [0.05, 0.40].
        /// Only meaningful when SubPaneName is set. Null = use metadata-declared ratio (0.22 default).
        /// </summary>
        public float? SubPaneHeightRatio { get; set; }

        /// <summary>
        /// Names of the series-level LevelConfig entries (by LevelConfig.Name) this component
        /// subscribes to for OB/OS zone noise and boundary earcons.
        /// Null = subscribe to all levels (default). Empty = subscribe to none.
        /// </summary>
        public IReadOnlyList<string>? SubscribedLevelNames { get; set; }

        public ComponentConfig Clone()
        {
            return new ComponentConfig
            {
                Name = Name, DisplayName = DisplayName, DisplayType = DisplayType,
                Role = Role, ColorSource = ColorSource,
                AmplitudeMapping = AmplitudeMapping, PitchMapping = PitchMapping,
                ColorHex = ColorHex, ColorHexSecondary = ColorHexSecondary, IsUserStyled = IsUserStyled,
                Waveform = Waveform, AboveReferenceWaveform = AboveReferenceWaveform, BelowReferenceWaveform = BelowReferenceWaveform,
                SecondaryWaveform = SecondaryWaveform, FreqMultiplier = FreqMultiplier, TriggerBoundaryClick = TriggerBoundaryClick,
                EnvelopeType = EnvelopeType,
                Volume = Volume, BaseFrequency = BaseFrequency, BullishFrequency = BullishFrequency, BearishFrequency = BearishFrequency,
                SpeechTemplate = SpeechTemplate, Thickness = Thickness, DashStyle = DashStyle, IsEnabled = IsEnabled,
                IsMuted = IsMuted, IsVisible = IsVisible, DataMapping = DataMapping,
                ReferenceLevel = ReferenceLevel,
                IsAreaFill = IsAreaFill, UsePolarityColoring = UsePolarityColoring,
                UpperComponentName = UpperComponentName, LowerComponentName = LowerComponentName,
                NoiseAmount = NoiseAmount,
                ColorBaseline = ColorBaseline,
                ColorRules = new List<ColorRule>(ColorRules),
                SoundPatchId = SoundPatchId,
                BullishSoundPatchId = BullishSoundPatchId,
                BearishSoundPatchId = BearishSoundPatchId,
                UsesGradientSpeech = UsesGradientSpeech,
                SubPaneName = SubPaneName,
                SubPaneHeightRatio = SubPaneHeightRatio,
                DecayMs = DecayMs,
                PlaybackLayer = PlaybackLayer,
                IsZoneLine = IsZoneLine,
                SignalSpeechTemplate = SignalSpeechTemplate,
                SubscribedLevelNames = SubscribedLevelNames,
                DeviationNorm = DeviationNorm
            };
        }
    }
}
