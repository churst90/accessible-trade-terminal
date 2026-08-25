using System.ComponentModel.DataAnnotations;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// Second factor of the sign-in. Reached only via the TwoFactorUserId cookie
    /// that PasswordSignInAsync sets when it returns RequiresTwoFactor — visiting
    /// cold redirects back to Login. Failed codes count toward the same lockout
    /// as passwords; the error message stays generic (no oracle).
    /// </summary>
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<AppUser> _signIn;
        private readonly ISecurityEventLog _audit;

        public LoginWith2faModel(SignInManager<AppUser> signIn, ISecurityEventLog audit)
        {
            _signIn = signIn;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        [BindProperty] public bool RememberMe { get; set; }
        public string? Error { get; private set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Enter the six-digit code from your authenticator app.")]
            [Display(Name = "Authenticator code")]
            public string Code { get; set; } = "";

            [Display(Name = "Remember this browser")]
            public bool RememberMachine { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(bool rememberMe = false)
        {
            // No pending first factor → nothing to challenge.
            if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null)
                return RedirectToPage("Login");
            RememberMe = rememberMe;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return RedirectToPage("Login");
            if (!ModelState.IsValid) return Page();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string code = TwoFactorSupport.NormalizeCode(Input.Code);

            var result = await _signIn.TwoFactorAuthenticatorSignInAsync(
                code, RememberMe, Input.RememberMachine);

            if (result.Succeeded)
            {
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthTwoFactorLoginSucceeded, "auth",
                    "Two-factor sign-in succeeded (authenticator code).",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = user.Email ?? "" }));
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? Url.Content("~/") : returnUrl);
            }

            _audit.Record(new SecurityEvent(
                DateTime.UtcNow,
                result.IsLockedOut ? SecurityEventKind.AuthLockout : SecurityEventKind.AuthTwoFactorLoginFailed,
                "auth",
                result.IsLockedOut
                    ? "Two-factor sign-in refused: account locked out."
                    : "Two-factor sign-in failed: invalid authenticator code.",
                new Dictionary<string, string> { ["ip"] = ip, ["email"] = user.Email ?? "" }));

            // Same generic wording for wrong-code and locked-out (no oracle).
            Error = "That code didn't work. Enter the current code from your authenticator app.";
            return Page();
        }
    }
}
