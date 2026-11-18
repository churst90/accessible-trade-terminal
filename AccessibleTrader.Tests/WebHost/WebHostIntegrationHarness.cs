using System.Text.RegularExpressions;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public static WebApplicationFactory<WebHostDemoMode> HostedFactory(
        string dataRoot,
        string environment = "Production",
        Action<IServiceCollection>? configureTestServices = null)
        => new WebApplicationFactory<WebHostDemoMode>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Accounts:Enabled", "true");
            b.UseSetting("Accounts:DataRoot", dataRoot);
            b.UseSetting("Launch:Disabled", "true");
            if (configureTestServices != null)
                b.ConfigureTestServices(configureTestServices);
        });

    /// <summary>
    /// Full mode (local single-user desktop host, no flags). The tray applet
    /// and the local background monitor are removed — they shell out to
    /// D-Bus / notify-send / speech tools, which a test box may not have and
    /// a test run must not disturb. Everything else is the real pipeline.
    /// </summary>
    /// <summary>
    /// The DEMO head — <c>--demo</c>, served under <c>/app/</c>. Boots through the
    /// <c>Demo:Enabled</c> configuration switch added 2026-09-05, because a factory cannot pass
    /// argv and until then no test could boot this head at all.
    /// </summary>
    public static WebApplicationFactory<WebHostDemoMode> DemoFactory(string environment = "Production")
        => new WebApplicationFactory<WebHostDemoMode>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Demo:Enabled", "true");
            b.UseSetting("Launch:Disabled", "true");
        });

    /// <summary>
    /// The Full-mode head served under an arbitrary prefix through the <c>PathBase</c>
    /// override — the configuration the browser harness runs in.
    /// </summary>
    public static WebApplicationFactory<WebHostDemoMode> FullFactoryUnder(string pathBase, string environment = "Production")
        => FullFactory(environment).WithWebHostBuilder(b => b.UseSetting("PathBase", pathBase));

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

    /// <summary>
    /// Pulls a hidden input's value out of a page (used for the registration fill-time
    /// stamp as well as remedy fields).
    /// </summary>
    public static string ExtractHiddenValue(string html, string name)
    {
        var m = Regex.Match(html,
            $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.True(m.Success, $"page contains no hidden input named {name}");
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        string password,
        string honeypot = "",
        TestTimeProvider? clock = null)
    {
        var resp = await client.GetAsync("/terminal/account/register");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(html);
        var fillStamp = ExtractHiddenValue(html, "FillTimeStamp");

        // The real form is filled by a person, so move past the three-second floor. A
        // mutable clock keeps the integration suite fast; a factory without one gets the
        // honest wait instead of a forged stamp.
        if (clock != null) clock.Advance(TimeSpan.FromSeconds(3.1));
        else await Task.Delay(TimeSpan.FromSeconds(3.1));

        return await client.PostAsync("/terminal/account/register", Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", password),
            ("Input.ConfirmPassword", password),
            ("Website", honeypot),
            ("FillTimeStamp", fillStamp)));
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
/// Snapshots the process-wide <c>PluginHostServices</c> bridges and puts them back.
///
/// <para>
/// Booting <c>Program.cs</c> assigns <c>HttpClientFactory</c>, <c>ApiKeys</c> and
/// <c>SecurityEvents</c> — the last two since 2026-08-27, when the fact that they were
/// never assigned on this head was fixed. Those are process-wide statics, so a WebHost
/// test that boots and does not put them back leaves the REAL credential bridge installed
/// for the rest of the run: every later provider test that relies on <c>Configure</c>-set
/// credentials then asks the real (empty) store instead and gets "no active API key
/// configured". That is exactly what happened — 20 provider tests went red on the first
/// full-suite run after the bridge landed, with no relationship to anything they touch.
/// </para>
///
/// <para>
/// The <c>ProviderCredentialBridge</c> collection stops these classes running in PARALLEL
/// with the provider tests; this stops them leaking into the ones that run AFTER. Both are
/// needed, and neither substitutes for the other.
/// </para>
/// </summary>
public sealed class PluginBridgeScope : IDisposable
{
    private readonly AccessibleTrader.Sdk.Services.IPluginHttpClientFactory? _httpClientFactory;
    private readonly AccessibleTrader.Sdk.Services.IApiKeyCheckout? _apiKeys;
    private readonly AccessibleTrader.Sdk.Services.ISecurityEventLog? _securityEvents;
    private readonly AccessibleTrader.Sdk.Services.IPluginSecureStorage? _secureStorage;

    public PluginBridgeScope()
    {
        _httpClientFactory = AccessibleTrader.Sdk.Services.PluginHostServices.HttpClientFactory;
        _apiKeys = AccessibleTrader.Sdk.Services.PluginHostServices.ApiKeys;
        _securityEvents = AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents;
        _secureStorage = AccessibleTrader.Sdk.Services.PluginHostServices.SecureStorage;
    }

    public void Dispose()
    {
        AccessibleTrader.Sdk.Services.PluginHostServices.HttpClientFactory = _httpClientFactory;
        AccessibleTrader.Sdk.Services.PluginHostServices.ApiKeys = _apiKeys;
        AccessibleTrader.Sdk.Services.PluginHostServices.SecurityEvents = _securityEvents;
        AccessibleTrader.Sdk.Services.PluginHostServices.SecureStorage = _secureStorage;
    }
}

/// <summary>
/// A hand-driven clock. Registration's fill-time gate and the reset-email throttle both
/// read <see cref="TimeProvider"/>, so tests move time instead of sleeping.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public TestTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }
}

