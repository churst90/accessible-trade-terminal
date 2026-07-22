using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// Keeps NON-focused tabs' feeds live (Phase C, docs/KEYED_FEEDS_DESIGN.md):
    /// when the opt-in setting is on, every background tab whose provider can
    /// multiplex live subscriptions gets a leased, live-subscribed feed — so
    /// switching back to the tab is instant (warm-feed fast path, no network)
    /// and background monitors evaluate on tick-fresh bars instead of a 30s
    /// poll. Providers that can't multiplex, and everything past the cap, keep
    /// today's poll behavior. Mirrors BackgroundMonitoringService's reconcile
    /// pattern; the two are complementary — monitors EVALUATE, this feeds them.
    /// </summary>
    public interface IBackgroundTabFeedService
    {
        void Reconcile();
        /// <summary>Identities whose background live feed THIS service started.</summary>
        IReadOnlyCollection<ChartIdentity> LiveBackgroundFeeds { get; }
    }

    public sealed class BackgroundTabFeedService : IBackgroundTabFeedService, IDisposable
    {
        public const string EnabledKey = SettingsKeys.LiveBackgroundTabs;
        // Sockets are cheap but not free; past the cap the oldest tabs simply
        // stay poll-driven. log()s when tabs are dropped so silence never reads
        // as coverage.
        public const int MaxLiveBackgroundFeeds = 8;

        private readonly IWorkspaceStore _store;
        private readonly ISettingsManager _settings;
        private readonly IMarketFeedHub _hub;
        private readonly ILogger<BackgroundTabFeedService> _logger;

        private readonly object _gate = new();
        // Leases pin the feeds against hub eviction while we keep them live.
        private readonly Dictionary<ChartIdentity, IDisposable> _leases = new();
        // Providers that answered "can't multiplex" once — retrying on every tab
        // switch would spam provider lookups (and gap-fill fetches) for nothing.
        private readonly HashSet<string> _nonMultiplexProviders = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IDisposable> _subs = new();

        public BackgroundTabFeedService(
            IWorkspaceStore store,
            IEventBus eventBus,
            ISettingsManager settings,
            IMarketFeedHub hub,
            ILogger<BackgroundTabFeedService> logger)
        {
            _store = store;
            _settings = settings;
            _hub = hub;
            _logger = logger;

            _subs.Add(eventBus.Subscribe<TabSwitchedEvent>(_ => Reconcile()));
            _subs.Add(_store.StateStream
                .Select(s => (s.TabSnapshots?.Count ?? 0, s.ActiveTabIndex))
                .DistinctUntilChanged()
                .Subscribe(_ => Reconcile()));
        }

        public bool IsEnabled => _settings.GetSetting(EnabledKey)?.ToObject<bool>() ?? false;

        public IReadOnlyCollection<ChartIdentity> LiveBackgroundFeeds
        {
            get { lock (_gate) return _leases.Keys.ToList(); }
        }

        public void Reconcile()
        {
            var state = _store.State;
            var desired = new Dictionary<ChartIdentity, TimeSeriesBuffer<Ohlcv>>();

            if (IsEnabled && state.TabSnapshots != null)
            {
                int dropped = 0;
                foreach (var snapshot in state.TabSnapshots)
                {
                    var identity = snapshot.Identity;
                    if (string.IsNullOrEmpty(identity.Symbol)) continue;
                    if (identity == state.Identity) continue; // focused tab has the legacy live path
                    if (_nonMultiplexProviders.Contains(identity.Provider)) continue;
                    if (desired.ContainsKey(identity)) continue;
                    if (desired.Count >= MaxLiveBackgroundFeeds) { dropped++; continue; }
                    desired[identity] = snapshot.Data;
                }
                if (dropped > 0)
                    _logger.LogInformation(
                        "BackgroundTabFeedService: {Dropped} background tab(s) beyond the {Max}-feed cap stay poll-driven.",
                        dropped, MaxLiveBackgroundFeeds);
            }

            List<ChartIdentity> toStop;
            List<KeyValuePair<ChartIdentity, TimeSeriesBuffer<Ohlcv>>> toStart;
            lock (_gate)
            {
                toStop = _leases.Keys.Where(k => !desired.ContainsKey(k)).ToList();
                toStart = desired.Where(kv => !_leases.ContainsKey(kv.Key)).ToList();

                foreach (var identity in toStop)
                {
                    if (_leases.Remove(identity, out var lease)) lease.Dispose();
                }
                // Reserve synchronously so overlapping reconciles never double-start.
                foreach (var kv in toStart)
                    _leases[kv.Key] = _hub.AcquireLease(kv.Key);
            }

            foreach (var identity in toStop)
            {
                var id = identity;
                SafeFireAndForget.Run(() => _hub.StopFeedLiveAsync(id), _logger, "StopBackgroundFeed");
            }
            foreach (var kv in toStart)
            {
                var id = kv.Key; var snapshot = kv.Value;
                SafeFireAndForget.Run(() => StartOneAsync(id, snapshot), _logger, "StartBackgroundFeed");
            }
        }

        private async Task StartOneAsync(ChartIdentity identity, TimeSeriesBuffer<Ohlcv> snapshot)
        {
            bool live = await _hub.TryStartFeedLiveAsync(identity).ConfigureAwait(false);
            if (!live)
            {
                // Provider can't multiplex (or the demo policy said no). Remember,
                // release the pin, and leave the tab to the poll paths.
                _nonMultiplexProviders.Add(identity.Provider);
                lock (_gate)
                {
                    if (_leases.Remove(identity, out var lease)) lease.Dispose();
                }
                return;
            }

            // Warm the buffer so the eventual tab-switch fast path has full
            // scrollback: live ticks alone would give fresh-but-shallow history,
            // which DataManager correctly refuses to bind over a deeper snapshot.
            var feed = _hub.GetOrCreateFeed(identity);
            if (feed.Bars.Count == 0 && snapshot.Count > 0)
                feed.RestoreSnapshot(snapshot);
            if (feed.Bars.Count > 0)
            {
                try { await feed.GapFillAsync().ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background feed gap-fill failed for {Identity}; live ticks continue.", identity);
                }
            }
        }

        public void Dispose()
        {
            foreach (var sub in _subs) sub.Dispose();
            lock (_gate)
            {
                foreach (var (identity, lease) in _leases.ToList())
                {
                    lease.Dispose();
                    var id = identity;
                    SafeFireAndForget.Run(() => _hub.StopFeedLiveAsync(id), _logger, "StopBackgroundFeedOnDispose");
                }
                _leases.Clear();
            }
        }
    }
}
