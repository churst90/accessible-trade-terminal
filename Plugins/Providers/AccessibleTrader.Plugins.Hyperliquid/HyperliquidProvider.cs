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
    public class HyperliquidProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider, ISymbolLeverageProvider, ITradeStreamProvider, IPerpetualContextProvider
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
        // The account's mode as userAbstraction last said, and when: see PoolsSpotCollateralAsync.
        private (string Account, string Mode, DateTime At)? _accountMode;
        private static readonly TimeSpan AccountModeTtl = TimeSpan.FromMinutes(10);

        // Metadata cache: perps and spot pairs, rebuilt when a symbol misses.
        private readonly SemaphoreSlim _assetLock = new(1, 1);
        private List<AssetInfo> _assets = new();
        private Dictionary<string, AssetInfo> _byNorm = new();
        private Dictionary<string, AssetInfo> _perpByCoin = new(StringComparer.Ordinal);
        private DateTime _assetsLoadedAt = DateTime.MinValue;
        private static readonly TimeSpan AssetTtl = TimeSpan.FromMinutes(30);
        // Builders whose number in our asset ids matched perpDexs at the last load (see VerifiedBuilders).
        private IReadOnlySet<string> _verifiedBuilders = new HashSet<string>();

        // Builders' margin, positions and orders are read one builder at a time (an open-orders
        // read weighs 20 of the 1,200 a minute). A builder where the account held nothing is
        // left alone for a minute; an order there, or the stream naming one of its markets,
        // reads it again at once. Keyed "kind:dex".
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _builderQuietUntil = new(StringComparer.Ordinal);
        internal static readonly TimeSpan BuilderQuietFor = TimeSpan.FromMinutes(1);
        private static readonly Regex BuilderCoinInFrame = new("\"coin\":\"([A-Za-z0-9]+):", RegexOptions.Compiled);

        // Websocket state for the focused chart.
        private ReconnectingWebSocket? _ws;
        private string? _candleCoin;
        private string? _candleInterval;
        private string? _bookCoin;
        private string? _bookSymbol;
        private BarBucketConsolidator? _consolidator;
        private readonly object _wsLock = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> _keyedSubscriptions = new();

        // The account's order stream: a socket of its own, opened when the order service subscribes.
        private readonly HyperliquidOrderStream _orderStream;
        private readonly object _accountWsLock = new();
        private readonly object _emitLock = new();
        private ReconnectingWebSocket? _accountWs;
        private string? _accountWsUser;
        private bool _orderStreamWanted;

        public HyperliquidProvider()
        {
            _http = PluginHostServices.CreateHttpClient(
                providerId:   "Hyperliquid",
                allowedHosts: new[] { "api.hyperliquid.xyz", "api.hyperliquid-testnet.xyz" });
            _orderStream = new HyperliquidOrderStream(DisplayForCoin, OrderTypeAsync, RestFillsAsync,
                u => { lock (_emitLock) _orderUpdates.OnNext(u); }, TimeSpan.FromSeconds(2));
        }

        // ── IProviderPlugin ───────────────────────────────────────────────────

        public override string Name => "Hyperliquid";

        /// <summary>
        /// Added to a perpetual's symbol ("BTC-PERP_FUNDING") to ask for that market's funding
        /// history instead of its candles. The Funding Rate indicator asks this way on a
        /// Hyperliquid chart.
        /// </summary>
        public const string FundingSuffix = "_FUNDING";

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
            if (typeof(T) == typeof(IPerpetualContextProvider)) return this as T;
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
            _accountMode = null;
            _builderQuietUntil.Clear();
            _keyRefusal = CheckCredential(address, secret);
            if (_orderStreamWanted) StartAccountStream();   // another account or venue: follow it
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

                // Builders' markets too (HIP-3: shares, metals, indices), best effort like spot.
                try
                {
                    perps.AddRange(HyperliquidWire.ParseAllPerpMetas(
                        await InfoAsync(new JObject { ["type"] = "allPerpMetas" }, _heavyInfoLimiter).ConfigureAwait(false) as JArray));
                }
                catch (Exception ex) when (!TransportFailure.IsTransient(ex))
                {
                    SurfaceError($"Hyperliquid builders' markets failed: {ex.Message}", ErrorSeverity.Low, ErrorCategory.Provider);
                }

                // The builders' numbers, checked against the list the SDK numbers from. A failure
                // leaves them unverified, which refuses orders on them; charts are unaffected.
                IReadOnlySet<string> verified = new HashSet<string>();
                if (perps.Any(a => a.Dex.Length > 0))
                {
                    try
                    {
                        verified = HyperliquidWire.VerifiedBuilders(
                            await InfoAsync(new JObject { ["type"] = "perpDexs" }, _heavyInfoLimiter).ConfigureAwait(false) as JArray, perps);
                    }
                    catch (Exception ex) when (!TransportFailure.IsTransient(ex))
                    {
                        SurfaceError($"Hyperliquid builders' list failed: {ex.Message}", ErrorSeverity.Low, ErrorCategory.Provider);
                    }
                }

                var all = perps.Concat(spot).ToList();
                var byNorm = new Dictionary<string, AssetInfo>(StringComparer.Ordinal);
                foreach (var a in all) byNorm.TryAdd(a.Norm, a);   // first (lowest id) wins on a duplicate name
                _perpByCoin = perps.ToDictionary(a => a.Coin, a => a, StringComparer.Ordinal);
                _verifiedBuilders = verified;
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

        /// <summary>
        /// The leverage amounts one perp takes: 2 to its own maxLeverage from meta (BTC 40, TAO 5,
        /// KAS 3 on 2026-11-10). The venue-wide <see cref="MaxLeverage"/> of 50 offered 50 on
        /// every market, and the order then went out at the market's own ceiling. Spot has no
        /// leverage (empty); null when the market or the metadata could not be found.
        /// </summary>
        public async Task<IReadOnlyList<int>?> GetAllowedLeverageAsync(string symbol, OrderSide side, CancellationToken ct = default)
        {
            try
            {
                var a = await ResolveAsync(symbol, null).ConfigureAwait(false);
                if (a == null) return null;
                if (a.IsSpot || a.MaxLeverage < 2) return Array.Empty<int>();
                return Enumerable.Range(2, a.MaxLeverage - 1).ToList();
            }
            catch (Exception ex)
            {
                SurfaceError($"Hyperliquid leverage limits for {symbol} could not be read: {ex.Message}", ErrorSeverity.Low, ErrorCategory.Provider);
                return null;
            }
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
                if (request.Symbol.EndsWith(FundingSuffix, StringComparison.OrdinalIgnoreCase))
                    return await FetchFundingAsync(request).ConfigureAwait(false);
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

        /// <summary>One <c>fundingHistory</c> reply holds at most this many hourly rates.</summary>
        private const int FundingPageHours = 500;

        /// <summary>
        /// Funding history for one perpetual, as bars whose every price is the rate in percent per
        /// 8 hours. Hyperliquid pays every hour; its hourly rate times eight keeps the Funding Rate
        /// indicator's levels, set for venues that pay every 8 hours, meaning the same thing. A reply
        /// starts at startTime and holds at most 500 hours, so one page is the 500 hours up to Until
        /// and the cross-series cache walks back page by page. A spot pair has none.
        /// </summary>
        private async Task<(List<Ohlcv>, List<(long, double)>)> FetchFundingAsync(MarketDataRequest request)
        {
            var asset = await ResolveAsync(request.Symbol[..^FundingSuffix.Length], "Futures").ConfigureAwait(false);
            if (asset == null || asset.IsSpot) return (new List<Ohlcv>(), new List<(long, double)>());
            long end = request.Until ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long start = Math.Max(request.Since ?? 0, end - FundingPageHours * 3_600_000L);
            var reply = await InfoAsync(new JObject
            {
                ["type"] = "fundingHistory", ["coin"] = asset.Coin, ["startTime"] = start, ["endTime"] = end,
            }, _heavyInfoLimiter).ConfigureAwait(false) as JArray;
            var bars = ParseFundingHistory(reply, end);
            return (bars, bars.Select(b => (new DateTimeOffset(b.Date, TimeSpan.Zero).ToUnixTimeSeconds(), 0.0)).ToList());
        }

        /// <summary>
        /// <c>[{coin, fundingRate, premium, time}]</c>, a fraction per hour, to percent per 8 hours,
        /// oldest first. The venue stamps each a few milliseconds past the hour; the bar is put on
        /// the second.
        /// </summary>
        internal static List<Ohlcv> ParseFundingHistory(JArray? reply, long end)
        {
            var bars = new List<Ohlcv>();
            if (reply == null) return bars;
            foreach (var row in reply)
            {
                long t = row["time"]?.Type == JTokenType.Integer ? row.Value<long>("time") : -1;
                if (t < 0 || t > end || row["fundingRate"] == null) continue;
                double v = HyperliquidWire.D(row["fundingRate"]) * 8 * 100;
                bars.Add(new Ohlcv(DateTimeOffset.FromUnixTimeMilliseconds(t - t % 1000).UtcDateTime, v, v, v, v, 0));
            }
            bars.Sort((a, b) => a.Date.CompareTo(b.Date));
            return bars;
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
            var json = await InfoAsync(WithDex(new JObject { ["type"] = "metaAndAssetCtxs" }, asset), _heavyInfoLimiter).ConfigureAwait(false) as JArray;
            return ParseAssetContext(json, asset.MetaIndex, asset.Display);
        }

        /// <summary>The spoken "funding now" readout's source: the asset context, funding paid every hour.</summary>
        public async Task<PerpetualContext?> GetPerpetualContextAsync(string symbol, CancellationToken ct = default)
        {
            var c = await GetAssetContextAsync(symbol).ConfigureAwait(false);
            return c is null ? null : new PerpetualContext(c.Symbol, c.FundingRate, FundingIntervalHours: 1,
                c.OpenInterest, c.MarkPrice, c.OraclePrice, c.DayVolumeUsd);
        }

        /// <summary>A builder's market is read from that builder's lists; Hyperliquid's own need no <c>dex</c>.</summary>
        private static JObject WithDex(JObject body, AssetInfo asset)
        {
            if (asset.Dex.Length > 0) body["dex"] = asset.Dex;
            return body;
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

        /// <summary>
        /// Every trade on the market with the side that took it, for true buy/sell volume
        /// (<see cref="ITradeStreamProvider"/>). Its own socket on the <c>trades</c> subscription.
        /// The first message after subscribing is the last few trades from before (Hyperliquid
        /// does not mark it); they are older than the moment counting starts, so the tape leaves
        /// them out of every bar it counts.
        /// </summary>
        public async Task<IAsyncDisposable> SubscribeTradesAsync(string market, string symbol,
            Action<PublicTrade> onTrade, Action<bool> onCounting)
        {
            var asset = await ResolveAsync(symbol, null).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Hyperliquid has no market named {symbol}.");
            string subscribe = SubscribeMessage("subscribe", "trades", asset.Coin, null);
            var ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                .WithHeartbeatMessage("{\"method\":\"ping\"}")
                .OnError(err => _errorStream.OnNext($"Hyperliquid trade stream ({asset.Display}): {err}"));
            ws.OnConnected(async w => { await w.SendAsync(subscribe).ConfigureAwait(false); onCounting(true); })
              .OnDisconnected(() => onCounting(false))
              .OnMessage(msg => { foreach (var t in ReadPublicTrades(msg, asset.Coin)) onTrade(t); });
            await ws.ConnectAsync().ConfigureAwait(false);
            return ws;
        }

        /// <summary>A <c>trades</c> message as <see cref="PublicTrade"/>s. <c>side</c> is the
        /// taker's: <c>B</c> a buyer took the trade, <c>A</c> a seller (checked live 2026-11-23,
        /// 46 of 47 trades outside the spread printed on that side).</summary>
        internal static List<PublicTrade> ReadPublicTrades(string msg, string coin)
        {
            var trades = new List<PublicTrade>();
            try
            {
                var json = JObject.Parse(msg);
                if (json["channel"]?.ToString() != "trades" || json["data"] is not JArray data) return trades;
                foreach (var t in data)
                {
                    if (t["coin"]?.ToString() != coin) continue;
                    OrderSide? taker = t["side"]?.ToString() switch { "B" => OrderSide.Buy, "A" => OrderSide.Sell, _ => null };
                    if (taker == null) continue;
                    if (!double.TryParse(t["px"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double price) || price <= 0) continue;
                    if (!double.TryParse(t["sz"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double size) || size <= 0) continue;
                    if (t["time"] is not JValue { Type: JTokenType.Integer } ms) continue;
                    trades.Add(new PublicTrade(DateTimeOffset.FromUnixTimeMilliseconds(ms.Value<long>()).UtcDateTime, price, size, taker.Value));
                }
            }
            catch { /* malformed frame */ }
            return trades;
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

            ReconnectingWebSocket? accountWs;
            lock (_accountWsLock)
            {
                accountWs = _accountWs; _accountWs = null; _accountWsUser = null;
                _orderStream.Reset();
            }
            if (accountWs != null) await CloseQuietlyAsync(accountWs).ConfigureAwait(false);

            ScrubCredentials(() => _agentKey = "", () => _account = "");
            _keyRefusal = null;
            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // ── ITradingProvider ──────────────────────────────────────────────────

        public bool IsConnected => CanSign;
        public override double MaxLeverage => 50.0;
        /// <summary>
        /// The account's order events, from its own socket (see <see cref="HyperliquidOrderStream"/>).
        /// Subscribing opens it: the order service subscribes for a venue it trades on, and the
        /// chart may be showing another venue, so waiting for <see cref="EnsureConnectedAsync"/>
        /// would leave the fills of an order sent from another chart unheard.
        /// </summary>
        public IObservable<OrderUpdate> OrderUpdateStream => Observable.Defer(() =>
        {
            _orderStreamWanted = true;
            StartAccountStream();
            return _orderUpdates.AsObservable();
        });

        public bool ProvidesOrderStream => true;

        /// <summary>
        /// True while the account socket is open and the venue has confirmed both subscriptions on
        /// it. False before that, after a drop, and with no account address: then a placed order is
        /// followed by the order-status poller instead.
        /// </summary>
        public bool SupportsOrderEventStreaming
        {
            get
            {
                lock (_accountWsLock)
                    return _accountWs?.IsConnected == true && _accountWsUser == _account && _orderStream.Subscribed;
            }
        }

        /// <summary>
        /// Opens the account socket for the configured address, or moves it to a new one. Reading
        /// the stream needs the address only, so a user who has not made an agent key still hears
        /// fills of orders placed on the website.
        /// </summary>
        private void StartAccountStream()
        {
            ReconnectingWebSocket? old = null, ws = null;
            string user = _account;
            lock (_accountWsLock)
            {
                bool valid = AddressShape.IsMatch(user);
                if (_accountWs != null && _accountWsUser == user && valid) return;
                old = _accountWs; _accountWs = null; _accountWsUser = null;
                _orderStream.Reset();
                if (valid)
                {
                    ws = new ReconnectingWebSocket(WsUrl, heartbeatInterval: TimeSpan.FromSeconds(30))
                        .WithHeartbeatMessage("{\"method\":\"ping\"}")
                        .OnError(err => _errorStream.OnNext($"Hyperliquid order stream: {err}"))
                        .OnConnected(async w =>
                        {
                            _orderStream.Reset();
                            await w.SendAsync(HyperliquidOrderStream.SubscribeMessage("orderUpdates", user)).ConfigureAwait(false);
                            await w.SendAsync(HyperliquidOrderStream.SubscribeMessage("userFills", user)).ConfigureAwait(false);
                        })
                        .OnDisconnected(() => _orderStream.Reset())
                        .OnMessage(msg => { WakeBuildersNamedIn(msg); _ = _orderStream.OnMessageAsync(msg); });
                    _accountWs = ws; _accountWsUser = user;
                }
            }
            if (old != null) _ = CloseQuietlyAsync(old);
            if (ws != null)
            {
                var opening = ws;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EnsureAssetsAsync().ConfigureAwait(false);   // coin names for the symbols
                        await opening.ConnectAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"Hyperliquid order stream did not open ({ex.GetType().Name}); placed orders are followed by polling instead.");
                    }
                });
            }
        }

        private static async Task CloseQuietlyAsync(ReconnectingWebSocket ws)
        {
            try { await ws.DisconnectAsync().ConfigureAwait(false); } catch (Exception) { }
            ws.Dispose();
        }

        private async Task<string?> OrderTypeAsync(long oid)
        {
            var reply = await InfoAsync(new JObject { ["type"] = "orderStatus", ["user"] = _account, ["oid"] = oid }, _lightInfoLimiter)
                .ConfigureAwait(false);
            return reply["order"]?["order"]?["orderType"]?.ToString();
        }

        private async Task<(double Qty, double Price)> RestFillsAsync(long oid)
        {
            var fills = await InfoAsync(new JObject { ["type"] = "userFills", ["user"] = _account }, _heavyInfoLimiter)
                .ConfigureAwait(false) as JArray ?? new JArray();
            return FillAverage(fills, oid.ToString(CultureInfo.InvariantCulture));
        }

        internal static (double Qty, double Price) FillAverage(JArray fills, string orderId)
        {
            double qty = 0, notional = 0;
            foreach (var f in fills.Where(f => f["oid"]?.ToString() == orderId))
            {
                double sz = HyperliquidWire.D(f["sz"]);
                qty += sz; notional += sz * HyperliquidWire.D(f["px"]);
            }
            return (qty, qty > 0 ? notional / qty : 0);
        }

        /// <summary>The testnet is a real practice venue: a Paper credential signs against it.</summary>
        public bool HasPracticeEnvironment => true;

        public bool SupportsOrderStatusQuery => true;

        public async Task<List<Balance>> GetBalancesAsync()
        {
            RequireAccount();
            var list = new List<Balance>();

            var spot = await InfoAsync(new JObject { ["type"] = "spotClearinghouseState", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            foreach (var b in spot?["balances"] as JArray ?? new JArray())
            {
                double total = HyperliquidWire.D(b["total"]), hold = HyperliquidWire.D(b["hold"]);
                if (total > 0) list.Add(new Balance(b["coin"]?.ToString() ?? "", total - hold, hold));
            }

            // A unified (or portfolio-margin) account trades perps from its spot balance, so the
            // perps side is the same money already listed above. Recorded 2026-11-26 from a public
            // unified account: perps margin used 978.32, spot USDC on hold 978.32. Listing both
            // counted it twice in the equity the quick trade sizes from.
            if (await PoolsSpotCollateralAsync(spot).ConfigureAwait(false)) return list;

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
            // Each builder's margin is its own balance on a classic account. Which builders there
            // are comes from the market list, loaded here if nothing else has loaded it yet.
            await EnsureAssetsAsync().ConfigureAwait(false);
            foreach (var (dex, state) in await ReadBuildersAsync("state", BuilderStateAsync, HoldsAnything).ConfigureAwait(false))
            {
                double withdrawable = HyperliquidWire.D(state?["withdrawable"]);
                double used = HyperliquidWire.D(state?["marginSummary"]?["totalMarginUsed"]);
                if (withdrawable > 0 || used > 0) list.Add(new Balance(HyperliquidWire.BuilderMarginAsset(dex), withdrawable, used));
            }
            return list;
        }

        // ── Builders' accounts (HIP-3) ───────────────────────────────────────

        /// <summary>The builders the account is read on: those with listed markets margined in USDC.</summary>
        private List<string> TradedBuilders() =>
            _perpByCoin.Values.Where(a => a.Dex.Length > 0 && a.Collateral == 0)
                .Select(a => a.Dex).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();

        /// <summary>
        /// One read per builder not resting quiet; a builder whose reply holds nothing rests for
        /// <see cref="BuilderQuietFor"/>. A failed read throws, so a caller never takes a partial
        /// list of positions for the whole.
        /// </summary>
        private async Task<List<(string Dex, T Reply)>> ReadBuildersAsync<T>(string kind, Func<string, Task<T>> read, Func<T, bool> holdsSomething)
        {
            var now = DateTime.UtcNow;
            var due = TradedBuilders().Where(d => !(_builderQuietUntil.TryGetValue(kind + ":" + d, out var until) && until > now)).ToList();
            var replies = await Task.WhenAll(due.Select(async d => (Dex: d, Reply: await read(d).ConfigureAwait(false)))).ConfigureAwait(false);
            foreach (var (dex, reply) in replies)
            {
                if (holdsSomething(reply)) _builderQuietUntil.TryRemove(kind + ":" + dex, out _);
                else _builderQuietUntil[kind + ":" + dex] = now + BuilderQuietFor;
            }
            return replies.ToList();
        }

        /// <summary>The next read of this builder asks again, whatever it last held.</summary>
        private void WakeBuilder(string dex)
        {
            if (dex.Length == 0) return;
            foreach (var key in _builderQuietUntil.Keys)
                if (key.EndsWith(":" + dex, StringComparison.Ordinal)) _builderQuietUntil.TryRemove(key, out _);
        }

        internal void WakeBuildersNamedIn(string frame)
        {
            if (_builderQuietUntil.IsEmpty || frame.IndexOf(':') < 0) return;
            foreach (Match m in BuilderCoinInFrame.Matches(frame)) WakeBuilder(m.Groups[1].Value);
        }

        private async Task<JObject?> BuilderStateAsync(string dex) =>
            await InfoAsync(new JObject { ["type"] = "clearinghouseState", ["user"] = _account, ["dex"] = dex }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;

        private async Task<JArray> OpenOrdersOnAsync(string dex)
        {
            var body = new JObject { ["type"] = "frontendOpenOrders", ["user"] = _account };
            if (dex.Length > 0) body["dex"] = dex;
            return await InfoAsync(body, _lightInfoLimiter).ConfigureAwait(false) as JArray ?? new JArray();
        }

        internal static bool HoldsAnything(JObject? state) =>
            (state?["assetPositions"] as JArray)?.Count > 0 || HyperliquidWire.D(state?["marginSummary"]?["accountValue"]) > 0;

        /// <summary>
        /// On a classic account each builder's margin is apart from the main perps margin, and the
        /// terminal does not move money: an order on a builder with none is refused in words before
        /// anything is signed. Unified and portfolio accounts draw on spot USDC; an unread mode
        /// lets the exchange decide.
        /// </summary>
        private async Task<string?> EmptyBuilderMarginRefusalAsync(AssetInfo asset)
        {
            if (await AccountModeAsync().ConfigureAwait(false) != "disabled") return null;
            var state = await BuilderStateAsync(asset.Dex).ConfigureAwait(false);
            if (HyperliquidWire.D(state?["marginSummary"]?["accountValue"]) > 0) return null;
            return $"your margin on {asset.Dex}, the builder that runs {asset.Display}, is empty. Hyperliquid keeps each builder's margin apart " +
                   $"from your main perpetuals margin, and the terminal does not move money: move USDC to {asset.Dex} on Hyperliquid's website first";
        }

        /// <summary>
        /// True when the account's perps draw on its spot balance: <c>userAbstraction</c> says
        /// "unifiedAccount" or "portfolioMargin" ("disabled" is the classic split, recorded
        /// 2026-11-26 from a public sub-account). Remembered for ten minutes. When the mode cannot
        /// be read, the spot reply is the clue: only a unified account's carries
        /// <c>tokenToAvailableAfterMaintenance</c> (also recorded 2026-11-26).
        /// </summary>
        private async Task<bool> PoolsSpotCollateralAsync(JObject? spot)
        {
            string? mode = await AccountModeAsync().ConfigureAwait(false);
            if (mode == null) return spot?["tokenToAvailableAfterMaintenance"] != null;
            return PoolsSpotCollateral(mode);
        }

        /// <summary><c>userAbstraction</c>, remembered for ten minutes; null when it cannot be read (and not remembered).</summary>
        private async Task<string?> AccountModeAsync()
        {
            var kept = _accountMode;
            string? mode = kept is { } k && k.Account == _account && DateTime.UtcNow - k.At < AccountModeTtl ? k.Mode : null;
            if (mode == null)
            {
                try
                {
                    var reply = await InfoAsync(new JObject { ["type"] = "userAbstraction", ["user"] = _account }, _lightInfoLimiter)
                        .ConfigureAwait(false);
                    if (reply.Type == JTokenType.String) mode = reply.ToString();
                }
                catch (Exception)
                {
                    // Not remembered, so the next read asks again.
                }
                if (mode != null) _accountMode = (_account, mode, DateTime.UtcNow);
            }
            return mode;
        }

        internal static bool PoolsSpotCollateral(string mode) =>
            mode is "unifiedAccount" or "portfolioMargin";

        public async Task<List<Position>> GetPositionsAsync()
        {
            RequireAccount();
            await EnsureAssetsAsync().ConfigureAwait(false);
            var state = await InfoAsync(new JObject { ["type"] = "clearinghouseState", ["user"] = _account }, _lightInfoLimiter)
                .ConfigureAwait(false) as JObject;
            var list = ParsePositions(state, _perpByCoin);
            foreach (var (_, builder) in await ReadBuildersAsync("state", BuilderStateAsync, HoldsAnything).ConfigureAwait(false))
                list.AddRange(ParsePositions(builder, _perpByCoin));
            return list;
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
            // One market's orders are read on its own builder only; all orders, on every builder.
            var market = symbol == null ? null : Find(symbol, null);
            var json = await OpenOrdersOnAsync(market?.Dex ?? "").ConfigureAwait(false);
            if (symbol == null)
                foreach (var (_, rows) in await ReadBuildersAsync("orders", OpenOrdersOnAsync, r => r.Count > 0).ConfigureAwait(false))
                    foreach (var row in rows) json.Add(row);
            string? want = symbol == null ? null : market?.Coin ?? symbol;
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
            var (qty, price) = await RestFillsAsync(oid).ConfigureAwait(false);
            return qty > 0 ? snapshot with { FilledPrice = price } : snapshot;
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
                if (HyperliquidWire.BuilderMarketRefusal(asset, _verifiedBuilders) is string builder) return $"ORDER_FAILED:{builder}";
                if (asset.Dex.Length > 0 && !signal.ReduceOnly
                    && await EmptyBuilderMarginRefusalAsync(asset).ConfigureAwait(false) is string empty)
                    return $"ORDER_FAILED:{empty}";
                WakeBuilder(asset.Dex);

                // A perp order that opens or adds always states its leverage. The ticket sends
                // none when its field says 1, and until 2026-11-10 none meant "leave the coin's
                // leverage alone", so a 1x ticket traded at whatever the account was last set to
                // (20x, say). Binance and MEXC read it the same way. A reduce-only order keeps
                // the position's leverage: changing it to close would be a surprise of its own.
                double? leverage = signal.Leverage is > 0 ? signal.Leverage
                                 : signal.ReduceOnly ? null : 1.0;
                bool setsLeverage = leverage != null && !asset.IsSpot;
                if (setsLeverage && HyperliquidWire.LeverageRefusal(asset, leverage!.Value) is string bad)
                    return $"ORDER_FAILED:{bad}";

                // The order is built (and its size and value checked) BEFORE the leverage is
                // changed, so an order refused here leaves the account's leverage as it was.
                var (wires, grouping, err) = await BuildOrdersAsync(signal, asset).ConfigureAwait(false);
                if (err != null) return $"ORDER_FAILED:{err}";

                // Leverage next, and a failure stops the order: trading at a leverage the user did
                // not ask for is the one outcome worse than not trading.
                if (setsLeverage)
                {
                    bool isolated = !string.Equals(signal.MarginType, "Cross", StringComparison.OrdinalIgnoreCase);
                    string? lerr = await UpdateLeverageAsync(asset, leverage!.Value, isCross: !isolated).ConfigureAwait(false);
                    if (lerr != null) return $"ORDER_FAILED:{lerr}";
                }

                var reply = await ExchangeAsync(HyperliquidWire.OrderAction(wires, grouping), expires: true).ConfigureAwait(false);
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
                    double px, valueAt;
                    OrderedMap type;
                    if (signal.Type == OrderType.Limit)
                    {
                        if (signal.Price is not > 0) return (new(), "na", "a limit order needs a price");
                        px = valueAt = signal.Price.Value;
                        type = HyperliquidWire.LimitType(tif ?? "Gtc");
                    }
                    else
                    {
                        double? mid = await GetMidAsync(asset).ConfigureAwait(false);
                        if (mid is not > 0) return (new(), "na", $"no current price for {asset.Display} to bound a market order");
                        px = SlippedPrice(mid.Value, buy);
                        valueAt = mid.Value;
                        type = HyperliquidWire.LimitType("Ioc");   // a market order is an IOC limit
                    }
                    // Valued at the limit, or at the middle price for a market order (the slipped
                    // bound is only the worst fill it may take).
                    if (MinimumValueRefusal(signal, asset, size, valueAt) is string small)
                        return (new(), "na", small);
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
                    if (MinimumValueRefusal(signal, asset, size, trigger.Value) is string small)
                        return (new(), "na", small);
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

        /// <summary>
        /// Why an order is under Hyperliquid's $10 minimum, in words, or null. A reduce-only order
        /// is left to the exchange: refusing a small close here could leave a position the user
        /// cannot get out of, and closing what is left is the one small order worth trying.
        /// </summary>
        private static string? MinimumValueRefusal(TradeSignal signal, AssetInfo asset, decimal size, double price) =>
            signal.ReduceOnly ? null : HyperliquidWire.OrderRules(asset).Refusal((double)size, price);

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
            var mids = await InfoAsync(WithDex(new JObject { ["type"] = "allMids" }, asset), _lightInfoLimiter).ConfigureAwait(false) as JObject;
            double v = HyperliquidWire.D(mids?[asset.Coin]);
            return v > 0 ? v : null;
        }

        /// <summary>Sets leverage and margin mode for one market. Returns an error message or null.
        /// A fraction or an amount past the market's own ceiling is refused here, before
        /// anything is signed (see <see cref="HyperliquidWire.LeverageRefusal"/>).</summary>
        private async Task<string?> UpdateLeverageAsync(AssetInfo asset, double leverage, bool isCross)
        {
            if (HyperliquidWire.LeverageRefusal(asset, leverage) is string bad) return bad;
            int want = (int)leverage;
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
                if (HyperliquidWire.BuilderMarketRefusal(asset, _verifiedBuilders) is string builder) { SurfaceError(builder, ErrorSeverity.Medium, ErrorCategory.Provider); return 1.0; }
                // Isolated unless the user chose otherwise elsewhere: a liquidation then takes one
                // position, not the account.
                string? err = await UpdateLeverageAsync(asset, leverage, isCross: false).ConfigureAwait(false);
                if (err != null) { SurfaceError(err, ErrorSeverity.Medium, ErrorCategory.Provider); return 1.0; }
                return (int)leverage;
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
                WakeBuilder(asset.Dex);
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
                if (HyperliquidWire.BuilderMarketRefusal(asset, _verifiedBuilders) is string builder) return $"ORDER_FAILED:{builder}";
                WakeBuilder(asset.Dex);
                var (wires, _, err) = await BuildOrdersAsync(replacement, asset).ConfigureAwait(false);
                if (err != null) return $"ORDER_FAILED:{err}";

                var reply = await ExchangeAsync(HyperliquidWire.ModifyAction(oid, wires[0]), expires: true).ConfigureAwait(false);
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
        /// <para><paramref name="expires"/> (orders and modifies): the action carries
        /// <c>expiresAfter</c>, signed, <see cref="OrderExpiry"/> past its nonce, so an order held
        /// up on the way is refused rather than placed at a price that has moved. Cancels, leverage
        /// and the dead-man's switch carry none: a late cancel is still wanted.</para>
        /// </summary>
        private async Task<JObject> ExchangeAsync(OrderedMap action, bool expires = false)
        {
            if (!CanSign) throw new InvalidOperationException("No Hyperliquid agent wallet is configured.");
            ulong nonce = NextNonce();
            ulong? expiresAfter = expires ? nonce + (ulong)OrderExpiry.TotalMilliseconds : null;
            var sig = HyperliquidSigner.SignL1Action(_agentKey, action, null, nonce, _isMainnet, expiresAfter);
            var payload = new JObject
            {
                ["action"] = HyperliquidWire.ToJson(action),
                ["nonce"] = nonce,
                ["signature"] = new JObject { ["r"] = sig.R, ["s"] = sig.S, ["v"] = sig.V },
            };
            if (expiresAfter.HasValue) payload["expiresAfter"] = expiresAfter.Value;

            await _exchangeLimiter.WaitAsync().ConfigureAwait(false);
            using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync($"{Rest}/exchange", content).ConfigureAwait(false);
            string text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);

            JObject? json = null;
            try { json = JObject.Parse(text); } catch (JsonReaderException) { }
            // A refusal the exchange explains keeps its explanation whatever the status code.
            if (json != null && json["status"] != null)
            {
                // Hyperliquid's wording for an expired action is not recorded (a test from a wallet
                // with no account is refused before the expiry is checked), so any refusal that
                // speaks of expiry gets the likely cause added: the computer's clock.
                if (expiresAfter.HasValue && json["status"]?.ToString() == "err"
                    && json["response"]?.Type == JTokenType.String
                    && json["response"]!.ToString().Contains("expir", StringComparison.OrdinalIgnoreCase))
                    json["response"] = json["response"] + ExpiredHint;
                return json;
            }
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"Hyperliquid refused the order request: HTTP {(int)res.StatusCode}{Excerpt(text)}", null, res.StatusCode);
            return json ?? throw new HttpRequestException("Hyperliquid answered the order request with a non-JSON body.");
        }

        /// <summary>How long a signed order or modify stays good (<c>expiresAfter</c>, past the nonce).</summary>
        internal static readonly TimeSpan OrderExpiry = TimeSpan.FromSeconds(60);

        internal const string ExpiredHint =
            ". The order is stamped to expire 60 seconds after it is signed, by this computer's clock; " +
            "if this keeps happening, that clock is probably wrong. Set it to update automatically.";

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