/// <summary>Every account email a test host "sent", and whether sends succeed.</summary>
public sealed class RecordingAccountEmailSender : IAccountEmailSender
{
    private readonly object _gate = new();

    public bool IsConfigured { get; set; } = true;
    public bool Succeed { get; set; } = true;
    public List<RecordedAccountEmail> Sent { get; } = new();

    /// <summary>When set, every send is recorded at once and then waits for this task — a
    /// stand-in for a slow SMTP round trip.</summary>
    public Task? Hold { get; set; }

    public async Task<bool> SendAsync(
        string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Sent.Add(new RecordedAccountEmail(toAddress, subject, plainTextBody));
        }
        if (Hold is { } hold) await hold;
        return Succeed;
    }

    public IReadOnlyList<RecordedAccountEmail> Snapshot()
    {
        lock (_gate) return Sent.ToList();
    }
}

public sealed record RecordedAccountEmail(string To, string Subject, string Body);

/// <summary>
/// One hosted-mode WebHost per test class: its own temp data root (auth.db,
/// DataProtection keys, per-user dirs all land there and are deleted after),
/// and — importantly — its own rate-limiter state, so each class has the full
/// 10-POST auth budget.
/// </summary>
public sealed class HostedWebHostFixture : IDisposable
{
    // Declared FIRST so it snapshots before the factory can boot and restores after it is
    // gone. See PluginBridgeScope for what happens when a WebHost test does not do this.
    private readonly PluginBridgeScope _bridges = new();

    public string DataRoot { get; } = TestTemp.NewDir("att-webhost-int-");
    public TestTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public WebApplicationFactory<WebHostDemoMode> Factory { get; }

    public HostedWebHostFixture()
    {
        Factory = WebHostIntegration.HostedFactory(DataRoot, configureTestServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public void Dispose()
    {
        Factory.Dispose();
        _bridges.Dispose();
        try { Directory.Delete(DataRoot, recursive: true); } catch { }
    }
}

/// <summary>
/// The same host with a recording account-email sender and a hand-driven clock. Every
/// test using this fixture is offline: the recording sender never opens a network socket.
/// </summary>
public sealed class HostedEmailWebHostFixture : IDisposable
{
    private readonly PluginBridgeScope _bridges = new();

    public string DataRoot { get; } = TestTemp.NewDir("att-webhost-email-");
    public TestTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public RecordingAccountEmailSender Email { get; } = new();
    public WebApplicationFactory<WebHostDemoMode> Factory { get; }

    public HostedEmailWebHostFixture()
    {
        Factory = WebHostIntegration.HostedFactory(DataRoot, configureTestServices: services =>
        {
            services.RemoveAll<IAccountEmailSender>();
            services.AddSingleton<IAccountEmailSender>(Email);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public void Dispose()
    {
        Factory.Dispose();
        _bridges.Dispose();
        try { Directory.Delete(DataRoot, recursive: true); } catch { }
    }
}
