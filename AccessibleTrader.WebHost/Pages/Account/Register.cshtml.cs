using System.ComponentModel.DataAnnotations;
using System.Globalization;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    public class RegisterModel : PageModel
    {
        /// <summary>DataProtection purpose for the hidden fill-time stamp.</summary>
        internal const string FillTimeProtectorPurpose = "AccessibleTrader.Register.FillTime";

        /// <summary>How long a real person needs on the form before a POST counts.</summary>
        public static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(3);

        /// <summary>How old a stamp may be before it is refused.</summary>
        public static readonly TimeSpan MaximumFillAge = TimeSpan.FromHours(2);

        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly ISecurityEventLog _audit;
        private readonly IDataProtector _fillTimeProtector;
        private readonly TimeProvider _time;

        public RegisterModel(
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            ISecurityEventLog audit,
            IDataProtectionProvider dataProtection,
            TimeProvider time)
        {
            _users = users;
            _signIn = signIn;
            _audit = audit;
            _fillTimeProtector = dataProtection.CreateProtector(FillTimeProtectorPurpose);
            _time = time;
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        // Honeypot. Real users never see or fill this (it is visually-hidden,
        // aria-hidden and out of the tab order); bots that fill every field trip
        // it. Not part of InputModel so it carries no validation attributes.
        [BindProperty] public string? Website { get; set; }

        /// <summary>
        /// When the form was rendered, protected so a client cannot forge a time. A missing,
        /// tampered, stale or impossibly fast POST is treated exactly like the honeypot.
        /// </summary>
        [BindProperty] public string? FillTimeStamp { get; set; }

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

        public void OnGet() => FillTimeStamp = IssueFillTimeStamp();

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            if (!ModelState.IsValid) return Page();

            // Honeypot tripped — almost certainly a bot. Silently drop the
            // submission without creating an account, mimicking the success
            // redirect so the bot gets no signal that it was rejected.
            //
            // The fill-time check uses the same silent path: a form submitted in under
            // three seconds, with a stamp that is missing/tampered/stale, was not filled
            // in by a person. The stamp is DataProtection-signed on GET, so a bot cannot
            // simply forge an older time.
            if (!string.IsNullOrEmpty(Website) || !IsFillTimeAcceptable())
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

        private string IssueFillTimeStamp() =>
            _fillTimeProtector.Protect(
                _time.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

        private bool IsFillTimeAcceptable()
        {
            if (string.IsNullOrWhiteSpace(FillTimeStamp)) return false;

            long issuedMs;
            try
            {
                var raw = _fillTimeProtector.Unprotect(FillTimeStamp);
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out issuedMs))
                    return false;
            }
            catch
            {
                // Tampered, truncated, or protected with a different key ring.
                return false;
            }

            var issued = DateTimeOffset.FromUnixTimeMilliseconds(issuedMs);
            var age = _time.GetUtcNow() - issued;
            return age >= MinimumFillTime && age <= MaximumFillAge;
        }
    }
}
