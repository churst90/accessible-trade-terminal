using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Schwab
{
    /// <summary>
    /// Charles Schwab Trader API provider — US equities &amp; options trading
    /// plus market-data (OHLCV, quotes).
    ///
    /// Authentication is OAuth2 authorization-code flow. The user supplies a
    /// <c>ClientId</c> (ApiKey), <c>ClientSecret</c> (ApiSecret), and
    /// optionally a <c>RefreshToken</c> (Passphrase slot). If no refresh token
    /// is available, a host UI must call
    /// <see cref="SchwabOAuthService.RunAuthorizationCodeFlowAsync"/> to obtain
    /// one. Refresh tokens live for 7 days; access tokens for 30 minutes.
    ///
    /// Rate limit: 120 requests per minute shared across all endpoints.
    ///
    /// This provider is v1 — single-leg EQUITY orders (MARKET / LIMIT / STOP /
    /// STOP_LIMIT) only. Multi-leg option strategies, conditional orders, and
    /// the WebSocket streamer are explicitly out of scope.
    /// </summary>
    public sealed class SchwabProvider : BaseMarketDataProvider, ITradingProvider
    {
        private const string ApiBase       = "https://api.schwabapi.com";
        private const string TraderV1      = ApiBase + "/trader/v1";
        private const string MarketDataV1  = ApiBase + "/marketdata/v1";

        private readonly HttpClient        _http;
        private readonly SchwabOAuthService _oauth;
        private readonly RateLimiter       _rateLimiter = new(120, TimeSpan.FromMinutes(1));

        private string? _clientId;
        private string? _clientSecret;
        private string? _redirectUri;

        // Cached account hashes. Schwab requires the hashed ID on every
        // /trader/v1/accounts/{hash}/... endpoint — we look them up once and
        // reuse them for the session.
        private readonly List<SchwabAccountNumber> _accountHashes = new();
        private string? _primaryAccountHash;

        // Order-update stream — v1 has no WebSocket wiring, so it stays empty.
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Polling state for live OHLCV updates. The Schwab WebSocket streamer
        // is intentionally deferred: we poll the last candle on an interval so
        // the existing LiveStream contract is still honoured.
        private CancellationTokenSource? _pollCts;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        public override string Name          => "Schwab";
        public override string Description   => "Charles Schwab — US Stocks & Options Trading";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Stock, MarketType.Options };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey  => true;
        public override bool IsConfigured    => !string.IsNullOrEmpty(_clientId)
                                             && !string.IsNullOrEmpty(_clientSecret)
                                             && _oauth.HasRefreshToken;
        public override bool SupportsLiveUpdates => true;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 20000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.Brackets;

        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => false;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => false;
        public override double MaxLeverage          => 1.0;

        public bool IsConnected => IsConfigured && !string.IsNullOrEmpty(_primaryAccountHash);

        public override List<string> NativelySupportedTimeframes => new()
        {
            StandardTimeframes.OneMinute,     StandardTimeframes.FiveMinutes,
            "10m",                             StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneDay,
            StandardTimeframes.OneWeek,       StandardTimeframes.OneMonth,
        };

        public SchwabProvider()
        {
            // Phase 4 Track B2 — allow-listed to api.schwabapi.com, which
            // covers the trading endpoints (/trader/v1/*), the market-data
            // endpoints (/marketdata/v1/*), AND the OAuth authorize/token
            // exchange that SchwabOAuthService performs through the same
            // HttpClient. One allow-list entry covers all three uses.
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "Schwab",
                allowedHosts: new[] { "api.schwabapi.com" });
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _oauth = new SchwabOAuthService(_http);
        }

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider))    return this as T;
            return null;
        }

        // ── Configuration ───────────────────────────────────────────────────

        public override void Configure(Dictionary<string, string> config)
        {
            // Host calls Configure with ApiKey / ApiSecret / Passphrase — map
            // them onto Schwab's OAuth vocabulary.
            //   ApiKey     → ClientId
            //   ApiSecret  → ClientSecret
            //   Passphrase → (optional) pre-existing refresh token. Normally
            //                empty; the host UI runs the auth-code flow and
            //                the token is persisted on disk.
            if (config.TryGetValue("ApiKey",      out var key)) _clientId     = key;
            if (config.TryGetValue("ApiSecret",   out var sec)) _clientSecret = sec;
            if (config.TryGetValue("RedirectUri", out var uri)) _redirectUri  = uri;

            _oauth.Configure(_clientId ?? "", _clientSecret ?? "", _redirectUri);

            if (config.TryGetValue("Passphrase", out var refresh) && !string.IsNullOrWhiteSpace(refresh))
                _oauth.SeedRefreshTokenIfMissing(refresh);
        }

        /// <summary>
        /// Kicks off an interactive browser-based authorization. Intended to
        /// be invoked by a host settings UI button ("Sign in to Schwab").
        /// </summary>
        public Task<SchwabTokenResponse> BeginAuthorizationAsync(
            Action<string>? openBrowser = null,
            CancellationToken ct = default)
            => _oauth.RunAuthorizationCodeFlowAsync(openBrowser, ct);

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                return (false, "Schwab client id / secret not configured.");
            if (!_oauth.HasRefreshToken)
                return (false, "No refresh token. Run Schwab authorization flow first.");

            try
            {
                await RefreshAccountHashesAsync().ConfigureAwait(false);
                return (true, $"Schwab connected. Accounts: {_accountHashes.Count}, primary: {MaskAccount(_accountHashes.FirstOrDefault()?.AccountNumber)}");
            }
            catch (SchwabReauthRequiredException ex)
            {
                return (false, ex.Message);
            }
            catch (Exception ex)
            {
                return (false, $"Schwab validation failed: {ex.Message}");
            }
        }

        // ── Connection / subscription (polling-based live updates) ──────────

        public override async Task EnsureConnectedAsync()
        {
            if (!IsConfigured) return;
            if (string.IsNullOrEmpty(_primaryAccountHash))
                await RefreshAccountHashesAsync().ConfigureAwait(false);
            _connectionStateStream.OnNext(ConnectionState.Connected);
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync().ConfigureAwait(false);
            if (_currentSymbol == symbol && _currentTimeframe == timeframe) return;

            _pollCts?.Cancel();
            _currentSymbol    = symbol;
            _currentTimeframe = timeframe;
            _pollCts          = new CancellationTokenSource();
            _ = Task.Run(() => PollLatestCandleAsync(symbol, timeframe, _pollCts.Token));
        }

        private async Task PollLatestCandleAsync(string symbol, string timeframe, CancellationToken ct)
        {
            var interval = MapTimeframeToPollInterval(timeframe);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var req = new MarketDataRequest("Stock", symbol, timeframe, Limit: 2);
                    var (bars, _) = await FetchOhlcvAsync(req).ConfigureAwait(false);
                    var last = bars.LastOrDefault();
                    if (last.Date != default)
                        _liveStream.OnNext(last);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _errorStream.OnNext($"Schwab poll error: {ex.Message}");
                }

                try { await Task.Delay(interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        public override Task DisconnectAsync()
        {
            _pollCts?.Cancel();
            _currentSymbol    = null;
            _currentTimeframe = null;

            // Drop references to OAuth client credentials so a crash dump
            // after disconnect can't recover them. The OAuth refresh token
            // is held separately by SchwabOAuthService and persists through
            // SecureStorage / DPAPI — those paths are unaffected.
            ScrubCredentials(
                () => _clientId = null,
                () => _clientSecret = null,
                () => _redirectUri = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
            return Task.CompletedTask;
        }

        // ── Market data ─────────────────────────────────────────────────────

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            if (!IsConfigured) return (new(), new());

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var url = BuildPriceHistoryUrl(request);
                    var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                    var parsed = JsonConvert.DeserializeObject<SchwabPriceHistoryResponse>(body);
                    if (parsed?.Candles == null || parsed.Candles.Count == 0)
                        return (new List<Ohlcv>(), new List<(long, double)>());

                    var ohlcv = parsed.Candles
                        .Select(c =>
                        {
                            var date = DateTimeOffset.FromUnixTimeMilliseconds(c.Datetime).UtcDateTime;
                            return new Ohlcv(date, c.Open, c.High, c.Low, c.Close, c.Volume);
                        })
                        .OrderBy(x => x.Date)
                        .ToList();

                    int limit = request.Limit > 0 ? Math.Min(request.Limit, ohlcv.Count) : ohlcv.Count;
                    ohlcv = ohlcv.TakeLast(limit).ToList();

                    var vols = ohlcv
                        .Select(x => (new DateTimeOffset(x.Date, TimeSpan.Zero).ToUnixTimeMilliseconds(), x.Volume))
                        .ToList();

                    return (ohlcv, vols);
                }).ConfigureAwait(false);
            }
            catch (SchwabReauthRequiredException ex)
            {
                _errorStream.OnNext($"Schwab reauth required: {ex.Message}");
                return (new(), new());
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab fetch error: {ex.Message}");
                return (new(), new());
            }
        }

        private static string BuildPriceHistoryUrl(MarketDataRequest request)
        {
            // Schwab's pricehistory params are (periodType, period, frequencyType, frequency).
            // We translate our compact timeframe strings into the nearest supported combo.
            var (periodType, period, frequencyType, frequency) = MapTimeframeToSchwabParams(request.Timeframe);

            var sb = new StringBuilder();
            sb.Append(MarketDataV1).Append("/pricehistory");
            sb.Append("?symbol=").Append(Uri.EscapeDataString(request.Symbol));
            sb.Append("&periodType=").Append(periodType);
            sb.Append("&frequencyType=").Append(frequencyType);
            sb.Append("&frequency=").Append(frequency);
            sb.Append("&needExtendedHoursData=false");

            if (request.Since.HasValue)
                sb.Append("&startDate=").Append(request.Since.Value);
            else
                sb.Append("&period=").Append(period);

            if (request.Until.HasValue)
                sb.Append("&endDate=").Append(request.Until.Value);

            return sb.ToString();
        }

        /// <summary>
        /// Maps our compact timeframe string to Schwab's quad of
        /// (periodType, period, frequencyType, frequency).
        /// </summary>
        private static (string periodType, int period, string frequencyType, int frequency)
            MapTimeframeToSchwabParams(string tf) => tf switch
        {
            "1m"  => ("day",   10, "minute", 1),
            "5m"  => ("day",   10, "minute", 5),
            "10m" => ("day",   10, "minute", 10),
            "15m" => ("day",   10, "minute", 15),
            "30m" => ("day",   10, "minute", 30),
            "1d"  => ("year",   2, "daily",  1),
            "1w"  => ("year",  10, "weekly", 1),
            "1M"  => ("year",  20, "monthly",1),
            _     => ("day",   10, "minute", 5),
        };

        private static TimeSpan MapTimeframeToPollInterval(string tf) => tf switch
        {
            "1m"  => TimeSpan.FromSeconds(15),
            "5m"  => TimeSpan.FromSeconds(30),
            "10m" => TimeSpan.FromMinutes(1),
            "15m" => TimeSpan.FromMinutes(1),
            "30m" => TimeSpan.FromMinutes(2),
            "1d"  => TimeSpan.FromMinutes(5),
            _     => TimeSpan.FromMinutes(2),
        };

        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            // Schwab has no bulk-symbol-list endpoint. A host UI is expected
            // to collect a symbol string from the user directly.
            return Task.FromResult(new List<string>());
        }

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(market == MarketType.Options
                ? new List<string> { "Call", "Put" }
                : new List<string> { "Spot" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "10m", "15m", "30m", "1d", "1w", "1M" });

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            if (!IsConfigured) return (new(), new());
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var url  = $"{MarketDataV1}/quotes?symbols={Uri.EscapeDataString(symbol)}";
                    var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                    var json = JObject.Parse(body);

                    var quote = json[symbol]?["quote"];
                    if (quote == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());

                    double bid    = quote["bidPrice"]?.Value<double>() ?? 0;
                    double bidSz  = quote["bidSize"]?.Value<double>()  ?? 0;
                    double ask    = quote["askPrice"]?.Value<double>() ?? 0;
                    double askSz  = quote["askSize"]?.Value<double>()  ?? 0;

                    var bids = bid > 0 ? new List<OrderBookEntry> { new(bid, bidSz * 100) } : new();
                    var asks = ask > 0 ? new List<OrderBookEntry> { new(ask, askSz * 100) } : new();
                    return (bids, asks);
                }).ConfigureAwait(false);
            }
            catch { return (new(), new()); }
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var url  = $"{TraderV1}/accounts?fields=positions";
                    var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                    var arr  = JArray.Parse(body);

                    var results = new List<Balance>();
                    foreach (var account in arr)
                    {
                        var sec = account["securitiesAccount"];
                        if (sec == null) continue;

                        double equity      = sec["currentBalances"]?["equity"]?.Value<double>()              ?? 0;
                        double cash        = sec["currentBalances"]?["cashBalance"]?.Value<double>()         ?? 0;
                        double buyingPower = sec["currentBalances"]?["buyingPower"]?.Value<double>()         ?? 0;

                        results.Add(new("Cash",         cash,        0));
                        results.Add(new("Equity",       equity,      0));
                        results.Add(new("Buying Power", buyingPower, 0));
                    }
                    return results;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab balances error: {ex.Message}");
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
                    var url  = $"{TraderV1}/accounts?fields=positions";
                    var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                    var arr  = JArray.Parse(body);

                    var positions = new List<Position>();
                    foreach (var account in arr)
                    {
                        var posArr = account["securitiesAccount"]?["positions"] as JArray;
                        if (posArr == null) continue;

                        foreach (var p in posArr)
                        {
                            var symbol   = p["instrument"]?["symbol"]?.ToString() ?? "";
                            double longQ = p["longQuantity"]?.Value<double>()  ?? 0;
                            double shortQ= p["shortQuantity"]?.Value<double>() ?? 0;
                            double qty   = longQ - shortQ;
                            if (Math.Abs(qty) < 1e-9) continue;

                            double avgPrice   = p["averagePrice"]?.Value<double>()        ?? 0;
                            double marketVal  = p["marketValue"]?.Value<double>()         ?? 0;
                            double unrealized = p["currentDayProfitLoss"]?.Value<double>() ?? 0;

                            positions.Add(new Position(symbol, Math.Abs(qty), avgPrice, marketVal, unrealized));
                        }
                    }
                    return positions;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab positions error: {ex.Message}");
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
                    var url  = $"{TraderV1}/accounts/{_primaryAccountHash}/orders?status=WORKING";
                    var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                    var arr  = JArray.Parse(body);

                    var list = new List<OpenOrder>();
                    foreach (var o in arr)
                    {
                        var legs = o["orderLegCollection"] as JArray;
                        if (legs == null || legs.Count == 0) continue;
                        var leg = legs[0];

                        var sym   = leg["instrument"]?["symbol"]?.ToString() ?? "";
                        if (symbol != null && !string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var instr = leg["instruction"]?.ToString() ?? "BUY";
                        var side  = instr.StartsWith("SELL", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy;
                        var type  = MapSchwabOrderType(o["orderType"]?.ToString());

                        list.Add(new OpenOrder(
                            o["orderId"]?.ToString() ?? "",
                            sym,
                            side,
                            type,
                            leg["quantity"]?.Value<double>() ?? 0,
                            o["price"]?.Value<double>() ?? 0,
                            o["status"]?.ToString() ?? ""
                        ));
                    }
                    return list;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab orders error: {ex.Message}");
                return new();
            }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var order = BuildSchwabOrder(signal);
                    if (order == null) return "ORDER_FAILED:Unsupported order type";

                    var url  = $"{TraderV1}/accounts/{_primaryAccountHash}/orders";
                    var json = JsonConvert.SerializeObject(order);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Ownership of 'content' transfers to the HttpRequestMessage
                    // inside SendWithAuthAsync, which disposes it along with the request.
                    var body = await SendWithAuthAsync(HttpMethod.Post, url, content).ConfigureAwait(false);

                    // Schwab returns the new order id in the Location header,
                    // but SendWithAuthAsync surfaces the body. Fall back to a
                    // synthetic id if we can't parse one.
                    if (!string.IsNullOrEmpty(body))
                    {
                        try
                        {
                            var parsed = JObject.Parse(body);
                            var maybeId = parsed["orderId"]?.ToString();
                            if (!string.IsNullOrEmpty(maybeId)) return maybeId;
                        }
                        catch (JsonException)
                        {
                            // Schwab returned non-JSON body on success -- rare but seen
                            // with empty 200s. Fall through to the synthetic id.
                        }
                    }
                    return "ORDER_SUBMITTED";
                }).ConfigureAwait(false);
            }
            catch (SchwabReauthRequiredException ex)
            {
                return $"ORDER_FAILED:{ex.Message}";
            }
            catch (Exception ex)
            {
                return $"ORDER_FAILED:{ex.Message}";
            }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                await _rateLimiter.ExecuteAsync(async () =>
                {
                    var url = $"{TraderV1}/accounts/{_primaryAccountHash}/orders/{orderId}";
                    await SendWithAuthAsync(HttpMethod.Delete, url, null).ConfigureAwait(false);
                    return true;
                }).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab cancel error: {ex.Message}");
                return false;
            }
        }

        public Task<double> SetLeverageAsync(string symbol, double leverage) => Task.FromResult(1.0);

        // ── HTTP plumbing with automatic token refresh ──────────────────────

        /// <summary>
        /// Executes an authenticated request. On a 401 we refresh the access
        /// token exactly once and retry. On a 429 we throw so the surrounding
        /// <see cref="RateLimiter.ExecuteAsync{T}"/> retry logic can back off.
        ///
        /// Because an <see cref="HttpRequestMessage"/> can only be sent once
        /// (and disposes its content on dispose), the caller buffers the raw
        /// body bytes up-front and we build a fresh request/content on each
        /// attempt.
        /// </summary>
        private async Task<string> SendWithAuthAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct = default)
        {
            // Buffer the content into memory so we can rebuild it on retry.
            byte[]? bodyBytes      = null;
            string? contentType    = null;
            if (content != null)
            {
                bodyBytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                contentType = content.Headers.ContentType?.ToString();
                content.Dispose();
            }

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var token = await _oauth.GetValidAccessTokenAsync(ct).ConfigureAwait(false);

                using var req = new HttpRequestMessage(method, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (bodyBytes != null)
                {
                    var freshContent = new ByteArrayContent(bodyBytes);
                    if (!string.IsNullOrEmpty(contentType))
                        freshContent.Headers.TryAddWithoutValidation("Content-Type", contentType);
                    req.Content = freshContent;
                }

                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _oauth.InvalidateAccessToken();
                    await _oauth.RefreshAccessTokenAsync(ct).ConfigureAwait(false);
                    continue;
                }

                if (resp.StatusCode == (HttpStatusCode)429)
                    throw new HttpRequestException("Schwab rate limit (429) hit; backing off.");

                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"Schwab {method} {url} → {(int)resp.StatusCode}: {body}");

                return body;
            }

            throw new SchwabReauthRequiredException("Schwab request failed repeatedly with 401 after token refresh.");
        }

        private async Task RefreshAccountHashesAsync()
        {
            var url  = $"{TraderV1}/accounts/accountNumbers";
            var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
            var list = JsonConvert.DeserializeObject<List<SchwabAccountNumber>>(body) ?? new List<SchwabAccountNumber>();

            _accountHashes.Clear();
            _accountHashes.AddRange(list);
            _primaryAccountHash = _accountHashes.FirstOrDefault()?.HashValue;
        }

        // ── Order mapping ───────────────────────────────────────────────────

        private static SchwabOrderRequest? BuildSchwabOrder(TradeSignal signal)
        {
            var leg = new SchwabOrderLeg
            {
                Instruction = signal.Side == OrderSide.Buy ? "BUY" : "SELL",
                Quantity    = signal.Quantity,
                Instrument  = new SchwabOrderInstrument
                {
                    Symbol    = signal.Symbol,
                    AssetType = string.Equals(signal.SubType, "Options", StringComparison.OrdinalIgnoreCase) ? "OPTION" : "EQUITY",
                },
            };

            var order = new SchwabOrderRequest
            {
                Session           = "NORMAL",
                Duration          = "DAY",
                OrderStrategyType = "SINGLE",
                OrderLegCollection = new List<SchwabOrderLeg> { leg },
            };

            switch (signal.Type)
            {
                case OrderType.Market:
                    order.OrderType = "MARKET";
                    break;

                case OrderType.Limit when signal.Price.HasValue:
                    order.OrderType = "LIMIT";
                    order.Price = signal.Price.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    break;

                case OrderType.StopMarket when signal.StopLoss.HasValue:
                    order.OrderType = "STOP";
                    order.StopPrice = signal.StopLoss.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    break;

                case OrderType.StopLimit when signal.Price.HasValue && signal.StopLoss.HasValue:
                    order.OrderType = "STOP_LIMIT";
                    order.Price     = signal.Price.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    order.StopPrice = signal.StopLoss.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    break;

                default:
                    return null;
            }

            return order;
        }

        private static OrderType MapSchwabOrderType(string? type) => (type ?? "").ToUpperInvariant() switch
        {
            "LIMIT"      => OrderType.Limit,
            "STOP"       => OrderType.StopMarket,
            "STOP_LIMIT" => OrderType.StopLimit,
            _            => OrderType.Market,
        };

        private static string MaskAccount(string? account)
        {
            if (string.IsNullOrEmpty(account)) return "(none)";
            return account!.Length <= 4 ? "****" : "****" + account[^4..];
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _pollCts?.Cancel();
                _pollCts?.Dispose();
                _orderUpdateSubject?.Dispose();
                _oauth?.Dispose();
                _http?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
