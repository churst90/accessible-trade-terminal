using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Input;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The live order streams are ARMED by application startup — and until 2026-09-06
    /// nothing armed them.</b>
    ///
    /// <para>
    /// This is <see cref="AlertPipelineArmedTests"/> one domain over, and the defect has the
    /// same shape. <c>GeneralOrderService.SubscribeOrderUpdatesAsync</c> was fully written,
    /// idempotent, and covered by four tests in <see cref="GeneralOrderServiceTests"/> — every
    /// one of which called it itself. Its only production caller was the service's own
    /// <c>ConnectionStatusEvent(Connected)</c> subscription, and the ONLY publisher of that
    /// event is <c>DataOrchestrator</c>'s circuit-breaker <c>onReset</c>: a provider that had
    /// failed ten consecutive times and then recovered. So on an ordinary session no live
    /// broker stream was ever hooked, and the only fills that announced were the ones this
    /// terminal placed itself and then polled. <b>A stop-loss triggering overnight on a
    /// resting order said nothing, on every head, in every mode.</b>
    /// </para>
    ///
    /// <para>
    /// The lesson is the file next door's, restated: <b>a method with tests and no production
    /// caller is a feature that does not exist.</b> So nothing here calls
    /// <c>SubscribeOrderUpdatesAsync</c>; startup is driven and a fill is pushed onto a real
    /// provider stream.
    /// </para>
    /// </summary>
    public class OrderStreamArmedTests
    {
        private static OrderUpdate Fill(string id, string symbol = "BTC/USD") => new(
            OrderId: id, Symbol: symbol, Side: OrderSide.Buy,
            FilledQuantity: 1, FilledPrice: 100, RemainingQuantity: 0,
            Status: OrderStatus.Filled, StopTriggered: false, TakeProfitTriggered: false,
            Timestamp: new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc));

        private static ApiKeyConfig Key(string provider, bool active = true, bool withdrawal = false) =>
            new(Provider: provider, Nickname: $"{provider}-key", ApiKey: "k", ApiSecret: "s",
                IsActive: active, AllowsWithdrawal: withdrawal);

        /// <summary>
        /// The composed startup graph, with a REAL <see cref="GeneralOrderService"/> over a
        /// provider whose order stream is a subject this test can push a fill onto. Everything
        /// <c>AppStartupService.InitializeAsync</c> resolves with <c>GetRequiredService</c> is
        /// substituted; the optional ones are left out so the null-tolerant paths are exercised
        /// too.
        /// </summary>
        private static (AppStartupService Startup, Subject<OrderUpdate> Stream, SpyEventBus Bus,
                        GeneralOrderService Orders)
            BuildStartupOver(params ApiKeyConfig[] keys)
        {
            var bus = new SpyEventBus();
            var stream = new Subject<OrderUpdate>();

            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp = (ITradingProvider)tpSub;
            tp.IsConnected.Returns(true);
            tp.SupportsOrderEventStreaming.Returns(true);
            tp.OrderUpdateStream.Returns(stream);

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));

            var apiKeys = Substitute.For<IApiKeyService>();
            apiKeys.GetAllKeysAsync().Returns(_ => Task.FromResult(keys.ToList()));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new Subject<OrderUpdate>());

            var orders = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false),
                new QuickTradeEquity());

            var services = new ServiceCollection();
            services.AddSingleton(data);
            services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
            services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
            services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
            services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
            services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
            services.AddSingleton<IAccessibilityFeedbackCoordinator>(Substitute.For<IAccessibilityFeedbackCoordinator>());
            services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());
            services.AddSingleton<IEventBus>(bus);
            services.AddSingleton<IWorkspaceStore>(new MockWorkspaceStore());
            services.AddSingleton(apiKeys);
            services.AddSingleton<IOrderExecutionService>(orders);

            var provider = services.BuildServiceProvider();
            return (new AppStartupService(provider, NullLogger<AppStartupService>.Instance),
                    stream, bus, orders);
        }

        // ── The arming itself ────────────────────────────────────────────────

        [Fact]
        public async Task Startup_hooks_the_live_order_stream_so_a_resting_orders_fill_announces()
        {
            var (startup, stream, bus, _) = BuildStartupOver(Key("Binance"));

            await startup.InitializeAsync();

            // A fill this terminal never placed — the venue simply pushed it. Before startup
            // armed the stream this went to nobody at all.
            stream.OnNext(Fill("overnight-stop"));

            var fills = bus.Log.OfType<OrderFilledEvent>().ToList();
            Assert.True(fills.Count == 1,
                $"Expected exactly one OrderFilledEvent from the composed startup graph, got {fills.Count}. " +
                "If this is 0, nothing hooked the venue's order stream and a fill on a resting " +
                "order is announced nowhere.");
            Assert.Equal("overnight-stop", fills[0].Order.OrderId);
        }

        [Fact]
        public async Task Startup_tags_the_fill_with_the_venue_it_came_from()
        {
            var (startup, stream, bus, _) = BuildStartupOver(Key("Binance"));

            await startup.InitializeAsync();
            stream.OnNext(Fill("tagged"));

            // OrderUpdate has never carried the venue — it is known only at subscription time.
            // The headless session needs it to decide whether a browser is already announcing
            // this fill (CircuitOrderCoverage), so it now rides on the event.
            Assert.Equal("Binance", bus.Log.OfType<OrderFilledEvent>().Single().Provider);
        }

        [Fact]
        public async Task A_withdrawal_profile_never_becomes_a_session_credential()
        {
            // The whole point of a separate withdrawal profile is that the trading path never
            // reaches for it. "Arming order streams" is a trading path.
            var (startup, stream, bus, orders) = BuildStartupOver(Key("Binance", withdrawal: true));

            await startup.InitializeAsync();

            Assert.Empty(orders.LiveOrderStreamProviders);
            stream.OnNext(Fill("must-not-announce"));
            Assert.Empty(bus.Log.OfType<OrderFilledEvent>());
        }

        [Fact]
        public async Task An_inactive_key_is_not_armed()
        {
            var (startup, _, _, orders) = BuildStartupOver(Key("Binance", active: false));

            await startup.InitializeAsync();

            Assert.Empty(orders.LiveOrderStreamProviders);
        }

        [Fact]
        public async Task Startup_survives_a_venue_whose_stream_cannot_be_hooked()
        {
            // One venue throwing must not cost the others their coverage, and must not take
            // application startup down with it.
            var bus = new SpyEventBus();
            var good = new Subject<OrderUpdate>();

            var badSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)badSub).OrderUpdateStream.Returns(_ => throw new InvalidOperationException("socket refused"));
            var goodSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)goodSub).OrderUpdateStream.Returns(good);

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(ci =>
                Task.FromResult<IMarketDataProvider?>(ci.Arg<string>() == "Broken" ? badSub : goodSub));

            var apiKeys = Substitute.For<IApiKeyService>();
            apiKeys.GetAllKeysAsync().Returns(_ =>
                Task.FromResult(new List<ApiKeyConfig> { Key("Broken"), Key("Binance") }));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new Subject<OrderUpdate>());
            var orders = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());

            var services = new ServiceCollection();
            services.AddSingleton(data);
            services.AddSingleton<IPluginLoaderService>(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IIndicatorService>(Substitute.For<IIndicatorService>());
            services.AddSingleton<IDataOrchestrationService>(Substitute.For<IDataOrchestrationService>());
            services.AddSingleton<IInputRouter>(Substitute.For<IInputRouter>());
            services.AddSingleton<IChartCommandManager>(Substitute.For<IChartCommandManager>());
            services.AddSingleton<IHistoryBufferCoordinator>(Substitute.For<IHistoryBufferCoordinator>());
            services.AddSingleton<IAccessibilityFeedbackCoordinator>(Substitute.For<IAccessibilityFeedbackCoordinator>());
            services.AddSingleton<IWorkspaceInitializer>(Substitute.For<IWorkspaceInitializer>());
            services.AddSingleton<IEventBus>(bus);
            services.AddSingleton<IWorkspaceStore>(new MockWorkspaceStore());
            services.AddSingleton(apiKeys);
            services.AddSingleton<IOrderExecutionService>(orders);

            var startup = new AppStartupService(services.BuildServiceProvider(),
                NullLogger<AppStartupService>.Instance);

            await startup.InitializeAsync();

            good.OnNext(Fill("still-announced"));
            Assert.Equal("still-announced", bus.Log.OfType<OrderFilledEvent>().Single().Order.OrderId);
        }

        // ── The stream that dies at 03:00 ────────────────────────────────────

        /// <summary>
        /// The defect the scope document called for by name: <i>"a websocket that dies at
        /// 03:00 must escalate the way DeadFeedTracker already does — silent non-coverage is
        /// worse than no feature."</i>
        ///
        /// <para>
        /// It could not, because the subscription went into the order service's map and stayed
        /// there whatever happened to it. The idempotency check at the top of
        /// <c>SubscribeOrderUpdatesAsync</c> then refused every re-subscribe attempt for the
        /// rest of the process, so a stream that failed once was dead until restart while the
        /// trader went on believing their fills were watched.
        /// </para>
        /// </summary>
        [Fact]
        public async Task A_failed_stream_forgets_itself_so_the_next_attempt_really_resubscribes()
        {
            var bus = new SpyEventBus();
            var first = new Subject<OrderUpdate>();
            var second = new Subject<OrderUpdate>();
            var current = first;

            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)tpSub).OrderUpdateStream.Returns(_ => current);

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new Subject<OrderUpdate>());
            var orders = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());

            await orders.SubscribeOrderUpdatesAsync("Binance");
            Assert.Equal(new[] { "Binance" }, orders.LiveOrderStreamProviders.ToArray());

            // 03:00. The socket dies.
            first.OnError(new IOException("connection reset"));
            Assert.Empty(orders.LiveOrderStreamProviders);

            // 03:01. The watch's next poll re-establishes it — which is only possible because
            // the dead entry was forgotten.
            current = second;
            await orders.SubscribeOrderUpdatesAsync("Binance");
            Assert.Equal(new[] { "Binance" }, orders.LiveOrderStreamProviders.ToArray());

            second.OnNext(Fill("after-reconnect"));
            Assert.Equal("after-reconnect", bus.Log.OfType<OrderFilledEvent>().Single().Order.OrderId);
        }

        [Fact]
        public async Task A_stream_that_completes_is_forgotten_too()
        {
            // A venue that closes the channel cleanly leaves the user exactly as uncovered as
            // one that faults, so onCompleted takes the same arm as onError.
            var bus = new SpyEventBus();
            var stream = new Subject<OrderUpdate>();
            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)tpSub).OrderUpdateStream.Returns(stream);
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new Subject<OrderUpdate>());
            var orders = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());

            await orders.SubscribeOrderUpdatesAsync("Binance");
            stream.OnCompleted();

            Assert.Empty(orders.LiveOrderStreamProviders);
        }

        [Fact]
        public async Task A_stream_that_was_already_dead_leaves_no_phantom_subscription()
        {
            // The terminal arms fire DURING Subscribe for an already-faulted observable, so the
            // "am I subscribed" flag has to be read after that, under the same lock the arm
            // writes it under. Getting this wrong records coverage that never existed — the
            // exact silent non-coverage the phase is about.
            var bus = new SpyEventBus();
            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)tpSub).OrderUpdateStream.Returns(
                Observable.Throw<OrderUpdate>(new IOException("already down")));
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(new Subject<OrderUpdate>());
            var orders = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());

            await orders.SubscribeOrderUpdatesAsync("Binance");

            Assert.Empty(orders.LiveOrderStreamProviders);
        }

        [Fact]
        public async Task Paper_fills_are_tagged_as_paper_rather_than_left_unattributed()
        {
            // A null provider routes to the headless announcer as "nobody covers this", which
            // is right for an unknown venue and wrong for the paper broker — its fills belong
            // to whoever is trading, and they are already announced in-session.
            var bus = new SpyEventBus();
            var paperStream = new Subject<OrderUpdate>();
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(paperStream);

            var orders = new GeneralOrderService(
                Substitute.For<IDataService>(), Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, bus, paper,
                Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false), new QuickTradeEquity());

            paperStream.OnNext(Fill("paper-1"));
            await Task.CompletedTask;

            Assert.Equal(GeneralOrderService.PaperProviderName,
                bus.Log.OfType<OrderFilledEvent>().Single().Provider);
        }
    }
}
