using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AccessibleTrader.Core.Services.MyData;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// Boots the real WebHost — <c>Program.cs</c>, the full DI graph, the real middleware order —
/// on a REAL Kestrel socket, because a browser cannot talk to <c>TestServer</c>.
///
/// <para>
/// <see cref="WebApplicationFactory{T}"/> normally hosts the app on an in-memory transport, which
/// is why every existing WebHost integration test drives it through <c>HttpClient</c> rather than
/// through a page. The double-build below is the documented way out: build the in-memory host the
/// base class requires, then build a SECOND host off the same builder with Kestrel bound to port
/// zero, and hand its address to the browser. Both hosts run the same startup; the Kestrel one is
/// the one under test.
/// </para>
///
/// <para>
/// Mode is Full (single-user local terminal, no accounts): that is the configuration the modals,
/// the shortcut table and the chart all run in unrestricted, so it is the honest surface to audit.
/// Hosted mode gates a third of the dialogs behind <c>DemoPolicy</c> and would quietly shrink the
/// sweep.
/// </para>
/// </summary>
internal sealed class TerminalServerFactory : WebApplicationFactory<WebHostDemoMode>
{
    private IHost? _kestrelHost;
    private readonly string _dataRoot;

    /// <summary>
    /// Redirects the whole app's storage into a throwaway directory BEFORE the host is built.
    ///
    /// <para>
    /// This is not tidiness, it is the difference between a test suite and an accident. Full mode
    /// is the local single-user terminal: <c>PlatformPaths.AppDataRoot()</c> resolves through
    /// <c>XDG_DATA_HOME</c> to <c>~/.local/share/AccessibleTrader</c>, which holds the developer's
    /// real workspaces, saved tabs, journal entries, alert definitions and API keys. The first
    /// run of this harness — before this existed — opened the developer's own four saved chart
    /// tabs and connected live websockets to MEXC, Kraken and Bitstamp to service them. A sweep
    /// that presses Escape twenty-seven times inside that session is one misrouted keystroke away
    /// from editing real data.
    /// </para>
    ///
    /// <para>
    /// The empty root is also what makes the audit repeatable: every run starts from a fresh
    /// install rather than from whatever the last session left behind.
    /// </para>
    /// </summary>
    public TerminalServerFactory()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "atbrowsertests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_dataRoot, "data"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "cache"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, "config"));
        // Pin Playwright's browser location BEFORE moving XDG_CACHE_HOME out from under it —
        // the node driver resolves ~/.cache/ms-playwright through that variable, so redirecting
        // it silently relocates the browsers directory and every launch reports "executable
        // doesn't exist" as though nothing were installed.
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", BrowserAvailability.BrowsersRoot);

        Environment.SetEnvironmentVariable("XDG_DATA_HOME",   Path.Combine(_dataRoot, "data"));
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME",  Path.Combine(_dataRoot, "cache"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_dataRoot, "config"));

