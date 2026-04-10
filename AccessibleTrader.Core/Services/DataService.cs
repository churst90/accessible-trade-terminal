using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Enums;
using Microsoft.Extensions.Logging;
using System.IO;

using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Core.Services
{
    public class DataService : IDataService
    {
        private readonly List<IMarketDataProvider> _providers = new();
        private readonly List<IProviderPlugin> _plugins = new();
        private readonly ILogger<DataService> _logger;
        private readonly ICacheService _cacheService;
        private readonly IApiKeyService _apiKeyService;
        private readonly IGlobalErrorCoordinator _errorCoordinator;
        private bool _isInitialized;

        public DataService(IPluginLoaderService pluginLoader, ILogger<DataService> logger, ICacheService cacheService, IApiKeyService apiKeyService, IGlobalErrorCoordinator errorCoordinator)
        {
            _logger = logger;
            _cacheService = cacheService;
            _apiKeyService = apiKeyService;
            _errorCoordinator = errorCoordinator;
        }

        public async Task InitializeAsync(IPluginLoaderService pluginLoader)
        {
            if (_isInitialized) return;

            try 
            {
                _logger.LogInformation("Initializing DataService plugins...");
                
                // 1. Scan the base execution directory (where MAUI often flattens DLLs)
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                LoadPluginsFromPath(pluginLoader, baseDir);

                // 2. Scan a specific Plugins subfolder if it exists
                var installPath = Path.Combine(baseDir, "Plugins");
                if (Directory.Exists(installPath)) LoadPluginsFromPath(pluginLoader, installPath);

                // 3. Load from Writable User Directory (User Drop-ins)
                var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "Plugins");
                if (!Directory.Exists(userPath)) Directory.CreateDirectory(userPath);
                LoadPluginsFromPath(pluginLoader, userPath);
                
                _logger.LogInformation("Total Unique Loaded Data Providers: {Count}.", _providers.Count);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin initialization failed");
            }
        }

        private void LoadPluginsFromPath(IPluginLoaderService pluginLoader, string path)
        {
            if (!Directory.Exists(path)) return;

            try 
            {
                var loadedPlugins = pluginLoader.LoadPlugins<IProviderPlugin>(path).ToList();
                foreach (var plugin in loadedPlugins)
                {
                    if (_plugins.Any(p => p.Name == plugin.Name)) continue;
                    _plugins.Add(plugin);
                    var dataProvider = plugin.GetCapability<IMarketDataProvider>();
                    if (dataProvider != null) _providers.Add(dataProvider);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during plugin loading from {Path}.", path);
            }
        }

        private async Task EnsureProviderConfiguredAsync(string providerName, string marketType = "Spot")
        {
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return;

            var key = await _apiKeyService.GetKeyForProviderAsync(providerName, marketType).ConfigureAwait(false);
            if (key != null)
            {
                var config = new Dictionary<string, string>
                {
                    { "ApiKey", key.ApiKey },
                    { "ApiSecret", key.ApiSecret },
                    { "Passphrase", key.Passphrase }
                };
                provider.Configure(config);
            }
        }

        public Task<List<string>> LoadAvailableMarketsAsync()
        {
            if (!_isInitialized) return Task.FromResult(new List<string>());
            var markets = new HashSet<MarketType>();
            foreach (var provider in _providers)
            {
                foreach (var m in provider.SupportedMarkets) markets.Add(m);
            }
            return Task.FromResult(markets.Select(m => m.ToString()).ToList());
        }

        public Task<List<string>> LoadProvidersAsync()
        {
            if (!_isInitialized) return Task.FromResult(new List<string>());
            return Task.FromResult(_providers.Select(p => p.Name).ToList());
        }

        public Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType)
        {
            if (!_isInitialized) return Task.FromResult(new List<string>());
            if (Enum.TryParse<MarketType>(marketType, out var type))
            {
                return Task.FromResult(_providers.Where(p => p.SupportedMarkets.Contains(type)).Select(p => p.Name).ToList());
            }
            return Task.FromResult(new List<string>());
        }

        public async Task<List<string>> GetSupportedSubTypesAsync(string providerName, string marketTypeStr)
        {
            if (!_isInitialized) return new List<string> { "Spot" };
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return new List<string> { "Spot" };

            if (Enum.TryParse<MarketType>(marketTypeStr, out var marketType))
            {
                return await provider.GetSupportedSubTypesAsync(marketType).ConfigureAwait(false);
            }
            return new List<string> { "Spot" };
        }

        public async Task<List<string>> LoadSymbolsAsync(string marketInfo, string providerName)
        {
            if (!_isInitialized) return new List<string>();
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return new List<string>();

            var parts = marketInfo.Split('|');
            string marketTypeStr = parts[0];
            string subType = parts.Length > 1 ? parts[1] : "Spot";

            await EnsureProviderConfiguredAsync(providerName, subType).ConfigureAwait(false);

            var cacheKey = $"symbols_{providerName}_{marketInfo}";
            var cached = await _cacheService.GetAsync<List<string>>(cacheKey).ConfigureAwait(false);
            if (cached != null) return cached;

            try
            {
                if (Enum.TryParse<MarketType>(marketTypeStr, out var marketType))
                {
                    var symbols = await provider.GetAvailableSymbolsAsync(marketType, subType).ConfigureAwait(false);
                    if (symbols != null && symbols.Any())
                    {
                        await _cacheService.SetAsync(cacheKey, symbols, TimeSpan.FromHours(24)).ConfigureAwait(false);
                    }
                    return symbols ?? new List<string>();
                }
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load symbols for {Provider} ({Market}).", providerName, marketInfo);
                return new List<string>();
            }
        }

        public async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(string providerName, MarketDataRequest request)
        {
            if (!_isInitialized) return (new List<Ohlcv>(), new List<(long, double)>());
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return (new List<Ohlcv>(), new List<(long, double)>());

            try
            {
                var parts = request.Market.Split('|');
                string subType = parts.Length > 1 ? parts[1] : "Spot";
                await EnsureProviderConfiguredAsync(providerName, subType).ConfigureAwait(false);

                return await provider.FetchOhlcvAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch OHLCV for {Symbol}.", request.Symbol);
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string providerName)
        {
            if (!_isInitialized) return Task.FromResult(new List<MarketType>());
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(provider != null ? provider.SupportedMarkets : new List<MarketType>());
        }

        public async Task<List<string>> GetSupportedTimeframesAsync(string providerName)
        {
            if (!_isInitialized) return new List<string>();
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider != null ? await provider.GetSupportedTimeframesAsync().ConfigureAwait(false) : new List<string>();
        }

        public Task<bool> IsProviderConfiguredAsync(string providerName)
        {
            if (!_isInitialized) return Task.FromResult(false);
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(provider?.IsConfigured ?? false);
        }

        public Task<bool> ProviderRequiresApiKeyAsync(string providerName)
        {
            if (!_isInitialized) return Task.FromResult(false);
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(provider?.RequiresApiKey ?? false);
        }

        public async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string providerName, string symbol, int limit = 10)
        {
            if (!_isInitialized) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            
            await EnsureProviderConfiguredAsync(providerName, "Spot").ConfigureAwait(false);

            try
            {
                return await provider.GetOrderBookAsync(symbol, limit).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get order book for {Symbol} from {Provider}.", symbol, providerName);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        public Task<IMarketDataProvider?> GetProviderAsync(string name)
        {
            if (!_isInitialized) return Task.FromResult<IMarketDataProvider?>(null);
            return Task.FromResult<IMarketDataProvider?>(_providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IProviderPlugin?> GetPluginAsync(string name)
        {
            if (!_isInitialized) return Task.FromResult<IProviderPlugin?>(null);
            return Task.FromResult<IProviderPlugin?>(_plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
