using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Phase 3 D3 — bars closing on a chart you have open but are not looking at.</b>
    ///
    /// <para>The defect these close is in `NewBarReachTests`: the entire new-bar feature reached
    /// one chart, the focused one, while `BackgroundTabFeedService` deliberately keeps up to
    /// eight non-focused tabs LIVE. Their bars closed in silence.</para>
    ///
    /// <para>Cody's decision, 2026-09-08: <b>toast and earcon by default, speech opt-in.</b> So
    /// the speech tests are written as a PAIR — switch off and switch on — because a test that
    /// only ever exercises the default proves nothing about the branch the setting controls, and
    /// a bare NSubstitute `ISettingsManager` returns the default for everything.</para>
    /// </summary>
    public class BackgroundBarAnnouncerTests
    {
        private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        private static Ohlcv Bar(int hours, double close = 100) =>
            new(T0.AddHours(hours), close, close + 1, close - 1, close, 10);
        private static ChartIdentity Id(string symbol, string tf = "1h") =>
            new("Spot", "TestProv", symbol, tf);

        private sealed class Harness : IDisposable
        {
            public readonly MarketFeedHub Hub;
            public readonly EventBus Bus = new();
            public readonly IBackgroundTabFeedService TabFeeds = Substitute.For<IBackgroundTabFeedService>();
            public readonly ISettingsManager Settings = Substitute.For<ISettingsManager>();
            public readonly IEarconService Earcons = Substitute.For<IEarconService>();
            public readonly ISpeechFeedbackRouter Speech = Substitute.For<ISpeechFeedbackRouter>();
            public readonly IWorkspaceStore Store = Substitute.For<IWorkspaceStore>();
            public readonly BackgroundBarAnnouncer Announcer;
            public readonly List<BackgroundBarClosedEvent> Published = new();

            private readonly List<ChartIdentity> _liveTabs = new();

            public Harness()
            {
                Hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(),
                    Substitute.For<IDataService>(), new DemoPolicy(isDemo: false),
                    NullLoggerFactory.Instance);
                Store.State.Returns(_ => WorkspaceState.Initial);
                TabFeeds.LiveBackgroundFeeds.Returns(_ => _liveTabs.ToList());
                Announcer = new BackgroundBarAnnouncer(Hub, TabFeeds, Store, Bus, Settings,
                    Earcons, Speech, NullLogger<BackgroundBarAnnouncer>.Instance);
                Bus.Subscribe<BackgroundBarClosedEvent>(e => { lock (Published) Published.Add(e); });
            }

            /// <summary>Marks an identity as a live background TAB, which is the eligibility rule.</summary>
            public ChartFeed OpenBackgroundTab(ChartIdentity id)
            {
                _liveTabs.Add(id);
                var feed = Hub.GetOrCreateFeed(id);
                feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { Bar(0), Bar(1) }));
                return feed;
            }

            public void SpeechOptIn(bool on) =>
                Settings.GetSetting(SettingsKeys.SpeakBackgroundTabBars).Returns(JToken.FromObject(on));

            public int Count { get { lock (Published) return Published.Count; } }

            public void Dispose() { Announcer.Dispose(); Bus.Dispose(); }
        }

        // ── The defect, closed ───────────────────────────────────────────────

        [Fact]
        public void ABarClosingOnALiveBackgroundTab_isAnnounced_andNamesItsOwnSymbol()
        {
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));            // the user is looking at BTC
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(2, 205)));

            var e = Assert.Single(h.Published);
            // The symbol is the whole point: this is by definition about a chart the user is not
            // looking at, so an announcement that did not name it would be unattributable.
            Assert.Equal("ETH/USD", e.Identity.Symbol);
            Assert.Equal(Bar(1).Date, e.ClosedBar.Date);
            Assert.Equal(Bar(2, 205).Date, e.NewBar.Date);
            h.Earcons.Received(1).PlayNewBar();
        }

        [Fact]
        public void TheFocusedChart_isNotAnnouncedTwice()
        {
            // The doubling hazard, third phase running. The focused feed already reaches the user
            // through NewBarEvent; this route must not also speak for it.
            using var h = new Harness();
            var btc = h.OpenBackgroundTab(Id("BTC/USD"));
            h.Hub.SetFocus(Id("BTC/USD"));            // now it IS the focused chart

            Assert.True(btc.ApplyLiveTick(Bar(2, 105)));

            Assert.Equal(0, h.Count);
            h.Earcons.DidNotReceive().PlayNewBar();
        }

        // ── Eligibility is TAB membership, not liveness ──────────────────────

        [Fact]
        public void AFeedThatIsNotAnOpenTab_isNotAnnounced()
        {
            // The hub also holds leased feeds belonging to monitors, evaluators and split views.
            // Those are not charts the user opened and have no business announcing anything.
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));
            var leased = h.Hub.GetOrCreateFeed(Id("XMR/USD"));   // never added to LiveBackgroundFeeds
            leased.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { Bar(0), Bar(1) }));

            Assert.True(leased.ApplyLiveTick(Bar(2, 305)));

            Assert.Equal(0, h.Count);
        }

        [Fact]
        public void AnIntraBarTick_isNotABarClose()
        {
            // LiveReplace is the forming bar being refreshed in place. Announcing it would be a
            // toast per tick — the failure mode the whole category defaults off to avoid.
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(1, 199)));   // same timestamp as the last bar

            Assert.Equal(0, h.Count);
        }

        [Fact]
        public void WhilePlaybackIsRunning_nothingIsAnnounced()
        {
            using var h = new Harness();
            h.Store.State.Returns(_ => WorkspaceState.Initial with { IsPlaying = true });
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(2, 205)));

            Assert.Equal(0, h.Count);
        }

        // ── Speech is OPT-IN, and the pair proves the switch is read ─────────

        [Fact]
        public void ByDefault_theBarCloseIsNotSPOKEN()
        {
            using var h = new Harness();
            h.SpeechOptIn(false);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(2, 205)));

            h.Speech.DidNotReceive().Speak(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<SpeechChannel>());
            // But it DID reach the user the other two ways — otherwise this test would pass
            // against an announcer that had simply stopped working.
            Assert.Equal(1, h.Count);
            h.Earcons.Received(1).PlayNewBar();
        }

        [Fact]
        public void WithTheOptInOn_theBarCloseIsSpoken_onTheEventChannel_withoutInterrupting()
        {
            using var h = new Harness();
            h.SpeechOptIn(true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(2, 205)));

            h.Speech.Received(1).Speak(
                Arg.Is<string>(s => s.Contains("ETH/USD")),
                interrupt: false,
                channel: SpeechChannel.Event);
        }

        // ── Wording ──────────────────────────────────────────────────────────

        [Fact]
        public void TheSentence_leadsWithTheSymbolAndTimeframe()
        {
            string s = BackgroundBarAnnouncer.BackgroundSentence(
                Id("ETH/USD", "1h"), Bar(1, 200), Bar(2, 205));

            Assert.StartsWith("ETH/USD 1h: close", s);
            Assert.Contains("New bar: open", s);
        }

        [Fact]
        public void ADailyBar_isStampedWithADate_notAClockTime()
        {
            // The same clock rule the focused announcement follows: every daily bar would
            // otherwise read "at 00:00".
            string s = BackgroundBarAnnouncer.BackgroundSentence(
                Id("ETH/USD", "1d"), Bar(24, 200), Bar(48, 205));

            Assert.Contains(" on ", s);
            Assert.DoesNotContain(" at ", s);
        }

        [Fact]
        public void TheToastTitleAndBody_matchTheFocusedChartsWording()
        {
            // One vocabulary for both routes, by construction rather than by two people
            // remembering to. If the focused wording changes, this goes red.
            Assert.Equal("ETH/USD 1h: bar closed",
                DesktopNotificationService.NewBarTitle("ETH/USD", "1h"));

            // The body carries the close and the bar's own clock, hourly bar -> a time of day.
            string body = DesktopNotificationService.NewBarBody(3600, Bar(1, 200));
            Assert.StartsWith("Close ", body);
            Assert.Contains(" at ", body);
            Assert.EndsWith(".", body);

            // And the daily rule, which is the half that regressed on the focused route before.
            Assert.Contains(" on ", DesktopNotificationService.NewBarBody(86400, Bar(24, 200)));
        }
    }
}
