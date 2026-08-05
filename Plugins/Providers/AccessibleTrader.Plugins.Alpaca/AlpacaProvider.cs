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
using AccessibleTrader.Sdk.Services;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Alpaca
{
    public class AlpacaProvider : BaseMarketDataProvider, ITradingProvider, IOrderBookProvider
    {
        private readonly HttpClient _httpClient;
        private string? _apiKey;
        private string? _apiSecret;
        private const string StockDataUrl = "https://data.alpaca.markets/v2";
        private const string CryptoDataUrl = "https://data.alpaca.markets/v1beta3/crypto";
        private string _tradingBaseUrl = "https://paper-api.alpaca.markets/v2";

        /// <summary>
        /// Which consolidated feed to ask for on STOCK bars. Alpaca's default when the parameter is
        /// omitted is <c>iex</c>, and that default was silently in force here — which matters more
        /// than it sounds. IEX is a single venue carrying roughly 2% of consolidated volume, its
        /// bars are sparse and its history only reaches 2022, whereas <c>sip</c> is the full
        /// consolidated tape back to 2016. A user charting SPY on 5-minute bars was getting the thin
        /// one with nothing anywhere saying so.
        /// <para>
        /// So the request asks for SIP and falls back ONCE to IEX if the account is not entitled,
        /// remembering the answer and saying so on the error stream. Accounts with SIP get the real
        /// tape; accounts without keep working. Neither is silent about which it got.
        /// </para>
        /// </summary>
        private string _stockFeed = "sip";
        private bool _feedDowngraded;

        // Rate limiter: Alpaca allows 200 requests/minute
        private readonly RateLimiter _rateLimiter = new(200, TimeSpan.FromMinutes(1));

        // Order update stream
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Order book streaming
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        // Live data WebSocket
        private ReconnectingWebSocket? _dataWs;

        // Trading update WebSocket
        private ReconnectingWebSocket? _tradeWs;

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
        // The stream sends completed one-shot bars (T=="b", per-minute), each
        // carrying its period's TOTAL volume — cumulative-bar semantics.
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle => AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment { get; } = ProviderEnvironment.Paper;
        public override int MaxBarsPerRequest => 10000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.L2 | ProviderCapabilities.Brackets;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.FiveMinutes, StandardTimeframes.FifteenMinutes,
            StandardTimeframes.OneHour, StandardTimeframes.OneDay, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public bool IsConnected => IsConfigured;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 1.0;

        public AlpacaProvider()
        {
            // Phase 4 Track B2 — allow-listed to Alpaca's REST hosts only.
            // data.alpaca.markets covers both stock + crypto data; api.alpaca.markets
            // is the live trading REST endpoint; paper-api.alpaca.markets is the
            // paper-trading REST endpoint. stream.data.alpaca.markets (WS) and
            // the two WS trading endpoints use ReconnectingWebSocket.
            _httpClient = PluginHostServices.CreateHttpClient(
                providerId:   "Alpaca",
                allowedHosts: new[]
                {
                    "data.alpaca.markets",
                    "api.alpaca.markets",
                    "paper-api.alpaca.markets",
                });
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider)) return this as T;
            if (typeof(T) == typeof(IOrderBookProvider)) return this as T;
            return null;
        }

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var key)) _apiKey = key;
            // Explicit override wins over the SIP-then-IEX probe, for accounts that know what they
            // have. Any value other than "sip" disables the one-time downgrade so a deliberate
            // choice is not second-guessed.
            if (config.TryGetValue("Feed", out var feed) && !string.IsNullOrWhiteSpace(feed))
            {
                _stockFeed = feed.Trim().ToLowerInvariant();
                _feedDowngraded = _stockFeed != "sip";
            }
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;

            if (config.TryGetValue("Environment", out var env) && env == "Live")
                _tradingBaseUrl = "https://api.alpaca.markets/v2";

            // NOTE: phase 4 Track B removed the DefaultRequestHeaders injection
            // that used to live here. Headers are now applied per-request via
            // ApplyAlpacaHeadersAsync which does a sign-time credential
            // checkout. Configure still populates _apiKey / _apiSecret so the
            // no-host-bridge fallback path (unit tests / CLI) continues to work.
        }

        // Sign-time credential checkout (phase 4 Track B). Prefers the
        // PluginHostServices.ApiKeys bridge; falls back to Configure-populated
        // fields so unit tests / CLI runs continue to work.
        private async Task<(string Key, string Secret)> CheckoutAlpacaCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("Alpaca").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("Alpaca: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("Alpaca: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        // Applies APCA-API-KEY-ID / APCA-API-SECRET-KEY headers to the shared
        // _httpClient from a fresh checkout. The rate limiter serializes REST
        // calls so DefaultRequestHeaders mutation is safe within the current
        // usage pattern.
        private async Task ApplyAlpacaHeadersAsync()
        {
            var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
            _httpClient.DefaultRequestHeaders.Remove("APCA-API-KEY-ID");
            _httpClient.DefaultRequestHeaders.Remove("APCA-API-SECRET-KEY");
            _httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKey);
            _httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecret);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (!IsConfigured) return (false, "API key not configured");
            try
            {
                await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                var response = await _httpClient.GetAsync($"{_tradingBaseUrl}/account");
                if (response.IsSuccessStatusCode)
                    return (true, "API key validated successfully");
                var body = await response.Content.ReadAsStringAsync();
                return (false, $"Key validation failed ({response.StatusCode}): {body}");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        public override Task EnsureConnectedAsync()
        {
            if (IsConfigured) _connectionStateStream.OnNext(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            if (_currentSymbol == symbol && _currentTimeframe == timeframe && _dataWs?.IsConnected == true) return;

            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;
            _currentMarket    = market;

            // Set up data stream with auto-reconnect
            if (_dataWs != null) { await _dataWs.DisconnectAsync(); _dataWs.Dispose(); }

            bool isCrypto = market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            string wsUrl = isCrypto
                ? "wss://stream.data.alpaca.markets/v1beta3/crypto/us"
                : "wss://stream.data.alpaca.markets/v2/stocks";

            // Crypto streams key bars by the slashed pair (BTC/USD), same as REST.
            var cleanSymbol = isCrypto ? ToAlpacaCryptoSymbol(symbol) : CleanSymbol(symbol);

            _dataWs = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    try
                    {
                        var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                        var auth = new JObject { ["action"] = "auth", ["key"] = apiKey, ["secret"] = apiSecret };
                        await ws.SendAsync(auth.ToString());
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Alpaca data WS auth failed: {ex.Message}");
                        return;
                    }
                    // Small delay to allow auth to process
                    await Task.Delay(500);
                    var sub = new JObject { ["action"] = "subscribe", ["bars"] = new JArray { cleanSymbol } };
                    await ws.SendAsync(sub.ToString());
                })
                .OnMessage(HandleDataMessage)
                .OnError(err => _errorStream.OnNext($"Alpaca data: {err}"))
                .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected));

            await _dataWs.ConnectAsync();
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start trading stream if not already running
            if (IsConfigured && _tradeWs?.IsConnected != true)
                await StartTradeStreamAsync();
        }

        private void HandleDataMessage(string msg)
        {
            try
            {
                var arr = JArray.Parse(msg);
                foreach (var item in arr)
                {
                    string? ev = item["T"]?.ToString();
                    if (ev == "b")
                    {
                        var bar = new Ohlcv(
                            item["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.UtcNow,
                            item["o"]?.Value<double>() ?? 0,
                            item["h"]?.Value<double>() ?? 0,
                            item["l"]?.Value<double>() ?? 0,
                            item["c"]?.Value<double>() ?? 0,
                            item["v"]?.Value<double>() ?? 0);
                        if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
                            && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime))
                            continue;
                        _liveStream.OnNext(bar);
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Alpaca] Malformed feed frame skipped: {ex.GetType().Name}"); }
        }

        private async Task StartTradeStreamAsync()
        {
            if (_tradeWs != null) { await _tradeWs.DisconnectAsync(); _tradeWs.Dispose(); }

            string wsUrl = _tradingBaseUrl.Contains("paper")
                ? "wss://paper-api.alpaca.markets/stream"
                : "wss://api.alpaca.markets/stream";

            _tradeWs = new ReconnectingWebSocket(wsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .OnConnected(async ws =>
                {
                    try
                    {
                        var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                        var auth = new JObject
                        {
                            ["action"] = "authenticate",
                            ["data"]   = new JObject { ["key_id"] = apiKey, ["secret_key"] = apiSecret }
                        };
                        await ws.SendAsync(auth.ToString());
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Alpaca trade WS auth failed: {ex.Message}");
                        return;
                    }
                    await Task.Delay(500);
                    var listen = new JObject
                    {
                        ["action"] = "listen",
                        ["data"]   = new JObject { ["streams"] = new JArray { "trade_updates" } }
                    };
                    await ws.SendAsync(listen.ToString());
                })
                .OnMessage(HandleTradeMessage)
                .OnError(err => _errorStream.OnNext($"Alpaca trade stream: {err}"));

            await _tradeWs.ConnectAsync();
        }

        private void HandleTradeMessage(string msg)
        {
            try
            {
                var json = JObject.Parse(msg);
                if (json["stream"]?.ToString() == "trade_updates")
                {
                    var data = json["data"];
                    if (data == null) return;
                    var evt   = data["event"]?.ToString();
                    var order = data["order"];
                    if (order == null) return;

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
                        double.TryParse(order["qty"]?.ToString(), out double tq) ? tq - fq : 0,
                        status, false, false, DateTime.UtcNow));
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Alpaca] Malformed feed frame skipped: {ex.GetType().Name}"); }
        }

        public override async Task DisconnectAsync()
        {
            if (_dataWs != null) { await _dataWs.DisconnectAsync(); _dataWs.Dispose(); _dataWs = null; }
            if (_tradeWs != null) { await _tradeWs.DisconnectAsync(); _tradeWs.Dispose(); _tradeWs = null; }

            _currentSymbol    = null;
            _currentTimeframe = null;
            _currentMarket    = null;

            // Drop references to the API key/secret so a crash dump after
            // disconnect can't recover them.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new List<Ohlcv>(), new List<(long, double)>());
            bool isCrypto = request.Market.Contains("Crypto", StringComparison.OrdinalIgnoreCase);
            // Alpaca crypto v1beta3 requires the SLASHED pair (BTC/USD) in BOTH the
            // request and the response key; stocks use the bare ticker. Stripping the
            // slash (CleanSymbol) silently returned an empty crypto chart.
            var stockSym  = CleanSymbol(request.Symbol);
            var cryptoSym = ToAlpacaCryptoSymbol(request.Symbol);
            var timeframe = MapTimeframe(request.Timeframe);
            int limit = Math.Min(request.Limit, MaxBarsPerRequest);

            string BuildUrl(string? pageToken)
            {
                string u = isCrypto
                    ? $"{CryptoDataUrl}/us/bars?symbols={Uri.EscapeDataString(cryptoSym)}&timeframe={timeframe}"
                    : $"{StockDataUrl}/stocks/{stockSym}/bars?timeframe={timeframe}&feed={_stockFeed}&adjustment=all";
                u += $"&limit={Math.Min(limit, 10000)}";
                if (request.Since.HasValue) u += $"&start={DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";
                if (request.Until.HasValue) u += $"&end={DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).ToString("yyyy-MM-ddTHH:mm:ssZ")}";
                if (!string.IsNullOrEmpty(pageToken)) u += $"&page_token={Uri.EscapeDataString(pageToken!)}";
                return u;
            }

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var ohlcvList = new List<Ohlcv>();
                    string? pageToken = null;

                    // Alpaca caps a single response at 10,000 bars and hands back a
                    // next_page_token for the rest. That token used to be ignored, so a request for
                    // deep intraday history stopped at the first page and reported success — the
                    // chart simply began later than asked, with no error. The page cap here is a
                    // runaway guard, not a data limit: 20 pages is 200,000 bars, well past anything
                    // a chart or a study asks for in one call.
                    for (int page = 0; page < 20; page++)
                    {
                        await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                        var response = await _httpClient.GetStringAsync(BuildUrl(pageToken));
                        var json = JObject.Parse(response);
                        JArray? bars = isCrypto ? json["bars"]?[cryptoSym] as JArray : json["bars"] as JArray;

                        if (bars == null)
                        {
                            var msg = json["message"]?.ToString();

                            // Not entitled to SIP: downgrade once, tell the user which feed they are
                            // now on, and retry. Falling back silently would leave them looking at
                            // one venue's 2% of volume believing it was the market.
                            if (!isCrypto && _stockFeed == "sip" && !_feedDowngraded
                                && !string.IsNullOrEmpty(msg)
                                && (msg!.Contains("subscription", StringComparison.OrdinalIgnoreCase)
                                 || msg.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
                                 || msg.Contains("permission", StringComparison.OrdinalIgnoreCase)))
                            {
                                _stockFeed = "iex";
                                _feedDowngraded = true;
                                _errorStream.OnNext(
                                    "Alpaca: this account is not entitled to the SIP consolidated feed. " +
                                    "Falling back to IEX, which carries a single venue's volume and only " +
                                    "reaches 2022.");
                                page--;                     // retry this page on the new feed
                                continue;
                            }

                            if (!string.IsNullOrEmpty(msg))
                                _errorStream.OnNext($"Alpaca data error for {request.Symbol}: {msg}");
                            break;
                        }

                        foreach (var b in bars)
                        {
                            ohlcvList.Add(new Ohlcv(
                                b["t"]?.Value<DateTime>().ToUniversalTime() ?? DateTime.MinValue,
                                b["o"]?.Value<double>() ?? 0,
                                b["h"]?.Value<double>() ?? 0,
                                b["l"]?.Value<double>() ?? 0,
                                b["c"]?.Value<double>() ?? 0,
                                b["v"]?.Value<double>() ?? 0));
                        }

                        pageToken = json["next_page_token"]?.Type == JTokenType.Null
                            ? null
                            : json["next_page_token"]?.ToString();
                        if (string.IsNullOrEmpty(pageToken) || ohlcvList.Count >= limit) break;
                    }

                    ohlcvList = ohlcvList.OrderBy(x => x.Date).ToList();
                    return (ohlcvList, ohlcvList.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca FetchOhlcvAsync failed for {request.Symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new List<Ohlcv>(), new List<(long, double)>());
            }
        }

        /// <summary>Alpaca crypto pair in the SLASHED form the v1beta3 API needs
        /// (BTC/USD). Re-inserts the quote separator the app's canonical symbols and
        /// CleanSymbol strip. Stocks never use this.</summary>
        internal static string ToAlpacaCryptoSymbol(string symbol)
        {
            var s = symbol.Replace("-", "/").ToUpperInvariant();
            if (s.Contains('/')) return s;                       // already BASE/QUOTE
            foreach (var q in new[] { "USDT", "USDC", "USD", "BTC", "ETH" })
                if (s.EndsWith(q) && s.Length > q.Length)
                    return string.Concat(s.AsSpan(0, s.Length - q.Length), "/", q);
            return s;
        }

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            if (!IsConfigured) return new List<string>();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = market == MarketType.Crypto
                        ? "https://api.alpaca.markets/v2/assets?asset_class=crypto&status=active"
                        : "https://api.alpaca.markets/v2/assets?asset_class=us_equity&status=active&tradable=true";

                    var (apiKey, apiSecret) = await CheckoutAlpacaCredentialsAsync().ConfigureAwait(false);
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Add("APCA-API-KEY-ID",     apiKey);
                    req.Headers.Add("APCA-API-SECRET-KEY", apiSecret);

                    var response = await _httpClient.SendAsync(req);
                    response.EnsureSuccessStatusCode();

                    var json   = await response.Content.ReadAsStringAsync();
                    var assets = JArray.Parse(json);
                    return assets
                        .Select(a => a["symbol"]?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .OrderBy(s => s)
                        .ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca GetSymbolsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new List<string>();
            }
        }

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => new List<string> { "Spot" };
        public override async Task<List<string>> GetSupportedTimeframesAsync() => NativelySupportedTimeframes;

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            var cleanSymbol = CleanSymbol(symbol);

            // Determine if this is a crypto or stock symbol
            bool isCrypto = _currentMarket?.Contains("Crypto", StringComparison.OrdinalIgnoreCase) == true;

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    if (isCrypto)
                    {
                        string url = $"{CryptoDataUrl}/us/orderbooks?symbols={cleanSymbol}";
                        var response = await _httpClient.GetStringAsync(url);
                        var json = JObject.Parse(response);
                        var book = json["orderbooks"]?[cleanSymbol];
                        if (book == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        var bids = (book["b"] as JArray)?.Take(limit)
                            .Select(b => new OrderBookEntry(double.Parse(b["p"]!.ToString()), double.Parse(b["s"]!.ToString())))
                            .ToList() ?? new();
                        var asks = (book["a"] as JArray)?.Take(limit)
                            .Select(a => new OrderBookEntry(double.Parse(a["p"]!.ToString()), double.Parse(a["s"]!.ToString())))
                            .ToList() ?? new();
                        return (bids, asks);
                    }
                    else
                    {
                        // Stock: use latest quote (NBBO) as a 1-level order book
                        string url = $"{StockDataUrl}/stocks/{cleanSymbol}/quotes/latest";
                        var response = await _httpClient.GetStringAsync(url);
                        var json = JObject.Parse(response);
                        var quote = json["quote"];
                        if (quote == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                        double bidPx  = quote["bp"]?.Value<double>() ?? 0;
                        double bidSz  = quote["bs"]?.Value<double>() ?? 0;
                        double askPx  = quote["ap"]?.Value<double>() ?? 0;
                        double askSz  = quote["as"]?.Value<double>() ?? 0;

                        var bids = bidPx > 0 ? new List<OrderBookEntry> { new(bidPx, bidSz) } : new List<OrderBookEntry>();
                        var asks = askPx > 0 ? new List<OrderBookEntry> { new(askPx, askSz) } : new List<OrderBookEntry>();
                        return (bids, asks);
                    }
                });
            }
            catch { return (new(), new()); }
        }

        // ── IOrderBookProvider ──────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) => _orderBookSubject.AsObservable();

        // ── ITradingProvider ─────────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
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
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca GetBalancesAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync($"{_tradingBaseUrl}/positions");
                    var arr = JArray.Parse(response);
                    return arr.Select(p => new Position(
                        p["symbol"]?.ToString() ?? "",
                        p["qty"]?.Value<double>() ?? 0,
                        p["avg_entry_price"]?.Value<double>() ?? 0,
                        p["market_value"]?.Value<double>() ?? 0,
                        p["unrealized_pl"]?.Value<double>() ?? 0
                    )).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca GetPositionsAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        /// <summary>Fill history via /account/activities?activity_types=FILL
        /// (History tab parity — returned the interface default empty until 2026-07-22).</summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConnected) return new();
            try
            {
                // Must go through the rate limiter AND set auth headers (both mutate the
                // shared HttpClient) — the old direct GetStringAsync had no credentials
                // on a fresh provider and 401'd, and raced other signed calls.
                var response = await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    return await _httpClient.GetStringAsync(
                        $"{_tradingBaseUrl}/account/activities?activity_types=FILL&page_size={Math.Clamp(limit, 1, 100)}");
                }).ConfigureAwait(false);
                var arr = JArray.Parse(response);
                var fills = new List<TradeFill>();
                foreach (var a in arr)
                {
                    string sym = a["symbol"]?.ToString() ?? "";
                    if (symbol != null && !sym.Equals(symbol.Replace("/", ""), StringComparison.OrdinalIgnoreCase)
                        && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                    fills.Add(new TradeFill(
                        a["id"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                        sym,
                        (a["side"]?.ToString() ?? "buy").StartsWith("sell", StringComparison.OrdinalIgnoreCase)
                            ? OrderSide.Sell : OrderSide.Buy,
                        a["qty"]?.Value<double>() ?? 0,
                        a["price"]?.Value<double>() ?? 0,
                        a["transaction_time"]?.Value<DateTime>() ?? DateTime.MinValue,
                        0,
                        a["order_id"]?.ToString()));
                }
                return fills.OrderByDescending(f => f.FilledAt).Take(limit).ToList();
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca GetFillsAsync failed ({ex.GetType().Name})");
                return new();
            }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConfigured) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    string url = $"{_tradingBaseUrl}/orders?status=open";
                    if (!string.IsNullOrEmpty(symbol)) url += $"&symbols={symbol}";
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.GetStringAsync(url);
                    var arr = JArray.Parse(response);
                    return arr.Select(o => new OpenOrder(
                        o["id"]?.ToString() ?? "",
                        o["symbol"]?.ToString() ?? "",
                        o["side"]?.ToString() == "buy" ? OrderSide.Buy : OrderSide.Sell,
                        MapAlpacaOrderType(o["type"]?.ToString() ?? "market"),
                        o["qty"]?.Value<double>() ?? 0,
                        o["limit_price"]?.Value<double>() ?? 0,
                        o["status"]?.ToString() ?? "",
                        o["stop_price"]?.Value<double>(),
                        null
                    )).ToList();
                });
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca GetOpenOrdersAsync failed ({ex.GetType().Name}): {ex.Message}");
                return new();
            }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConfigured) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var body = new JObject
                    {
                        ["symbol"]        = signal.Symbol,
                        ["qty"]           = signal.Quantity.ToString("F4"),
                        ["side"]          = signal.Side == OrderSide.Buy ? "buy" : "sell",
                        ["time_in_force"] = "gtc"
                    };

                    // Determine order type
                    if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                    {
                        body["type"] = "stop";
                        body["stop_price"] = signal.StopLoss.Value;
                    }
                    else if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                    {
                        body["type"] = "stop_limit";
                        body["stop_price"] = signal.StopLoss.Value;
                        body["limit_price"] = signal.Price.Value;
                    }
                    else if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                    {
                        body["type"] = "limit";
                        body["limit_price"] = signal.Price.Value;
                    }
                    else
                    {
                        body["type"] = "market";
                    }

                    ApplyProtectiveLegs(body, signal);

                    if (!string.IsNullOrEmpty(signal.ClientOid))
                        body["client_order_id"] = signal.ClientOid;

                    var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    var response = await _httpClient.PostAsync($"{_tradingBaseUrl}/orders", content);
                    var responseStr = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) return $"ORDER_FAILED:{responseStr}";
                    var json = JObject.Parse(responseStr);
                    return json["id"]?.ToString() ?? "ORDER_SUBMITTED";
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Alpaca order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        /// <summary>
        /// Attach the stop loss and take profit to an order body, as Alpaca wants them.
        ///
        /// <para>
        /// <b>This is where bracket atomicity comes from, and it is worth being explicit about.</b>
        /// Alpaca accepts the entry and both protective legs as a SINGLE order with
        /// <c>order_class</c> set, so the broker itself guarantees there is never a moment where the
        /// entry exists and the stop does not. Verified against a paper account on 2026-08-03: one
        /// POST returned a parent plus two legs already in <c>held</c> status. That is a stronger
        /// guarantee than the application could build on top of a broker that attached legs
        /// separately, where a failed second call leaves a naked position — which is what
        /// <c>GeneralOrderService.VerifyProtectiveOrdersAsync</c> exists to catch elsewhere.
        /// </para>
        ///
        /// <para>
        /// Extracted from the request builder so the three rules below can be tested without a
        /// network call. Each of them is a rejection waiting to happen if it is got wrong:
        /// </para>
        /// <list type="number">
        ///   <item><b>Legs only when the entry is market or limit.</b> A stop or stop-limit ENTRY is
        ///         itself a protective order — Alpaca rejects a bracket parented by a stop, and in
        ///         that case <c>signal.StopLoss</c> is the entry trigger rather than a child leg.
        ///         Passing it as a leg would both fail and mean something different.</item>
        ///   <item><b><c>bracket</c> for two legs, <c>oto</c> for one.</b> A one-leg
        ///         <c>bracket</c> is rejected outright.</item>
        ///   <item><b>Stop legs carry <c>stop_price</c>, target legs carry <c>limit_price</c>.</b>
        ///         The two are not interchangeable and swapping them is silently wrong in the
        ///         dangerous direction — a stop posted as a limit does not protect anything.</item>
        /// </list>
        /// </summary>
        internal static void ApplyProtectiveLegs(JObject body, TradeSignal signal)
        {
            bool entryIsMarketOrLimit = signal.Type is OrderType.Market or OrderType.Limit;
            if (!entryIsMarketOrLimit) return;
            if (!signal.StopLoss.HasValue && !signal.TakeProfit.HasValue) return;

            bool both = signal.StopLoss.HasValue && signal.TakeProfit.HasValue;
            body["order_class"] = both ? "bracket" : "oto";

            if (signal.StopLoss.HasValue)
                body["stop_loss"] = new JObject { ["stop_price"] = signal.StopLoss.Value };
            if (signal.TakeProfit.HasValue)
                body["take_profit"] = new JObject { ["limit_price"] = signal.TakeProfit.Value };
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConfigured) return false;
            try
            {
                var response = await _rateLimiter.ExecuteAsync(async () =>
                {
                    await ApplyAlpacaHeadersAsync().ConfigureAwait(false);
                    return await _httpClient.DeleteAsync($"{_tradingBaseUrl}/orders/{orderId}");
                });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Alpaca CancelOrderAsync failed for {orderId} ({ex.GetType().Name}): {ex.Message}");
                return false;
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        private static OrderType MapAlpacaOrderType(string type) => type switch
        {
            "limit"       => OrderType.Limit,
            "stop"        => OrderType.StopMarket,
            "stop_limit"  => OrderType.StopLimit,
            _             => OrderType.Market
        };

        private string MapTimeframe(string tf) => tf switch
        {
            "1m"  => "1Min",
            "5m"  => "5Min",
            "15m" => "15Min",
            "1h"  => "1Hour",
            "1d"  => "1Day",
            "1w"  => "1Week",
            "1M"  => "1Month",
            _     => "1Hour"
        };

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                _dataWs?.Dispose();
                _tradeWs?.Dispose();
                _orderUpdateSubject?.Dispose();
                _orderBookSubject?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
