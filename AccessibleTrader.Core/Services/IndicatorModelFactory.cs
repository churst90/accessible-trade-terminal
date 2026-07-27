using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
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
                ReferenceLevel = _stylingService.GetReferenceLevel(indicatorCode, componentName, type)
                                 ?? (type is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea ? 0.0 : (double?)null),
                IsAreaFill = _stylingService.GetIsAreaFill(indicatorCode, componentName, type),
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

        public ChartSeries CreateSeriesFromMetadata(IndicatorMetadata meta, string instanceName, string pane, List<(string Name, string Value)> parameters, List<ComponentConfig>? componentOverrides, string? restoreId = null)
        {
            string id = restoreId ?? Guid.NewGuid().ToString();
            var config = new SeriesConfig
            {
                Id = id,
                Name = instanceName, 
                FriendlyName = instanceName, 
                IndicatorCode = meta.Code, 
                Pane = pane
            };

            foreach (var p in parameters) if (TryParseParamValue(p.Value, out double val)) config.Parameters[p.Name] = val;
            
            // ── 3-layer component merge ─────────────────────────────────────────────
            // Layer 1: always generate fresh configs from provider metadata.
            //          This ensures new metadata defaults (colors, audio, shapes) are never
            //          silently overridden by a stale all-or-nothing workspace save.
            var sourceComponents = meta.Components
                .Select(mc => CreateComponentConfigFromMeta(meta.Code, mc))
                .ToList();

            // Sort components so main-pane components appear first (SubPaneName == null/empty),
            // followed by each sub-pane group in order of first appearance in the original list.
            // Within each group, relative declaration order is preserved (stable sort via index).
            {
                var paneOrder = new List<string?>();
                paneOrder.Add(null); // main pane slot
                for (int i = 0; i < sourceComponents.Count; i++)
                {
                    string? pn = sourceComponents[i].SubPaneName;
                    string? key = string.IsNullOrEmpty(pn) ? null : pn;
                    if (key != null && !paneOrder.Contains(key, StringComparer.OrdinalIgnoreCase))
                        paneOrder.Add(key);
                }
                sourceComponents = sourceComponents
                    .Select((c, idx) => (comp: c, idx))
                    .OrderBy(t =>
                    {
                        string? k = string.IsNullOrEmpty(t.comp.SubPaneName) ? null : t.comp.SubPaneName;
                        int pos = paneOrder.FindIndex(p => string.Equals(p, k, StringComparison.OrdinalIgnoreCase));
                        return pos < 0 ? paneOrder.Count : pos;
                    })
                    .ThenBy(t => t.idx)
                    .Select(t => t.comp)
                    .ToList();
            }

            // Layer 2: restore lightweight per-component user state from the workspace save
            //          (visibility, mute, component volume, freq multiplier ONLY).
            //          Colors and audio properties come from metadata or preferences — not workspace.
            if (componentOverrides != null && componentOverrides.Any())
            {
                foreach (var fresh in sourceComponents)
                {
                    var saved = componentOverrides.FirstOrDefault(o => o.Name == fresh.Name);
                    if (saved == null) continue;
                    fresh.IsVisible      = saved.IsVisible;
                    fresh.IsEnabled      = saved.IsEnabled;
                    fresh.IsMuted        = saved.IsMuted;
                    fresh.Volume         = saved.Volume;
                    fresh.FreqMultiplier = saved.FreqMultiplier;
                }
            }

            // Layer 3: apply explicit user preferences saved via Properties dialog.
            //          These take priority over both metadata defaults and workspace state,
            //          and are the only way to permanently persist appearance / audio changes.
            var prefs = _prefsService.GetPreferences(meta.Code);
            if (prefs != null)
            {
                foreach (var fresh in sourceComponents)
                {
                    var pref = prefs.FirstOrDefault(p => p.Name == fresh.Name);
                    if (pref == null) continue;
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
                    if (pref.IsVisible         != null) fresh.IsVisible         = pref.IsVisible.Value;
                }
            }

            foreach (var uiComp in sourceComponents) config.Components.Add(CloneComponent(uiComp));

            // Copy default cloud fills from metadata
            foreach (var fill in meta.DefaultCloudFills)
                config.CloudFills.Add(fill.Clone());

            // Copy default zone bands from metadata
            foreach (var band in meta.DefaultZoneBands)
                config.ZoneBands.Add(band.Clone());

            // NOTE: Reference level lines (OB/OS/zero) are NOT injected here as ComponentConfig entries.
            // They are visual-only and live in series.Levels (LevelConfig) via SeriesManagementService.InjectDefaultLevels.
            // Adding them to config.Components would make them navigable and audible, which is wrong.

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
            foreach (var p in parameters) if (TryParseParamValue(p.Value, out double val)) config.Parameters[p.Name] = val;

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
        /// Builds a ComponentConfig from indicator metadata, preferring the metadata's optional
        /// Default* and audio hint fields over global role/type-based StylingService defaults.
        /// DataMapping, UpperComponentName, LowerComponentName, and IsVisible are applied from metadata.
        /// </summary>
        private ComponentConfig CreateComponentConfigFromMeta(string indicatorCode, IndicatorComponentMetadata meta)
        {
            // DisplayType and Role are already set in metadata at provider discovery time.
            var type    = meta.DisplayType;
            var role    = meta.Role;
            var profile = _stylingService.GetSonificationProfile(type, role, meta.Name);

            var comp = new ComponentConfig
            {
                Name         = meta.Name,
                DisplayName  = meta.DisplayName ?? meta.Name,
                DisplayType  = type,
                Role         = role,
                DataMapping  = meta.DataMapping ?? string.Empty,
                UpperComponentName = meta.UpperComponentName ?? string.Empty,
                LowerComponentName = meta.LowerComponentName ?? string.Empty,
                IsVisible    = meta.IsVisible,
                IsEnabled    = true,
                SubPaneName       = meta.SubPaneName,
                SubPaneHeightRatio = meta.SubPaneHeightRatio,
                Volume       = 1.0f,

                // Colors — provider metadata wins; fall through to StylingService role defaults.
                ColorSource        = meta.DefaultColorSource  ?? _stylingService.GetColorSource(indicatorCode, meta.Name),
                ColorHex           = meta.DefaultColorHex     ?? _stylingService.GetDefaultColor(indicatorCode, meta.Name, type),
                ColorHexSecondary  = meta.DefaultColorHexSecondary ?? _stylingService.GetSecondaryColor(indicatorCode, meta.Name, type),
                ColorBaseline      = meta.ColorBaseline       ?? _stylingService.GetColorBaseline(indicatorCode, meta.Name),
                DashStyle          = meta.DefaultDashStyle    ?? DashStyle.Solid,

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
                // ReferenceLevel priority chain (highest → lowest):
                //   1. Provider metadata DefaultReferenceLevel (e.g. CipherB MF at -80)
                //   2. StylingService hard-code (RSI→50, MACD→0, STOCH→50)
                //   3. Oscillator/ZeroArea type default (0.0) — enables above/below waveform split
                //   4. Non-zero ColorBaseline fallback for histograms anchored away from zero
                ReferenceLevel     = meta.DefaultReferenceLevel
                                     ?? _stylingService.GetReferenceLevel(indicatorCode, meta.Name, type)
                                     ?? (type is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea ? 0.0 : (double?)null)
                                     ?? (meta.ColorBaseline.HasValue && meta.ColorBaseline.Value != 0.0 ? meta.ColorBaseline : null),
                IsAreaFill         = meta.DefaultIsAreaFill ?? _stylingService.GetIsAreaFill(indicatorCode, meta.Name, type),
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

        private ComponentConfig CloneComponent(ComponentConfig c)
        {
            return new ComponentConfig
            {
                Name = c.Name, DisplayName = c.DisplayName, DisplayType = c.DisplayType, Role = c.Role, ColorSource = c.ColorSource,
                AmplitudeMapping = c.AmplitudeMapping, PitchMapping = c.PitchMapping, ColorHex = c.ColorHex, ColorHexSecondary = c.ColorHexSecondary,
                Waveform = c.Waveform, AboveReferenceWaveform = c.AboveReferenceWaveform, BelowReferenceWaveform = c.BelowReferenceWaveform,
                FreqMultiplier = c.FreqMultiplier, TriggerBoundaryClick = c.TriggerBoundaryClick, EnvelopeType = c.EnvelopeType,
                Volume = c.Volume, Thickness = c.Thickness, DashStyle = c.DashStyle, IsEnabled = c.IsEnabled, IsVisible = c.IsVisible,
                BaseFrequency = c.BaseFrequency, BullishFrequency = c.BullishFrequency, BearishFrequency = c.BearishFrequency,
                NoiseAmount = c.NoiseAmount,
                SpeechTemplate = c.SpeechTemplate, ReferenceLevel = c.ReferenceLevel,
                IsAreaFill = c.IsAreaFill, UsePolarityColoring = c.UsePolarityColoring,
                ColorBaseline = c.ColorBaseline, DataMapping = c.DataMapping, SoundPatchId = c.SoundPatchId,
                BullishSoundPatchId = c.BullishSoundPatchId, BearishSoundPatchId = c.BearishSoundPatchId,
                UsesGradientSpeech = c.UsesGradientSpeech,
                // Cloud component bounds — must survive the clone or RenderCloud will silently skip.
                UpperComponentName = c.UpperComponentName, LowerComponentName = c.LowerComponentName,
                ColorRules = new List<ColorRule>(c.ColorRules),
                SubPaneName = c.SubPaneName, SubPaneHeightRatio = c.SubPaneHeightRatio,
                DecayMs = c.DecayMs, PlaybackLayer = c.PlaybackLayer,
                IsZoneLine = c.IsZoneLine,
                SignalSpeechTemplate = c.SignalSpeechTemplate,
                SubscribedLevelNames = c.SubscribedLevelNames,
                DeviationNorm = c.DeviationNorm,
            };
        }
    }
}
