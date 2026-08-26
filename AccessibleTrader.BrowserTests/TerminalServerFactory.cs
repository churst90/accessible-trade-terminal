using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    }

    /// <summary>The throwaway storage root, for tests that want to inspect what was written.</summary>
    public string DataRoot => _dataRoot;

    /// <summary>The http://127.0.0.1:PORT the browser should navigate to.</summary>
    public string RootUrl { get; private set; } = string.Empty;

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
        builder.ConfigureWebHost(b => b.UseKestrel(o => o.Listen(IPAddress.Loopback, 0)));
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        var addresses = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no bound addresses.");
        RootUrl = addresses.Addresses.First();

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
