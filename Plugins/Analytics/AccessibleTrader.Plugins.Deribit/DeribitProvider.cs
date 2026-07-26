using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Deribit
{
    /// <summary>
    /// Deribit crypto-options analytics — the piece the terminal was missing on the
    /// options side. Two keyless public series per currency (BTC / ETH):
    ///
    ///     BTC_DVOL     — the Deribit Volatility Index, crypto's "VIX": the 30-day
    ///                    forward implied volatility of the options book, as OHLC.
    ///     BTC_HISTVOL  — realised (historical) annualised volatility, single value.
    ///
    /// Both fold into the app's single-value analytics shape (Derivatives category).
    /// No API key: Deribit's public/get_volatility_index_data and
    /// public/get_historical_volatility endpoints are open. Strategy use: DVOL well
    /// above realised vol = options are pricing fear (mean-reversion setups); a DVOL
    /// spike alongside a price flush often marks capitulation.
    /// </summary>
    public class DeribitProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _http = PluginHostServices.CreateHttpClient(
            providerId: "Deribit",
            allowedHosts: new[] { "www.deribit.com" });

        private readonly RateLimiter _rateLimiter = new(600, TimeSpan.FromMinutes(1));

        private const string Base = "https://www.deribit.com/api/v2";
        private const string DvolSuffix    = "_DVOL";
        private const string HistVolSuffix = "_HISTVOL";
        private static readonly string[] Currencies = { "BTC", "ETH" };

        public override string Name        => "Deribit";
        public override string Description => "Deribit crypto-options volatility: DVOL index (crypto VIX) and realised volatility (analytics).";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Derivatives };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey       => false;
        public override bool IsConfigured         => true;
        public override bool SupportsLiveUpdates  => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest     => 1000;
        public override ProviderDataShape DataShape => ProviderDataShape.SingleValueLine;

        public override string GetSymbolDisplayName(string symbol)
        {
            if (symbol.EndsWith(DvolSuffix, StringComparison.Ordinal))
                return $"{symbol[..^DvolSuffix.Length]} DVOL (Volatility Index)";
            if (symbol.EndsWith(HistVolSuffix, StringComparison.Ordinal))
                return $"{symbol[..^HistVolSuffix.Length]} Realised Volatility";
            return symbol;
        }

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneHour, StandardTimeframes.TwelveHours, StandardTimeframes.OneDay
        };

        public override void Configure(Dictionary<string, string> config) { }

        public override Task EnsureConnectedAsync() => Task.CompletedTask;
        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
        public override Task DisconnectAsync() => Task.CompletedTask;

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Derivatives" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            var list = new List<string>();
            foreach (var c in Currencies) { list.Add(c + DvolSuffix); list.Add(c + HistVolSuffix); }
            return Task.FromResult(list);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            try
            {
                var (currency, isDvol) = SplitSymbol(request.Symbol);
                if (currency == null) return (new(), new());

                long endMs   = request.Until ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long startMs = request.Since ?? endMs - (long)DaysBack(request.Timeframe, request.Limit);

                var bars = await _rateLimiter.ExecuteAsync(() =>
                    isDvol ? FetchDvolAsync(currency, request.Timeframe, startMs, endMs)
                           : FetchHistVolAsync(currency)).ConfigureAwait(false);

                var trimmed = bars.OrderBy(b => b.Date).TakeLast(Math.Min(request.Limit, MaxBarsPerRequest)).ToList();
                return (trimmed, trimmed.Select(b => (new DateTimeOffset(b.Date).ToUnixTimeMilliseconds(), 0.0)).ToList());
            }
            catch (Exception ex)
            {
                SurfaceError($"Deribit fetch failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new(), new());
            }
        }

        private async Task<List<Ohlcv>> FetchDvolAsync(string currency, string timeframe, long startMs, long endMs)
        {
            // resolution is in seconds ("60" 1m, "3600" 1h, "43200" 12h) or "1D".
            string resolution = timeframe switch
            {
                "1h"  => "3600",
                "12h" => "43200",
                "1d"  => "1D",
                _     => "3600",
            };
            var q = new Dictionary<string, string>
            {
                ["currency"] = currency,
                ["start_timestamp"] = startMs.ToString(CultureInfo.InvariantCulture),
                ["end_timestamp"]   = endMs.ToString(CultureInfo.InvariantCulture),
                ["resolution"] = resolution,
            };
            var body = await _http.GetStringAsync($"{Base}/public/get_volatility_index_data{RestSigning.QueryPrefixed(q)}").ConfigureAwait(false);
            return ParseDvol(body);
        }

        private async Task<List<Ohlcv>> FetchHistVolAsync(string currency)
        {
            var body = await _http.GetStringAsync(
                $"{Base}/public/get_historical_volatility{RestSigning.QueryPrefixed(new Dictionary<string, string> { ["currency"] = currency })}").ConfigureAwait(false);
            return ParseHistVol(body);
        }

        /// <summary>result.data = [[ts_ms, open, high, low, close], ...].</summary>
        internal static List<Ohlcv> ParseDvol(string json)
        {
            var data = (JObject.Parse(json)["result"] as JObject)?["data"] as JArray;
            if (data == null) return new();
            var bars = new List<Ohlcv>(data.Count);
            foreach (var row in data)
            {
                if (row is not JArray a || a.Count < 5) continue;
                var date = DateTimeOffset.FromUnixTimeMilliseconds(a[0]!.Value<long>()).UtcDateTime;
                bars.Add(new Ohlcv(date, a[1]!.Value<double>(), a[2]!.Value<double>(), a[3]!.Value<double>(), a[4]!.Value<double>(), 0));
            }
            return bars;
        }

        /// <summary>result = [[ts_ms, volatility], ...] — single value → flat OHLC.</summary>
        internal static List<Ohlcv> ParseHistVol(string json)
        {
            var data = JObject.Parse(json)["result"] as JArray;
            if (data == null) return new();
            var bars = new List<Ohlcv>(data.Count);
            foreach (var row in data)
            {
                if (row is not JArray a || a.Count < 2) continue;
                var date = DateTimeOffset.FromUnixTimeMilliseconds(a[0]!.Value<long>()).UtcDateTime;
                double v = a[1]!.Value<double>();
                bars.Add(new Ohlcv(date, v, v, v, v, 0));
            }
            return bars;
        }

        private static (string? Currency, bool IsDvol) SplitSymbol(string symbol)
        {
            if (symbol.EndsWith(DvolSuffix, StringComparison.Ordinal))
                return (symbol[..^DvolSuffix.Length].ToUpperInvariant(), true);
            if (symbol.EndsWith(HistVolSuffix, StringComparison.Ordinal))
                return (symbol[..^HistVolSuffix.Length].ToUpperInvariant(), false);
            return (null, false);
        }

        private static double DaysBack(string timeframe, int limit)
        {
            double perBarMs = timeframe switch { "1h" => 3_600_000, "12h" => 43_200_000, "1d" => 86_400_000, _ => 3_600_000 };
            return perBarMs * Math.Max(limit, 1);
        }

        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) =>
            Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
    }
}
