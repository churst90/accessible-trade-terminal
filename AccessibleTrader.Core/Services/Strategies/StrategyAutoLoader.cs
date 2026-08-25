using System;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Walks <see cref="IStrategyLibrary.All"/> at app startup and registers every spec whose
    /// <see cref="StrategySpec.IsAutoActivate"/> flag is true with <see cref="IStrategyEngine"/>
    /// via <see cref="IConfigurableStrategyFactory"/>. This is the persistence layer for live
    /// composite strategies — users no longer have to re-add their setups after every restart.
    ///
    /// The "Add to Engine" button in the builder UI (BuildSetupTab) flips
    /// <c>IsAutoActivate=true</c> and persists; the Active tab's Remove button flips it back to
    /// false. Saved-but-not-activated specs stay in the library as templates the user can load
    /// and edit later without auto-running them.
    ///
    /// Eagerly resolved by injecting into <c>MainLayout.razor</c> so it runs once on app start.
    /// </summary>
    public sealed class StrategyAutoLoader
    {
        private readonly IStrategyLibrary _library;
        private readonly IConfigurableStrategyFactory _factory;
        private readonly IStrategyEngine _engine;
        private readonly IRoslynScriptingService? _roslyn;
        private readonly IAppLogger _logger;
        private readonly IStrategyPositionManager? _positions;
        private readonly IEventBus? _eventBus;
        private bool _hasLoaded;

        public StrategyAutoLoader(
            IStrategyLibrary library,
            IConfigurableStrategyFactory factory,
            IStrategyEngine engine,
            IAppLogger logger,
            IRoslynScriptingService? roslyn = null,
            IStrategyPositionManager? positions = null,
            IEventBus? eventBus = null)
        {
            _library = library;
            _factory = factory;
            _engine  = engine;
            _roslyn  = roslyn;
            _logger  = logger;
            _positions = positions;
            _eventBus = eventBus;
        }

        /// <summary>
        /// A strategy the user armed did not come back. Every path that drops one says so out
        /// loud, not just to the log.
        ///
        /// <para>
        /// Auto-load is best-effort by design — a bad spec must not stop the app from starting —
        /// but "best-effort" had meant a log line, and a log line is invisible to the person whose
        /// strategy is now not running. They armed it, they restarted, and the terminal says
        /// nothing: as far as they can tell it is live. That gap widened the day the causality
        /// gate landed, because a saved script that reads the next bar now legitimately fails to
        /// recompile — a refusal the author has to hear about, not discover from a missing trade.
        /// </para>
        /// </summary>
        private void AnnounceNotLoaded(string strategyName, string reason)
        {
            _eventBus?.Publish(new FeedbackRequestEvent(
                FeedbackType.Error,
                $"Strategy {strategyName} did not load: {reason}",
                Interrupt: false,
                IsUserInitiated: false));
        }

        /// <summary>
        /// Idempotent — safe to call multiple times. The first call walks the library and
        /// activates every spec marked IsAutoActivate; subsequent calls are no-ops.
        /// Async because Roslyn-script specs are recompiled here — the old synchronous
        /// version blocked a thread-pool thread on <c>task.Wait()</c> during startup,
        /// a visible stall on mobile when the library held several script strategies.
        /// </summary>
        public async System.Threading.Tasks.Task LoadAllAsync()
        {
            if (_hasLoaded) return;
            _hasLoaded = true;

            int count = 0;
            foreach (var spec in _library.All)
            {
                if (!spec.IsAutoActivate) continue;
                try
                {
                    // Roslyn-script specs carry C# source in spec.RoslynSource;
                    // recompile and register those through IRoslynScriptingService
                    // instead of the condition-tree factory. If the scripting
                    // service isn't available (e.g. stripped in a constrained
                    // build), skip with a warning rather than failing startup.
                    if (!string.IsNullOrWhiteSpace(spec.RoslynSource))
                    {
                        if (_roslyn == null)
                        {
                            _logger.LogWarning(
                                $"Auto-load skipped Roslyn strategy '{spec.Name}' — IRoslynScriptingService not registered.",
                                nameof(StrategyAutoLoader));
                            AnnounceNotLoaded(spec.Name, "scripting is not available on this build");
                            continue;
                        }
                        var result = await _roslyn.CompileStrategyAsync(spec.RoslynSource!).ConfigureAwait(false);
                        if (!result.Success)
                        {
                            _logger.LogWarning(
                                $"Auto-load failed to recompile Roslyn strategy '{spec.Name}' ({spec.Id}): "
                                + string.Join("; ", result.Errors),
                                nameof(StrategyAutoLoader));
                            // First finding only: the rest go to the log. Speech that reads out
                            // four Roslyn diagnostics is speech the user talks over.
                            AnnounceNotLoaded(spec.Name,
                                result.Errors is { Length: > 0 } ? result.Errors[0] : "it no longer compiles");
                            continue;
                        }
                        _engine.AddStrategy(result.Strategy!, new System.Collections.Generic.Dictionary<string, object>(),
                                            spec.ExecutionMode, specId: spec.Id);
                        count++;
                        continue;
                    }

                    var strategy = _factory.Create(spec);
                    _engine.AddStrategy(strategy, new System.Collections.Generic.Dictionary<string, object>(),
                                        spec.ExecutionMode, specId: spec.Id);
                    count++;
                }
                catch (Exception ex)
                {
                    // Auto-load is best-effort. A bad spec must never block app startup —
                    // log and continue with the rest of the library.
                    _logger.LogWarning(
                        $"Auto-load failed for strategy '{spec.Name}' ({spec.Id}): {ex.Message}",
                        nameof(StrategyAutoLoader));
                    AnnounceNotLoaded(spec.Name, ex.Message);
                }
            }

            if (count > 0)
            {
                _logger.LogInfo(
                    $"StrategyAutoLoader activated {count} strategy(ies) from the library.",
                    nameof(StrategyAutoLoader));
            }

            // ── Restart reconciliation ───────────────────────────────────────────
            // Every strategy above was rebuilt FLAT: Initialize resets the state machine and a
            // fresh BaseStrategy starts with no open side. The broker did not restart with us.
            // AddStrategy has already re-attached each spec's remembered position (via
            // IStrategyPositionManager.Adopt) so nothing can re-enter on top of one; this pass
            // then asks each venue what it actually holds and says what it found. It runs AFTER
            // the loop so one read per provider covers every strategy on it, and its failures
            // are contained — a venue that cannot answer must not stop the app from starting.
            if (_positions != null)
            {
                try
                {
                    await _positions.ReconcileAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        $"Strategy position reconciliation failed: {ex.Message}",
                        nameof(StrategyAutoLoader));
                }
            }
        }
    }
}
