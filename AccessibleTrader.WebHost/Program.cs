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

// Local background monitoring (HostMode.Full only): the server process outlives
// the browser tab, so alerts keep evaluating — heard through Orca/spd-say +
// notify-send + a notification sound. Registered only for Full because the
// singleton needs the singleton path service (hosted swaps it per-user), and
// because on hosted/demo the server has no speakers that reach the user anyway.
if (hostMode == HostMode.Full)
{
    // Shared recent-alerts list (unread/read/dismissed) feeding the tray's label and page,
    // plus the alert-snooze flag the tray sets and the monitor honours.
    builder.Services.AddSingleton<AccessibleTrader.WebHost.Services.RecentAlertsBuffer>();
    builder.Services.AddSingleton<AccessibleTrader.WebHost.Services.Tray.AlertSnooze>();
    // Per-circuit bridge: records browser-open alerts into the shared buffer too (the
    // monitor only covers browser-closed). Instantiated per circuit by the circuit handler.
    builder.Services.AddScoped<AccessibleTrader.WebHost.Services.InSessionAlertRecorder>();
    builder.Services.AddHostedService<AccessibleTrader.WebHost.Services.LocalBackgroundMonitor>();
    // Cross-platform panel tray (Linux verified; Windows/macOS best-effort). Gives a control
    // surface — reopen the UI, review alerts, silence, status, copy address, toggle
    // monitoring, quit — with the browser closed. Never registered on the hosted server.
    builder.Services.AddHostedService<AccessibleTrader.WebHost.Services.Tray.DesktopTrayService>();
}

// Hosted server-side alerts (HostMode.Hosted only): every registered user's
// saved alerts keep evaluating after their browser closes, delivered through
// their configured channels. Per-user scopes are seeded with ICurrentUser so
// the whole per-user stack resolves as if inside that user's circuit; users
// with a live circuit are skipped (their session owns delivery).
if (hostMode == HostMode.Hosted)
{
    var instanceRoot = string.IsNullOrWhiteSpace(accountsDataRoot)
        ? new WebHostPathService().AppDataDirectory
        : accountsDataRoot!;
    var usersRoot = Path.Combine(instanceRoot, "users");

    // Web Push plumbing: instance VAPID keys, per-user subscription files, and
    // the sender the monitor fans out through.
    builder.Services.AddSingleton(sp => new AccessibleTrader.WebHost.Services.Push.VapidKeyService(
        instanceRoot, sp.GetRequiredService<ILogger<AccessibleTrader.WebHost.Services.Push.VapidKeyService>>()));
    builder.Services.AddSingleton(sp => new AccessibleTrader.WebHost.Services.Push.PushSubscriptionStore(
        usersRoot, sp.GetRequiredService<ILogger<AccessibleTrader.WebHost.Services.Push.PushSubscriptionStore>>()));
    builder.Services.AddSingleton<AccessibleTrader.WebHost.Services.Push.HostedWebPushSender>();

    builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp =>
        new AccessibleTrader.WebHost.Services.HostedAlertMonitor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<DemoPolicy>(),
            usersRoot,
            sp.GetRequiredService<ILogger<AccessibleTrader.WebHost.Services.HostedAlertMonitor>>(),
            sp.GetRequiredService<AccessibleTrader.WebHost.Services.Push.HostedWebPushSender>()));
}

// Abuse guard for the public hosted endpoint — the strategy doc names a rate-limiter a
// prerequisite before public exposure. Two per-client-IP tiers (see AuthRateLimitPolicy):
// a generous general window over HTTP requests (page loads, SignalR negotiates), and a
// much stricter window on POSTs to the login/register pages so one IP can't brute-force
// credentials at page-load rates. The established per-circuit WebSocket is a single
// upgraded request, so normal use is unaffected.
if (accountsEnabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
            AuthRateLimitPolicy.GetPartition);
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

