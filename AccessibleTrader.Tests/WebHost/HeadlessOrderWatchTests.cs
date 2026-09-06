using System.Reactive.Subjects;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.WebHost.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>Phase 2 of the background monitor: order fills with the browser closed.</b>
///
/// <para>
/// Phase 1's hazard repeats here in a sharper form. A provider plugin is a SINGLETON, so the
/// stream the headless session hooks is the same object a browser circuit's order service
/// hooks; each publishes onto its own bus and both would announce the same fill. So, exactly
/// as in <see cref="HeadlessSessionTests"/>, <b>every delivery assertion in this file is
/// written twice</b> — with a circuit covering the venue and with none — and asserts exactly
/// one delivery in each. A test that exercised only the browser-closed state would prove
/// nothing about the state that breaks.
/// </para>
///
/// <para>
/// And the routing is per VENUE, not per process, because that is the mistake Phase 1 had to
/// undo one domain over: "a browser is open" is not the same claim as "that fill will be
/// announced".
/// </para>
/// </summary>
public class HeadlessOrderWatchTests : IDisposable
{
    public HeadlessOrderWatchTests() => CircuitOrderCoverage.ResetForTests();
    public void Dispose() => CircuitOrderCoverage.ResetForTests();

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class SpyPresenter : IDesktopAlertPresenter
    {
        public readonly List<(string Title, string Text, bool Urgent)> Toasts = new();
        public readonly List<string> Spoken = new();
        public int SoundsPlayed;

        public string Describe() => "spy";
        public string DescribeToast() => "spy toast";
        public bool CanNotify => true;
        public void PlayNotificationSound() => SoundsPlayed++;
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text, urgent));
        public void Speak(string text) => Spoken.Add(text);
    }

    private static readonly DateTime T0 = new(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc);

    private static OrderUpdate Update(
        OrderStatus status = OrderStatus.Filled,
        bool stop = false, bool takeProfit = false, bool trailing = false,
        double filled = 1, double remaining = 0, string? reason = null) =>
        new(OrderId: "o-1", Symbol: "BTC/USD", Side: OrderSide.Buy,
            FilledQuantity: filled, FilledPrice: 100, RemainingQuantity: remaining,
            Status: status, StopTriggered: stop, TakeProfitTriggered: takeProfit,
            Timestamp: T0, RealizedPnL: null, Trailing: trailing, Reason: reason);

    /// <summary>Pretend a browser circuit is open and announcing these venues' fills.</summary>
    private static IDisposable OpenCircuit(string id, params string[] providers) =>
        CircuitOrderCoverage.Register(id, () => providers);

    private static (EventBus Bus, SpyPresenter Presenter, HeadlessOrderAnnouncer Announcer) Announcing()
    {
        var bus = new EventBus();
        var presenter = new SpyPresenter();
        // The REAL coverage registry, not an injected predicate: the routing rule and the thing
        // the circuit handler registers into have to be the same object or the test proves only
        // that a lambda works.
        var announcer = new HeadlessOrderAnnouncer(
            bus, presenter, NullLogger<HeadlessOrderAnnouncer>.Instance);
        return (bus, presenter, announcer);
    }

    // ── The routing registry, pure ───────────────────────────────────────────

    [Fact]
    public void With_no_circuit_open_no_venue_is_covered()
    {
        // The browser-closed case: the headless session owns every fill. This is the behaviour
        // the whole feature exists for and the one that must never regress.
        Assert.Empty(CircuitOrderCoverage.CoveredProviders());
        Assert.False(CircuitOrderCoverage.IsCovered("Binance"));
    }

    [Fact]
    public void A_venue_an_open_circuit_has_hooked_is_covered_case_insensitively()
    {
        using var _ = OpenCircuit("c1", "binance");

        Assert.True(CircuitOrderCoverage.IsCovered("Binance"));
        Assert.True(CircuitOrderCoverage.IsCovered(" Binance "));
    }

    [Fact]
    public void A_venue_the_open_circuit_did_NOT_hook_is_still_ours()
    {
        // The Phase 1 lesson, applied before it can bite again: the circuit covers the venues
        // its own order service actually subscribed, not "everything, because a browser is
        // open". Standing down on the second claim is how an alert on an off-screen symbol
        // came to be watched by nobody.
        using var _ = OpenCircuit("c1", "Binance");

        Assert.False(CircuitOrderCoverage.IsCovered("Kraken"));
    }

    [Fact]
    public void Coverage_is_forgotten_when_the_circuit_closes()
    {
        var registration = OpenCircuit("c1", "Binance");
        Assert.True(CircuitOrderCoverage.IsCovered("Binance"));

        registration.Dispose();

        Assert.False(CircuitOrderCoverage.IsCovered("Binance"));
        Assert.Equal(0, CircuitOrderCoverage.SourceCount);
    }

    [Fact]
    public void A_circuit_whose_scope_is_disposing_covers_nothing_rather_than_throwing()
    {
        using var _ = CircuitOrderCoverage.Register("dying", () => throw new ObjectDisposedException("scope"));
        using var __ = OpenCircuit("healthy", "Kraken");

        Assert.Equal(new[] { "Kraken" }, CircuitOrderCoverage.CoveredProviders().ToArray());
    }

    [Fact]
    public void An_unattributed_event_is_never_treated_as_covered()
    {
        using var _ = OpenCircuit("c1", "Binance");

        // A fill that cannot say where it came from routes to the headless side deliberately.
        // The alternative is to guess somebody else has it, and that guess is silence.
        Assert.False(CircuitOrderCoverage.IsCovered(null));
        Assert.False(CircuitOrderCoverage.IsCovered("   "));
    }

    // ── Delivery: exactly one owner, in BOTH states ──────────────────────────

    [Fact]
    public void With_the_browser_closed_a_fill_is_sounded_toasted_and_spoken_exactly_once()
    {
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;

        bus.Publish(new OrderFilledEvent(Update(), "Binance"));

        Assert.Equal(1, presenter.SoundsPlayed);
        Assert.Equal("Order filled", Assert.Single(presenter.Toasts).Title);
        Assert.Equal("Order filled. Bought 1 BTC/USD at 100.00.", Assert.Single(presenter.Spoken));
    }

    [Fact]
    public void With_a_circuit_announcing_that_venue_the_headless_side_says_nothing()
    {
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;
        using var __ = OpenCircuit("c1", "Binance");

        bus.Publish(new OrderFilledEvent(Update(), "Binance"));

        // The circuit speaks it through the browser with an earcon; saying it again through
        // spd-say is the doubling this rule exists to prevent.
        Assert.Equal(0, presenter.SoundsPlayed);
        Assert.Empty(presenter.Toasts);
        Assert.Empty(presenter.Spoken);
    }

    [Fact]
    public void A_fill_on_a_venue_the_open_circuit_never_hooked_is_still_announced()
    {
        // The state the per-process rule would get wrong: a browser IS open, and it is not
        // announcing this venue. Standing down here is silent non-coverage.
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;
        using var __ = OpenCircuit("c1", "Binance");

        bus.Publish(new OrderFilledEvent(Update(), "Kraken"));

        Assert.Equal(1, presenter.SoundsPlayed);
        Assert.Single(presenter.Spoken);
    }

    [Fact]
    public void An_unattributed_fill_is_announced_rather_than_dropped()
    {
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;
        using var __ = OpenCircuit("c1", "Binance");

        bus.Publish(new OrderFilledEvent(Update()));   // no provider

        // Of the two ways to be wrong, a possible duplicate is recoverable and silence is not.
        Assert.Single(presenter.Spoken);
    }

    [Fact]
    public void A_PAPER_fill_is_the_circuits_whenever_a_circuit_is_open()
    {
        // The paper broker's stream is subscribed for the order service's whole LIFETIME rather
        // than hooked on demand, so it never appears in LiveOrderStreamProviders — and the paper
        // ACCOUNT is shared through PaperAccountHub, so a circuit's service and the headless one
        // are on the same subject. A circuit therefore reports "Paper" as covered explicitly; if
        // it did not, the browser would speak the fill and spd-say would say it again.
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;
        using var __ = OpenCircuit("c1", GeneralOrderService.PaperProviderName);

        bus.Publish(new OrderFilledEvent(Update(), GeneralOrderService.PaperProviderName));

        Assert.Empty(presenter.Spoken);
    }

    [Fact]
    public void A_PAPER_fill_with_no_browser_open_is_still_announced()
    {
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;

        bus.Publish(new OrderFilledEvent(Update(), GeneralOrderService.PaperProviderName));

        Assert.Single(presenter.Spoken);
    }

    [Theory]
    // The wording is the in-session pipeline's, word for word: what the user hears at 03:00
    // and what they hear at their desk must be the same sentence, or they have to reconcile
    // two descriptions of one event.
    [InlineData("stop", "Stop loss hit", "Stop loss hit. Bought 1 BTC/USD at 100.00.")]
    [InlineData("trailing-stop", "Trailing stop hit", "Trailing stop hit. Bought 1 BTC/USD at 100.00.")]
    [InlineData("take-profit", "Take profit hit", "Take profit hit. Bought 1 BTC/USD at 100.00.")]
    [InlineData("partial", "Partial fill", "Partial fill. Bought 1 BTC/USD at 100.00. 2 remaining.")]
    [InlineData("cancelled", "Order cancelled", "Order cancelled for BTC/USD.")]
    [InlineData("expired", "Order expired", "Order expired for BTC/USD.")]
    [InlineData("replaced", "Order replaced", "Order replaced for BTC/USD. It is still working under a new order id.")]
    [InlineData("rejected", "Order rejected", "Order rejected for BTC/USD. Insufficient balance.")]
    public void Every_order_outcome_is_announced_in_the_in_session_wording(
        string kind, string expectedTitle, string expectedSpeech)
    {
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;

        object evt = kind switch
        {
            "stop" => new StopHitEvent(Update(stop: true), "Binance"),
            "trailing-stop" => new StopHitEvent(Update(stop: true, trailing: true), "Binance"),
            "take-profit" => new TakeProfitHitEvent(Update(takeProfit: true), "Binance"),
            "partial" => new OrderPartialFillEvent(Update(OrderStatus.PartialFill, remaining: 2), "Binance"),
            "cancelled" => new OrderCancelledEvent(Update(OrderStatus.Cancelled, filled: 0), "Binance"),
            "expired" => new OrderExpiredEvent(Update(OrderStatus.Expired, filled: 0), "Binance"),
            "replaced" => new OrderReplacedEvent(Update(OrderStatus.Replaced, filled: 0), "Binance"),
            "rejected" => new OrderRejectedEvent(Update(OrderStatus.Rejected, filled: 0), "Insufficient balance", "Binance"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        PublishDynamic(bus, evt);

        Assert.Equal(expectedTitle, Assert.Single(presenter.Toasts).Title);
        Assert.Equal(expectedSpeech, Assert.Single(presenter.Spoken));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("take-profit")]
    [InlineData("partial")]
    [InlineData("cancelled")]
    [InlineData("expired")]
    [InlineData("replaced")]
    [InlineData("rejected")]
    public void Every_order_outcome_is_silent_when_the_circuit_owns_that_venue(string kind)
    {
        // The other half of the pair. Announcing the eight events but only proving the
        // suppression for the first one would leave seven routes that can double.
        var (bus, presenter, announcer) = Announcing();
        using var _ = announcer;
        using var __ = OpenCircuit("c1", "Binance");

        object evt = kind switch
        {
            "stop" => new StopHitEvent(Update(stop: true), "Binance"),
            "take-profit" => new TakeProfitHitEvent(Update(takeProfit: true), "Binance"),
            "partial" => new OrderPartialFillEvent(Update(OrderStatus.PartialFill, remaining: 2), "Binance"),
            "cancelled" => new OrderCancelledEvent(Update(OrderStatus.Cancelled, filled: 0), "Binance"),
            "expired" => new OrderExpiredEvent(Update(OrderStatus.Expired, filled: 0), "Binance"),
            "replaced" => new OrderReplacedEvent(Update(OrderStatus.Replaced, filled: 0), "Binance"),
            "rejected" => new OrderRejectedEvent(Update(OrderStatus.Rejected, filled: 0), "no", "Binance"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        PublishDynamic(bus, evt);

        Assert.Empty(presenter.Spoken);
        Assert.Empty(presenter.Toasts);
    }

    [Fact]
    public void The_toast_body_never_repeats_the_title()
    {
        // The toast's title IS the prefix, so a body that repeats it reads "Order filled /
        // Order filled. Bought 1 …" in the notification centre.
        Assert.Equal("Bought 1 BTC/USD at 100.00.",
            HeadlessOrderAnnouncer.ToastBody("Order filled", "Order filled. Bought 1 BTC/USD at 100.00."));

        // A sentence whose lead is not the title is shown whole rather than mangled.
        Assert.Equal("Order cancelled for BTC/USD.",
            HeadlessOrderAnnouncer.ToastBody("Order cancelled", "Order cancelled for BTC/USD."));
    }

    [Fact]
    public void A_presenter_that_throws_cannot_take_down_the_bus()
    {
        var bus = new EventBus();
        var reached = false;
        using var _ = bus.Subscribe<OrderFilledEvent>(_ => reached = true);
        using var __ = new HeadlessOrderAnnouncer(bus, new ThrowingPresenter(),
            NullLogger<HeadlessOrderAnnouncer>.Instance);

        bus.Publish(new OrderFilledEvent(Update(), "Binance"));

        Assert.True(reached, "A broken delivery channel must never cost every other subscriber the event.");
    }

    private sealed class ThrowingPresenter : IDesktopAlertPresenter
    {
        public string Describe() => "throwing";
        public string DescribeToast() => "throwing";
        public bool CanNotify => true;
        public void PlayNotificationSound() => throw new InvalidOperationException("no audio device");
        public void Notify(string title, string text, bool urgent) => throw new InvalidOperationException();
        public void Speak(string text) => throw new InvalidOperationException();
    }

    /// <summary>Sound and toast are broken; speech is not.</summary>
    private sealed class HalfBrokenPresenter : IDesktopAlertPresenter
    {
        public readonly List<string> Spoken = new();
        public string Describe() => "half";
        public string DescribeToast() => "half";
        public bool CanNotify => true;
        public void PlayNotificationSound() => throw new InvalidOperationException("no audio device");
        public void Notify(string title, string text, bool urgent) => throw new InvalidOperationException("no daemon");
        public void Speak(string text) => Spoken.Add(text);
    }

    [Fact]
    public void A_machine_with_no_audio_and_no_toast_daemon_still_SPEAKS_the_fill()
    {
        // The three channels are three attempts, not one. Speech is the one a blind user
        // actually depends on, and it must not be lost to a missing `paplay`.
        var bus = new EventBus();
        var presenter = new HalfBrokenPresenter();
        using var _ = new HeadlessOrderAnnouncer(bus, presenter, NullLogger<HeadlessOrderAnnouncer>.Instance);

        bus.Publish(new OrderFilledEvent(Update(), "Binance"));

        Assert.Equal("Order filled. Bought 1 BTC/USD at 100.00.", Assert.Single(presenter.Spoken));
    }

    private static void PublishDynamic(IEventBus bus, object evt) =>
        typeof(IEventBus).GetMethod(nameof(IEventBus.Publish))!
            .MakeGenericMethod(evt.GetType())
            .Invoke(bus, new[] { evt });

    // ── The category mask: one toast, not two ────────────────────────────────

    private sealed class SpyNotifier : Core.Services.Notifications.IDesktopNotifier
    {
        public readonly List<(string Title, string Body)> Shown = new();
        public bool IsAvailable => true;
        public string Describe() => "spy notifier";
        public void Notify(string title, string body) => Shown.Add((title, body));
    }

    /// <summary>
    /// The headless <c>DesktopNotificationService</c> must NOT own the OrderFills category.
    ///
    /// <para>
    /// It cannot ask <see cref="CircuitOrderCoverage"/> anything, so with a browser open it
    /// would toast a fill the circuit was already announcing — and with none it would raise a
    /// SECOND toast beside the announcer's. Same shape as Phase 1's Alerts mask, and the
    /// harness turns <c>notifications.desktop.orderFills</c> ON deliberately: with the switch
    /// off this assertion passes whether the mask works or not, which is a test of the default
    /// rather than of the routing.
    /// </para>
    /// </summary>
    [Fact]
    public void A_headless_fill_raises_ONE_toast_even_with_the_desktop_fill_switch_on()
    {
        var presenter = new SpyPresenter();
        var notifier = new SpyNotifier();

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.DesktopNotifyOrderFills).Returns(JToken.FromObject(true));

        var services = new ServiceCollection();
        services.AddScoped<IEventBus, EventBus>();
        services.AddScoped<IWorkspaceStore>(_ => new Mocks.MockWorkspaceStore());
        services.AddSingleton(settings);
        services.AddSingleton<Core.Services.Notifications.IDesktopNotifier>(notifier);
        services.AddSingleton<IDesktopAlertPresenter>(presenter);

        using var root = services.BuildServiceProvider();
        using var session = new HeadlessSession(
            root.GetRequiredService<IServiceScopeFactory>(), NullLogger<HeadlessSession>.Instance);

        session.Get<IEventBus>().Publish(new OrderFilledEvent(Update(), "Binance"));

        Assert.Single(presenter.Toasts);
        Assert.Empty(notifier.Shown);
    }

    [Fact]
    public void With_a_circuit_owning_the_venue_a_headless_fill_raises_NO_toast_at_all()
    {
        // The other half of the pair. If the headless DesktopNotificationService still owned
        // OrderFills this would show a toast for a fill the browser is announcing.
        var presenter = new SpyPresenter();
        var notifier = new SpyNotifier();
        using var _ = OpenCircuit("c1", "Binance");

        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.DesktopNotifyOrderFills).Returns(JToken.FromObject(true));

        var services = new ServiceCollection();
        services.AddScoped<IEventBus, EventBus>();
        services.AddScoped<IWorkspaceStore>(__ => new Mocks.MockWorkspaceStore());
        services.AddSingleton(settings);
        services.AddSingleton<Core.Services.Notifications.IDesktopNotifier>(notifier);
        services.AddSingleton<IDesktopAlertPresenter>(presenter);

        using var root = services.BuildServiceProvider();
        using var session = new HeadlessSession(
            root.GetRequiredService<IServiceScopeFactory>(), NullLogger<HeadlessSession>.Instance);

        session.Get<IEventBus>().Publish(new OrderFilledEvent(Update(), "Binance"));

        Assert.Empty(presenter.Toasts);
        Assert.Empty(notifier.Shown);
    }

    // ── Two loops, one scope, one non-thread-safe IDataService ───────────────

    /// <summary>
    /// Records the highest number of callers ever inside <c>InitializeAsync</c> at once.
    /// A substitute rather than a hand-written <c>IDataService</c>: the interface has two dozen
    /// members and only one of them is the subject.
    /// </summary>
    private sealed class ReentrancyProbe
    {
        private int _inside;
        private readonly object _gate = new();
        public int MaxConcurrent;
        public int Calls;

        public IDataService Build()
        {
            var data = Substitute.For<IDataService>();
            data.InitializeAsync(Arg.Any<IPluginLoaderService>()).Returns(async _ => await EnterAsync());
            return data;
        }

        public async Task EnterAsync()
        {
            int now = Interlocked.Increment(ref _inside);
            lock (_gate) MaxConcurrent = Math.Max(MaxConcurrent, now);
            Interlocked.Increment(ref Calls);
            // The real one loads plugin DLLs off disk between its entry and the line that sets
            // _isInitialized; anything that yields reproduces the window.
            await Task.Yield();
            await Task.Delay(5);
            Interlocked.Decrement(ref _inside);
        }
    }

    /// <summary>
    /// Phase 2 puts a SECOND <c>BackgroundService</c> on the same 60-second tick as the alert
    /// monitor, against the same scope and therefore the same scoped <c>IDataService</c>. That
    /// service's <c>InitializeAsync</c> guards on a plain bool set at the END of the method and
    /// appends to plain <c>List&lt;T&gt;</c>s in between, so two loops starting together would
    /// both enter it and mutate those lists concurrently. Hence one gate.
    /// </summary>
    [Fact]
    public async Task The_shared_data_preamble_admits_one_caller_at_a_time()
    {
        var probe = new ReentrancyProbe();
        var services = new ServiceCollection();
        services.AddScoped<IEventBus, EventBus>();
        services.AddSingleton(probe.Build());
        services.AddSingleton(Substitute.For<IPluginLoaderService>());

        using var root = services.BuildServiceProvider();
        using var session = new HeadlessSession(
            root.GetRequiredService<IServiceScopeFactory>(), NullLogger<HeadlessSession>.Instance);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => session.EnsureDataReadyAsync())));

        Assert.Equal(8, probe.Calls);
        Assert.Equal(1, probe.MaxConcurrent);
    }

    [Fact]
    public async Task The_probe_itself_would_catch_an_ungated_race()
    {
        // The vacuity check for the test above: the same probe, driven WITHOUT the gate, must
        // actually observe re-entry — otherwise "MaxConcurrent == 1" would pass for a gate that
        // does nothing at all.
        var probe = new ReentrancyProbe();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => probe.EnterAsync())));

        Assert.True(probe.MaxConcurrent > 1,
            "the probe never saw two callers at once, so the gated assertion proves nothing");
    }

    // ── The watch loop ───────────────────────────────────────────────────────

    private sealed class WatchHarness : IDisposable
    {
        public readonly SpyPresenter Presenter = new();
        public readonly HeadlessSession Session;
        public readonly HeadlessOrderWatch Watch;
        public readonly IOrderExecutionService Orders = Substitute.For<IOrderExecutionService>();
        public readonly List<string> Subscribed = new();
        public readonly ITradingProvider Trading;

        private readonly ServiceProvider _root;
        private readonly HashSet<string> _live = new(StringComparer.OrdinalIgnoreCase);

        public WatchHarness(
            bool optIn = true,
            IEnumerable<ApiKeyConfig>? keys = null,
            List<OpenOrder>? openOrders = null,
            List<Position>? positions = null,
            Exception? accountFailure = null,
            bool subscribeSucceeds = true)
        {
            var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
            Trading = (ITradingProvider)tpSub;
            if (accountFailure != null)
            {
                Trading.GetOpenOrdersAsync().Returns<Task<List<OpenOrder>>>(_ => throw accountFailure);
                Trading.GetPositionsAsync().Returns<Task<List<Position>>>(_ => throw accountFailure);
            }
            else
            {
                Trading.GetOpenOrdersAsync().Returns(openOrders ?? new List<OpenOrder>());
                Trading.GetPositionsAsync().Returns(positions ?? new List<Position>());
            }

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tpSub));

            var apiKeys = Substitute.For<IApiKeyService>();
            var keyList = (keys ?? new[] { ActiveKey("Binance") }).ToList();
            apiKeys.GetAllKeysAsync().Returns(_ => Task.FromResult(keyList));

            Orders.LiveOrderStreamProviders.Returns(_ => _live.ToArray());
            Orders.SubscribeOrderUpdatesAsync(Arg.Any<string>()).Returns(ci =>
            {
                var name = ci.Arg<string>();
                Subscribed.Add(name);
                if (subscribeSucceeds) _live.Add(name);
                return Task.CompletedTask;
            });

            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(LocalBackgroundMonitor.SettingKey).Returns(JToken.FromObject(optIn));

            var services = new ServiceCollection();
            services.AddScoped<IEventBus, EventBus>();
            services.AddSingleton(settings);
            services.AddSingleton(data);
            services.AddSingleton(apiKeys);
            services.AddSingleton(Orders);
            services.AddSingleton(Substitute.For<IPluginLoaderService>());

            _root = services.BuildServiceProvider();
            Session = new HeadlessSession(
                _root.GetRequiredService<IServiceScopeFactory>(), NullLogger<HeadlessSession>.Instance);
            Watch = new HeadlessOrderWatch(
                Session, new DemoPolicy(isDemo: false), Presenter, NullLogger<HeadlessOrderWatch>.Instance);
        }

        /// <summary>Pretend the venue's stream is already hooked.</summary>
        public void MarkLive(string provider) => _live.Add(provider);

        public Task PollAsync() => Watch.PollOnceAsync(CancellationToken.None);

        public void Dispose() { Session.Dispose(); _root.Dispose(); }
    }

    private static ApiKeyConfig ActiveKey(string provider, bool withdrawal = false) =>
        new(Provider: provider, Nickname: $"{provider}-key", ApiKey: "k", ApiSecret: "s",
            IsActive: true, AllowsWithdrawal: withdrawal);

    private static List<OpenOrder> OneOpenOrder() => new()
    {
        new OpenOrder("o-9", "BTC/USD", OrderSide.Buy, OrderType.StopMarket, 1, 95, "working"),
    };

    [Fact]
    public async Task Without_the_opt_in_no_venue_is_asked_anything()
    {
        // The switch is not decoration: with it off this loop must spend no API call, on an
        // account whose owner never asked to be watched.
        using var h = new WatchHarness(optIn: false, openOrders: OneOpenOrder());

        await h.PollAsync();

        Assert.Empty(h.Subscribed);
        await h.Trading.DidNotReceive().GetOpenOrdersAsync();
    }

    [Fact]
    public async Task A_venue_with_an_open_order_is_watched()
    {
        using var h = new WatchHarness(openOrders: OneOpenOrder());

        await h.PollAsync();

        Assert.Equal(new[] { "Binance" }, h.Subscribed.ToArray());
    }

    [Fact]
    public async Task A_venue_with_an_open_position_is_watched()
    {
        using var h = new WatchHarness(positions: new List<Position>
        {
            new("BTC/USD", 1, 100, 100, 0),
        });

        await h.PollAsync();

        Assert.Equal(new[] { "Binance" }, h.Subscribed.ToArray());
    }

    [Fact]
    public async Task A_venue_with_nothing_at_stake_is_not_watched_and_is_not_called_dead()
    {
        // Holding an authenticated socket open all night for an empty account spends the
        // user's rate limit to learn nothing — but an empty account is not a FAILURE, and
        // announcing one as a dead feed teaches the user to ignore the warning that matters.
        using var h = new WatchHarness();

        for (int i = 0; i < 5; i++) await h.PollAsync();

        Assert.Empty(h.Subscribed);
        Assert.Empty(h.Presenter.Spoken);
    }

    [Fact]
    public async Task A_withdrawal_profile_is_never_used_to_watch_orders()
    {
        using var h = new WatchHarness(
            keys: new[] { ActiveKey("Binance", withdrawal: true) }, openOrders: OneOpenOrder());

        await h.PollAsync();

        Assert.Empty(h.Subscribed);
    }

    [Fact]
    public async Task An_already_watched_venue_is_not_re_subscribed_and_costs_no_account_call()
    {
        using var h = new WatchHarness(openOrders: OneOpenOrder());
        h.MarkLive("Binance");

        await h.PollAsync();
        await h.PollAsync();

        Assert.Empty(h.Subscribed);
        await h.Trading.DidNotReceive().GetOpenOrdersAsync();
    }

    [Fact]
    public async Task A_stream_that_dropped_overnight_is_re_established_on_the_next_poll()
    {
        // The 03:00 case, from the watch's side: the order service forgets a dead stream, so
        // the venue is simply "not hooked" here and this loop hooks it again — at POLL cadence,
        // never in a tight reconnect loop, because hammering a venue that is refusing is how a
        // key gets rate-limited.
        using var h = new WatchHarness(openOrders: OneOpenOrder());

        await h.PollAsync();
        Assert.Single(h.Subscribed);

        h.Orders.LiveOrderStreamProviders.Returns(Array.Empty<string>());   // the socket died
        await h.PollAsync();

        Assert.Equal(2, h.Subscribed.Count);
    }

    [Fact]
    public async Task A_venue_that_cannot_be_read_escalates_after_three_polls_exactly_once()
    {
        using var h = new WatchHarness(accountFailure: new HttpRequestException("401 key expired"));

        await h.PollAsync();
        await h.PollAsync();
        Assert.Empty(h.Presenter.Spoken);   // one blip is not an outage

        await h.PollAsync();
        var said = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("Order monitoring stopped for Binance", said);
        Assert.Contains("are not being watched", said);
        Assert.Equal("Order monitoring", Assert.Single(h.Presenter.Toasts).Title);
        Assert.True(h.Presenter.Toasts[0].Urgent, "The watch reporting that it has stopped watching is urgent.");

        // Said once, not once a minute — a warning that repeats forever trains the reader to
        // ignore it.
        await h.PollAsync();
        await h.PollAsync();
        Assert.Single(h.Presenter.Spoken);
    }

    [Fact]
    public async Task A_venue_whose_stream_will_not_establish_escalates_too()
    {
        // ASSERT THE ARTIFACT, NOT THE INCANTATION: SubscribeOrderUpdatesAsync returning
        // without throwing is not coverage. A provider that is not a trading provider, or
        // whose stream was already dead, leaves the set unchanged — and the user must be told
        // rather than left believing they are watched.
        using var h = new WatchHarness(openOrders: OneOpenOrder(), subscribeSucceeds: false);

        await h.PollAsync();
        await h.PollAsync();
        await h.PollAsync();

        Assert.Contains("Order monitoring stopped for Binance", Assert.Single(h.Presenter.Spoken));
    }

    [Fact]
    public async Task Recovery_is_announced_because_the_user_has_no_other_way_to_learn_it()
    {
        var failing = true;
        using var h = new WatchHarness(openOrders: OneOpenOrder());
        h.Trading.GetOpenOrdersAsync().Returns(_ =>
            failing ? throw new HttpRequestException("401") : Task.FromResult(OneOpenOrder()));
        h.Trading.GetPositionsAsync().Returns(_ =>
            failing ? throw new HttpRequestException("401") : Task.FromResult(new List<Position>()));

        for (int i = 0; i < 3; i++) await h.PollAsync();
        Assert.Single(h.Presenter.Spoken);

        failing = false;
        await h.PollAsync();

        Assert.Equal(2, h.Presenter.Spoken.Count);
        Assert.Equal("Order monitoring resumed for Binance.", h.Presenter.Spoken[1]);
    }

    [Fact]
    public async Task The_watch_reporting_on_itself_plays_no_notification_sound()
    {
        // The sound is the cue that means MONEY MOVED. A monitor saying it has stopped
        // monitoring is important and is not that.
        using var h = new WatchHarness(accountFailure: new HttpRequestException("401"));

        for (int i = 0; i < 3; i++) await h.PollAsync();

        Assert.NotEmpty(h.Presenter.Spoken);
        Assert.Equal(0, h.Presenter.SoundsPlayed);
    }

    [Fact]
    public async Task A_data_only_provider_is_skipped_silently()
    {
        // FRED, CFTC, Wikipedia — a macroeconomic feed with a stored key has no orders to
        // watch, and reporting a dead order feed for one would train the user to ignore the
        // channel that announces real ones.
        var dataOnly = Substitute.For<IMarketDataProvider>();
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(dataOnly));

        Assert.False(await HeadlessOrderWatch.HasOpenWorkAsync(data, "FRED"));
    }

    [Fact]
    public async Task A_venue_that_cannot_be_resolved_is_UNKNOWN_not_empty()
    {
        // "No open orders" and "I could not reach the exchange" look identical to a caller
        // holding a bool, and treating the second as the first is how an expired key reads as
        // an empty account.
        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns<Task<IMarketDataProvider?>>(
            _ => throw new HttpRequestException("no route to host"));

        Assert.Null(await HeadlessOrderWatch.HasOpenWorkAsync(data, "Binance"));
    }

    [Fact]
    public async Task A_venue_that_refuses_a_null_symbol_order_query_still_answers_from_positions()
    {
        // MEXC spot requires a symbol on the open-orders endpoint. Giving up at the first
        // throw would call a perfectly healthy account unreadable.
        var tpSub = Substitute.For<IMarketDataProvider, ITradingProvider>();
        var tp = (ITradingProvider)tpSub;
        tp.GetOpenOrdersAsync().Returns<Task<List<OpenOrder>>>(_ => throw new InvalidOperationException("symbol required"));
        tp.GetPositionsAsync().Returns(new List<Position> { new("BTC/USD", 1, 100, 100, 0) });

        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(tpSub));

        Assert.True(await HeadlessOrderWatch.HasOpenWorkAsync(data, "MEXC"));
    }
}
