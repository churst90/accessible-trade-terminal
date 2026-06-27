using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.DataProtection;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost;
using AccessibleTrader.WebHost.Components;
using AccessibleTrader.WebHost.Services;

var builder = WebApplication.CreateBuilder(args);

// Multi-user web host: each Blazor circuit (browser connection) is its own DI scope,
// so per-visitor state services are registered Scoped (see ServiceCollectionExtensions
// + docs/WEBHOST_MULTI_USER_SCOPING.md). Validate that nothing captures a Scoped service
// in a Singleton (captive dependency) or resolves Scoped from the root provider —
// ValidateOnBuild fails fast at startup with the exact offenders so they can be fixed.
builder.Host.UseDefaultServiceProvider(o =>
{
    o.ValidateScopes = true;
    o.ValidateOnBuild = true;
});

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

// Per-circuit setup hook. Re-applies the Firefox Ctrl+Shift→Alt+Shift shortcut remap
// for each visitor — it used to run app-once, but IShortcutManager is now per-circuit
// (multi-user scoping), so the remap must run per circuit too.
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler,
    AccessibleTrader.WebHost.Services.WebHostBrowserCircuitHandler>();

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
// NOTE: pipeline init (AppStartupService.InitializeAsync), the shortcut remap, and the
// Twelve Data warm-up used to run ONCE here at app start. With per-circuit (Scoped)
// services they must run PER CIRCUIT instead — they are now invoked from
// MainLayout.OnInitializedAsync (which runs inside each visitor's DI scope). Resolving
// those Scoped services from the root provider here would throw under ValidateScopes.
// Only genuinely app-once work (e.g. seeding the shared Twelve Data key below) remains
// at app start.

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
