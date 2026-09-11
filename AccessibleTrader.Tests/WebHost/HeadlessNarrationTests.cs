using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Workspace;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;
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
/// <b>Phase 3 D4: the narration ladder with the browser closed, driven through the real
/// monitor poll.</b> Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken
/// when the browser is closed too."</i>
///
/// <para>
/// The harness registers the REAL indicator stack in the headless scope (Core + Skender
/// providers, engine, mapper, model factory, context analyser) and a provider whose clock the
/// test advances, so every case below is what actually reaches the desktop — and, as with every
/// delivery test since Phase 1, each is written with a browser circuit open and with none.
/// </para>
///
/// <para>
/// The second half pins the doubling Cody reported the same day — <i>"orca reads the
/// notification twice"</i> — through the same poll: where the screen reader reads the toast,
/// the monitor no longer speaks on top of it.
/// </para>
/// </summary>
[Collection("CircuitCoverage")]
public sealed class HeadlessNarrationTests : IDisposable
{
    public void Dispose() => CircuitAlertCoverage.ResetForTests();

    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    private const int StartHour = 99;

    private sealed class SpyPresenter : IDesktopAlertPresenter
    {
        public readonly List<(string Title, string Text, bool Urgent)> Toasts = new();
        public readonly List<string> Spoken = new();
        public int SoundsPlayed;
        public bool ToastIsSpokenByScreenReader;