// One-off admin CLI: mint a password-reset link and exit WITHOUT starting Kestrel
// (mirrors the owner-seed one-off above). There is no outbound mail server, so the
// admin generates the reset token here and delivers the URL to the user through a
// trusted channel; the user sets their own new password on the ResetPassword page.
// Run as:  dotnet AccessibleTrader.WebHost.dll --accounts --reset-link user@example.com
if (accountsEnabled)
{
    var resetIdx = Array.IndexOf(args, "--reset-link");
    if (resetIdx >= 0)
    {
        var resetEmail = resetIdx + 1 < args.Length ? args[resetIdx + 1]?.Trim() : null;
        if (string.IsNullOrWhiteSpace(resetEmail))
        {
            Console.WriteLine("Usage: --accounts --reset-link <email>");
            return;
        }

        using var resetScope = app.Services.CreateScope();
        var resetUsers = resetScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var resetUser = await resetUsers.FindByEmailAsync(resetEmail);
        if (resetUser is null)
        {
            // No enumeration even at the CLI: same generic message whether or not the
            // account exists. (The admin can retry with the exact address on file.)
            Console.WriteLine("If that account exists, a reset link was generated.");
            return;
        }

        var resetToken = await resetUsers.GeneratePasswordResetTokenAsync(resetUser);
        // Public HTTPS base, including the /terminal PathBase. Configurable so a
        // differently-mounted host can still print a correct link.
        var resetBase = (builder.Configuration["Accounts:ResetLinkBaseUrl"]
                         ?? "https://trade.codyhurst.com/terminal").TrimEnd('/');
        var resetUrl = $"{resetBase}/account/resetpassword" +
                       $"?email={Uri.EscapeDataString(resetEmail)}" +
                       $"&token={Uri.EscapeDataString(resetToken)}";
        Console.WriteLine(resetUrl);
        return;
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
    fwd.KnownIPNetworks.Clear();
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

// Response security headers on every mode and every response (static assets
// included): CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy,
// Permissions-Policy, and HSTS on HTTPS requests. Runs after UseForwardedHeaders
// so IsHttps reflects the nginx-terminated TLS on the public deployments.
// Values + rationale live in SecurityHeadersPolicy.
app.Use(async (ctx, next) =>
{
    SecurityHeadersPolicy.Apply(ctx);
    await next();
});

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

    // ── Web Push endpoints (hosted only, authenticated) ──────────────────────
    // JSON POSTs from webPush.js ride the same auth cookie as the circuit;
    // subscriptions are stored per user, keyed by the Identity user id.
    static string? PushUserId(HttpContext ctx) =>
        ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    app.MapGet("/push/vapid-public-key",
        (AccessibleTrader.WebHost.Services.Push.VapidKeyService vapid) => Results.Text(vapid.PublicKey))
        .RequireAuthorization();

    app.MapPost("/push/subscribe", async (HttpContext ctx,
        AccessibleTrader.WebHost.Services.Push.PushSubscriptionStore store) =>
    {
        var userId = PushUserId(ctx);
        if (userId == null) return Results.Unauthorized();
        var subscription = await ctx.Request.ReadFromJsonAsync<AccessibleTrader.WebHost.Services.Push.StoredPushSubscription>();
        if (subscription == null || !store.Add(userId, subscription)) return Results.BadRequest();
        return Results.Ok();
    }).RequireAuthorization();

    app.MapPost("/push/unsubscribe", async (HttpContext ctx,
        AccessibleTrader.WebHost.Services.Push.PushSubscriptionStore store) =>
    {
        var userId = PushUserId(ctx);
        if (userId == null) return Results.Unauthorized();
        var subscription = await ctx.Request.ReadFromJsonAsync<AccessibleTrader.WebHost.Services.Push.StoredPushSubscription>();
        if (subscription == null || string.IsNullOrWhiteSpace(subscription.Endpoint)) return Results.BadRequest();
        store.Remove(userId, subscription.Endpoint);
        return Results.Ok();
    }).RequireAuthorization();
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

// Recent-alerts review page (HostMode.Full, local desktop). The tray's "Show recent
// alerts" opens this; each alert carries Mark-read / Dismiss buttons. Plain server-rendered
// HTML — no Blazor circuit — so it's reachable even in a freshly (re)opened browser after a
// crash. Antiforgery is disabled on the POSTs: local, single-user, same-origin only.
if (hostMode == HostMode.Full)
{
    static string RenderAlertsPage(AccessibleTrader.WebHost.Services.RecentAlertsBuffer buffer)
    {
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        var items = buffer.Snapshot();
        var sb = new System.Text.StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>Recent alerts — Accessible Trade Terminal</title></head><body>");
        sb.Append("<h1>Recent alerts</h1>");
        if (items.Count == 0)
        {
            sb.Append("<p>No recent alerts.</p>");
        }
        else
        {
            sb.Append("<form method=\"post\" action=\"/alerts/recent/read-all\"><button type=\"submit\">Mark all read</button></form><ul>");
            foreach (var a in items)
            {
                string when = a.FiredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                string sym = a.Symbol != null ? enc.Encode(a.Symbol) + ": " : "";
                bool unread = a.State == AccessibleTrader.WebHost.Services.RecentAlertState.Unread;
                sb.Append("<li><p><strong>").Append(sym).Append("</strong>").Append(enc.Encode(a.Text));
                sb.Append(" <span>(").Append(when).Append(", ").Append(unread ? "unread" : "read").Append(")</span></p>");
                if (unread)
                    sb.Append("<form method=\"post\" action=\"/alerts/recent/").Append(a.Id)
                      .Append("/read\"><button type=\"submit\">Mark as read</button></form>");
                sb.Append("<form method=\"post\" action=\"/alerts/recent/").Append(a.Id)
                  .Append("/dismiss\"><button type=\"submit\">Dismiss</button></form></li>");
            }
            sb.Append("</ul>");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    app.MapGet("/alerts/recent", (AccessibleTrader.WebHost.Services.RecentAlertsBuffer buffer) =>
        Results.Content(RenderAlertsPage(buffer), "text/html"));
    app.MapPost("/alerts/recent/read-all", (AccessibleTrader.WebHost.Services.RecentAlertsBuffer buffer) =>
        { buffer.MarkAllRead(); return Results.Redirect("/alerts/recent"); }).DisableAntiforgery();
    app.MapPost("/alerts/recent/{id}/read", (string id, AccessibleTrader.WebHost.Services.RecentAlertsBuffer buffer) =>
        { buffer.MarkRead(id); return Results.Redirect("/alerts/recent"); }).DisableAntiforgery();
    app.MapPost("/alerts/recent/{id}/dismiss", (string id, AccessibleTrader.WebHost.Services.RecentAlertsBuffer buffer) =>
        { buffer.Dismiss(id); return Results.Redirect("/alerts/recent"); }).DisableAntiforgery();
}

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
    // Each entry: the provider's Name, the env vars to read (first non-empty wins), and the
    // MarketType to store the key under — the sub-type the data path looks it up by
    // (GetKeyForProviderAsync matches MarketType == subType). Seeding "Stock" for Twelve Data
    // would never match → the provider stays unconfigured; FRED's sub-type is "Standard".
    var seeds = new[]
    {
        (Provider: "Twelve Data",
         EnvVars:  new[] { "TWELVEDATA_APIKEY", "DEMO_TWELVEDATA_APIKEY" },
         Market:   "Spot"),
        // FRED unlocks the Economic market (CPI, NFP, GDP, unemployment, fed funds, yields).
        // Read-only public research data; there is nothing to trade with it, so it is safe to
        // hold server-side and share across hosted users exactly like the Twelve Data key.
        (Provider: "FRED",
         EnvVars:  new[] { "FRED_APIKEY", "FRED_API_KEY" },
         Market:   "Standard"),
    };

    var seedLogger = app.Services.GetService<ILogger<Program>>();
    foreach (var seed in seeds)
    {
        var key = seed.EnvVars
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (string.IsNullOrWhiteSpace(key))
        {
            // DemoPolicy.HostedProviders is a static list, but whether these providers actually
            // WORK depends on the environment. An unseeded provider still appears in the hosted
            // dropdown and can only render "API key required" — with the API-keys modal gated off,
            // that is a dead end the user cannot clear. Say so at startup, where an operator can
            // see it, rather than leaving it to be discovered from the UI.
            seedLogger?.LogWarning(
                "No server-side key for {Provider}: set one of {EnvVars}. It is offered in the " +
                "hosted provider list but will render as \"API key required\", which a hosted " +
                "user cannot resolve.",
                seed.Provider, string.Join(" or ", seed.EnvVars));
            continue;
        }

        try
        {
            var apiKeys = app.Services.GetRequiredService<IApiKeyService>();
            await apiKeys.SaveKeyAsync(new ApiKeyConfig(
                Provider:    seed.Provider,
                Nickname:    "demo",
                ApiKey:      key,
                ApiSecret:   "",
                Passphrase:  "",
                MarketType:  seed.Market,
                Environment: "Live",
                IsActive:    true));
            seedLogger?.LogInformation("Seeded server-side API key for {Provider}.", seed.Provider);
        }
        catch (Exception ex)
        {
            seedLogger?.LogWarning(ex, "Server-side key seed failed for {Provider}.", seed.Provider);
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
