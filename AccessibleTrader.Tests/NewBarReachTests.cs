using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Which charts can produce a new-bar announcement at all — measured, not read.</b>
    ///
    /// <para>Phase 3 of docs/BACKGROUND_MONITOR_SCOPE.md is "new bars with the browser closed".
    /// Before designing that, this file pins what the CURRENT reach is, because the scope
    /// document's table claims new bars work with the browser open (✅) and fail only with it
    /// closed. That claim is about a subscriber; nothing had measured the PUBLISHER.</para>
    ///
    /// <para>There is exactly one publisher of <see cref="NewBarEvent"/> in the codebase —
    /// <c>WorkspaceStore.Dispatch</c>, gated on <c>isLiveDataAction</c>, i.e. an
    /// <see cref="UpdateDataAction"/> with <c>IsInitialLoad: false</c>. The only thing that
    /// dispatches one from a live tick is <c>DataManager.OnFocusedFeedUpdated</c>, subscribed to
    /// <c>IMarketFeedHub.FocusedFeedUpdated</c>, which <c>MarketFeedHub.OnFeedUpdated</c> raises
    /// only when <c>ReferenceEquals(feed, _focused)</c>. So the reach of the whole new-bar
    /// feature — toast, speech, narration — is ONE chart: the focused one.</para>
    ///
    /// <para>That matters because <c>BackgroundTabFeedService</c> deliberately keeps up to
    /// <c>MaxLiveBackgroundFeeds</c> (8) non-focused tabs on live subscriptions. Those feeds
    /// tick, their buffers grow, their bars close — and nothing announced it. A user who opened
    /// four charts to watch four markets was told about bar closes on one of them.</para>
    ///
    /// <para><b>CLOSED 2026-09-08 by Phase 3 D3</b> — see <c>BackgroundBarAnnouncerTests</c>.
    /// The fix was NOT to widen <see cref="NewBarEvent"/>'s reach, so every assertion below
    /// still holds and must keep holding: that event carries no identity and its subscribers
    /// read the rest out of the focused chart's state, so a background feed publishing one would
    /// describe the wrong chart. Background tabs announce through
    /// <c>BackgroundBarClosedEvent</c>, which names its own symbol. These tests are now the
    /// guard on that boundary rather than a record of a gap.</para>
    ///
    /// <para>Each test below is written as a PAIR — the focused case and the background case —
    /// because a test that exercises only the working state proves nothing about the state that
    /// breaks.</para>
    /// </summary>
    public class NewBarReachTests
    {
        private static Ohlcv Bar(int hours, double close = 100) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hours),
                close, close + 1, close - 1, close, 10);

        private static ChartIdentity Id(string symbol) => new("Spot", "TestProv", symbol, "1h");

        private sealed class Harness : IDisposable
        {
            public readonly EventBus Bus = new();
            public readonly WorkspaceStore Store;
            public readonly MarketFeedHub Hub;
            public readonly DataManager Manager;
            public readonly List<NewBarEvent> NewBars = new();
            private readonly IDisposable _sub;

            public Harness()
            {
                Store = new WorkspaceStore(Bus, new ViewportRangeCalculator(),
                    new ViewportNavigationService(), new VolumeStateService());
                Hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(),
                    Substitute.For<IDataService>(), new DemoPolicy(isDemo: false),
                    NullLoggerFactory.Instance);
                Manager = new DataManager(Hub, Store, Bus,
                    NullLogger<DataManager>.Instance, Substitute.For<IServiceProvider>());
                _sub = Bus.Subscribe<NewBarEvent>(e => { lock (NewBars) NewBars.Add(e); });
            }

            /// <summary>Seeds a feed with two closed bars, the way a load or a snapshot restore would.</summary>
            public ChartFeed Seed(ChartIdentity id)
            {
                var feed = Hub.GetOrCreateFeed(id);
                feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { Bar(0), Bar(1) }));
                return feed;
            }

            public int Count { get { lock (NewBars) return NewBars.Count; } }

            public void Dispose() { _sub.Dispose(); Manager.Dispose(); Bus.Dispose(); }
        }

        // ── The positive control ─────────────────────────────────────────────
        //
        // Without this, the negative test below would pass just as happily against a harness
        // that never wired the bus up at all.

        [Fact]
        public void AFocusedChart_closingABar_publishes_NewBarEvent()
        {
            using var h = new Harness();
            var focused = h.Seed(Id("BTC/USD"));
            h.Hub.SetFocus(Id("BTC/USD"));
            // The store has to hold the feed's bars before the tick, or the append is the
            // store's FIRST data and prevDataCount is 0 — which the publisher skips by design.
            h.Store.Dispatch(new UpdateDataAction(focused.Bars, IsInitialLoad: true));

            Assert.True(focused.ApplyLiveTick(Bar(2, 105)));

            Assert.Equal(1, h.Count);
        }

        // ── The measurement ──────────────────────────────────────────────────

        [Fact]
        public void ALiveBackgroundChart_closingABar_publishes_NOTHING()
        {
            using var h = new Harness();
            var focused = h.Seed(Id("BTC/USD"));
            h.Hub.SetFocus(Id("BTC/USD"));
            h.Store.Dispatch(new UpdateDataAction(focused.Bars, IsInitialLoad: true));

            // A second chart, live via BackgroundTabFeedService's opt-in, but not focused.
            var background = h.Seed(Id("ETH/USD"));

            Assert.True(background.ApplyLiveTick(Bar(2, 105)),
                "The tick was rejected, so this test would prove nothing about announcement.");
            // The bar really did close on that feed: three bars where there were two.
            Assert.Equal(3, background.Bars.Count);

            Assert.Equal(0, h.Count);
        }

        [Fact]
        public void TheFeedHub_raises_FocusedFeedUpdated_for_the_focused_feed_only()
        {
            // The mechanism behind the test above, pinned directly: this is the line that
            // decides the whole feature's reach.
            using var h = new Harness();
            var seen = new List<string>();
            h.Hub.FocusedFeedUpdated += (f, _) => { lock (seen) seen.Add(f.Identity.Symbol); };

            var focused = h.Seed(Id("BTC/USD"));
            h.Hub.SetFocus(Id("BTC/USD"));
            var background = h.Seed(Id("ETH/USD"));

            Assert.True(focused.ApplyLiveTick(Bar(2, 105)));
            Assert.True(background.ApplyLiveTick(Bar(2, 205)));

            lock (seen)
            {
                Assert.Contains("BTC/USD", seen);
                Assert.DoesNotContain("ETH/USD", seen);
            }
        }
    }
}