        public string Describe() => "spy";
        public string DescribeToast() => "spy toast";
        public bool CanNotify => true;
        public bool ToastIsSpoken => ToastIsSpokenByScreenReader;
        public void PlayNotificationSound() => SoundsPlayed++;
        public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text, urgent));
        public void Speak(string text) => Spoken.Add(text);
    }

    private sealed class Harness : IDisposable
    {
        public readonly SpyPresenter Presenter = new();
        public readonly List<int> RequestedLimits = new();
        public readonly HeadlessSession Session;
        public readonly LocalBackgroundMonitor Monitor;

        private readonly ServiceProvider _root;
        private readonly ISettingsManager _settings;
        private int _clock;   // hours since StartHour; the newest bar is always forming

        public Harness(IReadOnlyList<SeriesConfig> savedSeries, string timeframe = "1h")
        {
            var provider = Substitute.For<IMarketDataProvider>();
            provider.FetchOhlcvAsync(Arg.Any<MarketDataRequest>()).Returns(ci =>
            {
                var req = ci.Arg<MarketDataRequest>();
                RequestedLimits.Add(req.Limit);
                int newest = StartHour + _clock;
                var bars = new List<Ohlcv>();
                for (int h = newest - req.Limit + 1; h <= newest; h++)
                {
                    double v = h == newest ? 5 : (h >= 0 ? 1000.0 * (h + 1) : 1);
                    bars.Add(new Ohlcv(T0.AddHours(h), 100, 100, 100, 100, v));
                }
                return (bars, new List<(long, double)>());
            });

            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(provider);

            var session = new WorkspaceConfiguration
            {
                Tabs = new List<TabConfiguration>
                {
                    new() { Market = "Spot", Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = timeframe, Series = savedSeries.ToList() },
                },
            };
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(_ => new List<AlertDefinition>());
            library.GetAllProfilesWithTimes().Returns(_ => new[] { (SessionAutosaveService.LastSessionProfileName + "x", DateTime.UtcNow) });
            library.LoadProfile(Arg.Any<string>()).Returns(_ => session);

            _settings = Substitute.For<ISettingsManager>();
            _settings.GetSetting(LocalBackgroundMonitor.SettingKey).Returns(JToken.FromObject(true));

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<IEventBus, EventBus>();
            services.AddScoped<IWorkspaceStore>(_ => new MockWorkspaceStore());
            services.AddSingleton(_settings);
            services.AddSingleton(library);
            services.AddSingleton(data);
            services.AddSingleton(Substitute.For<IPluginLoaderService>());

            // The real indicator stack, as the WebHost registers it in the headless scope.
            services.AddScoped<IIndicatorProvider, CoreIndicatorProvider>();
            services.AddScoped<IIndicatorProvider, SkenderTrendProvider>();
            services.AddScoped<IIndicatorService, IndicatorService>();
            services.AddScoped<ICustomIndicatorRegistry, CustomIndicatorRegistry>();
            services.AddScoped<IIndicatorEngine, IndicatorEngine>();
            services.AddScoped<IIndicatorStateMapper, IndicatorStateMapper>();
            services.AddScoped<IComponentRoleMapper, ComponentRoleMapper>();
            services.AddScoped<ISonificationProfileProvider, SonificationProfileProvider>();
            services.AddScoped<IPaneAssignmentService, PaneAssignmentService>();
            services.AddScoped<IStylingService, StylingService>();
            services.AddScoped<IIndicatorPreferencesService, MockIndicatorPreferencesService>();
            services.AddScoped<IIndicatorModelFactory, IndicatorModelFactory>();
            services.AddScoped<IIndicatorContextAnalyzer, IndicatorContextAnalyzer>();
            services.AddScoped<HeadlessNarration>();

            _root = services.BuildServiceProvider();
            Session = new HeadlessSession(_root.GetRequiredService<IServiceScopeFactory>(), NullLogger<HeadlessSession>.Instance);
            Monitor = new LocalBackgroundMonitor(
                Session, new DemoPolicy(isDemo: false), new RecentAlertsBuffer(), new AlertSnooze(),
                Presenter, NullLogger<LocalBackgroundMonitor>.Instance);
        }

        public void NewBarToasts(bool on) =>
            _settings.GetSetting(SettingsKeys.DesktopNotifyNewBars).Returns(JToken.FromObject(on));
        public void NarrationMaster(bool on) =>
            _settings.GetSetting(SettingsKeys.NarrateSignalsOnBarClose).Returns(JToken.FromObject(on));
        public void SpeakBesideToast(bool on) =>
            _settings.GetSetting(SettingsKeys.DesktopSpeakBesideToast).Returns(JToken.FromObject(on));
        public void BarFloor(string tf) =>
            _settings.GetSetting(SettingsKeys.HeadlessNewBarMinTimeframe).Returns(JToken.FromObject(tf));

        /// <summary>The clock moves: the forming bar closes and a new one opens.</summary>
        public void CloseABar() => _clock++;

        public Task PollAsync() => Monitor.PollOnceAsync(CancellationToken.None);

        public void Dispose() { Session.Dispose(); _root.Dispose(); }
    }

    private static SeriesConfig SavedVolume(bool narrated = true)
    {
        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Volume, Name = "Volume", FriendlyName = "Volume", IndicatorCode = "VOLUME",
            Pane = "Volume", IsAutoNarrated = narrated, IsVisible = true,
        };
        cfg.Components.Add(new ComponentConfig
        {
            Name = "Volume", DisplayName = "Volume", DisplayType = ComponentDisplayType.Bar,
            Role = ComponentRole.Volume, DataMapping = "volume", IsVisible = true,
        });
        return cfg;
    }

    private static IDisposable OpenCircuit(string id, params string[] symbols) =>
        CircuitAlertCoverage.Register(id, () => symbols);

    // ── The ladder, browser closed ────────────────────────────────────────────

    [Fact]
    public async Task With_no_browser_the_ladder_rides_the_bar_close_as_ONE_utterance()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        await h.PollAsync();          // seeds; nothing
        Assert.Empty(h.Presenter.Spoken);

        h.CloseABar();
        await h.PollAsync();          // hour 99 closed at its final volume

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.StartsWith("BTC/USD 1h: close 100.00", one, StringComparison.Ordinal);
        Assert.Contains("Volume 100,000", one, StringComparison.Ordinal);
        Assert.True(one.IndexOf("New bar", StringComparison.Ordinal) < one.IndexOf("Volume 100,000", StringComparison.Ordinal),
            "the close first, the reading last: " + one);
        // And the toast carries the same sentence — it is the spoken route where the screen
        // reader reads notifications.
        var toast = Assert.Single(h.Presenter.Toasts);
        Assert.Equal(one, toast.Text);
    }

    [Fact]
    public async Task With_the_new_bar_switch_off_the_ladder_still_speaks_led_by_the_symbol()
    {
        // In-session the ladder answers to the narration switches, not to the new-bar toast.
        // Headless the same: N on a volume pane is a request for the reading, whether or not
        // the user also wants "close … new bar" announced.
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(false);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Equal("BTC/USD 1h: Volume 100,000.", one);
        Assert.Equal(0, h.Presenter.SoundsPlayed);   // a reading is not a notification event
    }

    [Fact]
    public async Task With_the_narration_master_switch_off_the_ladder_is_silent()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.NarrationMaster(false);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("close 100.00", one, StringComparison.Ordinal);
        Assert.DoesNotContain("Volume", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_timeframe_floor_gates_the_bar_close_and_not_the_ladder()
    {
        // A 1-minute chart below a 1-hour floor: no "close … new bar", but the user flagged its
        // volume with N and that is a request for a reading a minute.
        using var h = new Harness(new[] { SavedVolume() }, timeframe: "1m");
        h.NewBarToasts(true);
        h.BarFloor("1h");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Equal("BTC/USD 1m: Volume 100,000.", one);
    }

    [Fact]
    public async Task A_tab_with_nothing_under_N_is_never_narrated_and_costs_the_small_fetch()
    {
        using var h = new Harness(new[] { SavedVolume(narrated: false) });
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.DoesNotContain("Volume", one, StringComparison.Ordinal);
        Assert.All(h.RequestedLimits, l => Assert.Equal(LocalBackgroundMonitor.MinFetch, l));
    }

    [Fact]
    public async Task The_first_fetch_asks_for_the_indicators_history_and_later_ones_for_three()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Equal(3, h.RequestedLimits.Count);
        Assert.Equal(HeadlessChartNarrator.MinBars, h.RequestedLimits[0]);
        Assert.Equal(HeadlessChartNarrator.CatchUpLimit, h.RequestedLimits[1]);
        Assert.Equal(HeadlessChartNarrator.CatchUpLimit, h.RequestedLimits[2]);
        Assert.Equal(2, h.Presenter.Spoken.Count);
    }

    // ── Ownership: the browser covers what it covers ──────────────────────────

    [Fact]
    public async Task A_chart_an_open_circuit_covers_is_observed_silently_and_narrates_the_first_close_after_the_browser_goes()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);

        var circuit = OpenCircuit("c1", "BTC/USD");
        await h.PollAsync();          // seeds while covered
        h.CloseABar();
        await h.PollAsync();          // a close while covered: the browser says it, not us
        Assert.Empty(h.Presenter.Spoken);
        Assert.Empty(h.Presenter.Toasts);

        circuit.Dispose();            // the browser closed
        h.CloseABar();
        await h.PollAsync();          // the FIRST close after the hand-off speaks — no re-seed

        string one = Assert.Single(h.Presenter.Spoken);
        Assert.Contains("Volume 101,000", one, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_circuit_on_another_symbol_does_not_silence_ours()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        using var _ = OpenCircuit("c1", "ETH/USD");

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Contains("Volume 100,000", Assert.Single(h.Presenter.Spoken), StringComparison.Ordinal);
    }

    // ── The doubling: "orca reads the notification twice" ─────────────────────

    [Fact]
    public async Task Where_the_screen_reader_reads_the_toast_the_monitor_does_not_speak_as_well()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.Presenter.ToastIsSpokenByScreenReader = true;

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Empty(h.Presenter.Spoken);
        var toast = Assert.Single(h.Presenter.Toasts);
        // The toast is now the whole announcement, ladder included.
        Assert.Contains("close 100.00", toast.Text, StringComparison.Ordinal);
        Assert.Contains("Volume 100,000", toast.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Presenter.SoundsPlayed);
    }

    [Fact]
    public async Task Unless_the_user_asked_to_be_spoken_to_as_well()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.Presenter.ToastIsSpokenByScreenReader = true;
        h.SpeakBesideToast(true);

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Single(h.Presenter.Spoken);
        Assert.Single(h.Presenter.Toasts);
    }

    [Fact]
    public async Task Where_the_screen_reader_does_NOT_read_the_toast_the_monitor_speaks_as_before()
    {
        using var h = new Harness(new[] { SavedVolume() });
        h.NewBarToasts(true);
        h.Presenter.ToastIsSpokenByScreenReader = false;

        await h.PollAsync();
        h.CloseABar();
        await h.PollAsync();

        Assert.Single(h.Presenter.Spoken);
        Assert.Single(h.Presenter.Toasts);
    }
}
