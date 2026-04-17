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

namespace AccessibleTrader.Plugins.BinanceDerivatives
{
    /// <summary>
    /// Binance USD-M Futures derivatives metadata: funding rates and open interest history.
    /// These are NOT tradeable instruments — they're analytical series that expose how
    /// derivatives traders are positioned, which is one of the most informative orthogonal
    /// signals available to a strategy that's mostly built on price.
    ///
    /// Lives under the Analytics terminal mode in the "Derivatives" market category. No
    /// API key required. Free public REST endpoints with generous rate limits (2400 req/min).
    ///
    /// ── Symbol convention ──────────────────────────────────────────────────────
    /// To distinguish funding-rate series from open-interest series and from spot/futures
    /// price series, this provider returns "metric symbols" that fold both the underlying
    /// instrument and the metric into one symbol string:
    ///
    ///     BTCUSDT_FUNDING — historical 8-hour funding rate, percent (e.g. 0.0001 = 0.01%)
    ///     ETHUSDT_FUNDING
    ///     SOLUSDT_FUNDING
    ///     BTCUSDT_OI      — historical open interest in USD value
    ///     ETHUSDT_OI
    ///     SOLUSDT_OI
    ///
    /// Each metric is returned as OHLCV with O=H=L=C=value, matching the FRED/single-value
    /// convention the rest of the application already understands. Volume is left at zero.
    ///
    /// ── Sampling frequency ─────────────────────────────────────────────────────
    /// Funding rates are settled every 8 hours by Binance. Open interest history is
    /// returned at user-selected intervals (5m / 15m / 30m / 1h / 2h / 4h / 6h / 12h / 1d).
    /// The natively supported timeframes list reflects this — requesting "1m" will return
    /// nothing useful for funding (8h granularity), but works for OI.
    ///
    /// Strategy use:
    ///   • Funding extreme positive (e.g. >0.05%/8h) + Cipher oversold = "longs are paying
    ///     through the nose to hold positions, the unwind is overdue, this is the buy."
    ///   • OI rising sharply + price falling = new shorts piling in (continuation).
    ///   • OI dropping sharply + price falling = shorts covering (often near reversal).
    /// </summary>
    public class BinanceDerivativesProvider : BaseMarketDataProvider
    {
        // Host-provided HttpClient: 32 MB / 60 s + outbound allow-list.
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "BinanceDerivatives",
            allowedHosts: new[] { "fapi.binance.com" });

        // Binance public limits are 2400 req/min on the futures REST API; we cap well below.
        private readonly RateLimiter _rateLimiter = new(1200, TimeSpan.FromMinutes(1));

        // Default symbol catalogue. Users can request any USD-M perpetual symbol by
        // adding _FUNDING / _OI suffix; this list seeds the dropdown for discovery.
        private static readonly string[] CoreSymbols = { "BTCUSDT", "ETHUSDT", "SOLUSDT", "BNBUSDT", "XRPUSDT" };

        private const string FundingSuffix = "_FUNDING";
        private const string OiSuffix      = "_OI";

        private const string FapiBase = "https://fapi.binance.com";

