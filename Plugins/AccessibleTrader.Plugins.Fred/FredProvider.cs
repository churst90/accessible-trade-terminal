using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using Newtonsoft.Json.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.Plugins.Fred
{
    public class FredProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private const string BaseUrl = "https://api.stlouisfed.org/fred";

        public override string Name => "FRED";
        public override string Description => "Federal Reserve Economic Data";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Economic };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey) && _apiKey != "demo";
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
        public override int MaxBarsPerRequest => 100000;
        
        public override List<string> NativelySupportedTimeframes => new List<string> { StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes };

        public FredProvider()
        {
            _httpClient = new HttpClient();
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
        public override Task DisconnectAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var frequency = MapFrequency(request.Timeframe);
            string url = $"{BaseUrl}/series/observations?series_id={request.Symbol}&api_key={_apiKey}&file_type=json";
            if (!string.IsNullOrEmpty(frequency)) url += $"&frequency={frequency}";
            if (request.Since.HasValue)
                url += $"&observation_start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime:yyyy-MM-dd}";
            if (request.Until.HasValue)
                url += $"&observation_end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime:yyyy-MM-dd}";

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var observations = JObject.Parse(response)["observations"] as JArray;
                if (observations == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var ohlcvList = observations.Select(o => {
                    double.TryParse(o["value"]?.ToString(), out var val);
                    return new Ohlcv(DateTime.SpecifyKind(DateTime.Parse(o["date"]?.ToString() ?? DateTime.MinValue.ToString()), DateTimeKind.Utc), val, val, val, val, 0);
                }).ToList();

                return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"FRED fetch error: {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>()); 
            }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => new List<string> { "GDP", "CPIAUCSL", "UNRATE", "FEDFUNDS" };
        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string> { "Standard" });
        public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string> { "1d", "1w", "1m", "3m", "1y" });
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        private string MapFrequency(string tf) => tf.ToLower() switch { "1d" => "d", "1w" => "w", "1m" => "m", "3m" => "q", "1y" => "a", _ => "" };
    }
}
