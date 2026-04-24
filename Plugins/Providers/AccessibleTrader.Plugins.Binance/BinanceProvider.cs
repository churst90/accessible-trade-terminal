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
using Binance.Net.Clients;

namespace AccessibleTrader.Plugins.Binance
{
    public class BinanceProvider : BaseMarketDataProvider, IProviderPlugin, ITradingProvider, IOrderBookProvider
    {
        private readonly BinanceRestClient _client;
        private BinanceRestClient? _tradingClient;
        private BinanceSocketClient? _socketClient;
        private IDisposable? _currentSubscription;
        private string? _currentSymbol;
        private string? _currentTimeframe;

        private string? _apiKey;
        private string? _apiSecret;
        private bool _isTestnet = false;

        // Rate limiter: Binance allows 1200 requests/minute for REST
        private readonly RateLimiter _rateLimiter = new(1200, TimeSpan.FromMinutes(1));

        // Order update stream
        private readonly Subject<OrderUpdate> _orderUpdateSubject = new();
        public IObservable<OrderUpdate> OrderUpdateStream => _orderUpdateSubject.AsObservable();

        // Order book streaming
        private readonly Subject<OrderBookUpdate> _orderBookSubject = new();
        private IDisposable? _orderBookSubscription;
        private string? _orderBookSymbol;

        // User data stream lifecycle
        private string? _listenKey;
        private System.Timers.Timer? _keepAliveTimer;

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

        public bool IsConnected => !string.IsNullOrEmpty(_apiKey);

        public override List<string> NativelySupportedTimeframes => new List<string>
        {
            StandardTimeframes.OneMinute, StandardTimeframes.ThreeMinutes, StandardTimeframes.FiveMinutes,
            StandardTimeframes.FifteenMinutes, StandardTimeframes.ThirtyMinutes, StandardTimeframes.OneHour,
            StandardTimeframes.TwoHours, StandardTimeframes.FourHours, StandardTimeframes.SixHours,
            StandardTimeframes.EightHours, StandardTimeframes.TwelveHours, StandardTimeframes.OneDay,
            StandardTimeframes.ThreeDays, StandardTimeframes.OneWeek, StandardTimeframes.OneMonth
        };

        public BinanceProvider()
        {
            _client = new BinanceRestClient();
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
            if (config.TryGetValue("Testnet",   out var tn))     _isTestnet = tn == "true";

            // Phase 4 Track B: the authenticated REST client is now built
            // lazily at first connect via EnsureTradingClientAsync, using a
            // fresh credential checkout. Configure still populates the
            // _apiKey / _apiSecret fallback fields so non-bridge callers
            // (unit tests, CLI) keep working.
        }

        // Sign-time credential checkout (phase 4 Track B). Prefers the
        // PluginHostServices.ApiKeys bridge; falls back to Configure-populated
        // fields for unit tests / CLI runs.
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

        // Lazy per-connection-lifecycle trading client. Binance.Net's REST
        // client holds credentials internally for its lifetime, so we build
        // it once per connect cycle and dispose it at DisconnectAsync time.
        private async Task EnsureTradingClientAsync()
        {
            if (_tradingClient != null) return;
            var (apiKey, apiSecret) = await CheckoutBinanceCredentialsAsync().ConfigureAwait(false);
            _tradingClient = new BinanceRestClient(opts =>
            {
                opts.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(apiKey, apiSecret);
            });
        }

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

        private BinanceRestClient TradingClient => _tradingClient ?? _client;

        // --- Connection & Subscription Management ---

