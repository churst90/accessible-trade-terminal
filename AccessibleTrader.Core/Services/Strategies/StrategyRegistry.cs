using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Unified <see cref="IStrategyRegistry"/> implementation that merges:
    /// <list type="bullet">
    ///   <item>Spec-backed strategies from <see cref="IStrategyLibrary"/> (built-in seeds + user-saved).</item>
    ///   <item>DLL-plugin templates from <see cref="IStrategyPluginRegistry"/>.</item>
    /// </list>
    /// <para>
    /// Catalogue entries are <c>ITradingStrategy</c> instances. For spec-backed entries the
    /// catalogue surfaces a thin <see cref="SpecCatalogEntry"/> that exposes the saved
    /// <see cref="StrategySpec.Id"/>/<see cref="StrategySpec.Name"/>/<see cref="StrategySpec.Description"/>
    /// so the UI can show them without instantiating the full runtime strategy. Actual
    /// instantiation happens lazily via <see cref="CreateInstance"/> — the spec is handed
    /// to <see cref="IConfigurableStrategyFactory.Create"/> at that point. For plugin
    /// templates we already have a real <see cref="ITradingStrategy"/> reference and
    /// clone its identity into a fresh <c>CreateInstance</c> call per selection (we
    /// return the cached template directly — callers are expected to initialise and
    /// then let the engine own its lifecycle).
    /// </para>
    /// <para>
    /// ID space: spec IDs are opaque GUIDs / <c>builtin.*</c> constants; plugin IDs are
    /// author-chosen stable strings. Collisions are resolved preferring spec entries
    /// (the library is the authoritative source for anything persisted).
    /// </para>
    /// </summary>
    public sealed class StrategyRegistry : IStrategyRegistry
    {
        private readonly IStrategyLibrary _library;
        private readonly IConfigurableStrategyFactory _factory;
        private readonly IStrategyPluginRegistry _plugins;

        public StrategyRegistry(
            IStrategyLibrary library,
            IConfigurableStrategyFactory factory,
            IStrategyPluginRegistry plugins)
        {
            _library = library;
            _factory = factory;
            _plugins = plugins;
        }

        public IReadOnlyList<ITradingStrategy> GetCatalog()
        {
            _plugins.Initialize();

            var entries = new List<ITradingStrategy>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var spec in _library.All)
            {
                if (string.IsNullOrEmpty(spec.Id)) continue;
                if (!seenIds.Add(spec.Id)) continue;
                entries.Add(new SpecCatalogEntry(spec));
            }

            foreach (var tmpl in _plugins.Templates)
            {
                if (tmpl == null || string.IsNullOrEmpty(tmpl.Id)) continue;
                if (!seenIds.Add(tmpl.Id)) continue;
                entries.Add(tmpl);
            }

            return entries;
        }

        public ITradingStrategy? CreateInstance(string strategyId)
        {
            if (string.IsNullOrEmpty(strategyId)) return null;

            var spec = _library.GetById(strategyId);
            if (spec != null) return _factory.Create(spec);

            _plugins.Initialize();
            var tmpl = _plugins.Templates.FirstOrDefault(
                t => string.Equals(t.Id, strategyId, StringComparison.OrdinalIgnoreCase));
            return tmpl;
        }
    }

    /// <summary>
    /// Read-only catalog entry that describes a saved <see cref="StrategySpec"/> without
    /// materialising its evaluator / resolver / event-bus dependencies. The UI shows
    /// these alongside live plugin templates and hands the <see cref="Id"/> back to
    /// <see cref="IStrategyRegistry.CreateInstance"/> when the user picks one.
    /// </summary>
    public sealed class SpecCatalogEntry : ITradingStrategy
    {
        private readonly StrategySpec _spec;

        public SpecCatalogEntry(StrategySpec spec)
        {
            _spec = spec;
        }

        public string Id => _spec.Id;
        public string Name => _spec.Name;
        public string Description => _spec.Description;
        public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Intermediate;
        public IReadOnlyList<StrategyParameter> Parameters { get; } = Array.Empty<StrategyParameter>();

        // Catalog entries are metadata-only. If someone adds a SpecCatalogEntry directly
        // to the engine (rather than going through CreateInstance), surface a descriptive
        // error rather than silently returning null signals.
        public void Initialize(
            IReadOnlyList<AccessibleTrader.Sdk.Models.Ohlcv> history,
            AccessibleTrader.Sdk.Models.WorkspaceState state,
            IDictionary<string, object> parameterValues)
            => throw new InvalidOperationException(
                $"SpecCatalogEntry '{_spec.Name}' is a catalog descriptor, not a runtime strategy. " +
                "Call IStrategyRegistry.CreateInstance(id) to materialise the backing spec.");

        public StrategySignal? OnBar(
            AccessibleTrader.Sdk.Models.Ohlcv newBar,
            IReadOnlyList<AccessibleTrader.Sdk.Models.Ohlcv> history,
            AccessibleTrader.Sdk.Models.WorkspaceState state) => null;

        public void OnOrderFilled(AccessibleTrader.Sdk.Trading.OrderUpdate fill) { }
        public void OnStop() { }

        public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);

        /// <summary>Exposes the backing spec so callers that know they're looking at a catalog entry can peek.</summary>
        public StrategySpec UnderlyingSpec => _spec;
    }
}
