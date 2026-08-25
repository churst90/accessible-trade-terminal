using System.Text.RegularExpressions;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Boots the REAL WebHost — <c>Program.cs</c>, full DI graph, middleware order,
/// routing — inside a <see cref="WebApplicationFactory{TEntryPoint}"/> test
/// server. This is the missing layer above the options-object tests
/// (<c>HostedAccountsAuthPolicyTests</c> proves configuration; these prove a
/// request is actually authorized, a handler actually runs, a cookie is
/// actually issued).
///
/// <para>
/// The entry-point marker is <see cref="WebHostDemoMode"/> — a public type
/// declared in Program.cs, so its assembly IS the WebHost entry assembly.
/// (The generated <c>Program</c> class is internal; using a public marker
/// avoids leaning on InternalsVisibleTo for the factory's type argument.)
/// </para>
///
/// <para>
/// Hosted mode is enabled through configuration (<c>Accounts:Enabled</c> /
/// <c>Accounts:DataRoot</c>) because a factory cannot pass command-line args;
/// Program.cs reads both. <c>Launch:Disabled</c> suppresses the browser
/// auto-launch the same way (added for exactly this reason). The client base
/// address is HTTPS because the auth cookie is <c>SecurePolicy.Always</c> —
/// over plain http the cookie would silently never be stored and every
/// "signed in" assertion would be testing nothing.
/// </para>
/// </summary>
internal static class WebHostIntegration
{
    /// <summary>Hosted multi-user mode (--accounts equivalent). Requests must use the /terminal path base.</summary>
    public static WebApplicationFactory<WebHostDemoMode> HostedFactory(string dataRoot, string environment = "Production")
        => new WebApplicationFactory<WebHostDemoMode>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Accounts:Enabled", "true");
            b.UseSetting("Accounts:DataRoot", dataRoot);
            b.UseSetting("Launch:Disabled", "true");
        });

    /// <summary>
    /// Full mode (local single-user desktop host, no flags). The tray applet
    /// and the local background monitor are removed — they shell out to
    /// D-Bus / notify-send / speech tools, which a test box may not have and
    /// a test run must not disturb. Everything else is the real pipeline.
    /// </summary>
    public static WebApplicationFactory<WebHostDemoMode> FullFactory(string environment = "Production")
        => new WebApplicationFactory<WebHostDemoMode>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Launch:Disabled", "true");
            b.ConfigureTestServices(services =>
            {
                var desktopOnly = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                             && (d.ImplementationType == typeof(AccessibleTrader.WebHost.Services.LocalBackgroundMonitor)
                              || d.ImplementationType == typeof(AccessibleTrader.WebHost.Services.Tray.DesktopTrayService)))
                    .ToList();
                foreach (var d in desktopOnly) services.Remove(d);
            });
        });

    public static HttpClient NewClient(WebApplicationFactory<WebHostDemoMode> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    // ── form plumbing ────────────────────────────────────────────────────────

    public static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    /// <summary>
    /// Pulls the <c>__RequestVerificationToken</c> hidden input out of a page.
    /// Every Razor Pages form here auto-emits one (the FormTagHelper is active
    /// via _ViewImports), and the POST is rejected without it.
    /// </summary>
    public static string ExtractAntiforgeryToken(string html)
    {
        var m = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "page contains no antiforgery token input");
        return m.Groups[1].Value;
    }

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        return ExtractAntiforgeryToken(await resp.Content.ReadAsStringAsync());
    }

    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string password, string honeypot = "")
    {
        var token = await GetAntiforgeryTokenAsync(client, "/terminal/account/register");
        return await client.PostAsync("/terminal/account/register", Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", password),
            ("Input.ConfirmPassword", password),
            ("Website", honeypot)));
    }

    public static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/terminal/account/login");
        return await client.PostAsync("/terminal/account/login", Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", password)));
    }

    /// <summary>
    /// Creates a user directly through the real <see cref="UserManager{TUser}"/>
    /// (same DI scope the app itself uses). Seeding this way instead of via the
    /// register page keeps HTTP login/register POSTs — which the auth rate
    /// limiter caps at 10 per 5-minute window per IP, and the TestServer's IP
    /// is one shared "unknown" — for the assertions that need them.
    /// </summary>
    public static async Task<AppUser> SeedUserAsync(
        WebApplicationFactory<WebHostDemoMode> factory, string email, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, CreatedUtc = DateTime.UtcNow, Tier = "free" };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded,
            "seed user creation failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        return user;
    }

    /// <summary>Whether an auth session cookie was issued on this response.</summary>
    public static bool SetsAuthCookie(HttpResponseMessage resp)
        => resp.Headers.TryGetValues("Set-Cookie", out var cookies)
           && cookies.Any(c => c.StartsWith("__Host-att.auth=", StringComparison.Ordinal)
                            && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One hosted-mode WebHost per test class: its own temp data root (auth.db,
/// DataProtection keys, per-user dirs all land there and are deleted after),
/// and — importantly — its own rate-limiter state, so each class has the full
/// 10-POST auth budget.
/// </summary>
public sealed class HostedWebHostFixture : IDisposable
{
    public string DataRoot { get; } = Directory.CreateTempSubdirectory("att-webhost-int-").FullName;
    public WebApplicationFactory<WebHostDemoMode> Factory { get; }

    public HostedWebHostFixture()
    {
        Factory = WebHostIntegration.HostedFactory(DataRoot);
    }

    public void Dispose()
    {
        Factory.Dispose();
        try { Directory.Delete(DataRoot, recursive: true); } catch { }
    }
}
