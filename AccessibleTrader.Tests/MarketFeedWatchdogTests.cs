using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Feeds;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The multi-live (background/keyed) watchdog: MarketFeedHub now detects a feed
    /// that has gone silent, announces it once, restarts a bounded number of times,
    /// then gives up — the safety net LiveStreamManager has for the focused feed.
    /// It also surfaces the provider's ErrorStream for keyed sockets. Without this a
    /// blind trader's background monitor could die silently.
    /// </summary>
    public class MarketFeedWatchdogTests
    {
        private static ChartIdentity Id(string provider = "MultiProv", string symbol = "BTC/USD", string tf = "1h") =>
            new("Spot", provider, symbol, tf);

        // Minimal multi-live provider with an ErrorStream we can push through.
        private sealed class WatchdogProvider : BaseMarketDataProvider
        {
            public int SubscribeCount;
            public Action<Ohlcv>? LastOnBar;

            public override string Name => "MultiProv";
            public override string Description => "";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => true;
            public override bool SupportsMultipleLiveSubscriptions => true;
            public override ProviderEnvironment Environment => ProviderEnvironment.Live;
            public override int MaxBarsPerRequest => 100;
            public override List<string> NativelySupportedTimeframes => new() { "1h" };
            public override void Configure(Dictionary<string, string> config) { }
            public override Task EnsureConnectedAsync() => Task.CompletedTask;
            public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
            public override Task DisconnectAsync() => Task.CompletedTask;
            public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string>());
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));

            public override Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            {
                SubscribeCount++;
                LastOnBar = onBar;
                return Task.FromResult<IAsyncDisposable>(new Handle());
            }

            public void PushError(string message) => _errorStream.OnNext(message);

            private sealed class Handle : IAsyncDisposable
            {
                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }

        private static (MarketFeedHub Hub, WatchdogProvider Provider, IGlobalErrorCoordinator Err) Build()
        {
            var provider = new WatchdogProvider();
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync("MultiProv").Returns(Task.FromResult<IMarketDataProvider?>(provider));
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var hub = new MarketFeedHub(Substitute.For<IDataOrchestrator>(), data,
                new DemoPolicy(false), NullLoggerFactory.Instance, err);
            return (hub, provider, err);
        }

        // ── Pure decision logic ──────────────────────────────────────────────

        [Fact]
        public void EvaluateSilence_walks_none_quiet_restart_giveup()
        {
            var (hub, _, _) = Build();
            using (hub)
            {
                // Defaults: quiet 90s, restart 240s, max 3.
                Assert.Equal(MarketFeedHub.WatchdogAction.None,          hub.EvaluateSilence(10_000, false, 0, false));
                Assert.Equal(MarketFeedHub.WatchdogAction.AnnounceQuiet, hub.EvaluateSilence(100_000, false, 0, false));
                Assert.Equal(MarketFeedHub.WatchdogAction.None,          hub.EvaluateSilence(100_000, true, 0, false)); // already announced
                Assert.Equal(MarketFeedHub.WatchdogAction.Restart,       hub.EvaluateSilence(300_000, true, 0, false));
                Assert.Equal(MarketFeedHub.WatchdogAction.GiveUp,        hub.EvaluateSilence(300_000, true, 3, false));
                Assert.Equal(MarketFeedHub.WatchdogAction.None,          hub.EvaluateSilence(300_000, true, 3, true));  // already gave up
            }
        }

        // ── Sweep behaviour ──────────────────────────────────────────────────

        [Fact]
        public async Task Silent_feed_is_announced_once_and_a_tick_resets_it()
        {
            var (hub, provider, err) = Build();
            using (hub)
            {
                Assert.Equal(FeedLiveStart.Started, await hub.TryStartFeedLiveAsync(Id()));
                hub.BackdateFeedForTest(Id(), 120_000); // 2 min silent (> 90s quiet, < 240s restart)

                await hub.RunWatchdogSweepAsync();
                await hub.RunWatchdogSweepAsync(); // second sweep must NOT re-announce

                err.Received(1).ReportError(
                    Arg.Is<string>(m => m.Contains("gone quiet") && m.Contains("BTC/USD")),
                    ErrorSeverity.Low, ErrorCategory.Informational);

                // A real tick resets the clock: no further quiet announcement.
                provider.LastOnBar!(new Ohlcv(DateTime.UtcNow, 1, 1, 1, 1, 1));
                err.ClearReceivedCalls();
                await hub.RunWatchdogSweepAsync();
                err.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<ErrorSeverity>(), Arg.Any<ErrorCategory>());
            }
        }

        [Fact]
        public async Task Extended_silence_restarts_the_subscription_then_gives_up()
        {
            var (hub, provider, err) = Build();
            using (hub)
            {
                await hub.TryStartFeedLiveAsync(Id());
                Assert.Equal(1, provider.SubscribeCount);

                // Each sweep at >restart-threshold resubscribes, up to MaxRestarts (3).
                for (int i = 0; i < 3; i++)
                {
                    hub.BackdateFeedForTest(Id(), 300_000);
                    await hub.RunWatchdogSweepAsync();
                }
                Assert.Equal(4, provider.SubscribeCount); // original + 3 restarts

                // The 4th silent sweep exhausts the budget → give up (no more resubscribes).
                hub.BackdateFeedForTest(Id(), 300_000);
                await hub.RunWatchdogSweepAsync();
                Assert.Equal(4, provider.SubscribeCount);
                err.Received().ReportError(
                    Arg.Is<string>(m => m.Contains("stopped updating")),
                    ErrorSeverity.Medium, ErrorCategory.Provider);
            }
        }

        [Fact]
        public async Task Provider_error_stream_is_surfaced_for_keyed_feeds()
        {
            var (hub, provider, err) = Build();
            using (hub)
            {
                await hub.TryStartFeedLiveAsync(Id());
                provider.PushError("keyed socket dropped");

                err.Received().ReportError("keyed socket dropped", ErrorSeverity.Medium, ErrorCategory.Provider);
            }
        }

        [Fact]
        public async Task Stopping_a_feed_removes_it_from_the_watchdog()
        {
            var (hub, _, err) = Build();
            using (hub)
            {
                await hub.TryStartFeedLiveAsync(Id());
                await hub.StopFeedLiveAsync(Id());

                hub.BackdateFeedForTest(Id(), 300_000); // no-op — feed is gone
                await hub.RunWatchdogSweepAsync();

                err.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<ErrorSeverity>(), Arg.Any<ErrorCategory>());
            }
        }
    }
}
