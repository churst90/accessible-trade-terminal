using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <see cref="LiveStreamManager"/>'s silence watchdog — the safety net for the
    /// FOCUSED feed, the counterpart to <see cref="MarketFeedWatchdogTests"/>'s
    /// coverage of the keyed/background one in <c>MarketFeedHub</c>. It had no tests
    /// at all: not the connected-but-quiet branch, not the reconnect budget, not
    /// <c>AttemptReconnectAsync</c>, and not the consolidator reset that corrupted
    /// the in-progress bar.
    ///
    /// The distinction it exists to draw is the one a blind trader cannot see for
    /// themselves: a chart that stops moving might be a dead socket (reconnect it)
    /// or a sparse feed on a tier with no live data (reconnecting it loops forever).
    /// Both look identical — silence — and only <c>ConnectionState</c> separates them.
    /// </summary>
    public class LiveStreamWatchdogTests
    {
        private const string Market = "Crypto";
        private const string Prov = "WatchProv";
        private const string Symbol = "BTC/USD";
        private const string Timeframe = "1h";

        private static DateTime At(int minute) => new(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc);

        /// <summary>
        /// A provider whose socket the test drives by hand: ticks, connection state,
        /// and whether <c>EnsureConnectedAsync</c> is allowed to succeed.
        /// </summary>
        private sealed class WatchProvider : BaseMarketDataProvider
        {
            public int EnsureConnectedCount;
            public int SetSubscriptionCount;
            public int DisconnectCount;
            public bool FailEnsureConnected;
            private LiveTickStyle _style = LiveTickStyle.TradeDeltas;

            public override string Name => Prov;
            public override string Description => "test";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => true;
            public override ProviderEnvironment Environment => ProviderEnvironment.Live;
            public override int MaxBarsPerRequest => 100;
            public override List<string> NativelySupportedTimeframes => new() { Timeframe };
            public override LiveTickStyle LiveTickStyle => _style;
            public void SetStyle(LiveTickStyle style) => _style = style;
            public override void Configure(Dictionary<string, string> config) { }

            public override Task EnsureConnectedAsync()
            {
                EnsureConnectedCount++;
                if (FailEnsureConnected) throw new InvalidOperationException("socket refused");
                return Task.CompletedTask;
            }

            public override Task SetSubscriptionAsync(string market, string symbol, string timeframe)
            {
                SetSubscriptionCount++;
                return Task.CompletedTask;
            }

            public override Task DisconnectAsync()
            {
                DisconnectCount++;
                return Task.CompletedTask;
            }

            public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string>());
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

            public void PushTick(Ohlcv tick) => _liveStream.OnNext(tick);
            public void PushState(ConnectionState state) => _connectionStateStream.OnNext(state);
        }

        private static (LiveStreamManager Mgr, WatchProvider Provider, IGlobalErrorCoordinator Err) Build()
        {
            var provider = new WatchProvider();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Prov).Returns(Task.FromResult<IMarketDataProvider?>(provider));
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var mgr = new LiveStreamManager(data, err, NullLogger<LiveStreamManager>.Instance);
            return (mgr, provider, err);
        }

        private static Task<LiveStreamManager.WatchdogAction> Sweep(LiveStreamManager mgr) =>
            mgr.RunWatchdogSweepAsync(Market, Prov, Symbol, Timeframe);

        /// <summary>Drains the channel and returns the last bar published, if any.</summary>
        private static Ohlcv? LastBar(LiveStreamManager mgr)
        {
            Ohlcv? last = null;
            while (mgr.LiveStream.TryRead(out var t)) last = t.Bar;
            return last;
        }

        // ── Pure decision logic ──────────────────────────────────────────────

        [Fact]
        public void EvaluateSilence_walks_none_quiet_reconnect_giveup()
        {
            var (mgr, _, _) = Build();
            using (mgr)
            {
                // Default: silence threshold 60s, budget 5 attempts.
                const ConnectionState Up = ConnectionState.Connected;
                const ConnectionState Down = ConnectionState.Disconnected;

                // Inside the threshold, nothing happens whatever the socket says.
                Assert.Equal(LiveStreamManager.WatchdogAction.None, mgr.EvaluateSilence(10_000, Up, false, 0));
                Assert.Equal(LiveStreamManager.WatchdogAction.None, mgr.EvaluateSilence(10_000, Down, false, 0));
                Assert.Equal(LiveStreamManager.WatchdogAction.None, mgr.EvaluateSilence(60_000, Down, false, 0)); // boundary is exclusive

                // Up but quiet: say so once, and NEVER reconnect — this is the branch
                // that stops a sparse/no-live-data tier from being storm-reconnected.
                Assert.Equal(LiveStreamManager.WatchdogAction.AnnounceQuiet, mgr.EvaluateSilence(90_000, Up, false, 0));
                Assert.Equal(LiveStreamManager.WatchdogAction.None, mgr.EvaluateSilence(90_000, Up, true, 0));
                Assert.Equal(LiveStreamManager.WatchdogAction.None, mgr.EvaluateSilence(90_000, Up, true, 4));

                // Down and quiet: reconnect until the budget is spent, then give up —
                // and keep saying GiveUp, because that is what stops the loop.
                Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, mgr.EvaluateSilence(90_000, Down, false, 0));
                Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, mgr.EvaluateSilence(90_000, Down, true, 4));
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, mgr.EvaluateSilence(90_000, Down, false, 5));
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, mgr.EvaluateSilence(90_000, Down, true, 5));

                // Connecting/Reconnecting are not Connected, so they take the
                // reconnect path rather than being mistaken for a healthy socket.
                Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, mgr.EvaluateSilence(90_000, ConnectionState.Connecting, false, 0));
            }
        }

        // ── The connected-but-quiet branch ───────────────────────────────────

        [Fact]
        public async Task Connected_but_quiet_is_announced_once_and_never_reconnects()
        {
            var (mgr, provider, err) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Connected);
                int subscribesAtStart = provider.SetSubscriptionCount;

                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.AnnounceQuiet, await Sweep(mgr));

                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.None, await Sweep(mgr)); // must not repeat

                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("connected but has sent nothing")),
                    ErrorSeverity.Low, ErrorCategory.Informational);

                // The whole point of the branch: a healthy socket is left alone.
                Assert.Equal(subscribesAtStart, provider.SetSubscriptionCount);
                Assert.Equal(0, provider.DisconnectCount);
            }
        }

        [Fact]
        public async Task A_tick_clears_the_quiet_announcement_so_it_can_be_said_again()
        {
            var (mgr, provider, err) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Connected);

                mgr.BackdateLastTickForTest(90_000);
                await Sweep(mgr);

                // A feed that wakes up and goes quiet AGAIN is a new event, and the
                // user needs to hear it again — otherwise the first hour of silence
                // is announced and every later one is not.
                provider.PushTick(new Ohlcv(At(10), 100, 100, 100, 100, 1));
                err.ClearReceivedCalls();

                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.AnnounceQuiet, await Sweep(mgr));
                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("connected but has sent nothing")),
                    ErrorSeverity.Low, ErrorCategory.Informational);
            }
        }

        // ── The reconnect budget ─────────────────────────────────────────────

        [Fact]
        public async Task A_dropped_socket_is_reconnected_and_resubscribed()
        {
            var (mgr, provider, err) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                Assert.Equal(1, provider.SetSubscriptionCount);
                provider.PushState(ConnectionState.Disconnected);

                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, await Sweep(mgr));

                // AttemptReconnectAsync: disconnect, ensure-connect, resubscribe.
                Assert.Equal(1, provider.DisconnectCount);
                Assert.Equal(2, provider.EnsureConnectedCount); // start + reconnect
                Assert.Equal(2, provider.SetSubscriptionCount);

                err.Received().ReportError(
                    Arg.Is<string>(m => m.Contains("Reconnecting (1/5)")),
                    ErrorSeverity.Low, ErrorCategory.Informational);
                err.Received().ReportError(
                    Arg.Is<string>(m => m.Contains("reconnected successfully")),
                    ErrorSeverity.Low, ErrorCategory.Informational);

                // The fresh subscription is live: a tick on it reaches the channel.
                provider.PushTick(new Ohlcv(At(30), 100, 100, 100, 100, 1));
                Assert.NotNull(LastBar(mgr));
            }
        }

        [Fact]
        public async Task The_reconnect_budget_is_bounded_then_it_gives_up_once()
        {
            var (mgr, provider, err) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Disconnected);

                for (int i = 1; i <= 5; i++)
                {
                    mgr.BackdateLastTickForTest(90_000);
                    Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, await Sweep(mgr));
                }
                Assert.Equal(6, provider.SetSubscriptionCount); // original + 5 reconnects

                // Budget spent: give up, and stop touching the socket.
                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, await Sweep(mgr));
                Assert.Equal(6, provider.SetSubscriptionCount);

                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("lost after 5 reconnect attempts")),
                    ErrorSeverity.High, ErrorCategory.Provider);

                // Sweeping again keeps returning GiveUp (that is what breaks the
                // loop) but must not announce the give-up a second time.
                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, await Sweep(mgr));
                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("lost after 5 reconnect attempts")),
                    ErrorSeverity.High, ErrorCategory.Provider);
            }
        }

        [Fact]
        public async Task A_tick_resets_the_attempt_count_so_a_flaky_socket_keeps_its_full_budget()
        {
            var (mgr, provider, _) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Disconnected);

                for (int i = 1; i <= 4; i++)
                {
                    mgr.BackdateLastTickForTest(90_000);
                    await Sweep(mgr);
                }

                // One real tick means the feed recovered. A socket that drops once an
                // hour must not accumulate its way to a permanent give-up.
                provider.PushTick(new Ohlcv(At(30), 100, 100, 100, 100, 1));

                for (int i = 1; i <= 5; i++)
                {
                    mgr.BackdateLastTickForTest(90_000);
                    Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, await Sweep(mgr));
                }
                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, await Sweep(mgr));
            }
        }

        [Fact]
        public async Task A_failing_reconnect_is_swallowed_and_still_spends_an_attempt()
        {
            var (mgr, provider, err) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Disconnected);
                provider.FailEnsureConnected = true;

                // Six sweeps against a socket that refuses to come back: five spend
                // the budget without the throw escaping, the sixth gives up.
                for (int i = 1; i <= 5; i++)
                {
                    mgr.BackdateLastTickForTest(90_000);
                    Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, await Sweep(mgr));
                }
                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.GiveUp, await Sweep(mgr));

                Assert.Equal(1, provider.SetSubscriptionCount); // never got past EnsureConnected
                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("lost after 5 reconnect attempts")),
                    ErrorSeverity.High, ErrorCategory.Provider);
            }
        }

        // ── The mid-bar reconnect ────────────────────────────────────────────

        /// <summary>
        /// The bug this was written for: <c>SubscribeToProvider</c> built a fresh
        /// <c>BarBucketConsolidator</c> on every subscribe, reconnects included. For a
        /// TradeDeltas provider the new bucket starts empty, so the first tick after a
        /// mid-period reconnect became the bar's Open, High/Low collapsed to the
        /// post-reconnect range, and Volume counted only the trades since — and that
        /// partial bar then replaced the correct one on the chart.
        /// </summary>
        [Fact]
        public async Task A_mid_bar_reconnect_preserves_the_in_progress_bar()
        {
            var (mgr, provider, _) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);

                // Build a partial 1h bar: O 100, H 110, L 95, V 8.
                provider.PushTick(new Ohlcv(At(10), 100, 100, 100, 100, 5));
                provider.PushTick(new Ohlcv(At(20), 110, 110, 95, 105, 3));
                var beforeDrop = LastBar(mgr);
                Assert.NotNull(beforeDrop);
                Assert.Equal(100, beforeDrop!.Value.Open);
                Assert.Equal(110, beforeDrop.Value.High);
                Assert.Equal(95, beforeDrop.Value.Low);
                Assert.Equal(8, beforeDrop.Value.Volume);

                // Socket drops at minute 20 and the watchdog reconnects it.
                provider.PushState(ConnectionState.Disconnected);
                mgr.BackdateLastTickForTest(90_000);
                Assert.Equal(LiveStreamManager.WatchdogAction.Reconnect, await Sweep(mgr));

                // A tick at minute 45, still inside the same hour.
                provider.PushTick(new Ohlcv(At(45), 106, 107, 106, 107, 2));

                var after = LastBar(mgr);
                Assert.NotNull(after);
                Assert.Equal(At(0), after!.Value.Date);
                Assert.Equal(100, after.Value.Open);   // NOT 106, the reconnect price
                Assert.Equal(110, after.Value.High);   // NOT 107
                Assert.Equal(95, after.Value.Low);     // NOT 106
                Assert.Equal(107, after.Value.Close);
                Assert.Equal(10, after.Value.Volume);  // NOT 2
            }
        }

        /// <summary>
        /// The other half of the same rule, and the reason the fix is not simply
        /// "never rebuild the consolidator": switching the chart to another timeframe
        /// must start a clean bucket, or the new timeframe's first bar inherits the
        /// old one's Open and volume.
        /// </summary>
        [Fact]
        public async Task Switching_timeframe_does_reset_the_bucket()
        {
            var (mgr, provider, _) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushTick(new Ohlcv(At(10), 100, 100, 100, 100, 5));
                LastBar(mgr);

                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, "15m");
                provider.PushTick(new Ohlcv(At(20), 106, 107, 106, 107, 2));

                var bar = LastBar(mgr);
                Assert.NotNull(bar);
                Assert.Equal(At(15), bar!.Value.Date); // the 15m bucket, not the hour
                Assert.Equal(106, bar.Value.Open);
                Assert.Equal(2, bar.Value.Volume);
            }
        }

        /// <summary>
        /// Anti-vacuity for the pair above: the identity stamped on a post-reconnect
        /// tick must still be the subscription's own. If the reconnect silently
        /// re-stamped ticks — or failed to resubscribe at all — the preservation
        /// assertions would be reading a bar nothing downstream would ever route.
        /// </summary>
        [Fact]
        public async Task A_post_reconnect_tick_keeps_the_subscription_identity()
        {
            var (mgr, provider, _) = Build();
            using (mgr)
            {
                await mgr.StartLiveStreamAsync(Market, Prov, Symbol, Timeframe);
                provider.PushState(ConnectionState.Disconnected);
                mgr.BackdateLastTickForTest(90_000);
                await Sweep(mgr);

                provider.PushTick(new Ohlcv(At(45), 106, 107, 106, 107, 2));

                Assert.True(mgr.LiveStream.TryRead(out var tick));
                Assert.Equal(Prov, tick.Identity.Provider);
                Assert.Equal(Symbol, tick.Identity.Symbol);
                Assert.Equal(Timeframe, tick.Identity.Timeframe);
                Assert.Equal(Market, tick.Identity.Market);
            }
        }
    }
}
