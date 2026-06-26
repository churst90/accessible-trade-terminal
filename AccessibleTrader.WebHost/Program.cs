using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.DataProtection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost;
using AccessibleTrader.WebHost.Components;
using AccessibleTrader.WebHost.Services;

var builder = WebApplication.CreateBuilder(args);

// Load the static-web-assets manifest in every environment, not just
// Development. CreateBuilder only auto-calls this when ASPNETCORE_ENVIRONMENT
// is "Development" — without it, blazor.web.js, the RCL's scoped-CSS bundle,
// and host-app styles all 404 in Production-style runs (e.g. plain `dotnet
// run` on Windows without a launchSettings.json).
builder.WebHost.UseStaticWebAssets();

// --demo  → public website chart demo mode (read-only, no API keys, no orders).
//           Wired in phase L7. Recognised at L1 only as a flag we don't crash on.
// --no-launch → skip the browser auto-launch (useful when running headless or
//               attaching from VS Code).
bool demoMode = args.Contains("--demo");
bool autoLaunch = !args.Contains("--no-launch");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDataProtection()
    .SetApplicationName("AccessibleTrader.WebHost");

builder.Services.AddAccessibleTraderWebHostServices();

builder.Services.AddSingleton(new WebHostDemoMode(demoMode));
// Central public-demo policy (provider/symbol/timeframe/indicator whitelist + feature
// gates). A no-op when demoMode is false, so the WebHost is unaffected outside --demo.
builder.Services.AddSingleton(new DemoPolicy(demoMode));

var app = builder.Build();

// In --demo mode the app is reverse-proxied behind nginx under the /app/ subpath
// on the public marketing site (trade.codyhurst.com/app/). UsePathBase aligns every
// route, static asset, and the /_blazor SignalR endpoint with the base href set in
// App.razor. Must run first in the pipeline. (Deploy-only; kept local to this host.)
if (demoMode)
{
    app.UsePathBase("/app");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// MapStaticAssets (not UseStaticFiles) serves the manifest-based asset endpoints —
// including _framework/blazor.web.js and the RCL's content-fingerprinted scoped-CSS
// bundle. UseStaticFiles only serves physical filenames, so those 404 in a published
// build and the Blazor circuit never boots ("no data loaded"). (Deploy-only fix.)
app.MapStaticAssets();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // The RCL holds every @page directive (Pages/Home.razor today). Without
    // AddAdditionalAssemblies the WebHost Router scans only this assembly and
    // would 404 on every route.
    .AddAdditionalAssemblies(typeof(AccessibleTrader.BlazorClient.Components.Routes).Assembly);

// Diagnostic endpoint — returns the last N journal entries so we can see
// what speech the server believes it produced. Useful for debugging the
// speech pipeline when Orca isn't announcing changes. Local-only; gated
// behind --enable-diag to keep it off in shipped builds.
if (args.Contains("--enable-diag") || app.Environment.IsDevelopment())
{
    app.MapGet("/diag/journal", (AccessibleTrader.Core.Services.IJournalService journal) =>
    {
        var snapshot = journal.Snapshot();
        var recent = snapshot
            .Reverse()
            .Take(100)
            .Select(e => new { time = e.Timestamp, kind = e.Kind.ToString(), source = e.Source, text = e.Text })
            .ToList();
        return Results.Json(recent);
    });
}

// Kick off the same startup sequence MAUI's MainPage constructor runs
// (line 54 of MainPage.xaml.cs). Without it, DataService._isInitialized
// stays false and every LoadSymbolsAsync silently returns an empty list,
// which presents as empty Symbol dropdowns and no chart data despite the
// provider dropdown looking populated.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        var startup = app.Services.GetRequiredService<IAppStartupService>();
        var log = app.Services.GetRequiredService<ILogger<Program>>();
        try
        {
            await startup.InitializeAsync().ConfigureAwait(false);
            log.LogInformation("AppStartupService.InitializeAsync completed.");

            // Remap Firefox-reserved Ctrl+Shift+letter chords (drawing tools,
            // detailed-point-summary) to Alt+Shift+letter so they actually
            // reach our keydown handler instead of being eaten by browser
            // chrome. Runs after AppStartupService so the ShortcutManager
            // is fully populated.
            var shortcuts = app.Services.GetRequiredService<IShortcutManager>();
            WebHostShortcutRemap.ApplyBrowserHostOverrides(shortcuts, log);

            // Demo: the curated stock/forex provider (Twelve Data) requires an API
            // key. Provider configuration is lazy (first data fetch), but
            // RefreshSymbolsAsync gates on IsConfigured *before* that fetch — so
            // without a warm-up the provider shows the "API key required" sentinel
            // and no symbols. Warm it here, AFTER init (providers are loaded and the
            // seeded key is in the store), so it is configured and its symbol lists
            // are cached before any visitor selects Stocks/Forex.
            if (demoMode)
            {
                try
                {
                    var data = app.Services.GetRequiredService<IDataService>();
                    await data.LoadSymbolsAsync("Stock", "Twelve Data").ConfigureAwait(false);
                    await data.LoadSymbolsAsync("Forex", "Twelve Data").ConfigureAwait(false);
                    log.LogInformation("Demo: Twelve Data provider warmed (Stock + Forex).");
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Demo: Twelve Data warm-up failed.");
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AppStartupService.InitializeAsync failed — providers will be unavailable.");
        }
    });
});

if (autoLaunch)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services
            .GetService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            ?.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
            ?.Addresses;
        var url = addresses?.FirstOrDefault() ?? "http://localhost:5000";
        OpenBrowser(url);
    });
}

// Demo stocks: seed the Twelve Data key from an environment variable so the demo can
// chart AAPL/TSLA/NVDA/SPY/EUR-USD without the key ever landing in source control.
// Crypto (Bitstamp) needs no key. The whitelist + caching keep us inside the free tier.
if (demoMode)
{
    var tdKey = Environment.GetEnvironmentVariable("DEMO_TWELVEDATA_APIKEY");
    if (!string.IsNullOrWhiteSpace(tdKey))
    {
        try
        {
            var apiKeys = app.Services.GetRequiredService<IApiKeyService>();
            await apiKeys.SaveKeyAsync(new ApiKeyConfig(
                Provider:    "Twelve Data",
                Nickname:    "demo",
                ApiKey:      tdKey,
                ApiSecret:   "",
                Passphrase:  "",
                // "Spot" — the sub-type the symbol/data path looks the key up by
                // (GetKeyForProviderAsync matches on MarketType==subType, default
                // "Spot"). Seeding "Stock" here would never match → unconfigured.
                MarketType:  "Spot",
                Environment: "Live",
                IsActive:    true));
        }
        catch (Exception ex)
        {
            app.Services.GetService<ILogger<Program>>()?.LogWarning(ex, "Demo Twelve Data key seed failed.");
        }
    }
}

app.Run();

static void OpenBrowser(string url)
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        }
    }
    catch
    {
        // Best-effort. Headless container? SSH? User can paste the URL by hand.
    }
}

/// <summary>
/// Marker singleton consulted by demo-mode-aware components (introduced in
/// phase L7). Present at L1 so the type is referenceable and the CLI flag
/// is parsed, but no component branches on it yet.
/// </summary>
public sealed record WebHostDemoMode(bool IsDemo);
