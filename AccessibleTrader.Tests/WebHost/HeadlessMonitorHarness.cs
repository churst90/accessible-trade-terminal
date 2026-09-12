using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Services.Strategies.Levels;
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

/// <summary>What reached the desktop. Every headless delivery test reads this.</summary>
internal sealed class SpyPresenter : IDesktopAlertPresenter
{
    public readonly List<(string Title, string Text, bool Urgent)> Toasts = new();
    public readonly List<string> Spoken = new();
    public int SoundsPlayed;
    /// <summary>Whether this machine has a notification tool. False by default so the
    /// "exactly one delivery" assertions read the sentence off <see cref="Spoken"/>;
    /// the doubling tests set it and read the toast instead.</summary>
    public bool HasNotificationTool;

    public string Describe() => "spy";
    public string DescribeToast() => "spy toast";
    public bool CanNotify => HasNotificationTool;
    public void PlayNotificationSound() => SoundsPlayed++;
    public void Notify(string title, string text, bool urgent) => Toasts.Add((title, text, urgent));
    public void Speak(string text) => Spoken.Add(text);
}

/// <summary>
/// <b>The real background monitor poll, with the real indicator stack, driven by a clock the
/// test advances.</b> Lifted out of <c>HeadlessNarrationTests</c> on 2026-09-11 when the
/// alert half of Phase 3 D4 needed exactly the same rig: one saved tab, a provider that
/// returns bars up to a forming one, the headless scope as the WebHost registers it, and a
/// presenter that records what reached the desktop.
///
/// <para>The registrations mirror <c>WebHost/ServiceCollectionExtensions</c> for everything
/// the headless chart reads — Core and Skender providers, the engine, mapper and model factory,
/// the profile service, the level service with the profile level provider, the signal catalog
/// and the condition-tree evaluator — so what a test observes is what the shipped poll does.</para>
/// </summary>
internal sealed class HeadlessMonitorHarness : IDisposable
{
    public static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
    public const int StartHour = 99;

    public readonly SpyPresenter Presenter = new();
    public readonly List<int> RequestedLimits = new();
    /// <summary>Every Warning-or-worse log line from the headless scope — the factory says why
    /// a series could not be built here, and a test that fails for that reason should say so.</summary>
    public readonly List<string> Logs = new();
    public readonly HeadlessSession Session;
    public readonly LocalBackgroundMonitor Monitor;

    private readonly ServiceProvider _root;
    private readonly ISettingsManager _settings;
    private int _clock;   // hours since StartHour; the newest bar is always forming

