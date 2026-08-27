using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Core.Services
{
    public interface IWorkspaceInitializer
    {
        /// <summary>
        /// Reconciles the active tab's core series stack to match the provider's data shape.
        /// Seeded set varies by <paramref name="dataShape"/>:
        ///   • Ohlcv (default) → Candles + Volume + Price (the historical price-chart stack)
        ///   • SingleValueLine → Price only (a single Line component for analytics metrics)
        ///
        /// On a shape change (e.g. switching from a trading provider to an analytics one),
        /// ALL non-core series — user-added indicators AND drawings — are stripped so the
        /// new tab reflects the new mode cleanly. Same-shape reloads are idempotent and
        /// preserve user content. The caller should capture the desired label via
        /// <see cref="IMarketDataProvider.GetSymbolDisplayName"/> and pass it through so
        /// the Price series gets a human-readable FriendlyName on analytics tabs.
        /// </summary>
        /// <param name="dataShape">The NEW provider's declared data shape.</param>
        /// <param name="symbolDisplayName">Human-readable label for the active symbol
        /// (e.g. "Fear &amp; Greed Index"). Empty string falls back to generic naming.</param>
        /// <param name="renderHints">Optional per-symbol render/sonification hints from
        /// the provider. When non-null, applied to the Price series on SingleValueLine
        /// loads: range clamp, reference levels, display type, speech template, color.
        /// Null means "use defaults" — OHLCV and unbounded analytics metrics pass null.</param>
        void InitializeDefaultSeries(ProviderDataShape dataShape = ProviderDataShape.Ohlcv, string symbolDisplayName = "", SymbolRenderHints? renderHints = null);

        /// <summary>
        /// Restores a full workspace from a saved profile configuration.
        /// Rebuilds all tabs' series through the factory so current metadata
        /// defaults (waveforms, colors, envelope types) are always applied.
        /// </summary>
        void RestoreWorkspace(WorkspaceConfiguration config);
    }

    public class WorkspaceInitializer : IWorkspaceInitializer
    {
        private readonly ISeriesManagementService _seriesService;
        private readonly IWorkspaceStore _store;
        private readonly IIndicatorService _indicatorService;

        // Optional (null in lightweight tests): the strategy-restore collaborators.
        private readonly Sdk.Strategies.IStrategyEngine? _strategyEngine;
        private readonly Strategies.IConfigurableStrategyFactory? _strategyFactory;
        private readonly Strategies.IStrategyLibrary? _strategyLibrary;
        private readonly IAppSettings? _settings;

        /// <summary>
        /// Guards the default Market Structure seed so removing the series does not have it
        /// reappear on the next chart load. Session-scoped on purpose: a fresh launch honours the
        /// setting again, which is what "on by default" should mean.
        /// </summary>
        private bool _structureSeededThisSession;

        public WorkspaceInitializer(
            ISeriesManagementService seriesService,
            IWorkspaceStore store,
            IIndicatorService indicatorService,
            Sdk.Strategies.IStrategyEngine? strategyEngine = null,
            Strategies.IConfigurableStrategyFactory? strategyFactory = null,
            Strategies.IStrategyLibrary? strategyLibrary = null,
            IAppSettings? settings = null)
        {
            _seriesService = seriesService;
            _store = store;
            _indicatorService = indicatorService;
            _strategyEngine = strategyEngine;
            _strategyFactory = strategyFactory;
            _strategyLibrary = strategyLibrary;
            _settings = settings;
        }

        /// <summary>
        /// The three "core" series ids that make up the provider-owned stack. Everything
        /// else (EMAs, RSIs, drawings, cross-series indicators) is considered user-owned
        /// and survives provider switches. This list is the authoritative definition of
        /// "what the provider dictates" — <see cref="ResolveDesiredCoreStack"/> picks a
        /// subset based on the provider's declared shape and the reconciler removes any
        /// core series that aren't in the desired subset.
        /// </summary>
        private static readonly string[] CoreStackIds =
        {
            CoreSeriesIds.Candles,
            CoreSeriesIds.Volume,
            CoreSeriesIds.Price,
        };

        /// <summary>
        /// Returns the core series ids that should exist on the chart for a given provider
        /// data shape, plus the id that should become the primary/focused series. This is
        /// the single source of truth for "what does this provider produce?" — called by
        /// both the fresh-load path (InitializeDefaultSeries) and the workspace-restore
        /// path so both routes make identical decisions.
        /// </summary>
        /// <param name="dataShape">The provider's declared data shape.</param>
        /// <returns>
        /// A tuple containing the desired core series ids in seed order and the id that
        /// should be set as <see cref="WorkspaceState.PrimarySeriesId"/>.
        /// </returns>
        public static (string[] Desired, string PrimaryId) ResolveDesiredCoreStack(ProviderDataShape dataShape)
        {
            return dataShape switch
            {
                // Single-value analytics providers (FRED, CoinGecko, CoinMetrics,
                // AlternativeMe FNG, Glassnode, OKX funding, Binance Derivatives, etc.)
                // have no candles and no volume — only a one-point-per-bar scalar series.
                // Seeding Candles/Volume for these produced the "three series showing the
                // same value" bug users reported after Phase 4: the guards let stale OHLCV
                // series survive a provider switch and the sync path happily re-populated
                // them against the new scalar buffer.
                ProviderDataShape.SingleValueLine => (
                    new[] { CoreSeriesIds.Price },
                    CoreSeriesIds.Price),

                // Default: a real OHLCV market data provider (exchange spot, perps, etc.)
                // gets the full Candles + Volume + Price stack with Candles as primary.
                _ => (
                    new[] { CoreSeriesIds.Candles, CoreSeriesIds.Volume, CoreSeriesIds.Price },
                    CoreSeriesIds.Candles),
            };
        }

        public void InitializeDefaultSeries(ProviderDataShape dataShape = ProviderDataShape.Ohlcv, string symbolDisplayName = "", SymbolRenderHints? renderHints = null)
        {
            // Provider-shape-driven reconciliation. Called on every chart load. Must be
            // idempotent (same shape twice = no-op) and must morph the core stack on a
            // shape change without clobbering user content that still makes sense.
            //
            // Rules by scenario:
            //
            //   (1) Same shape, same or different symbol (e.g. Bitstamp BTC → Bitstamp ETH,
            //       or Bitstamp → Binance both OHLCV):
            //       → Core stack already matches → no series mutations.
            //       → User-added EMAs/RSI/drawings stay put. Only the data buffer swaps.
            //
            //   (2) Shape change (OHLCV → SingleValueLine, or vice versa):
            //       → Strip ALL non-core series (user indicators + drawings). They belong
            //         to the old mode; they don't make sense on the new one. The caller
            //         (Toolbar.LoadChart) should have already warned the user and offered
            //         the "Open in New Tab" alternative.
            //       → Swap the core stack to match the new shape's desired set.
            //       → Update PrimarySeriesId + focus.
            //
            //   (3) First load on a blank tab:
            //       → ActiveSeries is empty → steps (2)'s logic runs trivially and seeds
            //         the full core stack for the new shape.
            //
            // The shape-change detection reads state.CurrentDataShape (set by the previous
            // SetProviderContextAction dispatch — defaults to Ohlcv for fresh tabs, which
            // means the very first analytics load on a fresh tab correctly triggers no
            // "strip" pass but still seeds only Price).

            var oldShape = _store.State.CurrentDataShape;
            bool shapeChanged = oldShape != dataShape && _store.State.ActiveSeries.Count > 0;

            var (desired, primaryId) = ResolveDesiredCoreStack(dataShape);
            var desiredSet = new HashSet<string>(desired, StringComparer.Ordinal);

            // 1. If the shape changed, strip EVERYTHING — non-core series (user indicators
            //    and drawings) AND core series (Candles/Volume/Price). Stripping core too is
            //    deliberate: we observed a bug where preserving the Price series across an
            //    analytics ↔ trading transition left stale data buffers (1 bar from FNG)
            //    that the UpdateData sync couldn't reconcile cleanly with a fresh 200-bar
            //    OHLCV buffer, causing Volume and Price to render only the first/last bar.
            //    Re-creating Price fresh sidesteps the entire class of buffer-state-leak
            //    bugs that come from reusing series instances across different shapes.
            //    Same-shape reloads (Bitstamp BTC → Bitstamp ETH) skip this whole branch
            //    so user indicators are preserved as before.
            if (shapeChanged)
            {
                var allIdsToRemove = _store.State.ActiveSeries
                    .Select(s => s.Id)
                    .ToList();

                foreach (var id in allIdsToRemove)
                    _store.Dispatch(new RemoveSeriesAction(id));
            }
            else
            {
                // Same-shape path: still strip core series that don't belong to the new
                // shape (defensive — in practice this is a no-op when shape is unchanged,
                // but the check is cheap and protects against edge cases like a tab that
                // somehow ended up with a mismatched core stack from save/load migration).
                var toRemove = _store.State.ActiveSeries
                    .Where(s => CoreStackIds.Contains(s.Id) && !desiredSet.Contains(s.Id))
                    .Select(s => s.Id)
                    .ToList();

                foreach (var id in toRemove)
                    _store.Dispatch(new RemoveSeriesAction(id));
            }

            // 3. Add any desired core series that are missing. Metadata lookup cached
            //    per call to avoid scanning the indicator list more than once.
            var allMeta = _indicatorService.GetAvailableIndicators();
            IndicatorMetadata? MetaFor(string code) =>
                allMeta.FirstOrDefault(m => m.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

            foreach (var id in desired)
            {
                if (_store.State.ActiveSeries.Any(s => s.Id == id)) continue;

                IndicatorMetadata? meta = id switch
                {
                    CoreSeriesIds.Candles => MetaFor("CANDLES"),
                    CoreSeriesIds.Volume  => MetaFor("VOLUME"),
                    CoreSeriesIds.Price   => MetaFor("PRICE"),
                    _                      => null,
                };
                if (meta != null) _seriesService.RegisterSeriesFromMetadata(meta);
            }

            // 3b. Market Structure overlay — seeded by default on OHLCV charts because
            //     structure is the orientation layer a sighted trader reads off the chart's
            //     shape for free. Opt-out via Settings; once the user removes the series it
            //     stays removed for that chart, and this only ever ADDS when absent, so a
            //     deliberate deletion is not undone on the next reconcile within the session.
            if (dataShape == ProviderDataShape.Ohlcv && (_settings?.MarketStructureOnByDefault ?? true))
            {
                bool alreadyPresent = _store.State.ActiveSeries.Any(sr =>
                    string.Equals(sr.IndicatorCode, Indicators.SwingStructureProvider.Code,
                        StringComparison.OrdinalIgnoreCase));
                if (!alreadyPresent && !_structureSeededThisSession)
                {
                    var structureMeta = MetaFor(Indicators.SwingStructureProvider.Code);
                    if (structureMeta != null)
                    {
                        _seriesService.RegisterSeriesFromMetadata(structureMeta);
                        _structureSeededThisSession = true;
                    }
                }
            }

            // 4. On SingleValueLine providers, relabel the Price series with the symbol
            //    display name (e.g. "Fear & Greed Index"). For OHLCV we leave the Candles
            //    series with its metadata-derived name ("Candles") — the bar value speech
            //    already includes the symbol elsewhere via the title bar, and relabeling
            //    Candles → "BTC/USDT" would redundantly echo it. We DO reset the Price
            //    series's name on OHLCV transitions in case it was previously labeled with
            //    an analytics symbol (otherwise speech would say "Fear & Greed Index" while
            //    actually navigating Bitstamp BTC/USDT data).
            if (dataShape == ProviderDataShape.SingleValueLine && !string.IsNullOrWhiteSpace(symbolDisplayName))
            {
                ApplyPriceSeriesLabel(symbolDisplayName);
                // Q3: apply per-symbol render hints to the Price series if the provider
                // supplied them. The hint-application mutates Config fields and dispatches
                // UpdateSeriesAction to propagate the changes through the store.
                if (renderHints != null)
                    ApplyRenderHints(renderHints);
            }
            else if (dataShape == ProviderDataShape.Ohlcv)
            {
                // Reset Price series to its metadata default ("Price") if it carried an
                // analytics label across a previous shape transition. With the Phase 6
                // strip-all-on-shape-change rule above, this branch is technically dead
                // because Price gets recreated fresh on every shape change — but the
                // defensive reset costs nothing and keeps the contract explicit.
                ApplyPriceSeriesLabel("Price");
            }

            // 5. Atomically update shape + display name on state. This also triggers the
            //    UI re-render that hides/shows trading-only controls based on shape.
            _store.Dispatch(new SetProviderContextAction(dataShape, symbolDisplayName));

            // 6. Set PrimarySeriesId + focus. Reducer picks the body component on Candles
            //    via GetDefaultComponentIndex so wick-body-wick nav starts centered.
            _store.Dispatch(new SetPrimarySeriesIdAction(primaryId));
            _store.Dispatch(new SelectSeriesAction(primaryId));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Series));
        }

        /// <summary>
        /// Rewrites the Price series' machine name (<see cref="SeriesConfig.Name"/>),
        /// friendly name, and its single line component's DisplayName to
        /// <paramref name="label"/>. The series id stays <see cref="CoreSeriesIds.Price"/>
        /// so lookups keep working. Used for analytics tabs so speech reads "Fear and Greed
        /// Index, 47" instead of "Price, 47".
        ///
        /// IMPORTANT: speech uses <c>ChartSeries.Name</c> which is <c>Config.Name</c> (NOT
        /// FriendlyName — see NavigationFeedbackManager.cs:176). Setting only FriendlyName
        /// silently fails for the speech path. Both fields are set here so render-side
        /// (FriendlyName) and speech-side (Name) stay in sync.
        ///
        /// Uses UpdateSeriesAction with an in-place mutation because the existing series
        /// architecture treats FriendlyName/Name/DisplayName as mutable metadata rather
        /// than part of the reducer-managed immutable state.
        /// </summary>
        private void ApplyPriceSeriesLabel(string label)
        {
            var price = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Price);
            if (price == null) return;

            price.Config.Name = label;          // ← what NavigationFeedbackManager reads for speech
            price.Config.FriendlyName = label;  // ← what render/UI labels show
            var line = price.Components.FirstOrDefault();
            if (line != null) line.DisplayName = label;

            // Trigger a re-render/speech refresh by dispatching an UpdateSeriesAction
            // with the (now-mutated) list — the reducer swaps ActiveSeries wholesale so
            // components observing state see the label change.
            _store.Dispatch(new UpdateSeriesAction(_store.State.ActiveSeries));
        }

        /// <summary>
        /// Applies a <see cref="SymbolRenderHints"/> bundle to the Price series. Called on
        /// SingleValueLine loads when the provider supplied hints. Each non-null field is
        /// copied to the corresponding config location:
        ///
        ///   • RangeMin/RangeMax    → <see cref="SeriesConfig.RangeMin"/> / RangeMax
        ///                            (ViewportRangeCalculator clamps main pane to these)
        ///   • ReferenceLevels      → converted to LevelConfig and appended to Config.Levels
        ///                            (ViewportRangeCalculator expands main range to include
        ///                            them; AudioZoneHelper maps "Overbought"/"Oversold"
        ///                            names to OB/OS sonification behavior)
        ///   • DisplayType          → component.DisplayType (Oscillator routes through the
        ///                            oscillator audio profile)
        ///   • SpeechTemplate       → component.SpeechTemplate (used by SpeechFormatter for
        ///                            the value readout — supports {name}/{value}/{zone})
        ///   • ColorHex             → component.ColorHex (line color override)
        ///
        /// Existing reference levels (there shouldn't be any on a fresh Price series, but
        /// the reconciler's strip-all-on-shape-change rule guarantees it) are cleared first
        /// so repeated loads don't accumulate duplicates.
        /// </summary>
        private void ApplyRenderHints(SymbolRenderHints hints)
        {
            var price = _store.State.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Price);
            if (price == null) return;

            // Range clamp — consumed by ViewportRangeCalculator.
            if (hints.RangeMin.HasValue) price.Config.RangeMin = hints.RangeMin;
            if (hints.RangeMax.HasValue) price.Config.RangeMax = hints.RangeMax;

            // Reference levels — clear any stale and add fresh. Convert from the immutable
            // LevelDescriptor record to mutable LevelConfig so users can toggle them in
            // the Properties modal without the hint being re-applied.
            if (hints.ReferenceLevels != null && hints.ReferenceLevels.Count > 0)
            {
                price.Config.Levels.Clear();
                foreach (var lvl in hints.ReferenceLevels)
                {
                    price.Config.Levels.Add(new LevelConfig
                    {
                        Name            = lvl.Name,
                        Value           = lvl.Value,
                        ColorHex        = lvl.ColorHex,
                        DashStyle       = lvl.Dash,
                        PlayEarcon      = lvl.PlayEarcon,
                        EarconVolume    = lvl.EarconVolume,
                        ZoneNoiseAmount = lvl.ZoneNoiseAmount,
                        ZoneNoiseType   = lvl.ZoneNoiseType,
                    });
                }
            }

            // Component-level overrides — display type, speech template, color.
            var line = price.Components.FirstOrDefault();
            if (line != null)
            {
                if (hints.DisplayType.HasValue)         line.DisplayType     = hints.DisplayType.Value;
                if (!string.IsNullOrEmpty(hints.SpeechTemplate)) line.SpeechTemplate = hints.SpeechTemplate!;
                if (!string.IsNullOrEmpty(hints.ColorHex))       line.ColorHex       = hints.ColorHex!;
            }

            // Propagate the mutations through the store so observers re-render and speech
            // picks up the new templates.
            _store.Dispatch(new UpdateSeriesAction(_store.State.ActiveSeries));
        }

        // NOTE on restore-path reconciliation:
        //   RestoreWorkspace rebuilds the saved series verbatim (after MigrateSeriesConfig
        //   rewrites legacy component names). It does NOT look up the provider's current
        //   data shape to strip incompatible core series — doing so would require making
        //   restore async, which cascades through LoadWorkspaceModal and the app startup
        //   path for little benefit. Instead, reconciliation happens lazily on the next
        //   LoadChartAsync call: InitializeDefaultSeries (above) is idempotent, shape-
        //   aware, and removes any stale core series in a single pass. Worst case the
        //   user sees a brief moment of stale Candles after loading a workspace until
        //   they click Load Chart — at which point the reconciler fixes it.
        //   If a future change adds an async restore path that CAN look up the provider,
        //   share the ResolveDesiredCoreStack helper to keep the two paths in sync.
        public void RestoreWorkspace(WorkspaceConfiguration config)
        {
            if (config == null) return;

            var allMeta = _indicatorService.GetAvailableIndicators();

            if (config.IsMultiTab)
            {
                RestoreMultiTabWorkspace(config, allMeta);
            }
            else
            {
                // Legacy single-tab format
                RestoreLegacySingleTab(config, allMeta);
            }

            RestoreActiveStrategies(config);
        }

        /// <summary>
        /// Re-activates the strategies that were live when the workspace was saved.
        /// REPLACE semantics: loading a workspace means "give me that state back",
        /// so currently-active strategies are removed first. Each saved record is
        /// looked up in the library (a deleted spec is skipped with a log — the
        /// workspace must still load) and re-bound to its SAVED symbol, not the
        /// currently-focused chart. Roslyn-script specs need async compilation and
        /// are handled by StrategyAutoLoader's IsAutoActivate path instead.
        /// </summary>
        private void RestoreActiveStrategies(WorkspaceConfiguration config)
        {
            if (_strategyEngine == null || _strategyFactory == null || _strategyLibrary == null) return;
            if (config.ActiveStrategies == null) return;

            foreach (var existing in _strategyEngine.ActiveStrategies.ToList())
                _strategyEngine.RemoveStrategy(existing.InstanceId);

            foreach (var saved in config.ActiveStrategies)
            {
                try
                {
                    var spec = _strategyLibrary.All.FirstOrDefault(s => s.Id == saved.SpecId);
                    if (spec == null || !string.IsNullOrWhiteSpace(spec.RoslynSource)) continue;

                    var strategy = _strategyFactory.Create(spec);
                    var mode = Enum.TryParse<Sdk.Strategies.StrategyExecutionMode>(saved.ExecutionMode, out var m)
                        ? m : spec.ExecutionMode;
                    string id = _strategyEngine.AddStrategy(strategy, new Dictionary<string, object>(),
                        mode, specId: spec.Id, bindSymbol: saved.Symbol);
                    if (saved.IsPaused) _strategyEngine.PauseStrategy(id, true);
                }
                catch (Exception)
                {
                    // A bad spec must never block workspace load; the rest restore.
                }
            }
        }

        private void RestoreMultiTabWorkspace(WorkspaceConfiguration config, List<IndicatorMetadata> allMeta)
        {
            // Restore the first tab's series into the active workspace state.
            // Additional tabs are added via AddTabAction + SwitchTabAction.
            if (config.Tabs.Count == 0) return;

            // Tabs are restored IN CONFIG ORDER, so store index N holds config tab N.
            //
            // This used to restore the SAVED ACTIVE tab into the store's existing slot
            // (store 0) and then append the others in config order at store 1..n, before
            // dispatching SwitchTabAction with the CONFIG index. With config.Tabs = [A, B, C]
            // and ActiveTabIndex = 1: B landed at store 0, A at store 1, C at store 2, and the
            // switch activated store 1 — which is A. The user resumed on the wrong chart with
            // the tab bar in the wrong order, every single time they saved while any tab but
            // the first was focused.
            //
            // Restoring in config order makes the two index spaces the same one, so the
            // SwitchTabAction below means what it says.
            int activeIdx = Math.Clamp(config.ActiveTabIndex, 0, config.Tabs.Count - 1);

            void RestoreTabInto(TabConfiguration tab)
            {
                if (!string.IsNullOrEmpty(tab.Symbol))
                {
                    _store.Dispatch(new SetIdentityAction(new ChartIdentity
                    {
                        Market = tab.Market,
                        Provider = tab.Provider,
                        Symbol = tab.Symbol,
                        Timeframe = tab.Timeframe
                    }));
                }

                // Restore display toggles
                if (tab.IsHeikinAshi != _store.State.IsHeikinAshi)
                    _store.Dispatch(new ToggleHeikinAshiAction());
                if (tab.IsLogScale != _store.State.IsLogScale)
                    _store.Dispatch(new ToggleLogScaleAction());

                // Restore series
                RestoreSeriesFromTab(tab, allMeta);

                // Restore pane height ratios
                RestorePaneHeightRatios(tab.PaneHeightRatios);

                // Restore this tab's saved zoom width before its snapshot is taken
                // (before data loads — see RestoreViewportLength).
                RestoreViewportLength(tab.ViewportLength);
            }

            // Config tab 0 goes into the slot the store already has.
            RestoreTabInto(config.Tabs[0]);

            // The rest each open a new tab, which snapshots the current one and creates a
            // blank active — so they land at store 1, 2, ... in config order.
            for (int i = 1; i < config.Tabs.Count; i++)
            {
                _store.Dispatch(new AddTabAction());
                RestoreTabInto(config.Tabs[i]);
            }

            // Switch back to the originally active tab. Store index == config index now.
            if (config.Tabs.Count > 1)
            {
                _store.Dispatch(new SwitchTabAction(activeIdx));
            }

            // Set the primary series and focus based on what actually got restored.
            // Workspaces saved before Phase 4 may not contain a Candles series (single-
            // value analytics tabs), so we can't assume Candles exists — fall back to
            // Price, then to whatever the first active series is.
            SetPrimaryAndFocusFromRestore();
        }

        private void RestoreLegacySingleTab(WorkspaceConfiguration config, List<IndicatorMetadata> allMeta)
        {
            if (config.Series?.Count > 0)
            {
                foreach (var seriesConfig in config.Series)
                {
                    MigrateSeriesConfig(seriesConfig, allMeta);
                    var meta = allMeta.FirstOrDefault(m =>
                        m.Code.Equals(seriesConfig.IndicatorCode, StringComparison.OrdinalIgnoreCase));
                    _seriesService.RestoreSeriesFromSaved(seriesConfig, meta);
                }

                RestorePaneHeightRatios(config.PaneHeightRatios);
                RestoreViewportLength(config.ViewportLength);
            }

            SetPrimaryAndFocusFromRestore();
        }

        /// <summary>
        /// Chooses the primary series id for a newly-restored workspace and sets focus on it.
        /// Preference order: CANDLES (OHLCV workspaces) → PRICE (single-value analytics) →
        /// the first active series as a last resort. This replaces the old assumption that
        /// CANDLES always exists and is always the focus target.
        /// </summary>
        private void SetPrimaryAndFocusFromRestore()
        {
            var active = _store.State.ActiveSeries;
            string? primaryId = null;
            if      (active.Any(s => s.Id == CoreSeriesIds.Candles)) primaryId = CoreSeriesIds.Candles;
            else if (active.Any(s => s.Id == CoreSeriesIds.Price))   primaryId = CoreSeriesIds.Price;
            else if (active.Count > 0)                                primaryId = active[0].Id;

            if (primaryId == null) return;

            _store.Dispatch(new SetPrimarySeriesIdAction(primaryId));
            _store.Dispatch(new SelectSeriesAction(primaryId));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Series));
        }

        private void RestoreSeriesFromTab(TabConfiguration tab, List<IndicatorMetadata> allMeta)
        {
            if (tab.Series?.Count > 0)
            {
                foreach (var seriesConfig in tab.Series)
                {
                    MigrateSeriesConfig(seriesConfig, allMeta);
                    var meta = allMeta.FirstOrDefault(m =>
                        m.Code.Equals(seriesConfig.IndicatorCode, StringComparison.OrdinalIgnoreCase));
                    _seriesService.RestoreSeriesFromSaved(seriesConfig, meta);
                }
            }
        }

        private void RestorePaneHeightRatios(Dictionary<string, float>? savedRatios)
        {
            if (savedRatios == null || savedRatios.Count == 0) return;

            var ratios = new Dictionary<string, float>(savedRatios);

            // Clamp any saved ratio below a minimum viable height.
            const float MinRatioFloor = 0.15f;
            foreach (var key in ratios.Keys.ToList())
                ratios[key] = Math.Max(ratios[key], MinRatioFloor);

            _store.Dispatch(new SetPaneHeightRatiosAction(ratios.ToImmutableDictionary()));
        }

        /// <summary>
        /// Restores a tab's saved ZOOM WIDTH (viewport bar count). Dispatched while the tab's
        /// data is still empty, so the subsequent initial load computes its live-edge window
        /// from this length instead of the default — previously the saved viewport was written
        /// to disk but never re-applied, so every load/resume snapped to the default zoom.
        ///
        /// The saved absolute scroll INDEX is deliberately not restored: after a reload the
        /// history depth and live edge differ, so a stored index would point at the wrong bar.
        /// Opening at the live edge with the user's zoom width is the correct, stable behavior.
        /// </summary>
        private void RestoreViewportLength(int savedLength)
        {
            if (savedLength > 0)
                _store.Dispatch(new ZoomAction(savedLength)); // reducer clamps to a valid range
        }

        /// <summary>
        /// Migrates a saved <see cref="SeriesConfig"/> to the current metadata:
        /// <list type="bullet">
        ///   <item>Removes any Cloud-type components (now rendered via CloudFills).</item>
        ///   <item>Adds any CloudFill definitions that are in the current metadata but missing from the saved config.</item>
        /// </list>
        /// </summary>
        private static void MigrateSeriesConfig(SeriesConfig config, List<IndicatorMetadata> allMeta)
        {
            // Phase 5 (2026-04-09): rename legacy Candles/Price component names to the
            // new snake_case machine names introduced in Phase 2. Old workspaces saved
            // "Candle Body" / "Upper Wick" / "Lower Wick" / "Close" as component Names;
            // new metadata uses body / upper_wick / lower_wick / line. Rewriting here
            // keeps the saved state compatible without a schema version bump. DataMapping
            // is regenerated by the factory on restore so we only need to touch Name.
            string codeUp = config.IndicatorCode?.ToUpperInvariant() ?? "";
            if (codeUp == "CANDLES" || codeUp == "PRICE")
            {
                foreach (var comp in config.Components)
                {
                    comp.Name = comp.Name switch
                    {
                        "Candle Body" => "body",
                        "Upper Wick"  => "upper_wick",
                        "Lower Wick"  => "lower_wick",
                        "Close"       => "line",
                        _             => comp.Name
                    };
                }
            }

            // Remove stale Cloud-type components (they are now handled by CloudFills).
            var cloudComps = config.Components
                .Where(c => c.DisplayType == ComponentDisplayType.Cloud)
                .ToList();
            foreach (var c in cloudComps) config.Components.Remove(c);

            // Merge missing cloud fills from current metadata defaults.
            var meta = allMeta.FirstOrDefault(m =>
                m.Code.Equals(config.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (meta == null) return;

            foreach (var fill in meta.DefaultCloudFills)
            {
                bool exists = config.CloudFills.Any(f =>
                    f.UpperComponentName.Equals(fill.UpperComponentName, StringComparison.OrdinalIgnoreCase) &&
                    f.LowerComponentName.Equals(fill.LowerComponentName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    config.CloudFills.Add(fill.Clone());
            }

            // Merge missing zone bands from current metadata defaults.
            foreach (var band in meta.DefaultZoneBands)
            {
                bool exists = config.ZoneBands.Any(b =>
                    b.ComponentName.Equals(band.ComponentName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    config.ZoneBands.Add(band.Clone());
            }
        }
    }
}
