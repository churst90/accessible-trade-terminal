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

namespace AccessibleTrader.Plugins.Mempool
{
    /// <summary>
    /// Mempool.space Bitcoin mining and mempool metrics. Provides daily-resolution data for
    /// hash rate, difficulty, block fees, block rewards, block sizes, and fee rates from the
    /// fully open mempool.space REST API (no key, no signup, no cost).
    ///
    /// -- Why this matters for strategies ------------------------------------------------
    /// Mining metrics are structural signals that cannot be inferred from price action:
    ///
    ///   - HASHRATE + DIFFICULTY rising = miner conviction increasing, often leads price.
    ///   - BLOCK_FEES spiking = mempool congestion / demand for block space, can indicate
    ///     on-chain urgency (large moves, exchange withdrawals).
    ///   - BLOCK_FEE_RATES trending up = sustained demand, not just single-block spikes.
    ///   - BLOCK_REWARDS declining (halvings) = supply squeeze thesis input.
    ///
    /// -- Endpoint summary ---------------------------------------------------------------
    /// Base: https://mempool.space
    ///   GET /api/v1/mining/hashrate/{period}       -> hashrates[] + difficulty[]
    ///   GET /api/v1/mining/blocks/fees/{period}     -> avgFees per block
    ///   GET /api/v1/mining/blocks/rewards/{period}  -> avgReward per block
    ///   GET /api/v1/mining/blocks/sizes-weights/{period} -> avgSize per block
    ///   GET /api/v1/mining/blocks/fee-rates/{period}     -> median fee rate
    ///
    /// All endpoints are unauthenticated. Rate limit: be polite (~10 req/min).
    /// </summary>
    public class MempoolProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = new();
        private readonly RateLimiter _rateLimiter = new(10, TimeSpan.FromMinutes(1));

        private const string BaseUrl = "https://mempool.space";

        // ── Symbol registry ─────────────────────────────────────────────────────────
        // Each symbol maps to: (endpoint path template, JSON value field, array key).
        // The {0} placeholder is replaced with the time period string (1m/3m/6m/1y/2y/3y).
        // ArrayKey is the top-level JSON property that holds the data array; null means
        // the response itself is a JSON array.
        private static readonly Dictionary<string, (string PathTemplate, string ValueField, string? ArrayKey)> SymbolMap
            = new(StringComparer.OrdinalIgnoreCase)
        {
            { "HASHRATE",        ("/api/v1/mining/hashrate/{0}",              "avgHashrate", "hashrates")  },
            { "DIFFICULTY",      ("/api/v1/mining/hashrate/{0}",              "difficulty",  "difficulty") },
            { "BLOCK_FEES",      ("/api/v1/mining/blocks/fees/{0}",           "avgFees",     null)         },
            { "BLOCK_REWARDS",   ("/api/v1/mining/blocks/rewards/{0}",        "avgReward",   null)         },
            { "BLOCK_SIZES",     ("/api/v1/mining/blocks/sizes-weights/{0}",  "avgSize",     null)         },
            { "BLOCK_FEE_RATES", ("/api/v1/mining/blocks/fee-rates/{0}",      "avgFee_50",   null)         },
        };

        // ── Provider metadata ────────────────────────────────────────────────────────

        public override string Name        => "Mempool";
        public override string Description => "Mempool.space \u2014 Bitcoin mempool stats, fees, hashrate, difficulty, mining pools";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 5000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // ── Display names ────────────────────────────────────────────────────────────

        public override string GetSymbolDisplayName(string symbol) => symbol switch
        {
            "HASHRATE"        => "Bitcoin Hash Rate",
            "DIFFICULTY"      => "Bitcoin Mining Difficulty",
            "BLOCK_FEES"      => "Average Block Fees (sats)",
            "BLOCK_REWARDS"   => "Average Block Rewards (sats)",
            "BLOCK_SIZES"     => "Average Block Size (bytes)",
            "BLOCK_FEE_RATES" => "Median Fee Rate (sat/vB)",
            _                 => symbol
        };

        // ── Render hints ─────────────────────────────────────────────────────────────
        // All mining metrics are non-negative. We set RangeMin=0 so the chart anchors at
        // zero rather than auto-scaling to the data minimum (which would exaggerate small
        // swings). No upper bound — these are unbounded metrics.

        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            if (!SymbolMap.ContainsKey(symbol)) return null;
            return new SymbolRenderHints(RangeMin: 0);
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneDay
        };

        // ── Configuration / connection ───────────────────────────────────────────────

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

        // ── Symbol / timeframe discovery ─────────────────────────────────────────────

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(SymbolMap.Keys.OrderBy(k => k).ToList());

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        // ── Data fetch ───────────────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var symbol = request.Symbol ?? string.Empty;
            if (!SymbolMap.TryGetValue(symbol, out var meta))
            {
                _errorStream.OnNext($"Mempool unknown symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var period = GetTimePeriod(request.Limit);
                    var path = string.Format(meta.PathTemplate, period);
                    var url = $"{BaseUrl}{path}";

                    var json = await _http.GetStringAsync(url).ConfigureAwait(false);

                    JArray dataArr;
                    if (meta.ArrayKey != null)
                    {
                        var root = JObject.Parse(json);
                        dataArr = root[meta.ArrayKey] as JArray ?? new JArray();
                    }
                    else
                    {
                        dataArr = JArray.Parse(json);
                    }

                    var bars = new List<Ohlcv>(dataArr.Count);
                    foreach (var entry in dataArr)
                    {
                        long ts = entry["timestamp"]?.Value<long>() ?? 0;
                        double val = entry[meta.ValueField]?.Value<double?>() ?? double.NaN;
                        if (double.IsNaN(val)) continue;

                        var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime;
                        bars.Add(new Ohlcv(date, val, val, val, val, 0));
                    }

                    // Ensure chronological order (older -> newer)
                    bars.Sort((a, b) => a.Date.CompareTo(b.Date));

                    // Apply date filters from request
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
                _errorStream.OnNext($"Mempool fetch error ({symbol}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maps the requested bar limit to the closest mempool.space time period parameter.
        /// Smaller periods return fewer data points but respond faster.
        /// </summary>
        private static string GetTimePeriod(int limit)
        {
            if (limit <= 30)  return "1m";
            if (limit <= 90)  return "3m";
            if (limit <= 180) return "6m";
            if (limit <= 365) return "1y";
            if (limit <= 730) return "2y";
            return "3y";
        }

        // ── Disposal ─────────────────────────────────────────────────────────────────

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
