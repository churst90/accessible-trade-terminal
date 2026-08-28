using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// The signed-in account-security hub: 2FA status + recovery-code count,
    /// enrollment entry point, and the two destructive actions — disable 2FA and
    /// regenerate recovery codes — both re-confirmed with the CURRENT password so
    /// a hijacked session can't quietly strip the account's second factor.
    ///
    /// <para>
    /// <b>The re-confirmation counts failures.</b> It used to call
    /// <see cref="UserManager{TUser}.CheckPasswordAsync"/>, which verifies the hash
    /// and nothing else — unlike the login page's
    /// <c>PasswordSignInAsync(..., lockoutOnFailure: true)</c>, it does not
    /// increment <c>AccessFailedCount</c>, so the ten-failure lockout configured in
    /// <c>AccountsServiceExtensions</c> never tripped here no matter how many
    /// guesses arrived. Combined with this page's absence from
    /// <see cref="Services.AuthRateLimitPolicy.IsAuthMutation"/> (fixed alongside),
    /// an attacker holding a stolen session could brute-force the account password
    /// through the "Turn off two-factor" form at general-tier rates, with no
    /// lockout and no audit event that looked different from ordinary use — and
    /// then strip the second factor. It now goes through
    /// <see cref="SignInManager{TUser}.CheckPasswordSignInAsync"/> with
    /// <c>lockoutOnFailure: true</c>, which is the same counter the front door
    /// uses, and records <see cref="SecurityEventKind.AuthReauthenticationFailed"/>
    /// so the probing is visible in the audit log.
    /// </para>
    /// </summary>
    [Authorize]
    public class SecurityModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly ISecurityEventLog _audit;

        public SecurityModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, ISecurityEventLog audit)
        {
            _users = users;
            _signIn = signIn;
            _audit = audit;
        }

        public string Email { get; private set; } = "";
        public bool TwoFactorEnabled { get; private set; }
        public int RecoveryCodesLeft { get; private set; }
        public string? Status { get; private set; }
        public string? Error { get; private set; }

        /// <summary>Non-null only on the render right after a regenerate.</summary>
        public IReadOnlyList<string>? NewRecoveryCodes { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostDisableAsync(string? password)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");

            if (!await ConfirmPasswordAsync(user, password))
            {
                await LoadAsync(user);
                return Page();
            }

            await _users.SetTwoFactorEnabledAsync(user, false);
            // Drop the enrolled key so a future re-enable mints a FRESH secret —
            // re-enabling must never silently revive an old (possibly leaked) key.
            await _users.ResetAuthenticatorKeyAsync(user);

            _audit.Record(new SecurityEvent(
                DateTime.UtcNow, SecurityEventKind.AuthTwoFactorDisabled, "auth",
                "Two-factor authentication disabled (password-confirmed).",
                AuditDetail(user)));

            Status = "Two-factor authentication is now off.";
            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostRegenerateCodesAsync(string? password)
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");

            if (!await ConfirmPasswordAsync(user, password))
            {
                await LoadAsync(user);
                return Page();
            }

            if (!await _users.GetTwoFactorEnabledAsync(user))
            {
                Error = "Two-factor authentication isn't enabled.";
                await LoadAsync(user);
                return Page();
            }

            var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            NewRecoveryCodes = codes?.ToList() ?? new List<string>();

            _audit.Record(new SecurityEvent(
                DateTime.UtcNow, SecurityEventKind.AuthRecoveryCodesGenerated, "auth",
                "New two-factor recovery codes generated (previous set invalidated).",
                AuditDetail(user)));

            Status = "Fresh recovery codes generated. The old ones no longer work.";
            await LoadAsync(user);
            return Page();
        }

        private async Task<bool> ConfirmPasswordAsync(AppUser user, string? password)
        {
            if (!string.IsNullOrEmpty(password))
            {
                // CheckPasswordSignInAsync, not CheckPasswordAsync: it runs the same
                // lockout accounting as the login page (and refuses outright once the
                // account is locked), while still NOT issuing a sign-in — this is a
                // re-confirmation, not a second authentication.
                var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
                if (result.Succeeded) return true;

                if (result.IsLockedOut)
                {
                    _audit.Record(new SecurityEvent(
                        DateTime.UtcNow, SecurityEventKind.AuthLockout, "auth",
                        "Account-security re-confirmation refused: account locked out after too many failed attempts.",
                        AuditDetail(user)));
                    // Say so plainly here, unlike the login page. There is no
                    // enumeration to protect against — the caller already holds this
                    // account's session — and a screen-reader user retrying a form
                    // that has silently stopped accepting ANY password is the worst
                    // possible version of this refusal.
                    Error = "Too many incorrect passwords. This account is locked for 15 minutes; no changes were made.";
                    return false;
                }

                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthReauthenticationFailed, "auth",
                    "Account-security re-confirmation failed: incorrect current password.",
                    AuditDetail(user)));
            }

            Error = "Password incorrect — no changes were made.";
            return false;
        }

        private async Task LoadAsync(AppUser user)
        {
            Email = user.Email ?? "";
            TwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(user);
            RecoveryCodesLeft = TwoFactorEnabled ? await _users.CountRecoveryCodesAsync(user) : 0;
        }

        private Dictionary<string, string> AuditDetail(AppUser user) => new()
        {
            ["ip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            ["email"] = user.Email ?? "",
        };
    }
}
