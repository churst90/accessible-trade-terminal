using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
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
    }

    public sealed class MarketFeedHub : IMarketFeedHub
    {
        // Feeds are ~240 KB each at the 5000-bar cap; 32 warm buffers is a few MB.
        // Beyond that the least-recently-updated unleased, non-focused feed is
        // evicted — cycling through many symbols must not grow memory unbounded.
        private const int MaxFeeds = 32;

        private readonly IDataOrchestrator _orchestrator;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<MarketFeedHub> _logger;
        private readonly ConcurrentDictionary<ChartIdentity, ChartFeed> _feeds = new();
        private readonly ConcurrentDictionary<ChartIdentity, int> _leaseCounts = new();
        private readonly object _evictionLock = new();

        private volatile ChartFeed? _focused;
        private CancellationTokenSource? _pumpCts;
        private Task? _pumpTask;

        public ChartFeed? FocusedFeed => _focused;
        public event Action<ChartFeed, FeedUpdateKind>? FocusedFeedUpdated;

        public MarketFeedHub(IDataOrchestrator orchestrator, ILoggerFactory loggerFactory)
        {
            _orchestrator = orchestrator;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MarketFeedHub>();
        }

        public ChartFeed GetOrCreateFeed(ChartIdentity identity)
        {
            if (_feeds.TryGetValue(identity, out var existing)) return existing;

            lock (_evictionLock)
            {
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
            _leaseCounts.AddOrUpdate(identity, 0, (_, n) => Math.Max(0, n - 1));
        }

        private void OnFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            if (ReferenceEquals(feed, _focused))
                FocusedFeedUpdated?.Invoke(feed, kind);
        }

        // Called under _evictionLock, before adding the new feed.
        private void EvictIfOverCapacity()
        {
            if (_feeds.Count < MaxFeeds) return;

            var victim = _feeds.Values
                .Where(f => !ReferenceEquals(f, _focused))
                .Where(f => !_leaseCounts.TryGetValue(f.Identity, out var n) || n == 0)
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
            await StopFocusedLiveAsync().ConfigureAwait(false);

            var feed = _focused;
            if (feed == null || string.IsNullOrEmpty(feed.Identity.Symbol)) return;

            _pumpCts = new CancellationTokenSource();
            var token = _pumpCts.Token;

            _pumpTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var tick in _orchestrator.LiveStream.ReadAllAsync(token))
                    {
                        // Route to whichever feed holds focus NOW — a tab switch mid-pump
                        // must not deliver the outgoing symbol's ticks to the old buffer
                        // (the orchestrator's subscription is retargeted by the caller).
                        _focused?.ApplyLiveTick(tick);
                    }
                }
                catch (OperationCanceledException) { /* normal stop */ }
                catch (Exception ex) { _logger.LogWarning(ex, "Focused live pump exited on error."); }
            }, token);

            var id = feed.Identity;
            await _orchestrator.StartLiveStreamAsync(id.Market, id.Provider, id.Symbol, id.Timeframe).ConfigureAwait(false);
        }

        public async Task StopFocusedLiveAsync()
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
            _pumpCts?.Cancel();
            try { _pumpTask?.Wait(TimeSpan.FromMilliseconds(500)); }
            catch { /* cancellation surfaces as AggregateException — expected */ }
            _pumpCts?.Dispose();
            foreach (var feed in _feeds.Values)
            {
                feed.Updated -= OnFeedUpdated;
                feed.Dispose();
            }
            _feeds.Clear();
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
