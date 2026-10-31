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
        /// <summary>
        /// Shape-specific text: the letter inside a <see cref="ComponentDisplayType.TriangleUpRing"/>;
        /// for <see cref="ComponentDisplayType.ValueLabel"/>, a suffix printed after the number on
        /// the chart and in the status line. The suffix is visual only and is not spoken.
        /// </summary>
        public string? MarkerText { get; set; }
        /// <summary>The name of this component's <see cref="ComponentCompanions.Path"/> picture;
        /// provider-owned, rebuilt from <see cref="IndicatorComponentMetadata.PathName"/> on every
        /// load. Null = no path.</summary>
        public string? PathName { get; set; }
        /// <summary>The ribbon this line narrates as part of; provider-owned, rebuilt from
        /// <see cref="IndicatorComponentMetadata.RibbonName"/> on every load. Null = none.</summary>
        public string? RibbonName { get; set; }
        /// <summary>The marker is painted in hindsight — on the bar it happened, found later
        /// (<see cref="ComponentCausality.Lookahead"/>). Provider-owned, rebuilt from the
        /// indicator's declared causality on every load.</summary>
        public bool IsHindsight { get; set; }
        /// <summary>The causal component that confirms this hindsight marker; provider-owned,
        /// rebuilt from <see cref="IndicatorComponentMetadata.ConfirmedBy"/> on every load.</summary>
        public string? ConfirmedBy { get; set; }
        /// <summary>Opacity (0-255) of this component's <see cref="ComponentCompanions.BoxEnd"/>
        /// boxes; provider-owned, rebuilt from <see cref="IndicatorComponentMetadata.BoxAlpha"/>
        /// on every load. 0 = no boxes.</summary>
        public int BoxAlpha { get; set; }
        /// <summary>Opacity (0-255) of a dotted vertical line through the whole pane at every bar
        /// this component has a value on, in its colour, drawn under the candles; provider-owned,
        /// rebuilt from <see cref="IndicatorComponentMetadata.MarkLineAlpha"/>. 0 = none.</summary>
        public int MarkLineAlpha { get; set; }
        /// <summary>The colour the <see cref="ComponentCompanions.Path"/> is drawn in, when it is
        /// not the component's own; provider-owned, rebuilt from
        /// <see cref="IndicatorComponentMetadata.PathColorHex"/>. Null = the component's colour.</summary>
        public string? PathColorHex { get; set; }
        /// <summary>A sibling marker this one is drawn beyond, on a bar both mark (a weekly low's
        /// mark under the daily low's); provider-owned, rebuilt from
        /// <see cref="IndicatorComponentMetadata.MarkerStacksUnder"/>. Null = at its own place.</summary>
        public string? MarkerStacksUnder { get; set; }
        /// <summary>A below-bar or above-bar marker drawn a couple of pixels clear of the bar's
        /// low or high, not padded off it by a share of its range; provider-owned, rebuilt from
        /// <see cref="IndicatorComponentMetadata.MarkerTouchesBar"/>.</summary>
        public bool MarkerTouchesBar { get; set; }
        /// <summary>A line sounded without the plain line's brown tinge; provider-owned, rebuilt from
        /// <see cref="IndicatorComponentMetadata.NoLineTinge"/>.</summary>
        public bool NoLineTinge { get; set; }
        /// <summary>The words a <see cref="ComponentCompanions.BadgeWord"/> picks from, split on
        /// '|'; provider-owned, rebuilt from <see cref="IndicatorComponentMetadata.BadgeWords"/>.</summary>
        public string? BadgeWords { get; set; }
        /// <summary>
        /// The USER's choice from Properties → Appearance → "Draw the {PathName}", or null when
        /// they have never chosen (draw it). The same split as <see cref="UserShowAxisLabel"/>.
        /// </summary>
        public bool? UserDrawPath { get; set; }
        /// <summary>Whether the path is drawn: the user's choice, else yes.</summary>
        public bool DrawsPathInEffect() => !string.IsNullOrEmpty(PathName) && (UserDrawPath ?? true);
        /// <summary>
        /// The value is a count — a cycle's day number, a score — and is printed and said as a
        /// whole number ("day 47"), not with a price's decimals ("47.00").
        /// </summary>
        public bool IsCount { get; set; }
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

        /// <summary>
        /// The INDICATOR's default for <see cref="IsVisible"/> when this config was built — set
        /// from <see cref="IndicatorComponentMetadata.IsVisible"/> on every build and saved with
        /// the workspace beside the flag itself. It is the baseline that tells a user's choice
        /// from a default: on restore a saved <see cref="IsVisible"/> EQUAL to it was never
        /// changed, so it follows the indicator's current default; one that differs is the user's
        /// (H, Properties, show-all) and is kept. Without it a provider could never change a
        /// default visibility for a chart that already exists — the bug levels had until
        /// <c>LevelConfig.ProviderDefaults</c> (2026-10-17). Null = saved before it existed;
        /// see <c>ProviderDefaultVisibility</c> for how those are read. Never set by the user.
        /// </summary>
        public bool? ProviderIsVisible { get; set; }

        /// <summary>
        /// Whether THIS component's signals are spoken unprompted — the component-level half of
        /// <see cref="SeriesConfig.IsAutoNarrated"/>, toggled by N with the cursor on a component.
        ///
        /// <para>
        /// Default FALSE, and it is NOT the gate on its own: a component narrates when its series
        /// is narrating and this is either false-for-every-component (narrate the whole series,
        /// which is what the series flag has always meant) or true for this one. See
        /// <c>SeriesNarrationScope</c>, which is the single place that rule is written down —
        /// putting it anywhere else means two readers of two flags disagreeing about a series
        /// whose components are half-selected.
        /// </para>
        /// </summary>
        [ObservableProperty] private bool _isAutoNarrated;
        
        [ObservableProperty] private string _dataMapping = "";
        
        [ObservableProperty] private double? _referenceLevel = null;
        [ObservableProperty] private bool _isAreaFill = false;
        [ObservableProperty] private bool _usePolarityColoring = false;

        /// <summary>Area-fill colour when it differs from the edge line's; empty = the line colour.
        /// See <see cref="IndicatorComponentMetadata.DefaultFillColorHex"/>.</summary>
        public string FillColorHex { get; set; } = "";

        /// <summary>A histogram area also strokes its value edge in its own colour. The indicator's
        /// choice, rebuilt from <see cref="IndicatorComponentMetadata.DefaultAreaEdge"/> on every load.</summary>
        public bool AreaEdge { get; set; }

        /// <summary>Paint order within the series (lower first). Drawing only — never navigation
        /// order. See <see cref="IndicatorComponentMetadata.DefaultDrawOrder"/>.</summary>
        public int DrawOrder { get; set; }

        /// <summary>Name of the sibling component this line's two colours compare against; empty =
        /// not used. See <see cref="IndicatorComponentMetadata.DefaultColorAboveComponent"/>.</summary>
        public string ColorAboveComponent { get; set; } = "";

        /// <summary>
        /// The INDICATOR's background tint for this component — rebuilt from
        /// <see cref="IndicatorComponentMetadata.DefaultBackgroundTint"/> on every load, so a
        /// provider's new default reaches charts that already exist. What is painted is
        /// <see cref="BackgroundTintInEffect"/>: the user's <see cref="UserBackgroundTint"/> when
        /// they have chosen one, else this. A tint is visual only and does not follow
        /// <see cref="IsVisible"/>; see <see cref="Models.BackgroundTint"/>.
        /// </summary>
        public BackgroundTint BackgroundTint { get; set; } = BackgroundTint.Off;

        /// <summary>
        /// The USER's background tint choice from Properties → Appearance, or null when they have
        /// never chosen (follow <see cref="BackgroundTint"/>). Kept with the workspace and restored
        /// as saved — a choice, not a value, so "I picked Price chart" survives a provider later
        /// changing its default, and "I never picked" keeps following it. A save from before the
        /// field existed has no value and therefore follows the indicator.
        /// </summary>
        public BackgroundTint? UserBackgroundTint { get; set; }

        /// <summary>Tint where the value is at or above <see cref="TintMidline"/>; empty = this
        /// component's <see cref="ColorHex"/> at alpha 0x16. An alpha of 00 paints nothing. See
        /// <see cref="IndicatorComponentMetadata.DefaultTintColorAboveHex"/>.</summary>
        public string TintColorAboveHex { get; set; } = "";

        /// <summary>Tint where the value is below <see cref="TintMidline"/>; empty =
        /// <see cref="ColorHexSecondary"/> at alpha 0x16. An alpha of 00 paints nothing.</summary>
        public string TintColorBelowHex { get; set; } = "";

        /// <summary>The value dividing the two tint colours; null = <see cref="ColorBaseline"/>.</summary>
        public double? TintMidline { get; set; }

        /// <summary>Null = constant strength; set = the strength ramps from 20% of the colour's
        /// alpha at the midline to 100% at this value. See
        /// <see cref="IndicatorComponentMetadata.DefaultTintFullAt"/>.</summary>
        public double? TintFullAt { get; set; }

        /// <summary>The tint actually painted: the user's choice when there is one, else the
        /// indicator's. A method, not a property, so the workspace file does not carry a third
        /// copy of the answer.</summary>
        public BackgroundTint BackgroundTintInEffect() => UserBackgroundTint ?? BackgroundTint;

        /// <summary>
        /// The USER's choice from Properties → Appearance → "Fill under the line" (or "Fill as an
        /// area" on a histogram), or null when they have never chosen (follow
        /// <see cref="IsAreaFill"/>, the indicator's, rebuilt from metadata on every load).
        /// Cody, 2026-10-22: shading "needs to be preferences in the appearance tab".
        /// </summary>
        public bool? UserIsAreaFill { get; set; }

        /// <summary>Whether the fill is drawn: the user's choice when there is one, else the indicator's.</summary>
        public bool IsAreaFillInEffect() => UserIsAreaFill ?? IsAreaFill;

        /// <summary>
        /// The USER's choice from Properties → Appearance → "Read as", or null when they have
        /// never chosen (the indicator's first form). Names the display type of the form chosen;
        /// a value that is neither of the component's forms is the first form. What the form
        /// decides — <see cref="DisplayType"/> (the type word and how it is navigated), the sound
        /// and the speech template — is rebuilt from that form's metadata on every load
        /// (<c>IndicatorModelFactory.BuildForm</c>), so only the choice is kept. The picture is
        /// the same in every form.
        /// Cody, 2026-10-30: "perhaps a toggle here would be best, with the waves being the
        /// default view".
        /// </summary>
        public ComponentDisplayType? UserDisplayType { get; set; }

        /// <summary>Painted as a filled histogram is, though not one; see
        /// <see cref="IndicatorComponentMetadata.DefaultAreaBySign"/>. Rebuilt from metadata on
        /// every load.</summary>
        public bool AreaBySign { get; set; }

        /// <summary>
        /// The INDICATOR's choice of whether this component's newest value is a label on its
        /// pane's value axis — rebuilt from
        /// <see cref="IndicatorComponentMetadata.DefaultShowAxisLabel"/> on every load, so a
        /// provider's new default reaches charts that already exist (Cipher A's ribbon,
        /// 2026-10-18). What is drawn is <see cref="ShowAxisLabelInEffect"/>: the user's
        /// <see cref="UserShowAxisLabel"/> when they have chosen, else this. Axis label only — the
        /// line itself, its speech, sound and status-line reading are untouched.
        /// </summary>
        public bool ShowAxisLabel { get; set; } = true;

        /// <summary>
        /// The USER's choice from Properties → Appearance → "Value label on the axis", or null when
        /// they have never chosen (follow <see cref="ShowAxisLabel"/>). Kept with the workspace and
        /// restored as saved, the same split as <see cref="UserBackgroundTint"/>: "I turned it
        /// on" survives a provider later changing its default, and "I never chose" keeps
        /// following it. A save from before the field existed has no value and follows the indicator.
        /// </summary>
        public bool? UserShowAxisLabel { get; set; }

        /// <summary>Whether the axis label is drawn: the user's choice when there is one, else the
        /// indicator's. A method, so the workspace file does not carry a third copy.</summary>
        public bool ShowAxisLabelInEffect() => UserShowAxisLabel ?? ShowAxisLabel;

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
        /// When true, NavigationFeedbackManager checks zone proximity on each navigation step:
        /// if the current candle's price range (High/Low) overlaps this component's value at that
        /// bar (within a 0.5% tolerance), a quiet 100ms proximity tone plays on audio slot 2.
        /// Intended for carry-forward S/R zone lines (Resistance Zone / Support Zone in Cipher SR).
        /// </summary>
        public bool IsZoneLine { get; set; } = false;

        /// <summary>
        /// False = heard, not painted: the component keeps its data, its navigation stop, its
        /// speech and its narration (a marker still announces when it fires), but the chart draws
        /// nothing for it and its values never stretch an axis. The component-level twin of
        /// <see cref="LevelConfig.IsDrawn"/>. Provider-owned: rebuilt from
        /// <see cref="IndicatorComponentMetadata.DefaultIsDrawn"/> on every load, never a user choice.
        ///
        /// <para>Added 2026-10-17 for the DBSI-style indicator, whose control flips and entry
        /// scores Cody asked to hear narrated: Market Cipher DBSI draws nothing for them (the
        /// numbers on the candles already show who leads), and a marker on every flip would bury
        /// those numbers.</para>
        /// </summary>
        public bool IsDrawn { get; set; } = true;


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
        /// Names of the series-level LevelConfig entries (by LevelConfig.Name) this component
        /// subscribes to for OB/OS zone noise and boundary earcons.
        /// Null = subscribe to all levels (default). Empty = subscribe to none.
        /// </summary>
        public IReadOnlyList<string>? SubscribedLevelNames { get; set; }

        /// <summary>
        /// A complete copy. EVERY settable property must appear below.
        ///
        /// <para>
        /// This method is the single hand-written clone left in the codebase, and a hand-written
        /// clone is a second place every new field has to be added. That has cost this repo four
        /// user-visible defects — component mute and narration both undone by every restart
        /// (2026-09-05), and <see cref="MarkerAnchor"/> dropped from every series build for as
        /// long as it has existed, which drew Market Structure's swing markers at the value
        /// instead of above and below the bar.
        /// </para>
        ///
        /// <para>
        /// It is safe to keep hand-written ONLY because <c>ComponentCloneCompletenessTests</c>
        /// enumerates this type's properties by reflection, sets each to a distinct non-default
        /// value, clones, and diffs. Add a property and that test goes red until it is copied
        /// here. Do not delete that test to make a build pass.
        /// </para>
        /// </summary>
        public ComponentConfig Clone()
        {
            return new ComponentConfig
            {
                Name = Name, DisplayName = DisplayName, DisplayType = DisplayType,
                MarkerAnchor = MarkerAnchor, MarkerText = MarkerText, IsCount = IsCount,
                PathName = PathName, RibbonName = RibbonName, IsHindsight = IsHindsight, ConfirmedBy = ConfirmedBy, UserDrawPath = UserDrawPath,
                BoxAlpha = BoxAlpha, MarkLineAlpha = MarkLineAlpha, PathColorHex = PathColorHex, MarkerStacksUnder = MarkerStacksUnder, MarkerTouchesBar = MarkerTouchesBar, NoLineTinge = NoLineTinge, BadgeWords = BadgeWords, UserIsAreaFill = UserIsAreaFill,
                Role = Role, ColorSource = ColorSource,
                AmplitudeMapping = AmplitudeMapping, PitchMapping = PitchMapping,
                ColorHex = ColorHex, ColorHexSecondary = ColorHexSecondary, IsUserStyled = IsUserStyled,
                Waveform = Waveform, AboveReferenceWaveform = AboveReferenceWaveform, BelowReferenceWaveform = BelowReferenceWaveform,
                FreqMultiplier = FreqMultiplier, TriggerBoundaryClick = TriggerBoundaryClick,
                EnvelopeType = EnvelopeType,
                Volume = Volume, BaseFrequency = BaseFrequency, BullishFrequency = BullishFrequency, BearishFrequency = BearishFrequency,
                SpeechTemplate = SpeechTemplate, Thickness = Thickness, DashStyle = DashStyle, IsEnabled = IsEnabled,
                IsMuted = IsMuted, IsVisible = IsVisible, ProviderIsVisible = ProviderIsVisible, IsAutoNarrated = IsAutoNarrated, DataMapping = DataMapping,
                ReferenceLevel = ReferenceLevel,
                IsAreaFill = IsAreaFill, UsePolarityColoring = UsePolarityColoring,
                FillColorHex = FillColorHex, AreaEdge = AreaEdge, AreaBySign = AreaBySign, UserDisplayType = UserDisplayType, DrawOrder = DrawOrder, ColorAboveComponent = ColorAboveComponent,
                BackgroundTint = BackgroundTint, UserBackgroundTint = UserBackgroundTint,
                TintColorAboveHex = TintColorAboveHex, TintColorBelowHex = TintColorBelowHex,
                TintMidline = TintMidline, TintFullAt = TintFullAt,
                ShowAxisLabel = ShowAxisLabel, UserShowAxisLabel = UserShowAxisLabel,
                UpperComponentName = UpperComponentName, LowerComponentName = LowerComponentName,
                NoiseAmount = NoiseAmount,
                ColorBaseline = ColorBaseline,
                ColorRules = new List<ColorRule>(ColorRules),
                SoundPatchId = SoundPatchId,
                BullishSoundPatchId = BullishSoundPatchId,
                BearishSoundPatchId = BearishSoundPatchId,
                UsesGradientSpeech = UsesGradientSpeech,
                DecayMs = DecayMs,
                PlaybackLayer = PlaybackLayer,
                IsZoneLine = IsZoneLine,
                IsDrawn = IsDrawn,
                SignalSpeechTemplate = SignalSpeechTemplate,
                SubscribedLevelNames = SubscribedLevelNames,
                DeviationNorm = DeviationNorm
            };
        }
    }
}
