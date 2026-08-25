using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Coordinates every strategy-management workflow that <c>StrategyModal.razor</c> used to
    /// orchestrate inline across 10 separate service injections. Wraps
    /// <see cref="IStrategyEngine"/>, <see cref="IStrategyBacktester"/>,
    /// <see cref="IBacktestWarmupAnalyzer"/>, <see cref="IStrategyLibrary"/>,
    /// <see cref="IConfigurableStrategyFactory"/>, and <see cref="IRoslynScriptingService"/>
    /// into a single coordinator whose methods capture the cross-service transactions:
    /// start/stop/delete a spec, remove a running instance, run a backtest, compile + add
    /// a custom-script strategy.
    ///
    /// The facade is not about reducing injection count alone — the real benefit is
    /// centralising the "here's how these services chain together" logic that previously
    /// lived scattered across the modal's event handlers. Each method owns its own error
    /// translation and returns a structured result so the UI layer just displays the message.
    /// </summary>
    public interface IStrategyModalCoordinator
    {
        /// <summary>Activate a saved spec — instantiate, register with engine, and flip
        /// <c>IsAutoActivate=true</c> so it survives restart. Deduplicates any existing
        /// engine instance with the same spec id first.</summary>
        StrategyCoordinatorResult StartSpec(string specId);

        /// <summary>Stop every running instance of a spec and clear its auto-activate flag.
        /// The spec stays in the library; only the running instance is removed.</summary>
        StrategyCoordinatorResult StopSpec(string specId);

        /// <summary>Remove an active strategy instance by id. If the instance corresponds
        /// to a library spec, also flips <c>IsAutoActivate=false</c> so it doesn't come
        /// back on next launch.</summary>
        StrategyCoordinatorResult RemoveActive(string instanceId);

        /// <summary>Pause / resume a running strategy instance. Returns ok regardless —
        /// engine lookup failures are logged but not surfaced.</summary>
        StrategyCoordinatorResult TogglePause(string instanceId, bool currentlyPaused);

        /// <summary>Compute the recommended warmup for the given spec by walking its
        /// condition tree. Returns <c>(recommended, ok-result)</c> on success or
        /// <c>(-1, error-result)</c> when the spec is missing.</summary>
        (int RecommendedBars, StrategyCoordinatorResult Result) RecommendedWarmup(string specId);

        /// <summary>Run a backtest for the given spec id. Returns the backtest result
        /// alongside the coordinator result so the UI can format its own display. Null
        /// result when the spec is missing or data is insufficient.</summary>
        Task<(BacktestResult? Result, StrategyCoordinatorResult Status)> RunBacktestAsync(
            string specId,
            IReadOnlyList<Ohlcv> data,
            BacktestConfig config,
            WorkspaceState state);

        /// <summary>Compile user C# source into an <c>ITradingStrategy</c> via Roslyn, then
        /// register it with the engine. Returns compile errors via the result on failure.</summary>
        Task<StrategyCoordinatorResult> CompileAndAddStrategyAsync(string code, StrategyExecutionMode execMode);

        /// <summary>All active strategy instances on the engine. Passthrough for UI binding.</summary>
        IEnumerable<ActiveStrategy> ActiveStrategies { get; }
    }

    /// <summary>Outcome of a coordinator operation — user-facing message plus an ok/error
    /// flag. Mirrors <see cref="StrategyLibraryResult"/> so the UI layer treats both the
    /// library facade and the modal coordinator uniformly.</summary>
    public readonly record struct StrategyCoordinatorResult(bool IsSuccess, string Message)
    {
        public static StrategyCoordinatorResult Ok(string message) => new(true, message);
        public static StrategyCoordinatorResult Error(string message) => new(false, message);
    }

    public sealed class StrategyModalCoordinator : IStrategyModalCoordinator
    {
        private readonly IStrategyEngine _engine;
        private readonly IStrategyBacktester _backtester;
        private readonly IBacktestWarmupAnalyzer _warmup;
        private readonly IStrategyLibrary _library;
        private readonly IConfigurableStrategyFactory _factory;
        private readonly IRoslynScriptingService _roslyn;

        public StrategyModalCoordinator(
            IStrategyEngine engine,
            IStrategyBacktester backtester,
            IBacktestWarmupAnalyzer warmup,
            IStrategyLibrary library,
            IConfigurableStrategyFactory factory,
            IRoslynScriptingService roslyn)
        {
            _engine     = engine;
            _backtester = backtester;
            _warmup     = warmup;
            _library    = library;
            _factory    = factory;
            _roslyn     = roslyn;
        }

        public IEnumerable<ActiveStrategy> ActiveStrategies => _engine.ActiveStrategies;

        public StrategyCoordinatorResult StartSpec(string specId)
        {
            try
            {
                var spec = _library.GetById(specId);
                if (spec == null)
                    return StrategyCoordinatorResult.Error("Spec not found in library.");

                RemoveExistingInstancesOfSpec(specId);

                var strategy = _factory.Create(spec);
                _engine.AddStrategy(strategy, new Dictionary<string, object>(), spec.ExecutionMode, specId: spec.Id);
                _library.Upsert(spec with { IsAutoActivate = true });

                return StrategyCoordinatorResult.Ok(
                    $"'{spec.Name}' started ({spec.ExecutionMode}). Will auto-activate on restart.");
            }
            catch (Exception ex)
            {
                return StrategyCoordinatorResult.Error($"Start failed: {ex.Message}");
            }
        }

        public StrategyCoordinatorResult StopSpec(string specId)
        {
            try
            {
                RemoveExistingInstancesOfSpec(specId);
                var spec = _library.GetById(specId);
                if (spec != null)
                    _library.Upsert(spec with { IsAutoActivate = false });
                return StrategyCoordinatorResult.Ok("Stopped.");
            }
            catch (Exception ex)
            {
                return StrategyCoordinatorResult.Error($"Stop failed: {ex.Message}");
            }
        }

        public StrategyCoordinatorResult RemoveActive(string instanceId)
        {
            try
            {
                var active = _engine.ActiveStrategies.FirstOrDefault(a => a.InstanceId == instanceId);
                if (active != null)
                {
                    var spec = _library.GetById(active.Strategy.Id);
                    if (spec != null)
                        _library.Upsert(spec with { IsAutoActivate = false });
                }
                _engine.RemoveStrategy(instanceId);
                return StrategyCoordinatorResult.Ok("Removed.");
            }
            catch (Exception ex)
            {
                return StrategyCoordinatorResult.Error($"Remove failed: {ex.Message}");
            }
        }

        public StrategyCoordinatorResult TogglePause(string instanceId, bool currentlyPaused)
        {
            try
            {
                _engine.PauseStrategy(instanceId, !currentlyPaused);
                return StrategyCoordinatorResult.Ok(currentlyPaused ? "Resumed." : "Paused.");
            }
            catch (Exception ex)
            {
                return StrategyCoordinatorResult.Error($"Toggle pause failed: {ex.Message}");
            }
        }

        public (int RecommendedBars, StrategyCoordinatorResult Result) RecommendedWarmup(string specId)
        {
            if (string.IsNullOrEmpty(specId))
                return (-1, StrategyCoordinatorResult.Error("Select a strategy from the dropdown first."));
            var spec = _library.GetById(specId);
            if (spec == null)
                return (-1, StrategyCoordinatorResult.Error("Selected spec not found in library."));

            int recommended = _warmup.RecommendedWarmup(spec);
            return (recommended,
                StrategyCoordinatorResult.Ok($"Warmup auto-set to {recommended} bars based on '{spec.Name}'."));
        }

        public async Task<(BacktestResult? Result, StrategyCoordinatorResult Status)> RunBacktestAsync(
            string specId,
            IReadOnlyList<Ohlcv> data,
            BacktestConfig config,
            WorkspaceState state)
        {
            if (string.IsNullOrEmpty(specId))
                return (null, StrategyCoordinatorResult.Error("Please select a saved strategy spec first."));
            if (data == null || data.Count < 10)
                return (null, StrategyCoordinatorResult.Error("Not enough price data loaded. Load a chart first."));

            var spec = _library.GetById(specId);
            if (spec == null)
                return (null, StrategyCoordinatorResult.Error("Selected spec not found in library."));

            try
            {
                var instance = _factory.Create(spec);
                var result = await _backtester.RunAsync(instance, data, config, state);
                return (result, StrategyCoordinatorResult.Ok($"Backtest complete. {result.SpeechSummary}"));
            }
            catch (Exception ex)
            {
                return (null, StrategyCoordinatorResult.Error($"Backtest failed: {ex.Message}"));
            }
        }

        public async Task<StrategyCoordinatorResult> CompileAndAddStrategyAsync(string code, StrategyExecutionMode execMode)
        {
            if (string.IsNullOrWhiteSpace(code))
                return StrategyCoordinatorResult.Error("Please enter strategy code.");

            try
            {
                var result = await _roslyn.CompileStrategyAsync(code);
                if (!result.Success)
                    return StrategyCoordinatorResult.Error("Compilation errors:\n" + string.Join("\n", result.Errors));

                var strategy = result.Strategy!;
                _engine.AddStrategy(strategy, new Dictionary<string, object>(), execMode);

                // Persist the source as a StrategySpec in the library so
                // StrategyAutoLoader can recompile and register it on the next app
                // start. Conditions + Risk are placeholders for the JSON shape —
                // the auto-loader dispatches on RoslynSource non-null and never
                // evaluates them. IsAutoActivate mirrors the non-Roslyn "Add to
                // Engine" behaviour: adding to the engine also arms autoload.
                try
                {
                    var now = DateTime.UtcNow;
                    var spec = new StrategySpec(
                        Id: string.IsNullOrWhiteSpace(strategy.Id) ? Guid.NewGuid().ToString() : strategy.Id,
                        Name: strategy.Name,
                        Description: "Roslyn-compiled custom script.",
                        Side: OrderSide.Buy,
                        Conditions: new ConditionGroup("roslyn-placeholder", LogicOperator.And, new List<ConditionNode>()),
                        Risk: new RiskPlan(
                            Stop:    new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                            TpLadder: Array.Empty<TpLadderRung>(),
                            Sizing:  new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                            Entry:   new EntryTrigger(EntryTriggerKind.Immediate)),
                        ExecutionMode: execMode,
                        CreatedUtc: now,
                        UpdatedUtc: now,
                        IsAutoActivate: true,
                        RoslynSource: code);
                    _library.Upsert(spec);
                    _library.Save();
                }
                catch (Exception persistEx)
                {
                    // Live instance is already running — report the persist failure but
                    // keep the strategy active so the user's current session is intact.
                    return StrategyCoordinatorResult.Ok(
                        $"Strategy '{strategy.Name}' compiled and added ({execMode} mode), " +
                        $"but persistence failed: {persistEx.Message}");
                }

                return StrategyCoordinatorResult.Ok($"Strategy '{strategy.Name}' compiled, added, and saved ({execMode} mode).");
            }
            catch (Exception ex)
            {
                return StrategyCoordinatorResult.Error($"Unexpected error: {ex.Message}");
            }
        }

        /// <summary>Remove every engine instance whose <c>Strategy.Id</c> matches the given
        /// spec id. Used internally by Start (dedupe), Stop, and Delete flows.</summary>
        private void RemoveExistingInstancesOfSpec(string specId)
        {
            var matches = _engine.ActiveStrategies
                .Where(a => string.Equals(a.Strategy.Id, specId, StringComparison.OrdinalIgnoreCase))
                .Select(a => a.InstanceId)
                .ToList();
            foreach (var id in matches)
                _engine.RemoveStrategy(id);
        }
    }
}
