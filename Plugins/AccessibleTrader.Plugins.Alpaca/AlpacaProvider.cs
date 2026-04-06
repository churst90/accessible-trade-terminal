using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Alpaca
{
    public class AlpacaProvider : BaseMarketDataProvider, ITradingProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;
        private const string StockDataUrl = "https://data.alpaca.markets/v2";

        // Order update stream — pushed from the Alpaca WebSocket trading stream.
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        private const string CryptoDataUrl = "https://data.alpaca.markets/v1beta3/crypto";
        private string _tradingBaseUrl = "https://paper-api.alpaca.markets/v2";

        // Live data WebSocket (replaces polling timer)
        private ClientWebSocket? _dataWs;
        private CancellationTokenSource? _dataWsCts;

        // Trading update WebSocket (for order fill events)
        private ClientWebSocket? _tradeWs;
        private CancellationTokenSource? _tradeWsCts;

        private string? _currentSymbol;
        private string? _currentTimeframe;
        private string? _currentMarket;

        public override string Name => "Alpaca";
        public override string Description => "Alpaca Stock & Crypto Market Integration";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Stock, MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment { get; } = ProviderEnvironment.Paper;
        public override int MaxBarsPerRequest => 10000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.None;
        
        public override List<string> NativelySupportedTimeframes => new List<string> 
        { 
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes, 
            StandardTimeframes.OneHour, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth 
        };
        public bool IsConnected => IsConfigured;

        public AlpacaProvider()
        {
            _httpClient = new HttpClient();
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;

            // Switch to live trading endpoint when explicitly configured.
            if (config.TryGetValue("Environment", out var env) && env == "Live")
                _tradingBaseUrl = "https://api.alpaca.markets/v2";

            if (IsConfigured)
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", _apiKey);
                _httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _apiSecret);
            }
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _dataWs?.State == WebSocketState.Open) return;

            _currentSymbol   = symbol;
            _currentTimeframe = timeframe;
            _currentMarket   = market;

            // Cancel any existing data stream
            _dataWsCts?.Cancel();
            _dataWsCts = new CancellationTokenSource();

            await ConnectDataStreamAsync(market, symbol, _dataWsCts.Token);

            // If configured for trading, ensure the trading update stream is running
            if (IsConfigured && _tradeWs?.State != WebSocketState.Open)
            {
                _tradeWsCts?.Cancel();
                _tradeWsCts = new CancellationTokenSource();
                _ = Task.Run(() => ConnectTradeStreamAsync(_tradeWsCts.Token));
            }
        }

        private async Task ConnectDataStreamAsync(string market, string symbol, CancellationToken ct)
        {
            _dataWs?.Dispose();
            _dataWs = new ClientWebSocket();

            bool isCrypto = market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            string wsUrl = isCrypto
                ? "wss://stream.data.alpaca.markets/v1beta3/crypto/us"
                : "wss://stream.data.alpaca.markets/v2/stocks";

            try
            {
                await _dataWs.ConnectAsync(new Uri(wsUrl), ct);

                // Authenticate
                var auth = new JObject
                {
                    ["action"] = "auth",
                    ["key"]    = _apiKey,
                    ["secret"] = _apiSecret
                };
                await SendWsAsync(_dataWs, auth.ToString(), ct);

                // Subscribe to minute bars
                var cleanSymbol = CleanSymbol(symbol);
                var sub = new JObject
                {
                    ["action"] = "subscribe",
                    ["bars"]   = new JArray { cleanSymbol }
                };
                await SendWsAsync(_dataWs, sub.ToString(), ct);

                _connectionStateStream.OnNext(ConnectionState.Connected);
                _ = Task.Run(() => DataReceiveLoopAsync(ct), ct);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca data stream error: {ex.Message}");
                _connectionStateStream.OnNext(ConnectionState.Error);
            }
        }

        private async Task DataReceiveLoopAsync(CancellationToken ct)
        {
            var buffer = new byte[1024 * 16];
            var ms = new System.IO.MemoryStream();
            try
            {
                while (!ct.IsCancellationRequested && _dataWs?.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _dataWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var msg = Encoding.UTF8.GetString(ms.ToArray());
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    var arr = JArray.Parse(msg);

                    foreach (var item in arr)
                    {
                        string? ev = item["T"]?.ToString();
                        if (ev == "b") // Minute bar
                        {
                            var bar = new Ohlcv(
                                item["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.UtcNow,
                                item["o"]?.Value<double>() ?? 0,
                                item["h"]?.Value<double>() ?? 0,
                                item["l"]?.Value<double>() ?? 0,
                                item["c"]?.Value<double>() ?? 0,
                                item["v"]?.Value<double>() ?? 0);
                            // Filter subscribe-confirmation frames: all-zero OHLCV with zero/epoch timestamp.
                            if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
                                && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime))
                                continue;
                            _liveStream.OnNext(bar);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _errorStream.OnNext($"Alpaca data stream receive error: {ex.Message}");
            }
            finally { ms.Dispose(); }
        }

        private async Task ConnectTradeStreamAsync(CancellationToken ct)
        {
            _tradeWs?.Dispose();
            _tradeWs = new ClientWebSocket();

            string wsUrl = _tradingBaseUrl.Contains("paper")
                ? "wss://paper-api.alpaca.markets/stream"
                : "wss://api.alpaca.markets/stream";

            try
            {
                await _tradeWs.ConnectAsync(new Uri(wsUrl), ct);

                var auth = new JObject
                {
                    ["action"] = "authenticate",
                    ["data"]   = new JObject { ["key_id"] = _apiKey, ["secret_key"] = _apiSecret }
                };
                await SendWsAsync(_tradeWs, auth.ToString(), ct);

                var listen = new JObject
                {
                    ["action"] = "listen",
                    ["data"]   = new JObject { ["streams"] = new JArray { "trade_updates" } }
                };
                await SendWsAsync(_tradeWs, listen.ToString(), ct);

                var buffer = new byte[1024 * 16];
                var ms = new System.IO.MemoryStream();
                while (!ct.IsCancellationRequested && _tradeWs.State == WebSocketState.Open)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _tradeWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    var msg  = Encoding.UTF8.GetString(ms.ToArray());
                    var json = JObject.Parse(msg);

                    if (json["stream"]?.ToString() == "trade_updates")
                    {
                        var data = json["data"];
                        if (data == null) continue;
                        var evt    = data["event"]?.ToString();
                        var order  = data["order"];
                        if (order == null) continue;

                        var status = evt switch
                        {
                            "fill"         => OrderStatus.Filled,
                            "partial_fill" => OrderStatus.PartialFill,
                            "canceled"     => OrderStatus.Cancelled,
                            "rejected"     => OrderStatus.Rejected,
                            _              => OrderStatus.Triggered
                        };

                        _orderUpdateSubject.OnNext(new OrderUpdate(
                            order["id"]?.ToString() ?? "",
                            order["symbol"]?.ToString() ?? "",
                            order["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                            double.TryParse(order["filled_qty"]?.ToString(), out double fq) ? fq : 0,
                            double.TryParse(order["filled_avg_price"]?.ToString(), out double fp) ? fp : 0,
                            0,
                            status,
                            false,
                            false,
                            DateTime.UtcNow));
                    }
                }
                ms.Dispose();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    _errorStream.OnNext($"Alpaca trade stream error: {ex.Message}");
            }
        }

        private static async Task SendWsAsync(ClientWebSocket ws, string message, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }

        public override Task DisconnectAsync()
        {
            _dataWsCts?.Cancel();
            _dataWs?.Dispose();
            _dataWs = null;

            _tradeWsCts?.Cancel();
            _tradeWs?.Dispose();
            _tradeWs = null;

            _currentSymbol    = null;
            _currentTimeframe = null;
            _currentMarket    = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            bool isCrypto = request.Market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            var symbol = CleanSymbol(request.Symbol);
            var timeframe = MapTimeframe(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);
            
            string url = isCrypto ? $"{CryptoDataUrl}/us/bars?symbols={symbol}&timeframe={timeframe}" : $"{StockDataUrl}/stocks/{symbol}/bars?timeframe={timeframe}";
            url += $"&limit={limit}";
            if (request.Since.HasValue) url += $"&start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            if (request.Until.HasValue) url += $"&end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                JArray? bars = isCrypto ? json["bars"]?[symbol] as JArray : json["bars"] as JArray;
                if (bars == null) return (new List<Ohlcv>(), new List<(long, double)>());

                var ohlcvList = bars.Select(b => new Ohlcv(b["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.MinValue, b["o"]?.Value<double>() ?? 0, b["h"]?.Value<double>() ?? 0, b["l"]?.Value<double>() ?? 0, b["c"]?.Value<double>() ?? 0, b["v"]?.Value<double>() ?? 0)).OrderBy(x => x.Date).ToList();
                return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
            }
            catch { return (new List<Ohlcv>(), new List<(long, double)>()); }
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                // Crypto uses the crypto assets endpoint; everything else uses the assets endpoint
                // filtered to active, tradeable US equities.
                string url = market == MarketType.Crypto
                    ? "https://api.alpaca.markets/v2/assets?asset_class=crypto&status=active"
                    : "https://api.alpaca.markets/v2/assets?asset_class=us_equity&status=active&tradable=true";

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("APCA-API-KEY-ID",     _apiKey);
                req.Headers.Add("APCA-API-SECRET-KEY", _apiSecret);

                var response = await _httpClient.SendAsync(req);
                response.EnsureSuccessStatusCode();

                var json    = await response.Content.ReadAsStringAsync();
                var assets  = JArray.Parse(json);
                return assets
                    .Select(a => a["symbol"]?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .OrderBy(s => s)
                    .ToList();
            }
            catch { return new List<string>(); }
        }
        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => new List<string> { "Spot" };
        public override async Task<List<string>> GetSupportedTimeframesAsync() => NativelySupportedTimeframes;
        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            var cleanSymbol = CleanSymbol(symbol);
            
            // Only crypto order books supported in this simple version
            string url = $"{CryptoDataUrl}/us/orderbooks?symbols={cleanSymbol}";
            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var json = JObject.Parse(response);
                var book = json["orderbooks"]?[cleanSymbol];
                if (book == null) return (new(), new());

                var bids = (book["b"] as JArray)?
                    .Take(limit)
                    .Select(b => new OrderBookEntry(double.Parse(b["p"]!.ToString()), double.Parse(b["s"]!.ToString())))
                    .ToList() ?? new();

                var asks = (book["a"] as JArray)?
                    .Take(limit)
                    .Select(a => new OrderBookEntry(double.Parse(a["p"]!.ToString()), double.Parse(a["s"]!.ToString())))
                    .ToList() ?? new();

                return (bids, asks);
            }
            catch { return (new(), new()); }
        }
        
        // ── ITradingProvider ─────────────────────────────────────────────────────

        /// <summary>Alpaca supports margin on crypto but not traditional leverage multiples.</summary>
        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override double MaxLeverage          => 1.0;

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                var response = await _httpClient.GetStringAsync($"{_tradingBaseUrl}/account");
                var json = JObject.Parse(response);
                double equity      = json["equity"]?.Value<double>() ?? 0;
                double cash        = json["cash"]?.Value<double>() ?? 0;
                double buyingPower = json["buying_power"]?.Value<double>() ?? 0;
                return new List<Balance>
                {
                    new("USD", cash, equity - cash),
                    new("Buying Power", buyingPower, 0)
                };
            }
            catch { return new(); }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                var response = await _httpClient.GetStringAsync($"{_tradingBaseUrl}/positions");
                var arr = JArray.Parse(response);
                return arr.Select(p => new Position(
                    p["symbol"]?.ToString() ?? "",
                    p["qty"]?.Value<double>() ?? 0,
                    p["avg_entry_price"]?.Value<double>() ?? 0,
                    p["market_value"]?.Value<double>() ?? 0,
                    p["unrealized_pl"]?.Value<double>() ?? 0
                )).ToList();
            }
            catch { return new(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            try
            {
                string url = $"{_tradingBaseUrl}/orders?status=open";
                if (!string.IsNullOrEmpty(symbol)) url += $"&symbols={symbol}";
                var response = await _httpClient.GetStringAsync(url);
                var arr = JArray.Parse(response);
                return arr.Select(o => new OpenOrder(
                    o["id"]?.ToString() ?? "",
                    o["symbol"]?.ToString() ?? "",
                    o["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                    MapAlpacaOrderType(o["type"]?.ToString() ?? "market"),
                    o["qty"]?.Value<double>() ?? 0,
                    o["limit_price"]?.Value<double>() ?? 0,
                    o["status"]?.ToString() ?? ""
                )).ToList();
            }
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                var body = new JObject
                {
                    ["symbol"]        = signal.Symbol,
                    ["qty"]           = signal.Quantity.ToString("F4"),
                    ["side"]          = signal.Side == OrderSide.Buy ? "buy" : "sell",
                    ["type"]          = signal.Type == OrderType.Limit ? "limit" : "market",
                    ["time_in_force"] = "gtc"
                };
                if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                    body["limit_price"] = signal.Price.Value;
                if (signal.StopLoss.HasValue)
                {
                    body["order_class"] = "bracket";
                    body["stop_loss"] = new JObject { ["stop_price"] = signal.StopLoss.Value };
                }
                if (signal.TakeProfit.HasValue)
                {
                    if (!body.ContainsKey("order_class")) body["order_class"] = "bracket";
                    body["take_profit"] = new JObject { ["limit_price"] = signal.TakeProfit.Value };
                }
                if (!string.IsNullOrEmpty(signal.ClientOid))
                    body["client_order_id"] = signal.ClientOid;

                var content = new System.Net.Http.StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_tradingBaseUrl}/orders", content);
                var responseStr = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{responseStr}";
                var json = JObject.Parse(responseStr);
                return json["id"]?.ToString() ?? "ORDER_SUBMITTED";
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConfigured) return false;
            try
            {
                var response = await _httpClient.DeleteAsync($"{_tradingBaseUrl}/orders/{orderId}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Alpaca does not support leverage multipliers; always returns 1.0.</summary>
        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        private static OrderType MapAlpacaOrderType(string type) => type switch
        {
            "limit"       => OrderType.Limit,
            "stop"        => OrderType.StopMarket,
            "stop_limit"  => OrderType.StopLimit,
            _             => OrderType.Market
        };

        private string MapTimeframe(string tf) => tf switch { "1m" => "1Min", "5m" => "5Min", "15m" => "15Min", "1h" => "1Hour", "1d" => "1Day", "1w" => "1Week", "1M" => "1Month", _ => "1Hour" };
    }
}
