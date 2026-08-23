using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// End-to-end HTTP coverage of the hosted-accounts WebHost: real Program.cs,
/// real middleware order, real Razor Pages handlers, real cookies. Until
/// 2026-08-22 none of these paths — Login/Register OnPostAsync, the
/// authorization redirect, the antiforgery gate — had ever executed in a test.
///
/// One factory (and so one rate-limiter window and one temp data root) for the
/// whole class. Each test uses its own unique email; HTTP login/register POSTs
/// are budgeted well below the limiter's 10-per-window cap, with users seeded
/// through UserManager where the POST itself is not the thing under test.
/// </summary>
public sealed class WebHostHostedAccountsIntegrationTests
    : IClassFixture<HostedWebHostFixture>
{
    private readonly HostedWebHostFixture _host;

    public WebHostHostedAccountsIntegrationTests(HostedWebHostFixture host) => _host = host;

    private const string Password = "Correct-h0rse-battery";

    [Fact]
    public async Task Anonymous_visitor_is_redirected_to_the_login_page()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.StartsWith("/terminal/account/login", resp.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Login_page_serves_a_form_with_antiforgery_and_security_headers()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/account/login");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        WebHostIntegration.ExtractAntiforgeryToken(html); // asserts presence

        Assert.Equal("nosniff", resp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resp.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'",
            resp.Headers.GetValues("Content-Security-Policy").Single());
        // Client base address is https, so the HSTS branch must fire too.
        Assert.True(resp.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Register_creates_the_account_signs_in_and_redirects_into_the_app()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.RegisterAsync(client, "reg-happy@example.test", Password);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/terminal/", resp.Headers.Location!.OriginalString);
        Assert.True(WebHostIntegration.SetsAuthCookie(resp),
            "successful registration must issue the __Host-att.auth session cookie");

        // The cookie actually authorizes: an endpoint behind RequireAuthorization serves.
        var vapid = await client.GetAsync("/terminal/push/vapid-public-key");
        Assert.Equal(HttpStatusCode.OK, vapid.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await vapid.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Register_honeypot_mimics_success_but_creates_no_account()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.RegisterAsync(
            client, "reg-bot@example.test", Password, honeypot: "https://spam.example");

        // Same redirect a real registration gets — the bot learns nothing.
        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.False(WebHostIntegration.SetsAuthCookie(resp),
            "the honeypot branch must not sign anyone in");

        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.Null(await users.FindByEmailAsync("reg-bot@example.test"));
    }

    [Fact]
    public async Task Register_with_duplicate_email_reports_a_generic_error_not_an_oracle()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "reg-dupe@example.test", Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.RegisterAsync(client, "reg-dupe@example.test", Password);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        // The apostrophe in "couldn't" is HTML-encoded by Razor; match around it.
        Assert.Contains("create your account with those details", html);
        Assert.DoesNotContain("already taken", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_with_wrong_password_shows_the_generic_error_and_no_cookie()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "login-wrong@example.test", Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.LoginAsync(client, "login-wrong@example.test", "not-the-password");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Email or password is incorrect.", await resp.Content.ReadAsStringAsync());
        Assert.False(WebHostIntegration.SetsAuthCookie(resp));
    }

    [Fact]
    public async Task Login_with_valid_credentials_issues_the_cookie_and_authorizes_requests()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "login-happy@example.test", Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await WebHostIntegration.LoginAsync(client, "login-happy@example.test", Password);

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Equal("/terminal/", resp.Headers.Location!.OriginalString);
        Assert.True(WebHostIntegration.SetsAuthCookie(resp));

        var vapid = await client.GetAsync("/terminal/push/vapid-public-key");
        Assert.Equal(HttpStatusCode.OK, vapid.StatusCode);
    }

    [Fact]
    public async Task Login_post_without_an_antiforgery_token_is_rejected()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.PostAsync("/terminal/account/login", WebHostIntegration.Form(
            ("Input.Email", "whoever@example.test"),
            ("Input.Password", Password)));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "logout@example.test", Password);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var login = await WebHostIntegration.LoginAsync(client, "logout@example.test", Password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/logout");
        var logout = await client.PostAsync("/terminal/account/logout", WebHostIntegration.Form(
            ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        Assert.Equal("/terminal/account/login", logout.Headers.Location!.OriginalString);

        // The session is really gone, not just redirected once.
        var after = await client.GetAsync("/terminal/push/vapid-public-key");
        Assert.Equal(HttpStatusCode.Found, after.StatusCode);
        Assert.StartsWith("/terminal/account/login", after.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Full_mode_alert_endpoints_do_not_exist_on_the_hosted_head()
    {
        // /alerts/recent is the LOCAL single-user surface (unauthenticated by
        // design, backed by an in-memory buffer). On the hosted head it must
        // not be mapped: no endpoint exists there at all, so an anonymous GET
        // gets a 404 — never the alerts page.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/alerts/recent");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.DoesNotContain("Recent alerts", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Diag_journal_is_not_mapped_outside_development()
    {
        // The factory boots with the Production environment and no --enable-diag,
        // so the diagnostics endpoint must not exist (anonymous callers just get
        // the login redirect from the Blazor catch-all, never journal JSON).
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/diag/journal");

        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Diag_journal_requires_a_signed_in_user_when_it_is_mapped()
    {
        // Even when the endpoint IS mapped (Development / --enable-diag), the
        // hosted head must never serve it anonymously: the journal is a
        // transcript of everything spoken to a user. This boots a separate
        // Development-environment hosted factory so the endpoint exists, then
        // asserts an anonymous GET is turned away at the auth layer.
        var root = System.IO.Directory.CreateTempSubdirectory("att-diag-").FullName;
        try
        {
            using var factory = WebHostIntegration.HostedFactory(root, environment: "Development");
            using var client = WebHostIntegration.NewClient(factory);

            var resp = await client.GetAsync("/terminal/diag/journal");

            Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
            // Cookie auth answers an unauthenticated GET with the login redirect.
            Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
            Assert.StartsWith("/terminal/account/login", resp.Headers.Location!.AbsolutePath);
        }
        finally
        {
            try { System.IO.Directory.Delete(root, recursive: true); } catch { /* temp dir */ }
        }
    }
}
