using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Glassnode
{
    /// <summary>
    /// Glassnode on-chain metrics for Bitcoin and Ethereum. The free Tier 1 plan exposes
    /// roughly 10 metrics across BTC and ETH at daily resolution — enough to build
    /// regime-aware strategies that have a non-price input the Cipher framework cannot see.
    ///
    /// ── Free metrics this provider exposes ──────────────────────────────────────
    /// Each metric is keyed by symbol "{ASSET}_{METRIC_CODE}" so the dropdown stays clear.
    ///
    ///   BTC_ACTIVE_ADDRS    — Bitcoin active addresses (network usage)
    ///   BTC_TX_COUNT        — Bitcoin daily transaction count
    ///   BTC_PRICE_USD       — Glassnode reference price (sanity check)
    ///   BTC_MARKET_CAP      — Market cap USD
    ///   BTC_HASH_RATE       — Hash rate (security / miner activity proxy)
    ///   ETH_ACTIVE_ADDRS    — Ethereum active addresses
    ///   ETH_TX_COUNT        — Ethereum daily transaction count
    ///
    /// Strategy use:
    ///   • BTC_ACTIVE_ADDRS rising at price oversold = network usage holding up while price
    ///     pukes → bullish divergence between fundamentals and price.
    ///   • BTC_HASH_RATE making new highs = miner conviction increasing, often leads price.
    ///
    /// ── API ─────────────────────────────────────────────────────────────────────
    /// Glassnode REST: https://api.glassnode.com/v1/metrics/&lt;category&gt;/&lt;metric&gt;
    /// Auth: API key as query string parameter `api_key=...`
    /// Free tier: ~10 metrics, daily resolution, rate limit 30 req/min
    /// Sign up free: https://glassnode.com (no credit card)
    ///
    /// Configure via Settings → API Keys → Glassnode → paste your free key.
    /// </summary>
    public class GlassnodeProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "Glassnode",
            allowedHosts: new[] { "api.glassnode.com" });

        private readonly RateLimiter _rateLimiter = new(30, TimeSpan.FromMinutes(1));
        private string? _apiKey;

        /// <summary>
        /// The API key, escaped for use as a query-string VALUE.
        ///
        /// <para>A raw interpolation mangles any key that is not already URL-safe: <c>&amp;</c>
        /// ends the parameter and starts a new one (the key is TRUNCATED at the ampersand),
        /// <c>+</c> decodes to a space at the server, <c>#</c> throws the rest of the URL away
        /// as a fragment. All three are legal in a generated credential, and the user is then
        /// told "validation failed" about a key they pasted correctly. <c>FredProvider</c> was
        /// the only provider that escaped its key; the name is what
        /// <c>ApiKeyUrlEscapingTests</c> requires at every key-bearing query site.</para>
        /// </summary>
        private string KeyParam => Uri.EscapeDataString(_apiKey ?? string.Empty);

        private const string BaseUrl = "https://api.glassnode.com/v1/metrics";

        // Symbol → (asset, metric path) mapping. Each entry maps a public-facing symbol
        // to the Glassnode REST endpoint that backs it. Adding a new metric is one line
        // here — no other code changes needed.
        private static readonly Dictionary<string, (string Asset, string Path)> MetricMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "BTC_ACTIVE_ADDRS", ("BTC", "addresses/active_count")    },
            { "BTC_TX_COUNT",     ("BTC", "transactions/count")        },
            { "BTC_PRICE_USD",    ("BTC", "market/price_usd_close")    },
            { "BTC_MARKET_CAP",   ("BTC", "market/marketcap_usd")      },
            { "BTC_HASH_RATE",    ("BTC", "mining/hash_rate_mean")     },
            { "ETH_ACTIVE_ADDRS", ("ETH", "addresses/active_count")    },
            { "ETH_TX_COUNT",     ("ETH", "transactions/count")        },
        };

        public override string Name        => "Glassnode";
        public override string Description => "On-chain metrics for BTC and ETH (free tier; requires free API key).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => true;
        public override bool IsConfigured         => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels for the Price series FriendlyName on analytics tabs.
        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "BTC_ACTIVE_ADDRS" => "BTC Active Addresses",
            "BTC_TX_COUNT"     => "BTC Transaction Count",
            "BTC_PRICE_USD"    => "BTC Price (Glassnode)",
            "BTC_MARKET_CAP"   => "BTC Market Cap (Glassnode)",
            "BTC_HASH_RATE"    => "BTC Hash Rate",
            "ETH_ACTIVE_ADDRS" => "ETH Active Addresses",
            "ETH_TX_COUNT"     => "ETH Transaction Count",
            _                   => symbol
        };

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Free tier is daily-only. The endpoint accepts ?i=24h.
            StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "Glassnode API key not configured.");
            try
            {
                // Cheap probe: pull a single recent point of BTC active addresses.
                var url = $"{BaseUrl}/addresses/active_count?a=BTC&api_key={KeyParam}&s={DateTimeOffset.UtcNow.AddDays(-3).ToUnixTimeSeconds()}";
                var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode) return (true, "Glassnode API key validated.");
                return (false, $"Validation failed ({resp.StatusCode}). Free-tier metrics only.");
            }
            catch (Exception ex)
            {
                // Glassnode's api_key is a URL query param and HttpRequestException messages
                // carry the request URI, so ex.Message would read the key onto a channel
                // that is both spoken and logged. Same rule as TwelveData/FRED/FMP.
                return (false, $"Glassnode validation error: {ex.GetType().Name}");
            }
        }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(IsConfigured ? ConnectionState.Connected : ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(MetricMap.Keys.OrderBy(k => k).ToList());

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());

            var symbol = request.Symbol ?? string.Empty;
            if (!MetricMap.TryGetValue(symbol, out var entry))
            {
                _errorStream.OnNext($"Glassnode unknown metric symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    // Glassnode uses Unix seconds, not ms; the request supplies ms.
                    long? sinceSec = request.Since.HasValue ? request.Since.Value / 1000 : (long?)null;
                    long? untilSec = request.Until.HasValue ? request.Until.Value / 1000 : (long?)null;

                    var url = $"{BaseUrl}/{entry.Path}?a={entry.Asset}&i=24h&api_key={KeyParam}";
                    if (sinceSec.HasValue) url += $"&s={sinceSec.Value}";
                    if (untilSec.HasValue) url += $"&u={untilSec.Value}";

                    var json = await _http.GetStringAsync(url);
                    var arr  = JArray.Parse(json);

                    var bars = new List<Ohlcv>(arr.Count);
                    foreach (var pt in arr)
                    {
                        long ts  = pt["t"]?.Value<long>() ?? 0;
                        double v = pt["v"]?.Value<double?>() ?? double.NaN;
                        if (double.IsNaN(v)) continue;
                        // Publication-stamped: a whole-day statistic is not knowable until
                        // that day is over. See AnalyticsPublicationLag for why one bar of
                        // look-ahead is the whole edge for a mean-reversion gate.
                        var date = AnalyticsPublicationLag.ForWholeDayMetric(
                            DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime);
                        bars.Add(new Ohlcv(date, v, v, v, v, 0));
                    }

                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Glassnode fetch error ({symbol}): {ex.GetType().Name}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }
    }
}
