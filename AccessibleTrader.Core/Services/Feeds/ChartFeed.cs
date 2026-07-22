using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// What changed in a <see cref="ChartFeed"/>'s buffer. LiveAppend means a NEW
    /// bar was appended — i.e. the previous bar just closed, which is the correct
    /// trigger for bar-close consumers like strategy evaluation. LiveReplace is an
    /// intra-bar update of the current live bar.
    /// </summary>
    public enum FeedUpdateKind { InitialLoad, SnapshotRestore, GapFill, Prepend, LiveAppend, LiveReplace }

    /// <summary>
    /// One market data buffer for one <see cref="ChartIdentity"/> — the per-identity
    /// unit of the keyed-feeds pipeline (docs/KEYED_FEEDS_DESIGN.md). Owns the OHLCV
    /// lifecycle that used to live inside the focused-chart DataManager singleton:
    /// initial refresh, snapshot restore + gap-fill, scrollback prepend, and
    /// live-tick merge. Knows nothing about the workspace store or the UI; the
    /// focused-feed binder (DataManager) and future consumers subscribe to
    /// <see cref="Updated"/> or call the operations and dispatch what they need.
    /// </summary>
    public sealed class ChartFeed : IDisposable
    {
        private const int MaxBarsInCache = 5000;
        // Live appends stop growing the buffer beyond this floor: each append past it
        // sheds the oldest bar. Deliberately lower than MaxBarsInCache — deep
        // scrollback (up to 5000) is only reachable via explicit prepends, while a
        // left-running live chart settles at 2000. Preserved from the original
        // DataManager verbatim.
        private const int LiveGrowthCap = 2000;

        private readonly IDataOrchestrator _orchestrator;
        private readonly ILogger _logger;
        private TimeSeriesBuffer<Ohlcv> _cache = TimeSeriesBuffer<Ohlcv>.Empty;
        // Guards every write to _cache against concurrent live-tick and prepend
        // mutations. TimeSeriesBuffer<Ohlcv> is immutable so reads (reference
        // assignment from a single field) are atomic on any 64-bit runtime; only
        // writers contend. Without this, a live tick interleaving with a prepend
        // could be silently dropped because both produce a new _cache snapshot
        // from the pre-mutation reference.
        private readonly object _cacheLock = new();
        private readonly SemaphoreSlim _prependLock = new(1, 1);

        public ChartIdentity Identity { get; }
        public TimeSeriesBuffer<Ohlcv> Bars => _cache;
        /// <summary>UTC time of the last buffer mutation — Phase C uses this to decide
        /// whether a tab switch can bind the feed instantly or must gap-fill first.</summary>
        public DateTime LastUpdateUtc { get; private set; }

        public event Action<ChartFeed, FeedUpdateKind>? Updated;

        public ChartFeed(ChartIdentity identity, IDataOrchestrator orchestrator, ILogger logger)
        {
            Identity = identity;
            _orchestrator = orchestrator;
            _logger = logger;
        }

        /// <summary>
        /// Fresh 200-bar load, replacing the buffer. Returns false (buffer untouched)
        /// when the identity has no symbol or the provider returned nothing. Throws
        /// OperationCanceledException when superseded — the token is checked after the
        /// fetch so a stale load never lands.
        /// </summary>
        public async Task<bool> RefreshAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(Identity.Symbol)) return false;

            _logger.LogInformation("ChartFeed: Refreshing data for {Symbol} (Initial Load: 200 bars).", Identity.Symbol);

            var newData = await _orchestrator.FetchOhlcvAsync(
                Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe, limit: 200).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (newData == null || !newData.Any()) return false;

            lock (_cacheLock) { _cache = new TimeSeriesBuffer<Ohlcv>(newData); }
            Touch();
            Updated?.Invoke(this, FeedUpdateKind.InitialLoad);
            return true;
        }

        /// <summary>
        /// Replaces the buffer with a saved snapshot (full scrollback preserved).
        /// Pair with <see cref="GapFillAsync"/> to catch up on bars that arrived
        /// while the feed was cold.
        /// </summary>
        public void RestoreSnapshot(TimeSeriesBuffer<Ohlcv> snapshot)
        {
            lock (_cacheLock) { _cache = snapshot; }
            Touch();
            Updated?.Invoke(this, FeedUpdateKind.SnapshotRestore);
        }

        /// <summary>
        /// Fetches recent bars and merges only those newer than the buffer's last bar
        /// (or refreshes the live bar intra-bar when nothing newer exists). Returns
        /// true when the provider returned data — the buffer may have changed — and
        /// false when the fetch came back empty. Never discards scrollback.
        /// </summary>
        public async Task<bool> GapFillAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(Identity.Symbol) || _cache.Count == 0) return false;

            var lastKnownDate = _cache[^1].Date;
            var recent = await _orchestrator.FetchOhlcvAsync(
                Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe, limit: 200).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (recent == null || !recent.Any()) return false;

            var gapBars = recent
                .Where(b => b.Date > lastKnownDate)
                .OrderBy(b => b.Date)
                .ToList();

            if (gapBars.Any())
            {
                int appended = 0;
                lock (_cacheLock)
                {
                    foreach (var bar in gapBars)
                    {
                        // Re-check against the CURRENT last bar inside the lock:
                        // a live subscription may have appended past this fetched
                        // bar while the fetch was in flight, and appending an
                        // older bar after a newer one breaks buffer ordering.
                        if (_cache.Count > 0 && bar.Date <= _cache[_cache.Count - 1].Date) continue;
                        _cache = _cache.Append(bar);
                        appended++;
                        if (_cache.Count > MaxBarsInCache)
                            _cache = _cache.RemoveFirst();
                    }
                }
                _logger.LogInformation("ChartFeed: Gap-filled {Count} bars for {Symbol}.", appended, Identity.Symbol);
            }
            else
            {
                // No new bars — but update the live bar in case it changed intra-bar.
                // Same in-lock guard: only touch the last bar if it is still the
                // bar this fetch described.
                var latest = recent.Last();
                lock (_cacheLock)
                {
                    if (latest.Date == lastKnownDate
                        && _cache.Count > 0 && _cache[_cache.Count - 1].Date == lastKnownDate)
                        _cache = _cache.ReplaceLast(latest);
                }
            }

            Touch();
            Updated?.Invoke(this, FeedUpdateKind.GapFill);
            return true;
        }

        /// <summary>
        /// Loads 200 bars of scrollback before the buffer's first bar. Returns the
        /// number of bars prepended (0 when the provider had nothing older), or -1
        /// when another prepend is already in flight — the request is silently
        /// dropped and <paramref name="onStarted"/> is NOT invoked (Option 1,
        /// drop-if-busy; see the original DataManager for the debounce/queue
        /// alternatives considered). <paramref name="onStarted"/> runs after the
        /// prepend lock is acquired, before any network traffic — the focused binder
        /// uses it to flip DataStatus so live ticks and recalcs pause.
        /// </summary>
        public async Task<int> PrependOlderAsync(Action? onStarted = null)
        {
            if (string.IsNullOrEmpty(Identity.Symbol)) return -1;
            if (!await _prependLock.WaitAsync(0).ConfigureAwait(false)) return -1;
            try
            {
                onStarted?.Invoke();

                if (_cache.Count == 0) return 0;
                var firstBar = _cache[0];
                long since = new DateTimeOffset(firstBar.Date).ToUnixTimeMilliseconds();

                _logger.LogInformation("ChartFeed: Prepending 200 bars before {Date}.", firstBar.Date);

                var olderData = await _orchestrator.FetchOhlcvAsync(
                    Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe,
                    limit: 200, until: since - 1).ConfigureAwait(false);

                if (olderData == null || !olderData.Any()) return 0;

                var uniqueOlder = olderData.Where(o => o.Date < firstBar.Date).ToList();
                if (!uniqueOlder.Any()) return 0;

                lock (_cacheLock)
                {
                    _cache = _cache.PrependRange(uniqueOlder);
                    while (_cache.Count > MaxBarsInCache)
                        _cache = _cache.RemoveLast();
                }
                _logger.LogInformation("ChartFeed: Prepended {Count} bars.", uniqueOlder.Count);
                Touch();
                Updated?.Invoke(this, FeedUpdateKind.Prepend);
                return uniqueOlder.Count;
            }
            finally
            {
                _prependLock.Release();
            }
        }

        /// <summary>
        /// Merges one consolidated live bar: append when its period is newer than the
        /// last bar (the previous bar just closed), replace-last otherwise. Returns
        /// false when the tick was dropped because a scrollback prepend is in flight —
        /// holding the prepend lock across the merge is what closes the race where a
        /// tick could interleave with the prepend's wholesale buffer replacement.
        /// The Updated event (LiveAppend/LiveReplace) is raised while the prepend
        /// lock is still held, matching the original pipeline's dispatch-before-
        /// release ordering so a prepend can never begin between merge and notify.
        /// </summary>
        public bool ApplyLiveTick(Ohlcv tick)
        {
            if (!_prependLock.Wait(0)) return false;
            try
            {
                FeedUpdateKind kind;
                lock (_cacheLock)
                {
                    var lastBar = _cache.Count > 0 ? _cache[_cache.Count - 1] : default;
                    if (_cache.Count == 0 || tick.Date > lastBar.Date)
                    {
                        _cache = _cache.Append(tick);
                        if (_cache.Count > LiveGrowthCap) _cache = _cache.RemoveFirst();
                        kind = FeedUpdateKind.LiveAppend;
                    }
                    else if (tick.Date == lastBar.Date)
                    {
                        _cache = _cache.ReplaceLast(tick);
                        kind = FeedUpdateKind.LiveReplace;
                    }
                    else
                    {
                        // A tick OLDER than the last bar (a concurrent gap-fill
                        // advanced the buffer while this tick was in flight, or a
                        // provider replayed on reconnect). Replacing the newer
                        // last bar with it would corrupt the series — drop it.
                        return false;
                    }
                }
                Touch();
                Updated?.Invoke(this, kind);
                return true;
            }
            finally
            {
                _prependLock.Release();
            }
        }

        private void Touch() => LastUpdateUtc = DateTime.UtcNow;

        public void Dispose() => _prependLock.Dispose();
    }
}
