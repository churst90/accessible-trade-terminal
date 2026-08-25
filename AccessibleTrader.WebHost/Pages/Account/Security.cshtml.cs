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
    /// </summary>
    [Authorize]
    public class SecurityModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly ISecurityEventLog _audit;

        public SecurityModel(UserManager<AppUser> users, ISecurityEventLog audit)
        {
            _users = users;
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
            if (!string.IsNullOrEmpty(password) && await _users.CheckPasswordAsync(user, password))
                return true;
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
