using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// TOTP enrollment. Accessible-first: the copyable setup key is the primary
    /// path (readonly input, grouped in fours), the QR is the phone-scan
    /// convenience. On success the ten recovery codes render ONCE in a readonly
    /// textarea (focusable, selectable, read line-by-line by screen readers).
    /// </summary>
    [Authorize]
    public class EnableAuthenticatorModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly ISecurityEventLog _audit;

        public EnableAuthenticatorModel(UserManager<AppUser> users, ISecurityEventLog audit)
        {
            _users = users;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        public string SharedKey { get; private set; } = "";
        public string QrDataUri { get; private set; } = "";
        public string? Error { get; private set; }

        /// <summary>Non-null only on the one render right after successful enrollment.</summary>
        public IReadOnlyList<string>? RecoveryCodes { get; private set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Enter the six-digit code from your authenticator app.")]
            [Display(Name = "Verification code")]
            public string Code { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            await LoadKeyAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");

            string code = TwoFactorSupport.NormalizeCode(Input.Code);
            bool valid = ModelState.IsValid && await _users.VerifyTwoFactorTokenAsync(
                user, _users.Options.Tokens.AuthenticatorTokenProvider, code);

            if (!valid)
            {
                Error = "That code didn't match. Codes change every 30 seconds — enter the current one and press Turn on again.";
                await LoadKeyAsync(user);
                return Page();
            }

            await _users.SetTwoFactorEnabledAsync(user, true);
            var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            RecoveryCodes = codes?.ToList() ?? new List<string>();

            _audit.Record(new SecurityEvent(
                DateTime.UtcNow, SecurityEventKind.AuthTwoFactorEnabled, "auth",
                "Two-factor authentication enabled (authenticator enrolled, 10 recovery codes issued).",
                new Dictionary<string, string>
                {
                    ["ip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    ["email"] = user.Email ?? "",
                }));
            return Page();
        }

        private async Task LoadKeyAsync(AppUser user)
        {
            // First visit mints the key; revisits reuse it so a half-finished
            // enrollment doesn't invalidate what the user already typed into
            // their app.
            var key = await _users.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _users.ResetAuthenticatorKeyAsync(user);
                key = await _users.GetAuthenticatorKeyAsync(user);
            }

            SharedKey = TwoFactorSupport.FormatSharedKey(key ?? "");
            QrDataUri = TwoFactorSupport.QrPngDataUri(
                TwoFactorSupport.BuildOtpauthUri(user.Email ?? user.UserName ?? "account", key ?? ""));
        }
    }
}
