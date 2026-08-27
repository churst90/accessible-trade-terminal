using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Is the chart in front of me live?</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Three watchdogs each spoke <i>once</i>, into a transient channel:
    /// <c>LiveStreamManager</c> announced connected-but-quiet once per subscription,
    /// <c>MarketFeedHub</c> announced background-feed quiet/restart/give-up, and
    /// <c>DataOrchestrator</c>'s breaker announced a trip and a reset. After that there was
    /// <b>no queryable state at all</b>. <c>DataStatus</c> was
    /// <c>{ Idle, LoadingHistorical, Filling, Ready, Error }</c> — no Stale, no Degraded — and
    /// nothing set <c>Error</c> from a live-feed stall. <c>DataState</c> on the orchestrator
    /// has <c>Stalled</c> and <c>NetworkLagged</c> but they are unreachable and its
    /// <c>StateChanged</c> has no consumers outside the class; <c>ConnectionManager</c> is dead.
    /// </para>
    ///
    /// <para>
    /// So a user who missed the spoken line — a screen reader interrupted mid-sentence, an
    /// announcement fired while they were inside a modal — had <b>no way to ask</b> whether the
    /// prices in front of them were current. For a product whose whole premise is that the
    /// spoken text is the interface, an unanswerable question is a missing feature.
    /// </para>
    /// </summary>
    public class FeedHonestyTests
    {
        private static WorkspaceState WithFeed(DataStatus status, DateTime? lastTick) =>
            WorkspaceState.Initial with { DataStatus = status, LastTickUtc = lastTick };

        [Fact]
        public void A_live_feed_reports_how_long_since_the_last_update()
        {
            var s = WithFeed(DataStatus.Ready, DateTime.UtcNow.AddSeconds(-20));

            string said = ChartLayoutDescriber.DescribeFeedFreshness(s);

            Assert.Contains("live", said, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("seconds", said, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_quiet_feed_says_so_and_says_how_long()
        {
            // The elapsed time, not just the word: "no data for eleven minutes" is actionable
            // in a way that "stale" is not.
            var s = WithFeed(DataStatus.Stale, DateTime.UtcNow.AddMinutes(-11));

            string said = ChartLayoutDescriber.DescribeFeedFreshness(s);

            Assert.Contains("QUIET", said, StringComparison.Ordinal);
            Assert.Contains("11 minutes", said, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_chart_that_has_never_ticked_is_not_called_stale()
        {
            // A historical-only provider is working exactly as intended. Calling that "quiet"
            // would cry wolf on every analytics chart, and a warning that fires when nothing
            // is wrong stops being read.
            var s = WithFeed(DataStatus.Ready, lastTick: null);

            string said = ChartLayoutDescriber.DescribeFeedFreshness(s);

            Assert.DoesNotContain("QUIET", said, StringComparison.Ordinal);
            Assert.Contains("No live data yet", said, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_layout_description_answers_the_question_at_all()
        {
            // Alt+Shift+L is the orientation key — "the one thing a sighted user gets for free
            // by glancing at the screen" — and until now it could not answer the question that
            // matters most about a trading chart.
            // With bars: Describe returns early on an empty chart, and a chart with no data
            // is not the case this question is about.
            var bars = new TimeSeriesBuffer<Ohlcv>(new[]
            {
                new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 110, 95, 105, 10),
                new Ohlcv(new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), 105, 115, 100, 110, 10),
            });
            var state = WithFeed(DataStatus.Stale, DateTime.UtcNow.AddMinutes(-3)) with
            {
                Data = bars,
                CurrentDataIndex = 1,
                ViewportStartIndex = 0,
                ViewportLength = 2,
            };

            string said = ChartLayoutDescriber.Describe(state, "BTC/USD", "1h");

            Assert.Contains("QUIET", said, StringComparison.Ordinal);
        }

        // ── The state transitions ────────────────────────────────────────────

        private static WorkspaceStoreHarness NewStore() => new();

        private sealed class WorkspaceStoreHarness
        {
            public readonly AccessibleTrader.Core.Services.WorkspaceStore Store =
                new(new AccessibleTrader.Core.Services.EventBus(),
                    new MockViewportRangeCalculator(),
                    new MockViewportNavigationService(),
                    new MockVolumeStateService());
        }

        [Fact]
        public void A_watchdog_verdict_becomes_state_rather_than_only_a_spoken_line()
        {
            var h = NewStore();

            h.Store.Dispatch(new MarkFeedStaleAction());

            Assert.Equal(DataStatus.Stale, h.Store.State.DataStatus);
        }

        [Fact]
        public void A_tick_clears_stale_so_recovery_is_as_visible_as_the_failure()
        {
            // A status that only ever goes one way is a status nobody can trust the second
            // time: someone who heard "quiet" would keep distrusting a feed that came back.
            var h = NewStore();
            h.Store.Dispatch(new MarkFeedStaleAction());

            var at = DateTime.UtcNow;
            h.Store.Dispatch(new LiveTickObservedAction(at));

            Assert.Equal(DataStatus.Ready, h.Store.State.DataStatus);
            Assert.Equal(at, h.Store.State.LastTickUtc);
        }

        [Fact]
        public void A_tick_does_not_overwrite_a_genuine_error_status()
        {
            // Stale is the only status a tick clears. Turning an Error into Ready because one
            // bar arrived would hide a real failure behind a coincidence.
            var h = NewStore();
            h.Store.Dispatch(new SetDataStatusAction(DataStatus.Error));

            h.Store.Dispatch(new LiveTickObservedAction(DateTime.UtcNow));

            Assert.Equal(DataStatus.Error, h.Store.State.DataStatus);
            Assert.NotNull(h.Store.State.LastTickUtc);
        }

        [Fact]
        public void Marking_stale_does_not_erase_when_the_last_tick_was()
        {
            // "How long has it been quiet" has to stay answerable — that is the number the
            // user acts on.
            var h = NewStore();
            var at = DateTime.UtcNow.AddMinutes(-5);
            h.Store.Dispatch(new LiveTickObservedAction(at));

            h.Store.Dispatch(new MarkFeedStaleAction());

            Assert.Equal(at, h.Store.State.LastTickUtc);
            Assert.Equal(DataStatus.Stale, h.Store.State.DataStatus);
        }
    }
}
