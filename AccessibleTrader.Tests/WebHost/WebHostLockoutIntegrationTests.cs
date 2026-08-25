using System.Net;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Identity's per-account lockout, observed over HTTP — the layer
/// <c>HostedAccountsAuthPolicyTests.Lockout_BruteForceGuard_TripsAfterTenFailures</c>
/// cannot see: that test pins <c>MaxFailedAccessAttempts == 10</c> on the options
/// object; this one proves the login PAGE actually passes <c>lockoutOnFailure</c>
/// and that the tenth wrong password locks the CORRECT password out.
///
/// <para>
/// The awkward part is the auth rate limiter: it allows exactly 10 login POSTs
/// per 5-minute window per client IP, and driving a lockout takes 11 (ten
/// failures plus the proof attempt). The limiter partitions on
/// <c>Connection.RemoteIpAddress</c>, which sits behind the forwarded-headers
/// middleware Program.cs installs for nginx (allow-lists cleared — any upstream
/// is trusted), so each phase of this test presents its own
/// <c>X-Forwarded-For</c> and gets its own rate-limit partition — while the
/// lockout, which is PER ACCOUNT, accumulates across all of them. That split is
/// itself part of what's being proven: a brute-forcer who rotates IPs beats the
/// rate limiter but still hits the account lockout.
/// </para>
/// </summary>
public sealed class WebHostLockoutIntegrationTests : IDisposable
{
    private readonly string _dataRoot = Directory.CreateTempSubdirectory("att-lockout-int-").FullName;
    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<WebHostDemoMode> _factory;

    public WebHostLockoutIntegrationTests()
    {
        _factory = WebHostIntegration.HostedFactory(_dataRoot);
    }

    public void Dispose()
    {
        _factory.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { }
    }

    private HttpClient ClientAs(string forwardedIp)
    {
        var client = WebHostIntegration.NewClient(_factory);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedIp);
        return client;
    }

    [Fact]
    public async Task Tenth_failed_login_locks_the_account_and_the_correct_password_is_refused()
    {
        const string email = "locked@example.test";
        const string password = "Correct horse 9 battery";
        await WebHostIntegration.SeedUserAsync(_factory, email, password);

        // ── Positive control FIRST: these credentials sign in. Without this,
        // the final refusal below could just as well mean the seed was broken.
        // (Runs first because a successful login resets the failed-attempt
        // count — and proving that reset is part of the contract anyway.)
        using (var control = ClientAs("203.0.113.10"))
        {
            var ok = await WebHostIntegration.LoginAsync(control, email, password);
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            Assert.True(WebHostIntegration.SetsAuthCookie(ok));
        }

        // ── Ten wrong passwords from one "IP". Exactly the rate-limit budget:
        // every one must reach Identity (a 429 here would mean the limiter ate
        // the attempt and the lockout counter never moved).
        using (var attacker = ClientAs("203.0.113.11"))
        {
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                var resp = await WebHostIntegration.LoginAsync(attacker, email, "wrong-password-x");
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode); // re-rendered form, not 429, not a redirect
                Assert.False(WebHostIntegration.SetsAuthCookie(resp));
            }
        }

        // The account itself is now locked — asserted through the same
        // UserManager the app uses, so the HTTP refusal below can't be
        // explained away as rate limiting or a bad antiforgery token.
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(await users.IsLockedOutAsync(user!),
                "ten failed logins did not lock the account — is lockoutOnFailure still passed?");
        }

        // ── The proof: the CORRECT password, from a fresh IP with a fresh
        // rate-limit budget, is refused — and with the SAME generic message a
        // wrong password gets. A distinct "account locked" message would
        // confirm the address is registered (an enumeration oracle); the
        // anti-oracle wording is pinned here so it can't regress quietly.
        using (var victim = ClientAs("203.0.113.12"))
        {
            var refused = await WebHostIntegration.LoginAsync(victim, email, password);
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            Assert.False(WebHostIntegration.SetsAuthCookie(refused));
            Assert.Contains("Email or password is incorrect.",
                await refused.Content.ReadAsStringAsync());
        }
    }
}
