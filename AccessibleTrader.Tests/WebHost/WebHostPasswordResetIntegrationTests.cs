using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The admin-mediated password-reset flow over HTTP. The reset token is minted
/// through the same <see cref="UserManager{TUser}"/> the <c>--reset-link</c>
/// CLI uses (there is deliberately no outbound mail), then consumed by the
/// real ResetPassword page handler. Also covers ForgotPassword — a page whose
/// whole job is to explain the out-of-band process without becoming an
/// enumeration oracle.
/// </summary>
// In the ProviderCredentialBridge collection: booting Program.cs now assigns
// PluginHostServices.ApiKeys and .SecurityEvents (they were null on this head until
// 2026-08-27, which is the defect that was fixed). Those are process-wide statics, so a
// host boot racing a provider test's FakeApiKeyCheckout.Install silently swaps the fake
// out from under it — 19 provider tests went red on the first full-suite run after the
// bridge landed. The collection serialises every class that touches that static.
[Collection("ProviderCredentialBridge")]
public sealed class WebHostPasswordResetIntegrationTests : IClassFixture<HostedWebHostFixture>
{
    private readonly HostedWebHostFixture _host;

    public WebHostPasswordResetIntegrationTests(HostedWebHostFixture host) => _host = host;

    private const string OldPassword = "Correct-h0rse-battery";
    private const string NewPassword = "Fresh-p0ny-paddock-42";

    private async Task<string> MintResetTokenAsync(string email)
    {
        using var scope = _host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        return await users.GeneratePasswordResetTokenAsync(user!);
    }

