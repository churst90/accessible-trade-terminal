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
            public readonly List<BackgroundBricksFormedEvent> Bricks = new();

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
                Bus.Subscribe<BackgroundBricksFormedEvent>(e => { lock (Bricks) Bricks.Add(e); });
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
                services.AddScoped<IIndicatorProvider, SkenderTrendProvider>();   // the moving averages
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
            public void SnapshotTab(ChartIdentity id, params SeriesConfig[] series) => SnapshotTab(id, false, series);

            public void SnapshotTab(ChartIdentity id, bool heikinAshi, params SeriesConfig[] series)
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
                    IsHeikinAshi: heikinAshi,
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

            /// <summary>The tab drawn as Renko with a frozen box, as choosing Renko leaves it.</summary>
            public void RenkoTab(ChartIdentity id, double box, params SeriesConfig[] series)
            {
                SnapshotTab(id, false, series);
                var snap = Store.State.TabSnapshots![0] with
                {
                    ChartStyle = PriceChartStyle.Renko,
                    Renko = new RenkoSettings(RenkoBoxMode.Fixed, box),
                    BrickBoxSize = box,
                };
                Store.State.Returns(_ => WorkspaceState.Initial with { TabSnapshots = ImmutableList.Create(snap) });
            }

            public int BrickCount { get { lock (Bricks) return Bricks.Count; } }

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
        public void WithTheOptInOn_theBarCloseIsSpoken_onTheNarrationChannel_withoutInterrupting()
        {
            using var h = new Harness();
            h.SpeechOptIn(true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = h.OpenBackgroundTab(Id("ETH/USD"));

            Assert.True(eth.ApplyLiveTick(Bar(2, 205)));

            h.Speech.Received(1).Speak(
                Arg.Is<string>(s => s.Contains("ETH/USD")),
                interrupt: false,
                channel: SpeechChannel.Narration);   // new bars are narration: F2, not Shift+F2
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

        /// <summary>Cody, 2026-11-02: "when a bar closes, I should hear the close and the
        /// indicator narration ladder. Then the open of the newest bar should be read as well."
        /// The ladder trailed the open until then, so what the indicators said about the bar that
        /// closed was heard after the next bar had been named.</summary>
        [Fact]
        public void TheLadder_isSpokenBetweenTheCloseAndTheOpen()
        {
            const string ladder = "Volume: well above average.";
            // The bar that opened says when (Cody, 2026-11-08: "New bar: open [price] at [timestamp]").
            string open = $"New bar: open {SpeechPriceFormatter.FormatPrice(205)} at {SpeechTimeFormatter.FormatTime(Bar(2, 205).Date)}.";

            string s = BackgroundBarAnnouncer.BackgroundSentence(
                Id("ETH/USD", "1h"), Bar(1, 200), Bar(2, 205), ladder);
            Assert.StartsWith("ETH/USD 1h: close ", s);
            Assert.EndsWith(" " + ladder + " " + open, s);

            // The notification body says the same three things in the same order.
            string body = DesktopNotificationService.NewBarBody(3600, Bar(1, 200), Bar(2, 205), ladder);
            Assert.StartsWith("Close ", body);
            Assert.EndsWith(" " + ladder + " " + open, body);

            // And with nothing on the ladder, the close and the open alone.
            Assert.EndsWith(". " + open, BackgroundBarAnnouncer.BackgroundSentence(
                Id("ETH/USD", "1h"), Bar(1, 200), Bar(2, 205), ladder: null));
            Assert.EndsWith(". " + open, DesktopNotificationService.NewBarBody(3600, Bar(1, 200), Bar(2, 205)));
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

            // In the order Cody asked for on 2026-11-02: the close, the ladder, then the open.
            int close = one.IndexOf("close ", StringComparison.Ordinal);
            int ladder = one.IndexOf("Volume", StringComparison.OrdinalIgnoreCase);
            int open = one.IndexOf("New bar: open", StringComparison.Ordinal);
            Assert.True(close >= 0 && close < ladder && ladder < open,
                $"close, ladder, open in that order: {one}");
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

        /// <summary>
        /// A background tab showing Heikin-Ashi narrates indicators computed on Heikin-Ashi
        /// candles, crossed by the Heikin-Ashi close, as the tab itself draws them (Cody,
        /// 2026-10-22: "if ha is on and a bar closes, speak that close"). After a hundred flat
        /// bars at 100, one bar opens at 100, wicks to 300 and closes at 101. On the real bars the
        /// close 101 crosses an EMA 20 that moves to about 100.1. Its Heikin-Ashi close is
        /// (100+300+100+101)/4 = 150.25, which crosses a Heikin-Ashi EMA lifted to 104.79. Before
        /// 2026-10-22 the raw close was held against the Heikin-Ashi EMA (101 under 104.79: no
        /// cross) — a cross of two lines that are not on the same chart.
        /// </summary>
        [Theory]
        [InlineData(false, "at 100.1")]
        [InlineData(true, "at 104.79")]
        public async Task ABackgroundHeikinAshiTab_narratesIndicatorsOnHeikinAshiCandles(bool heikinAshi, string crossedAt)
        {
            using var h = new Harness(withNarration: true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            var ema = new SeriesConfig
            {
                Id = "ema-20", Name = "EMA", FriendlyName = "EMA", IndicatorCode = "Ema", Pane = "Main",
                IsAutoNarrated = true, IsVisible = true,
            };
            ema.Parameters["lookbackPeriods"] = 20;
            h.SnapshotTab(eth, heikinAshi, ema);
            var feed = h.OpenBackgroundTab(eth);
            h.SpeechOptIn(true);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, 100).Select(i => new Ohlcv(T0.AddHours(i), 100, 100, 100, 100, 1)).ToList()));

            // The first close seeds the narrator; the wick bar comes after it.
            var ticks = new[]
            {
                new Ohlcv(T0.AddHours(100), 100, 100, 100, 100, 1),
                new Ohlcv(T0.AddHours(101), 100, 300, 100, 101, 1),
                new Ohlcv(T0.AddHours(102), 101, 101, 101, 101, 1),
                new Ohlcv(T0.AddHours(103), 101, 101, 101, 101, 1),
            };
            for (int i = 0; i < ticks.Length; i++)
            {
                Assert.True(feed.ApplyLiveTick(ticks[i]));
                await Settle(h, atLeast: i + 1);
            }

            string all;
            lock (h.Published) all = string.Join(" | ", h.Published.Select(e => e.Narration ?? ""));
            Assert.True(all.Contains($"crossed above EMA 20 {crossedAt}", StringComparison.Ordinal), $"heikinAshi={heikinAshi}: [{all}]");

            // And the close it says (and toasts) for the wick bar is the drawn one: 101 raw, 150.25 Heikin-Ashi.
            BackgroundBarClosedEvent wick;
            lock (h.Published) wick = h.Published.Single(e => e.ClosedBar.Date == T0.AddHours(101));
            Assert.Equal(heikinAshi ? 150.25 : 101, wick.ClosedBar.Close, 9);
            Assert.Contains($"close {(heikinAshi ? "150.25" : "101.00")}",
                BackgroundBarAnnouncer.BackgroundSentence(eth, wick.ClosedBar, wick.NewBar), StringComparison.Ordinal);
        }

        // ── Renko tabs announce bricks (Cody, 2026-11-19) ────────────────────

        private static Ohlcv At(int hours, double close) => new(T0.AddHours(hours), close, close, close, close, 1);

        /// <summary>
        /// Cody: "yes announce new bricks on renko views". A background Renko tab used to announce
        /// every time bar's close at its real price. Box 10 from 100: the bar closing at 125
        /// completes two up bricks, the one at 131 a third, and the one at 135 none — which is
        /// not news, so nothing at all is chimed, toasted or said for it. No bar close is
        /// announced on the tab, ever.
        /// </summary>
        [Fact]
        public void ABackgroundRenkoTab_announcesTheBricksACloseCompleted_andNothingElse()
        {
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.RenkoTab(eth, box: 10);
            var feed = h.OpenBackgroundTab(eth);
            h.SpeechOptIn(true);

            feed.ApplyLiveTick(At(1, 125));                  // the forming bar moves: no close
            Assert.True(feed.ApplyLiveTick(At(2, 125)));     // bar 1 closes at 125: two bricks
            feed.ApplyLiveTick(At(2, 131));
            Assert.True(feed.ApplyLiveTick(At(3, 131)));     // bar 2 closes at 131: one brick
            feed.ApplyLiveTick(At(3, 135));
            Assert.True(feed.ApplyLiveTick(At(4, 135)));     // bar 3 closes at 135: no brick

            Assert.Empty(h.Published);                       // no time-bar close, on any of them
            Assert.Equal(2, h.BrickCount);
            Assert.Equal("ETH/USD", h.Bricks[0].Identity.Symbol);
            string P(double v) => SpeechPriceFormatter.FormatPrice(v);
            Assert.Equal($"2 new up bricks, {P(100)} to {P(120)}", h.Bricks[0].Summary);
            Assert.Equal($"new up brick, {P(120)} to {P(130)}", h.Bricks[1].Summary);
            h.Earcons.Received(2).PlayNewBar();              // one chime per close that made bricks

            var spoken = h.Speech.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(ISpeechFeedbackRouter.Speak))
                .Select(c => c.GetArguments()[0] as string ?? "").ToList();
            Assert.Equal(new[]
            {
                $"ETH/USD 1h: 2 new up bricks, {P(100)} to {P(120)}.",
                $"ETH/USD 1h: new up brick, {P(120)} to {P(130)}.",
            }, spoken);
        }

        /// <summary>The other half: the same closes on a candle tab are three bar closes and no bricks.</summary>
        [Fact]
        public void TheSameClosesOnACandleTab_areBarCloses()
        {
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.SnapshotTab(eth);
            var feed = h.OpenBackgroundTab(eth);

            Assert.True(feed.ApplyLiveTick(At(2, 125)));
            Assert.True(feed.ApplyLiveTick(At(3, 131)));
            Assert.True(feed.ApplyLiveTick(At(4, 135)));

            Assert.Equal(3, h.Count);
            Assert.Equal(0, h.BrickCount);
        }

        /// <summary>
        /// A brick the forming bar has made is not news until that bar closes: a price that
        /// pokes past the edge and comes back says nothing.
        /// </summary>
        [Fact]
        public void ABrickTheFormingBarMadeAndTookBack_isNeverSaid()
        {
            using var h = new Harness();
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            h.RenkoTab(eth, box: 10);
            var feed = h.OpenBackgroundTab(eth);

            feed.ApplyLiveTick(At(1, 115));    // past the first edge while forming
            feed.ApplyLiveTick(At(1, 104));    // and back
            Assert.True(feed.ApplyLiveTick(At(2, 104)));

            Assert.Equal(0, h.BrickCount);
            Assert.Empty(h.Published);
            h.Earcons.DidNotReceive().PlayNewBar();
        }

        /// <summary>
        /// The ladder on a Renko tab is computed on its bricks and read at the newest brick. Forty
        /// bars step down a box at a time, then the price turns: the up brick 110 to 120 crosses
        /// above an EMA 3 of the brick closes. Before 2026-11-19 the narrator was a candle chart
        /// (the in-session route never told it the tab was Renko), and the browser-closed one read
        /// the real bar count as an index into the brick values.
        /// </summary>
        [Fact]
        public async Task ABackgroundRenkoTabsLadder_isAboutItsBricks()
        {
            using var h = new Harness(withNarration: true);
            h.Hub.SetFocus(Id("BTC/USD"));
            var eth = Id("ETH/USD");
            var ema = new SeriesConfig
            {
                Id = "ema-3", Name = "EMA", FriendlyName = "EMA", IndicatorCode = "Ema", Pane = "Main",
                IsAutoNarrated = true, IsVisible = true,
            };
            ema.Parameters["lookbackPeriods"] = 3;
            h.RenkoTab(eth, 10, ema);
            var feed = h.OpenBackgroundTab(eth);
            h.SpeechOptIn(true);
            feed.RestoreSnapshot(new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, 40).Select(i => At(i, 500 - 10 * i)).ToList()));

            Assert.True(feed.ApplyLiveTick(At(40, 100)));    // bar 39 closes at 110: a down brick; the narrator seeds
            await SettleBricks(h, 1);
            Assert.True(feed.ApplyLiveTick(At(41, 100)));    // bar 40 closes at 100: another
            await SettleBricks(h, 2);
            feed.ApplyLiveTick(At(41, 105));
            Assert.True(feed.ApplyLiveTick(At(42, 105)));    // bar 41 closes at 105: no brick, nothing said
            feed.ApplyLiveTick(At(42, 120));
            Assert.True(feed.ApplyLiveTick(At(43, 120)));    // bar 42 closes at 120: the turn, up 110 to 120
            await SettleBricks(h, 3);

            Assert.Empty(h.Published);
            Assert.Equal(3, h.BrickCount);
            var turn = h.Bricks[2];
            Assert.Equal($"new up brick, {SpeechPriceFormatter.FormatPrice(110)} to {SpeechPriceFormatter.FormatPrice(120)}", turn.Summary);
            Assert.False(string.IsNullOrWhiteSpace(turn.Narration), "the turn crosses the brick EMA");
            // The brick EMA: closes stepping down a box lag it by a box, 110 at the brick at 100,
            // then (120 + 110) / 2 = 115. On the time bars the 105 close in between pulls it to
            // 113.75 — the number a candle narrator would say.
            Assert.Contains("crossed above EMA 3 at 115", turn.Narration!, StringComparison.Ordinal);
        }

        private static async Task SettleBricks(Harness h, int atLeast)
        {
            for (int i = 0; i < 300 && h.BrickCount < atLeast; i++) await Task.Delay(10);
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
