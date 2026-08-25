using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// Pins the hosted-accounts auth configuration (AddHostedAccounts) against the
/// documented policy in docs/SERVER_SETUP.md and docs/HOSTED_AUTH_PERSISTENCE_DESIGN.md:
/// 10-char length-over-symbol-soup passwords, per-account lockout after 10 failures,
/// and a Secure + HttpOnly + SameSite=Lax auth cookie with 14-day sliding expiry that
/// sends unauthenticated visitors to the accessible login page. Follows the
/// ServiceCollection pattern of WebHostServiceLifetimeTests.
/// </summary>
public class HostedAccountsAuthPolicyTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly ServiceProvider _provider;

    public HostedAccountsAuthPolicyTests()
    {
        _dataRoot = Directory.CreateTempSubdirectory("att-auth-tests-").FullName;
        var services = new ServiceCollection();
        // AddHostedAccounts assumes the host already registered DataProtection
        // (Program.cs does); an ephemeral provider keeps the test off the real key ring.
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHostedAccounts(_dataRoot);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        try { Directory.Delete(_dataRoot, recursive: true); } catch { /* best effort */ }
    }

    private IdentityOptions Identity =>
        _provider.GetRequiredService<IOptions<IdentityOptions>>().Value;

    private CookieAuthenticationOptions AppCookie =>
        _provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

    [Fact]
    public void PasswordPolicy_LengthOverSymbolSoup()
    {
        // Documented a11y decision: 10+ characters, NO forced symbols — long passphrases
        // are easier for screen-reader users to type accurately and equally strong.
        Assert.Equal(10, Identity.Password.RequiredLength);
        Assert.False(Identity.Password.RequireNonAlphanumeric);
    }

    [Fact]
    public void Lockout_BruteForceGuard_TripsAfterTenFailures()
    {
        // Second defence layer behind the per-IP rate limiter (SERVER_SETUP.md):
        // Identity's per-account lockout.
        Assert.Equal(10, Identity.Lockout.MaxFailedAccessAttempts);
    }

    [Fact]
    public void Accounts_RequireUniqueEmail_ButNoEmailConfirmation()
    {
        Assert.True(Identity.User.RequireUniqueEmail);
        // Documented limitation: no transactional email is wired yet, so confirmation
        // must stay off or nobody could ever sign in.
        Assert.False(Identity.SignIn.RequireConfirmedAccount);
    }

    [Fact]
    public void AuthCookie_IsSecureHttpOnlySameSiteLax()
    {
        var cookie = AppCookie.Cookie;
        // SERVER_SETUP.md security checklist: HTTPS-only behind nginx → Secure Always;
        // HttpOnly blocks script theft; SameSite=Lax still flows on the top-level GET
        // after the login POST AND blocks cross-site WebSocket hijack of the circuit.
        Assert.Equal(CookieSecurePolicy.Always, cookie.SecurePolicy);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, cookie.SameSite);
    }

    [Fact]
    public void AuthCookie_SlidingFourteenDayExpiry()
    {
        Assert.True(AppCookie.SlidingExpiration);
        Assert.Equal(TimeSpan.FromDays(14), AppCookie.ExpireTimeSpan);
    }

    [Fact]
    public void UnauthenticatedVisitors_AreSentToTheAccessibleLoginPage()
    {
        Assert.Equal("/account/login", AppCookie.LoginPath);
        Assert.Equal("/account/logout", AppCookie.LogoutPath);
        // Access-denied ALSO routes to login (no separate denied page exists yet).
        Assert.Equal("/account/login", AppCookie.AccessDeniedPath);
    }

    [Fact]
    public void PerUserPathRouting_ReplacesTheSingleUserDescriptor_NotJustOverridesIt()
    {
        // AddHostedAccounts must REPLACE (RemoveAll + Add) the single-user
        // registrations rather than append a second descriptor: ValidateOnBuild
        // validates every descriptor, and a leftover Singleton path service would be
        // a captive dependency / wrong-user data path. Pin: exactly one descriptor,
        // Scoped (per-circuit = per-authenticated-user).
        var root = Directory.CreateTempSubdirectory("att-auth-di-").FullName;
        try
        {
            var s = new ServiceCollection()
                .AddAccessibleTraderWebHostServices()
                .AddHostedAccounts(root);

            var pathDescriptors = s.Where(d => d.ServiceType == typeof(IPlatformPathService)).ToList();
            Assert.Single(pathDescriptors);
            Assert.Equal(ServiceLifetime.Scoped, pathDescriptors[0].Lifetime);

            var cacheDescriptors = s.Where(d => d.ServiceType == typeof(ICacheService)).ToList();
            Assert.Single(cacheDescriptors);
            Assert.Equal(ServiceLifetime.Scoped, cacheDescriptors[0].Lifetime);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
