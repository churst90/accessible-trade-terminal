using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.DefiLlama
{
    /// <summary>
    /// DeFi TVL, stablecoin supply, and DEX volume data from the free DefiLlama API.
    ///
    /// ── Why this matters for strategies ─────────────────────────────────────────
    /// Total Value Locked is the single best proxy for capital commitment in DeFi.
    /// Rising TVL on a chain signals genuine usage growth (not just price pumps),
    /// while declining TVL warns of capital flight — often a leading indicator of
    /// token weakness. Stablecoin supply is the "dry powder" metric: growing supply
    /// means more fiat-equivalent capital sitting on-chain ready to deploy.
    ///
    /// Cross-referencing chain TVL trends with token price gives strategies an edge:
    ///   • TVL rising + price flat = accumulation phase, lean long
    ///   • TVL falling + price rising = unsustainable pump, lean short / reduce
    ///   • Stablecoin supply expanding = macro risk-on for crypto
    ///
    /// ── Endpoints ───────────────────────────────────────────────────────────────
    /// TVL:          https://api.llama.fi/v2/historicalChainTvl/{chain}
    /// Total TVL:    https://api.llama.fi/v2/historicalChainTvl
    /// Protocol TVL: https://api.llama.fi/protocol/{slug}
    /// Stablecoins:  https://stablecoins.llama.fi/stablecoincharts/all?stablecoin={id}
    /// Total stable: https://stablecoins.llama.fi/stablecoincharts/all
    ///
    /// No API key, no auth. Rate limit: treat as ~30/min to be polite.
    /// </summary>
    public class DefiLlamaProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = new();
        private readonly RateLimiter _rateLimiter = new(30, TimeSpan.FromMinutes(1));

        private const string TvlBase = "https://api.llama.fi";
        private const string StableBase = "https://stablecoins.llama.fi";

        // ── Chain TVL symbols → URL chain name ──────────────────────────────────
        private static readonly Dictionary<string, string> ChainTvlMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ETHEREUM_TVL"]  = "Ethereum",
            ["BSC_TVL"]       = "BSC",
            ["SOLANA_TVL"]    = "Solana",
            ["ARBITRUM_TVL"]  = "Arbitrum",
            ["POLYGON_TVL"]   = "Polygon",
            ["AVALANCHE_TVL"] = "Avalanche",
            ["BASE_TVL"]      = "Base",
            ["OPTIMISM_TVL"]  = "Optimism",
            ["TRON_TVL"]      = "Tron",
            ["BITCOIN_TVL"]   = "Bitcoin",
        };

        // ── Protocol TVL symbols → API slug ─────────────────────────────────────
        private static readonly Dictionary<string, string> ProtocolTvlMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["LIDO_TVL"]        = "lido",
            ["AAVE_TVL"]        = "aave",
            ["EIGENLAYER_TVL"]  = "eigenlayer",
            ["ETHENA_TVL"]      = "ethena",
            ["MAKER_TVL"]       = "makerdao",
            ["UNISWAP_TVL"]     = "uniswap",
            ["PENDLE_TVL"]      = "pendle",
            ["ROCKET_POOL_TVL"] = "rocket-pool",
        };

        // ── Stablecoin symbols → DefiLlama stablecoin id ────────────────────────
        private static readonly Dictionary<string, int> StablecoinMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USDT_SUPPLY"] = 1,
            ["USDC_SUPPLY"] = 2,
            ["DAI_SUPPLY"]  = 5,
        };

        private const string TotalTvlSymbol        = "TOTAL_TVL";
        private const string TotalStablecoinSymbol  = "TOTAL_STABLECOIN_SUPPLY";

        public override string Name        => "DefiLlama";
        public override string Description => "DefiLlama — DeFi TVL by chain/protocol, stablecoin supply, DEX volumes";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol) => symbol?.ToUpperInvariant() switch
        {
            "ETHEREUM_TVL"           => "Ethereum Total Value Locked",
            "BSC_TVL"                => "BSC Total Value Locked",
            "SOLANA_TVL"             => "Solana Total Value Locked",
            "ARBITRUM_TVL"           => "Arbitrum Total Value Locked",
            "POLYGON_TVL"            => "Polygon Total Value Locked",
            "AVALANCHE_TVL"          => "Avalanche Total Value Locked",
            "BASE_TVL"               => "Base Total Value Locked",
            "OPTIMISM_TVL"           => "Optimism Total Value Locked",
            "TRON_TVL"               => "Tron Total Value Locked",
            "BITCOIN_TVL"            => "Bitcoin Total Value Locked",
            "TOTAL_TVL"              => "Total DeFi TVL",
            "LIDO_TVL"               => "Lido Total Value Locked",
            "AAVE_TVL"               => "Aave Total Value Locked",
            "EIGENLAYER_TVL"         => "EigenLayer Total Value Locked",
            "ETHENA_TVL"             => "Ethena Total Value Locked",
            "MAKER_TVL"              => "Maker Total Value Locked",
            "UNISWAP_TVL"            => "Uniswap Total Value Locked",
            "PENDLE_TVL"             => "Pendle Total Value Locked",
            "ROCKET_POOL_TVL"        => "Rocket Pool Total Value Locked",
            "USDT_SUPPLY"            => "USDT Circulating Supply",
            "USDC_SUPPLY"            => "USDC Circulating Supply",
            "DAI_SUPPLY"             => "DAI Circulating Supply",
            "TOTAL_STABLECOIN_SUPPLY"=> "Total Stablecoin Supply",
            _                        => symbol ?? string.Empty
        };

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;

        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var upper = (subType ?? string.Empty).ToUpperInvariant();
            if (upper == "STABLECOINS")
            {
                var list = StablecoinMap.Keys.ToList();
                list.Add(TotalStablecoinSymbol);
                list.Sort();
                return Task.FromResult(list);
            }

            // Default: TVL sub-type — chains + protocols + total
            var tvl = new List<string>();
            tvl.Add(TotalTvlSymbol);
            tvl.AddRange(ChainTvlMap.Keys.OrderBy(k => k));
            tvl.AddRange(ProtocolTvlMap.Keys.OrderBy(k => k));
            return Task.FromResult(tvl);
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "TVL", "Stablecoins" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var symbol = (request.Symbol ?? string.Empty).ToUpperInvariant();

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    List<Ohlcv> bars;

                    if (symbol == TotalTvlSymbol)
                        bars = await FetchChainTvlAsync(null).ConfigureAwait(false);
                    else if (ChainTvlMap.TryGetValue(symbol, out var chain))
                        bars = await FetchChainTvlAsync(chain).ConfigureAwait(false);
                    else if (ProtocolTvlMap.TryGetValue(symbol, out var slug))
                        bars = await FetchProtocolTvlAsync(slug).ConfigureAwait(false);
                    else if (StablecoinMap.TryGetValue(symbol, out var stableId))
                        bars = await FetchStablecoinAsync(stableId).ConfigureAwait(false);
                    else if (symbol == TotalStablecoinSymbol)
                        bars = await FetchTotalStablecoinAsync().ConfigureAwait(false);
                    else
                    {
                        _errorStream.OnNext($"DefiLlama unknown symbol: {symbol}");
                        return (new List<Ohlcv>(), new List<(long, double)>());
                    }

                    // Filter by Since/Until (request uses milliseconds)
                    if (request.Since.HasValue)
                    {
                        var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date >= sinceDt).ToList();
                    }
                    if (request.Until.HasValue)
                    {
                        var untilDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime;
                        bars = bars.Where(b => b.Date <= untilDt).ToList();
                    }

                    var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                    return (bars, vols);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"DefiLlama fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // ── Chain TVL ───────────────────────────────────────────────────────────
        // GET /v2/historicalChainTvl/{chain} → [{"date":1506470400,"tvl":0.0}, ...]
        // GET /v2/historicalChainTvl          → total DeFi TVL (no chain param)
        private async Task<List<Ohlcv>> FetchChainTvlAsync(string? chain)
        {
            var url = chain == null
                ? $"{TvlBase}/v2/historicalChainTvl"
                : $"{TvlBase}/v2/historicalChainTvl/{chain}";

            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(json);

            var bars = new List<Ohlcv>(arr.Count);
            foreach (var item in arr)
            {
                long dateSec = item["date"]?.Value<long>() ?? 0;
                double tvl = item["tvl"]?.Value<double>() ?? 0;
                var dt = DateTimeOffset.FromUnixTimeSeconds(dateSec).UtcDateTime;
                bars.Add(new Ohlcv(dt, tvl, tvl, tvl, tvl, 0));
            }

            return bars;
        }

        // ── Protocol TVL ────────────────────────────────────────────────────────
        // GET /protocol/{slug} → object with "tvl" array:
        //   [{"date":1606780800,"totalLiquidityUSD":53.37}, ...]
        private async Task<List<Ohlcv>> FetchProtocolTvlAsync(string slug)
        {
            var url = $"{TvlBase}/protocol/{slug}";
            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var tvlArr = root["tvl"] as JArray;
            if (tvlArr == null) return new List<Ohlcv>();

            var bars = new List<Ohlcv>(tvlArr.Count);
            foreach (var item in tvlArr)
            {
                long dateSec = item["date"]?.Value<long>() ?? 0;
                double tvl = item["totalLiquidityUSD"]?.Value<double>() ?? 0;
                var dt = DateTimeOffset.FromUnixTimeSeconds(dateSec).UtcDateTime;
                bars.Add(new Ohlcv(dt, tvl, tvl, tvl, tvl, 0));
            }

            return bars;
        }

        // ── Individual stablecoin supply ────────────────────────────────────────
        // GET /stablecoincharts/all?stablecoin={id} on stablecoins.llama.fi
        //   [{"date":1606780800,"totalCirculating":{"peggedUSD":19400000000},...}, ...]
        private async Task<List<Ohlcv>> FetchStablecoinAsync(int stablecoinId)
        {
            var url = $"{StableBase}/stablecoincharts/all?stablecoin={stablecoinId}";
            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(json);

            var bars = new List<Ohlcv>(arr.Count);
            foreach (var item in arr)
            {
                long dateSec = item["date"]?.Value<long>() ?? 0;
                double supply = item["totalCirculating"]?["peggedUSD"]?.Value<double>() ?? 0;
                var dt = DateTimeOffset.FromUnixTimeSeconds(dateSec).UtcDateTime;
                bars.Add(new Ohlcv(dt, supply, supply, supply, supply, 0));
            }

            return bars;
        }

        // ── Total stablecoin supply ─────────────────────────────────────────────
        // GET /stablecoincharts/all → total across all stablecoins
        private async Task<List<Ohlcv>> FetchTotalStablecoinAsync()
        {
            var url = $"{StableBase}/stablecoincharts/all";
            var json = await _http.GetStringAsync(url).ConfigureAwait(false);
            var arr = JArray.Parse(json);

            var bars = new List<Ohlcv>(arr.Count);
            foreach (var item in arr)
            {
                long dateSec = item["date"]?.Value<long>() ?? 0;
                double supply = item["totalCirculating"]?["peggedUSD"]?.Value<double>() ?? 0;
                var dt = DateTimeOffset.FromUnixTimeSeconds(dateSec).UtcDateTime;
                bars.Add(new Ohlcv(dt, supply, supply, supply, supply, 0));
            }

            return bars;
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