        public override async Task EnsureConnectedAsync()
        {
            if (_socketClient != null) return;
            _socketClient = new BinanceSocketClient();
            _connectionStateStream.OnNext(ConnectionState.Connected);

            // Start the authenticated trading client + user-data stream only
            // when we have some source of credentials. Trade ops will build
            // the client lazily via EnsureTradingClientAsync if called later.
            if (!string.IsNullOrEmpty(_apiKey) || PluginHostServices.ApiKeys != null)
            {
                try
                {
                    await EnsureTradingClientAsync();
                    await StartUserDataStreamAsync();
                }
                catch
                {
                    // No creds available — fall through to public-data-only mode.
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

                await _socketClient!.SpotApi.Account.SubscribeToUserDataUpdatesAsync(
                    _listenKey,
                    onOrderUpdateMessage: data =>
                    {
                        var o = data.Data;
                        var status = o.Status switch
                        {
                            global::Binance.Net.Enums.OrderStatus.PartiallyFilled => OrderStatus.PartialFill,
                            global::Binance.Net.Enums.OrderStatus.Filled          => OrderStatus.Filled,
                            global::Binance.Net.Enums.OrderStatus.Canceled        => OrderStatus.Cancelled,
                            global::Binance.Net.Enums.OrderStatus.Rejected        => OrderStatus.Rejected,
                            _                                                      => OrderStatus.Filled
                        };
                        _orderUpdateSubject.OnNext(new OrderUpdate(
                            o.Id.ToString(),
                            o.Symbol,
                            o.Side == global::Binance.Net.Enums.OrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                            (double)o.QuantityFilled,
                            (double)o.Price,
                            (double)(o.Quantity - o.QuantityFilled),
                            status,
                            StopTriggered: o.Type == global::Binance.Net.Enums.SpotOrderType.StopLoss || o.Type == global::Binance.Net.Enums.SpotOrderType.StopLossLimit,
                            TakeProfitTriggered: o.Type == global::Binance.Net.Enums.SpotOrderType.TakeProfit || o.Type == global::Binance.Net.Enums.SpotOrderType.TakeProfitLimit,
                            DateTime.UtcNow));
                    },
                    onOcoOrderUpdateMessage: null,
                    onAccountPositionMessage: null,
                    onAccountBalanceUpdate: null);

                _keepAliveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(25).TotalMilliseconds);
                _keepAliveTimer.Elapsed += async (_, _) =>
                {
                    try { await TradingClient.SpotApi.Account.KeepAliveUserStreamAsync(_listenKey); }
                    catch (Exception ex)
                    {
                        // If keep-alive fails repeatedly the listen key expires and order-update
                        // delivery silently stops. Surface it so the UI can show a warning.
                        _errorStream.OnNext($"Binance user-data keep-alive failed: {ex.GetType().Name}");
                    }
                };
                _keepAliveTimer.AutoReset = true;
                _keepAliveTimer.Start();
            }
            catch (Exception ex)
            {
                // User-data stream is an enhancement over market data; failure here does not
                // block trading. But staying silent meant a trader never knew why order fills
                // weren't announcing. Publish a one-time error so the JournalModal records it.
                _errorStream.OnNext($"Binance user-data stream unavailable ({ex.GetType().Name}): order-fill updates won't be delivered.");
            }
        }

        public override async Task SetSubscriptionAsync(string market, string symbol, string timeframe)
        {
            await EnsureConnectedAsync();

            var cleanSymbol = CleanSymbol(symbol);
            if (_currentSymbol == cleanSymbol && _currentTimeframe == timeframe && _currentSubscription != null) return;

            if (_currentSubscription != null)
            {
                await _socketClient!.UnsubscribeAllAsync();
                _currentSubscription = null;
            }

            _currentSymbol = cleanSymbol;
            _currentTimeframe = timeframe;
            bool isFutures = market.Contains("Futures", StringComparison.OrdinalIgnoreCase);

            if (isFutures)
            {
                var subResult = await _socketClient!.UsdFuturesApi.SubscribeToKlineUpdatesAsync(cleanSymbol, MapInterval(timeframe), data =>
                {
                    var kline = data.Data.Data;
                    var bar = new Ohlcv(kline.OpenTime, (double)kline.OpenPrice, (double)kline.HighPrice, (double)kline.LowPrice, (double)kline.ClosePrice, (double)kline.Volume);
                    if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
                        && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime))
                        return;
                    _liveStream.OnNext(bar);
                });

