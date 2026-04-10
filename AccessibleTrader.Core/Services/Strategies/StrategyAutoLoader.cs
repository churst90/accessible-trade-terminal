using System;
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
        private readonly IAppLogger _logger;
        private bool _hasLoaded;

        public StrategyAutoLoader(
            IStrategyLibrary library,
            IConfigurableStrategyFactory factory,
            IStrategyEngine engine,
            IAppLogger logger)
        {
            _library = library;
            _factory = factory;
            _engine  = engine;
            _logger  = logger;
        }

        /// <summary>
        /// Idempotent — safe to call multiple times. The first call walks the library and
        /// activates every spec marked IsAutoActivate; subsequent calls are no-ops.
        /// </summary>
        public void LoadAll()
        {
            if (_hasLoaded) return;
            _hasLoaded = true;

            int count = 0;
            foreach (var spec in _library.All)
            {
                if (!spec.IsAutoActivate) continue;
                try
                {
                    var strategy = _factory.Create(spec);
                    _engine.AddStrategy(strategy, new System.Collections.Generic.Dictionary<string, object>(),
                                        spec.ExecutionMode);
                    count++;
                }
                catch (Exception ex)
                {
                    // Auto-load is best-effort. A bad spec must never block app startup —
                    // log and continue with the rest of the library.
                    _logger.LogWarning(
                        $"Auto-load failed for strategy '{spec.Name}' ({spec.Id}): {ex.Message}",
                        nameof(StrategyAutoLoader));
                }
            }

            if (count > 0)
            {
                _logger.LogInfo(
                    $"StrategyAutoLoader activated {count} strategy(ies) from the library.",
                    nameof(StrategyAutoLoader));
            }
        }
    }
}
