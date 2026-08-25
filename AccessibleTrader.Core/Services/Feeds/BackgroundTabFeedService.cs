using System.Reactive.Linq;
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
        // Tail of the serialized stop/start chain — see Reconcile for why order matters.
        private Task _applyChain = Task.CompletedTask;
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
            // Loading a different symbol/timeframe on the CURRENT tab changes the
            // focused identity without a TabSwitchedEvent — and if the new identity
            // matches a live background feed, that feed must stop or the legacy
            // pump and the independent subscription double-feed one buffer.
            _subs.Add(_store.StateStream
                .Select(s => s.Identity)
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

            HashSet<string> blacklist;
            lock (_gate) blacklist = new HashSet<string>(_nonMultiplexProviders, StringComparer.OrdinalIgnoreCase);

            if (IsEnabled && state.TabSnapshots != null)
            {
                int dropped = 0;
                foreach (var snapshot in state.TabSnapshots)
                {
                    var identity = snapshot.Identity;
                    if (string.IsNullOrEmpty(identity.Symbol)) continue;
                    if (identity == state.Identity) continue; // focused tab has the legacy live path
                    if (blacklist.Contains(identity.Provider)) continue;
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

                // SERIALIZED apply: stops and starts from successive reconciles must
                // execute in order. Unordered fire-and-forget let a queued stop from
                // reconcile N dispose the subscription that reconcile N+1's start
                // had just been told "already live" about — leaving the tab leased
                // but dead until it left the desired set.
                var stops = toStop; var starts = toStart;
                var previous = _applyChain;
                _applyChain = Task.Run(async () =>
                {
                    await previous.ConfigureAwait(false);
                    foreach (var id in stops)
                    {
                        try { await _hub.StopFeedLiveAsync(id).ConfigureAwait(false); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Stopping background feed {Identity} failed.", id); }
                    }
                    foreach (var kv in starts)
                    {
                        await StartOneAsync(kv.Key, kv.Value).ConfigureAwait(false);
                    }
                });
            }
        }

        private async Task StartOneAsync(ChartIdentity identity, TimeSeriesBuffer<Ohlcv> snapshot)
        {
            FeedLiveStart result;
            try
            {
                result = await _hub.TryStartFeedLiveAsync(identity).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Starting background feed {Identity} failed; will retry on a later reconcile.", identity);
                result = FeedLiveStart.Unavailable;
            }

            if (result is FeedLiveStart.NotSupported or FeedLiveStart.PolicyDenied)
            {
                // PERMANENT for the session — don't re-probe on every tab switch.
                // Transient failures (Unavailable) are deliberately NOT cached so
                // a plugin that loads late gets retried.
                lock (_gate) _nonMultiplexProviders.Add(identity.Provider);
            }

            if (result is not (FeedLiveStart.Started or FeedLiveStart.AlreadyLive))
            {
                // Release the pin so the feed is evictable and the identity is
                // eligible for a retry on the next reconcile.
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
