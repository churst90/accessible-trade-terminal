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
using Polly;
using Polly.CircuitBreaker;
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
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private bool _isInitialized;

        public DataService(IPluginLoaderService pluginLoader, ILogger<DataService> logger, ICacheService cacheService, IApiKeyService apiKeyService, IGlobalErrorCoordinator errorCoordinator)
        {
            _logger = logger;
            _cacheService = cacheService;
            _apiKeyService = apiKeyService;
            _errorCoordinator = errorCoordinator;

            _circuitBreaker = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(10),
                    onBreak: (ex, breakDelay) => {
                        _errorCoordinator.ReportError($"Data provider circuit broken. Pausing requests for {breakDelay.TotalSeconds} seconds.", ErrorSeverity.High, ErrorCategory.Systemic);
                    },
                    onReset: () => {
                        _errorCoordinator.ReportSuccess("Data provider connection restored.");
                    },
                    onHalfOpen: () => {
                        _logger.LogInformation("Circuit breaker half-open. Testing connection...");
                    }
                );
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
                
                _logger.LogInformation($"Total Unique Loaded Data Providers: {_providers.Count}");
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
                _logger.LogError(ex, $"Error during plugin loading from {path}");
            }
        }

        private async Task EnsureProviderConfiguredAsync(string providerName, string marketType = "Spot")
        {
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return;

            var key = await _apiKeyService.GetKeyForProviderAsync(providerName, marketType);
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

        public async Task<List<string>> LoadAvailableMarketsAsync()
        {
            if (!_isInitialized) return new List<string>();
            var markets = new HashSet<MarketType>();
            foreach (var provider in _providers)
            {
                foreach (var m in provider.SupportedMarkets) markets.Add(m);
            }
            return markets.Select(m => m.ToString()).ToList();
        }

        public async Task<List<string>> LoadProvidersAsync()
        {
            if (!_isInitialized) return new List<string>();
            return _providers.Select(p => p.Name).ToList();
        }

        public async Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType)
        {
            if (!_isInitialized) return new List<string>();
            if (Enum.TryParse<MarketType>(marketType, out var type))
            {
                return _providers.Where(p => p.SupportedMarkets.Contains(type)).Select(p => p.Name).ToList();
            }
            return new List<string>();
        }

        public async Task<List<string>> GetSupportedSubTypesAsync(string providerName, string marketTypeStr)
        {
            if (!_isInitialized) return new List<string> { "Spot" };
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return new List<string> { "Spot" };

            if (Enum.TryParse<MarketType>(marketTypeStr, out var marketType))
            {
                return await provider.GetSupportedSubTypesAsync(marketType);
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

            await EnsureProviderConfiguredAsync(providerName, subType);

            var cacheKey = $"symbols_{providerName}_{marketInfo}";
            var cached = await _cacheService.GetAsync<List<string>>(cacheKey);
            if (cached != null) return cached;

            try
            {
                if (Enum.TryParse<MarketType>(marketTypeStr, out var marketType))
                {
                    return await _circuitBreaker.ExecuteAsync(async () => {
                        var symbols = await provider.GetAvailableSymbolsAsync(marketType, subType)!;
                        if (symbols != null && symbols.Any())
                        {
                            await _cacheService.SetAsync(cacheKey, symbols, TimeSpan.FromHours(24));
                        }
                        return symbols ?? new List<string>();
                    });
                }
                return new List<string>();
            }
            catch (Exception)
            {
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
                await EnsureProviderConfiguredAsync(providerName, subType);

                return await _circuitBreaker.ExecuteAsync(async () => {
                    return await provider.FetchOhlcvAsync(request);
                });
            }
            catch (Exception)
            {
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public async Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string providerName)
        {
            if (!_isInitialized) return new List<MarketType>();
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider != null ? provider.SupportedMarkets : new List<MarketType>();
        }

        public async Task<List<string>> GetSupportedTimeframesAsync(string providerName)
        {
            if (!_isInitialized) return new List<string>();
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider != null ? await provider.GetSupportedTimeframesAsync() : new List<string>();
        }

        public async Task<bool> IsProviderConfiguredAsync(string providerName)
        {
            if (!_isInitialized) return false;
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider != null ? provider.IsConfigured : false;
        }

        public async Task<bool> ProviderRequiresApiKeyAsync(string providerName)
        {
            if (!_isInitialized) return false;
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider != null ? provider.RequiresApiKey : false;
        }

        public async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string providerName, string symbol, int limit = 10)
        {
            if (!_isInitialized) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            
            await EnsureProviderConfiguredAsync(providerName, "Spot");
            
            try
            {
                return await _circuitBreaker.ExecuteAsync(async () => {
                    return await provider.GetOrderBookAsync(symbol, limit);
                });
            }
            catch (Exception)
            {
                 return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        public async Task<IMarketDataProvider?> GetProviderAsync(string name)
        {
            if (!_isInitialized) return null;
            return _providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<IProviderPlugin?> GetPluginAsync(string name)
        {
            if (!_isInitialized) return null;
            return _plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
