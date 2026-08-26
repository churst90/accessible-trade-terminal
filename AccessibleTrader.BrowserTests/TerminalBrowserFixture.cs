using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// One WebHost and one Chromium for the whole browser suite, because starting either costs
/// seconds and neither carries state between tests — each test gets its own
/// <see cref="IBrowserContext"/>, which is Playwright's isolation unit (own cookies, own
/// storage, own page).
/// </summary>
public sealed class TerminalBrowserFixture : IAsyncLifetime
{
    private TerminalServerFactory? _factory;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    internal string RootUrl => _factory?.RootUrl
        ?? throw new InvalidOperationException("Fixture not initialised.");

    /// <summary>Everything the host has logged so far.</summary>
    internal IReadOnlyList<string> ServerLog => _factory?.Log ?? Array.Empty<string>();

    public async Task InitializeAsync()
    {
        _factory = new TerminalServerFactory();

        // Forces the lazy host build (and therefore the Kestrel bind) before any test runs.
        _ = _factory.Server;

        if (BrowserAvailability.SkipReason != null) return;   // every test is skipped anyway

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // The full Chromium build rather than Playwright's separate headless-shell download.
            // Two reasons: one browser to install instead of two, and the shell is a stripped
            // binary — this suite is auditing what a real browser exposes to a screen reader, so
            // it should run the same engine a user runs.
            Channel = "chromium",
            // Chromium's sandbox needs user namespaces, which are not universally available on
            // developer boxes or CI containers. The page under test is a localhost app this
            // process just started, so there is nothing here the sandbox is protecting against.
            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage" },
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _factory?.Dispose();
    }

    /// <summary>
    /// A fresh browser context on the terminal's home page, with the input pipeline proven
    /// armed. Dispose the returned object at the end of the test.
    /// </summary>
    internal async Task<TerminalPage> NewPageAsync()
    {
        if (_browser == null)
            throw new InvalidOperationException(
                "No browser. " + (BrowserAvailability.SkipReason ?? "Launch failed."));

        // If the app has already proven it will not load, say so immediately instead of waiting
        // out the timeouts again. This is not a nicety: on CI the first run of this suite spent
        // its entire 25-minute budget on 45 consecutive 30-second waits for the same heading on
        // the same broken host, and the job was killed mid-suite — so the run reported a timeout
        // rather than a failure, produced no .trx artifact, and never reached the tests that
        // might have failed differently. One host that cannot serve the app is one failure.
        if (_appNeverLoaded != null)
            throw new AppNeverLoadedException(
                "The terminal never loaded on the first attempt; not retrying for every test.\n\n"
                + _appNeverLoaded.Message);

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1400, Height = 950 },
        });
        var page = await context.NewPageAsync();
        var terminal = new TerminalPage(page, context, () => ServerLog);
        try
        {
            await terminal.GotoAppAsync(RootUrl);
        }
        catch (AppNeverLoadedException ex)
        {
            _appNeverLoaded = ex;
            await terminal.DisposeAsync();
            throw;
        }
        return terminal;
    }

    /// <summary>
    /// Set once the app has failed to load, so the rest of the suite fails fast with the same
    /// diagnosis rather than re-timing-out against a host already known to be broken.
    /// </summary>
    private AppNeverLoadedException? _appNeverLoaded;
}

/// <summary>
/// xUnit collection so the fixture is built once and the browser tests run one at a time.
/// Serial is deliberate: these share a Kestrel host whose singletons (workspace store, event
/// bus, speech manager) are process-wide in Full mode, so two pages at once would be two users
/// sharing one terminal.
/// </summary>
[CollectionDefinition("Terminal browser")]
public sealed class TerminalBrowserCollection : ICollectionFixture<TerminalBrowserFixture> { }
