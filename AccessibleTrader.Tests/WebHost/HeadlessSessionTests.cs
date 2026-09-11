using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Core.Services.Workspace;
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
[Collection("CircuitCoverage")]
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
        // A machine with NO notification tool, deliberately: since 2026-09-11 the notification is
        // the one path for the words and direct speech runs only where there is none
        // (DesktopAnnouncement). Every assertion on Spoken below is about "exactly one delivery",
        // which is a fact about owners and not about channels; the spy is the machine on which
        // that delivery arrives as speech. HeadlessNarrationTests covers the other machine.
        public bool CanNotify => false;
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
        private IReadOnlyList<(string Name, DateTime LastWriteUtc)> _sessionSlots = Array.Empty<(string, DateTime)>();
        private WorkspaceConfiguration? _savedSession;

        /// <param name="alerts">The user's saved alert list.</param>
        /// <param name="bars">Close prices for the two bars every fetch returns, oldest first.</param>
        /// <param name="advancingBars">
        /// When true, the clock MOVES: fetch N returns two bars at hours N and N+1, so a bar
        /// genuinely closes between polls. Only the FIRST fetch shows the <c>Prev</c> close —
        /// after that both bars sit at <c>Last</c>, which is what a market that crossed once and
        /// stayed there actually looks like. Feeding the crossing pair on every fetch instead
        /// would be fabricating a fresh crossing per poll, and an alert firing five times for
        /// five real crossings is correct behaviour, not a defect.
        /// The default (false) returns the identical pair every time, which is what the
        /// crossing-dedup tests need. Nothing can claim "N bars closed" against a provider that
        /// keeps handing back the same two timestamps.
        /// </param>
        /// <param name="barsPerFetch">
        /// How many bars the clock jumps between fetches when <paramref name="advancingBars"/>
        /// is set. More than one is a poll that was MISSED — a laptop asleep, a provider down —
        /// and is the only way to test that catching up announces once rather than once per
        /// skipped bar.
        /// </param>
        public Harness(IEnumerable<AlertDefinition> alerts, (double Prev, double Last) bars,
                       bool advancingBars = false, int barsPerFetch = 1)
        {
            int fetches = 0;
            var provider = Substitute.For<IMarketDataProvider>();
            provider.FetchOhlcvAsync(Arg.Any<MarketDataRequest>()).Returns(_ =>
            {
                int shift = advancingBars ? fetches++ * barsPerFetch : 0;
                double prev = shift == 0 ? bars.Prev : bars.Last;
                return (new List<Ohlcv> { Bar(prev, shift), Bar(bars.Last, shift + 1) },
                        new List<(long, double)>());
            });

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(provider);

            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(_ => alerts.ToList());
            // The saved session the monitor reads its bar-close watch list from. Empty unless a
            // test calls OpenTabs — so every pre-Phase-3 test keeps its exact previous behaviour.
            library.GetAllProfilesWithTimes().Returns(_ => _sessionSlots);
            library.LoadProfile(Arg.Any<string>()).Returns(_ => _savedSession);

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

        /// <summary>
        /// Pretend the user had these charts open when they closed the browser. Also turns the
        /// bar-close category ON — the monitor's opt-in is not enough, and a test that left it
        /// off would pass against a monitor that never looked.
        /// </summary>
        public void OpenTabs(params (string Symbol, string Timeframe)[] tabs)
        {
            _savedSession = new WorkspaceConfiguration
            {
                Tabs = tabs.Select(t => new TabConfiguration
                {
                    Market = "Spot", Provider = "Bitstamp", Symbol = t.Symbol, Timeframe = t.Timeframe
                }).ToList()
            };
            _sessionSlots = new[] { (SessionAutosaveService.LastSessionProfileName + "abc", DateTime.UtcNow) };
            Session.Get<ISettingsManager>().GetSetting(SettingsKeys.DesktopNotifyNewBars)
                .Returns(JToken.FromObject(true));
        }

        public void SetBarFloor(string timeframe) =>
            Session.Get<ISettingsManager>().GetSetting(SettingsKeys.HeadlessNewBarMinTimeframe)
                .Returns(JToken.FromObject(timeframe));

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

    // ── Phase 3 groundwork: the new-bar subscriber that nothing can reach ────
    //
    // HeadlessSession force-creates a DesktopNotificationService carrying
    // DesktopNotificationCategories.NewBars. That is a SUBSCRIBER. The only publisher of
    // NewBarEvent anywhere is WorkspaceStore.Dispatch, gated on an UpdateDataAction with
    // IsInitialLoad:false — and the headless monitor never dispatches into a store at all: it
    // fetches three bars straight off the provider and evaluates against WorkspaceState.Initial.
    //
    // So the headless new-bar toast is wired to an event that cannot occur headless. This is the
    // same shape as Phase 2's headline (a method with tests and no production caller), one layer
    // up: a subscriber with a mask, a comment, and no producer. Pinned here so Phase 3 has a
    // red-to-green line to work against rather than a claim.

    // ── Phase 3 D1/D2: bar closes with the browser closed ───────────────────

    [Fact]
    public async Task A_bar_closing_on_a_saved_tab_is_announced_with_no_browser_open()
    {
        // The headline of the phase. advancingBars means the clock really moves between polls.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));

        await h.PollAsync();           // seeds — announces nothing
        Assert.Empty(h.Presenter.Spoken);

        await h.PollAsync();           // a bar has closed since

        Assert.Single(h.Presenter.Spoken);
        Assert.Contains("BTC/USD", h.Presenter.Spoken[0]);
        Assert.Single(h.Presenter.Toasts);
        Assert.Equal(1, h.Presenter.SoundsPlayed);
    }

    [Fact]
    public async Task The_first_sighting_of_a_chart_announces_nothing()
    {
        // Without the seed, starting the terminal announces a bar close on every watched chart
        // at once — bars that closed while it was not running, presented as news.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"), ("ETH/USD", "1h"));

        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
        Assert.Empty(h.Presenter.Toasts);
    }

    [Fact]
    public async Task Each_poll_that_closes_a_bar_announces_exactly_once()
    {
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));

        await h.PollAsync();                                   // seed
        for (int i = 0; i < 5; i++) await h.PollAsync();       // five polls, one bar each

        Assert.Equal(5, h.Presenter.Spoken.Count);
    }

    [Fact]
    public async Task Catching_up_after_MISSED_polls_announces_once_not_once_per_skipped_bar()
    {
        // A laptop asleep, or a provider down for ten minutes: the clock jumps six bars between
        // two polls. The newest bar is the only one still true, and six announcements arriving
        // together would be worse than the silence they follow.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101),
                                  advancingBars: true, barsPerFetch: 6);
        h.OpenTabs(("BTC/USD", "1h"));

        await h.PollAsync();     // seed
        await h.PollAsync();     // six bars have closed since

        Assert.Single(h.Presenter.Spoken);
    }

    [Fact]
    public async Task Without_the_new_bar_switch_nothing_is_announced()
    {
        // The category switch is the gate; the timeframe floor is only the escape hatch. A test
        // that never turned the switch on would pass against a monitor that ignored it.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));
        h.Session.Get<ISettingsManager>().GetSetting(SettingsKeys.DesktopNotifyNewBars)
            .Returns((JToken?)null);      // the shipped default: off

        await h.PollAsync();
        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
    }

    [Fact]
    public async Task A_timeframe_below_the_floor_is_silent_and_one_above_it_is_not()
    {
        // Written as a PAIR against ONE floor. A test that only showed the silence would pass
        // against a monitor that had stopped announcing anything at all.
        using var quiet = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        quiet.OpenTabs(("BTC/USD", "1m"));
        quiet.SetBarFloor("15m");
        await quiet.PollAsync();
        await quiet.PollAsync();
        Assert.Empty(quiet.Presenter.Spoken);

        using var loud = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        loud.OpenTabs(("BTC/USD", "1h"));
        loud.SetBarFloor("15m");
        await loud.PollAsync();
        await loud.PollAsync();
        Assert.Single(loud.Presenter.Spoken);
    }

    [Fact]
    public async Task The_default_floor_announces_a_one_minute_chart()
    {
        // Cody, 2026-09-08: the default is 1 minute — every timeframe announces. A bare settings
        // substitute returns null here, which IS the shipped default, so this pins the decision
        // rather than a fixture.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1m"));

        await h.PollAsync();
        await h.PollAsync();

        Assert.Single(h.Presenter.Spoken);
    }

    [Fact]
    public async Task A_symbol_an_open_circuit_covers_is_not_announced_headless()
    {
        // The doubling hazard, third phase running. With a browser open on BTC, the focused
        // chart publishes NewBarEvent and BackgroundBarAnnouncer covers the other live tabs.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));
        using var _ = OpenCircuit("c1", "btc/usd");

        await h.PollAsync();
        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
    }

    [Fact]
    public async Task A_covered_chart_is_still_observed_so_the_first_close_after_the_browser_goes_is_announced()
    {
        // Cody, 2026-09-11: three tabs, a 1-minute chart, browser closed, nothing. Until this
        // test a covered chart was dropped from the watch list, so the monitor met it for the
        // first time when the browser closed — and a first sighting only seeds. The earliest
        // possible announcement was the SECOND bar to close after the hand-off.
        using var h = new Harness(Array.Empty<AlertDefinition>(), (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));
        var circuit = OpenCircuit("c1", "btc/usd");

        await h.PollAsync();           // browser open: seeds silently
        await h.PollAsync();           // browser open, a bar closed: the browser's to say
        Assert.Empty(h.Presenter.Spoken);

        circuit.Dispose();             // the browser is gone
        await h.PollAsync();           // a bar closed since the last look: OURS, and said NOW

        Assert.Single(h.Presenter.Spoken);
        Assert.Contains("BTC/USD", h.Presenter.Spoken[0]);
    }

    [Fact]
    public async Task A_chart_with_an_alert_AND_a_tab_costs_one_fetch_and_does_both()
    {
        // One request per chart, however many reasons there are to want it — and neither reason
        // may cancel the other. Before MergeTargets, taking the bar watch would have dropped the
        // alert list silently.
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101), advancingBars: true);
        h.OpenTabs(("BTC/USD", "1h"));

        await h.PollAsync();     // seeds the bar watch; the alert crosses here
        await h.PollAsync();     // a bar closes

        // The crossing spoke once (it does not re-cross), and the bar close spoke once.
        Assert.Equal(2, h.Presenter.Spoken.Count);
        Assert.Contains(h.Presenter.Spoken, t => t.Contains("crossed above"));
        Assert.Contains(h.Presenter.Spoken, t => t.Contains("close"));
    }

    // ── The pure helpers ────────────────────────────────────────────────────

    [Fact]
    public void Bar_close_watches_come_from_the_saved_tabs_deduped_and_capped()
    {
        var cfg = new WorkspaceConfiguration
        {
            Tabs = Enumerable.Range(0, 12)
                .Select(i => new TabConfiguration
                {
                    Market = "Spot", Provider = "Bitstamp", Symbol = $"SYM{i}/USD", Timeframe = "1h"
                })
                // the same chart twice, in two tabs
                .Concat(new[] { new TabConfiguration
                {
                    Market = "Spot", Provider = "Bitstamp", Symbol = "SYM0/USD", Timeframe = "1h"
                } })
                .ToList()
        };

        var watches = LocalBackgroundMonitor.DeriveBarCloseWatches(cfg);

        // Capped at the SAME budget BackgroundTabFeedService uses, not a second number.
        Assert.Equal(AccessibleTrader.Core.Services.Feeds.BackgroundTabFeedService.MaxLiveBackgroundFeeds,
                     watches.Count);
        Assert.Equal(watches.Count, watches.Select(w => LocalBackgroundMonitor.WatchKey(w)).Distinct().Count());
    }

    [Fact]
    public void A_tab_with_no_symbol_or_provider_is_not_a_watch()
    {
        var cfg = new WorkspaceConfiguration
        {
            Tabs = new()
            {
                new TabConfiguration { Provider = "Bitstamp", Symbol = "", Timeframe = "1h" },
                new TabConfiguration { Provider = "", Symbol = "BTC/USD", Timeframe = "1h" },
            }
        };

        Assert.Empty(LocalBackgroundMonitor.DeriveBarCloseWatches(cfg));
    }

    [Theory]
    [InlineData("1m", "1m", true)]     // the default floor lets everything through
    [InlineData("1m", "15m", false)]
    [InlineData("1h", "15m", true)]
    [InlineData("15m", "15m", true)]   // the floor itself clears it
    [InlineData("1h", "", true)]       // no floor set
    [InlineData("1h", "nonsense", true)]  // an unparseable floor is not a mute switch
    [InlineData("weird", "15m", true)]    // nor is an unparseable timeframe
    public void The_timeframe_floor_is_a_floor_not_a_gate(string tf, string floor, bool expected)
        => Assert.Equal(expected, LocalBackgroundMonitor.ClearsTimeframeFloor(tf, floor));

    [Fact]
    public async Task No_NewBarEvent_is_published_headless_however_many_bars_close()
    {
        // advancingBars: each poll's fetch returns bars one hour later than the last, so five
        // polls really are four bar closes. Against the default fixed pair this test would be
        // asserting that no bar closed — which is true and proves nothing.
        // The price crosses once and stays above, so the four later closes are bar closes and
        // nothing else: no alert to fire, and — the point of the test — no NewBarEvent either.
        using var h = new Harness(new[] { PriceAlert("BTC/USD", 100) }, (99, 101), advancingBars: true);
        var newBars = new List<NewBarEvent>();
        h.Session.Get<IEventBus>().Subscribe<NewBarEvent>(newBars.Add);

        for (int poll = 0; poll < 5; poll++) await h.PollAsync();

        Assert.Empty(newBars);

        // The negative is only meaningful because the poll DID work: the alert crossed and was
        // delivered. Without this line the test would pass against a monitor that did nothing.
        Assert.Single(h.Presenter.Spoken);
    }
}
