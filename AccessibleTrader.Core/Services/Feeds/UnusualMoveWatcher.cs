using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.ChangeLog;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Feeds
{
    /// <summary>
    /// Stage 5c's producer for the change log's "Unusual moves": at every bar close on the
    /// focused chart and on each live background tab, <see cref="UnusualMoveRules"/> compares the
    /// closed bar with that chart's own usual move and publishes <see cref="UnusualMoveEvent"/>
    /// when it was far outside it. Nothing is spoken; Alt+U reads it. The browser-closed half is
    /// <c>LocalBackgroundMonitor</c>, with the same rule, and the change log's key makes the two
    /// one entry.
    /// </summary>
    public sealed class UnusualMoveWatcher : IDisposable
    {
        private readonly IMarketFeedHub _hub;
        private readonly IBackgroundTabFeedService _tabFeeds;
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _bus;
        private readonly ILogger<UnusualMoveWatcher>? _logger;

        public UnusualMoveWatcher(
            IMarketFeedHub hub,
            IBackgroundTabFeedService tabFeeds,
            IWorkspaceStore store,
            IEventBus bus,
            ILogger<UnusualMoveWatcher>? logger = null)
        {
            _hub = hub;
            _tabFeeds = tabFeeds;
            _store = store;
            _bus = bus;
            _logger = logger;
            _hub.FocusedFeedUpdated += OnFocusedFeedUpdated;
            _hub.BackgroundFeedUpdated += OnBackgroundFeedUpdated;
        }

        private void OnFocusedFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            if (kind == FeedUpdateKind.LiveAppend) Check(feed);
        }

        private void OnBackgroundFeedUpdated(ChartFeed feed, FeedUpdateKind kind)
        {
            // Only tabs the user opened; the hub also holds feeds leased by monitors and splits.
            if (kind != FeedUpdateKind.LiveAppend) return;
            try
            {
                if (!_tabFeeds.LiveBackgroundFeeds.Contains(feed.Identity)) return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not determine background-tab membership for {Symbol}.", feed.Identity.Symbol);
                return;
            }
            Check(feed);
        }

        private void Check(ChartFeed feed)
        {
            try
            {
                // Replay's bars are history being stepped through, not a move happening now.
                if (_store.State.IsPlaying) return;
                if (UnusualMoveRules.AtClose(feed.Bars) is not { } move) return;
                _bus.Publish(new UnusualMoveEvent(feed.Identity, move.BarUtc,
                    UnusualMoveRules.Sentence(feed.Identity, move)));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unusual-move check failed for {Symbol}.", feed.Identity.Symbol);
            }
        }

        public void Dispose()
        {
            _hub.FocusedFeedUpdated -= OnFocusedFeedUpdated;
            _hub.BackgroundFeedUpdated -= OnBackgroundFeedUpdated;
        }
    }
}
