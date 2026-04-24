using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Strategies;

/// <summary>
/// Implemented by a DLL dropped into the <c>Strategies/</c> plug-in directory so the
/// host can discover and instantiate externally-authored trading strategies without
/// a recompile. The DLL filename must match <c>AccessibleTrader.Plugins.Strategy.*.dll</c>
/// so the loader can filter confidently against the non-trading-provider plugin set.
///
/// Lifecycle:
///   1. The host scans each configured strategy-plugin directory at startup, passes the
///      DLL through the existing <see cref="AccessibleTrader.Sdk.Services.IPluginSecureStorage"/>
///      trust check, and loads it into an isolated <c>AssemblyLoadContext</c>.
///   2. <c>Activator.CreateInstance</c> constructs the plugin via its public default ctor.
///      Plugins must not depend on DI — the loader supplies no service provider; any
///      configuration state should be read from <see cref="AccessibleTrader.Sdk.Services.PluginHostServices"/>.
///   3. <see cref="GetStrategies"/> is called once to snapshot the exported template set.
///      The host caches these templates in <c>IStrategyPluginRegistry</c> for the
///      lifetime of the process. Returned instances act as catalog prototypes; the host
///      clones or re-instantiates them per user selection.
///   4. On shutdown (or a user-initiated reload) the host unloads every plugin ALC. Plugins
///      that need cleanup should implement <see cref="System.IDisposable"/> on the template
///      instances — <c>IStrategyPluginRegistry</c> disposes them before unload.
///
/// Plugin authors: the exported <see cref="ITradingStrategy"/> instances are the entries
/// that show up in the strategy modal template picker. Give them stable, globally-unique
/// <see cref="ITradingStrategy.Id"/> values (e.g. <c>"myorg.momentum.v1"</c>) so saved
/// workspaces can rehydrate the same strategy across launches.
/// </summary>
public interface IStrategyPlugin
{
    /// <summary>Display name of the plugin (used for logging and the About panel).</summary>
    string Name { get; }

    /// <summary>One-line human description shown in the strategy modal.</summary>
    string Description { get; }

    /// <summary>
    /// Returns the template strategies this plugin exports. Called exactly once per
    /// load; results are cached by the host. Returning an empty list is valid and
    /// hides the plugin from the catalog without erroring.
    /// </summary>
    IReadOnlyList<ITradingStrategy> GetStrategies();
}
