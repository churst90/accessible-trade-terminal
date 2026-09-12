using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.DependencyInjection;
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

            /// <summary>The real narration stack, when a test wants the ladder (Q3, 2026-09-11).
            /// Null by default, which is how a head that does not register the factory behaves —
            /// the two-clause sentence alone.</summary>
            public HeadlessChartFactory? Charts;
            private ServiceProvider? _root;

            public Harness(bool withNarration = false)
            {
                if (withNarration) Charts = BuildNarrationStack(out _root);
                Hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(),
                    Substitute.For<IDataService>(), new DemoPolicy(isDemo: false),
                    NullLoggerFactory.Instance);
                Store.State.Returns(_ => WorkspaceState.Initial);
                TabFeeds.LiveBackgroundFeeds.Returns(_ => _liveTabs.ToList());
                Announcer = new BackgroundBarAnnouncer(Hub, TabFeeds, Store, Bus, Settings,
                    Earcons, Speech, NullLogger<BackgroundBarAnnouncer>.Instance, Charts);
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

            /// <summary>The indicator stack the narration ladder needs, as the WebHost registers
            /// it — so what a test observes here is what the shipped announcer does.</summary>
            private static HeadlessChartFactory BuildNarrationStack(
                out ServiceProvider root)
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddScoped<IIndicatorProvider, CoreIndicatorProvider>();
                services.AddScoped<IIndicatorService, IndicatorService>();
                services.AddScoped<ICustomIndicatorRegistry, CustomIndicatorRegistry>();
                services.AddScoped<IIndicatorEngine, IndicatorEngine>();
                services.AddScoped<IIndicatorStateMapper, IndicatorStateMapper>();
                services.AddScoped<IComponentRoleMapper, ComponentRoleMapper>();
                services.AddScoped<ISonificationProfileProvider, SonificationProfileProvider>();
                services.AddScoped<IPaneAssignmentService, PaneAssignmentService>();
                services.AddScoped<IStylingService, StylingService>();
                services.AddScoped<IIndicatorPreferencesService, AccessibleTrader.Tests.Mocks.MockIndicatorPreferencesService>();
                services.AddScoped<IIndicatorModelFactory, IndicatorModelFactory>();
                services.AddScoped<AccessibleTrader.Sdk.Analysis.IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();
                services.AddScoped<HeadlessChartFactory>();
                root = services.BuildServiceProvider();
                return root.GetRequiredService<HeadlessChartFactory>();
            }

            /// <summary>Put a tab in the workspace snapshot list with the series the user has on
            /// it — the source the ladder reads, and the same one the background monitors use.</summary>
            public void SnapshotTab(ChartIdentity id, params SeriesConfig[] series)
            {
                var s0 = WorkspaceState.Initial;
                var snap = new TabSnapshot(
                    TabIndex: 0,
                    Identity: id,
                    Data: s0.Data,
                    ActiveSeries: series.Select(c => new ChartSeries { Config = c }).ToImmutableList(),
                    FocusedSeriesIndex: s0.FocusedSeriesIndex,
                    FocusedSeriesId: s0.FocusedSeriesId,
                    FocusedComponentIndex: s0.FocusedComponentIndex,
                    FocusedBinIndex: s0.FocusedBinIndex,
                    CurrentDataIndex: s0.CurrentDataIndex,
                    ViewportStartIndex: s0.ViewportStartIndex,
                    ViewportLength: s0.ViewportLength,
                    RightMarginBars: s0.RightMarginBars,
                    ViewportRange: s0.ViewportRange,
                    PaneRanges: s0.PaneRanges,
                    IsHeikinAshi: false,
                    IsLogScale: false,
                    LastInteractionContext: s0.LastInteractionContext,
                    PaneHeightRatios: s0.PaneHeightRatios,
                    InitStatus: InitializationStatus.Ready,
                    DataStatus: DataStatus.Ready,
                    IsCoordinateEntryMode: false,
                    PendingDrawingTool: null,
                    CoordinateEntryAnchorCount: 0,
                    CoordinateEntryAnchor1Index: -1,
                    SymbolDisplayName: id.Symbol,
                    CurrentDataShape: ProviderDataShape.Ohlcv,
                    PrimarySeriesId: CoreSeriesIds.Volume);
                Store.State.Returns(_ => WorkspaceState.Initial with
                {
                    TabSnapshots = ImmutableList.Create(snap),
                });
            }

            public void Dispose() { Announcer.Dispose(); Bus.Dispose(); _root?.Dispose(); }
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

        // ── Q3: the full narration ladder for a background tab (Cody, 2026-09-11) ──

        /// <summary>
        /// <b>Closing the browser used to tell you MORE than leaving it open.</b> Headless, every
        /// saved tab got the bar close PLUS the full narration ladder; in-session, a background
        /// tab got a two-clause sentence and nothing else. Cody: <i>"giving the narration for
        /// other tabs would be useful to have the full ladder."</i>
        /// </summary>
        [Fact]
        public async Task ABackgroundTabsBarClose_carriesItsNarrationLadder()
        {
            using var h = new Harness(withNarration: true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.SnapshotTab(eth, NarratedVolume());
            var feed = h.OpenBackgroundTab(eth);
            h.SpeechOptIn(true);

            // TWO closes. The first is a first sighting: the narrator seeds and says nothing,
            // exactly as it does with the browser closed — a scan with no previous value to
            // compare against has no news, and inventing some would be the worse failure.
            Assert.True(feed.ApplyLiveTick(VolumeBar(2, 1_000)));
            await Settle(h, atLeast: 1);
            Assert.True(feed.ApplyLiveTick(VolumeBar(3, 5_000)));
            await Settle(h, atLeast: 2);

            var published = h.Published[1];
            Assert.False(string.IsNullOrWhiteSpace(published.Narration),
                "a tab with a volume pane flagged with N must carry a ladder on its second close");
            Assert.Contains("Volume", published.Narration!, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The spoken sentence is ONE utterance — the close and the ladder together,
        /// as in-session on the focused chart and as headless with no browser at all.</summary>
        [Fact]
        public async Task TheLadderRidesTheSpokenSentence_asOneUtterance()
        {
            using var h = new Harness(withNarration: true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.SnapshotTab(eth, NarratedVolume());
            var feed = h.OpenBackgroundTab(eth);
            h.SpeechOptIn(true);

            Assert.True(feed.ApplyLiveTick(VolumeBar(2, 1_000)));
            await Settle(h, atLeast: 1);
            Assert.True(feed.ApplyLiveTick(VolumeBar(3, 5_000)));
            await Settle(h, atLeast: 2);

            var spoken = h.Speech.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(ISpeechFeedbackRouter.Speak))
                .Select(c => c.GetArguments()[0] as string ?? "")
                .Where(t => t.Length > 0)
                .ToList();

            // Two closes, two utterances — and the SECOND carries the close and the ladder
            // together, as one sentence rather than two.
            Assert.Equal(2, spoken.Count);
            string one = spoken[1];
            Assert.Contains("ETH/USD", one, StringComparison.Ordinal);
            Assert.Contains("close ", one, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Volume", one, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The other half of the pair. A tab with NOTHING flagged with N gets the two-clause
        /// sentence and no ladder — a test that only showed the ladder would pass against an
        /// announcer that appended something to everything.
        /// </summary>
        [Fact]
        public async Task ATabWithNothingUnderN_getsNoLadder()
        {
            using var h = new Harness(withNarration: true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.SnapshotTab(eth, NarratedVolume(narrated: false));
            var feed = h.OpenBackgroundTab(eth);

            Assert.True(feed.ApplyLiveTick(VolumeBar(2, 1_000)));
            await Settle(h, atLeast: 1);
            Assert.True(feed.ApplyLiveTick(VolumeBar(3, 5_000)));
            await Settle(h, atLeast: 2);

            Assert.All(h.Published, e => Assert.True(string.IsNullOrWhiteSpace(e.Narration)));
        }

        /// <summary>A bar whose VOLUME moves, so a narrated volume pane has something to report.</summary>
        private static Ohlcv VolumeBar(int hours, double volume) =>
            new(T0.AddHours(hours), 100, 101, 99, 100, volume);

        /// <summary>The ladder is computed off the feed thread, so the assertions have to wait
        /// for it. Bounded, and it fails on the assertion rather than on a timeout.</summary>
        private static async Task Settle(Harness h, int atLeast)
        {
            for (int i = 0; i < 300 && h.Count < atLeast; i++) await Task.Delay(10);
        }

        private static SeriesConfig NarratedVolume(bool narrated = true)
        {
            var cfg = new SeriesConfig
            {
                Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume",
                IndicatorCode = "VOLUME", Pane = "Volume", IsAutoNarrated = narrated, IsVisible = true,
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
                Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
            });
            return cfg;
        }
    }
}
