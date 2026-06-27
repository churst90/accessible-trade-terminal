using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost;
using AccessibleTrader.WebHost.Account;
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

// --accounts (or Accounts:Enabled=true) → hosted multi-user mode: ASP.NET Core Identity
// login + per-user persistence. OFF by default, so the local single-user and --demo modes
// (and the MAUI head) are completely unaffected. See docs/HOSTED_AUTH_PERSISTENCE_DESIGN.md.
bool accountsEnabled = args.Contains("--accounts")
    || builder.Configuration.GetValue<bool>("Accounts:Enabled");
string? accountsDataRoot = builder.Configuration["Accounts:DataRoot"];

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("AccessibleTrader.WebHost");

// Hosted accounts: persist the DataProtection keyring to disk so auth cookies and
// antiforgery tokens survive process restarts — otherwise every restart silently logs
// every user out and breaks in-flight form posts. Keys live under the accounts data root.
if (accountsEnabled)
{
    var keyRing = Path.Combine(
        string.IsNullOrWhiteSpace(accountsDataRoot)
            ? new WebHostPathService().AppDataDirectory
            : accountsDataRoot!,
        "dp-keys");
    Directory.CreateDirectory(keyRing);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRing));
}

builder.Services.AddAccessibleTraderWebHostServices();

builder.Services.AddSingleton(new WebHostDemoMode(demoMode));
// Central feature policy — three tiers (see DemoPolicy / HostMode):
//   Full   (no flags) — desktop + local web, everything on
//   Demo   (--demo)   — locked-down, whitelisted public taste
//   Hosted (--accounts) — full app MINUS desktop-only scripts / real-money trading /
//                         broker keys / AI analyst (paper trading + everything else ON)
var hostMode = accountsEnabled ? HostMode.Hosted
             : demoMode        ? HostMode.Demo
             :                    HostMode.Full;
builder.Services.AddSingleton(new DemoPolicy(hostMode));

// Abuse guard for the public hosted endpoint — the strategy doc names a rate-limiter a
// prerequisite before public exposure. Generous per-client-IP fixed window over HTTP
// requests (page loads, SignalR negotiates, auth form posts). The established per-circuit
// WebSocket is a single upgraded request, so normal use is unaffected; rapid circuit
// creation or registration floods from one IP get 429'd.
if (accountsEnabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 200,
                    Window = TimeSpan.FromSeconds(10),
                    QueueLimit = 0,
                }));
    });
}

// Per-circuit setup hook. Re-applies the Firefox Ctrl+Shift→Alt+Shift shortcut remap
// for each visitor — it used to run app-once, but IShortcutManager is now per-circuit
// (multi-user scoping), so the remap must run per circuit too.
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler,
    AccessibleTrader.WebHost.Services.WebHostBrowserCircuitHandler>();

// Hosted multi-user accounts (Identity + per-user persistence). Overrides the single-user
// path/cache defaults with per-circuit ones; only runs when explicitly enabled.
if (accountsEnabled)
    builder.Services.AddHostedAccounts(accountsDataRoot);

var app = builder.Build();

// Create the accounts (Identity) schema on first run.
if (accountsEnabled)
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AccessibleTrader.WebHost.Account.AuthDbContext>()
        .Database.EnsureCreated();
}

// One-time owner/admin seed: provision a pre-set account from env vars, BYPASSING the public
// password policy (a server-provisioned account, e.g. the site owner). Idempotent — only acts
// when the email doesn't already exist. The hash is set directly so the password validators
// never run; sign-in only verifies the hash, so any chosen password works for this account.
if (accountsEnabled)
{
    var seedEmail = Environment.GetEnvironmentVariable("ACCOUNTS_SEED_EMAIL");
    var seedPass  = Environment.GetEnvironmentVariable("ACCOUNTS_SEED_PASSWORD");
    if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPass))
    {
        using var seedScope = app.Services.CreateScope();
        var users = seedScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(seedEmail) is null)
        {
            var seedUser = new AppUser { UserName = seedEmail, Email = seedEmail, EmailConfirmed = true };
            seedUser.PasswordHash = seedScope.ServiceProvider
                .GetRequiredService<IPasswordHasher<AppUser>>()
                .HashPassword(seedUser, seedPass);
            await users.CreateAsync(seedUser);   // no password arg → skips the policy validators
        }
    }
}

// Hosted accounts run behind nginx (TLS terminated upstream). Honour X-Forwarded-Proto/For
// so the app knows requests are HTTPS (Secure-cookie policy + correct redirect URLs after
// login) and sees the real client IP for the rate limiter. nginx is the only upstream and
// is on loopback, so the proxy/network allow-lists are cleared to trust the forwarded values.
if (accountsEnabled)
{
    var fwd = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    fwd.KnownNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

// The public builds are reverse-proxied behind nginx under a subpath on the marketing
// site: --demo under /app/, hosted accounts under /terminal/. UsePathBase aligns every
// route, static asset, and the /_blazor SignalR endpoint with the base href set in
// App.razor. Must run first in the pipeline. (Deploy-only; kept local to this host.)
if (demoMode)
{
    app.UsePathBase("/app");
}
else if (accountsEnabled)
{
    app.UsePathBase("/terminal");
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

// Rate limiter + auth middleware run only when accounts are enabled. The limiter goes
// first so floods are shed before auth/Identity work (cheap brute-force/DoS guard).
if (accountsEnabled)
{
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();

var blazorApp = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    // The RCL holds every @page directive (Pages/Home.razor today). Without
    // AddAdditionalAssemblies the WebHost Router scans only this assembly and
    // would 404 on every route.
    .AddAdditionalAssemblies(typeof(AccessibleTrader.BlazorClient.Components.Routes).Assembly);

if (accountsEnabled)
{
    // The whole app requires a signed-in user; anonymous visitors are redirected to the
    // accessible /account/login page (the cookie LoginPath). The login/register/logout
    // Razor Pages are served separately and stay anonymous.
    blazorApp.RequireAuthorization();
    app.MapRazorPages();
}

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

// Stocks/forex market data (Twelve Data) needs a server-side key; crypto (Bitstamp) needs
// none. Seed it from an env var in BOTH the public demo AND hosted-accounts modes — hosted
// users can't add their own keys (the API-keys modal is gated off), so the server provides
// the shared market-data key. The key never lands in source control. (It is read-only
// market data, not a trading credential — hosted trading is paper-only.)
if (demoMode || accountsEnabled)
{
    var tdKey = Environment.GetEnvironmentVariable("TWELVEDATA_APIKEY")
                ?? Environment.GetEnvironmentVariable("DEMO_TWELVEDATA_APIKEY");
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
