using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Builds a <see cref="HeadlessChartNarrator"/> for a saved tab: the indicator stack the
    /// background monitor needs, resolved once from the headless scope, and the restore rules a
    /// workspace load applies — run here with no store to dispatch into.
    ///
    /// <para>
    /// ── Why a saved tab's series are REBUILT and not just read ─────────────────
    /// A workspace file owns an indicator's parameters and switches, not its name and not its
    /// component defaults (see <c>SeriesManagementService.RestoreSeriesFromSaved</c>). Cody's
    /// own saved tabs still carry friendly names from the era when the namer recited every
    /// parameter — "Cipher SR 5 20 1.2 0.2 1 14 1" — and a headless ladder that spoke those would
    /// bring back a defect the twentieth pass removed, on the one path that had not been fixed.
    /// So each saved config goes through <c>MigrateSeriesConfig</c> and
    /// <c>MaterializeSaved</c>, exactly as it does on a load: the name is derived against its
    /// cohort, the components come from current metadata with the saved N selection on top, and
    /// the levels are the saved ones or the provider's defaults.
    /// </para>
    /// </summary>
    public sealed class HeadlessNarration
    {
        private readonly IIndicatorService _indicators;
        private readonly IIndicatorEngine _engine;
        private readonly IIndicatorStateMapper _mapper;
        private readonly IIndicatorModelFactory _factory;
        private readonly IIndicatorPreferencesService _prefs;
        private readonly IIndicatorContextAnalyzer _analyzer;
        private readonly IPluginLoaderService? _plugins;
        private readonly ILogger<HeadlessNarration>? _logger;

        public HeadlessNarration(
            IIndicatorService indicators,
            IIndicatorEngine engine,
            IIndicatorStateMapper mapper,
            IIndicatorModelFactory factory,
            IIndicatorPreferencesService prefs,
            IIndicatorContextAnalyzer analyzer,
            IPluginLoaderService? plugins = null,
            ILogger<HeadlessNarration>? logger = null)
        {
            _indicators = indicators;
            _engine = engine;
            _mapper = mapper;
            _factory = factory;
            _prefs = prefs;
            _analyzer = analyzer;
            _plugins = plugins;
            _logger = logger;
        }

        /// <summary>Whether any saved series on the tab carries the N flag. Cheap; asked before
        /// anything is fetched or built.</summary>
        public static bool HasNarratedSeries(IEnumerable<SeriesConfig>? saved)
            => saved != null && saved.Any(s => s.IsAutoNarrated && s.Drawing == null);

        /// <summary>
        /// Everything about a saved tab that changes what the ladder would say, in one string —
        /// which series are under N, their component selections, hidden and muted flags,
        /// parameters and levels. The monitor keeps a narrator (and its memory of what has
        /// already been said) for as long as this is unchanged, and rebuilds it when it is not.
        /// Colours and waveforms are deliberately absent: a save that only restyled something
        /// must not cost the warm seed.
        /// </summary>
        public static string Signature(IEnumerable<SeriesConfig>? saved)
        {
            if (saved == null) return "";
            var parts = new List<string>();
            foreach (var s in saved.Where(s => s.Drawing == null).OrderBy(s => s.Id, StringComparer.Ordinal))
            {
                parts.Add($"{s.Id}|{s.IndicatorCode}|{s.Pane}|{(s.IsAutoNarrated ? 1 : 0)}{(s.IsVisible ? 1 : 0)}{(s.IsMuted ? 1 : 0)}");
                foreach (var c in s.Components)
                    parts.Add($"  c:{c.Name}|{(c.IsAutoNarrated ? 1 : 0)}{(c.IsVisible ? 1 : 0)}{(c.IsMuted ? 1 : 0)}");
                foreach (var p in s.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
                    parts.Add($"  p:{p.Key}={p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
                foreach (var p in s.StringParameters.OrderBy(p => p.Key, StringComparer.Ordinal))
                    parts.Add($"  s:{p.Key}={p.Value}");
                foreach (var l in s.Levels)
                    parts.Add($"  l:{l.Name}={l.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}|{(l.IsVisible ? 1 : 0)}");
            }
            return string.Join("\n", parts);
        }

        /// <summary>
        /// The narrator for one saved chart. Only the series under N are rebuilt — the others
        /// cost nothing headless because nothing reads them.
        /// </summary>
        public HeadlessChartNarrator Create(ChartIdentity identity, IReadOnlyList<SeriesConfig> saved)
        {
            // Plugin indicators (Cipher and friends) are loaded lazily by the in-session startup;
            // the headless scope has to ask for them itself or every plugin code comes back with
            // no metadata and restores as a bare config with no components. Idempotent.
            if (_plugins != null)
            {
                try { _indicators.LoadIndicatorPlugins(_plugins); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Headless narration could not load indicator plugins."); }
            }

            var allMeta = _indicators.GetAvailableIndicators();
            var template = new List<ChartSeries>();

            foreach (var original in saved)
            {
                if (!original.IsAutoNarrated || original.Drawing != null) continue;

                try
                {
                    var config = original.Clone();
                    WorkspaceInitializer.MigrateSeriesConfig(config, allMeta);
                    var meta = allMeta.FirstOrDefault(m =>
                        m.Code.Equals(config.IndicatorCode, StringComparison.OrdinalIgnoreCase));

                    if (meta == null || IsCoreCode(config.IndicatorCode))
                    {
                        template.Add(SeriesManagementService.MaterializeVerbatim(config));
                        continue;
                    }

                    // The cohort is every saved instance of the same indicator on this tab,
                    // narrated or not — a lone narrated EMA 20 beside a silent EMA 50 is still
                    // "EMA 20", not "EMA".
                    var cohort = saved
                        .Where(s => s.Drawing == null
                                 && string.Equals(s.IndicatorCode, config.IndicatorCode, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var siblings = cohort
                        .Where(s => !string.Equals(s.Id, config.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(SeriesManagementService.ParameterSetOf)
                        .ToList();
                    int ordinal = cohort.FindIndex(s => string.Equals(s.Id, config.Id, StringComparison.OrdinalIgnoreCase)) + 1;

                    template.Add(SeriesManagementService.MaterializeSaved(
                        config, meta, _factory, siblings, _engine, _prefs, ordinal));
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Headless narration could not rebuild saved series {Code} on {Symbol}.",
                        original.IndicatorCode, identity.Symbol);
                }
            }

            return new HeadlessChartNarrator(identity, template, _engine, _mapper, _analyzer, _logger);
        }

        private static bool IsCoreCode(string code) =>
            code.ToUpperInvariant() is "CANDLES" or "PRICE" or "VOLUME" or "HEATMAP";
    }
}