        SeedMyDataDataset();
    }

    /// <summary>
    /// The dataset name the seeded market is charted under — the value of the Symbol dropdown
    /// in <see cref="TerminalPage.LoadSeededChartAsync"/>.
    ///
    /// <para>
    /// <b>The space is deliberate and load-bearing.</b> The first version of this seed failed
    /// here, and the failure was production's: <c>SymbolValidator</c>'s charset has no space in
    /// it, so the fetch chokepoint rejected the user's own dataset name and the terminal said
    /// "Invalid symbol 'Harness Candles' for My Data. No data for Harness Candles from My Data.
    /// The chart is empty." Any name a person would actually give a CSV was unchartable. Keeping
    /// the space means this suite fails again if that exemption is removed — see
    /// <c>SymbolChokepointExemptionTests</c>.
    /// </para>
    /// </summary>
    public const string SeededSymbol = "Harness Candles";

    /// <summary>The market and provider that serve <see cref="SeededSymbol"/>.</summary>
    public const string SeededMarket = "MyData";
    public const string SeededProvider = MyDataProvider.ProviderName;

    /// <summary>How many bars the seeded dataset holds.</summary>
    public const int SeededBarCount = 200;

    /// <summary>
    /// Writes one OHLCV dataset into the throwaway data root BEFORE the host is built, so the
    /// terminal boots with a market it can chart offline.
    ///
    /// <para>
    /// This exists because of what the fourteenth pass measured: EVERY browser route reached the
    /// Object Tree at cold start, where <c>ActiveSeries</c> is empty and the tree renders "No
    /// series active on chart" with no rows in it at all. The whole tree contract — expansion
    /// state, roving tabindex, the <c>&lt;details&gt;</c> toggle loop that hung Alt+O — was being
    /// asserted over an empty dialog. Adding an indicator through the Add Indicator dialog does
    /// not help: an indicator has nothing to compute against until the chart has bars.
    /// </para>
    ///
    /// <para>
    /// "My Data" is the seam that makes this offline. It is a built-in provider
    /// (<see cref="IBuiltInDataProvider"/>), needs no API key, declares
    /// <see cref="ProviderEnvironment.HistoricalOnly"/> so nothing opens a socket, and reads its
    /// datasets from <c>AppDataDirectory/my-data</c> — a directory this factory already owns,
    /// because the XDG variables above are set before anything resolves a path. No network, no
    /// credentials, no test-only branch in production code.
    /// </para>
    ///
    /// <para>
    /// The import goes through the REAL <see cref="MyDataStore.ImportAsync"/> rather than a
    /// hand-written manifest: a hand-written one would encode this test's belief about the
    /// on-disk format, and would keep passing after the format changed underneath it.
    /// </para>
    /// </summary>
    private void SeedMyDataDataset()
    {
        var paths = new WebHost.Services.WebHostPathService(
            Path.Combine(_dataRoot, "data", "AccessibleTrader"));
        var store = new MyDataStore(paths,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MyDataStore>.Instance);
        store.ImportAsync(SeededSymbol, BuildOhlcvCsv(SeededBarCount)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// A deterministic daily OHLCV series. Deterministic — a fixed seed, not
    /// <see cref="Random.Shared"/> — because a failure in this suite has to be reproducible from
    /// its name alone; and shaped rather than flat, because a constant close makes every
    /// range-dependent assertion (viewport min/max, sonification pitch, "value" readback) pass
    /// for the wrong reason.
    /// </summary>
    private static string BuildOhlcvCsv(int bars)
    {
        var rng = new Random(20260904);
        var sb = new System.Text.StringBuilder("date,open,high,low,close,volume\n");
        var day = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double close = 100.0;
        for (int i = 0; i < bars; i++)
        {
            double open = close;
            double drift = Math.Sin(i / 9.0) * 1.5 + (rng.NextDouble() - 0.5) * 1.2;
            close = Math.Round(Math.Max(1.0, open + drift), 2);
            double high = Math.Round(Math.Max(open, close) + rng.NextDouble() * 0.9, 2);
            double low  = Math.Round(Math.Min(open, close) - rng.NextDouble() * 0.9, 2);
            double vol  = Math.Round(500 + rng.NextDouble() * 500, 2);
            sb.Append(day.AddDays(i).ToString("yyyy-MM-dd"))
              .Append(',').Append(open.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(',').Append(high.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(',').Append(low.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(',').Append(close.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(',').Append(vol.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>The throwaway storage root, for tests that want to inspect what was written.</summary>
    public string DataRoot => _dataRoot;

    /// <summary>The http://127.0.0.1:PORT the browser should navigate to.</summary>
    public string RootUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Every address Kestrel actually bound, not just the one the browser is pointed at.
    ///
    /// <para>
    /// This is exposed because <see cref="RootUrl"/> cannot answer the question that matters:
    /// it is non-empty whether the host took one socket or three. The harness once bound the
    /// ephemeral port AND <c>appsettings.json</c>'s <c>5145</c>, which is silent on CI (nothing
    /// owns 5145 there) and fatal on the box that serves the demo on exactly that port.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> BoundAddresses { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Everything the host logged, in order. The WebHost swallows a great many exceptions by
    /// design (A1 counted 872 catch clauses); when the browser sees nothing happen, the server
    /// log is usually the only place that says why.
    /// </summary>
    public IReadOnlyList<string> Log => _log;
    private readonly List<string> _log = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        // Program.cs reads this instead of the --no-launch argv flag, because a factory cannot
        // pass command-line args. Without it the host shells out to xdg-open on every test run.
        builder.UseSetting("Launch:Disabled", "true");

        builder.ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
            lb.AddProvider(new CapturingLoggerProvider(_log));
        });

        builder.ConfigureTestServices(services =>
        {
            // The tray applet and the local background monitor shell out to D-Bus, notify-send
            // and speech tools. A test box may not have them and a test run must not disturb
            // the developer's desktop — the same two removals WebHostIntegration.FullFactory
            // makes for the HttpClient-level tests.
            var desktopOnly = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                         && (d.ImplementationType == typeof(WebHost.Services.LocalBackgroundMonitor)
                          || d.ImplementationType == typeof(WebHost.Services.Tray.DesktopTrayService)))
                .ToList();
            foreach (var d in desktopOnly) services.Remove(d);

            // Pin speech to the browser.
            //
            // WebHostSpeechManager probes the machine and prefers Orca over spd-say over the
            // browser. On any Linux developer box with speech-dispatcher installed — this one —
            // that resolves to SpdSay, which means two things at once: the harness talks out loud
            // through the developer's own speakers, and the ARIA live region is deliberately
            // emptied (ShouldEnableLiveRegion) so a screen reader does not hear everything twice.
            // Both make the browser deaf to what the app says, which is precisely what this
            // suite exists to listen to.
            //
            // Passing null probes forces SelectBackend to BrowserTts — the configuration every
            // hosted visitor gets, and the only one where "what the terminal said" is a question
            // a browser can answer.
            services.RemoveAll<AccessibleTrader.Core.Services.ISpeechManager>();
            services.RemoveAll<AccessibleTrader.Core.Services.IBrowserSpeechOutput>();
            services.AddScoped<AccessibleTrader.Core.Services.ISpeechManager>(sp =>
                new WebHost.Services.WebHostSpeechManager(
                    sp.GetRequiredService<BlazorClient.Services.BlazorSpeechManager>(),
                    sp.GetRequiredService<AccessibleTrader.Core.Services.IEventBus>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WebHost.Services.WebHostSpeechManager>>(),
                    spdSayPath: null, gdbusPath: null, orcaAvailable: false));
            services.AddScoped<AccessibleTrader.Core.Services.IBrowserSpeechOutput>(sp =>
                (WebHost.Services.WebHostSpeechManager)
                    sp.GetRequiredService<AccessibleTrader.Core.Services.ISpeechManager>());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The in-memory host the base class contract requires. It is started but never driven.
        var testHost = builder.Build();

        // The real one. Port 0 lets the OS pick, so parallel runs and a developer's own
        // `dotnet run` on 5145 cannot collide.
        //
        // OVERRIDE the configured endpoint; do not add a listener next to it. This used to be
        // `b.UseKestrel(o => o.Listen(IPAddress.Loopback, 0))`, and a Listen call does not
        // replace `Kestrel:Endpoints` — it adds to it. The builder still reads the WebHost's
        // appsettings.json, whose Http endpoint is http://localhost:5145, so the harness took
        // the ephemeral port AND the demo's port. Nothing owns 5145 on CI, so the second bind
        // succeeded in silence there; on the box that actually serves the demo, all 128 cases
        // died with "Failed to bind to address http://127.0.0.1:5145: address already in use"
        // — i.e. the one check that proves a deployed commit renders could not be run on the
        // machine doing the deploying.
        //
        // An in-memory source added here wins because it is appended after the app's own
        // sources; the guard in HarnessSmokeTests asserts the resulting bind set, not RootUrl.
        builder.ConfigureWebHost(b => b
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Http:Url"] = $"http://{IPAddress.Loopback}:0",
            }))
            .UseKestrel());
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        var addresses = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no bound addresses.");
        BoundAddresses = addresses.Addresses.ToList();
        RootUrl = BoundAddresses.First();

        testHost.Start();
        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kestrelHost?.StopAsync().GetAwaiter().GetResult();
            _kestrelHost?.Dispose();
            _kestrelHost = null;
            try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best effort */ }
        }
        base.Dispose(disposing);
    }
}

/// <summary>Minimal logger provider that appends every message to a shared list.</summary>
internal sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly List<string> _sink;
    public CapturingLoggerProvider(List<string> sink) => _sink = sink;
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Sink(categoryName, _sink);
    public void Dispose() { }

    private sealed class Sink : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _category;
        private readonly List<string> _sink;
        public Sink(string category, List<string> sink) { _category = category; _sink = sink; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_sink)
                _sink.Add($"[{logLevel}] {_category}: {formatter(state, exception)}"
                        + (exception is null ? "" : $" || {exception.GetType().Name}: {exception.Message}"));
        }
    }
}
