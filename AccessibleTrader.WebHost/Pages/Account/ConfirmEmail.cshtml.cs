using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// The second half of changing an email address. A link is mailed to the NEW address;
    /// GET only checks the link and offers a button, and the POST performs the change.
    ///
    /// <para>
    /// <b>GET changes nothing on purpose.</b> Mail clients and link scanners fetch URLs
    /// before a person ever sees them; a GET that consumed the token would let a scanner
    /// silently change the account's address. A bad or expired link says so in words and
    /// POST leaves everything as it was.
    /// </para>
    ///
    /// <para>
    /// On success the username is set to the new address too (this app signs in by email),
    /// an already-signed-in session is refreshed so its cookie carries the new name, and a
    /// notice goes to the OLD address — the one place a hijack would otherwise go unnoticed.
    /// </para>
    /// </summary>
    public class ConfirmEmailModel : PageModel
    {
        // Identity's UserManager builds change-email token purposes as "ChangeEmail:{newEmail}".
        private const string ChangeEmailPurposePrefix = "ChangeEmail:";

        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly IAccountEmailSender _email;
        private readonly ISecurityEventLog _audit;
        private readonly TimeProvider _time;

        public ConfirmEmailModel(
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            IAccountEmailSender email,
            ISecurityEventLog audit,
            TimeProvider time)
        {
            _users = users;
            _signIn = signIn;
            _email = email;
            _audit = audit;
            _time = time;
        }

        [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
        [BindProperty(SupportsGet = true)] public string? Email { get; set; }
        [BindProperty(SupportsGet = true)] public string? Code { get; set; }

        /// <summary>True when the link is valid and the confirm button may render.</summary>
        public bool LinkValid { get; private set; }

        public string? Status { get; private set; }
        public string? Error { get; private set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await FindUserAsync();
            if (user == null || !await TokenValidAsync(user))
            {
                Error = InvalidLinkMessage;
                return Page();
            }

            LinkValid = true;
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await FindUserAsync();
            if (user == null || !await TokenValidAsync(user))
            {
                Error = InvalidLinkMessage;
                return Page();
            }

            var newEmail = Email!.Trim();
            var oldEmail = user.Email ?? "";
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var changed = await _users.ChangeEmailAsync(user, newEmail, Code!);
            if (!changed.Succeeded)
            {
                // InvalidToken is the ordinary expired/already-used case; anything else is
                // still a no-op and is said plainly without exposing internals.
                Error = InvalidLinkMessage;
                return Page();
            }

            var renamed = await _users.SetUserNameAsync(user, newEmail);
            if (!renamed.Succeeded)
            {
                Error = "Your email was changed, but your sign-in name could not be updated. "
                      + "Contact support before signing out.";
                return Page();
            }

            if (_signIn.IsSignedIn(User))
                await _signIn.RefreshSignInAsync(user);

            _audit.Record(new SecurityEvent(
                _time.GetUtcNow().UtcDateTime, SecurityEventKind.AccountEmailChanged, "auth",
                "Email address changed and confirmed from the new address.",
                new Dictionary<string, string>
                {
                    ["ip"] = ip,
                    ["email"] = oldEmail,
                    ["newEmail"] = newEmail,
                }));

            // Tell the OLD address. Best effort: the change has already happened, and the
            // page must not pretend otherwise if the relay is down.
            if (!string.IsNullOrEmpty(oldEmail))
            {
                await _email.SendAsync(
                    oldEmail,
                    "Your Accessible Trader email was changed",
                    "The email address on your Accessible Trader account was changed to "
                    + newEmail + " at " + _time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") + "."
                    + Environment.NewLine + Environment.NewLine
                    + "If this was not you, contact support.",
                    HttpContext.RequestAborted);
            }

            Status = $"Your email is now {newEmail}. Use it to sign in from now on.";
            LinkValid = false;
            return Page();
        }

        private const string InvalidLinkMessage =
            "That confirmation link is invalid or has expired. Nothing was changed.";

        private async Task<AppUser?> FindUserAsync()
        {
            if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(Email)) return null;
            return await _users.FindByIdAsync(UserId);
        }

        private async Task<bool> TokenValidAsync(AppUser user)
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Code)) return false;
            return await _users.VerifyUserTokenAsync(
                user,
                _users.Options.Tokens.ChangeEmailTokenProvider,
                ChangeEmailPurposePrefix + Email,
                Code);
        }
    }
}
