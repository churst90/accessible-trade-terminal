using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using Newtonsoft.Json.Linq;
using System.Reactive.Subjects;

namespace AccessibleTrader.Plugins.Polygon
{
    public class PolygonProvider : BaseMarketDataProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private const string BaseUrl = "https://api.polygon.io";
        private const string WsUrl = "wss://delayed.polygon.io/stocks"; 

        private ClientWebSocket? _ws;
        private CancellationTokenSource? _wsCts;
        private string? _currentSymbol;

        public override string Name => "Polygon";
        public override string Description => "Polygon.io Multi-Market Data";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Stock, MarketType.Crypto, MarketType.Forex };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 50000;
        
        public override List<string> NativelySupportedTimeframes => new List<string> 
        { 
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes, 
            StandardTimeframes.OneHour, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth 
        };

        public PolygonProvider()
        {
            _httpClient = new HttpClient();
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
        }

        public override async Task EnsureConnectedAsync()
        {
            if (_ws != null && _ws.State == WebSocketState.Open) return;
            if (!IsConfigured) return;

            await DisconnectAsync();
            _wsCts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            
            _connectionStateStream.OnNext(ConnectionState.Connecting);
            try
            {
                await _ws.ConnectAsync(new Uri(WsUrl), _wsCts.Token);
                var auth = $"{{\"action\":\"auth\",\"params\":\"{_apiKey}\"}}";
                await SendWsMessage(auth);
                _connectionStateStream.OnNext(ConnectionState.Connected);
                _ = ReceiveWsLoop(_wsCts.Token);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Polygon connection failed: {ex.Message}");
                _connectionStateStream.OnNext(ConnectionState.Error);
                throw;
            }
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();
            if (_currentSymbol == symbol) return;

            // Unsubscribe previous
            if (!string.IsNullOrEmpty(_currentSymbol))
            {
                string prefix = market.Contains("Crypto") ? "XA" : market.Contains("Forex") ? "CA" : "AM";
                await SendWsMessage($"{{\"action\":\"unsubscribe\",\"params\":\"{prefix}.{_currentSymbol}\"}}");
            }

            _currentSymbol = symbol;
            string newPrefix = market.Contains("Crypto") ? "XA" : market.Contains("Forex") ? "CA" : "AM";
            await SendWsMessage($"{{\"action\":\"subscribe\",\"params\":\"{newPrefix}.{_currentSymbol}\"}}");
        }

        public override async Task DisconnectAsync()
        {
            _wsCts?.Cancel();
            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open) try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stop", CancellationToken.None); } catch { }
                _ws.Dispose();
                _ws = null;
            }
            _currentSymbol = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        private async Task SendWsMessage(string message)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _wsCts?.Token ?? CancellationToken.None);
        }

        private async Task ReceiveWsLoop(CancellationToken token)
        {
            var buffer = new byte[4096 * 4];
            try
            {
                while (!token.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var json = JArray.Parse(message);

                    foreach (var item in json)
                    {
                        var ev = item["ev"]?.ToString();
                        if (ev == "AM" || ev == "XA" || ev == "CA")
                        {
                            double o = item["o"]?.Value<double>() ?? 0;
                            double h = item["h"]?.Value<double>() ?? 0;
                            double l = item["l"]?.Value<double>() ?? 0;
                            double c = item["c"]?.Value<double>() ?? 0;

                            // Filter subscribe-confirmation and all-zero frames
                            if (o == 0 && h == 0 && l == 0 && c == 0) continue;

                            _liveStream.OnNext(new Ohlcv(
                                DateTimeOffset.FromUnixTimeMilliseconds(item["e"]?.Value<long>() ?? 0).UtcDateTime,
                                o, h, l, c,
                                item["v"]?.Value<double>() ?? 0
                            ));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            { 
                _errorStream.OnNext($"Polygon stream error: {ex.Message}");
                _connectionStateStream.OnNext(ConnectionState.Error); 
            }
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            var symbol = request.Symbol.ToUpper();
            var (multiplier, timespan) = MapTimeframe(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);
            long fromMs = request.Since ?? (DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds());
            long toMs = request.Until ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            string url = $"{BaseUrl}/v2/aggs/ticker/{symbol}/range/{multiplier}/{timespan}/{fromMs}/{toMs}?adjusted=true&sort=asc&limit={limit}&apiKey={_apiKey}";

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var results = json["results"] as JArray;
                if (results == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var ohlcvList = results.Select(r => new Ohlcv(DateTimeOffset.FromUnixTimeMilliseconds(r["t"]?.Value<long>() ?? 0).UtcDateTime, r["o"]?.Value<double>() ?? 0, r["h"]?.Value<double>() ?? 0, r["l"]?.Value<double>() ?? 0, r["c"]?.Value<double>() ?? 0, r["v"]?.Value<double>() ?? 0)).ToList();
                return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
            }
            catch { return (new List<Ohlcv>(), new List<(long, double)>()); }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                string m = market switch { MarketType.Stock => "stocks", MarketType.Crypto => "crypto", MarketType.Forex => "fx", _ => "stocks" };
                var response = await _httpClient.GetStringAsync($"{BaseUrl}/v3/reference/tickers?market={m}&active=true&limit=1000&apiKey={_apiKey}");
                var results = JObject.Parse(response)["results"] as JArray;
                return results?.Select(r => r["ticker"]?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).OrderBy(s => s).ToList() ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string> { "Standard" });
        public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(NativelySupportedTimeframes);
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10) => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

        private (int multiplier, string timespan) MapTimeframe(string tf) => tf.ToLower() switch { "1m" => (1, "minute"), "5m" => (5, "minute"), "15m" => (15, "minute"), "1h" => (1, "hour"), "1d" => (1, "day"), "1w" => (1, "week"), "1M" => (1, "month"), _ => (1, "hour") };
    }
}
