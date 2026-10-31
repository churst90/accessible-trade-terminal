using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IIndicatorModelFactory
    {
        /// <param name="restoreId">When restoring a saved workspace, pass the saved series ID to preserve it. Null = generate a new GUID.</param>
        ChartSeries CreateSeriesFromMetadata(IndicatorMetadata meta, string instanceName, string pane, List<(string Name, string Value)> parameters, List<ComponentConfig>? componentOverrides, string? restoreId = null);
        void UpdateSeriesFromUi(ChartSeries series, string instanceName, string pane, List<(string Name, string Value)> parameters, List<ComponentConfig> uiComponents);
        List<(string Name, string DisplayName, string Value, string Type)> GetUiParameters(ChartSeries series, IndicatorMetadata? meta);
        string GenerateInstanceName(IndicatorMetadata meta, IEnumerable<string> parameterValues);
        ComponentConfig CreateComponentConfig(string indicatorCode, string componentName);
        /// <summary>Rebuilds what <paramref name="comp"/>'s form decides from the indicator's
        /// metadata for the form its <see cref="ComponentConfig.UserDisplayType"/> chooses; see
        /// <see cref="IndicatorModelFactory.ApplyForm"/>.</summary>
        void ApplyForm(IndicatorMetadata indicator, ComponentConfig comp);
    }

    public class IndicatorModelFactory : IIndicatorModelFactory
    {
        private readonly IStylingService _stylingService;
        private readonly IIndicatorPreferencesService _prefsService;
        private readonly ISettingsManager? _settings; // optional: sound-theme lookup (null in minimal tests)

        public IndicatorModelFactory(IStylingService stylingService, IIndicatorPreferencesService prefsService,
            ISettingsManager? settings = null)
        {
            _stylingService = stylingService;
            _prefsService   = prefsService;
            _settings       = settings;
        }

        public ComponentConfig CreateComponentConfig(string indicatorCode, string componentName)
        {
            var type = _stylingService.GetDisplayType(indicatorCode, componentName);
            var role = _stylingService.GetComponentRole(indicatorCode, componentName);
            var profile = _stylingService.GetSonificationProfile(type, role, componentName);

            return new ComponentConfig
            {
                Name = componentName,
                DisplayName = componentName,
                DisplayType = type,
                Role = role,
                ColorSource = _stylingService.GetColorSource(indicatorCode, componentName),
                AmplitudeMapping = profile.AmplitudeMapping,
                PitchMapping = profile.PitchMapping,
                ColorHex = _stylingService.GetDefaultColor(indicatorCode, componentName, type),
                ColorHexSecondary = _stylingService.GetSecondaryColor(indicatorCode, componentName, type),
                Waveform = profile.Waveform,
                AboveReferenceWaveform = profile.AboveWaveform,
                BelowReferenceWaveform = profile.BelowWaveform,
                FreqMultiplier = profile.FreqMultiplier,
                TriggerBoundaryClick = profile.TriggerBoundaryClick,
                EnvelopeType = profile.EnvelopeType,
                Thickness = _stylingService.GetDefaultThickness(type),
                BaseFrequency = profile.BaseFrequency,
                SpeechTemplate = _stylingService.GetSpeechTemplate(indicatorCode, componentName, type),
                // No metadata on this path, so the display type is all there is to go on. Every
                // component that HAS metadata declares its own — see CreateComponentConfigFromMeta.
                ReferenceLevel = type is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea ? 0.0 : (double?)null,
                IsAreaFill = false,   // opt-in; see StandardRenderers.IsFilled
                UsePolarityColoring = _stylingService.GetUsePolarityColoring(indicatorCode, componentName, type),
                ColorBaseline = _stylingService.GetColorBaseline(indicatorCode, componentName),
                IsEnabled = true,
                IsVisible = true,
                Volume = 1.0f,
                // Sound theme: the active theme may voice this component with a factory
                // instrument patch (per-family). Null = classic built-in timbre. The user
                // can always override per component in Properties, which wins because it
                // simply overwrites this same field.
                SoundPatchId = Audio.SoundThemes.ResolvePatchId(
                    _settings?.GetSetting(Audio.SoundThemes.SettingsKey)?.ToString(), type, role)
            };
        }

        public string GenerateInstanceName(IndicatorMetadata meta, IEnumerable<string> parameterValues)
        {
            string paramString = string.Join(",", parameterValues);
            return string.IsNullOrEmpty(paramString) ? meta.Name : $"{meta.Name} ({paramString})";
        }

        public List<(string Name, string DisplayName, string Value, string Type)> GetUiParameters(ChartSeries series, IndicatorMetadata? meta)
        {
            var result = new List<(string Name, string DisplayName, string Value, string Type)>();
            foreach (var kvp in series.Parameters)
            {
                string displayName = kvp.Key;
                string type = "double";
                var metaParam = meta?.Parameters.FirstOrDefault(p => p.Name == kvp.Key);
                if (metaParam != null) { displayName = metaParam.DisplayName; type = metaParam.DataType.Name.ToLower(); }
                result.Add((kvp.Key, displayName, kvp.Value.ToString(), type));
            }
            return result;
        }


        /// <summary>
        /// Parses a formatted parameter value into the double the config dictionary stores.
        ///
        /// <para>
        /// Booleans need special handling: <c>SeriesManagementService.FormatParam</c> renders them
        /// as "true"/"false", and <c>double.TryParse</c> rejects both — so before this existed,
        /// every bool indicator parameter was silently DROPPED on its way into the config and the
        /// provider always fell back to its hardcoded default. That made Cipher SR's AdaptiveBreak,
        /// Cipher B's anchor suppression and every other bool knob permanently unsettable from the
        /// UI, with no error to show for it.
        /// </para>
        /// </summary>
        internal static bool TryParseParamValue(string? raw, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            if (bool.TryParse(raw, out bool b)) { value = b ? 1 : 0; return true; }
            return double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Routes each formatted parameter to the dictionary that can hold it: numbers and
        /// bools to <see cref="SeriesConfig.Parameters"/>, everything else to
        /// <see cref="SeriesConfig.StringParameters"/>.
        ///
        /// <para>
        /// The `else` branch is the fix: it used to not exist. Anything
        /// <see cref="TryParseParamValue"/> rejected was silently discarded, which made every
        /// <c>typeof(string)</c> parameter in the catalogue structurally unreachable — the UI
        /// collected a value, the config dropped it, and the provider read its hardcoded
        /// default with nothing anywhere reporting a problem.
        /// </para>
        /// </summary>
        internal static void ApplyParameters(SeriesConfig config, List<(string Name, string Value)> parameters)
        {
            foreach (var p in parameters)
            {
                if (TryParseParamValue(p.Value, out double val))
                    config.Parameters[p.Name] = val;
                else if (!string.IsNullOrWhiteSpace(p.Value))
                    config.StringParameters[p.Name] = p.Value;
            }
        }

        public ChartSeries CreateSeriesFromMetadata(IndicatorMetadata meta, string instanceName, string pane, List<(string Name, string Value)> parameters, List<ComponentConfig>? componentOverrides, string? restoreId = null)
        {
            string id = restoreId ?? Guid.NewGuid().ToString();
            var config = new SeriesConfig
            {
                Id = id,
                Name = instanceName, 
                FriendlyName = instanceName, 
                IndicatorCode = meta.Code, 
                Pane = pane,
                // The indicator's natural bounds, so its pane axis — and therefore its pitch —
                // never shrinks inside them. See IndicatorMetadata.RangeMin.
                RangeMin = meta.RangeMin,
                RangeMax = meta.RangeMax,
            };

            ApplyParameters(config, parameters);

            // ── 3-layer component merge ─────────────────────────────────────────────
            // Layer 1: always generate fresh configs from provider metadata.
            //          This ensures new metadata defaults (colors, audio, shapes) are never
            //          silently overridden by a stale all-or-nothing workspace save.
            //          Built from the FORM chosen ("Read as", UserDisplayType): the chart's
            //          own choice, else the one saved as this indicator's default, else the
            //          first form. Choosing here, before layers 2 and 3, keeps their order.
            var prefs = _prefsService.GetPreferences(meta.Code);
            var sourceComponents = meta.Components
                .Select(mc => BuildForm(meta, mc,
                    componentOverrides?.FirstOrDefault(o => o.Name == mc.Name)?.UserDisplayType
                    ?? prefs?.FirstOrDefault(p => p.Name == mc.Name)?.UserDisplayType))
                .ToList();

            // Layer 2: restore lightweight per-component user state from the workspace save
            //          (visibility, mute, component volume, freq multiplier, and the narration
            //          selection N makes on a component). Colors and audio properties come from
            //          metadata or preferences — not workspace. Narration was missing from this
            //          list until 2026-09-05, so "narrate only this component" lasted one session.
            if (componentOverrides != null && componentOverrides.Any())
            {
                foreach (var fresh in sourceComponents)
                {
                    var saved = componentOverrides.FirstOrDefault(o => o.Name == fresh.Name);
                    if (saved == null) continue;
                    // Visibility: the saved flag where the user changed it, else the indicator's
                    // CURRENT default — fresh.IsVisible here, from metadata. A provider can then
                    // hide or show a component by default and reach charts that already exist
                    // (Cipher B's own signals, 2026-10-17). See ProviderDefaultVisibility.
                    fresh.IsVisible      = ProviderDefaultVisibility.Resolve(
                        saved.IsVisible, saved.ProviderIsVisible, fresh.IsVisible,
                        ProviderDefaultVisibility.RetiredComponentDefault(meta.Code, fresh.Name));
                    fresh.IsEnabled      = saved.IsEnabled;
                    // Saved hidden by a default that now leaves the part out: left out, and
                    // shown if the user includes it (ProviderDefaultVisibility.HiddenDefaultIsNowLeftOut).
                    // Kept in from such a save (shown, or tinted, by the user): as saved — its old
                    // hidden default is now "left out", not a visibility to resolve against.
                    bool? nowDefaultIsEnabled = meta.Components.FirstOrDefault(m => m.Name == fresh.Name)?.DefaultIsEnabled;
                    if (ProviderDefaultVisibility.HiddenDefaultIsNowLeftOut(
                            nowDefaultIsEnabled, saved.IsVisible, saved.ProviderIsVisible,
                            ProviderDefaultVisibility.RetiredComponentDefault(meta.Code, fresh.Name),
                            userTinted: saved.UserBackgroundTint is { } tint && tint != BackgroundTint.Off))
                    {
                        fresh.IsEnabled = false;
                        fresh.IsVisible = true;
                    }
                    else if (nowDefaultIsEnabled == false && saved.ProviderIsVisible != true)
                        fresh.IsVisible = saved.IsVisible;
                    fresh.IsMuted        = saved.IsMuted;
                    fresh.Volume         = saved.Volume;
                    fresh.FreqMultiplier = saved.FreqMultiplier;
                    fresh.IsAutoNarrated = saved.IsAutoNarrated;
                    // "The user picked this colour by hand." Layer 3 restores the colour itself,
                    // but not the flag that stops a theme change overwriting it again — so
                    // without this line a hand-picked colour survives one restart and then loses
                    // its protection. StandardRenderers.cs:328 is the reader.
                    fresh.IsUserStyled   = saved.IsUserStyled;
                    // The user's background-tint CHOICE (null = never chose, follow the indicator).
                    // Only the choice: BackgroundTint itself came from metadata above, so a
                    // provider's new default reaches every chart whose user never picked one.
                    fresh.UserBackgroundTint = saved.UserBackgroundTint;
                    // The user's axis-label choice, the same split: ShowAxisLabel came from
                    // metadata above, so a provider's new default (Cipher A's ribbon, 2026-10-18)
                    // reaches every chart whose user never chose; null = never chose.
                    fresh.UserShowAxisLabel = saved.UserShowAxisLabel;
                    // The user's fill and path choices, the same split again: IsAreaFill and
                    // PathName came from metadata above.
                    fresh.UserIsAreaFill = saved.UserIsAreaFill;
                    fresh.UserDrawPath   = saved.UserDrawPath;
                    fresh.UserDisplayType = saved.UserDisplayType;
                }
            }

            // Layer 3: apply explicit user preferences saved via Properties dialog.
            //          These take priority over both metadata defaults and workspace state,
            //          and are the only way to permanently persist appearance / audio changes.
            if (prefs != null)
            {
                foreach (var fresh in sourceComponents)
                {
                    var pref = prefs.FirstOrDefault(p => p.Name == fresh.Name);
                    if (pref == null) continue;
                    // A choice saved on this chart is more specific than an indicator-type default.
                    if (fresh.UserShowAxisLabel is null && pref.UserShowAxisLabel is not null)
                        fresh.UserShowAxisLabel = pref.UserShowAxisLabel;
                    if (fresh.UserBackgroundTint is null && pref.UserBackgroundTint is not null)
                        fresh.UserBackgroundTint = pref.UserBackgroundTint;
                    if (fresh.UserIsAreaFill is null && pref.UserIsAreaFill is not null)
                        fresh.UserIsAreaFill = pref.UserIsAreaFill;
                    if (fresh.UserDrawPath is null && pref.UserDrawPath is not null)
                        fresh.UserDrawPath = pref.UserDrawPath;
                    if (fresh.UserDisplayType is null && pref.UserDisplayType is not null)
                        fresh.UserDisplayType = pref.UserDisplayType;
                    if (pref.ColorHex          != null) fresh.ColorHex          = pref.ColorHex;
                    if (pref.ColorHexSecondary  != null) fresh.ColorHexSecondary  = pref.ColorHexSecondary;
                    if (pref.Thickness         != null) fresh.Thickness         = pref.Thickness.Value;
                    if (pref.DashStyle         != null) fresh.DashStyle         = pref.DashStyle.Value;
                    if (pref.Waveform          != null) fresh.Waveform          = pref.Waveform;
                    if (pref.EnvelopeType      != null) fresh.EnvelopeType      = pref.EnvelopeType;
                    if (pref.Volume            != null) fresh.Volume            = pref.Volume.Value;
                    if (pref.FreqMultiplier    != null) fresh.FreqMultiplier    = pref.FreqMultiplier.Value;
                    if (pref.BaseFrequency     != null) fresh.BaseFrequency     = pref.BaseFrequency.Value;
                    if (pref.NoiseAmount       != null) fresh.NoiseAmount       = pref.NoiseAmount.Value;
                    // Defaults saved while the part was hidden by default (the same change as layer 2):
                    // it stays left out, and its saved "hidden" does not follow it in when included.
                    if (pref.IsVisible == false && ProviderDefaultVisibility.HiddenDefaultIsNowLeftOut(
                            meta.Components.FirstOrDefault(m => m.Name == fresh.Name)?.DefaultIsEnabled,
                            savedIsVisible: false, savedAgainst: null, retiredDefault: null))
                        continue;
                    if (pref.IsVisible         != null) fresh.IsVisible         = pref.IsVisible.Value;
                    // "Include" saved as this indicator's default — but a chart that saved its own
                    // choice keeps it, as with the axis label above.
                    if (pref.IsEnabled != null && componentOverrides?.Any(o => o.Name == fresh.Name) != true)
                        fresh.IsEnabled = pref.IsEnabled.Value;
                }
            }

            // ComponentConfig.Clone() rather than a clone of our own: a second hand-written
            // field list is a second place every new property must be added, and this one had
            // silently fallen three fields behind (MarkerAnchor, IsUserStyled,
            // SecondaryWaveform). MarkerAnchor mattered — it is set from provider metadata a
            // few lines above and dropped here on EVERY series build, not just a restore, so
            // Market Structure's swing markers were anchored to the value instead of above and
            // below the bar for as long as the anchor has existed.
            //
            // A component the user (or the indicator, by default) has left out goes to
            // ExcludedComponents instead: nothing downstream sees it. Metadata order is kept in both.
            foreach (var uiComp in sourceComponents)
                (uiComp.IsEnabled ? config.Components : config.ExcludedComponents).Add(uiComp.Clone());

            // Copy default cloud fills from metadata
            // ProviderIsVisible records the default each fill was built against, so a restore can
            // tell the user's show/hide from a default (ProviderDefaultVisibility).
            foreach (var fill in meta.DefaultCloudFills)
            {
                var copy = fill.Clone();
                copy.ProviderIsVisible = fill.IsVisible;
                config.CloudFills.Add(copy);
            }

            // Copy default zone bands from metadata
            foreach (var band in meta.DefaultZoneBands)
                config.ZoneBands.Add(band.Clone());

            // NOTE: Reference level lines (OB/OS/zero) are NOT injected here as ComponentConfig
            // entries. They live in series.Levels (LevelConfig) via
            // SeriesManagementService.InjectDefaultLevels. Adding them to config.Components would
            // make them NAVIGABLE and give them a voice of their own, which is wrong — they are
            // not series. They are emphatically not "visual-only", which this note used to claim:
            // a level drives its crossing earcon, its zone noise, the 0 key's toggle and the
            // narrator's zone word. Everything about a level except arrowing onto it is audible.

            var data = new SeriesDataBuffer { SeriesId = id };
            var series = new ChartSeries(config, data)
            {
                IsProfile = meta.Category == "Profile" || meta.Category == "Volume Profile" || meta.Code == "HEATMAP",
                RequiresFullRecalcOnTick = meta.RequiresFullRecalcOnTick
            };

            return series;
        }

        public void UpdateSeriesFromUi(ChartSeries series, string instanceName, string pane, List<(string Name, string Value)> parameters, List<ComponentConfig> uiComponents)
        {
            var config = series.Config;
            config.Name = instanceName; 
            config.FriendlyName = instanceName; 
            config.Pane = pane;
            config.Parameters.Clear();
            config.StringParameters.Clear();
            ApplyParameters(config, parameters);

            bool isProfileOrHeatmap = uiComponents.Any(c => c.DisplayType == ComponentDisplayType.Profile || c.DisplayType == ComponentDisplayType.Heatmap);
            series.IsProfile = isProfileOrHeatmap;

            foreach (var uiComp in uiComponents)
            {
                var target = config.Components.FirstOrDefault(c => c.Name == uiComp.Name);
                if (target != null)
                {
                    target.DisplayName = uiComp.DisplayName; 
                    target.DisplayType = uiComp.DisplayType;
                    target.ColorHex = uiComp.ColorHex; 
                    target.ColorHexSecondary = uiComp.ColorHexSecondary;
                    target.Waveform = uiComp.Waveform; 
                    target.Volume = uiComp.Volume;
                    target.Thickness = uiComp.Thickness; 
                    target.DashStyle = uiComp.DashStyle;
                    target.IsEnabled = uiComp.IsEnabled;
                }
            }
        }

        /// <summary>
        /// The form of <paramref name="mc"/> that <paramref name="chosen"/> names: its
        /// <see cref="IndicatorComponentMetadata.OtherForm"/> when the display type is that form's,
        /// else the first form (never chosen, or a type neither form has).
        /// </summary>
        public static IndicatorComponentMetadata FormOf(IndicatorComponentMetadata mc, ComponentDisplayType? chosen) =>
            chosen is { } t && mc.OtherForm is { } other && other.DisplayType == t && t != mc.DisplayType ? other : mc;

        /// <summary>
        /// <paramref name="mc"/> built in the form <paramref name="chosen"/> names. The PICTURE is
        /// always the first form's — every field of it — and the chosen form brings only what
        /// the screen reader is given: the display type (the type word, how it is navigated),
        /// the sound and the speech (<see cref="TakeForm"/>). Cody, 2026-10-30: "the toggle …
        /// shouldn't change what is visually shown, only what component type the screen reader
        /// sees and navigates on".
        /// </summary>
        private ComponentConfig BuildForm(IndicatorMetadata indicator, IndicatorComponentMetadata mc, ComponentDisplayType? chosen)
        {
            var comp = CreateComponentConfigFromMeta(indicator, mc);
            var form = FormOf(mc, chosen);
            if (!ReferenceEquals(form, mc)) TakeForm(comp, CreateComponentConfigFromMeta(indicator, form));
            return comp;
        }

        /// <summary>
        /// What a form decides, copied from <paramref name="from"/>: the display type, the sound
        /// and the speech. Nothing drawn — colours, fill, edge, thickness, paint order, baseline,
        /// polarity colouring and reference level stay as they are — nor the pitch multiplier,
        /// which is the user's.
        /// </summary>
        private static void TakeForm(ComponentConfig to, ComponentConfig from)
        {
            to.DisplayType = from.DisplayType;
            to.Waveform = from.Waveform; to.AboveReferenceWaveform = from.AboveReferenceWaveform;
            to.BelowReferenceWaveform = from.BelowReferenceWaveform; to.EnvelopeType = from.EnvelopeType;
            to.AmplitudeMapping = from.AmplitudeMapping; to.PitchMapping = from.PitchMapping;
            to.BaseFrequency = from.BaseFrequency; to.BullishFrequency = from.BullishFrequency;
            to.BearishFrequency = from.BearishFrequency;
            to.TriggerBoundaryClick = from.TriggerBoundaryClick; to.NoiseAmount = from.NoiseAmount;
            to.PlaybackLayer = from.PlaybackLayer; to.DeviationNorm = from.DeviationNorm;
            to.SubscribedLevelNames = from.SubscribedLevelNames; to.SpeechTemplate = from.SpeechTemplate;
            to.SignalSpeechTemplate = from.SignalSpeechTemplate; to.DecayMs = from.DecayMs;
            to.SoundPatchId = from.SoundPatchId;
        }

        /// <summary>
        /// "Read as", changed in Properties: the display type, sound and speech of the form
        /// <see cref="ComponentConfig.UserDisplayType"/> chooses, from the metadata, as a load
        /// would build them (<see cref="BuildForm"/>). The picture is not touched, and neither is
        /// what the user set on the component (visibility, inclusion, mute, volume, pitch
        /// multiplier, narration, colours, the User* choices).
        /// </summary>
        public void ApplyForm(IndicatorMetadata indicator, ComponentConfig comp)
        {
            var mc = indicator.Components.FirstOrDefault(m => m.Name == comp.Name);
            if (mc?.OtherForm == null) return;
            TakeForm(comp, BuildForm(indicator, mc, comp.UserDisplayType));
        }

        /// <summary>
        /// Builds a ComponentConfig from indicator metadata, preferring the metadata's optional
        /// Default* and audio hint fields over global role/type-based StylingService defaults.
        /// DataMapping, UpperComponentName, LowerComponentName, and IsVisible are applied from metadata.
        /// </summary>
        private ComponentConfig CreateComponentConfigFromMeta(IndicatorMetadata indicator, IndicatorComponentMetadata meta)
        {
            string indicatorCode = indicator.Code;
            // DisplayType and Role are already set in metadata at provider discovery time.
            var type    = meta.DisplayType;
            var role    = meta.Role;
            var profile = _stylingService.GetSonificationProfile(type, role, meta.Name);

            var comp = new ComponentConfig
            {
                Name         = meta.Name,
                DisplayName  = meta.DisplayName ?? meta.Name,
                DisplayType  = type,
                MarkerText   = meta.MarkerText,
                PathName     = meta.PathName,
                RibbonName   = meta.RibbonName,
                IsHindsight  = CausalityContract.Effective(indicator, meta) == ComponentCausality.Lookahead,
                ConfirmedBy  = meta.ConfirmedBy,
                BoxAlpha     = meta.BoxAlpha,
                MarkLineAlpha = meta.MarkLineAlpha,
                PathColorHex = meta.PathColorHex,
                MarkerStacksUnder = meta.MarkerStacksUnder,
                MarkerTouchesBar = meta.MarkerTouchesBar,
                BadgeWords   = meta.BadgeWords,
                IsCount      = meta.IsCount,
                Role         = role,
                DataMapping  = meta.DataMapping ?? string.Empty,
                UpperComponentName = meta.UpperComponentName ?? string.Empty,
                LowerComponentName = meta.LowerComponentName ?? string.Empty,
                IsVisible    = meta.IsVisible,
                // The baseline a restore compares the saved flag with (ProviderDefaultVisibility).
                ProviderIsVisible = meta.IsVisible,
                IsEnabled    = meta.DefaultIsEnabled ?? true,
                Volume       = 1.0f,

                // Colors — provider metadata wins; fall through to StylingService role defaults.
                ColorSource        = meta.DefaultColorSource  ?? _stylingService.GetColorSource(indicatorCode, meta.Name),
                ColorHex           = meta.DefaultColorHex     ?? _stylingService.GetDefaultColor(indicatorCode, meta.Name, type),
                ColorHexSecondary  = meta.DefaultColorHexSecondary ?? _stylingService.GetSecondaryColor(indicatorCode, meta.Name, type),
                ColorBaseline      = meta.ColorBaseline       ?? _stylingService.GetColorBaseline(indicatorCode, meta.Name),
                DashStyle          = meta.DefaultDashStyle    ?? DashStyle.Solid,
                MarkerAnchor       = meta.DefaultMarkerAnchor ?? MarkerAnchor.Value,

                // Visual size.
                Thickness = meta.DefaultThickness ?? _stylingService.GetDefaultThickness(type),

                // Audio — provider metadata overrides profile defaults; profile provides the type-level fallback.
                Waveform              = meta.DefaultWaveform        ?? profile.Waveform,
                AboveReferenceWaveform = meta.DefaultAboveWaveform  ?? profile.AboveWaveform,
                BelowReferenceWaveform = meta.DefaultBelowWaveform  ?? profile.BelowWaveform,
                EnvelopeType          = meta.DefaultEnvelopeType   ?? profile.EnvelopeType,
                AmplitudeMapping      = meta.DefaultAmplitudeMapping ?? profile.AmplitudeMapping,
                PitchMapping          = meta.DefaultPitchMapping    ?? profile.PitchMapping,
                BaseFrequency         = meta.DefaultBaseFrequency   ?? profile.BaseFrequency,
                BullishFrequency      = meta.DefaultBullishFrequency ?? 440.0,
                BearishFrequency      = meta.DefaultBearishFrequency ?? 220.0,
                FreqMultiplier        = meta.DefaultFreqMultiplier  ?? profile.FreqMultiplier,
                TriggerBoundaryClick  = meta.DefaultTriggerBoundaryClick ?? profile.TriggerBoundaryClick,
                NoiseAmount           = meta.DefaultNoiseAmount ?? 0f,

                // Speech template: provider metadata wins; fall through to StylingService generic default.
                SpeechTemplate     = meta.SpeechTemplate ?? _stylingService.GetSpeechTemplate(indicatorCode, meta.Name, type),
                // The value this component swings about: the audio layer splits its above/below
                // waveforms here, the amplitude mapping measures deviation from here, and the 0
                // key marks it. It is DECLARED — DeclaredNeutralTests holds every Oscillator and
                // ZeroArea component in the fleet to declaring one.
                //
                // There used to be four steps. Step 2 was a substring match on the indicator code
                // in StylingService (RSI→50, MACD→0, STOCH→50, WILLIAMS→−50), and steps 3 and 4
                // were a ?? chain with a TYPE DEFAULT in the middle of it: because step 3 returns
                // a non-null 0.0 for every Oscillator and ZeroArea, step 4 could never run for the
                // types its own comment named. MFI, the Ultimate Oscillator, Choppiness and STC
                // contain none of the four magic words, so all four took step 3 and got a neutral
                // of 0 on a 0–100 pane: a waveform that never flipped, and a 0 key that offered to
                // put a line named "Zero" on the floor.
                //
                // What remains is the declaration, then the ColorBaseline a histogram may be
                // anchored to, then the type default as the last resort for a component that
                // declares neither.
                ReferenceLevel     = meta.DefaultReferenceLevel
                                     ?? (meta.ColorBaseline.HasValue && meta.ColorBaseline.Value != 0.0 ? meta.ColorBaseline : null)
                                     ?? (type is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea ? 0.0 : (double?)null),
                // Opt-in, and the default is OFF. It used to fall through to a StylingService
                // answer of "true for every Oscillator", which nothing read — see
                // StandardRenderers.IsFilled for the whole account.
                IsAreaFill         = meta.DefaultIsAreaFill ?? false,
                FillColorHex       = meta.DefaultFillColorHex ?? string.Empty,
                AreaEdge           = meta.DefaultAreaEdge ?? false,
                AreaBySign         = meta.DefaultAreaBySign ?? false,
                DrawOrder          = meta.DefaultDrawOrder ?? 0,
                ColorAboveComponent = meta.DefaultColorAboveComponent ?? string.Empty,
                // Background tint — the indicator's declaration, rebuilt on every load. The user's
                // own choice is UserBackgroundTint, restored in layer 2; it is never set here.
                // A window is shaded unless its indicator says otherwise (ComponentDisplayType.Window).
                BackgroundTint     = meta.DefaultBackgroundTint
                                     ?? (type == ComponentDisplayType.Window ? BackgroundTint.ThisPane : BackgroundTint.Off),
                TintColorAboveHex  = meta.DefaultTintColorAboveHex ?? string.Empty,
                TintColorBelowHex  = meta.DefaultTintColorBelowHex ?? string.Empty,
                TintMidline        = meta.DefaultTintMidline,
                TintFullAt         = meta.DefaultTintFullAt,
                // Whether the newest value is a label on the axis — the indicator's, rebuilt on
                // every load; the user's own choice is UserShowAxisLabel, restored in layer 2.
                ShowAxisLabel      = meta.DefaultShowAxisLabel ?? true,
                IsMuted            = meta.DefaultIsMuted ?? false,
                UsePolarityColoring = meta.DefaultUsePolarityColoring ?? _stylingService.GetUsePolarityColoring(indicatorCode, meta.Name, type),

                // Bell synthesis hints — provider metadata Layer 1; overridden by user edit (DecayMs on ComponentConfig).
                DecayMs       = meta.DefaultDecayMs,
                PlaybackLayer = meta.DefaultPlaybackLayer ?? PlaybackLayer.Midground,

                // SoundPatch assignment — provider declares a named patch; null = use per-field settings.
                SoundPatchId  = meta.DefaultSoundPatchId,

                // Gradient speech — provider opts component into qualitative momentum language.
                UsesGradientSpeech = meta.UsesGradientSpeech,

                // Zone line — proximity earcon fired by NavigationFeedbackManager when cursor bar overlaps this level.
                IsZoneLine = meta.DefaultIsZoneLine,
                IsDrawn = meta.DefaultIsDrawn ?? true,

                // Signal speech template — overrides standard template for marker-type components when signal is active.
                SignalSpeechTemplate = meta.DefaultSignalSpeechTemplate,

                // Level subscription filter — null = all levels, empty = no zone noise or earcons.
                SubscribedLevelNames = meta.SubscribedLevelNames,

                // Amplitude deviation normalization — overrides viewport-derived denominator.
                DeviationNorm = meta.DefaultDeviationNorm,
            };

            // GAP 3: Copy declarative color rules from metadata into the component config.
            // Applied in order — first matching rule wins. Empty metadata = static ColorHex used for all bars.
            if (meta.DefaultColorRules is { Count: > 0 })
            {
                foreach (var rule in meta.DefaultColorRules)
                    comp.ColorRules.Add(rule);
            }

            return comp;
        }

    }
}