    /// <param name="savedSeries">The saved tab's series configs.</param>
    /// <param name="timeframe">The saved tab's timeframe (and the alerts', when they name one).</param>
    /// <param name="alerts">What <c>LoadAlerts</c> returns. Empty by default.</param>
    /// <param name="priceAt">The price of the bar at hour <c>h</c> (open, high, low and close
    /// are all this). Constant 100 by default, so indicator crossings are the test's to arrange.</param>
    /// <param name="withTab">False to save NO tab at all — an alert on a symbol nothing is
    /// open on, which is the ordinary case for a price alert and the unwatchable one for a POC alert.</param>
    public HeadlessMonitorHarness(
        IReadOnlyList<SeriesConfig> savedSeries, string timeframe = "1h",
        IReadOnlyList<AlertDefinition>? alerts = null, Func<int, double>? priceAt = null, bool withTab = true)
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
                double p = priceAt?.Invoke(h) ?? 100;
                bars.Add(new Ohlcv(T0.AddHours(h), p, p, p, p, v));
            }
            return (bars, new List<(long, double)>());
        });

        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(provider);

        var session = new WorkspaceConfiguration
        {
            Tabs = withTab
                ? new List<TabConfiguration>
                {
                    new() { Market = "Spot", Provider = "Bitstamp", Symbol = "BTC/USD", Timeframe = timeframe, Series = savedSeries.ToList() },
                }
                : new List<TabConfiguration>(),
        };
        var library = Substitute.For<IWorkspaceLibraryService>();
        library.LoadAlerts().Returns(_ => (alerts ?? Array.Empty<AlertDefinition>()).ToList());
        library.GetAllProfilesWithTimes().Returns(_ => new[] { (SessionAutosaveService.LastSessionProfileName + "x", DateTime.UtcNow) });
        library.LoadProfile(Arg.Any<string>()).Returns(_ => session);

        _settings = Substitute.For<ISettingsManager>();
        _settings.GetSetting(LocalBackgroundMonitor.SettingKey).Returns(JToken.FromObject(true));

        var services = new ServiceCollection();
        services.AddLogging(b => b.Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new ListLoggerProvider(Logs)));
        services.AddScoped<IEventBus, EventBus>();
        services.AddScoped<IWorkspaceStore>(_ => new MockWorkspaceStore());
        services.AddSingleton(_settings);
        services.AddSingleton(library);
        services.AddSingleton(data);
        services.AddSingleton(Substitute.For<IPluginLoaderService>());

        // The real indicator stack, as the WebHost registers it in the headless scope.
        services.AddScoped<IIndicatorProvider, CoreIndicatorProvider>();
        services.AddScoped<IIndicatorProvider, SkenderTrendProvider>();
        services.AddScoped<IIndicatorProvider, ProfileIndicatorProvider>();
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
        // What the alerts read: profiles (POC), levels off them, the catalog and tree evaluator.
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ILevelProvider, VolumeProfileLevelProvider>();
        services.AddScoped<ILevelService, LevelService>();
        services.AddScoped<ISignalCatalog, SignalCatalog>();
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        services.AddScoped<HeadlessChartFactory>();

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
    public void BarFloor(string tf) =>
        _settings.GetSetting(SettingsKeys.HeadlessNewBarMinTimeframe).Returns(JToken.FromObject(tf));

    /// <summary>The clock moves: the forming bar closes and a new one opens.</summary>
    public void CloseABar() => _clock++;

    public Task PollAsync() => Monitor.PollOnceAsync(CancellationToken.None);

    /// <summary>What reached the user: the toast bodies on a machine with a notification tool,
    /// the speech on one without (DesktopAnnouncement.Present raises the toast either way and
    /// speaks only where nothing can read a toast — so counting both would count one
    /// announcement twice).</summary>
    public IEnumerable<string> Delivered => Presenter.HasNotificationTool
        ? Presenter.Toasts.Select(t => t.Text)
        : Presenter.Spoken;

    /// <summary>Exactly one delivery, or a failure that shows what was delivered and logged.</summary>
    public string Single()
    {
        var d = Delivered.ToList();
        Assert.True(d.Count == 1, $"delivered {d.Count}: {string.Join(" || ", d)}\nlogs: {string.Join("\n", Logs)}");
        return d[0];
    }

    /// <summary>No delivery, or a failure that shows what was delivered.</summary>
    public void Nothing()
    {
        var d = Delivered.ToList();
        Assert.True(d.Count == 0, $"delivered {d.Count}: {string.Join(" || ", d)}\nlogs: {string.Join("\n", Logs)}");
    }

    public void Dispose() { Session.Dispose(); _root.Dispose(); }

    private sealed class ListLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        private readonly List<string> _sink;
        public ListLoggerProvider(List<string> sink) => _sink = sink;
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new ListLogger(_sink, categoryName);
        public void Dispose() { }

        private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly List<string> _sink; private readonly string _cat;
            public ListLogger(List<string> sink, string cat) { _sink = sink; _cat = cat; }
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level >= Microsoft.Extensions.Logging.LogLevel.Warning;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f)
            {
                if (!IsEnabled(level)) return;
                lock (_sink) _sink.Add($"[{level}] {_cat}: {f(state, ex)}{(ex == null ? "" : " :: " + ex)}");
            }
        }
    }
}
