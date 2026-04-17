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

namespace AccessibleTrader.Plugins.CoinGecko
{
    /// <summary>
    /// CoinGecko market breadth provider — total crypto market cap, BTC dominance, ETH
    /// dominance, and per-coin historical market cap. Free public API, no key required.
    ///
    /// ── Why market breadth matters ──────────────────────────────────────────────
    /// "Breadth" measures how many assets in a market are participating in the move.
    /// A rally on shrinking BTC dominance + rising total cap = altseason regime where
    /// alts outperform. A drop on rising BTC dominance = flight-to-quality bearish
    /// regime. The Cipher framework cannot see breadth — it's strictly per-instrument.
    /// Adding a breadth gate to a long strategy filters out moves that look bullish on
    /// BTC alone but are actually broad-market weakness with BTC briefly catching a bid.
    ///
    /// ── Free metrics this provider exposes ──────────────────────────────────────
    ///   GLOBAL_TOTAL_CAP    — total crypto market cap USD (weeks of history)
    ///   GLOBAL_BTC_DOM      — BTC market cap as % of total
    ///   GLOBAL_ETH_DOM      — ETH market cap as % of total
    ///   BTC_MCAP            — BTC market cap (longer history via /coins/{id}/market_chart)
    ///   ETH_MCAP            — ETH market cap
    ///
    /// ── API ─────────────────────────────────────────────────────────────────────
    /// Public CoinGecko: https://api.coingecko.com/api/v3
    /// No auth required for public endpoints. Rate limit ~10-30 req/min on free tier
    /// (varies). We cap at 20/min to be polite.
    /// Sign up free for "Demo" key if you want higher limits — optional.
    /// </summary>
    public class CoinGeckoProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "CoinGecko",
            allowedHosts: new[] { "api.coingecko.com" });

        private readonly RateLimiter _rateLimiter = new(20, TimeSpan.FromMinutes(1));

        private const string BaseUrl = "https://api.coingecko.com/api/v3";

        // Per-coin market cap history symbols → CoinGecko coin ids.
        private static readonly Dictionary<string, string> CoinMcapMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "BTC_MCAP", "bitcoin" },
            { "ETH_MCAP", "ethereum" },
        };

        // Global breadth symbols handled separately because they hit different endpoints.
        private static readonly string[] GlobalSymbols =
        {
            "GLOBAL_TOTAL_CAP",
            "GLOBAL_BTC_DOM",
            "GLOBAL_ETH_DOM",
        };

        public override string Name        => "CoinGecko";
        public override string Description => "Crypto market breadth: total cap, BTC/ETH dominance, per-coin market cap (free, no key).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.OnChain };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 365;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels for the Price series FriendlyName on analytics tabs.
        // Global symbols get descriptive names; per-coin market cap symbols are
        // resolved via the CoinMcapMap keys and get a "... Market Cap" suffix.
        public override string GetSymbolDisplayName(string symbol)
        {
            return symbol switch
            {
                "GLOBAL_TOTAL_CAP" => "Total Crypto Market Cap",
                "GLOBAL_BTC_DOM"   => "BTC Dominance",
                "GLOBAL_ETH_DOM"   => "ETH Dominance",
                _ when CoinMcapMap.ContainsKey(symbol) =>
                    // Title-case the coin id ("bitcoin" → "Bitcoin") using a simple
                    // char-level transform to avoid pulling in System.Globalization.
                    $"{ToTitle(CoinMcapMap[symbol])} Market Cap",
                _ => symbol
            };
        }

        private static string ToTitle(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

        // ── Render hints for global breadth symbols ────────────────────────────────
        // Dominance metrics are bounded 0–100% with meaningful reference zones.
        // BTC dominance: ~45% historical mid, <40% = alt season, >55% = BTC-led.
        // ETH dominance: ~15% historical mid, <10% fade, >20% ETH rotation.
        // Market-cap symbols (BTC_MCAP etc.) are unbounded dollar values — no
        // range clamp, just leave as a plain line.
        public override SymbolRenderHints? GetSymbolRenderHints(string symbol)
        {
            return symbol switch
            {
                "GLOBAL_BTC_DOM" => new SymbolRenderHints(
                    RangeMin:       0,
                    RangeMax:       100,
                    DisplayType:    ComponentDisplayType.Oscillator,
                    SpeechTemplate: "{name}. {value:F2} percent.",
                    ColorHex:       "#F7931A",  // Bitcoin orange
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor("Alt Season", 40, "#26A69A", DashStyle.Dash),
                        new LevelDescriptor("BTC Dominance Mid", 50, "#888888", DashStyle.Dot),
                        new LevelDescriptor("BTC-Led", 60, "#EF5350", DashStyle.Dash),
                    }),
                "GLOBAL_ETH_DOM" => new SymbolRenderHints(
                    RangeMin:       0,
                    RangeMax:       30,   // ETH dominance rarely exceeds 25%
                    DisplayType:    ComponentDisplayType.Oscillator,
                    SpeechTemplate: "{name}. {value:F2} percent.",
                    ColorHex:       "#627EEA",  // Ethereum blue
                    ReferenceLevels: new[]
                    {
                        new LevelDescriptor("ETH Fade", 10, "#26A69A", DashStyle.Dash),
                        new LevelDescriptor("ETH Dominance Mid", 15, "#888888", DashStyle.Dot),
                        new LevelDescriptor("ETH Rotation", 20, "#EF5350", DashStyle.Dash),
                    }),
                _ => null  // Per-coin market cap: unbounded dollar value, plain line is fine.
            };
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            // CoinGecko historical market_chart returns daily data when the range
            // exceeds 90 days, hourly inside 90, finer inside 1 day. Use daily as
            // the canonical resolution for strategy use.
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
            var all = new List<string>();
            all.AddRange(GlobalSymbols);
            all.AddRange(CoinMcapMap.Keys);
            return Task.FromResult(all);
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(NativelySupportedTimeframes);

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var symbol = request.Symbol ?? string.Empty;
            try
            {
                if (Array.Exists(GlobalSymbols, s => s.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
                    return await FetchGlobalAsync(symbol);

                if (CoinMcapMap.TryGetValue(symbol, out var coinId))
                    return await FetchCoinMcapAsync(coinId, request);

                _errorStream.OnNext($"CoinGecko unknown symbol: {symbol}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"CoinGecko fetch error ({symbol}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        /// <summary>
        /// Fetches the current global breadth snapshot from /global. CoinGecko's free API
        /// only exposes the LATEST global state — there's no public historical endpoint
        /// for total cap or dominance. We return a single-point series here so charts can
        /// at least display the current value; users who want historical breadth should
        /// run this provider on a schedule and accumulate snapshots in a separate store
        /// (a future enhancement).
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchGlobalAsync(string symbol)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var json = await _http.GetStringAsync($"{BaseUrl}/global");
                var data = JObject.Parse(json)["data"] as JObject;
                if (data == null) return (new List<Ohlcv>(), new List<(long, double)>());

                double value = symbol.ToUpperInvariant() switch
                {
                    "GLOBAL_TOTAL_CAP" => data["total_market_cap"]?["usd"]?.Value<double>() ?? 0,
                    "GLOBAL_BTC_DOM"   => data["market_cap_percentage"]?["btc"]?.Value<double>() ?? 0,
                    "GLOBAL_ETH_DOM"   => data["market_cap_percentage"]?["eth"]?.Value<double>() ?? 0,
                    _ => 0
                };

                var date = DateTime.UtcNow;
                var bar  = new Ohlcv(date, value, value, value, value, 0);
                return (new List<Ohlcv> { bar }, new List<(long, double)> { (new DateTimeOffset(date).ToUnixTimeMilliseconds(), 0) });
            });
        }

        /// <summary>
        /// Fetches per-coin historical market cap. CoinGecko exposes
        /// /coins/{id}/market_chart?vs_currency=usd&amp;days=N which returns price + market_cap +
        /// volume arrays. We pull market_cap and emit it as O=H=L=C.
        /// Daily resolution kicks in automatically when days &gt; 90.
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchCoinMcapAsync(string coinId, MarketDataRequest request)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                // Compute days from since/until or default to 365.
                int days = 365;
                if (request.Since.HasValue)
                {
                    var sinceDt = DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value);
                    var untilDt = request.Until.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value)
                        : DateTimeOffset.UtcNow;
                    days = Math.Max(1, (int)Math.Ceiling((untilDt - sinceDt).TotalDays));
                    days = Math.Min(days, 365); // free tier capped
                }

                var url = $"{BaseUrl}/coins/{coinId}/market_chart?vs_currency=usd&days={days}&interval=daily";
                var json = await _http.GetStringAsync(url);
                var root = JObject.Parse(json);
                var caps = root["market_caps"] as JArray;
                if (caps == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var bars = new List<Ohlcv>(caps.Count);
                foreach (var pt in caps)
                {
                    if (pt is not JArray a || a.Count < 2) continue;
                    long ts = a[0].Value<long>();
                    double v = a[1].Value<double>();
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    bars.Add(new Ohlcv(date, v, v, v, v, 0));
                }

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            });
        }
    }
}
