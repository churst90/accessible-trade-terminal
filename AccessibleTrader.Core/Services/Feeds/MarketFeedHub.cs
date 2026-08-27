using System.Collections.Concurrent;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// The keyed-feed registry (docs/KEYED_FEEDS_DESIGN.md): one <see cref="ChartFeed"/>
    /// per <see cref="ChartIdentity"/>, a single FOCUSED feed bound to the workspace
    /// store by the DataManager adapter, and the legacy live pump that routes the
    /// one provider subscription (DataOrchestrator.LiveStream) into whichever feed
    /// holds focus. Phase B adds per-feed live subscriptions for providers that can
    /// multiplex; Phase C lets tab switches and background monitors serve straight
    /// from warm feeds.
    /// </summary>
    public interface IMarketFeedHub : IDisposable
    {
        ChartFeed GetOrCreateFeed(ChartIdentity identity);
        ChartFeed? TryGetFeed(ChartIdentity identity);

        /// <summary>The feed bound to the workspace store — null until first focus.</summary>
        ChartFeed? FocusedFeed { get; }

        /// <summary>Moves focus, creating the feed if needed. Does NOT start live
        /// updates — the caller controls that, exactly as DataManager.Identity
        /// assignment never started a stream.</summary>
        ChartFeed SetFocus(ChartIdentity identity);

        /// <summary>Raised for buffer changes on the FOCUSED feed only.</summary>
        event Action<ChartFeed, FeedUpdateKind>? FocusedFeedUpdated;

        /// <summary>
        /// Pins a feed against eviction while a consumer (background monitor, split
        /// view, hosted evaluator) depends on it. Dispose the lease to release.
        /// </summary>
        IDisposable AcquireLease(ChartIdentity identity);

        /// <summary>Starts the single legacy live subscription for the focused feed's
        /// identity and pumps its ticks into that feed.</summary>
        Task StartFocusedLiveAsync();
        Task StopFocusedLiveAsync();

        /// <summary>
        /// Tries to open an INDEPENDENT live subscription for this identity's feed
        /// (Phase B): requires a provider that supports multiple concurrent live
        /// subscriptions and a demo policy that allows live streaming. The result
        /// tells the caller WHY it didn't start, so a capability "no" can be
        /// cached while a transient failure is retried. Idempotent.
        /// </summary>
        Task<FeedLiveStart> TryStartFeedLiveAsync(ChartIdentity identity);
        Task StopFeedLiveAsync(ChartIdentity identity);
        bool IsFeedLive(ChartIdentity identity);
    }

    /// <summary>Outcome of <see cref="IMarketFeedHub.TryStartFeedLiveAsync"/>.
    /// NotSupported and PolicyDenied are PERMANENT for the session (cacheable);
    /// Unavailable is transient (provider missing / load failure) and worth
    /// retrying on a later reconcile.</summary>
    public enum FeedLiveStart { Started, AlreadyLive, NotSupported, PolicyDenied, Unavailable }

    public sealed class MarketFeedHub : IMarketFeedHub
    {
        // Feeds are ~240 KB each at the 5000-bar cap; 32 warm buffers is a few MB.
        // Beyond that the least-recently-updated unleased, non-focused feed is
        // evicted — cycling through many symbols must not grow memory unbounded.
        private const int MaxFeeds = 32;

        private readonly IDataOrchestrator _orchestrator;
        private readonly IDataService _dataService;
        private readonly DemoPolicy _demo;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<MarketFeedHub> _logger;
        // Null in unit tests that don't exercise the watchdog; the focused path
        // (LiveStreamManager) always has one, so background feeds should too.
        private readonly IGlobalErrorCoordinator? _errorCoordinator;
        private readonly ConcurrentDictionary<ChartIdentity, ChartFeed> _feeds = new();
        private readonly ConcurrentDictionary<ChartIdentity, int> _leaseCounts = new();
        // Per-feed independent live subscriptions (Phase B) — provider handles that
        // own their own socket + reconnection; disposing unsubscribes.
        private readonly ConcurrentDictionary<ChartIdentity, IAsyncDisposable> _feedSubscriptions = new();
        private readonly object _evictionLock = new();

        // ── Multi-live watchdog ──────────────────────────────────────────────
        // The keyed/background feeds' sockets self-reconnect (ReconnectingWebSocket),
        // but a feed that goes SILENT (socket up but no bars, or a wedged
        // reconnect) was never detected or announced — a blind trader relying on
        // background monitoring had no way to hear it. This mirrors
        // LiveStreamManager's watchdog for the focused feed. TickCount64 is
        // monotonic (immune to NTP/VM clock jumps).
        private sealed class LiveFeedWatch
        {
            public long LastTickMs = Environment.TickCount64;
            public bool QuietAnnounced;
            public int  RestartAttempts;
            public bool GaveUp;
        }
        internal enum WatchdogAction { None, AnnounceQuiet, Restart, GiveUp }
        private readonly ConcurrentDictionary<ChartIdentity, LiveFeedWatch> _watch = new();
        // Provider ErrorStream subscriptions, refcounted per provider so keyed
        // socket errors surface audibly without double-subscribing.
        private sealed class ProviderErrorSub { public IDisposable Sub = default!; public int RefCount; }
        private readonly ConcurrentDictionary<string, ProviderErrorSub> _providerErrorSubs = new();
        private readonly object _providerErrorLock = new();
        private CancellationTokenSource? _watchdogCts;
        private Task? _watchdogTask;
        private readonly object _watchdogLock = new();
        // Tunable (internal) so tests can drive the sweep without real waits.
        internal TimeSpan WatchdogInterval = TimeSpan.FromSeconds(30);
        internal long QuietThresholdMs   = 90_000;   // announce "quiet" once
        internal long RestartThresholdMs = 240_000;  // full resubscribe as a last resort
        internal int  MaxRestarts        = 3;

        private volatile ChartFeed? _focused;
        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;
        // Serializes focused-live start/stop. Both are fired-and-forgot from
        // refresh + catch-up + tab switches; two unsynchronized starts each saw
        // the other's not-yet-assigned fields, both started pumps, and the
        // loser's pump leaked for the process lifetime, stealing ticks.
        private readonly SemaphoreSlim _pumpGate = new(1, 1);

        public ChartFeed? FocusedFeed => _focused;
        public event Action<ChartFeed, FeedUpdateKind>? FocusedFeedUpdated;

        public MarketFeedHub(IDataOrchestrator orchestrator, IDataService dataService, DemoPolicy demo,
            ILoggerFactory loggerFactory, IGlobalErrorCoordinator? errorCoordinator = null)
        {
            _orchestrator = orchestrator;
            _dataService = dataService;
            _demo = demo;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MarketFeedHub>();
            _errorCoordinator = errorCoordinator;
        }

        public ChartFeed GetOrCreateFeed(ChartIdentity identity)
        {
            // Fast path must re-check disposal: eviction removes-then-disposes
            // under the lock, and a reader could otherwise grab the instance in
            // that window and use a dead feed.
            if (_feeds.TryGetValue(identity, out var existing) && !existing.IsDisposed) return existing;

            lock (_evictionLock)
            {
                if (_feeds.TryGetValue(identity, out var raced))
                {
                    if (!raced.IsDisposed) return raced;
                    _feeds.TryRemove(identity, out _);
                }
                return _feeds.GetOrAdd(identity, id =>
                {
                    EvictIfOverCapacity();
                    var feed = new ChartFeed(id, _orchestrator, _loggerFactory.CreateLogger<ChartFeed>());
                    feed.Updated += OnFeedUpdated;
                    return feed;
                });
            }
        }

        public ChartFeed? TryGetFeed(ChartIdentity identity)
            => _feeds.TryGetValue(identity, out var feed) ? feed : null;

        public ChartFeed SetFocus(ChartIdentity identity)
        {
            var feed = GetOrCreateFeed(identity);
            _focused = feed;
            return feed;
        }

        public IDisposable AcquireLease(ChartIdentity identity)
        {
            GetOrCreateFeed(identity);
            _leaseCounts.AddOrUpdate(identity, 1, (_, n) => n + 1);
            return new FeedLease(this, identity);
        }

        private void ReleaseLease(ChartIdentity identity)
        {
            // Remove the entry at zero instead of parking a 0 forever — the map
            // must not grow monotonically with every identity ever leased.
            while (_leaseCounts.TryGetValue(identity, out var n))
            {
                if (n <= 1)
                {
                    if (_leaseCounts.TryRemove(new KeyValuePair<ChartIdentity, int>(identity, n))) return;
                }
                else if (_leaseCounts.TryUpdate(identity, n - 1, n)) return;
            }
        }

        private void OnFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            if (ReferenceEquals(feed, _focused))
                FocusedFeedUpdated?.Invoke(feed, kind);
        }

        // ── Per-feed live subscriptions (Phase B) ────────────────────────────

        public bool IsFeedLive(ChartIdentity identity) => _feedSubscriptions.ContainsKey(identity);

        public async Task<FeedLiveStart> TryStartFeedLiveAsync(ChartIdentity identity)
        {
            if (string.IsNullOrEmpty(identity.Symbol)) return FeedLiveStart.Unavailable;
            if (_feedSubscriptions.ContainsKey(identity)) return FeedLiveStart.AlreadyLive;

            // Same policy gate as the focused path: providers with no real live
            // feed (demo historical-only tiers) must never be live-subscribed.
            if (!_demo.AllowsLiveStream(identity.Provider)) return FeedLiveStart.PolicyDenied;

            // Pin the feed for the duration: the subscription only registers (and
            // becomes eviction-exempt) AFTER the awaits below, and an unleased,
            // unfocused feed is otherwise a legal eviction victim mid-subscribe —
            // the new socket would tick into a disposed feed.
            using var pin = AcquireLease(identity);

            var provider = await _dataService.GetProviderAsync(identity.Provider).ConfigureAwait(false);
            if (provider == null) return FeedLiveStart.Unavailable;
            if (!provider.SupportsMultipleLiveSubscriptions) return FeedLiveStart.NotSupported;

            var feed = GetOrCreateFeed(identity);
            // GetOrAdd (not assignment) so a watchdog RESTART preserves the running
            // restart counter — a fresh start after StopFeedLiveAsync removed the
            // entry gets a clean one.
            var watch = _watch.GetOrAdd(identity, _ => new LiveFeedWatch());
            watch.LastTickMs = Environment.TickCount64;
            var subscription = await provider.SubscribeLiveAsync(
                identity.Market, identity.Symbol, identity.Timeframe,
                bar => { StampTick(identity); feed.ApplyLiveTick(bar); }).ConfigureAwait(false);

            if (!_feedSubscriptions.TryAdd(identity, subscription))
            {
                // A concurrent caller won the race — theirs is live, ours goes away.
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                AddProviderErrorSubscription(identity.Provider, provider);
                EnsureWatchdogRunning();
                _logger.LogInformation("MarketFeedHub: independent live subscription started for {Identity}.", identity);
            }
            return FeedLiveStart.Started;
        }

        public async Task StopFeedLiveAsync(ChartIdentity identity)
        {
            if (_feedSubscriptions.TryRemove(identity, out var subscription))
            {
                _watch.TryRemove(identity, out _);
                ReleaseProviderErrorSubscription(identity.Provider);
                await subscription.DisposeAsync().ConfigureAwait(false);
                _logger.LogInformation("MarketFeedHub: independent live subscription stopped for {Identity}.", identity);
            }
        }

        // ── Watchdog implementation ──────────────────────────────────────────

        /// <summary>Test seam: pretend a live feed's last bar arrived <paramref name="msAgo"/>
        /// milliseconds ago, so a watchdog sweep can be exercised without real waits.</summary>
        internal void BackdateFeedForTest(ChartIdentity identity, long msAgo)
        {
            if (_watch.TryGetValue(identity, out var w))
                w.LastTickMs = Environment.TickCount64 - msAgo;
        }

        private void StampTick(ChartIdentity identity)
        {
            if (_watch.TryGetValue(identity, out var w))
            {
                w.LastTickMs = Environment.TickCount64;
                w.QuietAnnounced = false;
                w.RestartAttempts = 0;
                w.GaveUp = false;
            }
        }

        /// <summary>Pure silence-to-action decision — unit-tested directly.</summary>
        internal WatchdogAction EvaluateSilence(long silentMs, bool quietAnnounced, int restartAttempts, bool gaveUp)
        {
            if (silentMs < QuietThresholdMs) return WatchdogAction.None;
            if (silentMs >= RestartThresholdMs)
            {
                if (gaveUp) return WatchdogAction.None;
                return restartAttempts >= MaxRestarts ? WatchdogAction.GiveUp : WatchdogAction.Restart;
            }
            return quietAnnounced ? WatchdogAction.None : WatchdogAction.AnnounceQuiet;
        }

        private void EnsureWatchdogRunning()
        {
            lock (_watchdogLock)
            {
                if (_watchdogTask is { IsCompleted: false }) return;
                _watchdogCts = new CancellationTokenSource();
                var token = _watchdogCts.Token;
                _watchdogTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            await Task.Delay(WatchdogInterval, token).ConfigureAwait(false);
                            await RunWatchdogSweepAsync().ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { /* normal stop */ }
                    catch (Exception ex) { _logger.LogWarning(ex, "MarketFeedHub watchdog exited on error."); }
                }, token);
            }
        }

        /// <summary>One pass over the live feeds: announce prolonged quiet once,
        /// restart on extended silence (bounded), give up after that. Internal so a
        /// test can drive it deterministically. A tick resets a feed's state.</summary>
        internal async Task RunWatchdogSweepAsync()
        {
            long now = Environment.TickCount64;
            foreach (var kv in _watch)
            {
                var identity = kv.Key;
                var w = kv.Value;
                if (!_feedSubscriptions.ContainsKey(identity)) continue; // stopped mid-sweep

                switch (EvaluateSilence(now - w.LastTickMs, w.QuietAnnounced, w.RestartAttempts, w.GaveUp))
                {
                    case WatchdogAction.AnnounceQuiet:
                        w.QuietAnnounced = true;
                        _logger.LogInformation("MarketFeedHub: background feed {Identity} quiet.", identity);
                        _errorCoordinator?.ReportError(
                            $"Background feed for {identity.Symbol} on {identity.Provider} has gone quiet; it may only update on refresh.",
                            ErrorSeverity.Low, ErrorCategory.Informational);
                        break;

                    case WatchdogAction.Restart:
                        w.RestartAttempts++;
                        _logger.LogWarning("MarketFeedHub: restarting silent background feed {Identity} (attempt {Attempt}/{Max}).",
                            identity, w.RestartAttempts, MaxRestarts);
                        _errorCoordinator?.ReportError(
                            $"Background feed for {identity.Symbol} on {identity.Provider} went silent; reconnecting ({w.RestartAttempts}/{MaxRestarts}).",
                            ErrorSeverity.Low, ErrorCategory.Informational);
                        await RestartFeedAsync(identity).ConfigureAwait(false);
                        break;

                    case WatchdogAction.GiveUp:
                        w.GaveUp = true;
                        _logger.LogError("MarketFeedHub: background feed {Identity} gave up after {Max} restarts.", identity, MaxRestarts);
                        _errorCoordinator?.ReportError(
                            $"Background feed for {identity.Symbol} on {identity.Provider} stopped updating after {MaxRestarts} reconnect attempts.",
                            ErrorSeverity.Medium, ErrorCategory.Provider);
                        break;
                }
            }
        }

        private async Task RestartFeedAsync(ChartIdentity identity)
        {
            // Reset the clock first so the restart isn't immediately re-triggered by
            // the same sweep window; the preserved RestartAttempts counter still
            // advances toward GiveUp if ticks never resume.
            if (_watch.TryGetValue(identity, out var w)) w.LastTickMs = Environment.TickCount64;
            if (_feedSubscriptions.TryRemove(identity, out var old))
            {
                ReleaseProviderErrorSubscription(identity.Provider);
                try { await old.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogDebug(ex, "Old subscription dispose threw during restart (non-fatal)."); }
            }
            try { await TryStartFeedLiveAsync(identity).ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "Feed restart failed for {Identity}.", identity); }

            // GAP-FILL THE OUTAGE. A restart resubscribes and announces success; it does not
            // fetch the bars that closed while the feed was down. With the silence threshold
            // at a minute and several restart attempts before giving up, a routine flap costs
            // minutes of bars — on a 1m chart, several bars spliced invisibly into the buffer.
            // ChartFeed.ApplyLiveTick then appends the first post-restart tick straight onto
            // the pre-outage bar with no continuity check, so indicators, sonification and the
            // strategy engine's bar-close driver all compute across a discontinuity they
            // cannot see.
            //
            // GapFillAsync refuses a splice it cannot make whole and raises GapTooLarge, so a
            // long outage is reported rather than papered over.
            try
            {
                if (_feeds.TryGetValue(identity, out var feed))
                    await feed.GapFillAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gap-fill after feed restart failed for {Identity}.", identity);
            }
        }

        private void AddProviderErrorSubscription(string providerName, IMarketDataProvider provider)
        {
            lock (_providerErrorLock)
            {
                if (_providerErrorSubs.TryGetValue(providerName, out var existing))
                {
                    existing.RefCount++;
                    return;
                }
                var sub = provider.ErrorStream.Subscribe(err =>
                    _errorCoordinator?.ReportError(err, ErrorSeverity.Medium, ErrorCategory.Provider));
                _providerErrorSubs[providerName] = new ProviderErrorSub { Sub = sub, RefCount = 1 };
            }
        }

        private void ReleaseProviderErrorSubscription(string providerName)
        {
            lock (_providerErrorLock)
            {
                if (!_providerErrorSubs.TryGetValue(providerName, out var e)) return;
                if (--e.RefCount <= 0)
                {
                    e.Sub.Dispose();
                    _providerErrorSubs.TryRemove(providerName, out _);
                }
            }
        }

        // Called under _evictionLock, before adding the new feed.
        private void EvictIfOverCapacity()
        {
            if (_feeds.Count < MaxFeeds) return;

            var victim = _feeds.Values
                .Where(f => !ReferenceEquals(f, _focused))
                .Where(f => !_leaseCounts.TryGetValue(f.Identity, out var n) || n == 0)
                .Where(f => !_feedSubscriptions.ContainsKey(f.Identity)) // live feeds are wanted feeds
                .OrderBy(f => f.LastUpdateUtc)
                .FirstOrDefault();
            if (victim == null) return; // everything pinned — allow temporary overshoot

            if (_feeds.TryRemove(victim.Identity, out var removed))
            {
                removed.Updated -= OnFeedUpdated;
                removed.Dispose();
                _logger.LogInformation("MarketFeedHub: evicted cold feed {Identity} (capacity {Max}).",
                    removed.Identity, MaxFeeds);
            }
        }

        public async Task StartFocusedLiveAsync()
        {
            await _pumpGate.WaitAsync().ConfigureAwait(false);
            try { await StartFocusedLiveCoreAsync().ConfigureAwait(false); }
            finally { _pumpGate.Release(); }
        }

        public async Task StopFocusedLiveAsync()
        {
            await _pumpGate.WaitAsync().ConfigureAwait(false);
            try { await StopFocusedLiveCoreAsync().ConfigureAwait(false); }
            finally { _pumpGate.Release(); }
        }

        private async Task StartFocusedLiveCoreAsync()
        {
            await StopFocusedLiveCoreAsync().ConfigureAwait(false);

            var feed = _focused;
            if (feed == null || string.IsNullOrEmpty(feed.Identity.Symbol)) return;

            // Drain residue before pumping: the orchestrator's channel may still
            // hold buffered ticks of the PREVIOUS subscription. The identity check
            // in the pump is what makes cross-symbol merging impossible; this drain
            // is the cheaper half of the same job — it also sheds STALE ticks of the
            // same identity, which the identity check cannot distinguish.
            while (_orchestrator.LiveStream.TryRead(out _)) { }

            _pumpCts = new CancellationTokenSource();
            var token = _pumpCts.Token;

            _pumpTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var tick in _orchestrator.LiveStream.ReadAllAsync(token))
                    {
                        // Route to whichever feed holds focus NOW, but ONLY if the tick
                        // was subscribed for that same identity.
                        //
                        // Focus moves synchronously (DataManager.Identity → SetFocus) while
                        // the subscription is retargeted at the END of the catch-up, after
                        // an awaited gap-fill round-trip. For that whole window this pump
                        // holds the OUTGOING symbol's ticks and the INCOMING symbol's feed,
                        // and applying one to the other corrupts the last bar (ReplaceLast)
                        // or fabricates a bar at the wrong symbol's price (Append) — which
                        // raises LiveAppend, which is what StrategyEngine evaluates a closed
                        // bar on, which can place a real order.
                        //
                        // The comment that used to sit here asserted this could not happen
                        // "because the subscription is retargeted by the caller". It is, but
                        // not before focus moves. Compare, don't assume.
                        var target = _focused;
                        if (target == null) continue;
                        if (target.Identity != tick.Identity)
                        {
                            _logger.LogDebug(
                                "MarketFeedHub: dropped a live tick for {TickIdentity} while {FocusIdentity} holds focus (subscription retarget in flight).",
                                tick.Identity, target.Identity);
                            continue;
                        }
                        target.ApplyLiveTick(tick.Bar);
                    }
                }
                catch (OperationCanceledException) { /* normal stop */ }
                catch (Exception ex) { _logger.LogWarning(ex, "Focused live pump exited on error."); }
            }, token);

            var id = feed.Identity;
            await _orchestrator.StartLiveStreamAsync(id.Market, id.Provider, id.Symbol, id.Timeframe).ConfigureAwait(false);
        }

        private async Task StopFocusedLiveCoreAsync()
        {
            _pumpCts?.Cancel();
            var pump = _pumpTask;
            if (pump != null)
            {
                // Bounded wait: the pump exits promptly on cancellation, but a hung
                // provider read must not wedge a tab switch forever.
                await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            }
            _pumpTask = null;
            _pumpCts?.Dispose();
            _pumpCts = null;
            await _orchestrator.StopLiveStreamAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            _watchdogCts?.Cancel();
            try { _watchdogTask?.Wait(TimeSpan.FromMilliseconds(500)); }
            catch { /* cancellation surfaces as AggregateException — expected */ }
            _watchdogCts?.Dispose();
            lock (_providerErrorLock)
            {
                foreach (var e in _providerErrorSubs.Values) e.Sub.Dispose();
                _providerErrorSubs.Clear();
            }

            _pumpCts?.Cancel();
            try { _pumpTask?.Wait(TimeSpan.FromMilliseconds(500)); }
            catch { /* cancellation surfaces as AggregateException — expected */ }
            _pumpCts?.Dispose();
            foreach (var identity in _feedSubscriptions.Keys.ToList())
            {
                if (_feedSubscriptions.TryRemove(identity, out var subscription))
                {
                    try { subscription.DisposeAsync().AsTask().Wait(TimeSpan.FromMilliseconds(500)); }
                    catch { /* best-effort teardown */ }
                }
            }
            foreach (var feed in _feeds.Values)
            {
                feed.Updated -= OnFeedUpdated;
                feed.Dispose();
            }
            _feeds.Clear();
            _pumpGate.Dispose();
        }

        private sealed class FeedLease : IDisposable
        {
            private MarketFeedHub? _hub;
            private readonly ChartIdentity _identity;
            public FeedLease(MarketFeedHub hub, ChartIdentity identity) { _hub = hub; _identity = identity; }
            public void Dispose()
            {
                Interlocked.Exchange(ref _hub, null)?.ReleaseLease(_identity);
            }
        }
    }
}
