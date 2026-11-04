using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.RegularExpressions;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Sdk.Trading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Plugins.Hyperliquid
{
    /// <summary>
    /// Hyperliquid — an on-chain perpetuals and spot exchange, for users outside the United States.
    ///
    /// <para>
    /// <b>Who may use it.</b> Hyperliquid's terms exclude "Restricted Persons": anyone residing in,
    /// located in or incorporated in the United States or Ontario, plus sanctioned regions. Market
    /// data is public and needs no account; TRADING is for users the terms permit. The plugin
    /// cannot tell where you are; that responsibility is yours.
    /// </para>
    ///
    /// <para>
    /// <b>Credentials.</b> Two values, in the two fields every venue has. <c>ApiKey</c> is your MAIN
    /// account address (0x…, 42 characters), which every read uses. <c>ApiSecret</c> is the private
    /// key of an AGENT wallet (also called an API wallet) that you created and approved at
    /// app.hyperliquid.xyz/API. An agent can place and cancel orders; it cannot withdraw. The
    /// plugin refuses a secret that is the main wallet's own key, so a mistaken paste of the key
    /// that CAN withdraw is stopped before it is ever used.
    /// </para>
    ///
    /// <para>
    /// <b>Practice.</b> A credential marked Paper signs against Hyperliquid's testnet
    /// (api.hyperliquid-testnet.xyz), a separate venue with its own faucet-funded accounts and
    /// its own agent approval. Anything not explicitly Live is testnet.
    /// </para>
    /// </summary>
    public class HyperliquidProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        private const string MainnetRest = "https://api.hyperliquid.xyz";
        private const string TestnetRest = "https://api.hyperliquid-testnet.xyz";
        private const string MainnetWs   = "wss://api.hyperliquid.xyz/ws";
        private const string TestnetWs   = "wss://api.hyperliquid-testnet.xyz/ws";

        /// <summary>candleSnapshot returns at most about this many candles per call.</summary>
        internal const int SnapshotCap = 5000;
        private const int MaxSnapshotPages = 10;

        /// <summary>
        /// How far past (or under) the middle price a market order's limit is placed. Hyperliquid has
        /// no market order; it is an IOC limit, and the limit is the worst price the fill may take.
        /// Matches the Python SDK's default.
        /// </summary>
        internal const double MarketSlippage = 0.05;

        private static readonly Regex AddressShape = new("^0x[0-9a-fA-F]{40}$", RegexOptions.Compiled);
        private static readonly Regex KeyShape = new("^(0x)?[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        private readonly HttpClient _http;
        private readonly Subject<OrderUpdate> _orderUpdates = new();
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();

        // Hyperliquid meters by IP weight: 1,200 a minute, most info reads 20, light reads 2, an
        // exchange action 1. Three lanes keep a candle backfill from starving an order, with the
        // sum of the worst cases under the cap.
        private readonly RateLimiter _heavyInfoLimiter = new(30, TimeSpan.FromMinutes(1));
        private readonly RateLimiter _lightInfoLimiter = new(200, TimeSpan.FromMinutes(1));
        private readonly RateLimiter _exchangeLimiter  = new(100, TimeSpan.FromMinutes(1));

        private string _account = "";     // main wallet address — what reads ask about
        private string _agentKey = "";    // agent wallet private key — what signs
        private string? _keyRefusal;      // why the supplied key will not be used, if it will not
        private bool _isMainnet = true;   // reads default to mainnet; trading needs a configured credential
        private bool _configured;
        private long _lastNonce;

        // Metadata cache: perps and spot pairs, rebuilt when a symbol misses.
        private readonly SemaphoreSlim _assetLock = new(1, 1);
        private List<AssetInfo> _assets = new();
        private Dictionary<string, AssetInfo> _byNorm = new();
        private Dictionary<string, AssetInfo> _perpByCoin = new(StringComparer.Ordinal);
        private DateTime _assetsLoadedAt = DateTime.MinValue;
        private static readonly TimeSpan AssetTtl = TimeSpan.FromMinutes(30);

        // Websocket state for the focused chart.
        private ReconnectingWebSocket? _ws;
        private string? _candleCoin;
        private string? _candleInterval;
        private string? _bookCoin;
        private string? _bookSymbol;
        private BarBucketConsolidator? _consolidator;
        private readonly object _wsLock = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _keyedSubscriptions = new();

        public HyperliquidProvider()
        {
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "Hyperliquid",
                allowedHosts: new[] { "api.hyperliquid.xyz", "api.hyperliquid-testnet.xyz" });
        }

        // ── IProviderPlugin ───────────────────────────────────────────────────

        public override string Name => "Hyperliquid";

        public override string Description =>
            "Hyperliquid — on-chain perpetuals and spot. Market data is public. Trading uses an agent "
          + "wallet (it cannot withdraw): put your MAIN account address in the API key field and the "
          + "AGENT wallet's private key in the secret field. Not available to residents of the United "
          + "States or Ontario under Hyperliquid's terms. A Paper profile uses the Hyperliquid testnet.";

        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.Leverage | ProviderCapabilities.FuturesTrading |
            ProviderCapabilities.IsolatedMargin | ProviderCapabilities.Shorting |
            ProviderCapabilities.ReduceOnly | ProviderCapabilities.PostOnly |
            ProviderCapabilities.TimeInForce | ProviderCapabilities.Brackets;

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
        public override bool SupportsLiveUpdates => true;
        // No TrailingStop flag: Hyperliquid has no trailing order, so the dashboard does not offer one.
        public override bool SupportsStopLoss => true;
        public override bool SupportsTakeProfit => true;
        public override ProviderEnvironment Environment => _isMainnet ? ProviderEnvironment.Live : ProviderEnvironment.Paper;
        public override int MaxBarsPerRequest => SnapshotCap;

        public override List<string> NativelySupportedTimeframes =>
            new() { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "8h", "12h", "1d", "3d", "1w", "1M" };

        // A candle message is the whole running bar, re-sent with cumulative volume.
        public override LiveTickStyle LiveTickStyle => LiveTickStyle.CumulativeBars;
        public override bool SupportsMultipleLiveSubscriptions => true;

        private string Rest => _isMainnet ? MainnetRest : TestnetRest;
        private string WsUrl => _isMainnet ? MainnetWs : TestnetWs;

        public override void Configure(Dictionary<string, string> config)
        {
            string address = config.TryGetValue(ProviderConfigKeys.ApiKey, out var k) ? (k ?? "").Trim() : "";
            string secret  = config.TryGetValue(ProviderConfigKeys.ApiSecret, out var s) ? (s ?? "").Trim() : "";

            // Fail-safe polarity: only an explicit Live is the real venue; a missing or unknown
            // environment is the testnet.
            bool live = ProviderConfigKeys.IsLive(config);
            if (_configured && live != _isMainnet) ResetAssets();   // the other venue numbers its assets itself
            _isMainnet = live;
            _configured = true;

            _account = address;
            _agentKey = secret;
            _keyRefusal = CheckCredential(address, secret);
        }

        /// <summary>
        /// The reason a supplied credential must not be used, or null. The one that matters most:
        /// a secret that is the MAIN wallet's own key. That key can withdraw everything; the whole
        /// design is that the terminal holds only an agent.
        /// </summary>
        internal static string? CheckCredential(string address, string secret)
        {
            if (address.Length == 0 && secret.Length == 0) return null;   // public data only
            if (!AddressShape.IsMatch(address))
                return "The API key field must hold your main Hyperliquid account address: 0x followed by 40 hex characters.";
            if (secret.Length == 0)
                return "Add the agent wallet's private key in the secret field to trade; the address alone gives read-only access.";
            if (!KeyShape.IsMatch(secret))
                return "The secret field must hold the agent wallet's private key: 64 hex characters, with or without 0x.";
            string derived;
            try { derived = HyperliquidSigner.AddressOf(secret); }
            catch (Exception) { return "The agent wallet key is not a valid secp256k1 private key."; }
            if (string.Equals(derived, address, StringComparison.OrdinalIgnoreCase))
                return "That private key belongs to your MAIN wallet, which can withdraw funds. Refusing to use it. "
                     + "Create an agent (API) wallet at app.hyperliquid.xyz/API, approve it, and paste the agent's key instead.";
            return null;
        }

        private bool CanSign => _keyRefusal == null && AddressShape.IsMatch(_account) && KeyShape.IsMatch(_agentKey);

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (_account.Length == 0 && _agentKey.Length == 0)
                return (true, "No credentials provided (public market data only)");
            if (_keyRefusal != null) return (false, _keyRefusal);
            if (!CanSign) return (false, "Both the account address and the agent key are needed to trade.");

            try
            {
                string agent = HyperliquidSigner.AddressOf(_agentKey);
                var agents = await InfoAsync(new JObject { ["type"] = "extraAgents", ["user"] = _account }, _lightInfoLimiter)
                    .ConfigureAwait(false) as JArray ?? new JArray();
                bool approved = agents.Any(a =>
                    string.Equals(a["address"]?.ToString(), agent, StringComparison.OrdinalIgnoreCase));
                string venue = _isMainnet ? "mainnet" : "testnet";
                return approved
                    ? (true, $"Agent wallet {agent} is approved for {_account} on Hyperliquid {venue}")
                    : (false, $"Agent wallet {agent} is not approved for {_account} on Hyperliquid {venue}. "
                            + "Approve it at app.hyperliquid.xyz/API (testnet: app.hyperliquid-testnet.xyz/API).");
            }
            catch (Exception ex) { return (false, $"Hyperliquid key check failed: {ex.Message}"); }
        }

        // ── Metadata ──────────────────────────────────────────────────────────

        private void ResetAssets()
        {
            _assets = new(); _byNorm = new(); _perpByCoin = new(StringComparer.Ordinal);
            _assetsLoadedAt = DateTime.MinValue;
        }

        private async Task EnsureAssetsAsync(bool force = false)
        {
            if (!force && _assets.Count > 0 && DateTime.UtcNow - _assetsLoadedAt < AssetTtl) return;
            await _assetLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && _assets.Count > 0 && DateTime.UtcNow - _assetsLoadedAt < AssetTtl) return;

                var meta = await InfoAsync(new JObject { ["type"] = "meta" }, _heavyInfoLimiter).ConfigureAwait(false) as JObject
                    ?? throw new InvalidOperationException("Hyperliquid meta was not an object.");
                var perps = HyperliquidWire.ParseMeta(meta);

                // Spot is best effort: a failure there must not take the perps down with it.
                var spot = new List<AssetInfo>();
                try
                {
                    if (await InfoAsync(new JObject { ["type"] = "spotMeta" }, _heavyInfoLimiter).ConfigureAwait(false) is JObject sm)
                        spot = HyperliquidWire.ParseSpotMeta(sm);
                }
                catch (Exception ex) when (!TransportFailure.IsTransient(ex))
                {
                    SurfaceError($"Hyperliquid spot metadata failed: {ex.Message}", ErrorSeverity.Low, ErrorCategory.Provider);
                }

                var all = perps.Concat(spot).ToList();
                var byNorm = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
                foreach (var a in all) byNorm.TryAdd(a.Norm, a);   // first (lowest id) wins on a duplicate name
                _perpByCoin = perps.ToDictionary(a => a.Coin, a => a, StringComparer.Ordinal);
                _byNorm = byNorm;
                _assets = all;
                _assetsLoadedAt = DateTime.UtcNow;
            }
            finally { _assetLock.Release(); }
        }

        internal AssetInfo? Find(string symbol, string? subType)
        {
            string n = HyperliquidWire.Normalise(symbol);
            if (n.Length == 0) return null;
            bool wantSpot = string.Equals(subType, "Spot", StringComparison.OrdinalIgnoreCase);
            var tries = wantSpot ? new[] { n, n + "USDC", n + "PERP" } : new[] { n, n + "PERP", n + "USDC" };
            foreach (var t in tries)
                if (_byNorm.TryGetValue(t, out var hit)) return hit;
            return null;
        }

        private async Task<AssetInfo?> ResolveAsync(string symbol, string? subType)
        {
            await EnsureAssetsAsync().ConfigureAwait(false);
            var hit = Find(symbol, subType);
            if (hit != null) return hit;
            await EnsureAssetsAsync(force: true).ConfigureAwait(false);   // a just-listed market
            return Find(symbol, subType);
        }

        public override string GetCanonicalSymbol(string symbol)
        {
            var hit = Find(symbol, null);
            return hit != null ? hit.Norm : HyperliquidWire.Normalise(symbol);
        }

        /// <summary>The highest leverage the exchange allows on one market (it runs from 3x to 40x or more).</summary>
        public async Task<int> GetMaxLeverageAsync(string symbol, string? subType = null)
        {
            var a = await ResolveAsync(symbol, subType).ConfigureAwait(false);
            return a?.MaxLeverage ?? 1;
        }

        // ── Market data ───────────────────────────────────────────────────────

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Futures", "Spot" });

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(NativelySupportedTimeframes);

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Futures")
        {
            try
            {
                await EnsureAssetsAsync().ConfigureAwait(false);
                bool spot = string.Equals(subType, "Spot", StringComparison.OrdinalIgnoreCase);
                return _assets.Where(a => a.IsSpot == spot)
                    .Select(a => a.Display)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid symbols failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                if (TransportFailure.IsTransient(ex)) throw;
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
                var asset = await ResolveAsync(request.Symbol, null).ConfigureAwait(false);
                if (asset == null)
                {
                    SurfaceError($"Hyperliquid has no market named {request.Symbol}.", ErrorSeverity.Medium, ErrorCategory.Provider);
                    return (bars, vols);
                }

                string interval = NativelySupportedTimeframes.Contains(request.Timeframe)
                    ? request.Timeframe : TimeframeUtility.GetBestBaseTimeframe(request.Timeframe, NativelySupportedTimeframes);
                var rows = await FetchCandlesAsync(asset.Coin, interval, request).ConfigureAwait(false);
                foreach (var (t, bar) in rows)
                {
                    bars.Add(bar);
                    vols.Add((t / 1000, bar.Volume));
                }
                (bars, vols) = WindowedBars.Apply(request, bars, vols, "Hyperliquid", request.Symbol, SurfaceWindowMiss);
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid candles failed for {request.Symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                if (TransportFailure.IsTransient(ex)) throw;
            }
            return (bars, vols);
        }

        /// <summary>
        /// Pages BACKWARD from the end of the window. When a window holds more than the cap,
        /// the exchange answers with the NEWEST candles (measured: a 7-day 1m request returned
        /// the latest ~5,100), so the next page asks for everything older than the oldest one
        /// received. It stops when a page comes back short (the start of the data or of the
        /// window), when enough bars are in hand for a limit-only request, or after
        /// <see cref="MaxSnapshotPages"/>. Hyperliquid keeps only about the latest 5,000 candles
        /// per interval, so an old Since on a fast interval ends early with what exists.
        /// </summary>
        private async Task<SortedDictionary<long, Ohlcv>> FetchCandlesAsync(string coin, string interval, MarketDataRequest request)
        {
            var byTime = new SortedDictionary<long, Ohlcv>();
            long intervalMs = Math.Max(1, TimeframeUtility.ToMilliseconds(interval));
            long end = request.Until ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long wanted = request.Limit > 0 ? request.Limit : 200;
            long start = Math.Max(0, request.Since ?? end - wanted * intervalMs);

            for (int page = 0; page < MaxSnapshotPages; page++)
            {
                var body = new JObject
                {
                    ["type"] = "candleSnapshot",
                    ["req"] = new JObject { ["coin"] = coin, ["interval"] = interval, ["startTime"] = start, ["endTime"] = end },
                };
                var rows = await InfoAsync(body, _heavyInfoLimiter).ConfigureAwait(false) as JArray ?? new JArray();
                if (rows.Count == 0) break;

                long oldest = long.MaxValue;
                foreach (var c in rows)
                {
                    if (!HyperliquidWire.TryParseCandle(c, out var bar, out long t)) continue;
                    byTime[t] = bar;
                    oldest = Math.Min(oldest, t);
                }

                bool capped = rows.Count >= SnapshotCap && oldest > start + intervalMs;
                if (!capped || oldest == long.MaxValue) break;
                if (!request.Since.HasValue && byTime.Count >= wanted) break;
                end = oldest - 1;
            }
            return byTime;
        }

        private void SurfaceWindowMiss(string message) =>
            SurfaceError(message, ErrorSeverity.Medium, ErrorCategory.Provider);

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
            string symbol, int limit = 10)
        {
            try
            {
                var asset = await ResolveAsync(symbol, null).ConfigureAwait(false);
                if (asset == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
                var json = await InfoAsync(new JObject { ["type"] = "l2Book", ["coin"] = asset.Coin }, _lightInfoLimiter)
                    .ConfigureAwait(false);
                return ParseBook(json as JObject, limit);
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid order book failed for {symbol}: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                if (TransportFailure.IsTransient(ex)) throw;
                return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
            }
        }

        /// <summary>l2Book levels are [bids, asks], each a list of {px, sz, n}, best first.</summary>
        internal static (List<OrderBookEntry> Bids, List<OrderBookEntry> Asks) ParseBook(JObject? book, int limit)
        {
            var levels = book?["levels"] as JArray;
            List<OrderBookEntry> Side(int i) =>
                (levels != null && levels.Count > i ? levels[i] as JArray ?? new JArray() : new JArray())
                    .Take(limit)
                    .Select(l => new OrderBookEntry(HyperliquidWire.D(l["px"]), HyperliquidWire.D(l["sz"])))
                    .ToList();
            return (Side(0), Side(1));
        }

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth).ConfigureAwait(false);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol) =>
            _orderBookSubject.AsObservable().Where(u => string.Equals(
                HyperliquidWire.Normalise(u.Symbol), HyperliquidWire.Normalise(symbol), StringComparison.Ordinal)
                || Find(symbol, null)?.Norm == HyperliquidWire.Normalise(u.Symbol));

        /// <summary>
        /// Funding rate, open interest, mark and oracle price for one perp, from
        /// <c>metaAndAssetCtxs</c>. The terminal's funding indicator reads a different feed today;
        /// this is the Hyperliquid source for it. Null for a spot pair or an unknown symbol.
        /// </summary>
        public async Task<HyperliquidAssetContext?> GetAssetContextAsync(string symbol)
        {
            var asset = await ResolveAsync(symbol, "Futures").ConfigureAwait(false);
            if (asset == null || asset.IsSpot) return null;
            var json = await InfoAsync(new JObject { ["type"] = "metaAndAssetCtxs" }, _heavyInfoLimiter).ConfigureAwait(false) as JArray;
            return ParseAssetContext(json, asset.AssetId, asset.Display);
        }

        internal static HyperliquidAssetContext? ParseAssetContext(JArray? reply, int index, string display)
        {
            if (reply is null || reply.Count < 2 || reply[1] is not JArray ctxs || index < 0 || index >= ctxs.Count) return null;
            var c = ctxs[index];
            return new HyperliquidAssetContext(display,
                FundingRate: HyperliquidWire.D(c["funding"]), OpenInterest: HyperliquidWire.D(c["openInterest"]),
                MarkPrice: HyperliquidWire.D(c["markPx"]), OraclePrice: HyperliquidWire.D(c["oraclePx"]),
                DayVolumeUsd: HyperliquidWire.D(c["dayNtlVlm"]));
        }

        // ── Live streaming ────────────────────────────────────────────────────

        public override Task EnsureConnectedAsync()
        {
            lock (_wsLock) { if (_ws != null) return Task.CompletedTask; }
            return OpenSocketAsync();
        }

        private async Task OpenSocketAsync()
        {
            ReconnectingWebSocket ws;
            lock (_wsLock)
            {
                if (_ws != null) return;
                ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                    .WithHeartbeatMessage("{\"method\":\"ping\"}")   // the server drops a quiet connection after 60 s
                    .OnError(err => _errorStream.OnNext($"Hyperliquid stream: {err}"))
                    .OnConnected(async w =>
                    {
                        _connectionStateStream.OnNext(ConnectionState.Connected);
                        await ResubscribeAsync(w).ConfigureAwait(false);
                    })
                    .OnDisconnected(() => _connectionStateStream.OnNext(ConnectionState.Disconnected))
                    .OnMessage(HandleSocketMessage);
                _ws = ws;
            }
            _connectionStateStream.OnNext(ConnectionState.Connecting);
            await ws.ConnectAsync().ConfigureAwait(false);
        }

        private async Task ResubscribeAsync(ReconnectingWebSocket w)
        {
            string? candleCoin, interval, bookCoin;
            lock (_wsLock) { candleCoin = _candleCoin; interval = _candleInterval; bookCoin = _bookCoin; }
            if (candleCoin != null && interval != null)
                await w.SendAsync(SubscribeMessage("subscribe", "candle", candleCoin, interval)).ConfigureAwait(false);
            if (bookCoin != null)
                await w.SendAsync(SubscribeMessage("subscribe", "l2Book", bookCoin, null)).ConfigureAwait(false);
        }

        internal static string SubscribeMessage(string method, string type, string coin, string? interval)
        {
            var sub = new JObject { ["type"] = type, ["coin"] = coin };
            if (interval != null) sub["interval"] = interval;
            return new JObject { ["method"] = method, ["subscription"] = sub }.ToString(Formatting.None);
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            var asset = await ResolveAsync(symbol, null).ConfigureAwait(false);
            if (asset == null)
            {
                SurfaceError($"Hyperliquid has no market named {symbol} to stream.", ErrorSeverity.Medium, ErrorCategory.Provider);
                return;
            }
            string interval = NativelySupportedTimeframes.Contains(timeframe)
                ? timeframe : TimeframeUtility.GetBestBaseTimeframe(timeframe, NativelySupportedTimeframes);

            await OpenSocketAsync().ConfigureAwait(false);   // no-op when already open

            string? oldCoin, oldInterval, oldBook;
            ReconnectingWebSocket? ws;
            lock (_wsLock)
            {
                ws = _ws;
                oldCoin = _candleCoin; oldInterval = _candleInterval; oldBook = _bookCoin;
                _candleCoin = asset.Coin; _candleInterval = interval;
                _bookCoin = asset.Coin; _bookSymbol = asset.Display;
                _consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle);
            }
            if (ws == null) return;

            if (oldCoin != null && oldInterval != null)
                await ws.SendAsync(SubscribeMessage("unsubscribe", "candle", oldCoin, oldInterval)).ConfigureAwait(false);
            if (oldBook != null && oldBook != asset.Coin)
                await ws.SendAsync(SubscribeMessage("unsubscribe", "l2Book", oldBook, null)).ConfigureAwait(false);

            await ws.SendAsync(SubscribeMessage("subscribe", "candle", asset.Coin, interval)).ConfigureAwait(false);
            if (oldBook != asset.Coin)
                await ws.SendAsync(SubscribeMessage("subscribe", "l2Book", asset.Coin, null)).ConfigureAwait(false);
        }

        private void HandleSocketMessage(string msg)
        {
            try
            {
                var root = JObject.Parse(msg);
                switch (root["channel"]?.ToString())
                {
                    case "candle":
                        foreach (var bar in ParseSocketCandles(root, out var coin, out var interval))
                        {
                            Ohlcv? merged;
                            lock (_wsLock)
                            {
                                if (coin != _candleCoin || interval != _candleInterval || _consolidator == null) continue;
                                merged = _consolidator.Apply(bar);
                            }
                            if (merged.HasValue) _liveStream.OnNext(merged.Value);
                        }
                        break;
                    case "l2Book":
                        var data = root["data"] as JObject;
                        string bookSymbol;
                        lock (_wsLock) { if (data?["coin"]?.ToString() != _bookCoin) return; bookSymbol = _bookSymbol ?? ""; }
                        var (bids, asks) = ParseBook(data, 100);
                        _orderBookSubject.OnNext(new OrderBookUpdate(bookSymbol, bids, asks,
                            data?["time"]?.Value<long>() ?? 0, DateTime.UtcNow));
                        break;
                }
            }
            catch (Exception) { /* a malformed or unrelated frame (pong, subscriptionResponse) */ }
        }

        /// <summary>The candle(s) in one websocket frame: <c>data</c> is one candle, or a list of them.</summary>
        internal static List<Ohlcv> ParseSocketCandles(JObject frame, out string? coin, out string? interval)
        {
            coin = null; interval = null;
            var bars = new List<Ohlcv>();
            if (frame["channel"]?.ToString() != "candle") return bars;
            var data = frame["data"];
            IEnumerable<JToken> items = data is JArray arr ? arr : data != null ? new[] { data } : Array.Empty<JToken>();
            foreach (var c in items)
            {
                if (!HyperliquidWire.TryParseCandle(c, out var bar, out _)) continue;
                coin = c["s"]?.ToString(); interval = c["i"]?.ToString();
                bars.Add(bar);
            }
            return bars;
        }

        public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            var asset = await ResolveAsync(symbol, null).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Hyperliquid has no market named {symbol}.");
            string interval = NativelySupportedTimeframes.Contains(timeframe)
                ? timeframe : TimeframeUtility.GetBestBaseTimeframe(timeframe, NativelySupportedTimeframes);
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle);
            string subscribe = SubscribeMessage("subscribe", "candle", asset.Coin, interval);

            var ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .WithHeartbeatMessage("{\"method\":\"ping\"}")
                .OnError(err => _errorStream.OnNext($"Hyperliquid keyed stream ({asset.Display}): {err}"))
                .OnConnected(async w => await w.SendAsync(subscribe).ConfigureAwait(false))
                .OnMessage(msg =>
                {
                    try
                    {
                        var root = JObject.Parse(msg);
                        foreach (var bar in ParseSocketCandles(root, out var coin, out var iv))
                        {
                            if (coin != asset.Coin || iv != interval) continue;
                            var merged = consolidator.Apply(bar);
                            if (merged.HasValue) onBar(merged.Value);
                        }
                    }
                    catch (Exception) { /* malformed frame */ }
                });
            await ws.ConnectAsync().ConfigureAwait(false);
            return ws;   // disposing closes the socket
        }

        public override async Task DisconnectAsync()
        {
            ReconnectingWebSocket? ws;
            lock (_wsLock)
            {
                ws = _ws; _ws = null;
                _candleCoin = null; _candleInterval = null; _bookCoin = null; _bookSymbol = null; _consolidator = null;
            }
            if (ws != null) { await ws.DisconnectAsync().ConfigureAwait(false); ws.Dispose(); }

            ScrubCredentials(() => _agentKey = "", () => _account = "");
            _keyRefusal = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── ITradingProvider ──────────────────────────────────────────────────

        public bool IsConnected => CanSign;
        public override double MaxLeverage => 50.0;
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdates.AsObservable();

        /// <summary>
        /// No order push channel is wired yet. Hyperliquid has one (the <c>orderUpdates</c> and
        /// <c>userFills</c> subscriptions) and it is a follow-up; until then fills are resolved by
        /// the order-status poller through <see cref="GetOrderStatusAsync"/>.
        /// </summary>
        public bool ProvidesOrderStream => false;
        public bool SupportsOrderEventStreaming => false;

        /// <summary>The testnet is a real practice venue: a Paper credential signs against it.</summary>
        public bool HasPracticeEnvironment => true;

        public bool SupportsOrderStatusQuery => true;

        public async Task<List<Balance>> GetBalancesAsync()
        {
            RequireAccount();
            var list = new List<Balance>();

            var perp = await InfoAsync(new JObject { ["type"] = "clearinghouseState", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            if (perp != null)
            {
                double withdrawable = HyperliquidWire.D(perp["withdrawable"]);
                double used = HyperliquidWire.D(perp["marginSummary"]?["totalMarginUsed"]);
                // A separate row from spot USDC: perps margin is its own balance, and a name that
                // matched spot's would let the two be summed or confused.
                if (withdrawable > 0 || used > 0) list.Add(new Balance("USDC-PERPS", withdrawable, used));
            }

            var spot = await InfoAsync(new JObject { ["type"] = "spotClearinghouseState", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            foreach (var b in spot?["balances"] as JArray ?? new JArray())
            {
                double total = HyperliquidWire.D(b["total"]), hold = HyperliquidWire.D(b["hold"]);
                if (total > 0) list.Add(new Balance(b["coin"]?.ToString() ?? "", total - hold, hold));
            }
            return list;
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            RequireAccount();
            await EnsureAssetsAsync().ConfigureAwait(false);
            var state = await InfoAsync(new JObject { ["type"] = "clearinghouseState", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            return ParsePositions(state, _perpByCoin);
        }

        internal static List<Position> ParsePositions(JObject? state, IReadOnlyDictionary<string, AssetInfo> perpByCoin)
        {
            var list = new List<Position>();
            foreach (var ap in state?["assetPositions"] as JArray ?? new JArray())
            {
                var p = ap["position"];
                if (p is null) continue;
                double szi = HyperliquidWire.D(p["szi"]);
                if (szi == 0) continue;
                string coin = p["coin"]?.ToString() ?? "";
                double value = HyperliquidWire.D(p["positionValue"]);
                string kind = p["leverage"]?["type"]?.ToString() ?? "";
                list.Add(new Position(
                    perpByCoin.TryGetValue(coin, out var a) ? a.Display : coin + "-PERP",
                    szi,
                    HyperliquidWire.D(p["entryPx"]),                    // per unit, as the venue reports it
                    szi < 0 ? -value : value,
                    HyperliquidWire.D(p["unrealizedPnl"]),
                    Leverage: Math.Max(1.0, HyperliquidWire.D(p["leverage"]?["value"])),
                    // The venue's own liquidation price, never one computed here. Null (a position that
                    // cannot be liquidated) reads as 0.
                    LiquidationPrice: HyperliquidWire.D(p["liquidationPx"]),
                    MarginMode: kind.Equals("isolated", StringComparison.OrdinalIgnoreCase) ? MarginMode.Isolated
                              : kind.Equals("cross", StringComparison.OrdinalIgnoreCase) ? MarginMode.Cross : MarginMode.None));
            }
            return list;
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            RequireAccount();
            await EnsureAssetsAsync().ConfigureAwait(false);
            var json = await InfoAsync(new JObject { ["type"] = "frontendOpenOrders", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JArray ?? new JArray();
            string? want = symbol == null ? null : Find(symbol, null)?.Coin ?? symbol;
            return ParseOpenOrders(json, DisplayForCoin).Where(o =>
                want == null || string.Equals(o.Symbol, DisplayForCoin(want), StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private string DisplayForCoin(string coin)
        {
            if (_perpByCoin.TryGetValue(coin, out var p)) return p.Display;
            var spot = _assets.FirstOrDefault(a => a.IsSpot && a.Coin == coin);
            return spot?.Display ?? coin;
        }

        internal static List<OpenOrder> ParseOpenOrders(JArray rows, Func<string, string> display) =>
            rows.Select(o =>
            {
                string type = o["orderType"]?.ToString() ?? "Limit";
                bool trigger = o["isTrigger"]?.Value<bool>() == true || type.Contains("Stop") || type.Contains("Take Profit");
                double limit = HyperliquidWire.D(o["limitPx"]);
                double triggerPx = HyperliquidWire.D(o["triggerPx"]);
                bool isMarket = type.Contains("Market");
                return new OpenOrder(
                    o["oid"]?.ToString() ?? "",
                    display(o["coin"]?.ToString() ?? ""),
                    o["side"]?.ToString() == "A" ? OrderSide.Sell : OrderSide.Buy,
                    MapOrderType(type),
                    HyperliquidWire.D(o["sz"]),
                    trigger && (isMarket || limit == 0) ? triggerPx : limit,
                    trigger ? "trigger" : "open");
            }).ToList();

        internal static OrderType MapOrderType(string t) => t switch
        {
            "Stop Market"        => OrderType.StopMarket,
            "Stop Limit"         => OrderType.StopLimit,
            "Take Profit Market" => OrderType.TakeProfitMarket,
            "Take Profit Limit"  => OrderType.TakeProfitLimit,
            "Market"             => OrderType.Market,
            _                    => OrderType.Limit,
        };

        public async Task<List<TradeFill>> GetFillsAsync(string? symbol = null, int limit = 50)
        {
            RequireAccount();
            await EnsureAssetsAsync().ConfigureAwait(false);
            var json = await InfoAsync(new JObject { ["type"] = "userFills", ["user"] = _account }, _heavyInfoLimiter)
                .ConfigureAwait(false) as JArray ?? new JArray();
            string? want = symbol == null ? null : Find(symbol, null)?.Coin ?? symbol;
            return ParseFills(json, DisplayForCoin)
                .Where(f => want == null || string.Equals(f.Symbol, DisplayForCoin(want), StringComparison.OrdinalIgnoreCase))
                .Take(limit).ToList();
        }

        /// <summary>userFills is newest first; <c>side</c> is "B" for a buy and "A" for a sell.</summary>
        internal static List<TradeFill> ParseFills(JArray rows, Func<string, string> display) =>
            rows.Select(f => new TradeFill(
                f["tid"]?.ToString() ?? f["hash"]?.ToString() ?? "",
                display(f["coin"]?.ToString() ?? ""),
                f["side"]?.ToString() == "A" ? OrderSide.Sell : OrderSide.Buy,
                HyperliquidWire.D(f["sz"]), HyperliquidWire.D(f["px"]),
                DateTimeOffset.FromUnixTimeMilliseconds(f["time"]?.Value<long>() ?? 0).UtcDateTime,
                Fee: HyperliquidWire.D(f["fee"]),
                OrderId: f["oid"]?.ToString(),
                RealizedPnL: HyperliquidWire.D(f["closedPnl"]))).ToList();

        public async Task<OrderStatusSnapshot?> GetOrderStatusAsync(string orderId, string? symbol = null)
        {
            RequireAccount();
            if (!long.TryParse(orderId, NumberStyles.None, CultureInfo.InvariantCulture, out long oid)) return null;
            await EnsureAssetsAsync().ConfigureAwait(false);

            // A transport failure throws out of here on purpose: null means "answered, no such order".
            var reply = await InfoAsync(new JObject { ["type"] = "orderStatus", ["user"] = _account, ["oid"] = oid }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            var snapshot = ParseOrderStatus(reply, DisplayForCoin);
            if (snapshot is null || snapshot.FilledQuantity <= 0 || snapshot.FilledPrice > 0) return snapshot;

            // The status reply carries sizes, not the average fill price; the fills do.
            var fills = await InfoAsync(new JObject { ["type"] = "userFills", ["user"] = _account }, _heavyInfoLimiter)
                .ConfigureAwait(false) as JArray ?? new JArray();
            double qty = 0, notional = 0;
            foreach (var f in fills.Where(f => f["oid"]?.ToString() == orderId))
            {
                double sz = HyperliquidWire.D(f["sz"]);
                qty += sz; notional += sz * HyperliquidWire.D(f["px"]);
            }
            return qty > 0 ? snapshot with { FilledPrice = notional / qty } : snapshot;
        }

        internal static OrderStatusSnapshot? ParseOrderStatus(JObject? reply, Func<string, string> display)
        {
            if (reply is null || reply["status"]?.ToString() == "unknownOid") return null;
            var wrap = reply["order"];
            var o = wrap?["order"];
            if (o is null) return null;

            string status = wrap?["status"]?.ToString() ?? "";
            double orig = HyperliquidWire.D(o["origSz"]), remaining = HyperliquidWire.D(o["sz"]);
            double filled = Math.Max(0, orig - remaining);
            var side = o["side"]?.ToString() == "A" ? OrderSide.Sell : OrderSide.Buy;
            string sym = display(o["coin"]?.ToString() ?? "");

            PolledOrderState state;
            if (status == "open") state = filled > 0 ? PolledOrderState.PartiallyFilled : PolledOrderState.Working;
            else if (status == "triggered") state = PolledOrderState.Working;
            else if (status == "filled") { state = PolledOrderState.Filled; filled = orig; remaining = 0; }
            else if (status.EndsWith("Rejected", StringComparison.OrdinalIgnoreCase)) state = PolledOrderState.Rejected;
            else if (status.EndsWith("Canceled", StringComparison.OrdinalIgnoreCase) || status == "scheduledCancel")
                state = PolledOrderState.Cancelled;
            else state = PolledOrderState.Working;   // unknown: do not claim it ended

            return new OrderStatusSnapshot(state, side, sym, filled, 0, remaining,
                StopTriggered: status == "triggered");
        }

        private void RequireAccount()
        {
            if (_keyRefusal != null) throw new UnauthorizedAccessException(_keyRefusal);
            if (!AddressShape.IsMatch(_account))
                throw new UnauthorizedAccessException(
                    "Hyperliquid needs your main account address in the API key field to read the account.");
        }

        // ── Orders ────────────────────────────────────────────────────────────

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (_keyRefusal != null) return $"ORDER_FAILED:{_keyRefusal}";
            if (!CanSign) return "ORDER_FAILED:no Hyperliquid agent wallet configured (account address and agent key)";
            if (!(signal.Quantity > 0)) return "ORDER_FAILED:order size must be greater than zero";

            try
            {
                var asset = await ResolveAsync(signal.Symbol, signal.SubType).ConfigureAwait(false);
                if (asset == null) return $"ORDER_FAILED:Hyperliquid has no market named {signal.Symbol}";

                // Leverage first, and a failure stops the order: trading at a leverage the user did
                // not ask for is the one outcome worse than not trading.
                if (signal.Leverage is > 0 && !asset.IsSpot)
                {
                    bool isolated = !string.Equals(signal.MarginType, "Cross", StringComparison.OrdinalIgnoreCase);
                    string? lerr = await UpdateLeverageAsync(asset, signal.Leverage.Value, isCross: !isolated).ConfigureAwait(false);
                    if (lerr != null) return $"ORDER_FAILED:{lerr}";
                }

                var (wires, grouping, err) = await BuildOrdersAsync(signal, asset).ConfigureAwait(false);
                if (err != null) return $"ORDER_FAILED:{err}";

                var reply = await ExchangeAsync(HyperliquidWire.OrderAction(wires, grouping)).ConfigureAwait(false);
                var outcomes = HyperliquidWire.ParseOrderReply(reply);
                if (outcomes.Count == 0) return "ORDER_FAILED:Hyperliquid accepted the request but returned no order status";

                var entry = outcomes[0];
                if (entry.IsError) return $"ORDER_FAILED:{entry.Error}";

                // A protective leg that failed leaves a position with no stop. Say so loudly rather
                // than report a clean success.
                for (int i = 1; i < outcomes.Count; i++)
                    if (outcomes[i].IsError)
                        SurfaceError($"Hyperliquid: the entry order was placed but a protective order was refused: {outcomes[i].Error}. "
                                   + "The position has no stop or target until you add one.",
                                     ErrorSeverity.High, ErrorCategory.Provider);

                return entry.Oid ?? "ORDER_FAILED:Hyperliquid placed the order but returned no id";
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        /// <summary>
        /// The signed order wires for a signal, and the grouping. A Market or Limit entry that
        /// carries StopLoss / TakeProfit becomes an entry plus reduce-only trigger legs in one
        /// "normalTpsl" action, so the exchange links the legs to the position. A StopMarket,
        /// StopLimit, TakeProfitMarket or TakeProfitLimit is a single trigger order.
        /// </summary>
        private async Task<(List<OrderedMap> Wires, string Grouping, string? Error)> BuildOrdersAsync(
            TradeSignal signal, AssetInfo asset)
        {
            bool buy = signal.Side == OrderSide.Buy;
            decimal size = HyperliquidWire.RoundSize(signal.Quantity, asset.SzDecimals);
            if (size <= 0)
                return (new(), "na", $"size {signal.Quantity.ToString(CultureInfo.InvariantCulture)} rounds to zero at {asset.SzDecimals} decimals for {asset.Display}");
            string sz = HyperliquidWire.FormatWire(size);
            string? cloid = HyperliquidWire.ToCloid(signal.ClientOid);
            string? tif = TifFor(signal, out string? tifError);
            if (tifError != null) return (new(), "na", tifError);

            var wires = new List<OrderedMap>();

            switch (signal.Type)
            {
                case OrderType.Market:
                case OrderType.Limit:
                {
                    double px;
                    OrderedMap type;
                    if (signal.Type == OrderType.Limit)
                    {
                        if (signal.Price is not > 0) return (new(), "na", "a limit order needs a price");
                        px = signal.Price.Value;
                        type = HyperliquidWire.LimitType(tif ?? "Gtc");
                    }
                    else
                    {
                        double? mid = await GetMidAsync(asset).ConfigureAwait(false);
                        if (mid is not > 0) return (new(), "na", $"no current price for {asset.Display} to bound a market order");
                        px = SlippedPrice(mid.Value, buy);
                        type = HyperliquidWire.LimitType("Ioc");   // a market order is an IOC limit
                    }
                    wires.Add(HyperliquidWire.OrderWire(asset.AssetId, buy,
                        HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(px, asset.SzDecimals, asset.IsSpot)),
                        sz, signal.ReduceOnly, type, cloid));

                    bool bracket = signal.StopLoss is > 0 || signal.TakeProfit is > 0;
                    if (bracket && !asset.IsSpot)
                    {
                        if (signal.TakeProfit is > 0) wires.Add(TriggerLeg(asset, !buy, sz, signal.TakeProfit.Value, "tp"));
                        if (signal.StopLoss is > 0)   wires.Add(TriggerLeg(asset, !buy, sz, signal.StopLoss.Value, "sl"));
                        return (wires, "normalTpsl", null);
                    }
                    if (bracket)
                        return (new(), "na", "Hyperliquid spot has no attached stop or target; place them as separate orders");
                    return (wires, "na", null);
                }

                case OrderType.StopMarket:
                case OrderType.StopLimit:
                case OrderType.TakeProfitMarket:
                case OrderType.TakeProfitLimit:
                {
                    bool stop = signal.Type is OrderType.StopMarket or OrderType.StopLimit;
                    bool isMarket = signal.Type is OrderType.StopMarket or OrderType.TakeProfitMarket;
                    double? trigger = stop ? signal.TriggerPrice ?? signal.StopLoss : signal.TriggerPrice ?? signal.TakeProfit;
                    if (trigger is not > 0) return (new(), "na", "a stop or take-profit order needs a trigger price");
                    double limitPx = isMarket ? SlippedPrice(trigger.Value, buy) : signal.Price ?? 0;
                    if (!(limitPx > 0)) return (new(), "na", "a stop-limit or take-profit-limit order needs a limit price");
                    var type = HyperliquidWire.TriggerType(isMarket,
                        HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(trigger.Value, asset.SzDecimals, asset.IsSpot)),
                        stop ? "sl" : "tp");
                    wires.Add(HyperliquidWire.OrderWire(asset.AssetId, buy,
                        HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(limitPx, asset.SzDecimals, asset.IsSpot)),
                        sz, signal.ReduceOnly, type, cloid));
                    return (wires, "na", null);
                }

                default:
                    return (new(), "na", $"order type {signal.Type} is not supported by Hyperliquid");
            }
        }

        /// <summary>A reduce-only market trigger on the opposite side, sized like the entry.</summary>
        private static OrderedMap TriggerLeg(AssetInfo asset, bool isBuy, string size, double trigger, string tpsl)
        {
            string trig = HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(trigger, asset.SzDecimals, asset.IsSpot));
            string worst = HyperliquidWire.FormatWire(HyperliquidWire.RoundPrice(SlippedPrice(trigger, isBuy), asset.SzDecimals, asset.IsSpot));
            return HyperliquidWire.OrderWire(asset.AssetId, isBuy, worst, size, true,
                HyperliquidWire.TriggerType(true, trig, tpsl), null);
        }

        /// <summary>The worst price a market-style order may fill at: slippage past the reference, in the aggressive direction.</summary>
        internal static double SlippedPrice(double reference, bool isBuy) =>
            reference * (isBuy ? 1 + MarketSlippage : 1 - MarketSlippage);

        /// <summary>Maps the signal's time-in-force and post-only flag to Hyperliquid's Gtc / Ioc / Alo, or an error for ones it lacks.</summary>
        private static string? TifFor(TradeSignal signal, out string? error)
        {
            error = null;
            if (signal.PostOnly) return "Alo";   // add-liquidity-only: rejected rather than taken
            string? t = signal.TimeInForce;
            if (string.IsNullOrEmpty(t) || t.Equals("GTC", StringComparison.OrdinalIgnoreCase)) return "Gtc";
            if (t.Equals("IOC", StringComparison.OrdinalIgnoreCase)) return "Ioc";
            error = $"Hyperliquid supports good-till-cancel, immediate-or-cancel and post-only, not {t}";
            return null;
        }

        private async Task<double?> GetMidAsync(AssetInfo asset)
        {
            var mids = await InfoAsync(new JObject { ["type"] = "allMids" }, _lightInfoLimiter).ConfigureAwait(false) as JObject;
            double v = HyperliquidWire.D(mids?[asset.Coin]);
            return v > 0 ? v : null;
        }

        /// <summary>Sets leverage and margin mode for one market. Returns an error message or null.</summary>
        private async Task<string?> UpdateLeverageAsync(AssetInfo asset, double leverage, bool isCross)
        {
            int want = (int)Math.Clamp(Math.Round(leverage), 1, Math.Max(1, asset.MaxLeverage));
            if (asset.OnlyIsolated) isCross = false;
            var reply = await ExchangeAsync(HyperliquidWire.UpdateLeverageAction(asset.AssetId, isCross, want)).ConfigureAwait(false);
            return string.Equals(reply["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"Hyperliquid refused leverage {want}x on {asset.Display}: {reply["response"]}";
        }

        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            try
            {
                if (!CanSign) return 1.0;
                var asset = await ResolveAsync(symbol, "Futures").ConfigureAwait(false);
                if (asset == null || asset.IsSpot) return 1.0;
                // Isolated unless the user chose otherwise elsewhere: a liquidation then takes one
                // position, not the account.
                string? err = await UpdateLeverageAsync(asset, leverage, isCross: false).ConfigureAwait(false);
                if (err != null) { SurfaceError(err, ErrorSeverity.Medium, ErrorCategory.Provider); return 1.0; }
                return Math.Clamp(Math.Round(leverage), 1, Math.Max(1, asset.MaxLeverage));
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid set leverage failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return 1.0;
            }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            try
            {
                if (!CanSign || !long.TryParse(orderId, NumberStyles.None, CultureInfo.InvariantCulture, out long oid)) return false;
                var asset = await ResolveAsync(symbol, null).ConfigureAwait(false);
                if (asset == null) return false;
                var reply = await ExchangeAsync(HyperliquidWire.CancelAction(new[] { (asset.AssetId, oid) })).ConfigureAwait(false);
                var outcomes = HyperliquidWire.ParseOrderReply(reply);
                return outcomes.Count > 0 && !outcomes[0].IsError;
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid cancel failed: {ex.Message}", ErrorSeverity.Medium, ErrorCategory.Provider);
                return false;
            }
        }

        /// <summary>Cancels every open order (optionally one market's) in a single action. Returns how many were cancelled.</summary>
        public async Task<int> CancelAllAsync(string? symbol = null)
        {
            if (!CanSign) return 0;
            var open = await GetOpenOrdersAsync(symbol).ConfigureAwait(false);
            var cancels = new List<(int, long)>();
            foreach (var o in open)
            {
                var asset = await ResolveAsync(o.Symbol, null).ConfigureAwait(false);
                if (asset != null && long.TryParse(o.Id, out long oid)) cancels.Add((asset.AssetId, oid));
            }
            if (cancels.Count == 0) return 0;
            var reply = await ExchangeAsync(HyperliquidWire.CancelAction(cancels)).ConfigureAwait(false);
            return HyperliquidWire.ParseOrderReply(reply).Count(o => !o.IsError);
        }

        /// <summary>
        /// Changes a resting order's price and size in place, keeping its queue place where the
        /// venue allows. The replacement is built exactly as a new order would be.
        /// </summary>
        public async Task<string> ModifyOrderAsync(string orderId, TradeSignal replacement)
        {
            if (_keyRefusal != null) return $"ORDER_FAILED:{_keyRefusal}";
            if (!CanSign) return "ORDER_FAILED:no Hyperliquid agent wallet configured";
            if (!long.TryParse(orderId, NumberStyles.None, CultureInfo.InvariantCulture, out long oid))
                return "ORDER_FAILED:order id is not a Hyperliquid order number";
            try
            {
                var asset = await ResolveAsync(replacement.Symbol, replacement.SubType).ConfigureAwait(false);
                if (asset == null) return $"ORDER_FAILED:Hyperliquid has no market named {replacement.Symbol}";
                var (wires, _, err) = await BuildOrdersAsync(replacement, asset).ConfigureAwait(false);
                if (err != null) return $"ORDER_FAILED:{err}";

                var reply = await ExchangeAsync(HyperliquidWire.ModifyAction(oid, wires[0])).ConfigureAwait(false);
                if (!string.Equals(reply["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    return $"ORDER_FAILED:{reply["response"]}";
                return orderId;
            }
            catch (Exception ex) { return $"ORDER_FAILED:{ex.Message}"; }
        }

        /// <summary>
        /// The dead-man's switch. Asks Hyperliquid to cancel every open order at the given time
        /// (at least 5 seconds ahead; 10 uses a day), or clears it with null. Nothing calls this
        /// yet: wiring a heartbeat that keeps pushing it forward while the terminal runs, and
        /// speaking when it fires, is the safety-layer follow-up. Returns an error text or null.
        /// </summary>
        public async Task<string?> ScheduleCancelAsync(DateTimeOffset? at)
        {
            if (!CanSign) return "no Hyperliquid agent wallet configured";
            if (at.HasValue && at.Value < DateTimeOffset.UtcNow.AddSeconds(5))
                return "Hyperliquid needs the cancel time at least 5 seconds ahead";
            var reply = await ExchangeAsync(HyperliquidWire.ScheduleCancelAction(at?.ToUnixTimeMilliseconds())).ConfigureAwait(false);
            return string.Equals(reply["status"]?.ToString(), "ok", StringComparison.OrdinalIgnoreCase)
                ? null : reply["response"]?.ToString() ?? "refused";
        }

        // ── Transport ─────────────────────────────────────────────────────────

        /// <summary>One POST to /info. A non-2xx answer throws with the status so retry classification works; the URL and account never travel in the message.</summary>
        private async Task<JToken> InfoAsync(JObject body, RateLimiter limiter)
        {
            await limiter.WaitAsync().ConfigureAwait(false);
            string type = body["type"]?.ToString() ?? "?";
            using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync($"{Rest}/info", content).ConfigureAwait(false);
            string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Hyperliquid refused {type}: HTTP {(int)res.StatusCode}{Excerpt(text)}", null, res.StatusCode);
            try { return JToken.Parse(text); }
            catch (JsonReaderException)
            {
                throw new HttpRequestException($"Hyperliquid answered {type} with a non-JSON body (HTTP {(int)res.StatusCode}).");
            }
        }

        private static string Excerpt(string text)
        {
            text = text.Trim();
            if (text.Length == 0) return "";
            return ": " + (text.Length > 200 ? text[..200] : text);
        }

        /// <summary>
        /// Signs and posts one action. The JSON action and the signed msgpack come from the SAME
        /// tree, so what is sent is what was signed. The reply is returned as the exchange gave it:
        /// <c>{"status":"ok"|"err",...}</c>.
        /// </summary>
        private async Task<JObject> ExchangeAsync(OrderedMap action)
        {
            if (!CanSign) throw new InvalidOperationException("No Hyperliquid agent wallet is configured.");
            ulong nonce = NextNonce();
            var sig = HyperliquidSigner.SignL1Action(_agentKey, action, null, nonce, _isMainnet);
            var payload = new JObject
            {
                ["action"] = HyperliquidWire.ToJson(action),
                ["nonce"] = nonce,
                ["signature"] = new JObject { ["r"] = sig.R, ["s"] = sig.S, ["v"] = sig.V },
            };

            await _exchangeLimiter.WaitAsync().ConfigureAwait(false);
            using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync($"{Rest}/exchange", content).ConfigureAwait(false);
            string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            JObject? json = null;
            try { json = JObject.Parse(text); } catch (JsonReaderException) { }
            // A refusal the exchange explains keeps its explanation whatever the status code.
            if (json != null && json["status"] != null) return json;
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Hyperliquid refused the order request: HTTP {(int)res.StatusCode}{Excerpt(text)}", null, res.StatusCode);
            return json ?? throw new HttpRequestException("Hyperliquid answered the order request with a non-JSON body.");
        }

        /// <summary>Millisecond clock, forced strictly increasing: the exchange rejects a nonce it has seen.</summary>
        private ulong NextNonce()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            while (true)
            {
                long last = Interlocked.Read(ref _lastNonce);
                long next = Math.Max(now, last + 1);
                if (Interlocked.CompareExchange(ref _lastNonce, next, last) == last) return (ulong)next;
            }
        }
    }

    /// <summary>Funding, open interest and prices for one perp (see <see cref="HyperliquidProvider.GetAssetContextAsync"/>).</summary>
    public sealed record HyperliquidAssetContext(
        string Symbol, double FundingRate, double OpenInterest, double MarkPrice, double OraclePrice, double DayVolumeUsd);
}