    [Fact]
    public async Task Reset_round_trip_sets_the_new_password_and_retires_the_old_one()
    {
        const string email = "reset-happy@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, OldPassword);
        string resetToken = await MintResetTokenAsync(email);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var pageToken = await WebHostIntegration.GetAntiforgeryTokenAsync(
            client, "/terminal/account/resetpassword?email=" + Uri.EscapeDataString(email)
                  + "&token=" + Uri.EscapeDataString(resetToken));

        var resp = await client.PostAsync("/terminal/account/resetpassword", WebHostIntegration.Form(
            ("__RequestVerificationToken", pageToken),
            ("Input.Email", email),
            ("Input.Token", resetToken),
            ("Input.NewPassword", NewPassword),
            ("Input.ConfirmPassword", NewPassword)));

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("reset=1", resp.Headers.Location!.OriginalString); // login page confirmation banner

        // New password signs in…
        using var freshClient = WebHostIntegration.NewClient(_host.Factory);
        var login = await WebHostIntegration.LoginAsync(freshClient, email, NewPassword);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.True(WebHostIntegration.SetsAuthCookie(login));

        // …and the old one is dead.
        using var staleClient = WebHostIntegration.NewClient(_host.Factory);
        var stale = await WebHostIntegration.LoginAsync(staleClient, email, OldPassword);
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Email or password is incorrect.", await stale.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reset_with_a_bad_token_fails_with_the_same_message_as_an_unknown_email()
    {
        const string email = "reset-badtoken@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, OldPassword);

        using var client = WebHostIntegration.NewClient(_host.Factory);
        string ExpectFailure(string html)
        {
            Assert.Contains("invalid or has expired", html);
            return html;
        }

        // Known email, garbage token.
        var pageToken = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/resetpassword");
        var badToken = await client.PostAsync("/terminal/account/resetpassword", WebHostIntegration.Form(
            ("__RequestVerificationToken", pageToken),
            ("Input.Email", email),
            ("Input.Token", "not-a-real-token"),
            ("Input.NewPassword", NewPassword),
            ("Input.ConfirmPassword", NewPassword)));
        Assert.Equal(HttpStatusCode.OK, badToken.StatusCode);
        var badTokenHtml = ExpectFailure(await badToken.Content.ReadAsStringAsync());

        // Unknown email — byte-for-byte the same generic message (no oracle).
        var pageToken2 = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/resetpassword");
        var unknown = await client.PostAsync("/terminal/account/resetpassword", WebHostIntegration.Form(
            ("__RequestVerificationToken", pageToken2),
            ("Input.Email", "nobody@example.test"),
            ("Input.Token", "not-a-real-token"),
            ("Input.NewPassword", NewPassword),
            ("Input.ConfirmPassword", NewPassword)));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        ExpectFailure(await unknown.Content.ReadAsStringAsync());
        Assert.DoesNotContain("nobody@example.test is not registered", badTokenHtml);
    }

    [Fact]
    public async Task Forgot_password_acknowledges_without_confirming_the_account_exists()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var pageToken = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/forgotpassword");

        var resp = await client.PostAsync("/terminal/account/forgotpassword", WebHostIntegration.Form(
            ("__RequestVerificationToken", pageToken),
            ("Input.Email", "whoever@example.test")));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        // The neutral post-submit view, with the support contact — and no
        // wording that reveals whether the address is registered.
        Assert.DoesNotContain("no account", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not registered", html, StringComparison.OrdinalIgnoreCase);
    }

    // ── The operator hears about it (hosted notes §4c.1) ─────────────────────────────

    private sealed class RecordingPushSender : AccessibleTrader.WebHost.Services.Push.IWebPushSender
    {
        public readonly TaskCompletionSource<(string User, string Title, string Body)> First = new();
        public Task SendToUserAsync(string userKey, string title, string body, CancellationToken ct)
        {
            First.TrySetResult((userKey, title, body));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_reset_request_is_pushed_to_the_owner_account_naming_the_requester_and_the_ip()
    {
        // The event used to land in a security-event file and nowhere else; two requests sat
        // there unread for ten days each. Now it goes to the owner's push subscriptions — the
        // channel the alert monitor already delivers on — without the page ever looking up the
        // account that was named (that would rebuild the enumeration oracle).
        const string owner = "owner@example.test";
        var pushes = new RecordingPushSender();
        string dataRoot = TestTemp.NewDir("att-reset-push-");
        try
        {
            using var factory = WebHostIntegration.HostedFactory(dataRoot).WithWebHostBuilder(b =>
            {
                b.UseSetting(AccessibleTrader.WebHost.Services.Push.OwnerPushResetRequestNotifier.OwnerEmailKey, owner);
                b.ConfigureTestServices(services =>
                {
                    services.RemoveAll<AccessibleTrader.WebHost.Services.Push.IWebPushSender>();
                    services.AddSingleton<AccessibleTrader.WebHost.Services.Push.IWebPushSender>(pushes);
                });
            });
            var ownerUser = await WebHostIntegration.SeedUserAsync(factory, owner, OldPassword);

            using var client = WebHostIntegration.NewClient(factory);
            var pageToken = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/forgotpassword");
            var resp = await client.PostAsync("/terminal/account/forgotpassword", WebHostIntegration.Form(
                ("__RequestVerificationToken", pageToken),
                ("Input.Email", "stranded@example.test")));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var pushed = await pushes.First.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ownerUser.Id, pushed.User);
            Assert.Equal("Password reset requested", pushed.Title);
            Assert.Contains("stranded@example.test", pushed.Body);
            Assert.Contains("--reset-link stranded@example.test", pushed.Body);
        }
        finally { try { Directory.Delete(dataRoot, true); } catch { } }
    }

    [Fact]
    public async Task With_no_owner_configured_the_request_is_still_acknowledged_and_nothing_is_pushed()
    {
        // The fixture's host has no Accounts:OwnerEmail. The page must not fail or change its
        // answer because the operator side is unconfigured — the visitor sees the same neutral
        // page, and the journal carries the request.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var pageToken = await WebHostIntegration.GetAntiforgeryTokenAsync(client, "/terminal/account/forgotpassword");

        var resp = await client.PostAsync("/terminal/account/forgotpassword", WebHostIntegration.Form(
            ("__RequestVerificationToken", pageToken),
            ("Input.Email", "nobody-home@example.test")));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var notifier = _host.Factory.Services.GetRequiredService<AccessibleTrader.WebHost.Services.Push.IPasswordResetRequestNotifier>();
        Assert.Null(((AccessibleTrader.WebHost.Services.Push.OwnerPushResetRequestNotifier)notifier).OwnerEmail);
    }
}
