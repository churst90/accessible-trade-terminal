using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using AccessibleTrader.Sdk.Enums;
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
    /// Scope: EQUITY orders (MARKET / LIMIT / STOP / STOP_LIMIT) plus native
    /// bracket protection since 2026-07-22 — an entry with SL/TP becomes a
    /// TRIGGER order whose child is the exit (or an OCO pair of exits), enforced
    /// by Schwab server-side. Multi-leg OPTION strategies and the WebSocket
    /// streamer (ACCT_ACTIVITY) remain out of scope; order updates arrive via
    /// the order-status polling fallback.
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

        // The stream above is a dead subject — no streaming implementation yet.
        // Declaring it lets GeneralOrderService poll order status so fills still
        // announce. Flip to true when the real event stream lands.
        public bool SupportsOrderEventStreaming => false;

        // Schwab ALWAYS polls (no stream), and its transaction records don't carry
        // the placed order id, so the poller must resolve via the authoritative
        // GET /orders/{id} lookup — otherwise a filled order announces as cancelled.
        public bool SupportsOrderStatusQuery => true;

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
        // Schwab has no push feed: PollLatestCandleAsync re-fetches the last
        // /pricehistory candle every 15-30 s and pushes the WHOLE re-sent bar,
        // whose Volume is the interval's running total — cumulative-bar semantics.
        // Without this the consolidator adds each poll's total to the running bar
        // and a 1-minute bar accumulates roughly 4x its true volume before the
        // next REST refresh corrects it.
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle =>
            AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 20000;
        public override ProviderCapabilities Capabilities => ProviderCapabilities.Brackets;

        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
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

            // Cancel AND dispose. A cancelled-but-undisposed source holds its registration list
            // and any armed timer handle, and a symbol switch does this hundreds of times over a
            // long session. Binance already got this right; Schwab and Tradier did not.
            _pollCts?.Cancel();
            _pollCts?.Dispose();
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
                // Transport faults belong to the pipeline's retry + circuit breaker
                // (see TransportFailure). Swallowing them here is what made all three
                // Polly layers above this call decorative and left an empty chart as
                // the only symptom of a dead network. Everything else — a malformed
                // payload, an unknown symbol, an auth refusal — is still ours to eat.
                if (TransportFailure.IsTransient(ex)) throw;
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
            //
            // The empty list is correct and the SILENCE was not: an empty dropdown reads as
            // "Schwab has no symbols" or "the fetch broke", and for a screen reader user it is
            // indistinguishable from either. Say which it is.
            SurfaceError(
                "Schwab does not publish a symbol list — type the symbol you want instead of "
              + "picking one from the list.");
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
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab GetOrderBookAsync failed for {symbol} ({ex.GetType().Name}): {ex.Message}");
                return (new(), new());
            }
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
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
                    // Only the account orders are routed to — see IsTradedAccount. Emitting a
                    // "Cash"/"Equity"/"Buying Power" row per account under identical asset
                    // names made the dashboard's numbers belong to no particular account.
                    if (!IsTradedAccount(sec)) continue;

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

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var url  = $"{TraderV1}/accounts?fields=positions";
                var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                var arr  = JArray.Parse(body);

                var positions = new List<Position>();
                foreach (var account in arr)
                {
                    var sec = account["securitiesAccount"];
                    // Only the account orders are routed to — see IsTradedAccount. Showing an
                    // IRA's positions next to a Sell button that trades the brokerage account
                    // is the shape of this bug.
                    if (!IsTradedAccount(sec)) continue;

                    var posArr = sec?["positions"] as JArray;
                    if (posArr == null) continue;

                    foreach (var p in posArr)
                    {
                        var symbol   = p["instrument"]?["symbol"]?.ToString() ?? "";
                        double longQ = p["longQuantity"]?.Value<double>()  ?? 0;
                        double shortQ= p["shortQuantity"]?.Value<double>() ?? 0;
                        double qty   = longQ - shortQ;
                        if (Math.Abs(qty) < 1e-9) continue;

                        double avgPrice   = p["averagePrice"]?.Value<double>() ?? 0;
                        double marketVal  = p["marketValue"]?.Value<double>()  ?? 0;
                        // Unrealized P&L is the OPEN P&L for the held position, not
                        // the day P&L — a position held >1 day would otherwise report
                        // the wrong number. Long/short open P&L are mutually exclusive
                        // per position; fall back to day P&L only if neither is present.
                        double openPnl = (p["longOpenProfitLoss"]?.Value<double>() ?? 0)
                                       + (p["shortOpenProfitLoss"]?.Value<double>() ?? 0);
                        double unrealized = openPnl != 0
                            ? openPnl
                            : (p["currentDayProfitLoss"]?.Value<double>() ?? 0);

                        // Signed: consumers derive long/short from the sign; Abs
                        // made a short read as a long in risk math and speech.
                        positions.Add(new Position(symbol, qty, avgPrice, marketVal, unrealized));
                    }
                }
                return positions;
            }).ConfigureAwait(false);
        }

        /// <summary>Fill history via /transactions?types=TRADE, last 30 days
        /// (History tab parity — returned the interface default empty until
        /// 2026-07-22). Each TRADE transaction's first priced equity transfer
        /// item carries the fill; amount sign is the side.</summary>
        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                string start = Uri.EscapeDataString(DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                string end = Uri.EscapeDataString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                var url = $"{TraderV1}/accounts/{_primaryAccountHash}/transactions?startDate={start}&endDate={end}&types=TRADE";
                var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                var arr = JArray.Parse(body);

                var fills = new List<TradeFill>();
                foreach (var txn in arr)
                {
                    var item = (txn["transferItems"] as JArray)?
                        .FirstOrDefault(i => (i["price"]?.Value<double>() ?? 0) > 0);
                    if (item == null) continue;
                    string sym = item["instrument"]?["symbol"]?.ToString() ?? "";
                    if (symbol != null && !sym.Equals(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                    double amount = item["amount"]?.Value<double>() ?? 0;
                    fills.Add(new TradeFill(
                        txn["activityId"]?.ToString() ?? Guid.NewGuid().ToString("N"),
                        sym,
                        amount >= 0 ? OrderSide.Buy : OrderSide.Sell,
                        Math.Abs(amount),
                        item["price"]?.Value<double>() ?? 0,
                        txn["tradeDate"]?.Value<DateTime>() ?? DateTime.MinValue,
                        Math.Abs((txn["transferItems"] as JArray)?
                            .Where(i => (i["feeType"]?.ToString() ?? "").Length > 0)
                            .Sum(i => i["cost"]?.Value<double>() ?? 0) ?? 0)));
                }
                return fills.OrderByDescending(f => f.FilledAt).Take(limit).ToList();
            });
        }

        /// <summary>Authoritative single-order status via GET /accounts/{hash}/orders/{id}.
        /// Returns null only when the order cannot be identified; a transient failure THROWS —
        /// see the comment in the body and <see cref="ITradingProvider.GetOrderStatusAsync"/>.</summary>
        public async Task<OrderStatusSnapshot?> GetOrderStatusAsync(string orderId, string? symbol = null)
        {
            if (!IsConnected || string.IsNullOrEmpty(orderId)) return null;
            // No catch: the order poller counts consecutive failures and gives up
            // with a spoken warning. Returning null here read as "still resolving"
            // and turned a dead endpoint into a silent infinite retry.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var url  = $"{TraderV1}/accounts/{_primaryAccountHash}/orders/{orderId}";
                var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                return MapOrderToSnapshot(JObject.Parse(body));
            }).ConfigureAwait(false);
        }

        /// <summary>Maps a Schwab order object to a status snapshot. A WORKING order
        /// with some filled quantity is reported as PartiallyFilled; fill price is
        /// the quantity-weighted average across execution legs. Internal for testing
        /// (the transport needs OAuth).</summary>
        internal static OrderStatusSnapshot MapOrderToSnapshot(JObject order)
        {
            string status = order["status"]?.ToString() ?? "";
            var state = status switch
            {
                "FILLED"                  => PolledOrderState.Filled,
                "CANCELED" or "CANCELLED" => PolledOrderState.Cancelled,
                // EXPIRED is not a cancel — nobody asked; the order timed out.
                "EXPIRED"                 => PolledOrderState.Expired,
                // REPLACED means the order is STILL LIVE under a new id. The old
                // REPLACED→Cancelled squash told the trader they were flat; they
                // re-entered and were double-sized with the original resting.
                "REPLACED"                => PolledOrderState.Replaced,
                "REJECTED"                => PolledOrderState.Rejected,
                // Schwab's remaining vocabulary (WORKING, QUEUED, ACCEPTED,
                // PENDING_*, AWAITING_*) is all working-family — keep polling.
                _                         => PolledOrderState.Working,
            };

            var leg   = (order["orderLegCollection"] as JArray)?.FirstOrDefault();
            string instr = leg?["instruction"]?.ToString() ?? "BUY";
            var side  = instr.StartsWith("SELL", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy;
            string sym = leg?["instrument"]?["symbol"]?.ToString() ?? "";

            double filled    = order["filledQuantity"]?.Value<double>()    ?? 0;
            double remaining = order["remainingQuantity"]?.Value<double>() ?? 0;

            double weighted = 0, qtySum = 0;
            foreach (var act in (order["orderActivityCollection"] as JArray) ?? new JArray())
                foreach (var ex in (act["executionLegs"] as JArray) ?? new JArray())
                {
                    double q = ex["quantity"]?.Value<double>() ?? 0;
                    double p = ex["price"]?.Value<double>() ?? 0;
                    weighted += p * q; qtySum += q;
                }
            double avgFill = qtySum > 0 ? weighted / qtySum : 0;

            if (state == PolledOrderState.Working && filled > 0)
                state = PolledOrderState.PartiallyFilled;

            return new OrderStatusSnapshot(state, side, sym, filled, avgFill, remaining);
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            // No catch: a failed read must throw so the order service can classify
            // it (ProviderResult.FromException). Returning an empty result here is
            // what re-armed the reconciliation incident ProviderResult.cs documents —
            // a transient 502 read as "account flat" and overwrote the snapshot.
            return await _rateLimiter.ExecuteAsync(async () =>
            {
                var url  = $"{TraderV1}/accounts/{_primaryAccountHash}/orders?status=WORKING";
                var body = await SendWithAuthAsync(HttpMethod.Get, url, null).ConfigureAwait(false);
                return ParseOpenOrders(JArray.Parse(body), symbol);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Bracket orders are TREES: a TRIGGER entry whose children are the
        /// protective exits (possibly wrapped in a leg-less OCO node). Walks the
        /// whole tree so resting/pending protection is visible in the Orders tab,
        /// not just the entry. Internal for direct testing — the transport needs
        /// OAuth. Stops carry stopPrice, not price; without the fallback every
        /// resting stop displayed (and spoke) as 0.
        /// </summary>
        internal static List<OpenOrder> ParseOpenOrders(JArray orders, string? symbol)
        {
            var list = new List<OpenOrder>();

            void Walk(JToken node)
            {
                var status = node["status"]?.ToString() ?? "";
                var legs = node["orderLegCollection"] as JArray;
                if (legs != null && legs.Count > 0
                    && status is "WORKING" or "PENDING_ACTIVATION" or "QUEUED" or "ACCEPTED")
                {
                    var leg = legs[0];
                    var sym = leg["instrument"]?["symbol"]?.ToString() ?? "";
                    if (symbol == null || string.Equals(sym, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        var instr = leg["instruction"]?.ToString() ?? "BUY";
                        var side = instr.StartsWith("SELL", StringComparison.OrdinalIgnoreCase) ? OrderSide.Sell : OrderSide.Buy;
                        double price = node["price"]?.Value<double>() ?? node["stopPrice"]?.Value<double>() ?? 0;
                        list.Add(new OpenOrder(
                            node["orderId"]?.ToString() ?? "",
                            sym, side,
                            MapSchwabOrderType(node["orderType"]?.ToString()),
                            leg["quantity"]?.Value<double>() ?? 0,
                            price,
                            status));
                    }
                }
                if (node["childOrderStrategies"] is JArray children)
                    foreach (var child in children) Walk(child);
            }

            foreach (var o in orders) Walk(o);
            return list;
        }

        /// <summary>The order id is the last path segment of the Location header
        /// (…/accounts/{hash}/orders/{orderId}). Schwab ids are numeric; anything
        /// else (an unexpected header shape, the literal "orders") is rejected so
        /// the poller is never handed a non-id to query with.</summary>
        internal static string? OrderIdFromLocation(string? location)
        {
            if (string.IsNullOrWhiteSpace(location)) return null;
            var last = location.TrimEnd('/').Split('/')[^1];
            return last.Length > 0 && last.All(char.IsDigit) ? last : null;
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            try
            {
                return await _rateLimiter.ExecuteOnceAsync(async () =>
                {
                    var order = BuildSchwabOrder(signal);
                    if (order == null) return "ORDER_FAILED:Unsupported order type";

                    var url  = $"{TraderV1}/accounts/{_primaryAccountHash}/orders";
                    var json = JsonConvert.SerializeObject(order);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Ownership of 'content' transfers to the HttpRequestMessage
                    // inside SendWithAuthCoreAsync, which disposes it along with the request.
                    var (body, location) = await SendWithAuthCoreAsync(HttpMethod.Post, url, content).ConfigureAwait(false);

                    // The normal success shape is 201 + empty body + the order id as
                    // the last segment of the Location header. A JSON body with an
                    // orderId is accepted too, but the header is the documented home.
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
                            // with empty 200s. Fall through to the Location header.
                        }
                    }
                    return OrderIdFromLocation(location) ?? "ORDER_SUBMITTED";
                }).ConfigureAwait(false);
            }
            catch (SchwabReauthRequiredException ex)
            {
                // Controlled exception — message is our own string, safe to surface.
                return $"ORDER_FAILED:{ex.Message}";
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"Schwab order error: {ex.GetType().Name}");
                return $"ORDER_FAILED:{ex.GetType().Name}";
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
            => (await SendWithAuthCoreAsync(method, url, content, ct).ConfigureAwait(false)).Body;

        private async Task<(string Body, string? Location)> SendWithAuthCoreAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct = default)
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

                // The status code goes in the PROPERTY, not just the message.
                // TransportFailure.IsTransient returns true for any HttpRequestException with
                // no StatusCode, so putting the code only in the text made a Schwab 401 or 404
                // read as a transient network fault: retried by the pipeline, counted against
                // the circuit breaker, and announced to the user as a connection problem
                // rather than "your session expired". RateLimiter.ShouldRetry was defeated the
                // same way. 429 genuinely IS transient and keeps the retry it wants.
                if (resp.StatusCode == (HttpStatusCode)429)
                    throw new HttpRequestException(
                        "Schwab rate limit (429) hit; backing off.", null, resp.StatusCode);

                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"Schwab {method} {url} → {(int)resp.StatusCode}: {body}", null, resp.StatusCode);

                // Order placement returns 201 with an EMPTY body; the new order id
                // exists ONLY in the Location header. Discarding it here is what
                // left every Schwab order id-less: the "ORDER_SUBMITTED" fallback
                // prefix-matches the error sentinel, so the fill poller and the
                // protective-order verification never started.
                return (body, resp.Headers.Location?.ToString());
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

        /// <summary>
        /// The plain account number of the account every order actually goes to.
        ///
        /// <para>Schwab addresses orders by an opaque <c>hashValue</c>, but the
        /// <c>/accounts?fields=positions</c> payload identifies each account by its plain
        /// <c>accountNumber</c>. This is the join between the two, and it exists because
        /// balances and positions used to be read from <b>every</b> account while
        /// <c>PlaceOrderAsync</c>, <c>CancelOrderAsync</c>, <c>GetOpenOrdersAsync</c>,
        /// <c>GetFillsAsync</c> and <c>GetOrderStatusAsync</c> all addressed
        /// <see cref="_primaryAccountHash"/> alone. A user holding a brokerage account and an
        /// IRA saw the IRA's positions in the dashboard, pressed sell, and the order went to
        /// whichever account Schwab happened to list first. Balances compounded it: "Cash",
        /// "Equity" and "Buying Power" were emitted once per account under identical asset
        /// names, so the dashboard summed or last-wrote them with no way to tell which was
        /// which.</para>
        ///
        /// <para>Until there is an account selector in the UI, the honest behaviour is for the
        /// account you can SEE to be the account you can TRADE. Reads are scoped here.</para>
        /// </summary>
        private string? PrimaryAccountNumber =>
            _accountHashes.FirstOrDefault(a => a.HashValue == _primaryAccountHash)?.AccountNumber;

        /// <summary>
        /// True when this <c>securitiesAccount</c> node is the one orders are routed to.
        /// When the account number cannot be resolved at all, no account matches and the
        /// caller reports nothing — an empty positions list is recoverable, a list mixing
        /// two accounts is not.
        /// </summary>
        private bool IsTradedAccount(JToken? securitiesAccount)
        {
            var number = PrimaryAccountNumber;
            if (string.IsNullOrEmpty(number)) return false;
            return string.Equals(
                securitiesAccount?["accountNumber"]?.ToString(), number, StringComparison.Ordinal);
        }

        // ── Order mapping ───────────────────────────────────────────────────

        /// <summary>Test seam: the bracket/order builder is where SL/TP were once
        /// silently dropped — BrokerParityTests pins its payload shapes.</summary>
        internal static SchwabOrderRequest? BuildSchwabOrderForTest(TradeSignal signal) => BuildSchwabOrder(signal);

        /// <summary>
        /// A price as Schwab must receive it: every digit the user chose, invariant culture.
        ///
        /// <para>These four call sites used <c>ToString("0.##")</c>. Schwab lists sub-dollar
        /// equities, which quote in $0.0001 increments under Reg NMS Rule 612 — a limit at
        /// 0.4567 was submitted at 0.46, two percent away from the level chosen, and anything
        /// under half a cent became "0.00". This is the same defect the repo already fixed on
        /// Bitstamp; that sweep did not reach Schwab.</para>
        ///
        /// <para><c>"R"</c>-style round-tripping via the default <c>ToString</c> keeps full
        /// precision. Rounding an order price is the venue's job, not ours: it knows the tick
        /// size for the instrument and we do not.</para>
        /// </summary>
        private static string Wire(double price) => price.ToString(CultureInfo.InvariantCulture);

        private static SchwabOrderRequest? BuildSchwabOrder(TradeSignal signal)
        {
            // Entry + protective legs → Schwab's native conditional tree:
            // TRIGGER entry whose child is the exit (or an OCO pair when both SL
            // and TP are given). Exchange-enforced — the protection exists even
            // if the terminal dies right after submit. Before 2026-07-22 SL/TP
            // on entries were SILENTLY DROPPED by this builder.
            if (signal.Type is OrderType.Market or OrderType.Limit
                && (signal.StopLoss is > 0 || signal.TakeProfit is > 0))
                return BuildBracket(signal);

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
                    order.Price = Wire(signal.Price.Value);
                    break;

                case OrderType.StopMarket when signal.StopLoss.HasValue:
                    order.OrderType = "STOP";
                    order.StopPrice = Wire(signal.StopLoss.Value);
                    break;

                case OrderType.StopLimit when signal.Price.HasValue && signal.StopLoss.HasValue:
                    order.OrderType = "STOP_LIMIT";
                    order.Price     = Wire(signal.Price.Value);
                    order.StopPrice = Wire(signal.StopLoss.Value);
                    break;

                default:
                    return null;
            }

            return order;
        }

        private static SchwabOrderRequest? BuildBracket(TradeSignal signal)
        {
            var entry = BuildSchwabOrder(signal with { StopLoss = null, TakeProfit = null });
            if (entry == null) return null;
            entry.OrderStrategyType = "TRIGGER";
            entry.Duration = "GTC"; // protective legs must outlive the session

            string exitInstruction = signal.Side == OrderSide.Buy ? "SELL" : "BUY";
            // The exit legs are the same instrument as the entry, so they carry the same
            // asset type. They used to be hardcoded "EQUITY" while the entry honoured
            // signal.SubType: a single-leg OPTION order with a stop or target built a TRIGGER
            // tree whose parent was OPTION and whose children claimed to be EQUITY on the same
            // symbol. Schwab rejects that tree — or, worse, accepts the parent and leaves the
            // user in an option position whose protective legs never armed. The class doc
            // scopes MULTI-leg options out; a single-leg option with a bracket reaches here.
            string exitAssetType =
                string.Equals(signal.SubType, "Options", StringComparison.OrdinalIgnoreCase) ? "OPTION" : "EQUITY";
            SchwabOrderRequest ExitLeg(string orderType, double price, bool stop)
            {
                var leg = new SchwabOrderRequest
                {
                    OrderType = orderType,
                    Session = "NORMAL",
                    Duration = "GTC",
                    OrderStrategyType = "SINGLE",
                    OrderLegCollection = new List<SchwabOrderLeg>
                    {
                        new()
                        {
                            Instruction = exitInstruction,
                            Quantity = signal.Quantity,
                            Instrument = new SchwabOrderInstrument
                            {
                                Symbol = signal.Symbol,
                                AssetType = exitAssetType,
                            },
                        },
                    },
                };
                string px = Wire(price);
                if (stop) leg.StopPrice = px; else leg.Price = px;
                return leg;
            }

            var exits = new List<SchwabOrderRequest>();
            if (signal.TakeProfit is > 0) exits.Add(ExitLeg("LIMIT", signal.TakeProfit.Value, stop: false));
            if (signal.StopLoss is > 0) exits.Add(ExitLeg("STOP", signal.StopLoss.Value, stop: true));

            entry.ChildOrderStrategies = exits.Count == 2
                ? new List<SchwabOrderRequest>
                {
                    new()
                    {
                        OrderStrategyType = "OCO",
                        OrderType = null, Session = null, Duration = null,
                        OrderLegCollection = null, // an OCO node has children, not legs
                        ChildOrderStrategies = exits,
                    },
                }
                : exits;
            return entry;
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
