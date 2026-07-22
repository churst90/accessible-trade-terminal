using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;
using Mexc.Net;
using Mexc.Net.Clients;

namespace AccessibleTrader.Plugins.Mexc
{
    public class MexcProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        private readonly MexcRestClient _client;
        private MexcRestClient? _tradingClient;
        private MexcSocketClient? _socketClient;
        private IDisposable? _currentSubscription;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        private string? _apiKey;
        private string? _apiSecret;

        // MEXC spot: 500 requests / 10 seconds per IP. Futures: 20 req/2s per UID.
        // Stay conservative with 1000/min — aligns with other providers and leaves headroom.
        private readonly RateLimiter _rateLimiter = new(1000, TimeSpan.FromMinutes(1));

        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private IDisposable? _orderBookSubscription;
        private string? _orderBookSymbol;

        private string? _listenKey;
        private System.Timers.Timer? _keepAliveTimer;

        public override string Name => "MEXC";
        public override string Description => "Live MEXC Exchange Data (Spot & Futures). Not officially available in the US — review provider ToS before use.";
        public override List<MarketType> SupportedMarkets => new List<MarketType> { MarketType.Crypto };
        public override bool SupportsSymbolSearch => true;
        public override bool RequiresApiKey => false;
        public override bool IsConfigured => true;
        public override bool SupportsLiveUpdates => true;
        // Kline updates re-send the current candle with CUMULATIVE volume-so-far;
        // consolidation must diff, not accumulate (see LiveTickStyle docs).
        public override AccessibleTrader.Sdk.Plugins.LiveTickStyle LiveTickStyle => AccessibleTrader.Sdk.Plugins.LiveTickStyle.CumulativeBars;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 500;
        public override ProviderCapabilities Capabilities =>
            ProviderCapabilities.L2 | ProviderCapabilities.MarketDepth |
            ProviderCapabilities.Leverage | ProviderCapabilities.Brackets;

        public override bool SupportsMarginTrading  => false;
        public override bool SupportsFuturesTrading => true;
        public override bool SupportsStopLoss       => true;
        public override bool SupportsTakeProfit     => true;
        public override double MaxLeverage          => 200.0;

