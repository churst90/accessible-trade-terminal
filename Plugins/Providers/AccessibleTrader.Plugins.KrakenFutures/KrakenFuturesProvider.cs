using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.KrakenFutures
{
    /// <summary>
    /// Kraken Futures — perpetual and fixed-maturity contracts.
    ///
    /// <para>
    /// **A separate plugin from Kraken spot, deliberately.** It is a different host
    /// (<c>futures.kraken.com</c>), a different API version, a different signing
    /// algorithm, and — the part that decides the question — **separate API keys**,
    /// generated in a different place. Folding it into the spot plugin as a
    /// sub-type would mean one credential slot for two credentials, and the first
    /// symptom would be authentication failures the user could not explain.
    /// </para>
    ///
    /// <para>
    /// It is also a genuinely different product: positions here are contracts with
    /// leverage, funding and liquidation, not holdings. The capability flags say so
    /// rather than inheriting spot's.
    /// </para>
    ///
    /// <para>
    /// **The demo environment is a first-class target**, not an afterthought.
    /// <c>demo-futures.kraken.com</c> is identical in structure and takes paper
    /// funds, so the whole plugin can be exercised end to end without money and —
    /// importantly — without the account verification that gates real funding.
    /// Set <c>Environment</c> to Paper on the key profile to use it.
    /// </para>
    /// </summary>
    public class KrakenFuturesProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        private const string LiveHost = "https://futures.kraken.com";
        private const string DemoHost = "https://demo-futures.kraken.com";

        // The signature covers the path WITHOUT this prefix even though the request
        // carries it. Kept as two constants so the difference is impossible to
        // collapse by accident.
        private const string ApiRoot   = "/derivatives/api/v3";
        private const string SignRoot  = "/api/v3";

        private readonly HttpClient _http;
        private readonly Subject<OrderUpdate> _orderUpdates = new();

        private string _apiKey = "";
        private string _apiSecret = "";
        private bool _useDemo;

        public KrakenFuturesProvider()
        {
            // Both hosts are allow-listed because a Paper key profile routes to the
            // demo venue, which is a different subdomain rather than a flag.
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "KrakenFutures",
                allowedHosts: new[] { "futures.kraken.com", "demo-futures.kraken.com" });
        }

        private string Host => _useDemo ? DemoHost : LiveHost;

        // ── IProviderPlugin ───────────────────────────────────────────────────

        public override string Name => "KrakenFutures";
        public override string Description => "Kraken Futures — perpetual and fixed-maturity contracts";

        /// <summary>
        /// Declared from what this plugin actually does, checked against the static
        /// audit. <c>Leverage</c> and <c>FuturesTrading</c> are real here in a way
        /// they are not on the spot plugin; <c>MarginTrading</c> is absent because
        /// this is a derivatives venue rather than a lending one, which is the same
        /// distinction MEXC has.
        /// </summary>
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.Leverage | ProviderCapabilities.FuturesTrading |
            ProviderCapabilities.Shorting | ProviderCapabilities.ReduceOnly |
            ProviderCapabilities.PostOnly | ProviderCapabilities.TimeInForce;

        public override T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            if (typeof(T) == typeof(ITradingProvider))    return this as T;
            if (typeof(T) == typeof(IOrderBookProvider))  return this as T;
            return null;
        }

        public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;          // public market data needs none
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => false;      // REST polling for now; WS is a later slice
        public override ProviderEnvironment Environment => _useDemo ? ProviderEnvironment.Paper : ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 5000;
        public override List<string> NativelySupportedTimeframes =>
            new() { "1m", "5m", "15m", "30m", "1h", "4h", "12h", "1d", "1w" };

        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var k)) _apiKey = k;
            if (config.TryGetValue("ApiSecret", out var s)) _apiSecret = s;
            // A Paper profile routes to the demo venue, which is a real environment
            // with its own keys — not a simulation we run.
            if (config.TryGetValue("Environment", out var e))
                _useDemo = string.Equals(e, "Paper", StringComparison.OrdinalIgnoreCase);
        }

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey)) return (true, "No API key provided (public market data only)");
            try
            {
                var json = await GetPrivateAsync("/accounts").ConfigureAwait(false);
                return json["result"]?.ToString() == "success"
                    ? (true, $"API key validated against {(_useDemo ? "the demo" : "the live")} futures venue")
                    : (false, ErrorOf(json) ?? "Kraken Futures rejected the credentials");
            }
            catch (Exception ex) { return (false, $"Kraken Futures key check failed: {ex.Message}"); }
        }

        // ── Market data ───────────────────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Futures" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Futures")
        {
            try
            {
                var json = JObject.Parse(await _http.GetStringAsync($"{Host}{ApiRoot}/instruments").ConfigureAwait(false));
                return (json["instruments"] as JArray ?? new JArray())
                    // Delisted contracts still appear; offering one would produce a
                    // chart that can never load and an order that can never fill.
                    .Where(i => i["tradeable"]?.Value<bool>() == true)
                    .Select(i => i["symbol"]?.ToString().ToUpperInvariant())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => s!)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                SurfaceError($"Kraken Futures instruments failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return new List<string>();
            }
        }

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(
            MarketDataRequest request)
        {
            var bars = new List<Ohlcv>();
            var vols = new List<(long, double)>();
            try
            {
                // Candles live on a DIFFERENT api from trading — /api/charts/v1 rather
                // than /derivatives/api/v3 — and are public, so no signing here.
                string res = MapResolution(request.Timeframe);
                string url = $"{Host}/api/charts/v1/trade/{request.Symbol.ToLowerInvariant()}/{res}";

                var json = JObject.Parse(await _http.GetStringAsync(url).ConfigureAwait(false));
                foreach (var c in json["candles"] as JArray ?? new JArray())
                {
                    // Times arrive in milliseconds here, unlike the spot API's seconds.
                    long ms = c["time"]?.Value<long>() ?? 0;
                    var t = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                    double o = D(c["open"]), h = D(c["high"]), l = D(c["low"]), cl = D(c["close"]), v = D(c["volume"]);
                    bars.Add(new Ohlcv(t, o, h, l, cl, v));
                    vols.Add((ms / 1000, v));
                }

                if (request.Limit > 0 && bars.Count > request.Limit)
                {
                    bars = bars.Skip(bars.Count - request.Limit).ToList();
                    vols = vols.Skip(vols.Count - request.Limit).ToList();
                }
            }
            catch (Exception ex)
            {
                SurfaceError($"Kraken Futures candles failed for {request.Symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
            }
            return (bars, vols);
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
            string symbol, int limit = 10)
        {
            try
            {
                var json = JObject.Parse(await _http
                    .GetStringAsync($"{Host}{ApiRoot}/orderbook?symbol={Uri.EscapeDataString(symbol.ToLowerInvariant())}")
                    .ConfigureAwait(false));
                var book = json["orderBook"];
                return (Side(book?["bids"] as JArray, limit), Side(book?["asks"] as JArray, limit));
            }
            catch (Exception ex)
            {
                SurfaceError($"Kraken Futures order book failed for {symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        private static List<OrderBookEntry> Side(JArray? rows, int limit) =>
            (rows ?? new JArray()).Take(limit)
                .Select(r => new OrderBookEntry(D(r[0]), D(r[1])))
                .ToList();

        // ── IOrderBookProvider ────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth).ConfigureAwait(false);
            // Sequence 0: the REST snapshot carries no sequence number, and inventing
            // one would let a consumer believe it could order updates against it.
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) =>
            Observable.Empty<OrderBookUpdate>();   // websocket streaming is a later slice

        // ── ITradingProvider ──────────────────────────────────────────────────

        public bool IsConnected => !string.IsNullOrEmpty(_apiKey);
        public override double MaxLeverage => 50.0;
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        // Kraken Futures has no order-event push in this plugin yet, so the order
        // service must poll rather than wait for a stream that will never emit.
        public bool SupportsOrderEventStreaming => false;

        public async Task<List<Balance>> GetBalancesAsync()
        {
            var json = await GetPrivateAsync("/accounts").ConfigureAwait(false);
            Throw(json, "account balances");

            var list = new List<Balance>();
            if (json["accounts"] is JObject accounts)
            {
                foreach (var acct in accounts.Properties())
                {
                    // Multi-collateral accounts hold several currencies; single-collateral
                    // ones report one balance for the whole account.
                    if (acct.Value?["balances"] is JObject balances)
                        foreach (var b in balances.Properties())
                        {
                            double v = D(b.Value);
                            if (v > 0) list.Add(new Balance(b.Name.ToUpperInvariant(), v, 0));
                        }
                }
            }
            return list;
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            var json = await GetPrivateAsync("/openpositions").ConfigureAwait(false);
            Throw(json, "open positions");

            return (json["openPositions"] as JArray ?? new JArray()).Select(p =>
            {
                double size = D(p["size"]);
                // "short" means a negative position; the API reports size unsigned.
                bool isShort = string.Equals(p["side"]?.ToString(), "short", StringComparison.OrdinalIgnoreCase);
                double qty = isShort ? -size : size;
                double entry = D(p["price"]);
                return new Position(
                    p["symbol"]?.ToString().ToUpperInvariant() ?? "",
                    qty, entry, qty * entry, D(p["unrealizedFunding"]),
                    Leverage: 1.0,
                    // The venue's own number, never one we compute. Liquidation is the
                    // figure that must never be optimistic, and theirs is the one that
                    // will actually be acted on.
                    LiquidationPrice: D(p["liquidationThreshold"]));
            }).ToList();
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            var json = await GetPrivateAsync("/openorders").ConfigureAwait(false);
            Throw(json, "open orders");

            return (json["openOrders"] as JArray ?? new JArray())
                .Where(o => symbol == null ||
                            string.Equals(o["symbol"]?.ToString(), symbol, StringComparison.OrdinalIgnoreCase))
                .Select(o => new OpenOrder(
                    o["order_id"]?.ToString() ?? "",
                    o["symbol"]?.ToString().ToUpperInvariant() ?? "",
                    string.Equals(o["side"]?.ToString(), "sell", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Sell : OrderSide.Buy,
                    MapOrderType(o["orderType"]?.ToString()),
                    D(o["unfilledSize"]), D(o["limitPrice"]),
                    o["status"]?.ToString() ?? "open"))
                .ToList();
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (string.IsNullOrEmpty(_apiKey)) return "ORDER_FAILED:no Kraken Futures API key configured";

            var args = new List<(string, string)>
            {
                ("orderType", signal.Type switch
                {
                    OrderType.Limit          => "lmt",
                    OrderType.StopMarket     => "stp",
                    OrderType.StopLimit      => "stp",
                    OrderType.TakeProfitMarket or OrderType.TakeProfitLimit => "take_profit",
                    _                        => "mkt",
                }),
                ("symbol", signal.Symbol.ToLowerInvariant()),
                ("side", signal.Side == OrderSide.Buy ? "buy" : "sell"),
                ("size", signal.Quantity.ToString(CultureInfo.InvariantCulture)),
            };

            if (signal.Price is > 0) args.Add(("limitPrice", signal.Price.Value.ToString(CultureInfo.InvariantCulture)));
            if (signal.TriggerPrice is > 0) args.Add(("stopPrice", signal.TriggerPrice.Value.ToString(CultureInfo.InvariantCulture)));
            else if (signal.StopLoss is > 0 && signal.Type is OrderType.StopMarket or OrderType.StopLimit)
                args.Add(("stopPrice", signal.StopLoss.Value.ToString(CultureInfo.InvariantCulture)));

            if (signal.ReduceOnly) args.Add(("reduceOnly", "true"));
            // Kraken Futures expresses post-only as its own order type rather than a
            // flag, so a maker limit becomes "post" instead of "lmt".
            if (signal.PostOnly && signal.Type == OrderType.Limit)
            {
                args.RemoveAll(a => a.Item1 == "orderType");
                args.Insert(0, ("orderType", "post"));
            }
            if (!string.IsNullOrEmpty(signal.TimeInForce) &&
                signal.TimeInForce.Equals("IOC", StringComparison.OrdinalIgnoreCase))
                args.Add(("triggerSignal", "last"));

            try
            {
                var json = await PostPrivateAsync("/sendorder", args).ConfigureAwait(false);
                string? err = ErrorOf(json);
                if (err != null) return $"ORDER_FAILED:{err}";

                var status = json["sendStatus"];
                string? orderId = status?["order_id"]?.ToString();
                string? state = status?["status"]?.ToString();

                // "placed" is the only success state. Everything else — insufficient
                // margin, market suspended, would-not-reduce — arrives as HTTP 200
                // with a status string, so returning the id blindly would report a
                // rejected order as accepted.
                if (!string.Equals(state, "placed", StringComparison.OrdinalIgnoreCase))
                    return $"ORDER_FAILED:{state ?? "Kraken Futures did not place the order"}";

                return orderId ?? "ORDER_FAILED:Kraken Futures placed the order but returned no id";
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            try
            {
                var json = await PostPrivateAsync("/cancelorder", new[] { ("order_id", orderId) }).ConfigureAwait(false);
                return string.Equals(json["cancelStatus"]?["status"]?.ToString(), "cancelled",
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                SurfaceError($"Kraken Futures cancel failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
        }

        /// <summary>
        /// Kraken Futures sets leverage per SYMBOL through its own endpoint rather
        /// than per order. Returning the requested value without calling anything
        /// would be the no-op that the capability audit exists to catch.
        /// </summary>
        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            double want = Math.Clamp(leverage, 1, MaxLeverage);
            try
            {
                var json = await PostPrivateAsync("/leveragepreferences", new[]
                {
                    ("symbol", symbol.ToLowerInvariant()),
                    ("maxLeverage", want.ToString(CultureInfo.InvariantCulture)),
                }).ConfigureAwait(false);

                return ErrorOf(json) == null ? want : 1.0;
            }
            catch (Exception ex)
            {
                SurfaceError($"Kraken Futures set leverage failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return 1.0;
            }
        }

        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            var json = await GetPrivateAsync("/fills").ConfigureAwait(false);
            Throw(json, "fill history");

            return (json["fills"] as JArray ?? new JArray())
                .Where(f => symbol == null ||
                            string.Equals(f["symbol"]?.ToString(), symbol, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select(f => new TradeFill(
                    f["fill_id"]?.ToString() ?? "",
                    f["symbol"]?.ToString().ToUpperInvariant() ?? "",
                    string.Equals(f["side"]?.ToString(), "sell", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Sell : OrderSide.Buy,
                    D(f["size"]), D(f["price"]),
                    DateTime.TryParse(f["fillTime"]?.ToString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var t) ? t : DateTime.UtcNow,
                    OrderId: f["order_id"]?.ToString()))
                .ToList();
        }

        // ── Signed transport ──────────────────────────────────────────────────

        private async Task<JObject> GetPrivateAsync(string path)
        {
            using var req = Signed(HttpMethod.Get, path, "");
            var res = await _http.SendAsync(req).ConfigureAwait(false);
            return JObject.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private async Task<JObject> PostPrivateAsync(string path, IEnumerable<(string Key, string Value)> args)
        {
            // The SAME encoded string must be signed and sent. Building it once and
            // reusing it is the only way to be sure of that — encoding it twice
            // invites two spellings and a signature that never matches.
            string body = string.Join("&", args.Select(a =>
                $"{Uri.EscapeDataString(a.Key)}={Uri.EscapeDataString(a.Value)}"));

            using var req = Signed(HttpMethod.Post, path, body);
            req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
            var res = await _http.SendAsync(req).ConfigureAwait(false);
            return JObject.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        private HttpRequestMessage Signed(HttpMethod method, string path, string postData)
        {
            string nonce = KrakenFuturesAuth.Nonce();
            var req = new HttpRequestMessage(method, $"{Host}{ApiRoot}{path}");
            req.Headers.Add("APIKey", _apiKey);
            req.Headers.Add("Nonce", nonce);
            req.Headers.Add("Authent", KrakenFuturesAuth.Sign(postData, nonce, SignRoot + path, _apiSecret));
            return req;
        }

        /// <summary>
        /// Kraken Futures answers HTTP 200 with <c>result: error</c> for refusals,
        /// exactly as the spot API does with its error array. A caller reading only
        /// the status code sees success and empty data.
        /// </summary>
        private static string? ErrorOf(JObject json) =>
            string.Equals(json["result"]?.ToString(), "error", StringComparison.OrdinalIgnoreCase)
                ? json["error"]?.ToString() ?? "unspecified error"
                : null;

        private static void Throw(JObject json, string what)
        {
            string? err = ErrorOf(json);
            if (err == null) return;

            // A permission problem is fixable by the user at the venue; a fault is
            // not. The order service classifies on this distinction.
            if (err.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("apiLimitExceeded", StringComparison.OrdinalIgnoreCase) == false &&
                err.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
                err.Contains("key", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Kraken Futures refused {what}: {err}. Futures keys are separate from spot keys — "
                  + "check you generated one at futures.kraken.com.");

            throw new InvalidOperationException($"Kraken Futures could not return {what}: {err}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static string MapResolution(string timeframe) => timeframe.ToLowerInvariant() switch
        {
            "1m" => "1m", "5m" => "5m", "15m" => "15m", "30m" => "30m",
            "1h" => "1h", "4h" => "4h", "12h" => "12h",
            "1d" => "1d", "1w" => "1w",
            _    => "1h",
        };

        private static OrderType MapOrderType(string? t) => t?.ToLowerInvariant() switch
        {
            "lmt" or "post" => OrderType.Limit,
            "stp"           => OrderType.StopMarket,
            "take_profit"   => OrderType.TakeProfitMarket,
            _               => OrderType.Market,
        };

        private static double D(JToken? t) =>
            double.TryParse(t?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

        // ── Connection lifecycle ──────────────────────────────────────────────
        //
        // REST-only for now: every read is a fresh request, so there is no session
        // to open, keep or tear down. Declared explicitly rather than left to a
        // default so it is obvious this is a decision and not an omission — and
        // SupportsLiveUpdates is false to match, so nothing waits on a stream that
        // will never emit.

        public override Task EnsureConnectedAsync() => Task.CompletedTask;

        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) =>
            Task.CompletedTask;

        public override Task DisconnectAsync() => Task.CompletedTask;
    }
}
