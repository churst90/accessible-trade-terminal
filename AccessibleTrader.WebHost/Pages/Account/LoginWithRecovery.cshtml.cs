using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// Fallback second factor: a single-use recovery code. Identity invalidates
    /// the code on use; the audit trail records the consumption so the operator
    /// can spot recovery-code brute-forcing (which also trips the lockout).
    /// </summary>
    public class LoginWithRecoveryModel : PageModel
    {
        private readonly SignInManager<AppUser> _signIn;
        private readonly UserManager<AppUser> _users;
        private readonly ISecurityEventLog _audit;

        public LoginWithRecoveryModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, ISecurityEventLog audit)
        {
            _signIn = signIn;
            _users = users;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? Error { get; private set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Enter one of your recovery codes.")]
            [Display(Name = "Recovery code")]
            public string RecoveryCode { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null)
                return RedirectToPage("Login");
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToPage("Login");
            if (!ModelState.IsValid) return Page();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string code = TwoFactorSupport.NormalizeRecoveryCode(Input.RecoveryCode);

            var result = await _signIn.TwoFactorRecoveryCodeSignInAsync(code);

            if (result.Succeeded)
            {
                int remaining = await _users.CountRecoveryCodesAsync(user);
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthRecoveryCodeUsed, "auth",
                    $"Recovery code consumed to complete sign-in; {remaining} remaining.",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = user.Email ?? "" }));
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl);
            }

            _audit.Record(new SecurityEvent(
                DateTime.UtcNow,
                result.IsLockedOut ? SecurityEventKind.AuthLockout : SecurityEventKind.AuthTwoFactorLoginFailed,
                "auth",
                result.IsLockedOut
                    ? "Recovery-code sign-in refused: account locked out."
                    : "Recovery-code sign-in failed: invalid or already-used code.",
                new Dictionary<string, string> { ["ip"] = ip, ["email"] = user.Email ?? "" }));

            Error = "That recovery code didn't work. Each code can be used only once.";
            return Page();
        }
    }
}
