using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Plugins.Binance
{
    /// <summary>
    /// Binance provider talking directly to the public/private REST and WebSocket
    /// endpoints — no Binance.Net / CryptoExchange.Net dependency (see the csproj
    /// header for why). Covers spot + USD-M futures: historical klines, symbols,
    /// the order book (REST + live), the user-data order stream, and full trading
    /// (balances, positions, open orders, place/cancel, leverage, protective TP/SL).
    /// </summary>
    public class BinanceProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        // ── Endpoints (mainnet / testnet) ─────────────────────────────────────
        private string SpotRest => _isTestnet ? "https://testnet.binance.vision"     : "https://api.binance.com";
        private string FutRest  => _isTestnet ? "https://testnet.binancefuture.com"  : "https://fapi.binance.com";
        private string SpotWs   => _isTestnet ? "wss://testnet.binance.vision/ws/"   : "wss://stream.binance.com:9443/ws/";
        private string FutWs    => _isTestnet ? "wss://stream.binancefuture.com/ws/" : "wss://fstream.binance.com/ws/";

        private HttpClient? _httpField;
        private HttpClient Http => _httpField ??= PluginHostServices.CreateHttpClient(
            "Binance",
            new[] { "api.binance.com", "fapi.binance.com", "testnet.binance.vision", "testnet.binancefuture.com" },
            userAgent: "AccessibleTrader/1.0");

        private string? _apiKey;
        private string? _apiSecret;
        private bool _isTestnet = false;

        // Binance allows 1200 request-weight/minute for REST.
        private readonly RateLimiter _rateLimiter = new(1200, TimeSpan.FromMinutes(1));

        // Live kline subscription.
        private CancellationTokenSource? _klineCts;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        // Order update stream (user-data).
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Live order book.
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private CancellationTokenSource? _orderBookCts;
        private string? _orderBookSymbol;

        // User-data stream lifecycle.
        private CancellationTokenSource? _userDataCts;
        private string? _listenKey;

        public override string Name => "Binance";
        public override string Description => "Live Binance Exchange Data (Spot & Futures)";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => _isTestnet ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 1000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth | ProviderCapabilities.TrailingStop | ProviderCapabilities.OCO | ProviderCapabilities.Leverage | ProviderCapabilities.Brackets;

        public override bool SupportsMarginTrading  => true;
        public override bool SupportsFuturesTrading => true;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 125.0;

        // Trading is available when we have either Configure-supplied creds or the
        // host credential-checkout bridge to pull an active key at sign time.
        public bool IsConnected => !string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour,
            StandardTimeframes.TwoHours, StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.EightHours, StandardTimeframes.TwelveHours, StandardTimeframes.OneDay,
            StandardTimeframes.ThreeDays, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            if (typeof(T) == typeof(IOrderBookProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey",    out var key))    _apiKey    = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;
            if (config.TryGetValue("Testnet",   out var tn))     _isTestnet = tn == "true";
        }

        // Sign-time credential checkout. Prefers the PluginHostServices.ApiKeys
        // bridge (use-and-discard); falls back to Configure-populated fields for
        // unit tests / CLI runs.
        private async Task<(string Key, string Secret)> CheckoutBinanceCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Binance").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Binance: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Binance: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) && PluginHostServices.ApiKeys == null)
                return (true, "No API key provided (public data only)");
            try
            {
                await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/account", new Dictionary<string, string>());
                return (true, "API key validated successfully");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // ── Connection & subscription management ──────────────────────────────

        public override async Task EnsureConnectedAsync()
        {
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start the authenticated user-data stream only when a credential
            // source exists. Trade ops sign per-request regardless.
            if (IsConnected && _userDataCts == null)
            {
                try { await StartUserDataStreamAsync(); }
                catch { /* public-data-only mode */ }
            }
        }

        private async Task StartUserDataStreamAsync()
        {
            try
            {
                _listenKey = await CreateListenKeyAsync();
                if (string.IsNullOrEmpty(_listenKey)) return;

                _userDataCts = new CancellationTokenSource();
                var ct = _userDataCts.Token;
                var uri = new Uri($"{SpotWs}{_listenKey}");

                _ = Task.Run(() => RunSocketAsync(uri, OnUserDataMessage, ct));

                // Keep-alive every 25 min so the listen key doesn't expire (60 min TTL).
                _ = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try { await Task.Delay(TimeSpan.FromMinutes(25), ct); }
                        catch { break; }
                        try { await KeepAliveListenKeyAsync(_listenKey!); }
                        catch (Exception ex)
                        {
                            _errorStream.OnNext($"Binance user-data keep-alive failed: {ex.GetType().Name}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance user-data stream unavailable ({ex.GetType().Name}): order-fill updates won't be delivered.");
            }
        }

        private void OnUserDataMessage(JsonElement root)
        {
            if (!root.TryGetProperty("e", out var ev) || ev.GetString() != "executionReport") return;

            string statusStr = root.GetProperty("X").GetString() ?? "";
            OrderStatus? status = statusStr switch
            {
                "PARTIALLY_FILLED" => OrderStatus.PartialFill,
                "FILLED"           => OrderStatus.Filled,
                "CANCELED"         => OrderStatus.Cancelled,
                "REJECTED"         => OrderStatus.Rejected,
                "EXPIRED"          => OrderStatus.Rejected,
                _                  => (OrderStatus?)null  // NEW / others: nothing to announce
            };
            if (status == null) return;

            string symbol   = root.GetProperty("s").GetString() ?? "";
            var side        = (root.GetProperty("S").GetString() == "SELL") ? OrderSide.Sell : OrderSide.Buy;
            double origQty  = Dbl(root, "q");
            double filled   = Dbl(root, "z");                 // cumulative filled qty
            double lastPx   = Dbl(root, "L");                 // last filled price
            double orderPx  = Dbl(root, "p");
            double fillPx   = lastPx > 0 ? lastPx : orderPx;
            string orderId  = root.TryGetProperty("i", out var idEl) ? idEl.GetRawText() : "";
            string oType    = root.GetProperty("o").GetString() ?? "";

            bool stopTrig = oType is "STOP_LOSS" or "STOP_LOSS_LIMIT";
            bool tpTrig   = oType is "TAKE_PROFIT" or "TAKE_PROFIT_LIMIT";

            _orderUpdateSubject.OnNext(new OrderUpdate(
                orderId, symbol, side,
                filled, fillPx, Math.Max(0, origQty - filled),
                status.Value, stopTrig, tpTrig, DateTime.UtcNow));
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var cleanSymbol = CleanSymbol(symbol);
            if (_currentSymbol == cleanSymbol && _currentTimeframe == timeframe && _klineCts != null) return;

            _klineCts?.Cancel();
            _klineCts?.Dispose();
            _klineCts = new CancellationTokenSource();
            var ct = _klineCts.Token;

            _currentSymbol = cleanSymbol;
            _currentTimeframe = timeframe;

            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            string wsBase = isFutures ? FutWs : SpotWs;
            string interval = MapInterval(timeframe);
            var uri = new Uri($"{wsBase}{cleanSymbol.ToLowerInvariant()}@kline_{interval}");

            _ = Task.Run(() => RunSocketAsync(uri, OnKlineMessage, ct));
        }

        private void OnKlineMessage(JsonElement root)
        {
            if (!root.TryGetProperty("k", out var k)) return;
            long openTime = k.GetProperty("t").GetInt64();
            var bar = new Ohlcv(
                DateTimeOffset.FromUnixTimeMilliseconds(openTime).UtcDateTime,
                Dbl(k, "o"), Dbl(k, "h"), Dbl(k, "l"), Dbl(k, "c"), Dbl(k, "v"));

            if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0) return;
            _liveStream.OnNext(bar);
        }

        public override async Task DisconnectAsync()
        {
            _klineCts?.Cancel();      _klineCts?.Dispose();      _klineCts = null;
            _orderBookCts?.Cancel();  _orderBookCts?.Dispose();  _orderBookCts = null;
            _orderBookSymbol = null;

            _userDataCts?.Cancel();   _userDataCts?.Dispose();   _userDataCts = null;

            if (!string.IsNullOrEmpty(_listenKey))
            {
                try { await CloseListenKeyAsync(_listenKey); }
                catch (Exception ex) { _errorStream.OnNext($"Binance StopUserStream failed: {ex.GetType().Name}"); }
                _listenKey = null;
            }

            _currentSymbol = null;
            _currentTimeframe = null;

            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            await Task.CompletedTask;
        }

        // ── Data discovery & fetching ─────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Spot", "Futures" });

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    bool isFutures = subType.Equals("Futures", StringComparison.OrdinalIgnoreCase);
                    string body = await GetPublicAsync(isFutures ? FutRest : SpotRest,
                        isFutures ? "/fapi/v1/exchangeInfo" : "/api/v3/exchangeInfo", null);
                    using var doc = JsonDocument.Parse(body);
                    return doc.RootElement.GetProperty("symbols")
                        .EnumerateArray()
                        .Select(s => s.GetProperty("symbol").GetString() ?? "")
                        .Where(s => s.Length > 0)
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(new List<string> { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = CleanSymbol(request.Symbol);
            bool isFutures = request.Market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            string interval = MapInterval(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var q = new StringBuilder();
                    q.Append("symbol=").Append(cleanSymbol).Append("&interval=").Append(interval).Append("&limit=").Append(limit);
                    if (request.Since != null) q.Append("&startTime=").Append(request.Since.Value);
                    if (request.Until != null) q.Append("&endTime=").Append(request.Until.Value);

                    string body = await GetPublicAsync(isFutures ? FutRest : SpotRest,
                        isFutures ? "/fapi/v1/klines" : "/api/v3/klines", q.ToString());

                    using var doc = JsonDocument.Parse(body);
                    var ohlcv = new List<Ohlcv>();
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        // [ openTime, open, high, low, close, volume, ... ]
                        long openTime = row[0].GetInt64();
                        ohlcv.Add(new Ohlcv(
                            DateTimeOffset.FromUnixTimeMilliseconds(openTime).UtcDateTime,
                            DblAt(row, 1), DblAt(row, 2), DblAt(row, 3), DblAt(row, 4), DblAt(row, 5)));
                    }
                    var vol = ohlcv.Select(x => (new DateTimeOffset(x.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), x.Volume)).ToList();
                    return (ohlcv, vol);
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            var cleanSymbol = CleanSymbol(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string body = await GetPublicAsync(SpotRest, "/api/v3/depth",
                        $"symbol={cleanSymbol}&limit={SnapDepth(limit)}");
                    using var doc = JsonDocument.Parse(body);
                    return (ParseLevels(doc.RootElement.GetProperty("bids")),
                            ParseLevels(doc.RootElement.GetProperty("asks")));
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        // ── IOrderBookProvider ────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol)
        {
            var cleanSymbol = CleanSymbol(symbol);
            if (_orderBookSymbol == cleanSymbol && _orderBookCts != null)
                return _orderBookSubject.AsObservable();

            _orderBookCts?.Cancel();
            _orderBookCts?.Dispose();
            _orderBookCts = new CancellationTokenSource();
            var ct = _orderBookCts.Token;
            _orderBookSymbol = cleanSymbol;

            var uri = new Uri($"{SpotWs}{cleanSymbol.ToLowerInvariant()}@depth20@1000ms");
            _ = Task.Run(() => RunSocketAsync(uri, root =>
            {
                if (!root.TryGetProperty("bids", out var bids) || !root.TryGetProperty("asks", out var asks)) return;
                _orderBookSubject.OnNext(new OrderBookUpdate(
                    cleanSymbol, ParseLevels(bids), ParseLevels(asks), 0, DateTime.UtcNow));
            }, ct));

            return _orderBookSubject.AsObservable();
        }

        // ── ITradingProvider ──────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string body = await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/account", new Dictionary<string, string>());
                    using var doc = JsonDocument.Parse(body);
                    var list = new List<Balance>();
                    foreach (var b in doc.RootElement.GetProperty("balances").EnumerateArray())
                    {
                        double free = Dbl(b, "free");
                        double locked = Dbl(b, "locked");
                        if (free > 0 || locked > 0)
                            list.Add(new Balance(b.GetProperty("asset").GetString() ?? "", free, locked));
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetBalancesAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string body = await SignedRequestAsync(HttpMethod.Get, FutRest, "/fapi/v2/positionRisk", new Dictionary<string, string>());
                    using var doc = JsonDocument.Parse(body);
                    var list = new List<Position>();
                    foreach (var p in doc.RootElement.EnumerateArray())
                    {
                        double qty = Dbl(p, "positionAmt");
                        if (qty == 0) continue;
                        double mark = Dbl(p, "markPrice");
                        list.Add(new Position(
                            p.GetProperty("symbol").GetString() ?? "",
                            Math.Abs(qty),
                            Dbl(p, "entryPrice"),
                            Math.Abs(qty) * mark,
                            Dbl(p, "unRealizedProfit"),
                            Dbl(p, "leverage"),
                            Dbl(p, "liquidationPrice")));
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetPositionsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var p = new Dictionary<string, string>();
                    if (symbol != null) p["symbol"] = CleanSymbol(symbol);
                    string body = await SignedRequestAsync(HttpMethod.Get, SpotRest, "/api/v3/openOrders", p);
                    using var doc = JsonDocument.Parse(body);
                    var list = new List<OpenOrder>();
                    foreach (var o in doc.RootElement.EnumerateArray())
                    {
                        list.Add(new OpenOrder(
                            o.GetProperty("orderId").GetRawText(),
                            o.GetProperty("symbol").GetString() ?? "",
                            (o.GetProperty("side").GetString() == "SELL") ? OrderSide.Sell : OrderSide.Buy,
                            MapOrderType(o.GetProperty("type").GetString() ?? ""),
                            Dbl(o, "origQty"),
                            Dbl(o, "price"),
                            o.GetProperty("status").GetString() ?? ""));
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance GetOpenOrdersAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            bool isFutures = string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var symbol = CleanSymbol(signal.Symbol);
                    string side = signal.Side == OrderSide.Buy ? "BUY" : "SELL";

                    if (isFutures)
                    {
                        if (signal.Leverage.HasValue && signal.Leverage.Value > 1)
                            await SetLeverageAsync(symbol, signal.Leverage.Value);

                        var p = new Dictionary<string, string> { ["symbol"] = symbol, ["side"] = side };
                        string futType = signal.Type switch
                        {
                            OrderType.Limit            => "LIMIT",
                            OrderType.StopMarket       => "STOP_MARKET",
                            OrderType.StopLimit        => "STOP",
                            OrderType.TakeProfitMarket => "TAKE_PROFIT_MARKET",
                            OrderType.TakeProfitLimit  => "TAKE_PROFIT",
                            _                          => "MARKET"
                        };
                        p["type"] = futType;
                        p["quantity"] = Fmt(signal.Quantity);
                        if (futType != "MARKET")
                        {
                            if (signal.Price.HasValue) p["price"] = Fmt(signal.Price.Value);
                            if (signal.StopLoss.HasValue) p["stopPrice"] = Fmt(signal.StopLoss.Value);
                            p["timeInForce"] = "GTC";
                        }
                        if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;

                        string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
                        string id = ParseOrderId(body);

                        // Protective TP/SL attach (separate reduce-only orders).
                        string exitSide = side == "BUY" ? "SELL" : "BUY";
                        if (signal.TakeProfit.HasValue)
                            await AttachProtectiveOrderAsync(symbol, exitSide, "TAKE_PROFIT_MARKET",
                                signal.Quantity, signal.TakeProfit.Value, DeriveProtectiveOid(signal.ClientOid, "tp"), "take-profit");
                        if (signal.StopLoss.HasValue && futType == "MARKET")
                            await AttachProtectiveOrderAsync(symbol, exitSide, "STOP_MARKET",
                                signal.Quantity, signal.StopLoss.Value, DeriveProtectiveOid(signal.ClientOid, "sl"), "stop-loss");

                        return id;
                    }
                    else
                    {
                        var p = new Dictionary<string, string> { ["symbol"] = symbol, ["side"] = side };
                        switch (signal.Type)
                        {
                            case OrderType.Market:
                                p["type"] = "MARKET"; p["quantity"] = Fmt(signal.Quantity);
                                break;
                            case OrderType.Limit when signal.Price.HasValue:
                                p["type"] = "LIMIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["price"] = Fmt(signal.Price.Value); p["timeInForce"] = "GTC";
                                break;
                            case OrderType.StopMarket when signal.StopLoss.HasValue:
                                p["type"] = "STOP_LOSS"; p["quantity"] = Fmt(signal.Quantity);
                                p["stopPrice"] = Fmt(signal.StopLoss.Value);
                                break;
                            case OrderType.StopLimit when signal.StopLoss.HasValue && signal.Price.HasValue:
                                p["type"] = "STOP_LOSS_LIMIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["price"] = Fmt(signal.Price.Value); p["stopPrice"] = Fmt(signal.StopLoss.Value);
                                p["timeInForce"] = "GTC";
                                break;
                            case OrderType.TakeProfitMarket when signal.TakeProfit.HasValue:
                                p["type"] = "TAKE_PROFIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["stopPrice"] = Fmt(signal.TakeProfit.Value);
                                break;
                            case OrderType.TakeProfitLimit when signal.TakeProfit.HasValue && signal.Price.HasValue:
                                p["type"] = "TAKE_PROFIT_LIMIT"; p["quantity"] = Fmt(signal.Quantity);
                                p["price"] = Fmt(signal.Price.Value); p["stopPrice"] = Fmt(signal.TakeProfit.Value);
                                p["timeInForce"] = "GTC";
                                break;
                            default:
                                return "ORDER_FAILED:Unsupported order type";
                        }
                        if (!string.IsNullOrEmpty(signal.ClientOid)) p["newClientOrderId"] = signal.ClientOid!;

                        string body = await SignedRequestAsync(HttpMethod.Post, SpotRest, "/api/v3/order", p);
                        return ParseOrderId(body);
                    }
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Binance order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.Message}"; }
        }

        /// <summary>
        /// Places a futures protective order (TP/SL) with one retry, surfacing a loud
        /// error if both attempts fail — a naked position must be heard about now.
        /// </summary>
        private async Task AttachProtectiveOrderAsync(
            string symbol, string exitSide, string type, double quantity, double triggerPrice, string? clientOrderId, string label)
        {
            string? lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var p = new Dictionary<string, string>
                    {
                        ["symbol"] = symbol, ["side"] = exitSide, ["type"] = type,
                        ["quantity"] = Fmt(quantity), ["stopPrice"] = Fmt(triggerPrice),
                        ["timeInForce"] = "GTC", ["reduceOnly"] = "true"
                    };
                    if (!string.IsNullOrEmpty(clientOrderId)) p["newClientOrderId"] = clientOrderId!;
                    await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/order", p);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    // Duplicate clientOrderId on the retry means the first attempt landed.
                    if (attempt == 1 && lastError.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                        return;
                }
            }
            _errorStream.OnNext(
                $"POSITION UNPROTECTED: {label} order for {symbol} failed to attach after retry " +
                $"({lastError}). The entry order is live without its {label}. " +
                $"Place the {label} manually or close the position.");
        }

        private static string? DeriveProtectiveOid(string? entryOid, string suffix)
        {
            if (string.IsNullOrEmpty(entryOid)) return null;
            string baseOid = entryOid.Length > 32 ? entryOid.Substring(0, 32) : entryOid;
            return $"{baseOid}-{suffix}";
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var p = new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["orderId"] = orderId };
                    await SignedRequestAsync(HttpMethod.Delete, SpotRest, "/api/v3/order", p);
                    return true;
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance CancelOrderAsync failed for {orderId}/{symbol} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            if (!IsConnected) return 1.0;
            try
            {
                int lev = (int)Math.Clamp(leverage, 1, MaxLeverage);
                var p = new Dictionary<string, string> { ["symbol"] = CleanSymbol(symbol), ["leverage"] = lev.ToString(CultureInfo.InvariantCulture) };
                string body = await SignedRequestAsync(HttpMethod.Post, FutRest, "/fapi/v1/leverage", p);
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("leverage", out var l) ? l.GetDouble() : lev;
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Binance SetLeverageAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return 1.0;
            }
        }

        // ── User-data stream (listen key) REST ────────────────────────────────

        private async Task<string?> CreateListenKeyAsync()
        {
            string body = await KeyOnlyRequestAsync(HttpMethod.Post, "/api/v3/userDataStream", null);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("listenKey", out var lk) ? lk.GetString() : null;
        }

        private Task KeepAliveListenKeyAsync(string listenKey)
            => KeyOnlyRequestAsync(HttpMethod.Put, "/api/v3/userDataStream", listenKey);

        private Task CloseListenKeyAsync(string listenKey)
            => KeyOnlyRequestAsync(HttpMethod.Delete, "/api/v3/userDataStream", listenKey);

        // ── HTTP plumbing ─────────────────────────────────────────────────────

        private async Task<string> GetPublicAsync(string baseUrl, string path, string? query)
        {
            string url = $"{baseUrl}{path}" + (string.IsNullOrEmpty(query) ? "" : $"?{query}");
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
            return body;
        }

        // SIGNED request: appends timestamp + recvWindow, HMAC-SHA256 signs the
        // query, sends params in the query string with the API-key header.
        private async Task<string> SignedRequestAsync(HttpMethod method, string baseUrl, string path, Dictionary<string, string> p)
        {
            var (key, secret) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);

            var sb = new StringBuilder();
            foreach (var kv in p)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(kv.Key).Append('=').Append(Uri.EscapeDataString(kv.Value));
            }
            if (sb.Length > 0) sb.Append('&');
            sb.Append("recvWindow=5000&timestamp=").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            string query = sb.ToString();
            string signature = Sign(query, secret);

            using var req = new HttpRequestMessage(method, $"{baseUrl}{path}?{query}&signature={signature}");
            req.Headers.Add("X-MBX-APIKEY", key);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
            return body;
        }

        // Key-only request (user-data stream lifecycle): API-key header, no signature.
        private async Task<string> KeyOnlyRequestAsync(HttpMethod method, string path, string? listenKey)
        {
            var (key, _) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);
            string url = $"{SpotRest}{path}" + (listenKey != null ? $"?listenKey={listenKey}" : "");
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Add("X-MBX-APIKEY", key);
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"{(int)resp.StatusCode} {body}");
            return body;
        }

        private static string Sign(string query, string secret)
        {
            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            byte[] hash = h.ComputeHash(Encoding.UTF8.GetBytes(query));
            return Convert.ToHexStringLower(hash);
        }

        // ── WebSocket runner with reconnect ───────────────────────────────────

        private async Task RunSocketAsync(Uri uri, Action<JsonElement> onMessage, CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                    var sb = new StringBuilder();
                    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                    {
                        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (!result.EndOfMessage) continue;
                        string msg = sb.ToString();
                        sb.Clear();
                        try
                        {
                            using var doc = JsonDocument.Parse(msg);
                            onMessage(doc.RootElement);
                        }
                        catch { /* ignore malformed frame */ }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"Binance socket {uri.AbsolutePath} error ({ex.GetType().Name}); reconnecting.");
                }
                if (!ct.IsCancellationRequested)
                {
                    try { await Task.Delay(2000, ct).ConfigureAwait(false); } catch { break; }
                }
            }
        }

        // ── Small parse/format helpers ────────────────────────────────────────

        private static double Dbl(JsonElement obj, string prop)
        {
            if (!obj.TryGetProperty(prop, out var el)) return 0;
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
                _ => 0
            };
        }

        private static double DblAt(JsonElement arr, int i)
        {
            var el = arr[i];
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDouble(),
                JsonValueKind.String => double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
                _ => 0
            };
        }

        private static List<OrderBookEntry> ParseLevels(JsonElement levels)
        {
            var list = new List<OrderBookEntry>();
            foreach (var lvl in levels.EnumerateArray())
                list.Add(new OrderBookEntry(DblAt(lvl, 0), DblAt(lvl, 1)));
            return list;
        }

        private static string ParseOrderId(string body)
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("orderId", out var id) ? id.GetRawText() : "ORDER_FAILED:no orderId";
        }

        private static string Fmt(double v) => v.ToString("0.##########", CultureInfo.InvariantCulture);

        // Binance /depth only accepts a fixed set of limits.
        private static int SnapDepth(int limit)
        {
            int[] allowed = { 5, 10, 20, 50, 100, 500, 1000, 5000 };
            foreach (var a in allowed) if (limit <= a) return a;
            return 5000;
        }

        private static OrderType MapOrderType(string type) => type switch
        {
            "LIMIT"             => OrderType.Limit,
            "MARKET"            => OrderType.Market,
            "STOP_LOSS"         => OrderType.StopMarket,
            "STOP_LOSS_LIMIT"   => OrderType.StopLimit,
            "TAKE_PROFIT"       => OrderType.TakeProfitMarket,
            "TAKE_PROFIT_LIMIT" => OrderType.TakeProfitLimit,
            _                   => OrderType.Market
        };

        // Binance kline interval strings == the app's timeframe codes; validate
        // and fall back to 1h on anything unexpected.
        private static string MapInterval(string tf) => tf switch
        {
            "1m" or "3m" or "5m" or "15m" or "30m"
            or "1h" or "2h" or "4h" or "6h" or "8h" or "12h"
            or "1d" or "3d" or "1w" or "1M" => tf,
            _ => "1h"
        };
    }
}
