using System.Globalization;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Etherscan
{
    /// <summary>
    /// Etherscan provider — ETH gas prices, supply, and on-chain data from the Etherscan
    /// API v2. The free tier provides current-snapshot endpoints for gas oracle, supply
    /// stats, price, and node count. These are not historical time-series; each call
    /// returns the latest value only. FetchOhlcvAsync returns a single data point stamped at
    /// the moment it was read, so the metric can be used as a "current reading" overlay on
    /// charts, and data accumulates over time as the app polls periodically. A request for a
    /// window that has already closed is REFUSED with a spoken explanation rather than
    /// answered with today's reading wearing that window's date.
    ///
    /// ── Available symbols ───────────────────────────────────────────────────────
    ///   ETH_GAS_SAFE     — Gas Oracle safe gas price (Gwei)
    ///   ETH_GAS_FAST     — Gas Oracle fast gas price (Gwei)
    ///   ETH_GAS_PROPOSE  — Gas Oracle proposed gas price (Gwei)
    ///   ETH_SUPPLY       — Total ETH supply (ETH, converted from wei)
    ///   ETH_SUPPLY2      — ETH2 supply breakdown (ETH, converted from wei)
    ///   ETH_PRICE        — Current ETH price in USD
    ///   ETH_NODE_COUNT   — Total Ethereum node count
    ///
    /// ── API ─────────────────────────────────────────────────────────────────────
    /// Base URL: https://api.etherscan.io/v2/api
    /// Auth: apikey query parameter
    /// Chain: chainid=1 (Ethereum mainnet)
    /// Free tier: 5 calls/sec, no credit card required
    /// Sign up free: https://etherscan.io
    ///
    /// Configure via Settings → API Keys → Etherscan → paste your free key.
    /// </summary>
    public class EtherscanProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "Etherscan",
            allowedHosts: new[] { "api.etherscan.io" });

        private readonly RateLimiter _rateLimiter = new(5, TimeSpan.FromSeconds(1));
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

        private const string BaseUrl = "https://api.etherscan.io/v2/api";

        // All known symbols this provider exposes.
        private static readonly List<string> Symbols = new()
        {
            "ETH_GAS_SAFE",
            "ETH_GAS_FAST",
            "ETH_GAS_PROPOSE",
            "ETH_SUPPLY",
            "ETH_SUPPLY2",
            "ETH_PRICE",
            "ETH_NODE_COUNT"
        };

        public override string Name        => "Etherscan";
        public override string Description => "Etherscan \u2014 ETH gas prices, supply, and on-chain data";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => true;
        public override bool IsConfigured         => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 1;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "ETH_GAS_SAFE"    => "ETH Gas Price (Safe)",
            "ETH_GAS_FAST"    => "ETH Gas Price (Fast)",
            "ETH_GAS_PROPOSE" => "ETH Gas Price (Proposed)",
            "ETH_SUPPLY"      => "ETH Total Supply",
            "ETH_SUPPLY2"     => "ETH2 Supply Breakdown",
            "ETH_PRICE"       => "ETH Price (USD)",
            "ETH_NODE_COUNT"  => "ETH Network Node Count",
            _                  => symbol
        };

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol) => symbol switch
        {
            "ETH_GAS_SAFE"    => new SymbolRenderHints(RangeMin: 0),
            "ETH_GAS_FAST"    => new SymbolRenderHints(RangeMin: 0),
            "ETH_GAS_PROPOSE" => new SymbolRenderHints(RangeMin: 0),
            "ETH_SUPPLY"      => new SymbolRenderHints(RangeMin: 0),
            "ETH_SUPPLY2"     => new SymbolRenderHints(RangeMin: 0),
            "ETH_PRICE"       => new SymbolRenderHints(RangeMin: 0),
            "ETH_NODE_COUNT"  => new SymbolRenderHints(RangeMin: 0),
            _                  => null
        };

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "Etherscan API key not configured.");
            try
            {
                var url = $"{BaseUrl}?chainid=1&module=stats&action=ethprice&apikey={KeyParam}";
                var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                var obj = JObject.Parse(json);
                var status = obj["status"]?.ToString();
                if (status == "1")
                    return (true, "Etherscan API key validated.");
                var msg = obj["message"]?.ToString() ?? "Unknown error";
                return (false, $"Etherscan validation failed: {msg}");
            }
            catch (Exception ex)
            {
                // Etherscan's apikey is a URL query param and HttpRequestException messages
                // carry the request URI, so ex.Message would read the key onto a channel
                // that is both spoken and logged. Same rule as TwelveData/FRED/FMP.
                return (false, $"Etherscan validation error: {ex.GetType().Name}");
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
            => Task.FromResult(Symbols.ToList());

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
            if (!Symbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
            {
                _errorStream.OnNext($"Etherscan unknown symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    // A SNAPSHOT CANNOT ANSWER A HISTORICAL WINDOW — say so instead of
                    // returning a point that has nothing to do with what was asked for.
                    //
                    // Every endpoint below is current-value-only on the free tier. This wrapped
                    // the scalar in one bar stamped DateTime.UtcNow.Date and returned it
                    // whatever the request asked for, which had two consequences. The chart
                    // showed a single dot for a five-year window with nothing spoken to explain
                    // it — for a user who cannot see the axis, one dot and a real series look
                    // the same until the numbers are read. And DataService's analytics disk
                    // cache keys by Since/Until (DataService.AnalyticsCacheKey), so every
                    // distinct historical window got its own cache entry holding today's
                    // reading, stamped with a date inside a window it was never from.
                    if (request.Until.HasValue &&
                        DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime < DateTime.UtcNow.AddMinutes(-1))
                    {
                        _errorStream.OnNext(
                            $"Etherscan {symbol} is a current reading, not a history — it cannot fill a past date range.");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    double value = await FetchCurrentValueAsync(symbol).ConfigureAwait(false);
                    if (double.IsNaN(value))
                        return (new List<Ohlcv>(), new List<(long, double)>());

                    // Stamped WHEN IT WAS READ, not at today's midnight. Midnight is a lie in
                    // the direction that matters: CrossSeriesForwardFill admits ties
                    // (`ticks[i].Ts <= barTs`), so a gas price read at noon was visible to an
                    // indicator sitting on today's 00:00 bar — twelve hours of look-ahead in a
                    // series whose whole use is as a live overlay.
                    var now = DateTime.UtcNow;
                    var bar = new Ohlcv(now, value, value, value, value, 0);
                    var bars = new List<Ohlcv> { bar };
                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Etherscan fetch error ({symbol}): {ex.GetType().Name}");
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        /// <summary>
        /// Fetches the current snapshot value for the given symbol from the appropriate
        /// Etherscan API endpoint.
        /// </summary>
        private async Task<double> FetchCurrentValueAsync(string symbol)
        {
            string url;
            switch (symbol.ToUpperInvariant())
            {
                case "ETH_GAS_SAFE":
                case "ETH_GAS_FAST":
                case "ETH_GAS_PROPOSE":
                    url = $"{BaseUrl}?chainid=1&module=gastracker&action=gasoracle&apikey={KeyParam}";
                    break;
                case "ETH_SUPPLY":
                    url = $"{BaseUrl}?chainid=1&module=stats&action=ethsupply&apikey={KeyParam}";
                    break;
                case "ETH_SUPPLY2":
                    url = $"{BaseUrl}?chainid=1&module=stats&action=ethsupply2&apikey={KeyParam}";
                    break;
                case "ETH_PRICE":
                    url = $"{BaseUrl}?chainid=1&module=stats&action=ethprice&apikey={KeyParam}";
                    break;
                case "ETH_NODE_COUNT":
                    url = $"{BaseUrl}?chainid=1&module=stats&action=nodecount&apikey={KeyParam}";
                    break;
                default:
                    return double.NaN;
            }

            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var obj = JObject.Parse(json);

            if (obj["status"]?.ToString() != "1")
            {
                _errorStream.OnNext($"Etherscan API error for {symbol}: {obj["message"]}");
                return double.NaN;
            }

            var result = obj["result"];
            if (result == null) return double.NaN;

            return symbol.ToUpperInvariant() switch
            {
                "ETH_GAS_SAFE"    => ParseDouble(result["SafeGasPrice"]?.ToString()),
                "ETH_GAS_FAST"    => ParseDouble(result["FastGasPrice"]?.ToString()),
                "ETH_GAS_PROPOSE" => ParseDouble(result["ProposeGasPrice"]?.ToString()),
                "ETH_SUPPLY"      => ParseWei(result.ToString()),
                "ETH_SUPPLY2"     => ParseWei(result["EthSupply"]?.ToString()),
                "ETH_PRICE"       => ParseDouble(result["ethusd"]?.ToString()),
                "ETH_NODE_COUNT"  => ParseDouble(result["TotalNodeCount"]?.ToString()),
                _                  => double.NaN
            };
        }

        /// <summary>Parses a numeric string to double, returning NaN on failure.</summary>
        private static double ParseDouble(string? s)
            => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;

        /// <summary>Parses a wei string (integer) and converts to ETH by dividing by 1e18.</summary>
        private static double ParseWei(string? s)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var wei)
                ? wei / 1e18
                : double.NaN;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
