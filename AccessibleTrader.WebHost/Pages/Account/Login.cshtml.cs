using System.ComponentModel.DataAnnotations;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signIn;
        private readonly ISecurityEventLog _audit;

        public LoginModel(SignInManager<AppUser> signIn, ISecurityEventLog audit)
        {
            _signIn = signIn;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? Error { get; set; }

        /// <summary>Set when the visitor arrives from a successful password reset (?reset=1) — shows a confirmation banner.</summary>
        public bool PasswordReset { get; set; }

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";

            [Required, DataType(DataType.Password), Display(Name = "Password")]
            public string Password { get; set; } = "";

            [Display(Name = "Keep me signed in")]
            public bool RememberMe { get; set; }
        }

        public void OnGet(int reset = 0) => PasswordReset = reset == 1;

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            var email = Input.Email?.Trim() ?? "";
            // RemoteIpAddress sits behind the already-configured forwarded-headers
            // middleware, so this is the real client IP, not nginx's.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Users register with email as their username, so sign in by email.
            var result = await _signIn.PasswordSignInAsync(
                email, Input.Password, Input.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthLoginSucceeded, "auth",
                    "Sign-in succeeded.",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));
                return LocalRedirect(ReturnUrlPolicy.Sanitize(Url, returnUrl));
            }

            if (result.RequiresTwoFactor)
            {
                // First factor passed; hand off to the authenticator challenge.
                // No audit event yet — success/failure is recorded there.
                return RedirectToPage("LoginWith2fa",
                    new { rememberMe = Input.RememberMe, returnUrl });
            }

            if (result.IsLockedOut)
            {
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthLockout, "auth",
                    "Sign-in refused: account locked out after too many failed attempts.",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));
            }
            else
            {
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthLoginFailed, "auth",
                    "Sign-in failed: invalid credentials.",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));
            }

            // Show the SAME generic message for the locked-out and wrong-credentials
            // branches. A distinct "account is locked" message would confirm the
            // address is registered (an enumeration oracle); the real reason is kept
            // only in the audit log above.
            Error = "Email or password is incorrect.";
            return Page();
        }
    }
}
