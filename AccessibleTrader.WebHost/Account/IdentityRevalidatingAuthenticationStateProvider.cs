using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Re-checks a live Blazor circuit's principal against the database on an interval, and
    /// tears the circuit's authentication down when the security stamp has rotated.
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>AddCascadingAuthenticationState()</c> was registered but nothing registered a
    /// revalidating provider, and there was no <c>SecurityStampValidatorOptions</c>
    /// configuration anywhere. Blazor's default <c>ServerAuthenticationStateProvider</c>
    /// captures the principal from the HTTP request that booted the circuit and <b>never looks
    /// again</b>; <c>blazorApp.RequireAuthorization()</c> is enforced on the page endpoint, not
    /// on every hub message, and <c>CurrentUser.Set</c> is likewise called exactly once.
    /// </para>
    ///
    /// <para>
    /// So: an attacker gets the victim's session and opens the terminal. The victim notices and
    /// follows the documented recovery path — admin mints a <c>--reset-link</c>, victim sets a
    /// new password, enables 2FA. Identity rotates the security stamp, so every <i>HTTP</i>
    /// request carrying the old cookie is rejected within the validation window — but the
    /// attacker's WebSocket circuit is already established and keeps full access to charts,
    /// alerts, settings, the paper account and the user's alert-channel credentials until they
    /// close the tab or the process restarts. <b>Password reset, 2FA enrollment, lockout and
    /// sign-out did not evict an already-open session</b>, which directly defeats the property
    /// the security page claims.
    /// </para>
    ///
    /// <para>
    /// The interval is short on purpose. This is the window during which a compromised session
    /// survives the victim's own remediation, and the cost of checking is one indexed lookup
    /// per circuit per interval.
    /// </para>
    /// </summary>
    public sealed class IdentityRevalidatingAuthenticationStateProvider
        : RevalidatingServerAuthenticationStateProvider
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly IOptions<IdentityOptions> _options;

        public IdentityRevalidatingAuthenticationStateProvider(
            ILoggerFactory loggerFactory,
            IServiceScopeFactory scopes,
            IOptions<IdentityOptions> options)
            : base(loggerFactory)
        {
            _scopes = scopes;
            _options = options;
        }

        /// <summary>How often a live circuit re-checks who it belongs to.</summary>
        protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

        protected override async Task<bool> ValidateAuthenticationStateAsync(
            AuthenticationState authenticationState, CancellationToken cancellationToken)
        {
            await using var scope = _scopes.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

            var user = await userManager.GetUserAsync(authenticationState.User);
            if (user is null) return false;

            // A locked-out account keeps its stamp, so the stamp check alone would not evict
            // one. Lockout is a deliberate "this account is not to be used right now", and a
            // circuit that stays open through it is the same hole by another name.
            if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))
                return false;

            if (!userManager.SupportsUserSecurityStamp) return true;

            var principalStamp = authenticationState.User.FindFirstValue(
                _options.Value.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);

            return principalStamp == userStamp;
        }
    }
}
