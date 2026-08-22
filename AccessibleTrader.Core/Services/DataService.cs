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
                // PlatformPaths, not GetFolderPath: an empty return on Unix would make this
                // RELATIVE and load DLLs out of the process's working directory. Machine-level by
                // design — plugin drop-ins are executable code, never per-user state.
                var userPath = Path.Combine(PlatformPaths.AppDataRoot(), "Plugins");
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

        /// <summary>
        /// Configure every provider that has an active stored key, directly from the
        /// key store. Provider configuration is otherwise lazy (first data fetch), but
        /// <see cref="MarketOrchestrator"/>.RefreshSymbolsAsync gates on
        /// <c>IsConfigured</c> *before* that fetch — so without this a key-required
        /// provider shows the "API key required" sentinel and never self-configures.
        /// Call once after <see cref="InitializeAsync"/>, and again after a key is saved.
        /// Idempotent: skips providers that are already configured. Configures from the
        /// key directly, so it is unaffected by the MarketType/sub-type lookup mismatch.
        /// </summary>
        public async Task ConfigureStoredKeyProvidersAsync()
        {
            if (!_isInitialized) return;

            List<ApiKeyConfig> keys;
            try { keys = await _apiKeyService.GetAllKeysAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not load stored keys for provider configuration."); return; }

            foreach (var k in keys)
            {
                // AllowsWithdrawal is checked even though such profiles are never
                // active through the UI: the flag predates its checkbox and was set
                // by editing storage, so an active withdrawal profile can exist —
                // and its key must never become a provider's session credential.
                if (!k.IsActive || k.AllowsWithdrawal || string.IsNullOrEmpty(k.ApiKey)) continue;
                var provider = _providers.FirstOrDefault(p => p.Name.Equals(k.Provider, StringComparison.OrdinalIgnoreCase));
                if (provider == null || provider.IsConfigured) continue;
                try
                {
                    provider.Configure(new Dictionary<string, string>
                    {
                        { "ApiKey", k.ApiKey },
                        { "ApiSecret", k.ApiSecret ?? "" },
                        { "Passphrase", k.Passphrase ?? "" }
                    });
                    _logger.LogInformation("Configured provider {Provider} from stored key '{Nickname}'.", k.Provider, k.Nickname);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to configure provider {Provider} from stored key.", k.Provider);
                }
            }
        }

        public void RegisterProvider(IMarketDataProvider provider)
        {
            if (_providers.Any(p => p.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase)))
                return;
            _providers.Add(provider);
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
                // Defense-in-depth: an analytics market (OnChain / Economic / Derivatives /
                // Sentiment) is non-tradeable by definition, so only SingleValueLine providers
                // may appear there. Tradeable markets (Crypto / Stock / Forex / etc.) get Ohlcv
                // providers only. Even if a plugin mis-declares SupportedMarkets, the DataShape
                // check prevents an analytics provider from leaking into a tradeable dropdown.
                bool isAnalyticsMarket =
                    type == MarketType.OnChain    ||
                    type == MarketType.Economic   ||
                    type == MarketType.Derivatives ||
                    type == MarketType.Sentiment;

                // MyData is exempt: for imported CSVs the shape belongs to the SYMBOL, not the
                // provider (an OHLCV import is candles, a budget column is a line), and
                // MyDataProvider answers per symbol via GetDataShapeForSymbol. Reading its
                // class-level default as a verdict dropped the only provider this market has,
                // leaving My Data listed — LoadAvailableMarketsAsync applies no shape filter —
                // with an empty provider dropdown behind it. The rule above guards against a
                // plugin mis-declaring SupportedMarkets; MyData has nothing to guard against.
                bool shapeIsPerSymbol = type == MarketType.MyData;

                return Task.FromResult(_providers
                    .Where(p => p.SupportedMarkets.Contains(type))
                    .Where(p => shapeIsPerSymbol
                        || (isAnalyticsMarket
                            ? p.DataShape == ProviderDataShape.SingleValueLine
                            : p.DataShape == ProviderDataShape.Ohlcv))
                    .Select(p => p.Name)
                    .ToList());
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

            // My Data symbols change the moment the user imports a file — the
            // 24h cache below would hide new datasets until tomorrow.
            bool cacheable = !marketTypeStr.Equals(nameof(MarketType.MyData), StringComparison.OrdinalIgnoreCase);

            var cacheKey = $"symbols_{providerName}_{marketInfo}";
            if (cacheable)
            {
                var cached = await _cacheService.GetAsync<List<string>>(cacheKey).ConfigureAwait(false);
                if (cached != null) return cached;
            }

            try
            {
                if (Enum.TryParse<MarketType>(marketTypeStr, out var marketType))
                {
                    var symbols = await provider.GetAvailableSymbolsAsync(marketType, subType).ConfigureAwait(false);
                    if (cacheable && symbols != null && symbols.Any())
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

        /// <summary>
        /// Fetches bars from one provider.
        ///
        /// <para>
        /// <b>Throws.</b> This method is the innermost body of the pipeline's retry +
        /// circuit-breaker policy (<see cref="DataOrchestrator"/>), and it used to end
        /// in <c>catch (Exception) { return empty; }</c>. That one catch made the whole
        /// resilience layer decorative: no transport exception could reach the policy,
        /// so the retries never retried, the breakers never tripped,
        /// <c>onBreak</c>/<c>onReset</c> never fired, and neither
        /// <c>ConnectionStatusEvent(Error)</c> nor <c>DataTrigger.ErrorOccurred</c> was
        /// reachable from a network failure. The only symptom a user ever got was an
        /// empty chart — silence, on a terminal whose entire premise is that you cannot
        /// see the screen.
        /// </para>
        /// <para>
        /// A policy whose body cannot fail is not a policy. Every caller
        /// (<see cref="HistoricalDataFetcher"/> via the orchestrator's policy,
        /// <c>BackfillManager</c>, <c>MarketFeeds</c>) has its own terminal handler, so
        /// letting the exception out costs nothing and is what makes the layer real.
        /// </para>
        /// </summary>
        public async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(string providerName, MarketDataRequest request)
        {
            if (!_isInitialized) return (new List<Ohlcv>(), new List<(long, double)>());
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            if (provider == null) return (new List<Ohlcv>(), new List<(long, double)>());

            var parts = request.Market.Split('|');
            string subType = parts.Length > 1 ? parts[1] : "Spot";
            await EnsureProviderConfiguredAsync(providerName, subType).ConfigureAwait(false);

            // ── Analytics series cache ──────────────────────────────────────────────
            // Economic / on-chain / derivatives / sentiment series are published on a slow
            // schedule — FRED revises daily at best, most on-chain metrics are one point per
            // day — yet every chart load, asset switch and timeframe change re-fetched them
            // from scratch. That is wasted latency for the user and wasted quota against
            // providers that meter us (FRED allows 120 requests/minute; the free CoinGecko
            // tier is far tighter).
            //
            // Only analytics markets are cached. Live crypto/stock bars must NOT be: the last
            // bar changes on every tick, and serving a cached one would freeze the chart.
            //
            // Both halves are guarded on their own: a corrupt or unreadable cache file is a
            // LOCAL fault, and letting it out of here would count against the network circuit
            // breaker and be announced to the user as a connection problem. Falling through to
            // the provider is always the right answer.
            var cacheKey = AnalyticsCacheKey(providerName, request);
            if (cacheKey != null)
            {
                try
                {
                    var hit = await _cacheService.GetAsync<CachedSeries>(cacheKey).ConfigureAwait(false);
                    if (hit?.Bars is { Count: > 0 })
                    {
                        _logger.LogDebug("Analytics cache hit for {Provider} {Symbol} {Timeframe}.",
                            providerName, request.Symbol, request.Timeframe);
                        return (hit.Bars, hit.Volume.Select(v => (v.Timestamp, v.Volume)).ToList());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analytics cache read failed for {Key}; fetching from the provider.", cacheKey);
                }
            }

            var fetched = await provider.FetchOhlcvAsync(request).ConfigureAwait(false);

            if (cacheKey != null && fetched.Ohlcv is { Count: > 0 })
            {
                try
                {
                    var payload = new CachedSeries
                    {
                        Bars   = fetched.Ohlcv,
                        Volume = fetched.Volume.Select(v => new CachedVolumePoint
                        {
                            Timestamp = v.Timestamp,
                            Volume    = v.Volume
                        }).ToList()
                    };
                    await _cacheService.SetAsync(cacheKey, payload, AnalyticsCacheTtl(request.Timeframe))
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analytics cache write failed for {Key}; the bars are still returned.", cacheKey);
                }
            }

            return fetched;
        }

        // ── Analytics series cache ───────────────────────────────────────────────────────
        //
        // Cached to disk via ICacheService, whose directory is deliberately SHARED across hosted
        // users (public market data, one copy for everyone). So the first person to chart CPI
        // warms it for the next — which is the point of caching a research dataset rather than a
        // per-user one.

        /// <summary>Analytics markets: published on a slow schedule, safe to serve from cache.
        /// Kept in step with MarketOrchestrator.AnalyticsCategories.</summary>
        private static readonly string[] CacheableMarkets =
            { "Economic", "OnChain", "Derivatives", "Sentiment" };

        /// <summary>
        /// The cache key for an analytics request, or <c>null</c> when this request must always hit
        /// the provider (any tradeable market — its last bar moves on every tick).
        /// </summary>
        // Internal (not private): the cache POLICY — which markets, and for how long — is the part
        // worth pinning in tests, and it is pure. See AnalyticsSeriesCacheTests.
        internal static string? AnalyticsCacheKey(string providerName, MarketDataRequest request)
        {
            // "Economic|Standard" → "Economic".
            string category = (request.Market ?? "").Split('|')[0];
            if (!CacheableMarkets.Contains(category, StringComparer.OrdinalIgnoreCase)) return null;

            // Every field that changes the response is in the key. Since/Until included: a
            // historical window request must not be served the live-edge window.
            return $"series_{providerName}_{request.Market}_{request.Symbol}_{request.Timeframe}_" +
                   $"{request.Limit}_{request.Since?.ToString() ?? "-"}_{request.Until?.ToString() ?? "-"}";
        }

        /// <summary>
        /// How long an analytics series stays fresh: half its bar interval, clamped to 15 minutes
        /// … 12 hours. A daily series (FRED, most on-chain metrics) caches for 12 h — it cannot
        /// change more often than daily anyway — while hourly derivatives data (funding, open
        /// interest) refreshes every 30 minutes. The clamp is what keeps a bad or missing timeframe
        /// string from producing either a useless 0-second TTL or a week-long stale chart.
        /// </summary>
        internal static TimeSpan AnalyticsCacheTtl(string timeframe)
        {
            long barMs = TimeframeUtility.ToMilliseconds(timeframe ?? "");
            if (barMs <= 0) return TimeSpan.FromHours(1);
            var half = TimeSpan.FromMilliseconds(barMs / 2.0);
            if (half < TimeSpan.FromMinutes(15)) return TimeSpan.FromMinutes(15);
            if (half > TimeSpan.FromHours(12)) return TimeSpan.FromHours(12);
            return half;
        }

        /// <summary>Cache payload. A DTO rather than the raw
        /// <c>(List&lt;Ohlcv&gt;, List&lt;(long, double)&gt;)</c> tuple because System.Text.Json
        /// does not round-trip ValueTuple — its members are fields, not properties, so a tuple
        /// serialises to <c>{}</c> and every cache read would come back empty.</summary>
        private sealed class CachedSeries
        {
            public List<Ohlcv> Bars { get; set; } = new();
            public List<CachedVolumePoint> Volume { get; set; } = new();
        }

        private sealed class CachedVolumePoint
        {
            public long Timestamp { get; set; }
            public double Volume { get; set; }
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

        public Task<bool> IsProviderConfiguredAsync(string providerName) =>
            Task.FromResult(IsProviderConfigured(providerName));

        public bool IsProviderConfigured(string providerName)
        {
            if (!_isInitialized) return false;
            var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));
            return provider?.IsConfigured ?? false;
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
