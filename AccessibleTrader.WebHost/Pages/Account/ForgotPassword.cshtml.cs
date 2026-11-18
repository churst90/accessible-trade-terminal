using System.ComponentModel.DataAnnotations;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// Starts a password reset without leaking which emails exist. When the email service
    /// is configured it generates an Identity reset token and emails the link straight to
    /// the address on file (throttled: one per address per five minutes). When it is not
    /// configured it falls back to today's admin-mediated flow — the request is recorded and
    /// the owner is told, and the admin mints a link with Program.cs <c>--reset-link</c>.
    ///
    /// <para>
    /// Either way the page ALWAYS shows the same neutral result, whether or not the address
    /// belongs to an account: no enumeration oracle. POSTs here are in the strict
    /// <c>auth:{ip}</c> rate-limit tier — listed explicitly in
    /// <c>AuthRateLimitPolicy.IsAuthMutation</c>, because every POST writes an
    /// attacker-supplied email to the security log and may spend a relay send.
    /// </para>
    /// </summary>
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly IAccountEmailSender _email;
        private readonly PasswordResetEmailThrottle _throttle;
        private readonly ISecurityEventLog _audit;
        private readonly Services.Push.IPasswordResetRequestNotifier? _notifier;
        private readonly string _publicBaseUrl;

        public ForgotPasswordModel(
            IConfiguration config,
            UserManager<AppUser> users,
            IAccountEmailSender email,
            PasswordResetEmailThrottle throttle,
            ISecurityEventLog audit,
            Services.Push.IPasswordResetRequestNotifier? notifier = null)
        {
            _users = users;
            _email = email;
            _throttle = throttle;
            _audit = audit;
            _notifier = notifier;
            SupportEmail = config["Accounts:SupportEmail"] ?? "support@accessibletrader.com";
            _publicBaseUrl = (config["Accounts:PublicBaseUrl"] ?? "https://accessibletrader.com/terminal").TrimEnd('/');
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        /// <summary>True once the form has been posted — flips the view to the neutral message.</summary>
        public bool Submitted { get; set; }

        /// <summary>Where users are told to reach out; configurable via <c>Accounts:SupportEmail</c>.</summary>
        public string SupportEmail { get; }

        /// <summary>Which neutral sentence the view shows: mailed link vs contact support.</summary>
        public bool EmailConfigured => _email.IsConfigured;

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var email = Input.Email?.Trim() ?? "";
            // RemoteIpAddress sits behind the already-configured forwarded-headers
            // middleware, so this is the real client IP, not nginx's.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Record the request either way. We deliberately record even an unknown address
            // (the request is attacker-supplied text; logging only real accounts would make
            // the log an enumeration oracle).
            _audit.Record(new SecurityEvent(
                DateTime.UtcNow, SecurityEventKind.AuthPasswordResetRequested, "auth",
                "Password-reset link requested.",
                new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));

            if (_email.IsConfigured)
            {
                // Look the address up only when the token can actually be delivered. The
                // response is identical for unknown addresses and throttled repeats.
                var user = await _users.FindByEmailAsync(email);
                if (user is not null && _throttle.TryBegin(email))
                {
                    var token = await _users.GeneratePasswordResetTokenAsync(user);
                    var link = $"{_publicBaseUrl}/account/resetpassword" +
                               $"?email={Uri.EscapeDataString(email)}" +
                               $"&token={Uri.EscapeDataString(token)}";

                    // The body is handed to the sender; its logs name the domain and subject
                    // only, never the link.
                    await _email.SendAsync(
                        email,
                        "Reset your Accessible Trader password",
                        "Someone asked to reset the password for this Accessible Trader account."
                        + Environment.NewLine + Environment.NewLine
                        + "Open this link to choose a new password:"
                        + Environment.NewLine + link
                        + Environment.NewLine + Environment.NewLine
                        + "If you did not ask for this, you can ignore this email; your password has not changed.",
                        HttpContext.RequestAborted);
                }
            }
            else
            {
                // Today's fallback: tell the operator, because a security event nobody reads
                // is a locked-out user nobody helps. Fire-and-forget and never throws.
                _notifier?.Notify(email, ip);
            }

            // ALWAYS the same neutral outcome — no signal about whether the email exists.
            Submitted = true;
            return Page();
        }
    }
}
