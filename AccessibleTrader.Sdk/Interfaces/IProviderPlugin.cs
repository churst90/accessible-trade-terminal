using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// The base interface for all dynamically loaded external plugins.
    /// Acts as an entry point for querying the plugin's capabilities (e.g., Data Provider, Broker).
    /// </summary>
    public interface IProviderPlugin
    {
        /// <summary>
        /// The canonical name of the plugin (e.g., "Binance", "Alpaca").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A brief description of the plugin's purpose and the services it connects to.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The set of features and capabilities (e.g., L2, Shorting) supported by this provider.
        /// </summary>
        ProviderCapabilities Capabilities { get; }

        /// <summary>
        /// Queries the plugin for a specific capability interface.
        /// </summary>
        /// <typeparam name="T">The interface type of the capability requested (e.g., IMarketDataProvider).</typeparam>
        /// <returns>The implementation of the requested capability, or null if not supported.</returns>
        T? GetCapability<T>() where T : class;
    }
}