                if (subResult.Success)
                    _currentSubscription = (IDisposable)subResult.Data;
                else
                {
                    _errorStream.OnNext($"Binance Futures subscription failed: {subResult.Error?.Message}");
                    _connectionStateStream.OnNext(ConnectionState.Error);
                }
            }
            else
            {
                var subResult = await _socketClient!.SpotApi.ExchangeData.SubscribeToKlineUpdatesAsync(cleanSymbol, MapInterval(timeframe), data =>
                {
                    var kline = data.Data.Data;
                    var bar = new Ohlcv(kline.OpenTime, (double)kline.OpenPrice, (double)kline.HighPrice, (double)kline.LowPrice, (double)kline.ClosePrice, (double)kline.Volume);
                    if (bar.Open == 0 && bar.High == 0 && bar.Low == 0 && bar.Close == 0 && bar.Volume == 0
                        && (bar.Date == DateTime.MinValue || bar.Date == DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime))
                        return;
                    _liveStream.OnNext(bar);
                });

                if (subResult.Success)
                    _currentSubscription = (IDisposable)subResult.Data;
                else
                {
                    _errorStream.OnNext($"Binance Spot subscription failed: {subResult.Error?.Message}");
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

            if (_socketClient != null)
            {
                await _socketClient.UnsubscribeAllAsync();
                _socketClient.Dispose();
                _socketClient = null;
            }

            if (!string.IsNullOrEmpty(_listenKey) && _tradingClient != null)
            {
                // Only null the listen key if Binance acknowledged the stop. If the
                // stop request failed (network blip mid-disconnect), the key remains
                // valid on Binance's side and would pile up as a zombie across
                // reconnect cycles — each disconnect/reconnect pair without success
                // was previously creating a fresh key while leaking the old one.
                bool stopped = false;
                try
                {
                    var result = await _tradingClient.SpotApi.Account.StopUserStreamAsync(_listenKey);
                    stopped = result != null && result.Success;
                }
                catch (Exception ex)
                {
                    _errorStream.OnNext($"Binance StopUserStream failed: {ex.GetType().Name}");
                }
                if (stopped) _listenKey = null;
            }
            else
            {
                _listenKey = null;
            }

            // Dispose the authenticated client so its internally-held
            // credentials are released. The public _client stays — it holds
            // no secrets and is reused across connect cycles.
            _tradingClient?.Dispose();
            _tradingClient = null;

            _currentSubscription = null;
            _currentSymbol = null;
            _currentTimeframe = null;

            // Drop credential references so a crash dump taken after disconnect
            // doesn't still root the API key/secret strings.
            ScrubCredentials(
                () => _apiKey = null,
                () => _apiSecret = null);

            _connectionStateStream.OnNext(ConnectionState.Disconnected);
        }

        // --- Data Discovery & Fetching ---

        public override async Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => new List<string> { "Spot", "Futures" };

        public override async Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
        {
            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    if (subType.Equals("Futures", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await _client.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync();
                        return result.Success ? result.Data.Symbols.Select(s => s.Name).OrderBy(s => s).ToList() : new List<string>();
                    }
                    else
                    {
                        var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();
                        return result.Success ? result.Data.Symbols.Select(s => s.Name).OrderBy(s => s).ToList() : new List<string>();
                    }
                });
            }
            catch { return new List<string>(); }
        }

        public override async Task<List<string>> GetSupportedTimeframesAsync() => new List<string> { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w", "1M" };

        public override async Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
        {
            var cleanSymbol = CleanSymbol(request.Symbol);
            bool isFutures = request.Market.Contains("Futures", StringComparison.OrdinalIgnoreCase);
            var interval = MapInterval(request.Timeframe);
            int limit = Math.Min(request.Limit, 1000);

            try
            {
                return await _rateLimiter.ExecuteAsync(async () =>
                {
                    DateTime? startTime = request.Since != null ? DateTimeOffset.FromUnixTimeMilliseconds(request.Since.Value).UtcDateTime : null;
                    DateTime? endTime   = request.Until != null ? DateTimeOffset.FromUnixTimeMilliseconds(request.Until.Value).UtcDateTime : null;

                    // Binance /klines honors single-bound queries (startTime alone walks
                    // forward up to `limit` bars; endTime alone walks backward). If either
                    // of those guarantees ever changes, pagination will silently degrade
                    // to "latest N" -- see MexcProvider.FetchOhlcvAsync for the defensive
                    // bound-computation pattern to copy.
                    var result = isFutures
                        ? await _client.UsdFuturesApi.ExchangeData.GetKlinesAsync(cleanSymbol, interval, startTime: startTime, endTime: endTime, limit: limit)
                        : await _client.SpotApi.ExchangeData.GetKlinesAsync(cleanSymbol, interval, startTime: startTime, endTime: endTime, limit: limit);

                    if (!result.Success || result.Data == null) return (new List<Ohlcv>(), new List<(long, double)>());
                    var ohlcv = result.Data.Select(k => new Ohlcv(k.OpenTime.ToUniversalTime(), (double)k.OpenPrice, (double)k.HighPrice, (double)k.LowPrice, (double)k.ClosePrice, (double)k.Volume)).ToList();
                    return (ohlcv, ohlcv.Select(x => (new DateTimeOffset(x.Date).ToUnixTimeMilliseconds(), x.Volume)).ToList());
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
                    var subResult = await _socketClient!.SpotApi.ExchangeData.SubscribeToPartialOrderBookUpdatesAsync(
                        cleanSymbol, 20, 1000, data =>
                        {
                            var bids = data.Data.Bids.Select(b => new OrderBookEntry((double)b.Price, (double)b.Quantity)).ToList();
                            var asks = data.Data.Asks.Select(a => new OrderBookEntry((double)a.Price, (double)a.Quantity)).ToList();
                            _orderBookSubject.OnNext(new OrderBookUpdate(cleanSymbol, bids, asks, 0, DateTime.UtcNow));
                        });

                    if (subResult.Success)
                        _orderBookSubscription = (IDisposable)subResult.Data;
                    else
                        _errorStream.OnNext($"Binance order book subscription failed: {subResult.Error?.Message}");
                }
                catch (Exception ex) { _errorStream.OnNext($"Binance order book error: {ex.Message}"); }
            });

            return _orderBookSubject.AsObservable();
        }

        // ── ITradingProvider implementation ─────────────────────────────────

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
                    var result = await TradingClient.UsdFuturesApi.Account.GetPositionInformationAsync();
                    if (!result.Success || result.Data == null) return new List<Position>();
                    return result.Data
                        .Where(p => p.Quantity != 0)
                        .Select(p => new Position(
                            p.Symbol,
                            (double)Math.Abs(p.Quantity),
                            (double)p.EntryPrice,
                            (double)(Math.Abs(p.Quantity) * p.MarkPrice),
                            (double)p.UnrealizedPnl,
                            (double)p.Leverage,
                            (double)p.LiquidationPrice))
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
                    var result = symbol != null
                        ? await TradingClient.SpotApi.Trading.GetOpenOrdersAsync(CleanSymbol(symbol))
                        : await TradingClient.SpotApi.Trading.GetOpenOrdersAsync();
                    if (!result.Success || result.Data == null) return new List<OpenOrder>();
                    return result.Data.Select(o => new OpenOrder(
                        o.Id.ToString(),
                        o.Symbol,
                        o.Side == global::Binance.Net.Enums.OrderSide.Buy ? OrderSide.Buy : OrderSide.Sell,
                        MapOrderType(o.Type),
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
                    var symbol = CleanSymbol(signal.Symbol);
                    var side   = signal.Side == OrderSide.Buy
                        ? global::Binance.Net.Enums.OrderSide.Buy
                        : global::Binance.Net.Enums.OrderSide.Sell;

                    if (isFutures)
                    {
                        var futType = signal.Type switch
                        {
                            OrderType.Limit            => global::Binance.Net.Enums.FuturesOrderType.Limit,
                            OrderType.StopMarket       => global::Binance.Net.Enums.FuturesOrderType.StopMarket,
                            OrderType.StopLimit        => global::Binance.Net.Enums.FuturesOrderType.Stop,
                            OrderType.TakeProfitMarket => global::Binance.Net.Enums.FuturesOrderType.TakeProfitMarket,
                            OrderType.TakeProfitLimit  => global::Binance.Net.Enums.FuturesOrderType.TakeProfit,
                            _                          => global::Binance.Net.Enums.FuturesOrderType.Market
                        };

                        if (signal.Leverage.HasValue && signal.Leverage.Value > 1)
                            await SetLeverageAsync(symbol, signal.Leverage.Value);

                        var r = await TradingClient.UsdFuturesApi.Trading.PlaceOrderAsync(
                            symbol, side, futType,
                            quantity: (decimal)signal.Quantity,
                            price: signal.Price.HasValue && futType != global::Binance.Net.Enums.FuturesOrderType.Market ? (decimal?)signal.Price.Value : null,
                            stopPrice: signal.StopLoss.HasValue ? (decimal?)signal.StopLoss.Value : null,
                            timeInForce: futType == global::Binance.Net.Enums.FuturesOrderType.Market ? null : global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                            newClientOrderId: signal.ClientOid);

                        if (!r.Success) return $"ORDER_FAILED:{r.Error?.Message}";

                        // Attach take-profit if requested
                        if (signal.TakeProfit.HasValue)
                        {
                            var tpSide = side == global::Binance.Net.Enums.OrderSide.Buy
                                ? global::Binance.Net.Enums.OrderSide.Sell
                                : global::Binance.Net.Enums.OrderSide.Buy;
                            await TradingClient.UsdFuturesApi.Trading.PlaceOrderAsync(
                                symbol, tpSide, global::Binance.Net.Enums.FuturesOrderType.TakeProfitMarket,
                                quantity: (decimal)signal.Quantity,
                                stopPrice: (decimal)signal.TakeProfit.Value,
                                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled);
                        }

                        // Attach stop-loss as a separate order if not included in the primary order
                        if (signal.StopLoss.HasValue && futType == global::Binance.Net.Enums.FuturesOrderType.Market)
                        {
                            var slSide = side == global::Binance.Net.Enums.OrderSide.Buy
                                ? global::Binance.Net.Enums.OrderSide.Sell
                                : global::Binance.Net.Enums.OrderSide.Buy;
                            await TradingClient.UsdFuturesApi.Trading.PlaceOrderAsync(
                                symbol, slSide, global::Binance.Net.Enums.FuturesOrderType.StopMarket,
                                quantity: (decimal)signal.Quantity,
                                stopPrice: (decimal)signal.StopLoss.Value,
                                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled);
                        }

                        return r.Data.Id.ToString();
                    }
                    else
                    {
                        // ── Spot order ───────────────────────────────────────────────
                        if (signal.Type == OrderType.Market)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.Market,
                                quantity: (decimal)signal.Quantity,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        else if (signal.Type == OrderType.Limit && signal.Price.HasValue)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.Limit,
                                quantity: (decimal)signal.Quantity,
                                price: (decimal)signal.Price.Value,
                                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        else if (signal.Type == OrderType.StopMarket && signal.StopLoss.HasValue)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.StopLoss,
                                quantity: (decimal)signal.Quantity,
                                stopPrice: (decimal)signal.StopLoss.Value,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        else if (signal.Type == OrderType.StopLimit && signal.StopLoss.HasValue && signal.Price.HasValue)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.StopLossLimit,
                                quantity: (decimal)signal.Quantity,
                                price: (decimal)signal.Price.Value,
                                stopPrice: (decimal)signal.StopLoss.Value,
                                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        else if (signal.Type == OrderType.TakeProfitMarket && signal.TakeProfit.HasValue)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.TakeProfit,
                                quantity: (decimal)signal.Quantity,
                                stopPrice: (decimal)signal.TakeProfit.Value,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        else if (signal.Type == OrderType.TakeProfitLimit && signal.TakeProfit.HasValue && signal.Price.HasValue)
                        {
                            var r = await TradingClient.SpotApi.Trading.PlaceOrderAsync(
                                symbol, side, global::Binance.Net.Enums.SpotOrderType.TakeProfitLimit,
                                quantity: (decimal)signal.Quantity,
                                price: (decimal)signal.Price.Value,
                                stopPrice: (decimal)signal.TakeProfit.Value,
                                timeInForce: global::Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                                newClientOrderId: signal.ClientOid);
                            return r.Success ? r.Data.Id.ToString() : $"ORDER_FAILED:{r.Error?.Message}";
                        }
                        return "ORDER_FAILED:Unsupported order type";
                    }
                });
            }
            catch (Exception ex) { _errorStream.OnNext($"Binance order error: {ex.GetType().Name}"); return $"ORDER_FAILED:{ex.GetType().Name}"; }
        }

        public async Task<bool> CancelOrderAsync(string orderId, string symbol)
        {
            if (!IsConnected) return false;
            try
            {
                var r = await _rateLimiter.ExecuteAsync(async () =>
                {
                    await EnsureTradingClientAsync();
                    return await TradingClient.SpotApi.Trading.CancelOrderAsync(CleanSymbol(symbol), long.Parse(orderId));
                });
                return r.Success;
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
                var r = await TradingClient.UsdFuturesApi.Account.ChangeInitialLeverageAsync(
                    CleanSymbol(symbol), lev);
                return r.Success ? (double)r.Data.Leverage : 1.0;
            }
            catch { return 1.0; }
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static OrderType MapOrderType(global::Binance.Net.Enums.SpotOrderType type) => type switch
        {
            global::Binance.Net.Enums.SpotOrderType.Limit           => OrderType.Limit,
            global::Binance.Net.Enums.SpotOrderType.Market          => OrderType.Market,
            global::Binance.Net.Enums.SpotOrderType.StopLoss        => OrderType.StopMarket,
            global::Binance.Net.Enums.SpotOrderType.StopLossLimit   => OrderType.StopLimit,
            global::Binance.Net.Enums.SpotOrderType.TakeProfit      => OrderType.TakeProfitMarket,
            global::Binance.Net.Enums.SpotOrderType.TakeProfitLimit => OrderType.TakeProfitLimit,
            _ => OrderType.Market
        };

        private global::Binance.Net.Enums.KlineInterval MapInterval(string tf)
        {
            return tf switch
            {
                "1m"  => global::Binance.Net.Enums.KlineInterval.OneMinute,
                "3m"  => global::Binance.Net.Enums.KlineInterval.ThreeMinutes,
                "5m"  => global::Binance.Net.Enums.KlineInterval.FiveMinutes,
                "15m" => global::Binance.Net.Enums.KlineInterval.FifteenMinutes,
                "30m" => global::Binance.Net.Enums.KlineInterval.ThirtyMinutes,
                "1h"  => global::Binance.Net.Enums.KlineInterval.OneHour,
                "2h"  => global::Binance.Net.Enums.KlineInterval.TwoHour,
                "4h"  => global::Binance.Net.Enums.KlineInterval.FourHour,
                "6h"  => global::Binance.Net.Enums.KlineInterval.SixHour,
                "8h"  => global::Binance.Net.Enums.KlineInterval.EightHour,
                "12h" => global::Binance.Net.Enums.KlineInterval.TwelveHour,
                "1d"  => global::Binance.Net.Enums.KlineInterval.OneDay,
                "3d"  => global::Binance.Net.Enums.KlineInterval.ThreeDay,
                "1w"  => global::Binance.Net.Enums.KlineInterval.OneWeek,
                "1M"  => global::Binance.Net.Enums.KlineInterval.OneMonth,
                _     => global::Binance.Net.Enums.KlineInterval.OneHour
            };
        }
    }
}
