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
        //
        // That immutability claim was FALSE until 2026-08-27 and this comment was the reason
        // nobody checked: TimeSeriesBuffer.ReplaceLast wrote into the shared backing array and
        // returned a new wrapper over it, so a reader doing state.Data[^1] during a live
        // replace could see a 48-byte Ohlcv half-written — the new Close with the old High.
        // ReplaceLast copies now, so the sentence above is true rather than aspirational.
        private readonly object _cacheLock = new();
        private readonly SemaphoreSlim _prependLock = new(1, 1);

        public ChartIdentity Identity { get; }
        public TimeSeriesBuffer<Ohlcv> Bars => _cache;
        /// <summary>True once the hub has evicted/disposed this feed. Late callers
        /// (a live socket delivering its final ticks) get a clean false/no-op
        /// instead of an ObjectDisposedException from the prepend semaphore.</summary>
        public bool IsDisposed => _disposed;
        private volatile bool _disposed;
        /// <summary>UTC time of the last buffer mutation — Phase C uses this to decide
        /// whether a tab switch can bind the feed instantly or must gap-fill first.</summary>
        public DateTime LastUpdateUtc { get; private set; }

        public event Action<ChartFeed, FeedUpdateKind>? Updated;

        /// <summary>
        /// Raised when a gap-fill found MORE missing bars than one fetch can supply, carrying
        /// the size of the hole. The feed does not repair it — the honest repair is a clean
        /// refresh, and the owner of the identity is the only thing that can do one. What must
        /// never happen again is the silent splice.
        /// </summary>
        public event Action<ChartFeed, TimeSpan>? GapTooLarge;

        /// <summary>
        /// How many bars a gap-fill fetches from the live edge. It was a bare literal 200 at
        /// the call site; naming it makes the shortfall check above legible, and makes it
        /// obvious that the limit is what bounds how big a gap can be repaired in one pass.
        /// </summary>
        private const int GapFillLimit = 200;

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

            // The token goes INTO the fetch, not just after it.
            //
            // These three call sites checked `ct` only once the round trip had already
            // returned, because nothing in the path took a token at all. So the tab-switch CTS
            // could not abort anything: six rapid tab switches queued six unabortable HTTP
            // requests that each ran to completion, burning provider quota and counting
            // against the per-provider circuit breaker, and a provider that hangs held the tab
            // switch until its own HttpClient timeout. The post-fetch checks stay — they are
            // still what stops a stale result being applied.
            var newData = await _orchestrator.FetchOhlcvAsync(
                Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe, limit: 200, ct: ct).ConfigureAwait(false);

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
                Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe, limit: GapFillLimit, ct: ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (recent == null || !recent.Any()) return false;

            // SamePeriod, not equality: a live bar appended at the UTC bucket start (00:00Z) and
            // the provider's own stamp for that day (04:00Z for Alpaca or Polygon stocks) are
            // one bar, and appending the fetched one beside it would chart the day twice.
            var gapBars = recent
                .Where(b => b.Date > lastKnownDate && !SamePeriod(b.Date, lastKnownDate))
                .OrderBy(b => b.Date)
                .ToList();

            // ── Is there a HOLE between what we have and what we just fetched? ───
            //
            // The fetch is a fixed window from the LIVE EDGE. If the feed was cold for
            // longer than that window — a 1m tab left in the background for four hours, or
            // any resumed session — then every fetched bar is newer than lastKnownDate, all
            // of them append, and the missing interval between them is simply GONE. There was
            // no continuity check, no log and no announcement; Updated(GapFill) fired as if it
            // had succeeded. Every indicator over that buffer is wrong at the seam, and the
            // chart's bar-index arithmetic treats a four-hour jump as one bar.
            //
            // A shortfall is detected by comparing the OLDEST fetched bar against the bar
            // that should immediately follow what we hold. When there is one, a partial
            // splice is refused outright: a clean refresh is the only honest repair, and
            // saying which happened matters more than the repair itself.
            if (gapBars.Count > 0)
            {
                long barMs = Sdk.Models.TimeframeUtility.ToMilliseconds(Identity.Timeframe);
                if (barMs > 0)
                {
                    var expectedNext = lastKnownDate.AddMilliseconds(barMs);
                    // One bar of tolerance: venues are not perfectly punctual and a
                    // boundary that is a few seconds late is not a four-hour hole.
                    if (gapBars[0].Date > expectedNext.AddMilliseconds(barMs))
                    {
                        var missing = gapBars[0].Date - lastKnownDate;
                        _logger.LogWarning(
                            "ChartFeed: gap-fill for {Symbol} would splice a hole of {Missing} "
                            + "(have up to {Last:o}, oldest fetched {First:o}); refusing the partial "
                            + "splice — a full refresh is needed.",
                            Identity.Symbol, missing, lastKnownDate, gapBars[0].Date);

                        GapTooLarge?.Invoke(this, missing);
                        return false;
                    }
                }
            }

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
                        if (_cache.Count > 0 && (bar.Date <= _cache[_cache.Count - 1].Date
                                                 || SamePeriod(bar.Date, _cache[_cache.Count - 1].Date))) continue;
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
                    if (SamePeriod(latest.Date, lastKnownDate)
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
        public async Task<int> PrependOlderAsync(Action? onStarted = null, CancellationToken ct = default)
        {
            if (_disposed || string.IsNullOrEmpty(Identity.Symbol)) return -1;
            try
            {
                if (!await _prependLock.WaitAsync(0).ConfigureAwait(false)) return -1;
            }
            catch (ObjectDisposedException) { return -1; } // disposed between check and wait
            try
            {
                onStarted?.Invoke();

                if (_cache.Count == 0) return 0;
                var firstBar = _cache[0];
                long since = new DateTimeOffset(firstBar.Date).ToUnixTimeMilliseconds();

                _logger.LogInformation("ChartFeed: Prepending 200 bars before {Date}.", firstBar.Date);

                var olderData = await _orchestrator.FetchOhlcvAsync(
                    Identity.Market, Identity.Provider, Identity.Symbol, Identity.Timeframe,
                    limit: 200, until: since - 1, ct: ct).ConfigureAwait(false);

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
                try { _prependLock.Release(); }
                catch (ObjectDisposedException) { /* evicted mid-operation — nothing left to release */ }
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
        /// <param name="tick">The consolidated bar for the tick's period.</param>
        /// <param name="source">The consolidator that built it — see <see cref="Models.LiveStreamSource"/>.
        /// With a source, a bar for the period the buffer already holds CONTINUES that bar
        /// (its open, its range, its volume plus only what this stream added since); null
        /// replaces the last bar outright, for a caller holding a complete candle.</param>
        public bool ApplyLiveTick(Ohlcv tick, Models.LiveStreamSource? source = null)
        {
            if (_disposed) return false;
            try
            {
                if (!_prependLock.Wait(0))
                {
                    // Feed is mid prepend/gap-fill — this tick is dropped, so the title won't
                    // refresh for it. Logged (Debug) for diagnosing "the price sometimes stops
                    // updating" — a burst of these lines against a stale title points here.
                    _logger.LogDebug("ChartFeed: live tick dropped for {Symbol} (busy with prepend/gap-fill).",
                        Identity.Symbol);
                    return false;
                }
            }
            catch (ObjectDisposedException) { return false; } // disposed between check and wait
            try
            {
                FeedUpdateKind kind;
                lock (_cacheLock)
                {
                    // A subscription OLDER than one this feed has already heard from is a
                    // superseded socket still draining (a retarget overlaps the old stream by
                    // a moment). Its bucket counts the same trades as the newer one, so letting
                    // both add growth would count them twice.
                    if (source is { } superseded && superseded.StreamId < _streamId)
                    {
                        _logger.LogDebug("ChartFeed: live tick from superseded stream {Old} (current {Current}) for {Symbol} — dropped.",
                            superseded.StreamId, _streamId, Identity.Symbol);
                        return false;
                    }

                    var lastBar = _cache.Count > 0 ? _cache[_cache.Count - 1] : default;
                    // Which period the bar is for, relative to the one held. A live bucket is
                    // stamped at the period's UTC start; several providers stamp their own bars
                    // elsewhere in the same period — Alpaca, Polygon and Schwab daily bars at
                    // US-Eastern or US-Central midnight (04:00Z/05:00Z), Polygon weeks on the
                    // Sunday. Compared for EQUALITY, every live bucket on those charts was
                    // "older" than the bar it belonged to and was dropped, so a stock's daily
                    // candle never moved. Nearest period instead. Only for a sourced bar: a
                    // caller handing over complete candles keeps the exact comparison.
                    int relation = _cache.Count == 0 ? 1
                        : source is null ? tick.Date.CompareTo(lastBar.Date)
                        : SamePeriod(tick.Date, lastBar.Date) ? 0
                        : tick.Date.CompareTo(lastBar.Date);
                    if (relation > 0)
                    {
                        // A period the buffer has never seen: the bucket IS the bar. When the
                        // stream crossed the boundary itself it holds every trade since the
                        // period opened; when it did not, it is the best there is until a
                        // fetch supplies the provider's own bar.
                        _cache = _cache.Append(tick);
                        if (_cache.Count > LiveGrowthCap) _cache = _cache.RemoveFirst();
                        kind = FeedUpdateKind.LiveAppend;
                    }
                    else if (relation == 0)
                    {
                        var merged = source is { } src ? ContinueFormingBar(lastBar, tick, src) : tick;
                        _cache = _cache.ReplaceLast(merged);
                        kind = FeedUpdateKind.LiveReplace;
                    }
                    else
                    {
                        // A tick OLDER than the last bar (a concurrent gap-fill
                        // advanced the buffer while this tick was in flight, or a
                        // provider replayed on reconnect). Replacing the newer
                        // last bar with it would corrupt the series — drop it.
                        _logger.LogDebug("ChartFeed: live tick {TickDate:o} older than last bar {LastDate:o} for {Symbol} — dropped.",
                            tick.Date, lastBar.Date, Identity.Symbol);
                        return false;
                    }

                    // Only once the bar is APPLIED: a tick dropped above (busy, or older) must
                    // leave the baseline where it was, so the next one's growth still includes it.
                    if (source is { } applied)
                    {
                        _streamId = applied.StreamId;
                        _streamPeriod = tick.Date;
                        _streamVolume = tick.Volume;
                    }
                }
                Touch();
                Updated?.Invoke(this, kind);
                return true;
            }
            finally
            {
                try { _prependLock.Release(); }
                catch (ObjectDisposedException) { /* evicted mid-operation — nothing left to release */ }
            }
        }

        // The live stream this feed last applied a bar from, the period that bar was for, and
        // the bucket volume it carried — the baseline the next bar's growth is measured from.
        // Guarded by _cacheLock with the buffer they describe.
        private long _streamId;
        private DateTime _streamPeriod;
        private double _streamVolume;

        /// <summary>
        /// The forming bar the buffer holds, carried forward by one live bucket from the same
        /// period. Caller holds <c>_cacheLock</c>.
        ///
        /// <para>The bar already held is the authority on everything before this stream's
        /// latest bucket — it is the provider's fetched forming bar, or that bar as earlier
        /// ticks already advanced it. So: its Open stands; High and Low widen to anything the
        /// bucket saw; Close is the latest tick; Volume grows by what the bucket ADDED since this
        /// stream's previous bar. A re-fetch landing mid-stream needs no special case: it
        /// replaces the bar, and the stream's baseline (what it had delivered before the fetch
        /// returned) is exactly the volume that fetch already counts.</para>
        ///
        /// <para>The floor of the bucket's own volume keeps a stream that delivers the provider's
        /// COMPLETE forming candle (Binance, MEXC and Kraken klines; Schwab's polled bar) where it
        /// always was: such a candle is never below what the feed holds, so it wins outright and
        /// nothing is counted twice.</para>
        /// </summary>
        private Ohlcv ContinueFormingBar(Ohlcv held, Ohlcv bucket, Models.LiveStreamSource source)
        {
            double added;
            if (source.StreamId == _streamId && _streamPeriod == bucket.Date)
                added = Math.Max(0, bucket.Volume - _streamVolume);
            else
                // This stream's first bucket of the period. Trades it holds arrived after we
                // subscribed, so they are new; a cumulative source bar carries its running
                // total from before we subscribed, which the held bar already counts.
                added = source.Style == Sdk.Plugins.LiveTickStyle.TradeDeltas ? bucket.Volume : 0;

            return new Ohlcv(
                held.Date,
                held.Open > 0 ? held.Open : bucket.Open,
                Math.Max(held.High, bucket.High),
                held.Low > 0 ? Math.Min(held.Low, bucket.Low) : bucket.Low,
                bucket.Close,
                Math.Max(held.Volume + added, bucket.Volume));
        }

        /// <summary>
        /// True when two stamps name the same bar of this feed's timeframe: closer together
        /// than half a bar. Equal stamps always are; an unparseable timeframe falls back to
        /// equality.
        /// </summary>
        private bool SamePeriod(DateTime a, DateTime b)
        {
            if (a == b) return true;
            long barMs = Sdk.Models.TimeframeUtility.ToMilliseconds(Identity.Timeframe);
            return barMs > 0 && Math.Abs((a - b).TotalMilliseconds) < barMs / 2.0;
        }

        private void Touch() => LastUpdateUtc = DateTime.UtcNow;

        public void Dispose()
        {
            _disposed = true;
            _prependLock.Dispose();
        }
    }
}
