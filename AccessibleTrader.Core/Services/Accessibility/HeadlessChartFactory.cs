using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Builds a <see cref="HeadlessChart"/> for a saved tab and the alerts on it: the indicator
    /// stack the background monitor needs, resolved once from the headless scope, and the
    /// restore rules a workspace load applies — run here with no store to dispatch into.
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
    ///
    /// <para>
    /// ── Which series an ALERT gets ─────────────────────────────────────────────
    /// An alert names an indicator by CODE and a component by name — nothing else, because
    /// in-session it reads whichever instance of that indicator is on the chart
    /// (<c>AlertEvaluator</c> takes the first series with that code). Headless, the same rule:
    /// the first saved series on the tab with that code, narrated or not, rebuilt as above; and
    /// when the tab has none, the indicator's DEFAULTS — an alert on "RSI crosses 70" on a chart
    /// that no longer shows an RSI is still an alert on the 14-period RSI, which is what the
    /// user meant when they wrote it. A POC alert gets every profile on the tab; a tree alert
    /// gets every indicator its leaves reference, through the signal catalog. An indicator that
    /// cannot be built at all — a plugin not loaded here — is reported on
    /// <see cref="HeadlessChart.MissingIndicators"/> for the monitor to SAY.
    /// </para>
    /// </summary>
    public sealed class HeadlessChartFactory
    {
        private readonly IIndicatorService _indicators;
        private readonly IIndicatorEngine _engine;
        private readonly IIndicatorStateMapper _mapper;
        private readonly IIndicatorModelFactory _factory;
        private readonly IIndicatorPreferencesService _prefs;
        private readonly IIndicatorContextAnalyzer _analyzer;
        private readonly IPluginLoaderService? _plugins;
        private readonly IProfileService? _profiles;
        private readonly ISignalCatalog? _catalog;
        private readonly ILogger<HeadlessChartFactory>? _logger;

        public HeadlessChartFactory(
            IIndicatorService indicators,
            IIndicatorEngine engine,
            IIndicatorStateMapper mapper,
            IIndicatorModelFactory factory,
            IIndicatorPreferencesService prefs,
            IIndicatorContextAnalyzer analyzer,
            IPluginLoaderService? plugins = null,
            ILogger<HeadlessChartFactory>? logger = null,
            IProfileService? profiles = null,
            ISignalCatalog? catalog = null)
        {
            _indicators = indicators;
            _engine = engine;
            _mapper = mapper;
            _factory = factory;
            _prefs = prefs;
            _analyzer = analyzer;
            _plugins = plugins;
            _profiles = profiles;
            _catalog = catalog;
            _logger = logger;
        }

        /// <summary>Whether any saved series on the tab carries the N flag. Cheap; asked before
        /// anything is fetched or built.</summary>
        public static bool HasNarratedSeries(IEnumerable<SeriesConfig>? saved)
            => saved != null && saved.Any(s => s.IsAutoNarrated && s.Drawing == null);

        /// <summary>
        /// Everything about a saved tab that changes what the ladder would say or what the
        /// alerts would read, in one string — which series are under N, their component
        /// selections, hidden and muted flags, parameters and levels, and which alerts reference
        /// which indicators. The monitor keeps a chart (and its memory of what has already been
        /// said and seen) for as long as this is unchanged, and rebuilds it when it is not.
        /// Colours and waveforms are deliberately absent: a save that only restyled something
        /// must not cost the warm seed.
        /// </summary>
        public static string Signature(IEnumerable<SeriesConfig>? saved, IEnumerable<AlertDefinition>? alerts = null)
        {
            var parts = new List<string>();
            if (saved != null)
            {
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
            }
            if (alerts != null)
            {
                foreach (var a in alerts.Where(a => a.IsActive).OrderBy(a => a.Id, StringComparer.Ordinal))
                    parts.Add($"a:{a.Id}|{a.Target}|{a.IndicatorCode}|{a.ComponentName}|{a.SeriesId}|{a.Condition}|{(a.ConditionTree == null ? "" : a.ConditionTree.Id)}"
                              + $"|{a.LineIndicatorCode}|{a.LineComponentName}|{a.LineSeriesId}");
            }
            return string.Join("\n", parts);
        }

        /// <summary>The chart for one saved tab's narrated series. See the alerts overload.</summary>
        public HeadlessChart Create(ChartIdentity identity, IReadOnlyList<SeriesConfig> saved)
            => Create(identity, saved, Array.Empty<AlertDefinition>());

        /// <summary>
        /// The chart for one saved tab and the alerts on it. The series under N are rebuilt for
        /// the ladder; the series the alerts reference are rebuilt for the evaluator; a series
        /// that is both is built once. Everything else on the tab costs nothing headless because
        /// nothing reads it.
        /// </summary>
        public HeadlessChart Create(ChartIdentity identity, IReadOnlyList<SeriesConfig>? saved, IReadOnlyList<AlertDefinition> alerts)
        {
            saved ??= Array.Empty<SeriesConfig>();

            // Plugin indicators (Cipher and friends) are loaded lazily by the in-session startup;
            // the headless scope has to ask for them itself or every plugin code comes back with
            // no metadata and restores as a bare config with no components. Idempotent.
            if (_plugins != null)
            {
                try { _indicators.LoadIndicatorPlugins(_plugins); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Headless chart could not load indicator plugins."); }
            }

            var allMeta = _indicators.GetAvailableIndicators();
            var template = new List<ChartSeries>();
            var builtIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();

            // 1. The narrated series, as before.
            foreach (var original in saved)
            {
                if (!original.IsAutoNarrated || original.Drawing != null) continue;
                var built = Build(identity, original, saved, allMeta);
                if (built != null && builtIds.Add(original.Id)) template.Add(built);
            }

            // 2. The series the alerts read. One per indicator code — the evaluator takes the
            //    first series with the code, so a second instance would never be read.
            //
            //    NOT for a code every alert reaches through a named INSTANCE. Those are built
            //    under their saved id in step 3 or not at all: the evaluator answers an alert that
            //    names an instance from that instance only, and says once when it is gone. This
            //    step's two stand-ins — the first saved series with the code, or the indicator's
            //    DEFAULTS when the tab has none — would otherwise be a different series answering
            //    for it: an "SMA 50" alert whose SMA 50 was removed watched as an SMA 20.
            var instanceOnly = CodesReachedOnlyThroughInstances(alerts);
            foreach (var code in ReferencedIndicatorCodes(alerts, saved))
            {
                if (instanceOnly.Contains(code)) continue;
                if (template.Any(t => string.Equals(t.IndicatorCode, code, StringComparison.OrdinalIgnoreCase))) continue;

                var config = saved.FirstOrDefault(s => s.Drawing == null
                    && string.Equals(s.IndicatorCode, code, StringComparison.OrdinalIgnoreCase));
                if (config == null)
                {
                    var meta = allMeta.FirstOrDefault(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (meta == null) { missing.Add(code); continue; }
                    // The indicator's defaults, written onto the config the way the Add
                    // Indicator dialog writes its pre-filled fields: MaterializeSaved restores
                    // PARAMETERS and does not invent them, so a config with none would build an
                    // EMA with a lookback of zero (measured: "Lookback periods must be greater
                    // than 0 for EMA").
                    config = new SeriesConfig
                    {
                        Id = $"alert-{code}", IndicatorCode = meta.Code, Name = meta.Name, FriendlyName = meta.Name,
                        Pane = PaneAssignmentService.PaneFor(meta), IsVisible = true, IsAutoNarrated = false,
                    };
                    ApplyDefaultParameters(config, meta);
                }

                var built = Build(identity, config, saved, allMeta);
                if (built == null) { missing.Add(code); continue; }
                // An alert-only series never narrates, whatever the saved flag said — the saved
                // flag was false or it would already be in the template.
                built.Config.IsAutoNarrated = false;
                if (builtIds.Add(built.Id)) template.Add(built);
            }

            // 3. The INSTANCE an alert names, when it is not the first of its code. A tab with an
            //    SMA 20 and an SMA 50 built only the SMA 20 above, and "price touches the SMA 50"
            //    would have been answered by the wrong line. The evaluator finds a series by id
            //    first, so the instance has to exist under its saved id.
            foreach (var id in alerts.Where(a => a.IsActive && a.ConditionTree == null)
                                     .SelectMany(a => new[] { a.SeriesId, a.LineSeriesId })
                                     .Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                if (builtIds.Contains(id!)) continue;
                var config = saved.FirstOrDefault(s => s.Drawing == null
                    && string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (config == null) continue;   // gone from the tab: the evaluator says so, and watches nothing in its place
                var built = Build(identity, config, saved, allMeta);
                if (built == null) continue;
                built.Config.IsAutoNarrated = false;
                if (builtIds.Add(built.Id)) template.Add(built);
            }

            // 4. The instances a tree leaf is bound to by its parameters (2026-10). Step 2 builds
            //    one series per code because an unbound leaf reads the first; a leaf bound to the
            //    SMA 50 reads only an SMA 50, so a tab with an SMA 20 first and an SMA 50 second
            //    — or with no SMA 50 at all — would leave that leaf nothing to read headless.
            //    The saved instance with those parameters when there is one, else the defaults
            //    with the leaf's parameters written over them.
            foreach (var (code, parameters) in ReferencedInstances(alerts))
            {
                if (template.Any(t => string.Equals(t.IndicatorCode, code, StringComparison.OrdinalIgnoreCase)
                                   && ConditionEvaluator.ParametersMatch(t.Parameters, parameters))) continue;

                var config = saved.FirstOrDefault(s => s.Drawing == null
                    && string.Equals(s.IndicatorCode, code, StringComparison.OrdinalIgnoreCase)
                    && ConditionEvaluator.ParametersMatch(s.Parameters, parameters));
                if (config == null)
                {
                    var meta = allMeta.FirstOrDefault(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (meta == null) { missing.Add(code); continue; }
                    config = new SeriesConfig
                    {
                        Id = $"alert-{code}-{ConditionEvaluator.DescribeParameters(parameters)}",
                        IndicatorCode = meta.Code, Name = meta.Name, FriendlyName = meta.Name,
                        Pane = PaneAssignmentService.PaneFor(meta), IsVisible = true, IsAutoNarrated = false,
                    };
                    ApplyDefaultParameters(config, meta);
                    foreach (var kv in parameters) config.Parameters[kv.Key] = kv.Value;
                }

                var built = Build(identity, config, saved, allMeta);
                if (built == null) { missing.Add(code); continue; }
                built.Config.IsAutoNarrated = false;
                if (builtIds.Add(built.Id)) template.Add(built);
            }

            return new HeadlessChart(identity, template, _engine, _mapper, _analyzer, _logger, _profiles, missing);
        }

        /// <summary>
        /// The indicator codes the alerts on a chart read: the indicator target's, a trend or
        /// zone condition's, every leaf of a condition tree (via the signal catalog), and every
        /// profile on the saved tab for a POC alert.
        /// </summary>
        public IReadOnlyList<string> ReferencedIndicatorCodes(IEnumerable<AlertDefinition> alerts, IEnumerable<SeriesConfig> saved)
        {
            var codes = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? code) { if (!string.IsNullOrWhiteSpace(code) && seen.Add(code)) codes.Add(code); }

            foreach (var a in alerts)
            {
                if (!a.IsActive) continue;
                if (a.ConditionTree != null)
                {
                    CollectTreeCodes(a.ConditionTree, Add);
                    continue;
                }
                if (a.Target == AlertTarget.Indicator
                    || a.Condition is AlertCondition.TrendChange or AlertCondition.EntersZone or AlertCondition.ExitsZone)
                    Add(a.IndicatorCode);
                // "Price touches the 50-week SMA": the line is a series too.
                if (a.ComparesToLine())
                    Add(a.LineIndicatorCode);
                if (a.Target == AlertTarget.Poc)
                    foreach (var s in saved.Where(s => s.Drawing == null && ProfileAnchoring.IsProfileCode(s.IndicatorCode)))
                        Add(s.IndicatorCode);
            }
            return codes;
        }

        /// <summary>
        /// The codes that NO simple alert reads by code alone — every reference to them names the
        /// instance (<see cref="AlertDefinition.SeriesId"/> for the subject,
        /// <see cref="AlertDefinition.LineSeriesId"/> for a line). A code a tree or a code-only
        /// alert also reads is not in the set: those still read the first series with the code.
        /// </summary>
        private HashSet<string> CodesReachedOnlyThroughInstances(IEnumerable<AlertDefinition> alerts)
        {
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byCode = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in alerts.Where(a => a.IsActive))
            {
                if (a.ConditionTree != null)
                {
                    CollectTreeCodes(a.ConditionTree, c => { if (!string.IsNullOrWhiteSpace(c)) byCode.Add(c!); });
                    continue;
                }
                bool readsSubject = a.Target == AlertTarget.Indicator
                    || a.Condition is AlertCondition.TrendChange or AlertCondition.EntersZone or AlertCondition.ExitsZone;
                if (readsSubject && !string.IsNullOrWhiteSpace(a.IndicatorCode))
                    (string.IsNullOrWhiteSpace(a.SeriesId) ? byCode : named).Add(a.IndicatorCode!);
                if (a.ComparesToLine())
                    (string.IsNullOrWhiteSpace(a.LineSeriesId) ? byCode : named).Add(a.LineIndicatorCode!);
            }
            named.ExceptWith(byCode);
            return named;
        }

        /// <summary>The (indicator code, parameters) pairs tree-alert leaves are bound to on the
        /// chart's own timeframe — a higher-timeframe line is computed from HTF bars, not read
        /// from this chart.</summary>
        private IReadOnlyList<(string Code, IReadOnlyDictionary<string, double> Parameters)> ReferencedInstances(
            IEnumerable<AlertDefinition> alerts)
        {
            var found = new List<(string, IReadOnlyDictionary<string, double>)>();
            if (_catalog == null) return found;
            void Add(string? descId, IReadOnlyDictionary<string, double>? p, string? tf)
            {
                if (p == null || p.Count == 0 || !string.IsNullOrEmpty(tf) || string.IsNullOrEmpty(descId)) return;
                var code = _catalog.GetById(descId)?.IndicatorCode;
                if (!string.IsNullOrEmpty(code)) found.Add((code, p));
            }
            void Walk(ConditionNode n)
            {
                switch (n)
                {
                    case ConditionLeaf l:
                        Add(l.SignalDescriptorId, l.Parameters, l.Timeframe);
                        Add(l.SecondSignalDescriptorId, l.SecondParameters, l.SecondTimeframe ?? l.Timeframe);
                        break;
                    case ConditionGroup g:
                        foreach (var c in g.Children) Walk(c);
                        break;
                }
            }
            foreach (var a in alerts)
                if (a.IsActive && a.ConditionTree != null) Walk(a.ConditionTree);
            return found;
        }

        private void CollectTreeCodes(ConditionNode node, Action<string?> add)
        {
            switch (node)
            {
                case ConditionLeaf leaf:
                    if (_catalog == null) return;
                    add(_catalog.GetById(leaf.SignalDescriptorId)?.IndicatorCode);
                    if (!string.IsNullOrEmpty(leaf.SecondSignalDescriptorId))
                        add(_catalog.GetById(leaf.SecondSignalDescriptorId)?.IndicatorCode);
                    break;
                case ConditionGroup g:
                    foreach (var c in g.Children) CollectTreeCodes(c, add);
                    break;
            }
        }

        /// <summary>One saved config, rebuilt as a workspace load rebuilds it. Null when it cannot be.</summary>
        private ChartSeries? Build(ChartIdentity identity, SeriesConfig original, IReadOnlyList<SeriesConfig> saved, List<IndicatorMetadata> allMeta)
        {
            try
            {
                var config = original.Clone();
                WorkspaceInitializer.MigrateSeriesConfig(config, allMeta);
                var meta = allMeta.FirstOrDefault(m =>
                    m.Code.Equals(config.IndicatorCode, StringComparison.OrdinalIgnoreCase));

                if (meta == null || IsCoreCode(config.IndicatorCode))
                    return SeriesManagementService.MaterializeVerbatim(config);

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

                return SeriesManagementService.MaterializeSaved(
                    config, meta, _factory, siblings, _engine, _prefs, ordinal);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Headless chart could not rebuild saved series {Code} on {Symbol}.",
                    original.IndicatorCode, identity.Symbol);
                return null;
            }
        }

        /// <summary>Every declared parameter at its metadata default — numbers (and switches, as
        /// 1 or 0) into <c>Parameters</c>, everything else into <c>StringParameters</c>.</summary>
        internal static void ApplyDefaultParameters(SeriesConfig config, IndicatorMetadata meta)
        {
            foreach (var p in meta.Parameters)
            {
                if (string.IsNullOrEmpty(p.Name) || p.DefaultValue == null) continue;
                switch (p.DefaultValue)
                {
                    case bool b: config.Parameters[p.Name] = b ? 1 : 0; break;
                    case string str: config.StringParameters[p.Name] = str; break;
                    default:
                        if (double.TryParse(Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                            config.Parameters[p.Name] = d;
                        else
                            config.StringParameters[p.Name] = Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        break;
                }
            }
        }

        private static bool IsCoreCode(string code) =>
            code.ToUpperInvariant() is "CANDLES" or "PRICE" or "VOLUME" or "HEATMAP";
    }
}
