using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services
{
    public interface IDataService
    {
        Task InitializeAsync(IPluginLoaderService pluginLoader);
        Task<List<string>> LoadAvailableMarketsAsync();
        Task<List<string>> LoadProvidersAsync();
        Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType);
        Task<List<string>> GetSupportedSubTypesAsync(string provider, string marketType);
        Task<List<string>> LoadSymbolsAsync(string marketInfo, string provider);
        Task<List<string>> GetSupportedTimeframesAsync(string provider);
        Task<bool> IsProviderConfiguredAsync(string provider);
        Task<bool> ProviderRequiresApiKeyAsync(string provider);
        Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(string provider, MarketDataRequest request);
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string provider, string symbol, int limit = 10);
        Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string provider);
        Task<IMarketDataProvider?> GetProviderAsync(string name);
        Task<IProviderPlugin?> GetPluginAsync(string name);
    }
}
