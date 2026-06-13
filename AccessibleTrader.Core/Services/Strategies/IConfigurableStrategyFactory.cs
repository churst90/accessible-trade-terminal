using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Constructs a runnable <c>ConfigurableStrategy</c> from a serialized <see cref="StrategySpec"/>.
    /// Exists so the strategy modal (Session D) and any persistence-load path can create
    /// instances without manually wiring evaluator / resolver / catalog / event bus dependencies.
    /// </summary>
    public interface IConfigurableStrategyFactory
    {
        /// <summary>
        /// Creates a configured strategy ready to be passed to <c>IStrategyEngine.AddStrategy</c>.
        /// </summary>
        /// <param name="spec">The serialized strategy specification.</param>
        /// <param name="instanceId">Optional caller-supplied instance id; a GUID is generated if null.</param>
        AccessibleTrader.Sdk.Strategies.ITradingStrategy Create(StrategySpec spec, string? instanceId = null);
    }

    /// <inheritdoc />
    public class ConfigurableStrategyFactory : IConfigurableStrategyFactory
    {
        private readonly IConditionEvaluator _evaluator;
        private readonly IRiskPlanResolver _resolver;
        private readonly ISignalCatalog _catalog;
        private readonly AccessibleTrader.Core.Services.IEventBus _eventBus;
        private readonly IMultiTimeframeDataService? _mtf;
        private readonly IBacktestWarmupAnalyzer? _warmupAnalyzer;

        public ConfigurableStrategyFactory(
            IConditionEvaluator evaluator,
            IRiskPlanResolver resolver,
            ISignalCatalog catalog,
            AccessibleTrader.Core.Services.IEventBus eventBus,
            IMultiTimeframeDataService? mtf = null,
            IBacktestWarmupAnalyzer? warmupAnalyzer = null)
        {
            _evaluator = evaluator;
            _resolver  = resolver;
            _catalog   = catalog;
            _eventBus  = eventBus;
            _mtf       = mtf;
            _warmupAnalyzer = warmupAnalyzer;
        }

        public AccessibleTrader.Sdk.Strategies.ITradingStrategy Create(StrategySpec spec, string? instanceId = null)
        {
            // The same analyzer-computed warmup the backtester uses gates the live
            // engine, so live is never looser than the simulation that validated
            // the spec. Without the analyzer (e.g. minimal test hosts) the gate is
            // disabled rather than guessed.
            int warmup = _warmupAnalyzer?.RecommendedWarmup(spec) ?? 0;

            return new AccessibleTrader.Core.Strategies.ConfigurableStrategy(
                spec,
                _evaluator,
                _resolver,
                _catalog,
                _eventBus,
                instanceId ?? System.Guid.NewGuid().ToString("N"),
                _mtf,
                warmup);
        }
    }
}
