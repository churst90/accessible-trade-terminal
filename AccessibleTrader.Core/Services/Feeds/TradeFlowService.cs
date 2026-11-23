using System.Collections.Concurrent;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// True buy/sell volume (Cody, 2026-11-23: "true buy sell volume if only pertaining to one
    /// exchange no. if it can be useful for any exchange, yes"). The candles carry only total
    /// volume, so the split is counted from the venue's public trade stream, which says which
    /// side took each trade (<see cref="ITradeStreamProvider"/>). One <see cref="TradeTape"/> per
    /// venue, market and symbol, opened the first time an indicator asks for it and closed when
    /// nothing has asked for <see cref="IdleTimeout"/>.
    ///
    /// <para>
    /// There is no history: the venues send trades from the moment of subscribing, so counting
    /// starts when the chart does, and a bar is counted only if the stream was up for all of it.
    /// </para>
    /// </summary>
    public interface ITradeFlowService
    {
        /// <summary>The tape for this venue and symbol, opening its trade stream on the first
        /// call. Never null: a venue with no trade sides gets a tape in
        /// <see cref="TradeTape.TapeState.Unsupported"/>.</summary>
        TradeTape Watch(string market, string provider, string symbol);
    }

    public sealed class TradeFlowService : ITradeFlowService, IAsyncDisposable, IDisposable
    {
        /// <summary>How long a tape no indicator has read stays open.</summary>
        internal static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

        /// <summary>How long a failed stream waits before the next ask tries it again: a venue
        /// that is down is not re-dialled on every tick.</summary>
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        private sealed class Entry
        {
            public required TradeTape Tape;
            public IAsyncDisposable? Subscription;
            public bool Closed;
            private int _started;
            /// <summary>Set when this entry gave up; the first ask after it replaces the entry.</summary>
            public DateTime? RetryAt;
            public bool TryStart() => Interlocked.Exchange(ref _started, 1) == 0;
        }

        private readonly IDataService _data;
        private readonly DemoPolicy? _demo;
        private readonly ILogger _logger;
        private readonly Func<DateTime> _now;
        private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Timer _sweep;

        public TradeFlowService(IDataService data, ILogger<TradeFlowService>? logger = null, DemoPolicy? demo = null)
            : this(data, logger, demo, () => DateTime.UtcNow) { }

        internal TradeFlowService(IDataService data, ILogger? logger, DemoPolicy? demo, Func<DateTime> now)
        {
            _data = data;
            _demo = demo;
            _logger = logger ?? NullLogger.Instance;
            _now = now;
            _sweep = new Timer(_ => _ = SweepIdleAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public TradeTape Watch(string market, string provider, string symbol)
        {
            string key = $"{provider}|{market}|{symbol}";
            var entry = _entries.GetOrAdd(key, __ => new Entry { Tape = new TradeTape(provider, _now) });
            if (entry.RetryAt is { } at && _now() >= at)
            {
                var fresh = new Entry { Tape = new TradeTape(provider, _now) };
                entry = _entries.TryUpdate(key, fresh, entry) ? fresh : _entries.GetOrAdd(key, fresh);
            }
            // Started here rather than in the factory, which can run more than once for one key.
            if (entry.TryStart()) _ = OpenAsync(entry, market, provider, symbol);
            entry.Tape.LastAsked = _now();
            return entry.Tape;
        }

        private async Task OpenAsync(Entry entry, string market, string providerName, string symbol)
        {
            var tape = entry.Tape;
            try
            {
                if (_demo != null && !_demo.AllowsLiveStream(providerName)) { tape.MarkUnsupported(); return; }
                var provider = await _data.GetProviderAsync(providerName).ConfigureAwait(false);
                // Not loaded yet (the app is still starting): not a "no", ask again next time.
                if (provider == null) { entry.RetryAt = _now(); return; }
                var stream = provider as ITradeStreamProvider ?? provider?.GetCapability<ITradeStreamProvider>();
                if (stream == null) { tape.MarkUnsupported(); return; }
                var sub = await stream.SubscribeTradesAsync(market, symbol, tape.Add, tape.SetCounting).ConfigureAwait(false);
                bool closed;
                lock (entry) { closed = entry.Closed; if (!closed) entry.Subscription = sub; }
                // Swept or disposed while connecting: nobody will close this one but us.
                if (closed) await sub.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trade stream failed for {Symbol} on {Provider}", symbol, providerName);
                tape.MarkFailed();
                // Tried again after a pause, rather than a dead tape for the session.
                entry.RetryAt = _now() + RetryDelay;
            }
        }

        internal Task SweepIdleAsync()
        {
            var cutoff = _now() - IdleTimeout;
            var closing = new List<Task>();
            foreach (var kv in _entries)
                if (kv.Value.Tape.LastAsked < cutoff && _entries.TryRemove(kv))
                    closing.Add(CloseAsync(kv.Value));
            return Task.WhenAll(closing);
        }

        private static async Task CloseAsync(Entry entry)
        {
            IAsyncDisposable? sub;
            lock (entry) { entry.Closed = true; sub = entry.Subscription; entry.Subscription = null; }
            entry.Tape.SetCounting(false);
            if (sub != null)
            {
                try { await sub.DisposeAsync().ConfigureAwait(false); }
                catch { /* the socket is going either way */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _sweep.DisposeAsync().ConfigureAwait(false);
            var all = _entries.Values.ToList();
            _entries.Clear();
            await Task.WhenAll(all.Select(CloseAsync)).ConfigureAwait(false);
        }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// One symbol's trades, added up per second by the side that took them, and the stretches of
    /// time the stream was known to be up. A bar is read with <see cref="Sum"/>, which answers
    /// only for a bar the stream saw from its first second.
    /// </summary>
    public sealed class TradeTape
    {
        public enum TapeState { Connecting, Counting, Dropped, Unsupported, Failed }

        /// <summary>How far back the tape keeps its seconds: three days of a busy market is a few
        /// hundred thousand entries.</summary>
        internal static readonly TimeSpan Retention = TimeSpan.FromDays(3);

        private readonly object _gate = new();
        private readonly Func<DateTime> _now;
        private readonly List<(long Second, double Buy, double Sell)> _seconds = new();
        private readonly List<(DateTime From, DateTime To)> _up = new();   // To = MaxValue while up

        internal TradeTape(string provider, Func<DateTime> now) { Provider = provider; _now = now; }

        public string Provider { get; }
        public TapeState State { get; private set; } = TapeState.Connecting;
        internal DateTime LastAsked { get; set; }

        /// <summary>When the stream last came up, or null if it never has.</summary>
        public DateTime? CountingSince
        {
            get { lock (_gate) return _up.Count == 0 ? null : _up[^1].From; }
        }

        internal void Add(PublicTrade t)
        {
            if (t.Quantity <= 0 || double.IsNaN(t.Quantity)) return;
            long sec = Sec(t.Time);
            bool buy = t.TakerSide == OrderSide.Buy;
            lock (_gate)
            {
                // Trades arrive in order, give or take a batch: look from the end.
                int i = _seconds.Count - 1;
                while (i >= 0 && _seconds[i].Second > sec) i--;
                if (i >= 0 && _seconds[i].Second == sec)
                {
                    var s = _seconds[i];
                    _seconds[i] = buy ? (sec, s.Buy + t.Quantity, s.Sell) : (sec, s.Buy, s.Sell + t.Quantity);
                }
                else
                {
                    _seconds.Insert(i + 1, buy ? (sec, t.Quantity, 0) : (sec, 0, t.Quantity));
                }
                Prune();
            }
        }

        internal void SetCounting(bool on)
        {
            lock (_gate)
            {
                bool up = _up.Count > 0 && _up[^1].To == DateTime.MaxValue;
                if (on && !up) _up.Add((_now(), DateTime.MaxValue));
                else if (!on && up) _up[^1] = (_up[^1].From, _now());
                if (State is TapeState.Connecting or TapeState.Counting or TapeState.Dropped)
                    State = on ? TapeState.Counting : _up.Count > 0 ? TapeState.Dropped : State;
            }
        }

        internal void MarkUnsupported() { lock (_gate) State = TapeState.Unsupported; }
        internal void MarkFailed() { lock (_gate) State = TapeState.Failed; }

        /// <summary>
        /// The buy and sell volume of the trades in [<paramref name="from"/>, <paramref name="to"/>),
        /// or null when the stream was not up for all of it. A bar still forming is counted while
        /// the stream is still up (its stretch runs to MaxValue), and not once a drop has ended it.
        /// Times are UTC.
        /// </summary>
        public (double Buy, double Sell)? Sum(DateTime from, DateTime to)
        {
            // Older than the tape keeps: its seconds may be gone, so it is not known.
            if (from < _now() - Retention) return null;
            lock (_gate)
            {
                bool seen = false;
                foreach (var (f, t) in _up)
                    if (f <= from && t >= to) { seen = true; break; }
                if (!seen) return null;

                long b = Sec(to);
                int i = LowerBound(Sec(from));
                double buy = 0, sell = 0;
                for (; i < _seconds.Count && _seconds[i].Second < b; i++)
                {
                    buy += _seconds[i].Buy;
                    sell += _seconds[i].Sell;
                }
                return (buy, sell);
            }
        }

        /// <summary>Bar and trade times are UTC; an unlabelled one is read as UTC, never local.</summary>
        private static long Sec(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToUnixTimeSeconds();

        private int LowerBound(long second)
        {
            int lo = 0, hi = _seconds.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (_seconds[mid].Second < second) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private void Prune()
        {
            if (_seconds.Count < 4096) return;
            long oldest = Sec(_now() - Retention);
            int cut = LowerBound(oldest);
            if (cut > 1024) _seconds.RemoveRange(0, cut);
        }
    }
}
