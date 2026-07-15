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
    /// Consumes an out-of-band reset token (minted by the admin CLI, see
    /// Program.cs <c>--reset-link</c>) and lets the user set their own new password.
    /// No admin ever sees the password. Every failure — unknown email, bad/expired
    /// token, weak password — returns the SAME generic error so the page can't be
    /// used to enumerate which addresses are registered.
    /// </summary>
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly ISecurityEventLog _audit;

        public ResetPasswordModel(UserManager<AppUser> users, ISecurityEventLog audit)
        {
            _users = users;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        public List<string> Errors { get; } = new();

        public class InputModel
        {
            [Required] public string Email { get; set; } = "";

            [Required] public string Token { get; set; } = "";

            [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password), Display(Name = "New password")]
            public string NewPassword { get; set; } = "";

            [DataType(DataType.Password), Display(Name = "Confirm new password")]
            [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
            public string ConfirmPassword { get; set; } = "";
        }

        // Bind the email + token from the reset link into the hidden form fields so
        // they survive the POST. The token is opaque and already URL-decoded by the
        // model binder.
        public void OnGet(string? email, string? token)
        {
            Input.Email = email ?? "";
            Input.Token = token ?? "";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var email = Input.Email?.Trim() ?? "";
            // RemoteIpAddress sits behind the already-configured forwarded-headers
            // middleware, so this is the real client IP, not nginx's.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var user = await _users.FindByEmailAsync(email);
            if (user is not null)
            {
                var result = await _users.ResetPasswordAsync(user, Input.Token, Input.NewPassword);
                if (result.Succeeded)
                {
                    _audit.Record(new SecurityEvent(
                        DateTime.UtcNow, SecurityEventKind.AuthPasswordReset, "auth",
                        "Password reset via reset token.",
                        new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));
                    return RedirectToPage("Login", new { reset = 1 });
                }

                // Preserve genuine password-policy failures (e.g. too short) so the
                // user can self-correct. An invalid/expired token surfaces as a
                // token error — collapse those to the generic message below rather
                // than leak that the email was valid but the link was stale.
                bool tokenError = false;
                foreach (var e in result.Errors)
                {
                    if (e.Code is "InvalidToken")
                        tokenError = true;
                    else
                        Errors.Add(e.Description);
                }
                if (tokenError && Errors.Count == 0)
                    Errors.Add("That reset link is invalid or has expired. Ask the site owner for a new one.");

                if (Errors.Count > 0)
                    return Page();
            }

            // User is null (unknown email) OR the token was invalid: return the SAME
            // generic failure either way so this page is not an enumeration oracle.
            if (Errors.Count == 0)
                Errors.Add("That reset link is invalid or has expired. Ask the site owner for a new one.");
            return Page();
        }
    }
}
