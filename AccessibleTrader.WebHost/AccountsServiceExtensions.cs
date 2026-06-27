using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

            // Secrets stay process-wide (shared DataProtection store). Give the Singleton a
            // shared path so it doesn't capture the per-user path service.
            services.RemoveAll<WebHostSecureStorageService>();
            services.AddSingleton<WebHostSecureStorageService>(sp =>
                new WebHostSecureStorageService(sp.GetRequiredService<IDataProtectionProvider>(), new WebHostPathService()));

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
            });

            services.AddAuthorization();
            services.AddCascadingAuthenticationState();

            // Razor Pages host the accessible login/register/logout forms — server-rendered
            // (built-in antiforgery), independent of the Blazor render-mode pipeline.
            services.AddRazorPages();

            return services;
        }
    }
}
