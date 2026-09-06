using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost.Services;
using AccessibleTrader.WebHost.Services.Tray;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// <b>Phase 1 of the background monitor: one long-lived scope, and exactly one delivery owner.</b>
///
/// <para>
/// The feature is "close the browser and keep being told things", and the obstacle was never a
/// missing notifier — it was that every service that could tell you anything is <c>AddScoped</c>,
/// which on Blazor Server means per browser circuit. <see cref="HeadlessSession"/> keeps ONE
/// scope for the process, so the session outlives the browser.
/// </para>
///
/// <para>
/// <b>THE HAZARD, and it is the narration bug of 2026-09-05 inverted.</b> Two subscribers
/// speaking about the same event was one LOST utterance. Two sessions alive in one process is
/// the mirror image: a DOUBLED one. So every delivery test here is written twice — once with a
/// browser circuit open and once with none — and asserts EXACTLY ONE delivery in each. A test
/// that exercised only the browser-closed state would prove nothing about the state that breaks.
/// </para>
/// </summary>
public class HeadlessSessionTests : IDisposable
{
    public HeadlessSessionTests() => CircuitAlertCoverage.ResetForTests();
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    // ── Fakes ────────────────────────────────────────────────────────────────

    /// <summary>Records sound, toast and speech; spawns nothing.</summary>
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

    /// <summary>The seam <see cref="DesktopNotificationService"/> toasts through.</summary>
    private sealed class SpyNotifier : IDesktopNotifier
    {
        public readonly List<(string Title, string Body)> Shown = new();
        public bool IsAvailable => true;
        public string Describe() => "spy notifier";
        public void Notify(string title, string body) => Shown.Add((title, body));
    }

