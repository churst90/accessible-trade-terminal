using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Phase 10-F(c). Pins the unified <see cref="IStrategyRegistry"/> contract: the
/// catalogue merges built-in seeds + user-saved specs from <see cref="IStrategyLibrary"/>
/// with DLL-plugin templates from <see cref="IStrategyPluginRegistry"/>, and
/// <see cref="IStrategyRegistry.CreateInstance"/> prefers specs over plugin templates
/// when both use the same ID.
/// </summary>
public sealed class StrategyRegistryCatalogTests
{
    [Fact]
    public void Catalog_merges_spec_library_and_plugin_templates()
    {
        var library = new FakeLibrary(new[]
        {
            BuildSpec("spec.alpha", "Alpha Spec"),
            BuildSpec("spec.beta",  "Beta Spec"),
        });
        var plugins = new FakePluginRegistry(new ITradingStrategy[]
        {
            new StubStrategy("plugin.gamma", "Gamma Plugin"),
            new StubStrategy("plugin.delta", "Delta Plugin"),
        });
        var registry = new StrategyRegistry(library, new StubFactory(), plugins);

        var catalog = registry.GetCatalog();

        Assert.Equal(4, catalog.Count);
        var ids = catalog.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("spec.alpha",   ids);
        Assert.Contains("spec.beta",    ids);
        Assert.Contains("plugin.gamma", ids);
        Assert.Contains("plugin.delta", ids);
    }

    [Fact]
    public void Catalog_deduplicates_on_id_collision_preferring_spec_over_plugin()
    {
        // When a plugin template happens to share an ID with a saved spec (e.g. the
        // plugin author picked an ID that collides with a built-in seed), the spec
        // wins — the library is the persistence source of truth.
        var library = new FakeLibrary(new[] { BuildSpec("shared.id", "Spec Name") });
        var plugins = new FakePluginRegistry(new ITradingStrategy[]
        {
            new StubStrategy("shared.id", "Plugin Name"),
        });
        var registry = new StrategyRegistry(library, new StubFactory(), plugins);

        var catalog = registry.GetCatalog();

        Assert.Single(catalog);
        Assert.IsType<SpecCatalogEntry>(catalog[0]);
        Assert.Equal("Spec Name", catalog[0].Name);
    }

    [Fact]
    public void CreateInstance_spec_id_returns_factory_built_strategy()
    {
        var library = new FakeLibrary(new[] { BuildSpec("spec.alpha", "Alpha") });
        var plugins = new FakePluginRegistry(Array.Empty<ITradingStrategy>());
        var factory = new StubFactory();
        var registry = new StrategyRegistry(library, factory, plugins);

        var created = registry.CreateInstance("spec.alpha");

        Assert.NotNull(created);
        Assert.Equal(1, factory.CreateCallCount);
        Assert.Equal("spec.alpha", created!.Id);
    }

    [Fact]
    public void CreateInstance_plugin_id_returns_cached_template()
    {
        var gamma = new StubStrategy("plugin.gamma", "Gamma");
        var library = new FakeLibrary(Array.Empty<StrategySpec>());
        var plugins = new FakePluginRegistry(new ITradingStrategy[] { gamma });
        var registry = new StrategyRegistry(library, new StubFactory(), plugins);

        var created = registry.CreateInstance("plugin.gamma");

        Assert.Same(gamma, created);
    }

    [Fact]
    public void CreateInstance_unknown_id_returns_null()
    {
        var library = new FakeLibrary(Array.Empty<StrategySpec>());
        var plugins = new FakePluginRegistry(Array.Empty<ITradingStrategy>());
        var registry = new StrategyRegistry(library, new StubFactory(), plugins);

        Assert.Null(registry.CreateInstance("no.such.id"));
        Assert.Null(registry.CreateInstance(""));
    }

    // ── Test fixtures ───────────────────────────────────────────────────────

    private static StrategySpec BuildSpec(string id, string name) =>
        new(id, name, "desc", OrderSide.Buy,
            new ConditionLeaf($"{id}.leaf", "x", LeafOperator.Fired),
            new RiskPlan(
                Stop:    new StopSource(StopSourceKind.PercentOfPrice, PercentValue: 1.0),
                TpLadder: Array.Empty<TpLadderRung>(),
                Sizing:  new PositionSizing(Mode: SizingMode.FixedRiskPercent, RiskPercent: 0.005),
                Entry:   new EntryTrigger(EntryTriggerKind.Immediate)),
            StrategyExecutionMode.Suggestion);

    private sealed class FakeLibrary : IStrategyLibrary
    {
        private readonly List<StrategySpec> _items;
        public FakeLibrary(IEnumerable<StrategySpec> items) { _items = items.ToList(); }
        public IReadOnlyList<StrategySpec> All => _items;
        public StrategySpec? GetById(string id) =>
            _items.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        public void Upsert(StrategySpec spec)
        {
            var idx = _items.FindIndex(s => string.Equals(s.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _items[idx] = spec; else _items.Add(spec);
        }
        public void Remove(string id) =>
            _items.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        public void Save() { }
        public void Reload() { }
    }

    private sealed class FakePluginRegistry : IStrategyPluginRegistry
    {
        public FakePluginRegistry(IEnumerable<ITradingStrategy> templates)
        {
            Templates = templates.ToList();
            LoadedPluginNames = new[] { "fake" };
        }
        public IReadOnlyList<ITradingStrategy> Templates { get; }
        public IReadOnlyList<string> LoadedPluginNames { get; }
        public void Initialize() { }
        public void UnloadAll() { }
    }

    private sealed class StubFactory : IConfigurableStrategyFactory
    {
        public int CreateCallCount { get; private set; }
        public ITradingStrategy Create(StrategySpec spec, string? instanceId = null)
        {
            CreateCallCount++;
            return new StubStrategy(spec.Id, spec.Name);
        }
    }

    private sealed class StubStrategy : ITradingStrategy
    {
        public StubStrategy(string id, string name) { Id = id; Name = name; }
        public string Id { get; }
        public string Name { get; }
        public string Description => "stub";
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
        public IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();
        public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }
        public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state) => null;
        public void OnOrderFilled(OrderUpdate fill) { }
        public void OnStop() { }
        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
    }
}
