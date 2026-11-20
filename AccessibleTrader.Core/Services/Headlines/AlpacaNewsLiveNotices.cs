using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>The news stream as the live notices see it, so the tests can drive it without a
    /// network. Production is <see cref="AlpacaNewsSocketFactory"/>.</summary>
    public interface INewsSocket : IAsyncDisposable
    {
        bool IsConnected { get; }

        /// <summary>Connects. <paramref name="onConnected"/> runs after every (re)connection,
        /// <paramref name="onMessage"/> for every text frame. False when the handshake failed.</summary>
        Task<bool> ConnectAsync(Func<Task> onConnected, Action<string> onMessage);

        Task SendAsync(string message);
    }

    public interface INewsSocketFactory
    {
        INewsSocket Create();
    }

    /// <summary>Alpaca's news stream on the SDK's <see cref="ReconnectingWebSocket"/>. Alpaca
    /// defines no client ping, so the heartbeat is off, as in the Alpaca provider.</summary>
    public sealed class AlpacaNewsSocketFactory : INewsSocketFactory
    {
        public const string Url = "wss://stream.data.alpaca.markets/v1beta1/news";

        private readonly ILogger<AlpacaNewsSocketFactory>? _logger;

        public AlpacaNewsSocketFactory(ILogger<AlpacaNewsSocketFactory>? logger = null) => _logger = logger;

        public INewsSocket Create() => new Socket(_logger);

        private sealed class Socket : INewsSocket
        {
            private readonly ReconnectingWebSocket _ws = new(Url, heartbeatInterval: TimeSpan.FromMilliseconds(-1));
            private readonly ILogger? _logger;

            public Socket(ILogger? logger) => _logger = logger;

            public bool IsConnected => _ws.IsConnected;

            public Task<bool> ConnectAsync(Func<Task> onConnected, Action<string> onMessage)
            {
                _ws.OnConnected(_ => onConnected())
                   .OnMessage(onMessage)
                   .OnError(_ => _logger?.LogDebug("The news stream reported an error."));
                return _ws.ConnectOrReportAsync(ex =>
                    _logger?.LogDebug("The news stream could not connect: {Type}.", ex.GetType().Name));
            }

            public Task SendAsync(string message) => _ws.SendAsync(message);

            public async ValueTask DisposeAsync()
            {
                try { await _ws.DisconnectAsync().ConfigureAwait(false); }
                catch { /* best-effort */ }
                await _ws.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Headlines, stage 2: the news wire. Alpaca's news stream (fiscal.ai's stories on a free account) is subscribed to
    /// whole, and a story is said when it is about a stock or coin the trader touches — an open
    /// chart or a held position — and its importance (<see cref="NewsStoryWords"/>) passes the
    /// Headlines level. <see cref="NewsHeadlineGate"/> gathers bursts and caps each symbol; it is
    /// shared with Finnhub's news (<see cref="FinnhubNewsLiveNotices"/>, stage 3).
    ///
    /// <para>
    /// <b>Off means no connection.</b> With the level Off, the "Include news wire stories" switch
    /// off, no Alpaca key in API Keys, or nothing touched yet, no socket is opened; turning it off
    /// closes the socket and forgets every pending story.
    /// </para>
    ///
    /// <para>
    /// <b>Reconnecting.</b> The SDK socket retries a dropped connection itself. If it has been down
    /// for <see cref="DeadAfter"/>, or Alpaca sent an error, this closes it and opens a new one
    /// after a wait: one minute doubling to <see cref="MaxBackoff"/>; half an hour after a refused
    /// key (402), five minutes after "connection limit exceeded" (406 — a second window of the web
    /// version, say), six hours after "insufficient subscription" (409).
    /// </para>
    ///
    /// <para>
    /// <b>The key</b> is checked out from API Keys at each connection and dropped once the
    /// authentication message is sent: never kept, never logged.
    /// </para>
    /// </summary>
    public sealed class AlpacaNewsLiveNotices : IDisposable
    {
        public static readonly TimeSpan DeadAfter = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan NoKeyRecheck = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan RefusedKeyWait = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan ConnectionLimitWait = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan NoSubscriptionWait = TimeSpan.FromHours(6);

        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly IApiKeyCheckout _keys;
        private readonly INewsSocketFactory _sockets;
        private readonly IHeldPositionReader? _held;
        private readonly IUserPresence? _presence;
        private readonly ILogger<AlpacaNewsLiveNotices>? _logger;
        private readonly TimeProvider _time;
        private readonly ITimer _timer;

        private readonly object _lock = new();
        private readonly NewsHeadlineGate _gate;

        /// <summary>The gate this wire says through, for the tests: the host gives both wires
        /// the same one.</summary>
        internal NewsHeadlineGate Gate => _gate;
        private IReadOnlyList<NewsTarget> _targets = Array.Empty<NewsTarget>();
        private MarketEventLevel _level = MarketEventLevel.Off;

        private INewsSocket? _socket;
        private DateTime _nextConnectUtc = DateTime.MinValue;
        private DateTime? _downSinceUtc;
        private int _failures;
        private volatile bool _dropRequested;
        private DateTime _dropUntilUtc;
        private volatile bool _subscribed;

        private IReadOnlyList<MarketEventHeldPosition> _heldPositions = Array.Empty<MarketEventHeldPosition>();
        private DateTime _heldReadAtUtc = DateTime.MinValue;
        private int _ticking;
        private bool _disposed;

        public AlpacaNewsLiveNotices(
            IWorkspaceStore store,
            IEventBus bus,
            IApiKeyCheckout keys,
            INewsSocketFactory sockets,
            IHeldPositionReader? held = null,
            TimeProvider? timeProvider = null,
            IUserPresence? presence = null,
            ILogger<AlpacaNewsLiveNotices>? logger = null,
            NewsHeadlineGate? gate = null)
        {
            _gate = gate ?? new NewsHeadlineGate();
            _store = store;
            _bus = bus;
            _keys = keys;
            _sockets = sockets;
            _held = held;
            _presence = presence;
            _logger = logger;
            _time = timeProvider ?? TimeProvider.System;
            _timer = _time.CreateTimer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private DateTime NowUtc => _time.GetUtcNow().UtcDateTime;

        /// <summary>True once Alpaca has confirmed the subscription on the current connection.</summary>
        internal bool Subscribed => _subscribed;

        internal bool HasSocket => _socket != null;

        /// <summary>One pass of the clock. Internal to the test project; the timer calls it every
        /// second and it never waits for a previous pass.</summary>
        internal async Task TickAsync()
        {
            if (_disposed) return;
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
            try
            {
                var state = _store.State;
                var level = MarketEventLevels.Parse(state.HeadlinesLevel);
                bool wireOff = level == MarketEventLevel.Off || !state.HeadlinesNewsWire;
                if (wireOff || !state.HeadlinesAlpaca)
                {
                    await CloseAsync().ConfigureAwait(false);
                    _failures = 0;
                    _nextConnectUtc = DateTime.MinValue;
                    // The gate is shared with Finnhub: only when every wire is off is there
                    // nothing left to remember. Alpaca alone off (its default) keeps Finnhub's
                    // dedupe and pacing intact.
                    if (wireOff) lock (_lock) _gate.Forget();
                    return;
                }

                var now = NowUtc;
                if (_held != null && now - _heldReadAtUtc >= EdgarHeadlineLiveNotices.HeldRefresh)
                {
                    _heldReadAtUtc = now;
                    try { _heldPositions = await _held.ReadHeldPositionsAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("News positions could not be read: {Type}.", ex.GetType().Name);
                    }
                }

                var targets = TouchedTargets(state, _heldPositions);
                lock (_lock)
                {
                    _targets = targets;
                    _level = level;
                }

                await KeepConnectedAsync(targets.Count > 0, now).ConfigureAwait(false);

                if (_presence is { CanReachUser: false }) return;

                IReadOnlyList<Headline> due;
                var labels = targets.Select(t => t.Label).ToList();
                lock (_lock) due = _gate.Due(now, labels);
                foreach (var h in due)
                    _bus.Publish(new HeadlineEvent(h.Text, h.Importance, h.Id, h.Symbol));
            }
            catch (Exception ex)
            {
                // A background notice must never take the terminal down; the next tick tries again.
                _logger?.LogWarning("A news headline tick failed: {Type}.", ex.GetType().Name);
            }
            finally
            {
                Volatile.Write(ref _ticking, 0);
            }
        }

        private async Task KeepConnectedAsync(bool wanted, DateTime now)
        {
            if (_socket != null)
            {
                if (_dropRequested)
                {
                    await CloseAsync().ConfigureAwait(false);
                    _nextConnectUtc = _dropUntilUtc;
                    return;
                }
                if (_socket.IsConnected) { _downSinceUtc = null; return; }
                _downSinceUtc ??= now;
                if (now - _downSinceUtc.Value < DeadAfter) return;
                await CloseAsync().ConfigureAwait(false);
                _nextConnectUtc = now + Backoff(_failures++);
                return;
            }

            if (!wanted || now < _nextConnectUtc) return;

            // Checked here only to decide whether to connect at all; OnConnected checks it out again
            // for the authentication message, so the key is never held between the two.
            var probe = await _keys.CheckoutAsync("Alpaca").ConfigureAwait(false);
            if (!probe.HasCredentials)
            {
                _nextConnectUtc = now + NoKeyRecheck;
                return;
            }

            var socket = _sockets.Create();
            _socket = socket;
            _subscribed = false;
            _dropRequested = false;
            _downSinceUtc = null;
            bool ok = await socket.ConnectAsync(() => AuthenticateAsync(socket), OnMessage).ConfigureAwait(false);
            if (!ok)
            {
                await CloseAsync().ConfigureAwait(false);
                _nextConnectUtc = now + Backoff(_failures++);
            }
        }

        private async Task AuthenticateAsync(INewsSocket socket)
        {
            _subscribed = false;
            var key = await _keys.CheckoutAsync("Alpaca").ConfigureAwait(false);
            if (!key.HasCredentials)
            {
                RequestDrop(NoKeyRecheck);
                return;
            }
            await socket.SendAsync(JsonConvert.SerializeObject(new { action = "auth", key = key.Key, secret = key.Secret }))
                .ConfigureAwait(false);
        }

        /// <summary>Every frame from the stream. Runs on the socket's receive loop.</summary>
        private void OnMessage(string json)
        {
            var (stories, controls) = AlpacaNewsParser.ParseFrame(json);
            foreach (var c in controls)
                OnControl(c);
            if (stories.Count == 0) return;

            var now = NowUtc;
            lock (_lock)
            {
                if (_level == MarketEventLevel.Off) return;
                foreach (var story in stories)
                {
                    var hits = NewsStoryWords.Matches(story, _targets);
                    if (hits.Count == 0) continue;
                    var importance = NewsStoryWords.Importance(story);
                    _gate.Note(story, hits, importance);
                    if (!MarketEventLevels.Includes(_level, importance)) continue;
                    _gate.Offer(story, hits, importance, now);
                }
            }
        }

        private void OnControl(AlpacaStreamControl c)
        {
            if (c.Type == "success" && c.Message == "authenticated")
            {
                var socket = _socket;
                if (socket != null)
                    _ = SubscribeAsync(socket);
            }
            else if (c.Type == "subscription")
            {
                _subscribed = true;
                _failures = 0;
            }
            else if (c.Type == "error")
            {
                _logger?.LogWarning("The news stream refused: {Code} {Message}.", c.Code, c.Message);
                RequestDrop(c.Code switch
                {
                    402 => RefusedKeyWait,
                    406 => ConnectionLimitWait,
                    409 => NoSubscriptionWait,
                    _ => Backoff(_failures++),
                });
            }
        }

        private async Task SubscribeAsync(INewsSocket socket)
        {
            try { await socket.SendAsync("{\"action\":\"subscribe\",\"news\":[\"*\"]}").ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger?.LogDebug("The news subscription could not be sent: {Type}.", ex.GetType().Name);
            }
        }

        /// <summary>Closing from the receive loop would wait on that loop, so the next tick closes.</summary>
        private void RequestDrop(TimeSpan wait)
        {
            _dropUntilUtc = NowUtc + wait;
            _dropRequested = true;
        }

        private async Task CloseAsync()
        {
            var socket = _socket;
            _socket = null;
            _subscribed = false;
            _dropRequested = false;
            _downSinceUtc = null;
            if (socket != null)
            {
                try { await socket.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger?.LogDebug("The news stream did not close cleanly: {Type}.", ex.GetType().Name);
                }
            }
        }

        /// <summary>One minute, doubling per failure, at most <see cref="MaxBackoff"/>.</summary>
        public static TimeSpan Backoff(int failures)
        {
            double minutes = FirstBackoff.TotalMinutes * Math.Pow(2, Math.Min(failures, 10));
            return TimeSpan.FromMinutes(Math.Min(minutes, MaxBackoff.TotalMinutes));
        }

        /// <summary>
        /// The stocks (<see cref="EdgarHeadlineLiveNotices.TouchedStocks"/>) and the coins the
        /// trader touches: every open chart in a Crypto market (the focused chart with no category
        /// counts when the selected market is Crypto) and every held Crypto position, by base asset
        /// ("BTC" for BTC/USDT, XBTUSD and BTC-USD alike). Focused chart first, without repeats.
        /// </summary>
        public static IReadOnlyList<NewsTarget> TouchedTargets(
            WorkspaceState state, IReadOnlyList<MarketEventHeldPosition> held)
        {
            var targets = new List<NewsTarget>();
            void AddCoin(string? symbol)
            {
                string coin = NewsStoryWords.CoinLabel(SymbolAssets.BaseOf(symbol));
                if (coin.Length > 0 && !targets.Any(t => t.Coin && t.Label == coin))
                    targets.Add(new NewsTarget(coin, true));
            }

            var focusedMarket = EdgarHeadlineLiveNotices.MarketOf(state.Identity);
            bool focusedCoin = focusedMarket == MarketType.Crypto
                || (focusedMarket == null && state.Data.Count > 0 && state.SelectedMarketType == MarketType.Crypto);
            var stocks = EdgarHeadlineLiveNotices.TouchedStocks(state, held);

            // The focused chart leads whichever kind it is.
            if (focusedCoin) AddCoin(state.Identity.Symbol);
            foreach (var s in stocks)
                if (!targets.Any(t => !t.Coin && t.Label == s))
                    targets.Add(new NewsTarget(s, false));
            foreach (var tab in state.TabSnapshots ?? ImmutableList<TabSnapshot>.Empty)
                if (EdgarHeadlineLiveNotices.MarketOf(tab.Identity) == MarketType.Crypto)
                    AddCoin(tab.Identity.Symbol);
            foreach (var p in held)
                if (p.Market == MarketType.Crypto)
                    AddCoin(p.Symbol);
            return targets;
        }

        public void Dispose()
        {
            _disposed = true;
            _timer.Dispose();
            var socket = _socket;
            _socket = null;
            if (socket != null) _ = socket.DisposeAsync().AsTask();
        }
    }
}