    /// <summary>Counts scopes, because "one scope for the process" is the whole phase.</summary>
    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceScopeFactory _inner;
        public int Created;
        public CountingScopeFactory(IServiceScopeFactory inner) => _inner = inner;
        public IServiceScope CreateScope() { Created++; return _inner.CreateScope(); }
    }

    // ── Harness ──────────────────────────────────────────────────────────────

    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Ohlcv Bar(double close, int hour) =>
        new(T0.AddHours(hour), close, close, close, close, 0);

    private static AlertDefinition PriceAlert(string symbol, double threshold) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = $"{symbol} above {threshold}",
        Target = AlertTarget.Price,
        Condition = AlertCondition.CrossesAbove,
        Threshold = threshold,
        Delivery = AlertDelivery.Both,
        IsActive = true,
        Symbol = symbol,
        Provider = "Bitstamp",
        Timeframe = "1h",
    };

    private sealed class Harness : IDisposable
    {
        public readonly SpyPresenter Presenter = new();
        public readonly SpyNotifier Notifier = new();
        public readonly RecentAlertsBuffer Recent = new();
        public readonly CountingScopeFactory Scopes;
        public readonly HeadlessSession Session;
        public readonly LocalBackgroundMonitor Monitor;
        public readonly List<AlertFiredEvent> PublishedHeadless = new();

        private readonly ServiceProvider _root;

        /// <param name="alerts">The user's saved alert list.</param>
        /// <param name="bars">Close prices for the two bars every fetch returns, oldest first.</param>
        public Harness(IEnumerable<AlertDefinition> alerts, (double Prev, double Last) bars)
        {
            var provider = Substitute.For<IMarketDataProvider>();
            provider.FetchOhlcvAsync(Arg.Any<MarketDataRequest>()).Returns(_ =>
                (new List<Ohlcv> { Bar(bars.Prev, 0), Bar(bars.Last, 1) },
                 new List<(long, double)>()));

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(provider);

            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(_ => alerts.ToList());

            var settings = Substitute.For<ISettingsManager>();
            // The monitor's own opt-in, ON.
            settings.GetSetting(LocalBackgroundMonitor.SettingKey).Returns(JToken.FromObject(true));
            // And the desktop ALERT toast switch, also ON — deliberately. If the headless
            // DesktopNotificationService owned the Alerts category it would toast here, so
            // asserting Notifier.Shown is empty proves the CATEGORY MASK and not merely a
            // settings default. A test with this switch off would pass either way.
            settings.GetSetting(SettingsKeys.DesktopNotifyAlerts).Returns(JToken.FromObject(true));

            var services = new ServiceCollection();
            services.AddScoped<IEventBus, EventBus>();
            services.AddScoped<IWorkspaceStore>(_ => new MockWorkspaceStore());
            services.AddSingleton(settings);
            services.AddSingleton(library);
            services.AddSingleton(data);
            services.AddSingleton(Substitute.For<IPluginLoaderService>());
            services.AddSingleton<IDesktopNotifier>(Notifier);
            services.AddScoped<AlertDeliveryService>();

            _root = services.BuildServiceProvider();
            Scopes = new CountingScopeFactory(_root.GetRequiredService<IServiceScopeFactory>());
            Session = new HeadlessSession(Scopes, NullLogger<HeadlessSession>.Instance);
            Monitor = new LocalBackgroundMonitor(
                Session, new DemoPolicy(isDemo: false), Recent, new AlertSnooze(),
                Presenter, NullLogger<LocalBackgroundMonitor>.Instance);
        }

        /// <summary>Subscribe to the long-lived session's bus the way a headless subscriber does.</summary>
        public void WatchHeadlessBus() =>
            Session.Get<IEventBus>().Subscribe<AlertFiredEvent>(PublishedHeadless.Add);

        public Task PollAsync() => Monitor.PollOnceAsync(CancellationToken.None);

        public void Dispose() { Session.Dispose(); _root.Dispose(); }
    }

    /// <summary>Pretend a browser circuit is open with these symbols on screen.</summary>
    private static IDisposable OpenCircuit(string id, params string[] symbols) =>
        CircuitAlertCoverage.Register(id, () => symbols);

    // ── The routing rule, pure ───────────────────────────────────────────────

    [Fact]
    public void With_no_circuit_open_every_watch_is_ours()
    {
        // The browser-closed case is the behaviour that already shipped, and the one that
        // must not regress while the pause is being replaced.
        var watches = LocalBackgroundMonitor.DeriveWatches(
            new[] { PriceAlert("BTC/USD", 100), PriceAlert("ETH/USD", 10) });

        var owned = LocalBackgroundMonitor.OwnedWatches(watches, CircuitAlertCoverage.CoveredSymbols());

        Assert.Equal(2, owned.Count);
    }

    [Fact]
    public void A_symbol_an_open_circuit_is_watching_is_not_ours()
    {
        var watches = LocalBackgroundMonitor.DeriveWatches(
            new[] { PriceAlert("BTC/USD", 100), PriceAlert("ETH/USD", 10) });

        using var _ = OpenCircuit("c1", "btc/usd");   // case differs on purpose

        var owned = LocalBackgroundMonitor.OwnedWatches(watches, CircuitAlertCoverage.CoveredSymbols());

        // ETH is ours. Before Phase 1 the whole poll returned early and ETH was watched by
        // NOBODY: the in-session pipeline gates alerts to the on-screen chart, so closing the
        // browser made more of the user's alerts work than leaving it open.
        Assert.Equal("ETH/USD", Assert.Single(owned).Symbol);
    }

    [Fact]
    public void Coverage_is_forgotten_when_the_circuit_closes()
    {
        var registration = OpenCircuit("c1", "BTC/USD");
        Assert.Contains("BTC/USD", CircuitAlertCoverage.CoveredSymbols());

        registration.Dispose();

        // A registration that outlived its circuit would leave the symbol permanently
        // "covered" by a browser that is not there — silent non-coverage, which is the exact
        // failure this feature exists to prevent.
        Assert.Empty(CircuitAlertCoverage.CoveredSymbols());
        Assert.Equal(0, CircuitAlertCoverage.SourceCount);
    }

    [Fact]
    public void A_circuit_whose_scope_is_disposing_covers_nothing_rather_than_throwing()
    {
        using var _ = CircuitAlertCoverage.Register("dying",
            () => throw new ObjectDisposedException("scope"));
        using var __ = OpenCircuit("healthy", "ETH/USD");

        // Failing towards "the headless side takes it" risks a duplicate; failing the other
        // way loses the alert outright. One of those two is recoverable.
        Assert.Equal(new[] { "ETH/USD" }, CircuitAlertCoverage.CoveredSymbols().ToArray());
    }

    // ── The scope really is long-lived ───────────────────────────────────────

    [Fact]
    public async Task Two_polls_share_one_scope_and_therefore_one_event_bus()
    {
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101));

        var busBefore = h.Session.Get<IEventBus>();
        await h.PollAsync();
        await h.PollAsync();

        // One scope for the process, not one per poll. This is the whole phase: a subscription
        // taken inside the scope has to outlive a 60-second tick to be worth anything.
        Assert.Equal(1, h.Scopes.Created);
        Assert.Same(busBefore, h.Session.Get<IEventBus>());
    }

    [Fact]
    public void The_scope_is_not_created_until_something_asks_for_it()
    {
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101));

        Assert.False(h.Session.IsStarted);
        Assert.Equal(0, h.Scopes.Created);

        _ = h.Session.Services;

        Assert.True(h.Session.IsStarted);
        Assert.Equal(1, h.Scopes.Created);
    }

    [Fact]
    public void A_disposed_session_refuses_to_hand_out_services()
    {
        var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101));
        _ = h.Session.Services;
        h.Session.Dispose();

        // Handing out a provider from a disposed scope would fail later, somewhere else, in a
        // background thread — say so here instead.
        Assert.Throws<ObjectDisposedException>(() => h.Session.Services);
        h.Dispose();
    }

    // ── THE HAZARD: exactly one delivery, in both states ─────────────────────

    [Fact]
    public async Task With_the_browser_closed_a_fired_alert_is_delivered_exactly_once()
    {
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101));
        h.WatchHeadlessBus();

        await h.PollAsync();

        Assert.Single(h.Presenter.Spoken);
        Assert.Single(h.Presenter.Toasts);
        Assert.Equal(1, h.Presenter.SoundsPlayed);

        // Published once on the long-lived bus, so the ordinary in-session subscribers — the
        // email / Telegram / webhook fan-out, the journal — see a background alert for the
        // first time.
        Assert.Single(h.PublishedHeadless);

        // And NOT toasted a second time by the headless DesktopNotificationService. Its
        // Alerts category is masked off precisely so this cannot happen, and the alert toast
        // switch is ON in this harness so the mask is what is being measured.
        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public async Task With_a_circuit_open_on_that_symbol_the_headless_side_delivers_nothing()
    {
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101));
        h.WatchHeadlessBus();
        using var _ = OpenCircuit("c1", "BTC/USD");

        await h.PollAsync();

        // The circuit's own pipeline is evaluating this symbol. Speaking it here would be the
        // same sentence twice through the same Orca.
        Assert.Empty(h.Presenter.Spoken);
        Assert.Empty(h.Presenter.Toasts);
        Assert.Equal(0, h.Presenter.SoundsPlayed);
        Assert.Empty(h.PublishedHeadless);
        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public async Task With_a_circuit_open_on_another_symbol_ours_is_still_delivered_exactly_once()
    {
        // The case the old whole-process pause got wrong: the browser is open on BTC, and the
        // user's ETH alert was being evaluated by nobody at all.
        using var h = new Harness(
            new[] { PriceAlert("BTC/USD", 100), PriceAlert("ETH/USD", 100) }, (99, 101));
        h.WatchHeadlessBus();
        using var _ = OpenCircuit("c1", "BTC/USD");

        await h.PollAsync();

        var spoken = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("ETH/USD", spoken);
        Assert.Single(h.Presenter.Toasts);
        var published = Assert.Single(h.PublishedHeadless);
        Assert.Equal("ETH/USD", published.Alert.Symbol);
    }

    [Fact]
    public async Task A_background_alert_carries_the_symbol_it_fired_on()
    {
        // AlertEvaluator leaves AlertFired.Symbol null — the in-session pipeline stamps it
        // afterwards from the on-screen chart and this monitor never did, so every background
        // alert reached the tray's recent list with no symbol on it, and would now reach
        // per-asset webhook routing the same way.
        using var h = new Harness(new[] { PriceAlert("ETH/USD", 100) }, (99, 101));
        h.WatchHeadlessBus();

        await h.PollAsync();

        Assert.Equal("ETH/USD", Assert.Single(h.PublishedHeadless).Alert.Symbol);
        Assert.Equal("ETH/USD", Assert.Single(h.Recent.Snapshot()).Symbol);
    }

    [Fact]
    public async Task A_crossing_is_delivered_once_across_repeated_polls()
    {
        // The monitor re-fetches the same two bars every 60 seconds for the whole timeframe.
        // The persistent evaluator is what stops that becoming 59 announcements — and moving
        // to a long-lived scope must not have moved the evaluator with it.
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101));

        for (int poll = 0; poll < 5; poll++) await h.PollAsync();

        Assert.Single(h.Presenter.Spoken);
    }

    [Fact]
    public async Task The_monitors_own_opt_in_still_gates_everything()
    {
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101));
        h.Session.Get<ISettingsManager>().GetSetting(LocalBackgroundMonitor.SettingKey)
            .Returns((JToken?)null);   // the shipped default: off
        h.WatchHeadlessBus();

        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
        Assert.Empty(h.PublishedHeadless);
    }
}
