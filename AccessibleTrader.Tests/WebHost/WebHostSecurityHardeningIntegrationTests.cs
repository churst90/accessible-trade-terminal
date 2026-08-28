using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The half of the 2026-08-27 hosted-security batch that only a real request can settle:
/// where an audit event actually lands, what a rejected request actually says, and whether
/// <c>/Error</c> and <c>/diag/journal</c> actually answer.
///
/// <para>
/// Its own factory and data root, not the shared <see cref="HostedWebHostFixture"/> — one
/// of these deliberately exhausts the auth rate-limit budget, and the audit-log assertion
/// needs a data root nothing else has written to.
/// </para>
/// </summary>
// In the ProviderCredentialBridge collection: booting Program.cs now assigns
// PluginHostServices.ApiKeys and .SecurityEvents (they were null on this head until
// 2026-08-27, which is the defect that was fixed). Those are process-wide statics, so a
// host boot racing a provider test's FakeApiKeyCheckout.Install silently swaps the fake
// out from under it — 19 provider tests went red on the first full-suite run after the
// bridge landed. The collection serialises every class that touches that static.
[Collection("ProviderCredentialBridge")]
public sealed class WebHostSecurityHardeningIntegrationTests : IDisposable
{
    // Declared FIRST so it snapshots before the factory boots — see PluginBridgeScope.
    private readonly PluginBridgeScope _bridges = new();
    private readonly string _dataRoot = TestTemp.NewDir("att-sec-hardening-int-");
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<WebHostDemoMode> _factory;

    private const string Password = "Correct-h0rse-battery";

