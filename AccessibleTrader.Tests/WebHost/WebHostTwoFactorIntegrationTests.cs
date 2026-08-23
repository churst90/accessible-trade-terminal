using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The full two-factor journey over HTTP against the real WebHost: TOTP
/// enrollment on the [Authorize] EnableAuthenticator page, the password →
/// authenticator-challenge handoff, the LoginWith2fa handler, and the
/// single-use recovery-code fallback. The authenticator codes are computed by
/// the test's own RFC 6238 implementation (shared with
/// <see cref="HostedAccountsTwoFactorTests"/>), so this proves interop with a
/// real authenticator app rather than Identity agreeing with itself.
/// </summary>
public sealed class WebHostTwoFactorIntegrationTests : IClassFixture<HostedWebHostFixture>
{
    private readonly HostedWebHostFixture _host;

    public WebHostTwoFactorIntegrationTests(HostedWebHostFixture host) => _host = host;

    private const string Password = "Correct-h0rse-battery";

    [Fact]
    public async Task Enable2fa_requires_a_signed_in_user()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/account/enable2fa");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.StartsWith("/terminal/account/login", resp.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task LoginWith2fa_visited_cold_redirects_back_to_login()
    {
        // No pending first factor (no TwoFactorUserId cookie) → nothing to challenge.
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await client.GetAsync("/terminal/account/loginwith2fa");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.EndsWith("/account/login", resp.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Totp_enrollment_then_2fa_login_then_recovery_code_login()
    {
        const string email = "twofactor@example.test";
        await WebHostIntegration.SeedUserAsync(_host.Factory, email, Password);

        // ── Enroll: sign in, read the shared key off the page, confirm a code ──
        using var enrollClient = WebHostIntegration.NewClient(_host.Factory);
        var login = await WebHostIntegration.LoginAsync(enrollClient, email, Password);
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var enrollPage = await enrollClient.GetAsync("/terminal/account/enable2fa");
        Assert.Equal(HttpStatusCode.OK, enrollPage.StatusCode);
        var enrollHtml = await enrollPage.Content.ReadAsStringAsync();

        var keyMatch = Regex.Match(enrollHtml, "id=\"shared-key\"[\\s\\S]*?value=\"([^\"]+)\"");
        Assert.True(keyMatch.Success, "enable2fa page shows no shared key");
        string sharedKey = keyMatch.Groups[1].Value; // grouped-in-fours; Base32Decode skips the spaces

        var confirm = await enrollClient.PostAsync("/terminal/account/enable2fa", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(enrollHtml)),
            ("Input.Code", HostedAccountsTwoFactorTests.ComputeTotp(sharedKey, DateTimeOffset.UtcNow))));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmHtml = await confirm.Content.ReadAsStringAsync();
        Assert.Contains("Two-factor authentication is on", confirmHtml);

        var codesMatch = Regex.Match(confirmHtml, "id=\"recovery-codes\"[^>]*>([\\s\\S]*?)</textarea>");
        Assert.True(codesMatch.Success, "recovery codes were not rendered after enrollment");
        // Razor's HTML encoder renders the joining newlines as &#xA; — decode first.
        var recoveryCodes = WebUtility.HtmlDecode(codesMatch.Groups[1].Value)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        Assert.Equal(10, recoveryCodes.Count);

        // ── Password alone no longer signs in: it hands off to the 2FA page ──
        using var totpClient = WebHostIntegration.NewClient(_host.Factory);
        var secondLogin = await WebHostIntegration.LoginAsync(totpClient, email, Password);
        Assert.Equal(HttpStatusCode.Found, secondLogin.StatusCode);
        Assert.Contains("/account/loginwith2fa", secondLogin.Headers.Location!.OriginalString);
        Assert.False(WebHostIntegration.SetsAuthCookie(secondLogin),
            "the first factor alone must not issue the session cookie");

        var challenge = await totpClient.GetAsync("/terminal/account/loginwith2fa");
        Assert.Equal(HttpStatusCode.OK, challenge.StatusCode);
        var challengeHtml = await challenge.Content.ReadAsStringAsync();

        var wrong = await totpClient.PostAsync("/terminal/account/loginwith2fa", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(challengeHtml)),
            ("RememberMe", "false"),
            ("Input.Code", "000000")));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("code didn", await wrong.Content.ReadAsStringAsync()); // generic error, no oracle

        var retryHtml = await (await totpClient.GetAsync("/terminal/account/loginwith2fa")).Content.ReadAsStringAsync();
        var right = await totpClient.PostAsync("/terminal/account/loginwith2fa", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(retryHtml)),
            ("RememberMe", "false"),
            ("Input.Code", HostedAccountsTwoFactorTests.ComputeTotp(sharedKey, DateTimeOffset.UtcNow))));
        Assert.Equal(HttpStatusCode.Found, right.StatusCode);
        Assert.True(WebHostIntegration.SetsAuthCookie(right));
        Assert.Equal(HttpStatusCode.OK,
            (await totpClient.GetAsync("/terminal/push/vapid-public-key")).StatusCode);

        // ── Recovery code completes sign-in once, and only once ──
        using var recoveryClient = WebHostIntegration.NewClient(_host.Factory);
        var thirdLogin = await WebHostIntegration.LoginAsync(recoveryClient, email, Password);
        Assert.Contains("/account/loginwith2fa", thirdLogin.Headers.Location!.OriginalString);

        var recoveryPage = await recoveryClient.GetAsync("/terminal/account/loginwithrecovery");
        Assert.Equal(HttpStatusCode.OK, recoveryPage.StatusCode);
        var recoveryHtml = await recoveryPage.Content.ReadAsStringAsync();

        var redeemed = await recoveryClient.PostAsync("/terminal/account/loginwithrecovery", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(recoveryHtml)),
            ("Input.RecoveryCode", recoveryCodes[0])));
        Assert.Equal(HttpStatusCode.Found, redeemed.StatusCode);
        Assert.True(WebHostIntegration.SetsAuthCookie(redeemed));

        using var replayClient = WebHostIntegration.NewClient(_host.Factory);
        var fourthLogin = await WebHostIntegration.LoginAsync(replayClient, email, Password);
        Assert.Contains("/account/loginwith2fa", fourthLogin.Headers.Location!.OriginalString);

        var replayHtml = await (await replayClient.GetAsync("/terminal/account/loginwithrecovery")).Content.ReadAsStringAsync();
        var replay = await replayClient.PostAsync("/terminal/account/loginwithrecovery", WebHostIntegration.Form(
            ("__RequestVerificationToken", WebHostIntegration.ExtractAntiforgeryToken(replayHtml)),
            ("Input.RecoveryCode", recoveryCodes[0])));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Contains("used only once", await replay.Content.ReadAsStringAsync());
        Assert.False(WebHostIntegration.SetsAuthCookie(replay));
    }
}
