using System.ComponentModel.DataAnnotations;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly ISecurityEventLog _audit;

        public RegisterModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, ISecurityEventLog audit)
        {
            _users = users;
            _signIn = signIn;
            _audit = audit;
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        // Honeypot. Real users never see or fill this (it is visually-hidden,
        // aria-hidden and out of the tab order); bots that fill every field trip
        // it. Not part of InputModel so it carries no validation attributes.
        [BindProperty] public string? Website { get; set; }

        public List<string> Errors { get; } = new();

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";

            [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password), Display(Name = "Password")]
            public string Password { get; set; } = "";

            [DataType(DataType.Password), Display(Name = "Confirm password")]
            [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
            public string ConfirmPassword { get; set; } = "";
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            // Honeypot tripped — almost certainly a bot. Silently drop the
            // submission without creating an account, mimicking the success
            // redirect so the bot gets no signal that it was rejected.
            if (!string.IsNullOrEmpty(Website))
                return LocalRedirect(ReturnUrlPolicy.Sanitize(Url, returnUrl));

            var email = Input.Email?.Trim() ?? "";
            // RemoteIpAddress sits behind the already-configured forwarded-headers
            // middleware, so this is the real client IP, not nginx's.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var user = new AppUser
            {
                UserName = Input.Email,   // email is the username (RequireUniqueEmail)
                Email = Input.Email,
                CreatedUtc = DateTime.UtcNow,
                Tier = "free",
            };

            var result = await _users.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                _audit.Record(new SecurityEvent(
                    DateTime.UtcNow, SecurityEventKind.AuthRegistration, "auth",
                    "New account registered.",
                    new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));
                await _signIn.SignInAsync(user, isPersistent: false);
                return LocalRedirect(ReturnUrlPolicy.Sanitize(Url, returnUrl));
            }

            // Map a duplicate email/username to a generic failure so registration
            // does not confirm that an address is already registered (an
            // enumeration oracle). Preserve every other validation message
            // (e.g. weak password) verbatim so users can still self-correct.
            bool duplicate = false;
            foreach (var e in result.Errors)
            {
                if (e.Code is "DuplicateEmail" or "DuplicateUserName")
                    duplicate = true;
                else
                    Errors.Add(e.Description);
            }
            if (duplicate)
                Errors.Add("We couldn't create your account with those details. Please check your information and try again.");

            return Page();
        }
    }
}
