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

        /// <summary>
        /// The account's live cash, already maintained for the quick-trade sizing path.
        ///
        /// <para>Optional: absent, every strategy sizes against its plan's static
        /// <c>NotionalEquity</c>, which is the pre-2026-08-27 behaviour and the only honest
        /// answer for a host with no account attached. Present, a strategy on a $500 account
        /// stops being sized against a 10000 someone left in the risk editor.</para>
        /// </summary>
        private readonly AccessibleTrader.Core.Services.Trading.QuickTradeEquity? _equity;

        public ConfigurableStrategyFactory(
            IConditionEvaluator evaluator,
            IRiskPlanResolver resolver,
            ISignalCatalog catalog,
            AccessibleTrader.Core.Services.IEventBus eventBus,
            IMultiTimeframeDataService? mtf = null,
            IBacktestWarmupAnalyzer? warmupAnalyzer = null,
            AccessibleTrader.Core.Services.Trading.QuickTradeEquity? equity = null)
        {
            _evaluator = evaluator;
            _resolver  = resolver;
            _catalog   = catalog;
            _eventBus  = eventBus;
            _mtf       = mtf;
            _warmupAnalyzer = warmupAnalyzer;
            _equity    = equity;
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
                warmup,
                // Returns NULL until a balance has actually been reported: QuickTradeEquity
                // .Latest is 0 before then, and 0 would read as "an account with nothing in
                // it" rather than "we do not know yet". Sizing must fall back to the plan's
                // notional in that window, not refuse — a strategy that silently never fires
                // is indistinguishable from a market with no signals.
                _equity == null
                    ? null
                    : () => _equity.Latest > 0 ? _equity.Latest : (double?)null);
        }
    }
}