        public override string Name        => "BinanceDerivatives";
        public override string Description => "Binance USD-M perpetual funding rates and open interest history (analytics).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 500;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        // Human-readable labels for the Price series FriendlyName. Symbols are
        // encoded as "<PAIR>_FUNDING" or "<PAIR>_OI" where PAIR is e.g. "BTCUSDT".
        public override string GetSymbolDisplayName(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return symbol;

            string metric = "";
            string pair = symbol;
            if (symbol.EndsWith(FundingSuffix, StringComparison.Ordinal))
            {
                metric = "Funding Rate";
                pair = symbol[..^FundingSuffix.Length];
            }
            else if (symbol.EndsWith(OiSuffix, StringComparison.Ordinal))
            {
                metric = "Open Interest";
                pair = symbol[..^OiSuffix.Length];
            }
            // Insert the "/" between base and quote for the common USDT/USDC/BUSD pairs.
            foreach (var quote in new[] { "USDT", "USDC", "BUSD", "BTC", "ETH" })
            {
                if (pair.EndsWith(quote, StringComparison.Ordinal) && pair.Length > quote.Length)
                {
                    pair = $"{pair[..^quote.Length]}/{quote}";
                    break;
                }
            }
            return string.IsNullOrEmpty(metric) ? pair : $"{pair} {metric}";
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            // Funding settles every 8h on Binance. Anything finer than 8h is the same value
            // repeated; anything coarser than 8h aggregates several settlements.
            // For OI, Binance offers 5m / 15m / 30m / 1h / 2h / 4h / 6h / 12h / 1d.
            // We expose the union; the FetchOhlcvAsync path picks the right native call.
            StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes,
            StandardTimeframes.OneHour,
            StandardTimeframes.FourHours,
            StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config)
        {
            // Public endpoints — nothing to configure.
        }

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

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Standard" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            // Seed each core symbol with both metrics. Users see e.g. BTCUSDT_FUNDING +
            // BTCUSDT_OI side-by-side in the symbol dropdown.
            var symbols = new List<string>(CoreSymbols.Length * 2);
            foreach (var s in CoreSymbols)
            {
                symbols.Add(s + FundingSuffix);
                symbols.Add(s + OiSuffix);
            }
            return Task.FromResult(symbols);
        }

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            string symbol = request.Symbol ?? string.Empty;
            try
            {
                if (symbol.EndsWith(FundingSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var underlying = symbol.Substring(0, symbol.Length - FundingSuffix.Length);
                    return await FetchFundingHistoryAsync(underlying, request);
                }
                if (symbol.EndsWith(OiSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var underlying = symbol.Substring(0, symbol.Length - OiSuffix.Length);
                    return await FetchOpenInterestHistoryAsync(underlying, request);
                }

                // Unknown suffix → return empty rather than error so the UI doesn't crash on
                // an unrecognised symbol; the user sees an empty chart and can pick a valid one.
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"BinanceDerivatives fetch error ({symbol}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        /// <summary>
        /// Fetches historical funding rate. Endpoint:
        ///   GET /fapi/v1/fundingRate?symbol=BTCUSDT&amp;startTime=...&amp;endTime=...&amp;limit=1000
        ///
        /// Returns one entry per 8-hour funding period. Each entry is mapped to an Ohlcv
        /// where O=H=L=C is the funding rate as a fraction (0.0001 = 0.01%, "0.01% per 8h").
        /// The strategy system reads these values via the standard component data path —
        /// no custom branch needed.
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchFundingHistoryAsync(string underlying, MarketDataRequest request)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var url = $"{FapiBase}/fapi/v1/fundingRate?symbol={underlying}&limit=1000";
                if (request.Since.HasValue) url += $"&startTime={request.Since.Value}";
                if (request.Until.HasValue) url += $"&endTime={request.Until.Value}";

                var json = await _http.GetStringAsync(url);
                var arr  = JArray.Parse(json);

                var bars = new List<Ohlcv>(arr.Count);
                foreach (var entry in arr)
                {
                    long ts = entry["fundingTime"]?.Value<long>() ?? 0;
                    double rate = double.TryParse(entry["fundingRate"]?.ToString(), out var r) ? r : 0.0;

                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    // Multiply by 100 so the chart displays in percent rather than fraction —
                    // e.g. 0.0001 → 0.01 — which is more readable on a thresholded oscillator
                    // and lets strategies compare against threshold values like "0.05" rather
                    // than "0.0005" which is easy to misread.
                    double pct = rate * 100.0;
                    bars.Add(new Ohlcv(date, pct, pct, pct, pct, 0));
                }

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            });
        }

        /// <summary>
        /// Fetches historical open interest. Endpoint:
        ///   GET /futures/data/openInterestHist?symbol=BTCUSDT&amp;period=1h&amp;limit=500
        ///
        /// Returns OI samples at the requested period. Mapped to Ohlcv with O=H=L=C =
        /// sumOpenInterestValue (USD-denominated open interest, which is more useful than
        /// raw contract count because it normalises across price changes).
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchOpenInterestHistoryAsync(string underlying, MarketDataRequest request)
        {
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string period = MapOiPeriod(request.Timeframe);
                var url = $"{FapiBase}/futures/data/openInterestHist?symbol={underlying}&period={period}&limit=500";
                if (request.Since.HasValue) url += $"&startTime={request.Since.Value}";
                if (request.Until.HasValue) url += $"&endTime={request.Until.Value}";

                var json = await _http.GetStringAsync(url);
                var arr  = JArray.Parse(json);

                var bars = new List<Ohlcv>(arr.Count);
                foreach (var entry in arr)
                {
                    long ts = entry["timestamp"]?.Value<long>() ?? 0;
                    double oiUsd = double.TryParse(entry["sumOpenInterestValue"]?.ToString(), out var v) ? v : 0.0;
                    var date = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
                    bars.Add(new Ohlcv(date, oiUsd, oiUsd, oiUsd, oiUsd, 0));
                }

                var vols = bars.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), b.Volume)).ToList();
                return (bars, vols);
            });
        }

        /// <summary>
        /// Translate the chart's timeframe code to a Binance OI history period code.
        /// Binance only accepts a fixed set; we round to the nearest supported value.
        /// </summary>
        private static string MapOiPeriod(string? timeframe) => (timeframe ?? "1h").ToLowerInvariant() switch
        {
            "5m"  => "5m",
            "15m" => "15m",
            "30m" => "30m",
            "1h"  => "1h",
            "2h"  => "2h",
            "4h"  => "4h",
            "6h"  => "6h",
            "12h" => "12h",
            "1d"  => "1d",
            _     => "1h"
        };
    }
}