        public bool IsConnected => !string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null;

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute,
            StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes,
            StandardTimeframes.ThirtyMinutes,
            StandardTimeframes.OneHour,
            StandardTimeframes.FourHours,
            StandardTimeframes.OneDay,
            StandardTimeframes.OneWeek,
            StandardTimeframes.OneMonth
        };

        public MexcProvider()
        {
            _client = new MexcRestClient();
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
            if (config.TryGetValue("ApiKey",    out var key))    _apiKey    = key;
            if (config.TryGetValue("ApiSecret", out var secret)) _apiSecret = secret;
        }

        // Sign-time credential checkout. Prefers the PluginHostServices.ApiKeys bridge
        // (phase 4 Track B); falls back to Configure-populated fields for CLI / tests.
        private async Task<(string Key, string Secret)> CheckoutMexcCredentialsAsync()
        {
            var host = PluginHostServices.ApiKeys;
            if (host != null)
            {
                var checkout = await host.CheckoutAsync("MEXC").ConfigureAwait(false);
                if (!checkout.HasCredentials)
                    throw new InvalidOperationException("MEXC: no active API key configured.");
                return (checkout.Key, checkout.Secret);
            }

            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_apiSecret))
                throw new InvalidOperationException("MEXC: no API credentials configured.");
            return (_apiKey!, _apiSecret!);
        }

        private async Task EnsureTradingClientAsync()
        {
            if (_tradingClient != null) return;
            var (apiKey, apiSecret) = await CheckoutMexcCredentialsAsync().ConfigureAwait(false);
            _tradingClient = new MexcRestClient(opts =>
            {
                opts.ApiCredentials = new MexcCredentials(apiKey, apiSecret);
            });
        }

        private MexcRestClient TradingClient => _tradingClient ?? _client;

        public override async Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
        {
            if (string.IsNullOrEmpty(_apiKey) && PluginHostServices.ApiKeys == null)
                return (true, "No API key provided (public data only)");
            try
            {
                await EnsureTradingClientAsync();
                var result = await TradingClient.SpotApi.Account.GetAccountInfoAsync();
                return result.Success
                    ? (true, "API key validated successfully")
                    : (false, $"Key validation failed: {result.Error?.Message}");
            }
            catch (Exception ex) { return (false, $"Key validation error: {ex.Message}"); }
        }

        // --- Connection & Subscription Management ---

        public override async Task EnsureConnectedAsync()
        {
            if (_socketClient != null) return;
            _socketClient = new MexcSocketClient();
            _connectionStateStream.OnNext(ConnectionState.Connected);

            if (!string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null)
            {
                try
                {
                    await EnsureTradingClientAsync();
                    await StartUserDataStreamAsync();
                }
                catch
                {
                    // No creds available — public-data-only mode.
                }
            }
        }

        private async Task StartUserDataStreamAsync()
        {
            try
            {
                var keyResult = await TradingClient.SpotApi.Account.StartUserStreamAsync();
                if (!keyResult.Success || string.IsNullOrEmpty(keyResult.Data)) return;
                _listenKey = keyResult.Data;

                await _socketClient!.SpotApi.SubscribeToOrderUpdatesAsync(_listenKey, data =>
                {
                    var o = data.Data;
                    var status = o.Status switch
                    {
                        global::Mexc.Net.Enums.OrderStatus.PartiallyFilled => OrderStatus.PartialFill,
                        global::Mexc.Net.Enums.OrderStatus.Filled          => OrderStatus.Filled,
                        global::Mexc.Net.Enums.OrderStatus.Canceled        => OrderStatus.Cancelled,
                        global::Mexc.Net.Enums.OrderStatus.PartiallyCanceled => OrderStatus.Cancelled,
                        _                                                   => OrderStatus.Triggered
                    };
                    _orderUpdateSubject.OnNext(new OrderUpdate(
                        o.OrderId ?? string.Empty,
                        _currentSymbol ?? string.Empty,
                        o.Side == global::Mexc.Net.Enums.OrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                        (double)(o.CumulativeQuantity ?? 0m),
                        (double)((o.AveragePrice ?? 0m) == 0m ? o.Price : (o.AveragePrice ?? 0m)),
                        (double)o.QuantityRemaining,
                        status,
                        StopTriggered: false,
                        TakeProfitTriggered: false,
                        DateTime.UtcNow));
                });

                // MEXC listen key TTL is 60 minutes; refresh every 30 min.
                _keepAliveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
                _keepAliveTimer.Elapsed += async (_, _) =>
                {
                    try { await TradingClient.SpotApi.Account.KeepAliveUserStreamAsync(_listenKey!); }
                    catch (Exception ex)
                    {
                        _errorStream.OnNext($"MEXC user-data keep-alive failed: {ex.GetType().Name}");
                    }
                };
                _keepAliveTimer.AutoReset = true;
                _keepAliveTimer.Start();
            }
            catch (Exception ex)
            {
                _errorStream.OnNext($"MEXC user-data stream unavailable ({ex.GetType().Name}): order-fill updates won't be delivered.");
            }
        }

        // ── Keyed-feed subscriptions (multiple concurrent live streams) ───────

        public override bool SupportsMultipleLiveSubscriptions => true;

        /// <summary>
        /// The JK.Mexc.Net socket client natively supports many concurrent kline
        /// subscriptions on its managed connections — each SubscribeLiveAsync is
        /// its own SDK subscription with its own consolidator; disposing the
        /// handle closes just that subscription. The SDK owns reconnection.
        /// </summary>
        public override async Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
        {
            if (_socketClient == null) await EnsureConnectedAsync();
            var cleanSymbol = CleanSymbol(symbol);
            var consolidator = new BarBucketConsolidator(timeframe, LiveTickStyle.CumulativeBars);
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            IDisposable subscription;
            if (isFutures)
            {
                var result = await _socketClient!.FuturesApi.SubscribeToKlineUpdatesAsync(
                    ToFuturesSymbol(cleanSymbol), MapFuturesInterval(timeframe), data =>
                    {
                        var k = data.Data;
                        var raw = new Ohlcv(k.OpenTime,
                            (double)k.OpenPrice, (double)k.HighPrice, (double)k.LowPrice,
                            (double)k.ClosePrice, (double)k.Volume);
                        if (IsEmptyBar(raw)) return;
                        var bar = consolidator.Apply(raw);
                        if (bar.HasValue) onBar(bar.Value);
                    });
                if (!result.Success)
                    throw new InvalidOperationException($"MEXC futures kline subscription failed: {result.Error?.Message}");
                subscription = (IDisposable)result.Data;
            }
            else
            {
                var result = await _socketClient!.SpotApi.SubscribeToKlineUpdatesAsync(
                    cleanSymbol, MapSpotInterval(timeframe), data =>
                    {
                        var k = data.Data;
                        var raw = new Ohlcv(k.StartTime,
                            (double)k.OpenPrice, (double)k.HighPrice, (double)k.LowPrice,
                            (double)k.ClosePrice, (double)k.Volume);
                        if (IsEmptyBar(raw)) return;
                        var bar = consolidator.Apply(raw);
                        if (bar.HasValue) onBar(bar.Value);
                    });
                if (!result.Success)
                    throw new InvalidOperationException($"MEXC spot kline subscription failed: {result.Error?.Message}");
                subscription = (IDisposable)result.Data;
            }

            return new SdkSubscriptionHandle(subscription);
        }

        private sealed class SdkSubscriptionHandle : IAsyncDisposable
        {
            private IDisposable? _subscription;
            public SdkSubscriptionHandle(IDisposable subscription) { _subscription = subscription; }
            public ValueTask DisposeAsync()
            {
                System.Threading.Interlocked.Exchange(ref _subscription, null)?.Dispose();
                return ValueTask.CompletedTask;
            }
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var cleanSymbol = CleanSymbol(symbol);
            if (_currentSymbol == cleanSymbol && _currentTimeframe == timeframe && _currentSubscription != null) return;

            if (_currentSubscription != null)
            {
                _currentSubscription.Dispose();
                _currentSubscription = null;
            }

            _currentSymbol = cleanSymbol;
            _currentTimeframe = timeframe;
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            if (isFutures)
            {
                var futuresSymbol = ToFuturesSymbol(cleanSymbol);
                var subResult = await _socketClient!.FuturesApi.SubscribeToKlineUpdatesAsync(
                    futuresSymbol, MapFuturesInterval(timeframe), data =>
                    {
                        var k = data.Data;
                        var bar = new Ohlcv(
                            k.OpenTime,
                            (double)k.OpenPrice,
                            (double)k.HighPrice,
                            (double)k.LowPrice,
                            (double)k.ClosePrice,
                            (double)k.Volume);
                        if (IsEmptyBar(bar)) return;
                        _liveStream.OnNext(bar);
                    });

                if (subResult.Success)
                    _currentSubscription = (IDisposable)subResult.Data;
                else
                {
                    _errorStream.OnNext($"MEXC Futures subscription failed: {subResult.Error?.Message}");
                    _connectionStateStream.OnNext(ConnectionState.Error);
                }
            }
            else
            {
                var subResult = await _socketClient!.SpotApi.SubscribeToKlineUpdatesAsync(
                    cleanSymbol, MapSpotInterval(timeframe), data =>
                    {
                        var k = data.Data;
                        var bar = new Ohlcv(
                            k.StartTime,
                            (double)k.OpenPrice,
                            (double)k.HighPrice,
                            (double)k.LowPrice,
                            (double)k.ClosePrice,
                            (double)k.Volume);
                        if (IsEmptyBar(bar)) return;
                        _liveStream.OnNext(bar);
                    });

                if (subResult.Success)
                    _currentSubscription = (IDisposable)subResult.Data;
                else
                {
                    _errorStream.OnNext($"MEXC Spot subscription failed: {subResult.Error?.Message}");
                    _connectionStateStream.OnNext(ConnectionState.Error);
                }
            }
        }

        public override async Task DisconnectAsync()
        {
            _keepAliveTimer?.Stop();
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;

            _orderBookSubscription?.Dispose();
            _orderBookSubscription = null;
            _orderBookSymbol = null;

            _currentSubscription?.Dispose();
            _currentSubscription = null;

            if (_socketClient != null)
            {
                await _socketClient.UnsubscribeAllAsync();
                _socketClient.Dispose();
                _socketClient = null;
            }

            if (!string.IsNullOrEmpty(_listenKey) && _tradingClient != null)
            {
                try { await _tradingClient.SpotApi.Account.StopUserStreamAsync(_listenKey); }
                catch { /* best-effort */ }
            }
            _listenKey = null;

            _tradingClient?.Dispose();
            _tradingClient = null;

            _currentSymbol = null;
            _currentTimeframe = null;

            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // --- Data Discovery & Fetching ---

        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) =>
            Task.FromResult(new List<string> { "Spot", "Futures" });

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    if (subType.Equals("Futures", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await _client.FuturesApi.ExchangeData.GetSymbolsAsync();
                        if (!result.Success || result.Data == null) return new List<string>();
                        return result.Data.Select(s => s.Symbol).OrderBy(s => s).ToList();
                    }
                    else
                    {
                        var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(null);
                        if (!result.Success || result.Data == null) return new List<string>();
                        return result.Data.Symbols.Select(s => s.Name).OrderBy(s => s).ToList();
                    }
                });
            }
            catch { return new List<string>(); }
        }

        public override Task<List<string>> GetSupportedTimeframesAsync() =>
            Task.FromResult(new List<string> { "1m", "5m", "15m", "30m", "1h", "4h", "1d", "1w", "1M" });

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = CleanSymbol(request.Symbol);
            bool isFutures = request.Market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            int limit = Math.Min(request.Limit, 500);

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    DateTime? startTime = request.Since != null ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime : null;
                    DateTime? endTime   = request.Until != null ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime : null;

                    // MEXC spot klines ignore a single time bound — if only startTime
                    // or only endTime is set, the endpoint silently returns "latest N"
                    // instead of a historical window. Compute the missing bound from
                    // the known bound + limit * bar-duration so paginated backfill
                    // calls actually walk backwards in time.
                    var barDuration = TimeframeDuration(request.Timeframe);
                    if (startTime.HasValue && !endTime.HasValue)
                    {
                        var computed = startTime.Value.Add(TimeSpan.FromTicks(barDuration.Ticks * limit));
                        endTime = computed > DateTime.UtcNow ? DateTime.UtcNow : computed;
                    }
                    else if (!startTime.HasValue && endTime.HasValue)
                    {
                        startTime = endTime.Value.Subtract(TimeSpan.FromTicks(barDuration.Ticks * limit));
                    }

                    if (isFutures)
                    {
                        var futuresSymbol = ToFuturesSymbol(cleanSymbol);
                        var r = await _client.FuturesApi.ExchangeData.GetKlinesAsync(
                            futuresSymbol, MapFuturesInterval(request.Timeframe), startTime, endTime);
                        if (!r.Success || r.Data == null) return (new List<Ohlcv>(), new List<(long, double)>());
                        var ohlcv = r.Data.Select(k => new Ohlcv(
                            k.OpenTime.ToUniversalTime(),
                            (double)k.OpenPrice, (double)k.HighPrice, (double)k.LowPrice, (double)k.ClosePrice,
                            (double)k.Volume)).ToList();
                        return (ohlcv, ohlcv.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                    }
                    else
                    {
                        var r = await _client.SpotApi.ExchangeData.GetKlinesAsync(
                            cleanSymbol, MapSpotInterval(request.Timeframe), startTime, endTime, limit);
                        if (!r.Success || r.Data == null) return (new List<Ohlcv>(), new List<(long, double)>());
                        var ohlcv = r.Data.Select(k => new Ohlcv(
                            k.OpenTime.ToUniversalTime(),
                            (double)k.OpenPrice, (double)k.HighPrice, (double)k.LowPrice, (double)k.ClosePrice,
                            (double)k.Volume)).ToList();
                        return (ohlcv, ohlcv.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
                    }
                });
            }
            catch { return (new List<Ohlcv>(), new List<(long, double)>()); }
        }

        public override async Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
        {
            var cleanSymbol = CleanSymbol(symbol);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    var result = await _client.SpotApi.ExchangeData.GetOrderBookAsync(cleanSymbol, limit);
                    if (!result.Success || result.Data == null) return (new List<OrderBookEntry>(), new List<OrderBookEntry>());
                    return (
                        result.Data.Bids.Select(b => new OrderBookEntry((double)b.Price, (double)b.Quantity)).ToList(),
                        result.Data.Asks.Select(a => new OrderBookEntry((double)a.Price, (double)a.Quantity)).ToList()
                    );
                });
            }
            catch { return (new List<OrderBookEntry>(), new List<OrderBookEntry>()); }
        }

        // ── IOrderBookProvider ─────────────────────────────────────────────────

        async Task<OrderBookSnapshot> IOrderBookProvider.GetOrderBookAsync(string symbol, int depth)
        {
            var (bids, asks) = await GetOrderBookAsync(symbol, depth);
            return new OrderBookSnapshot(symbol, bids, asks, 0, DateTime.UtcNow);
        }

        public IObservable<OrderBookUpdate> SubscribeOrderBook(string symbol)
        {
            var cleanSymbol = CleanSymbol(symbol);
            if (_orderBookSymbol == cleanSymbol && _orderBookSubscription != null)
                return _orderBookSubject.AsObservable();

            _orderBookSubscription?.Dispose();
            _orderBookSymbol = cleanSymbol;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (_socketClient == null) await EnsureConnectedAsync();
                    var subResult = await _socketClient!.SpotApi.SubscribeToPartialOrderBookUpdatesAsync(
                        cleanSymbol, 20, data =>
                        {
                            var bids = data.Data.Bids.Select(b => new OrderBookEntry((double)b.Price, (double)b.Quantity)).ToList();
                            var asks = data.Data.Asks.Select(a => new OrderBookEntry((double)a.Price, (double)a.Quantity)).ToList();
                            _orderBookSubject.OnNext(new OrderBookUpdate(cleanSymbol, bids, asks, 0, DateTime.UtcNow));
                        });

                    if (subResult.Success)
                        _orderBookSubscription = (IDisposable)subResult.Data;
                    else
                        _errorStream.OnNext($"MEXC order book subscription failed: {subResult.Error?.Message}");
                }
                catch (Exception ex) { _errorStream.OnNext($"MEXC order book error: {ex.Message}"); }
            });

            return _orderBookSubject.AsObservable();
        }

        // ── ITradingProvider ────────────────────────────────────────────────

        public async Task<List<Balance>> GetBalancesAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();
                    var result = await TradingClient.SpotApi.Account.GetAccountInfoAsync();
                    if (!result.Success || result.Data == null) return new List<Balance>();
                    return result.Data.Balances
                        .Where(b => b.Available > 0 || b.Locked > 0)
                        .Select(b => new Balance(b.Asset, (double)b.Available, (double)b.Locked))
                        .ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<List<Position>> GetPositionsAsync()
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();
                    var result = await TradingClient.FuturesApi.Trading.GetPositionsAsync(null);
                    if (!result.Success || result.Data == null) return new List<Position>();
                    return result.Data
                        .Where(p => p.PositionSize != 0)
                        .Select(p =>
                        {
                            double qty  = (double)p.PositionSize;
                            double avg  = (double)p.HoldAveragePrice;
                            double pnl  = (double)(p.Pnl ?? 0m);
                            double liq  = (double)p.LiquidationPrice;
                            double lev  = (double)p.Leverage;
                            double mv   = Math.Abs(qty) * avg;
                            return new Position(p.Symbol, qty, avg, mv, pnl, lev, liq);
                        })
                        .ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<List<OpenOrder>> GetOpenOrdersAsync(string? symbol = null)
        {
            if (!IsConnected) return new();
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();
                    // MEXC spot GetOpenOrdersAsync requires a symbol — callers must supply one.
                    if (string.IsNullOrEmpty(symbol)) return new List<OpenOrder>();

                    var result = await TradingClient.SpotApi.Trading.GetOpenOrdersAsync(CleanSymbol(symbol));
                    if (!result.Success || result.Data == null) return new List<OpenOrder>();
                    return result.Data.Select(o => new OpenOrder(
                        o.OrderId,
                        o.Symbol,
                        o.Side == global::Mexc.Net.Enums.OrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                        MapOrderType(o.OrderType),
                        (double)o.Quantity,
                        (double)o.Price,
                        o.Status.ToString()
                    )).ToList();
                });
            }
            catch { return new(); }
        }

        public async Task<string> PlaceOrderAsync(TradeSignal signal)
        {
            if (!IsConnected) return "PROVIDER_NOT_CONFIGURED";
            bool isFutures = string.Equals(signal.SubType, "Futures", StringComparison.OrdinalIgnoreCase);
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();

                    if (isFutures)
                        return await PlaceFuturesOrderAsync(signal);

                    return await PlaceSpotOrderAsync(signal);
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"MEXC order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        private async Task<string> PlaceSpotOrderAsync(TradeSignal signal)
        {
            var symbol = CleanSymbol(signal.Symbol);
            var side   = signal.Side == OrderSide.Buy
                ? global::Mexc.Net.Enums.OrderSide.Buy
                : global::Mexc.Net.Enums.OrderSide.Sell;

            // MEXC spot REST only exposes Market / Limit / LimitMaker / IOC / FOK.
            // Stop / take-profit on spot is not wrapped by this library — reject those.
            var type = signal.Type switch
            {
                OrderType.Market => global::Mexc.Net.Enums.OrderType.Market,
                OrderType.Limit  => global::Mexc.Net.Enums.OrderType.Limit,
                _                => (global::Mexc.Net.Enums.OrderType?)null
            };
            if (type == null)
                return "ORDER_FAILED:MEXC spot only supports Market/Limit via this plugin (use Futures for stop/TP).";

            if (type == global::Mexc.Net.Enums.OrderType.Limit && !signal.Price.HasValue)
                return "ORDER_FAILED:Limit order requires Price.";

            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                side,
                type.Value,
                quantity: (decimal)signal.Quantity,
                quoteQuantity: null,
                price: signal.Price.HasValue ? (decimal?)signal.Price.Value : null,
                clientOrderId: signal.ClientOid);

            return r.Success ? r.Data.OrderId : $"ORDER_FAILED:{r.Error?.Message}";
        }

        private async Task<string> PlaceFuturesOrderAsync(TradeSignal signal)
        {
            var futuresSymbol = ToFuturesSymbol(CleanSymbol(signal.Symbol));

            // FuturesOrderSide encodes open/close + long/short in a single enum.
            // Default: Buy → OpenLong, Sell → OpenShort (new position).
            var futSide = signal.Side == OrderSide.Buy
                ? global::Mexc.Net.Enums.FuturesOrderSide.OpenLong
                : global::Mexc.Net.Enums.FuturesOrderSide.OpenShort;

            var futType = signal.Type switch
            {
                OrderType.Limit            => global::Mexc.Net.Enums.FuturesOrderType.Limit,
                OrderType.Market           => global::Mexc.Net.Enums.FuturesOrderType.Market,
                OrderType.StopMarket       => global::Mexc.Net.Enums.FuturesOrderType.Market,
                OrderType.StopLimit        => global::Mexc.Net.Enums.FuturesOrderType.Limit,
                OrderType.TakeProfitMarket => global::Mexc.Net.Enums.FuturesOrderType.Market,
                OrderType.TakeProfitLimit  => global::Mexc.Net.Enums.FuturesOrderType.Limit,
                _                          => global::Mexc.Net.Enums.FuturesOrderType.Market
            };

            int? leverage = signal.Leverage.HasValue && signal.Leverage.Value > 1
                ? (int)Math.Clamp(signal.Leverage.Value, 1, MaxLeverage)
                : (int?)null;

            // MEXC requires a MarginType when leverage is specified on an isolated position.
            // Default to Isolated unless the caller explicitly asked for Cross.
            global::Mexc.Net.Enums.MarginType? marginType = null;
            if (leverage.HasValue)
            {
                marginType = string.Equals(signal.MarginType, "Cross", StringComparison.OrdinalIgnoreCase)
                    ? global::Mexc.Net.Enums.MarginType.Cross
                    : global::Mexc.Net.Enums.MarginType.Isolated;
            }

            var r = await TradingClient.FuturesApi.Trading.PlaceOrderAsync(
                futuresSymbol,
                futSide,
                futType,
                quantity: (decimal)signal.Quantity,
                price: signal.Price.HasValue ? (decimal?)signal.Price.Value : null,
                leverage: leverage,
                marginType: marginType,
                clientOrderId: signal.ClientOid ?? string.Empty,
                positionId: null,
                takeProfitPrice: signal.TakeProfit.HasValue ? (decimal?)signal.TakeProfit.Value : null,
                stopLossPrice:   signal.StopLoss.HasValue   ? (decimal?)signal.StopLoss.Value   : null);

            return r.Success ? r.Data.OrderId.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var r = await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();

                    // Try spot first; if it fails and the orderId parses as a futures long ID, try futures.
                    var spot = await TradingClient.SpotApi.Trading.CancelOrderAsync(
                        CleanSymbol(symbol), orderId, string.Empty, string.Empty);
                    if (spot.Success) return true;

                    if (long.TryParse(orderId, out var futOrderId))
                    {
                        var fut = await TradingClient.FuturesApi.Trading.CancelOrdersAsync(new[] { futOrderId });
                        return fut.Success;
                    }
                    return false;
                });
                return r;
            }
            catch { return false; }
        }

        public async Task<double> SetLeverageAsync(string symbol, double leverage)
        {
            if (!IsConnected) return 1.0;
            try
            {
                await EnsureTradingClientAsync();
                int lev = (int)Math.Clamp(leverage, 1, MaxLeverage);
                var r = await TradingClient.FuturesApi.Account.SetLeverageAsync(
                    leverage: lev,
                    positionId: null,
                    marginType: global::Mexc.Net.Enums.MarginType.Isolated,
                    symbol: ToFuturesSymbol(CleanSymbol(symbol)),
                    positionSide: null);
                return r.Success ? lev : 1.0;
            }
            catch { return 1.0; }
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static TimeSpan TimeframeDuration(string tf) => tf switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h"  => TimeSpan.FromHours(1),
            "4h"  => TimeSpan.FromHours(4),
            "8h"  => TimeSpan.FromHours(8),
            "1d"  => TimeSpan.FromDays(1),
            "1w"  => TimeSpan.FromDays(7),
            "1M"  => TimeSpan.FromDays(30),
            _     => TimeSpan.FromHours(1)
        };

        private static bool IsEmptyBar(Ohlcv bar) =>
            bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
            && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime);

        // MEXC futures uses underscore-separated pairs (e.g. BTC_USDT). Spot uses concatenated (BTCUSDT).
        // CleanSymbol produces the spot shape; this helper converts to the futures shape.
        private static string ToFuturesSymbol(string spotSymbol)
        {
            if (spotSymbol.Contains('_')) return spotSymbol;
            foreach (var quote in new[] { "USDT", "USDC", "USD", "BTC", "ETH" })
            {
                if (spotSymbol.EndsWith(quote, StringComparison.OrdinalIgnoreCase) && spotSymbol.Length > quote.Length)
                    return $"{spotSymbol[..^quote.Length]}_{quote}";
            }
            return spotSymbol;
        }

        private static OrderType MapOrderType(global::Mexc.Net.Enums.OrderType type) => type switch
        {
            global::Mexc.Net.Enums.OrderType.Limit             => OrderType.Limit,
            global::Mexc.Net.Enums.OrderType.Market            => OrderType.Market,
            global::Mexc.Net.Enums.OrderType.LimitMaker        => OrderType.Limit,
            global::Mexc.Net.Enums.OrderType.ImmediateOrCancel => OrderType.Limit,
            global::Mexc.Net.Enums.OrderType.FillOrKill        => OrderType.Limit,
            global::Mexc.Net.Enums.OrderType.TpSlOrder         => OrderType.StopMarket,
            _ => OrderType.Market
        };

        private static global::Mexc.Net.Enums.KlineInterval MapSpotInterval(string tf) => tf switch
        {
            "1m"  => global::Mexc.Net.Enums.KlineInterval.OneMinute,
            "5m"  => global::Mexc.Net.Enums.KlineInterval.FiveMinutes,
            "15m" => global::Mexc.Net.Enums.KlineInterval.FifteenMinutes,
            "30m" => global::Mexc.Net.Enums.KlineInterval.ThirtyMinutes,
            "1h"  => global::Mexc.Net.Enums.KlineInterval.OneHour,
            "4h"  => global::Mexc.Net.Enums.KlineInterval.FourHours,
            "1d"  => global::Mexc.Net.Enums.KlineInterval.OneDay,
            "1w"  => global::Mexc.Net.Enums.KlineInterval.OneWeek,
            "1M"  => global::Mexc.Net.Enums.KlineInterval.OneMonth,
            _     => global::Mexc.Net.Enums.KlineInterval.OneHour
        };

        private static global::Mexc.Net.Enums.FuturesKlineInterval MapFuturesInterval(string tf) => tf switch
        {
            "1m"  => global::Mexc.Net.Enums.FuturesKlineInterval.OneMinute,
            "5m"  => global::Mexc.Net.Enums.FuturesKlineInterval.FiveMinutes,
            "15m" => global::Mexc.Net.Enums.FuturesKlineInterval.FifteenMinutes,
            "30m" => global::Mexc.Net.Enums.FuturesKlineInterval.ThirtyMinutes,
            "1h"  => global::Mexc.Net.Enums.FuturesKlineInterval.OneHour,
            "4h"  => global::Mexc.Net.Enums.FuturesKlineInterval.FourHours,
            "8h"  => global::Mexc.Net.Enums.FuturesKlineInterval.EightHours,
            "1d"  => global::Mexc.Net.Enums.FuturesKlineInterval.OneDay,
            "1w"  => global::Mexc.Net.Enums.FuturesKlineInterval.OneWeek,
            "1M"  => global::Mexc.Net.Enums.FuturesKlineInterval.OneMonth,
            _     => global::Mexc.Net.Enums.FuturesKlineInterval.OneHour
        };
    }
}