    public WebHostSecurityHardeningIntegrationTests()
    {
        _factory = WebHostIntegration.HostedFactory(_dataRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _bridges.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { }
    }

    // ── TODO:5476 — every audit event landed in users/anon ─────────────────────

    /// <summary>
    /// A sign-in is recorded from a Razor Page. A Razor Page request is not a Blazor
    /// circuit, and <c>ICurrentUser</c> was only ever populated by the circuit handler — so
    /// the audit sink resolved <c>{dataRoot}/users/anon</c> and every user's sign-ins,
    /// failures, lockouts, 2FA changes and password resets pooled there, with email address
    /// and client IP, in a directory whose name asserts it holds no user data and which the
    /// pruner skips.
    ///
    /// <para>
    /// This is the demonstration for the whole finding: the file has to appear under the
    /// signing-in user's own id, and nothing may appear under <c>anon</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_sign_in_is_audited_under_the_users_own_directory_not_anon()
    {
        var user = await WebHostIntegration.SeedUserAsync(_factory, "audit-scope@example.test", Password);
        using var client = WebHostIntegration.NewClient(_factory);

        var login = await WebHostIntegration.LoginAsync(client, "audit-scope@example.test", Password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var mine = Path.Combine(_dataRoot, "users", user.Id, "SecurityEvents");
        var pooled = Path.Combine(_dataRoot, "users", "anon", "SecurityEvents");

        Assert.True(Directory.Exists(mine),
            $"no audit directory at {mine}. Every authentication event is written from a Razor "
            + "Page, so if ICurrentUser does not resolve there the events land in the shared "
            + "anon slot instead of this user's own.");

        var written = Directory.GetFiles(mine, "security-events-*.jsonl");
        Assert.NotEmpty(written);
        Assert.Contains("AuthLoginSucceeded", await File.ReadAllTextAsync(written[0]), StringComparison.Ordinal);

        Assert.False(Directory.Exists(pooled),
            $"authentication events are still being pooled at {pooled}, which is the defect: "
            + "one shared file holding every account's email addresses and client IPs.");
    }

    // ── TODO:5492 — a non-local returnUrl 500'd into a page that did not exist ──

    /// <summary>
    /// <c>LocalRedirectResult</c> validates at result-EXECUTION time by throwing, so a
    /// query-string <c>returnUrl</c> pointing off-site produced a *successful* sign-in (the
    /// cookie is issued first) followed by an unhandled exception — a one-link denial of the
    /// login page for anyone who can get a user to click it.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("//example.com/")]
    [InlineData("/\\example.com/")]
    public async Task A_non_local_returnUrl_still_signs_in_and_lands_locally(string returnUrl)
    {
        string email = $"ret-{Guid.NewGuid():N}@example.test";
        await WebHostIntegration.SeedUserAsync(_factory, email, Password);
        using var client = WebHostIntegration.NewClient(_factory);

        string url = "/terminal/account/login?returnUrl=" + Uri.EscapeDataString(returnUrl);
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, url);
        var resp = await client.PostAsync(url, WebHostIntegration.Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", Password)));

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.True(WebHostIntegration.SetsAuthCookie(resp), "the sign-in itself must still succeed");

        var location = resp.Headers.Location!.ToString();
        Assert.DoesNotContain("example.com", location, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("/terminal/", location, StringComparison.Ordinal);
    }

    /// <summary>The vacuity control: a legitimate local returnUrl must still be honoured.</summary>
    [Fact]
    public async Task A_local_returnUrl_is_still_honoured()
    {
        string email = $"ret-ok-{Guid.NewGuid():N}@example.test";
        await WebHostIntegration.SeedUserAsync(_factory, email, Password);
        using var client = WebHostIntegration.NewClient(_factory);

        const string wanted = "/terminal/account/security";
        string url = "/terminal/account/login?returnUrl=" + Uri.EscapeDataString(wanted);
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, url);
        var resp = await client.PostAsync(url, WebHostIntegration.Form(
            ("__RequestVerificationToken", token),
            ("Input.Email", email),
            ("Input.Password", Password)));

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal(wanted, resp.Headers.Location!.ToString());
    }

    /// <summary>
    /// The route <c>UseExceptionHandler</c> points at had no endpoint at all — it matched
    /// only the Blazor fallback, which on this head carries <c>RequireAuthorization()</c>.
    /// It must exist, be anonymous, and say something a screen reader will announce.
    /// </summary>
    [Fact]
    public async Task The_error_route_exists_and_is_anonymous()
    {
        using var client = WebHostIntegration.NewClient(_factory);   // never signed in

        var resp = await client.GetAsync("/terminal/Error");

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", body, StringComparison.Ordinal);
        Assert.Contains("Something went wrong", body, StringComparison.Ordinal);
        // Not the Blazor fallback's "Page not found", and not a redirect to login.
        Assert.DoesNotContain("Page not found", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── TODO:5506 — the 429 was empty and had no Retry-After ───────────────────

    /// <summary>
    /// Exhausts the auth tier on purpose (hence the dedicated factory) and then reads what
    /// the refused caller is actually given. It used to be a zero-length body and no header.
    /// </summary>
    [Fact]
    public async Task A_rate_limited_request_carries_Retry_After_and_an_announceable_body()
    {
        using var client = WebHostIntegration.NewClient(_factory);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        HttpResponseMessage? rejected = null;
        for (int attempt = 0; attempt <= AccessibleTrader.WebHost.Services.AuthRateLimitPolicy.AuthPermitLimit; attempt++)
        {
            var resp = await client.PostAsync("/terminal/account/login",
                WebHostIntegration.Form(("Input.Email", $"x{attempt}@example.test"), ("Input.Password", "nope")));
            if (resp.StatusCode == HttpStatusCode.TooManyRequests) { rejected = resp; break; }
        }

        Assert.NotNull(rejected);

        Assert.True(rejected!.Headers.TryGetValues("Retry-After", out var retryAfter),
            "a 429 with no Retry-After leaves the caller no way to know the refusal is temporary");
        Assert.True(int.Parse(retryAfter!.First()) > 0);

        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", body, StringComparison.Ordinal);
        Assert.Contains("try again", body, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// <c>/diag/journal</c> is only mapped under <c>--enable-diag</c> or Development, and a
/// <c>WebApplicationFactory</c> cannot pass command-line args — so this class runs the host
/// in Development, alone, to exercise the endpoint the other classes cannot reach.
/// </summary>
// In the ProviderCredentialBridge collection: booting Program.cs now assigns
// PluginHostServices.ApiKeys and .SecurityEvents (they were null on this head until
// 2026-08-27, which is the defect that was fixed). Those are process-wide statics, so a
// host boot racing a provider test's FakeApiKeyCheckout.Install silently swaps the fake
// out from under it — 19 provider tests went red on the first full-suite run after the
// bridge landed. The collection serialises every class that touches that static.
[Collection("ProviderCredentialBridge")]
public sealed class WebHostDiagJournalIntegrationTests : IDisposable
{
    // Declared FIRST so it snapshots before the factory boots — see PluginBridgeScope.
    private readonly PluginBridgeScope _bridges = new();
    private readonly string _dataRoot = TestTemp.NewDir("att-diag-journal-int-");
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<WebHostDemoMode> _factory;

    private const string Password = "Correct-h0rse-battery";

    public WebHostDiagJournalIntegrationTests()
    {
        _factory = WebHostIntegration.HostedFactory(_dataRoot, environment: "Development");
    }

    public void Dispose()
    {
        _factory.Dispose();
        _bridges.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// <c>IJournalService</c> is Scoped with an instance ring buffer, and a minimal-API
    /// endpoint resolves the REQUEST scope — so the endpoint constructed a fresh empty
    /// journal on every call and could only ever return <c>[]</c>, in every mode, while its
    /// own comment warned about a leak the code was incapable of. The existing guard asserts
    /// the auth redirect only, so it stayed green throughout.
    ///
    /// <para>
    /// Journaling here goes through a scope with <c>ICurrentUser</c> seeded the way the
    /// circuit handler seeds it, which is the situation the endpoint is meant to report on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Diag_journal_returns_what_the_users_own_circuit_recorded()
    {
        var user = await WebHostIntegration.SeedUserAsync(_factory, "diag@example.test", Password);
        using var client = WebHostIntegration.NewClient(_factory);
        Assert.Equal(HttpStatusCode.Found,
            (await WebHostIntegration.LoginAsync(client, "diag@example.test", Password)).StatusCode);

        const string spoken = "Bitcoin 64,000 dollars, up 2 percent";
        using (var scope = _factory.Services.CreateScope())
        {
            // Exactly what WebHostBrowserCircuitHandler.OnCircuitOpenedAsync does.
            ((CurrentUser)scope.ServiceProvider.GetRequiredService<ICurrentUser>()).Set(user.Id);
            scope.ServiceProvider
                .GetRequiredService<AccessibleTrader.Core.Services.IJournalService>()
                .AddSpeech(spoken);
        }

        var json = await client.GetStringAsync("/terminal/diag/journal");

        Assert.Contains(spoken, json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The isolation half, and the reason a singleton journal was the wrong fix: the journal
    /// is a transcript of everything spoken to a user — positions, balances, alerts — so one
    /// signed-in user must never be able to read another's.
    /// </summary>
    [Fact]
    public async Task Diag_journal_never_returns_another_users_transcript()
    {
        var alice = await WebHostIntegration.SeedUserAsync(_factory, "diag-alice@example.test", Password);
        await WebHostIntegration.SeedUserAsync(_factory, "diag-bob@example.test", Password);

        const string alicesPosition = "Position: 3 BTC long, unrealised 4,120 dollars";
        using (var scope = _factory.Services.CreateScope())
        {
            ((CurrentUser)scope.ServiceProvider.GetRequiredService<ICurrentUser>()).Set(alice.Id);
            scope.ServiceProvider
                .GetRequiredService<AccessibleTrader.Core.Services.IJournalService>()
                .AddSpeech(alicesPosition);
        }

        using var bob = WebHostIntegration.NewClient(_factory);
        Assert.Equal(HttpStatusCode.Found,
            (await WebHostIntegration.LoginAsync(bob, "diag-bob@example.test", Password)).StatusCode);

        var json = await bob.GetStringAsync("/terminal/diag/journal");

        Assert.DoesNotContain(alicesPosition, json, StringComparison.Ordinal);
    }
}
