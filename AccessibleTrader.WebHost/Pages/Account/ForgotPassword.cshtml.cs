using System.ComponentModel.DataAnnotations;
using AccessibleTrader.Sdk.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// Sets expectations for a forgotten password without leaking which emails
    /// exist. There is no outbound mail server, so this page can't send anything
    /// itself — on POST it ALWAYS shows the same neutral message regardless of
    /// whether the address is registered (closes the enumeration oracle) and
    /// records the request in the security log so the admin can mint a reset link
    /// out of band (Program.cs <c>--reset-link</c>). POSTs here are in the strict
    /// <c>auth:{ip}</c> rate-limit tier — listed explicitly in
    /// <c>AuthRateLimitPolicy.IsAuthMutation</c>, because every POST writes an
    /// attacker-supplied email to the security log.
    /// </summary>
    public class ForgotPasswordModel : PageModel
    {
        private readonly ISecurityEventLog _audit;

        public ForgotPasswordModel(IConfiguration config, ISecurityEventLog audit)
        {
            _audit = audit;
            SupportEmail = config["Accounts:SupportEmail"] ?? "support@accessibletrader.com";
        }

        [BindProperty] public InputModel Input { get; set; } = new();

        /// <summary>True once the form has been posted — flips the view to the neutral message.</summary>
        public bool Submitted { get; set; }

        /// <summary>Where users are told to reach out; configurable via <c>Accounts:SupportEmail</c>.</summary>
        public string SupportEmail { get; }

        public class InputModel
        {
            [Required, EmailAddress, Display(Name = "Email")]
            public string Email { get; set; } = "";
        }

        public void OnGet() { }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid) return Page();

            var email = Input.Email?.Trim() ?? "";
            // RemoteIpAddress sits behind the already-configured forwarded-headers
            // middleware, so this is the real client IP, not nginx's.
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Record the request so the admin sees it in the security log and can run
            // the reset-link CLI. We do NOT look the user up here: doing so and only
            // logging real accounts would recreate the enumeration oracle in the log,
            // and the admin verifies existence when they run the CLI anyway.
            _audit.Record(new SecurityEvent(
                DateTime.UtcNow, SecurityEventKind.AuthPasswordResetRequested, "auth",
                "Password-reset link requested.",
                new Dictionary<string, string> { ["ip"] = ip, ["email"] = email }));

            // ALWAYS the same neutral outcome — no signal about whether the email exists.
            Submitted = true;
            return Page();
        }
    }
}
