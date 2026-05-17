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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
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
