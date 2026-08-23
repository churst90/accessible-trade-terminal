using System;
using System.Net;
using System.Threading.Tasks;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The admin-mediated password-reset flow over HTTP. The reset token is minted
/// through the same <see cref="UserManager{TUser}"/> the <c>--reset-link</c>
/// CLI uses (there is deliberately no outbound mail), then consumed by the
/// real ResetPassword page handler. Also covers ForgotPassword — a page whose
/// whole job is to explain the out-of-band process without becoming an
/// enumeration oracle.
/// </summary>
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
}
