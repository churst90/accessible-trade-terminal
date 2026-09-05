using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface ISeriesManagementService
    {
        void RegisterSeries(string id, string n, List<string> c, Dictionary<string, double>? parameters = null);
        /// <summary>
        /// Registers a series from full metadata, preserving CloudFills and component styling.
        /// Prefer this over <see cref="RegisterSeries"/> when adding indicators from the UI.
        /// </summary>
        // Parameters are now object-typed so the modal can pass strings (provider names,
        // symbol selections) for cross-series indicators. The implementation formats each
        // value via FormatParam below — doubles use invariant "G", ints use plain text,
        // strings pass through unchanged. Existing callers that pass null continue to work.
        void RegisterSeriesFromMetadata(IndicatorMetadata meta, Dictionary<string, object>? parameters = null);
        /// <summary>
        /// Restores a series from a previously-saved <see cref="SeriesConfig"/> (e.g. loaded
        /// from a workspace profile). The config is used as-is so user-customised colors,
        /// levels, and parameters are preserved exactly as they were when saved.
        /// </summary>
        void RegisterSeriesFromConfig(SeriesConfig config);
        /// <summary>
        /// Smart workspace restore: rebuilds the series through <see cref="IIndicatorModelFactory"/>
        /// so current metadata defaults (waveforms, colors, envelope types) are always applied,
        /// with the saved config supplying only the lightweight Layer-2 user-state overrides
        /// (visibility, mute, volume, freq multiplier). Falls back to <see cref="RegisterSeriesFromConfig"/>
        /// for core series (Candles/Volume/Price/Heatmap) and any indicator whose code is not
        /// found in the current metadata catalogue.
        /// </summary>
        void RestoreSeriesFromSaved(SeriesConfig config, IndicatorMetadata? meta);
        /// <summary>Saves the current series layout to the default workspace profile.</summary>
        void PersistWorkspace();

        /// <summary>
        /// Registers a compiled <see cref="ICustomIndicator"/> as a new chart series.
        /// The indicator's <see cref="ICustomIndicator.Id"/> becomes the series <c>IndicatorCode</c>.
        /// </summary>
        void AddCustomIndicator(ICustomIndicator indicator, WorkspaceState state);
        /// <summary>
        /// Clears all levels on the series and re-injects fresh provider defaults.
        /// Does NOT apply saved IndicatorPreferencesService overrides — call
        /// <see cref="IIndicatorPreferencesService.ClearPreferences"/> first if you
        /// want pure provider defaults with no type-default overrides.
        /// </summary>
        void ResetLevelsToProviderDefaults(ChartSeries series);
    }

    public class SeriesManagementService : ISeriesManagementService
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly IIndicatorModelFactory _modelFactory;
        private readonly IStylingService _stylingService;
        private readonly IWorkspaceLibraryService _library;
        private readonly AccessibleTrader.Core.Services.Indicators.ICustomIndicatorRegistry _customRegistry;
        private readonly Indicators.IIndicatorEngine _indicatorEngine;
        private readonly IIndicatorPreferencesService _indicatorPrefs;

        private const string DefaultProfileName = "default";

        public SeriesManagementService(
            IWorkspaceStore store,
            IEventBus eventBus,
            IIndicatorModelFactory modelFactory,
            IStylingService stylingService,
            IWorkspaceLibraryService library,
            AccessibleTrader.Core.Services.Indicators.ICustomIndicatorRegistry customRegistry,
            Indicators.IIndicatorEngine indicatorEngine,
            IIndicatorPreferencesService indicatorPrefs)
        {
            _store = store;
            _eventBus = eventBus;
            _modelFactory = modelFactory;
            _stylingService = stylingService;
            _library = library;
            _customRegistry = customRegistry;
            _indicatorEngine = indicatorEngine;
            _indicatorPrefs = indicatorPrefs;
        }

        public void RegisterSeries(string id, string n, List<string> c, Dictionary<string, double>? parameters = null)
        {
            string indicatorCode = string.IsNullOrEmpty(id) ? n : id;
            string codeUp = indicatorCode.ToUpperInvariant();

            // Core series use fixed well-known IDs so they remain singletons.
            // All other indicator instances get a GUID — this allows multiple EMA, RSI etc. with different parameters.
            string seriesId;
            if      (codeUp == "PRICE")   seriesId = CoreSeriesIds.Price;
            else if (codeUp == "CANDLES") seriesId = CoreSeriesIds.Candles;
            else if (codeUp == "VOLUME")  seriesId = CoreSeriesIds.Volume;
            else if (codeUp == "HEATMAP") seriesId = CoreSeriesIds.Heatmap;
            else                           seriesId = Guid.NewGuid().ToString();

            // Check if series already exists
            var existingSeries = _store.State.ActiveSeries.FirstOrDefault(s => s.Id.Equals(seriesId, StringComparison.OrdinalIgnoreCase));
            if (existingSeries != null)
            {
                _store.Dispatch(new SelectSeriesAction(existingSeries.Id));
                if (!existingSeries.IsVisible) _store.Dispatch(new ToggleHideAction(existingSeries.Id, null));
                return;
            }

            var config = new SeriesConfig
            {
                Id = seriesId,
                Name = n,
                FriendlyName = n,
                IndicatorCode = indicatorCode,
                Pane = _stylingService.GetPane(indicatorCode)
            };

            if (parameters != null)
            {
                foreach (var p in parameters) config.Parameters[p.Key] = p.Value;

                // The metadata-free path: no declared parameters, so no order to name them in and
                // no defaults to fall back to. It gets the one reduction that needs neither —
                // values only when there IS a sibling to be told apart from — and then a length
                // cap, because without knowing which values are the indicator's own, dropping one
                // could drop the only thing distinguishing two instances.
                if (SiblingParameterSets(indicatorCode, exceptSeriesId: null).Count > 0)
                {
                    config.FriendlyName = IndicatorInstanceName.ForValues(
                        n, parameters.Values.Select(v => v.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }

            // Ensure components are populated. Uses the new snake_case machine names
            // introduced in Phase 2; also accepts legacy names ("Upper Wick", etc.) in
            // the DataMapping fallback below so old SeriesConfig payloads still resolve.
            if (codeUp == "CANDLES" && !c.Any()) c = new List<string> { "upper_wick", "body", "lower_wick" };
            if (codeUp == "PRICE"   && !c.Any()) c = new List<string> { "line" };
            if (codeUp == "VOLUME"  && !c.Any()) c = new List<string> { "Volume" };
            if (!c.Any()) c = new List<string> { "Default" };

            foreach (var componentName in c)
            {
                var comp = _modelFactory.CreateComponentConfig(indicatorCode, componentName);
                if (seriesId == CoreSeriesIds.Price || seriesId == CoreSeriesIds.Candles)
                {
                    // New machine names first, then legacy names for backwards compat
                    // with workspaces saved before the Phase 2 rename.
                    if      (componentName == "upper_wick" || componentName == "Upper Wick") comp.DataMapping = "high";
                    else if (componentName == "lower_wick" || componentName == "Lower Wick") comp.DataMapping = "low";
                    else if (componentName == "body" || componentName == "line"
                          || componentName == "Candle Body" || componentName == "Close")    comp.DataMapping = "close";
                }
                if (seriesId == CoreSeriesIds.Volume && (componentName == "Volume" || componentName == "Bars")) comp.DataMapping = "volume";
                
                config.Components.Add(comp);
            }

            bool isProfile = codeUp == "VPVR" || codeUp == "VPFR" || codeUp == "TPO" ||
                             codeUp.Contains("VOLUME PROFILE") || codeUp.Contains("MARKET PROFILE");
            bool isHeatmap = codeUp == "HEATMAP";

            if (isProfile || isHeatmap)
            {
                foreach (var comp in config.Components) comp.DisplayType = isHeatmap ? ComponentDisplayType.Heatmap : ComponentDisplayType.Distribution;
            }

            // A fixed-range profile has to record WHICH range, and the only moment that information
            // exists is now — the viewport the user was looking at when they added it. Recorded as
            // timestamps so that loading older history later cannot slide the profile onto a
            // different stretch of chart, which bar indices would.
            if (isProfile && !ProfileAnchoring.FollowsViewport(codeUp))
            {
                config.Parameters ??= new();
                if (!config.Parameters.ContainsKey(ProfileAnchoring.AnchorStartParam))
                {
                    var st = _store.State;
                    if (st.Data != null && st.Data.Count > 0)
                        ProfileAnchoring.CaptureAnchor(config.Parameters, st.Data.ToList(),
                            st.ViewportStartIndex, st.ViewportLength);
                }
            }

            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = seriesId })
            {
                IsProfile = isProfile || isHeatmap
            };

            // Auto-inject reference levels so oscillators and zero-line indicators
            // display threshold lines immediately without user having to add them manually.
            InjectDefaultLevels(series, codeUp, config.Parameters ?? new());

            _store.Dispatch(new AddSeriesAction(series));

            if (codeUp != "CANDLES" && codeUp != "PRICE" && codeUp != "VOLUME")
            {
                _eventBus.Publish(new IndicatorUpdatedEvent(seriesId));
            }
        }

        public void RegisterSeriesFromMetadata(IndicatorMetadata meta, Dictionary<string, object>? parameters = null)
        {
            string indicatorCode = meta.Code;
            string codeUp = indicatorCode.ToUpperInvariant();

            // Core series use fixed well-known IDs; all others get a GUID.
            string? restoreId;
            if      (codeUp == "PRICE")   restoreId = CoreSeriesIds.Price;
            else if (codeUp == "CANDLES") restoreId = CoreSeriesIds.Candles;
            else if (codeUp == "VOLUME")  restoreId = CoreSeriesIds.Volume;
            else if (codeUp == "HEATMAP") restoreId = CoreSeriesIds.Heatmap;
            else                           restoreId = null; // factory generates GUID

            // Determine the actual series ID for duplicate checks.
            string seriesId = restoreId ?? Guid.NewGuid().ToString();
            // For non-core (GUID) we must check after factory call; for core, check now.
            if (restoreId != null)
            {
                var existingSeries = _store.State.ActiveSeries.FirstOrDefault(s => s.Id.Equals(restoreId, StringComparison.OrdinalIgnoreCase));
                if (existingSeries != null)
                {
                    _store.Dispatch(new SelectSeriesAction(existingSeries.Id));
                    if (!existingSeries.IsVisible) _store.Dispatch(new ToggleHideAction(existingSeries.Id, null));
                    return;
                }
            }

            // Convert parameters dictionary to factory tuple list. FormatParam handles the
            // object → string conversion for each supported parameter type.
            var paramList = parameters?.Select(kvp => (kvp.Key, FormatParam(kvp.Value))).ToList()
                            ?? new List<(string, string)>();

            // Build the instance name from what ELSE of this indicator is already on the chart.
            // Alone it is called what it is called; with siblings the suffix is the parameters
            // the cohort disagrees on. See IndicatorInstanceName for the rule and the two reports
            // behind it.
            var siblings = SiblingParameterSets(indicatorCode, exceptSeriesId: restoreId);
            string instanceName = IndicatorInstanceName.For(meta, parameters, siblings);

            string pane = meta.DefaultPane ?? _stylingService.GetPane(indicatorCode);

            // Use the CORRECT factory path — CreateSeriesFromMetadata applies all Default* metadata
            // fields (colors, waveforms, envelope types, thicknesses) via CreateComponentConfigFromMeta.
            var series = _modelFactory.CreateSeriesFromMetadata(
                meta,
                instanceName,
                pane,
                paramList,
                componentOverrides: null,
                restoreId: restoreId);

            // InjectDefaultLevels is the service layer's responsibility — the factory has no access to
            // the user's saved level preferences — so it is called here, after factory creation.
            InjectDefaultLevels(series, codeUp, series.Config.Parameters ?? new());

            _store.Dispatch(new AddSeriesAction(series));

            // A distinguishing suffix on ONE of a pair does not distinguish. The first EMA was
            // named "EMA" while it was alone and has to become "EMA 20" the moment a second
            // arrives, so the whole cohort is re-named together after the add — including the
            // one just added, whose siblings list did not yet contain the others' final names.
            RenameCohort(indicatorCode, meta);

            // Clear any stale saved ratio for a newly-added non-main pane so it starts at
            // equal-weight height rather than inheriting a previous drag value.
            if (series.Config.Pane != "Main")
            {
                var currentRatios = _store.State.PaneHeightRatios;
                if (currentRatios != null && currentRatios.ContainsKey(series.Config.Pane))
                    _store.Dispatch(new SetPaneHeightRatiosAction(currentRatios.Remove(series.Config.Pane)));
            }

            if (codeUp != "CANDLES" && codeUp != "PRICE" && codeUp != "VOLUME")
                _eventBus.Publish(new IndicatorUpdatedEvent(series.Config.Id));
        }

        // ── COHORT NAMING ────────────────────────────────────────────────────────────────
        //
        // "Cohort" is every series on the chart running the same indicator. A name only has to
        // distinguish within one, so the cohort is what the name is computed against and the
        // cohort is what gets renamed when it changes size.

        /// <summary>
        /// The parameter sets of the other instances of <paramref name="indicatorCode"/> already
        /// on the chart. <paramref name="exceptSeriesId"/> excludes a series being RESTORED into
        /// its own id — without it a workspace reload would see each series as its own sibling.
        /// </summary>
        private IReadOnlyList<IReadOnlyDictionary<string, object>> SiblingParameterSets(
            string indicatorCode, string? exceptSeriesId)
            => _store.State.ActiveSeries
                .Where(s => !s.IsDrawing
                         && string.Equals(s.Config.IndicatorCode, indicatorCode, StringComparison.OrdinalIgnoreCase)
                         && (exceptSeriesId == null || !string.Equals(s.Id, exceptSeriesId, StringComparison.OrdinalIgnoreCase)))
                .Select(s => ParameterSetOf(s.Config))
                .ToList();

        /// <summary>
        /// One series' parameters as the <c>object</c> dictionary the namer compares.
        ///
        /// <para>
        /// BOTH halves, and that is the whole reason this is a method. A SeriesConfig splits its
        /// parameters into a numeric <c>Parameters</c> and a <c>StringParameters</c> beside it —
        /// the same split that once reset every string parameter to its default on a workspace
        /// reload, because a caller took the numeric half for the whole set. Two Moving Averages
        /// that differ only in MA type differ in the STRING half; reading the numeric half alone
        /// would find nothing to disagree about and name them both by ordinal.
        /// </para>
        /// </summary>
        private static IReadOnlyDictionary<string, object> ParameterSetOf(SeriesConfig config)
        {
            var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in config.Parameters) merged[kv.Key] = kv.Value;
            foreach (var kv in config.StringParameters) merged[kv.Key] = kv.Value;
            return merged;
        }

        /// <summary>
        /// Re-derives the name of every instance of one indicator, each against the others.
        ///
        /// <para>
        /// Run after an ADD, which is the direction that creates ambiguity: adding a second EMA
        /// has to turn the first from "EMA" into "EMA 20", or the suffix distinguishes one of a
        /// pair from nothing.
        /// </para>
        ///
        /// <para>
        /// <b>Deliberately NOT run after a remove.</b> Deleting the second EMA leaves the first
        /// called "EMA 20" — one word longer than it needs to be, but still correct, still
        /// unique, and still the name the user has been hearing for that object all session.
        /// The alternative costs more than it saves: removal has eight call sites, several of
        /// them bulk (workspace load clears the chart series by series), and a rename firing per
        /// removal during a load would rename a cohort repeatedly against a shrinking set. The
        /// name is re-derived the next time the cohort grows.
        /// </para>
        ///
        /// <para>
        /// Mutates the configs in place and dispatches one <c>UpdateSeriesAction</c>, the pattern
        /// <c>WorkspaceInitializer</c> already uses to push a config edit through the store.
        /// </para>
        /// </summary>
        private void RenameCohort(string indicatorCode, IndicatorMetadata meta)
        {
            var cohort = _store.State.ActiveSeries
                .Where(s => !s.IsDrawing
                         && string.Equals(s.Config.IndicatorCode, indicatorCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (cohort.Count == 0) return;

            bool changed = false;
            for (int i = 0; i < cohort.Count; i++)
            {
                var s = cohort[i];
                var siblings = cohort.Where(o => o != s).Select(o => ParameterSetOf(o.Config)).ToList();

                // The ordinal is this instance's POSITION, not the cohort's size. The first
                // version passed nothing and the namer fell back to siblings + 1 — which for a
                // pair of identically-tuned indicators named BOTH of them "2".
                string name = IndicatorInstanceName.For(meta, ParameterSetOf(s.Config), siblings, ordinal: i + 1);
                if (string.Equals(s.Config.FriendlyName, name, StringComparison.Ordinal)) continue;
                s.Config.FriendlyName = name;
                changed = true;
            }

            if (changed) _store.Dispatch(new UpdateSeriesAction(_store.State.ActiveSeries));
        }

        public void RegisterSeriesFromConfig(SeriesConfig config)
        {
            // Use the saved config directly — preserves colors, levels, parameters.
            // Drawing rehydrates from the persisted anchors; the component arrays
            // start empty and IndicatorOrchestrator recomputes them from the
            // anchors as soon as chart data is available (its IsDrawing branch).
            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = config.Id })
            {
                IsProfile = config.IndicatorCode.ToUpperInvariant() is "VPVR" or "VPFR" or "TPO"
                         || config.IndicatorCode.ToUpperInvariant().Contains("PROFILE"),
                Drawing = config.Drawing
            };
            _store.Dispatch(new AddSeriesAction(series));
            // No PersistWorkspace here — restoring from a saved profile must not overwrite it.

            string codeUp = config.IndicatorCode.ToUpperInvariant();
            if (codeUp != "CANDLES" && codeUp != "PRICE" && codeUp != "VOLUME")
            {
                _eventBus.Publish(new IndicatorUpdatedEvent(config.Id));
            }
        }

        public void RestoreSeriesFromSaved(SeriesConfig config, IndicatorMetadata? meta)
        {
            // Core series and unknown indicators fall back to direct restore.
            // Core series have no provider metadata and must use their pre-built configs verbatim.
            if (meta == null || IsCoreCode(config.IndicatorCode))
            {
                RegisterSeriesFromConfig(config);
                return;
            }

            // Convert saved parameters to the factory's tuple list format. Both dictionaries:
            // restoring only the numeric half would silently reset every string parameter
            // (comparison symbol, MA type, pivot period, threshold mode) to its metadata
            // default on the next workspace load, which is the drop this fix exists to stop.
            var parameters = config.Parameters
                .Select(kvp => (kvp.Key, kvp.Value.ToString("G")))
                .Concat(config.StringParameters.Select(kvp => (kvp.Key, kvp.Value)))
                .ToList();

            // THE NAME IS DERIVED, NOT RESTORED.
            //
            // Until 2026-09-05 this pinned the saved Name and FriendlyName back onto the fresh
            // series ("ensure the exact saved name is preserved"), and that is how the parameter
            // recitation the nineteenth pass removed came back the next morning: Cody, 2026-09-05,
            // "when I move through series, I now hear AGAIN all of the parameters in a huge list".
            // His workspaces were saved when the namer joined every value onto the name —
            // "Cipher SR 5 20 1.2 0.2 1 14 1", "Value Deviation (scale-in tiers) 480 5 2 0 0" —
            // and the restore path handed those strings straight back to navigation, so the fix
            // was invisible on every chart that already existed. A workspace does not own an
            // indicator's name; it owns the parameters the name is computed from. An indicator's
            // name is never user-typed (the Properties rename box is for drawings, which take the
            // meta == null path above), so there is nothing here to preserve.
            var siblings = SiblingParameterSets(config.IndicatorCode, exceptSeriesId: config.Id);
            string instanceName = IndicatorInstanceName.For(meta, ParameterSetOf(config), siblings);

            // Build fresh series through the factory (3-layer merge):
            //   Layer 1: current provider metadata defaults (waveforms, colors, envelope types)
            //   Layer 2: saved component state (visibility/mute/volume/FreqMultiplier only)
            //   Layer 3: IIndicatorPreferencesService preferences (applied inside factory)
            // Pass the saved ID so pane ratios and other ID-keyed state remain valid.
            var freshSeries = _modelFactory.CreateSeriesFromMetadata(
                meta,
                instanceName,
                config.Pane,
                parameters,
                config.Components.ToList(),
                restoreId: config.Id);

            // Restore user-saved CloudFills (MigrateSeriesConfig already merged any new defaults into them).
            if (config.CloudFills.Count > 0)
            {
                freshSeries.Config.CloudFills.Clear();
                foreach (var fill in config.CloudFills)
                    freshSeries.Config.CloudFills.Add(fill.Clone());
            }

            // Restore user-saved ZoneBands.
            if (config.ZoneBands.Count > 0)
            {
                freshSeries.Config.ZoneBands.Clear();
                foreach (var band in config.ZoneBands)
                    freshSeries.Config.ZoneBands.Add(band.Clone());
            }

            // Restore series-level mute/volume/visibility from the saved config.
            freshSeries.Config.IsMuted    = config.IsMuted;
            freshSeries.Config.Volume     = config.Volume;
            freshSeries.Config.IsVisible  = config.IsVisible;

            // Restore levels from workspace-saved config when available (per-instance source of
            // truth).  This prevents global IndicatorPreferencesService type-defaults from
            // silencing zone noise that the user set independently on each indicator instance.
            // Fall back to InjectDefaultLevels only for old saves that predated level persistence.
            if (config.Levels.Count > 0)
            {
                freshSeries.Config.Levels.Clear();
                foreach (var l in config.Levels) freshSeries.Config.Levels.Add(l.Clone());
            }
            else
            {
                // Backward-compat: old workspace save with no Levels → inject provider defaults
                // and apply any global type-defaults from IndicatorPreferencesService.
                InjectDefaultLevels(freshSeries, config.IndicatorCode.ToUpperInvariant(), freshSeries.Config.Parameters ?? new());
            }

            _store.Dispatch(new AddSeriesAction(freshSeries));

            // Same as an add: the cohort is re-named against its new size, so the EMA restored
            // first — alone at the time, called "EMA" — becomes "EMA 20" when the second one
            // lands. A load restores series one at a time, so this runs once per series of the
            // cohort; RenameCohort dispatches only when a name actually changed.
            RenameCohort(config.IndicatorCode, meta);

            // No PersistWorkspace — restoring must not overwrite the saved profile.
            _eventBus.Publish(new IndicatorUpdatedEvent(config.Id));
        }

        private static bool IsCoreCode(string code) =>
            code.ToUpperInvariant() is "CANDLES" or "PRICE" or "VOLUME" or "HEATMAP";

        /// <summary>
        /// Format a parameter value for the factory tuple list. Numeric values use invariant
        /// "G" formatting, strings pass through unchanged. Used by the modal-driven add path
        /// since the introduction of string-typed parameters for cross-series indicators.
        /// </summary>
        private static string FormatParam(object? v) => v switch
        {
            null     => string.Empty,
            string s => s,
            double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            float f  => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            int i    => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l   => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b   => b ? "true" : "false",
            IConvertible c => c.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _        => v.ToString() ?? string.Empty
        };

        public void PersistWorkspace()
        {
            var state = _store.State;
            var cfg = new WorkspaceConfiguration
            {
                Series = state.ActiveSeries.Select(s => s.Config).ToList(),
                PaneHeightRatios = state.PaneHeightRatios != null
                    ? new Dictionary<string, float>(state.PaneHeightRatios)
                    : new()
            };
            _library.SaveProfile(DefaultProfileName, cfg);
        }

        /// <summary>
        /// Adds reference level lines to a newly created indicator series.
        ///
        /// <para>
        /// Two tiers, in order: the provider's own <c>GetDefaultLevels(code)</c>, then the user's
        /// saved per-indicator level preferences on top. User-supplied threshold parameters (custom
        /// overbought/oversold values) override the defaults for bounded oscillators.
        /// </para>
        ///
        /// <para>
        /// <b>A default level must be in the units of the pane the indicator lands on</b>, and pane
        /// assignment is decided by <see cref="PaneAssignmentService"/> from the indicator code —
        /// not by the provider's own <c>DefaultPane</c>, which several providers set to a value the
        /// assignment service never returns. A fixed constant can never be a price, so no indicator
        /// that resolves to the "Main" pane may declare one; <c>MainPaneLevelUnitsTests</c> enforces
        /// that across every provider. The consequence of getting it wrong is not cosmetic — the
        /// viewport expands the price range to reach any visible main-pane level.
        /// </para>
        /// </summary>
        private void InjectDefaultLevels(ChartSeries series, string codeUp, Dictionary<string, double> parameters)
        {
            var provider = _indicatorEngine.GetProvider(codeUp);
            IReadOnlyList<LevelDescriptor> defaults =
                provider != null ? provider.GetDefaultLevels(codeUp) : System.Array.Empty<LevelDescriptor>();

            if (defaults.Count == 0) return;

            // Layer 1: provider defaults.
            foreach (var desc in defaults)
            {
                // Allow user-supplied parameters to override OB/OS thresholds.
                double finalValue = desc.Value;
                if (desc.Name == "Overbought" && parameters.TryGetValue("Overbought", out double ub)) finalValue = ub;
                if (desc.Name == "Oversold"   && parameters.TryGetValue("Oversold",   out double lb)) finalValue = lb;

                series.Levels.Add(new LevelConfig
                {
                    Name            = desc.Name,
                    Value           = finalValue,
                    ColorHex        = desc.ColorHex,
                    DashStyle       = desc.Dash,
                    IsVisible       = true,
                    PlayEarcon      = desc.PlayEarcon,
                    EarconVolume    = desc.EarconVolume,
                    ZoneNoiseAmount = desc.ZoneNoiseAmount,
                    ZoneNoiseType   = desc.ZoneNoiseType,
                });
            }

            // Layer 3: apply saved level preferences (user overrides) on top of provider defaults.
            var savedLevelPrefs = _indicatorPrefs.GetLevelPreferences(codeUp);
            foreach (var lp in savedLevelPrefs)
            {
                var target = series.Levels.FirstOrDefault(l => l.Name == lp.Name);
                if (target == null) continue;
                if (lp.Value.HasValue)           target.Value           = lp.Value.Value;
                if (lp.IsVisible.HasValue)        target.IsVisible       = lp.IsVisible.Value;
                if (lp.PlayEarcon.HasValue)       target.PlayEarcon      = lp.PlayEarcon.Value;
                if (lp.EarconVolume.HasValue)     target.EarconVolume    = lp.EarconVolume.Value;
                if (lp.ZoneNoiseAmount.HasValue)  target.ZoneNoiseAmount = lp.ZoneNoiseAmount.Value;
                if (lp.ZoneNoiseType != null)     target.ZoneNoiseType   = lp.ZoneNoiseType;
                if (lp.ColorHex != null)          target.ColorHex        = lp.ColorHex;
                if (lp.Thickness.HasValue)        target.Thickness       = lp.Thickness.Value;
                if (lp.DashStyle.HasValue)        target.DashStyle       = lp.DashStyle.Value;
                if (lp.CrossDirection.HasValue)   target.CrossDirection  = lp.CrossDirection.Value;
            }
        }

        /// <summary>
        /// Restores the provider's own levels, <b>keeping any the user placed by hand</b>.
        ///
        /// <para>
        /// "Reset to defaults" means restore what the indicator ships with. A level someone marked on
        /// the chart is not part of the indicator's configuration — it is their annotation, it may be
        /// the price they are watching, and wiping it as collateral damage of resetting an unrelated
        /// colour would be indefensible. Provider levels are keyed by name, so a user level cannot be
        /// re-injected as a duplicate either.
        /// </para>
        /// </summary>
        public void ResetLevelsToProviderDefaults(ChartSeries series)
        {
            var codeUp = series.Config?.IndicatorCode?.ToUpperInvariant() ?? "";
            var userLevels = series.Levels.Where(l => l.IsUserDefined).Select(l => l.Clone()).ToList();

            series.Levels.Clear();
            InjectDefaultLevels(series, codeUp, series.Config?.Parameters ?? new());

            foreach (var l in userLevels)
                if (!series.Levels.Any(x => string.Equals(x.Name, l.Name, StringComparison.OrdinalIgnoreCase)))
                    series.Levels.Add(l);
        }

        public void AddCustomIndicator(ICustomIndicator indicator, WorkspaceState state)
        {
            // Register in the runtime lookup table so IndicatorEngine.CalculateAsync
            // can route to indicator.Calculate() instead of IIndicatorService.
            _customRegistry.Register(indicator);

            RegisterSeries(
                indicator.Id,
                indicator.DisplayName,
                indicator.ComponentNames.ToList(),
                indicator.DefaultParameters);
        }
    }
}
