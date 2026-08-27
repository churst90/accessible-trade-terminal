using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;

namespace AccessibleTrader.WebHost
{
    /// <summary>
    /// Wires hosted multi-user accounts: ASP.NET Core Identity (self-hosted email +
    /// password) plus per-user persistence routing. Call ONLY when accounts are enabled,
    /// AFTER <c>AddAccessibleTraderWebHostServices</c> — the registrations here override the
    /// single-user defaults. The local/demo modes (which never call this) and the MAUI head
    /// are unaffected. See docs/HOSTED_AUTH_PERSISTENCE_DESIGN.md.
    /// </summary>
    public static class AccountsServiceExtensions
    {
        public static IServiceCollection AddHostedAccounts(this IServiceCollection services, string? dataRoot)
        {
            // ── Per-user state routing (overrides the single-user defaults) ─────────
            // NOTE: these RemoveAll calls matter — adding a second registration changes
            // *resolution* but leaves the original descriptor in the collection, and
            // ValidateOnBuild validates every descriptor. So replace, don't just add.
            services.AddHttpContextAccessor();
            services.AddScoped<ICurrentUser, CurrentUser>();

            // Route AppDataDirectory per circuit-user; CacheDirectory stays shared.
            services.RemoveAll<IPlatformPathService>();
            services.AddScoped<IPlatformPathService>(sp =>
                new UserScopedPathService(sp.GetRequiredService<ICurrentUser>(), accountsEnabled: true, dataRoot));

            // FileCache used IPlatformPathService and was a Singleton — make it per-circuit
            // so it doesn't capture the now-Scoped path service. (Per-user cache for the
            // first cut; a shared symbol-keyed pool is the documented future optimisation.)
            services.RemoveAll<ICacheService>();
            services.AddScoped<ICacheService, FileCacheService>();

            // Secrets stay process-wide (shared DataProtection store, not per-user). Give the
            // Singleton a NON-scoped path so it doesn't capture the per-user path service. Pin it
            // under this instance's own data root when one is configured, so a hosted instance is
            // self-contained and can't collide with a co-located demo — both otherwise resolve to
            // the default ~/.local/share/AccessibleTrader and clobber each other's encrypted
            // market-data secret (last writer wins). Falls back to the OS default when no root set.
            var sharedSecretPaths = string.IsNullOrWhiteSpace(dataRoot)
                ? new WebHostPathService()
                : new WebHostPathService(dataRoot!);
            services.RemoveAll<WebHostSecureStorageService>();
            services.AddSingleton<WebHostSecureStorageService>(sp =>
                new WebHostSecureStorageService(sp.GetRequiredService<IDataProtectionProvider>(), sharedSecretPaths));

            // ── Identity: self-hosted email + password, no email confirmation yet ───
            string authDbDir = string.IsNullOrWhiteSpace(dataRoot) ? new WebHostPathService().AppDataDirectory : dataRoot!;
            Directory.CreateDirectory(authDbDir);
            services.AddDbContext<AuthDbContext>(o =>
                o.UseSqlite($"Data Source={Path.Combine(authDbDir, "auth.db")}"));

            services.AddIdentityCore<AppUser>(o =>
                {
                    o.Password.RequiredLength = 10;
                    o.Password.RequireNonAlphanumeric = false;   // length over symbol-soup (a11y-friendly, equally strong)
                    o.User.RequireUniqueEmail = true;
                    o.SignIn.RequireConfirmedAccount = false;    // no transactional email wired yet
                    o.Lockout.MaxFailedAccessAttempts = 10;      // brute-force guard
                    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); // cool-off after the limit
                    o.Lockout.AllowedForNewUsers = true;         // enforce lockout for accounts created here
                })
                .AddEntityFrameworkStores<AuthDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddIdentityCookies();

            // Send unauthenticated visitors to the accessible login page.
            services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, o =>
            {
                o.LoginPath = "/account/login";
                o.LogoutPath = "/account/logout";
                o.AccessDeniedPath = "/account/login";
                // Public site is HTTPS-only behind nginx (UseForwardedHeaders makes the
                // request scheme HTTPS): mark the auth cookie Secure + HttpOnly + SameSite=Lax
                // (Lax still flows on the top-level GET back from the login form POST).
                o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                // Neutral cookie name (drops the default ".AspNetCore.Identity.Application"
                // fingerprint). The __Host- prefix pins the cookie to this exact host over
                // HTTPS and requires Secure + Path=/ + no Domain — Secure is already set
                // above, so lock Path to "/" and leave Domain unset to satisfy the prefix.
                o.Cookie.Name = "__Host-att.auth";
                o.Cookie.Path = "/";
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
            });

            services.AddAuthorization();
            services.AddCascadingAuthenticationState();

            // Re-check a LIVE CIRCUIT's principal, not just the request that booted it.
            //
            // Blazor's default ServerAuthenticationStateProvider captures the principal once
            // and never looks again, so password reset, 2FA enrollment, lockout and sign-out
            // did not evict an already-open session — a stolen session survived the victim's
            // own remediation until the tab closed or the process restarted. This is the stock
            // .NET Identity-on-Blazor arrangement, which this app had simply never registered.
            services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
                               IdentityRevalidatingAuthenticationStateProvider>();

            // ...and make the HTTP side's window match. Without this the cookie's stamp is
            // revalidated on the framework default, which is looser than the circuit's.
            services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(o =>
                o.ValidationInterval = TimeSpan.FromMinutes(5));

            // Razor Pages host the accessible login/register/logout forms — server-rendered
            // (built-in antiforgery), independent of the Blazor render-mode pipeline.
            services.AddRazorPages();

            return services;
        }
    }
}
