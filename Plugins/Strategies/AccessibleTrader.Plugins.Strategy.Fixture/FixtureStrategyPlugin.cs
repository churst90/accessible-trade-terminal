using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Plugins.Strategy.Fixture
{
    /// <summary>
    /// Tiny IStrategyPlugin whose sole purpose is to let the test harness exercise
    /// the full DLL-plugin lifecycle — load via AssemblyLoadContext, call GetStrategies,
    /// instantiate via IStrategyRegistry.CreateInstance, unload, reload. Not shipped to
    /// end users.
    /// </summary>
    public sealed class FixtureStrategyPlugin : IStrategyPlugin
    {
        public string Name => "AccessibleTrader.Plugins.Strategy.Fixture";
        public string Description => "Fixture plugin — ships a single no-op template strategy for the Phase 10-F loader tests.";

        public IReadOnlyList<ITradingStrategy> GetStrategies()
            => new ITradingStrategy[] { new FixtureStrategy() };
    }

    /// <summary>
    /// No-op strategy template. Initialize/OnBar/OnOrderFilled/OnStop are implemented as
    /// stubs so the test harness can Initialize it without throwing, but the strategy
    /// never emits a real signal — we only need to prove the template can round-trip
    /// through the registry.
    /// </summary>
    public sealed class FixtureStrategy : ITradingStrategy
    {
        public string Id => "fixture.plugin.noop.v1";
        public string Name => "Fixture No-Op";
        public string Description => "Test-only strategy template from the fixture plugin.";
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters { get; } = System.Array.Empty<StrategyParameter>();

        public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }
        public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) => null;
        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
    }
}
