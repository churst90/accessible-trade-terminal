using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services
{
    public interface IDataService
    {
        Task InitializeAsync(IPluginLoaderService pluginLoader);

        /// <summary>Registers a built-in (non-plugin) data provider — e.g. the
        /// My Data CSV provider. Idempotent by provider name.</summary>
        void RegisterProvider(Sdk.Plugins.IMarketDataProvider provider);

        /// <summary>Configure every provider that has an active stored key, directly
        /// from the key store, so key-required providers are usable immediately. Call
        /// after InitializeAsync and after a key is saved. Idempotent.</summary>
        Task ConfigureStoredKeyProvidersAsync();

        Task<List<string>> LoadAvailableMarketsAsync();
        Task<List<string>> LoadProvidersAsync();
        Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType);
        Task<List<string>> GetSupportedSubTypesAsync(string provider, string marketType);
        Task<List<string>> LoadSymbolsAsync(string marketInfo, string provider);
        Task<List<string>> GetSupportedTimeframesAsync(string provider);
        Task<bool> IsProviderConfiguredAsync(string provider);
        /// <summary>Synchronous convenience — same check without the Task wrapper. Use from
        /// sync call sites to avoid GetAwaiter().GetResult() on a
        /// value that's always available without I/O.</summary>
        bool IsProviderConfigured(string provider);
        Task<bool> ProviderRequiresApiKeyAsync(string provider);
        Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(string provider, MarketDataRequest request, CancellationToken ct = default);
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string provider, string symbol, int limit = 10);
        Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string provider);
        Task<IMarketDataProvider?> GetProviderAsync(string name);
        Task<IProviderPlugin?> GetPluginAsync(string name);
    }
}
