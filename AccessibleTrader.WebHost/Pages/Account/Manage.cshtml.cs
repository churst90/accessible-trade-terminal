using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text.Json;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.WebHost.Account;
using AccessibleTrader.WebHost.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AccessibleTrader.WebHost.Pages.Account
{
    /// <summary>
    /// The signed-in account page: display name, leaderboard choice, change password,
    /// change email, download a copy of the account's data, and delete the account.
    ///
    /// <para>
    /// Each section is its own form and handler, so a rejected field in one never wipes the
    /// input of another. Every success is recorded in the per-user security log with the
    /// client IP, as registration and sign-in already are. Destructive actions re-confirm the
    /// CURRENT password (through <c>CheckPasswordSignInAsync</c>, so failures count toward
    /// the same lockout as the front door), and deletion additionally requires the second
    /// factor when it is on.
    /// </para>
    ///
    /// <para>
    /// <b>Deletion stops memory before it removes disk.</b> The paper account is shared per
    /// user through <see cref="PaperAccountHub"/> and can keep writing its file; it is evicted
    /// and torn down first, then the Identity row is deleted, then the user's folder — and
    /// only after <see cref="AccountDataFolderGuard"/> proves it is inside
    /// <c>{dataRoot}/users/</c> and is not <c>users/anon</c>.
    /// </para>
    /// </summary>
    [Authorize]
    public class ManageModel : PageModel
    {
        private readonly UserManager<AppUser> _users;
        private readonly SignInManager<AppUser> _signIn;
        private readonly IAccountEmailSender _email;
        private readonly ISecurityEventLog _audit;
        private readonly IPlatformPathService _paths;
        private readonly IConfiguration _config;
        private readonly PaperAccountHub _paper;
        private readonly QuickTradeEquityHub _equity;
        private readonly IEnumerable<IHostedService> _hostedServices;
        private readonly TimeProvider _time;
        private readonly ILogger<ManageModel> _logger;

        public ManageModel(
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            IAccountEmailSender email,
            ISecurityEventLog audit,
            IPlatformPathService paths,
            IConfiguration config,
            PaperAccountHub paper,
            QuickTradeEquityHub equity,
            IEnumerable<IHostedService> hostedServices,
            TimeProvider time,
            ILogger<ManageModel> logger)
        {
            _users = users;
            _signIn = signIn;
            _email = email;
            _audit = audit;
            _paths = paths;
            _config = config;
            _paper = paper;
            _equity = equity;
            _hostedServices = hostedServices;
            _time = time;
            _logger = logger;
        }

        public string Email { get; private set; } = "";
        public bool TwoFactorEnabled { get; private set; }
        public bool EmailServiceConfigured => _email.IsConfigured;
        public string? Status { get; private set; }
        public string? Error { get; private set; }

        [BindProperty] public string? NewDisplayName { get; set; }
        [BindProperty] public bool NewLeaderboardOptIn { get; set; }
        [BindProperty] public ChangePasswordInput PasswordInput { get; set; } = new();
        [BindProperty] public ChangeEmailInput EmailInput { get; set; } = new();
        [BindProperty] public DeleteAccountInput DeleteInput { get; set; } = new();

        public class ChangePasswordInput
        {
            [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password)]
            [Display(Name = "Current password")]
            public string CurrentPassword { get; set; } = "";

            [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password)]
            [Display(Name = "New password")]
            public string NewPassword { get; set; } = "";

            [DataType(DataType.Password)]
            [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
            [Display(Name = "Confirm new password")]
            public string ConfirmPassword { get; set; } = "";
        }

        public class ChangeEmailInput
        {
            [Required, EmailAddress, Display(Name = "New email")]
            public string NewEmail { get; set; } = "";

            [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password)]
            [Display(Name = "Current password")]
            public string CurrentPassword { get; set; } = "";
        }

        public class DeleteAccountInput
        {
            [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password)]
            [Display(Name = "Current password")]
            public string CurrentPassword { get; set; } = "";

            [Display(Name = "I understand this cannot be undone")]
            public bool Understands { get; set; }

            [Display(Name = "Authenticator or recovery code")]
            public string? TwoFactorCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            await LoadAsync(user);
            return Page();
        }

        // ── Display name ─────────────────────────────────────────────────────

        public async Task<IActionResult> OnPostDisplayNameAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState(nameof(NewDisplayName), nameof(NewLeaderboardOptIn));

            if (!DisplayNameRules.TryNormalize(NewDisplayName, out var normalized, out var nameError))
            {
                ModelState.AddModelError(nameof(NewDisplayName), nameError!);
                await LoadAsync(user);
                return Page();
            }

            if (normalized != null &&
                await _users.Users.AnyAsync(u => u.Id != user.Id && u.DisplayName == normalized))
            {
                ModelState.AddModelError(nameof(NewDisplayName),
                    "That display name is already taken. Names are unique regardless of letter case.");
                await LoadAsync(user);
                return Page();
            }

            user.DisplayName = normalized;
            if (normalized == null)
                user.LeaderboardOptIn = false;   // no name, no leaderboard

            IdentityResult updated;
            try
            {
                updated = await _users.UpdateAsync(user);
            }
            catch (DbUpdateException)
            {
                // The partial unique index is the final authority. Two accounts saving the
                // same name at once is a race the earlier AnyAsync check cannot close.
                ModelState.AddModelError(nameof(NewDisplayName),
                    "That display name is already taken. Names are unique regardless of letter case.");
                await LoadAsync(user);
                return Page();
            }

            if (!updated.Succeeded)
            {
                if (updated.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                {
                    ModelState.AddModelError(nameof(NewDisplayName),
                        "That display name is already taken. Names are unique regardless of letter case.");
                }
                else
                {
                    Error = "We couldn't save your display name. Nothing was changed.";
                }
                await LoadAsync(user);
                return Page();
            }

            Audit(user, SecurityEventKind.AccountDisplayNameChanged,
                normalized == null
                    ? "Display name cleared (leaderboard opt-in turned off)."
                    : "Display name set.");

            Status = normalized == null
                ? "Display name cleared. You are no longer on the public leaderboard."
                : "Your display name was saved.";
            await LoadAsync(user);
            return Page();
        }

        // ── Leaderboard opt-in ───────────────────────────────────────────────

        public async Task<IActionResult> OnPostLeaderboardAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState(nameof(NewLeaderboardOptIn));

            if (NewLeaderboardOptIn && string.IsNullOrWhiteSpace(user.DisplayName))
            {
                Error = "Set a display name first.";
                await LoadAsync(user);
                return Page();
            }

            user.LeaderboardOptIn = NewLeaderboardOptIn;
            var updated = await _users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                Error = "We couldn't save your leaderboard choice. Nothing was changed.";
                await LoadAsync(user);
                return Page();
            }

            Audit(user, SecurityEventKind.AccountLeaderboardOptInChanged,
                NewLeaderboardOptIn ? "Leaderboard opt-in turned on." : "Leaderboard opt-in turned off.");

            Status = NewLeaderboardOptIn
                ? "You will appear on the public leaderboard with your display name."
                : "You will not appear on the public leaderboard.";
            await LoadAsync(user);
            return Page();
        }

        // ── Change password ──────────────────────────────────────────────────

        public async Task<IActionResult> OnPostPasswordAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState(nameof(PasswordInput));

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var result = await _users.ChangePasswordAsync(
                user, PasswordInput.CurrentPassword, PasswordInput.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var e in result.Errors)
                {
                    var field = e.Code == "PasswordMismatch"
                        ? nameof(PasswordInput) + "." + nameof(PasswordInput.CurrentPassword)
                        : nameof(PasswordInput) + "." + nameof(PasswordInput.NewPassword);
                    ModelState.AddModelError(field,
                        e.Code == "PasswordMismatch"
                            ? "Current password is incorrect — your password was not changed."
                            : e.Description);
                }
                await LoadAsync(user);
                return Page();
            }

            await _signIn.RefreshSignInAsync(user);
            Audit(user, SecurityEventKind.AccountPasswordChanged, "Password changed from the account page.");

            bool mailed = false;
            if (_email.IsConfigured)
            {
                var when = _time.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
                mailed = await _email.SendAsync(
                    user.Email ?? "",
                    "Your Accessible Trader password was changed",
                    "The password for this Accessible Trader account was changed at "
                    + when + "."
                    + Environment.NewLine + Environment.NewLine
                    + "If this was not you, reset your password at "
                    + PublicBaseUrl + "/account/forgotpassword and contact support.",
                    HttpContext.RequestAborted);
            }

            Status = mailed
                ? "Your password was changed. A confirmation email is on its way."
                : "Your password was changed.";
            await LoadAsync(user);
            return Page();
        }

        // ── Change email ─────────────────────────────────────────────────────

        public async Task<IActionResult> OnPostEmailAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState(nameof(EmailInput));

            if (!_email.IsConfigured)
            {
                Error = "Changing your email needs the email service, which is not set up on this server.";
                await LoadAsync(user);
                return Page();
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var newEmail = EmailInput.NewEmail.Trim();
            if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(EmailInput) + "." + nameof(EmailInput.NewEmail), "That is already your email address.");
                await LoadAsync(user);
                return Page();
            }

            var existing = await _users.FindByEmailAsync(newEmail);
            if (existing is not null && existing.Id != user.Id)
            {
                // The caller is signed in and proved their password, so plainly naming the
                // collision leaks nothing to a stranger.
                ModelState.AddModelError(nameof(EmailInput) + "." + nameof(EmailInput.NewEmail),
                    "That address is already used by another account.");
                await LoadAsync(user);
                return Page();
            }

            if (!await ConfirmPasswordAsync(user, EmailInput.CurrentPassword, nameof(EmailInput) + "." + nameof(EmailInput.CurrentPassword)))
            {
                await LoadAsync(user);
                return Page();
            }

            var token = await _users.GenerateChangeEmailTokenAsync(user, newEmail);
            var link = PublicBaseUrl + "/account/confirmemail"
                + $"?userId={Uri.EscapeDataString(user.Id)}"
                + $"&email={Uri.EscapeDataString(newEmail)}"
                + $"&code={Uri.EscapeDataString(token)}";

            bool sent = await _email.SendAsync(
                newEmail,
                "Confirm your new Accessible Trader email address",
                "Open this link to confirm this as the email address for your Accessible Trader account:"
                + Environment.NewLine + link
                + Environment.NewLine + Environment.NewLine
                + $"Your email stays {user.Email} until you open it.",
                HttpContext.RequestAborted);

            Audit(user, SecurityEventKind.AccountEmailChangeRequested,
                sent
                    ? "Email change requested; confirmation link sent to the new address."
                    : "Email change requested, but the confirmation email could not be sent.");

            if (!sent)
            {
                Error = "We couldn't send the confirmation email. Your email address has not been changed.";
                await LoadAsync(user);
                return Page();
            }

            Status = $"We sent a link to {newEmail}. Your email stays {user.Email} until you open it.";
            await LoadAsync(user);
            return Page();
        }

        // ── Download my data ─────────────────────────────────────────────────

        public async Task<IActionResult> OnPostDownloadAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState();

            var folder = _paths.AppDataDirectory;

            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                        if (!written.Add(relative)) continue;

                        var entry = zip.CreateEntry(relative, CompressionLevel.Optimal);
                        await using var target = entry.Open();
                        await using var source = System.IO.File.OpenRead(file);
                        await source.CopyToAsync(target, HttpContext.RequestAborted);
                    }
                }

                // account.json is generated, not copied from disk. Deliberately ONLY the
                // harmless profile fields: no password hash, security stamp, authenticator
                // key, recovery codes or tokens — a download must never be a credential.
                var accountEntry = zip.CreateEntry("account.json", CompressionLevel.Optimal);
                await using var accountStream = accountEntry.Open();
                var account = new
                {
                    email = user.Email,
                    displayName = user.DisplayName,
                    leaderboardOptIn = user.LeaderboardOptIn,
                    createdUtc = user.CreatedUtc,
                    lastSeenUtc = user.LastSeenUtc,
                    tier = user.Tier,
                    twoFactorEnabled = await _users.GetTwoFactorEnabledAsync(user),
                };
                await JsonSerializer.SerializeAsync(
                    accountStream, account, new JsonSerializerOptions { WriteIndented = true },
                    HttpContext.RequestAborted);
            }

            Audit(user, SecurityEventKind.AccountDataDownloaded, "Account data downloaded as a zip.");

            var name = $"accessible-trader-data-{_time.GetUtcNow():yyyyMMdd}.zip";
            return File(buffer.ToArray(), "application/zip", name);
        }

        // ── Delete account ───────────────────────────────────────────────────

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            var user = await _users.GetUserAsync(User);
            if (user == null) return RedirectToPage("Login");
            KeepModelState(nameof(DeleteInput));

            if (!DeleteInput.Understands)
            {
                ModelState.AddModelError(nameof(DeleteInput) + "." + nameof(DeleteInput.Understands),
                    "Tick the box to confirm you understand this cannot be undone.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            if (!await ConfirmPasswordAsync(user, DeleteInput.CurrentPassword, nameof(DeleteInput) + "." + nameof(DeleteInput.CurrentPassword)))
            {
                await LoadAsync(user);
                return Page();
            }

            if (await _users.GetTwoFactorEnabledAsync(user))
            {
                if (!await ConfirmTwoFactorAsync(user, DeleteInput.TwoFactorCode))
                {
                    ModelState.AddModelError(nameof(DeleteInput) + "." + nameof(DeleteInput.TwoFactorCode),
                        "That authenticator or recovery code didn't work. Nothing was deleted.");
                    await LoadAsync(user);
                    return Page();
                }
            }

            // Prove the folder is this user's BEFORE touching anything. Refusing here leaves
            // the account intact, which is the only safe failure: deleting the Identity row
            // and then discovering the path was wrong would strand the data with no owner.
            var folder = _paths.AppDataDirectory;
            var dataRoot = DataRoot;
            if (!AccountDataFolderGuard.IsSafeUserFolder(dataRoot, folder, out var fullFolder))
            {
                _logger.LogError(
                    "Refusing to delete account data at {Folder}: it is not inside {UsersRoot}/users or is the anon folder.",
                    folder, dataRoot);
                Error = "We couldn't safely identify your data folder, so the account was not deleted.";
                await LoadAsync(user);
                return Page();
            }

            // 1. Stop and drop everything the process holds for this user, so no shared
            //    account or per-user cache writes their files back after the delete.
            _paper.Evict(user.Id);
            _equity.Evict(user.Id);
            WebHostBrowserCircuitHandler.ForgetUser(user.Id);
            foreach (var hosted in _hostedServices)
            {
                if (hosted is HostedAlertMonitor monitor) monitor.ForgetUser(user.Id);
            }

            // 2. The Identity row.
            var deleted = await _users.DeleteAsync(user);
            if (!deleted.Succeeded)
            {
                _logger.LogWarning("Account deletion failed for {User}: {Errors}",
                    user.Id, string.Join("; ", deleted.Errors.Select(e => e.Description)));
                Error = "We couldn't delete your account. Nothing was changed.";
                await LoadAsync(user);
                return Page();
            }

            // 3. Sign out, then clear the request principal BEFORE auditing. The audit sink
            //    resolves its directory from the ambient HttpContext per event, so an event
            //    recorded while the cookie's claims are still visible would be written into
            //    the very folder about to be deleted — recreating it, and then losing the
            //    record with it. An anonymous principal sends this event to the instance log,
            //    where an operator can still see that the account was deleted.
            await _signIn.SignOutAsync();
            HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity());

            Audit(user, SecurityEventKind.AccountDeleted,
                "Account deleted: Identity row, in-memory state and data folder removed.");

            // 4. The data folder, already proven safe above.
            try
            {
                if (Directory.Exists(fullFolder)) Directory.Delete(fullFolder, recursive: true);
            }
            catch (Exception ex)
            {
                // The account is gone; this is the one failure we can only report to the
                // operator, not to the (now signed-out) user.
                _logger.LogError(ex, "Account {User} was deleted but its data folder {Folder} could not be removed.",
                    user.Id, fullFolder);
            }

            TempData["LoginStatus"] = "Your account has been deleted.";
            return RedirectToPage("Login");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private string PublicBaseUrl =>
            (_config["Accounts:PublicBaseUrl"] ?? "https://accessibletrader.com/terminal").TrimEnd('/');

        private string DataRoot => string.IsNullOrWhiteSpace(_config["Accounts:DataRoot"])
            ? new WebHostPathService().AppDataDirectory
            : _config["Accounts:DataRoot"]!;

        private async Task<bool> ConfirmPasswordAsync(AppUser user, string? password, string field)
        {
            if (!string.IsNullOrEmpty(password))
            {
                // Same lockout accounting as the front door and the security page: a stolen
                // session must not get unlimited password guesses here.
                var result = await _signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
                if (result.Succeeded) return true;

                if (result.IsLockedOut)
                {
                    Audit(user, SecurityEventKind.AuthLockout,
                        "Account-page re-confirmation refused: account locked out after too many failed attempts.");
                    Error = "Too many incorrect passwords. This account is locked for 15 minutes; no changes were made.";
                    return false;
                }

                Audit(user, SecurityEventKind.AuthReauthenticationFailed,
                    "Account-page re-confirmation failed: incorrect current password.");
            }

            ModelState.AddModelError(field, "Current password is incorrect — no changes were made.");
            return false;
        }

        private async Task<bool> ConfirmTwoFactorAsync(AppUser user, string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            var authenticator = TwoFactorSupport.NormalizeCode(code);
            if (await _users.VerifyTwoFactorTokenAsync(
                    user, _users.Options.Tokens.AuthenticatorTokenProvider, authenticator))
                return true;

            var recovery = await _users.RedeemTwoFactorRecoveryCodeAsync(
                user, TwoFactorSupport.NormalizeRecoveryCode(code));
            return recovery.Succeeded;
        }

        /// <summary>
        /// Drop validation entries belonging to the OTHER forms on the page.
        ///
        /// <para>
        /// All the section inputs are bound properties on one PageModel, and ASP.NET
        /// validates the whole model after binding. A POST from the email form therefore
        /// arrived with empty, required password/delete fields and <c>ModelState.IsValid</c>
        /// false before the handler could read its own fields. Keeping only the prefix the
        /// handler owns makes each form validate itself — separate forms, separate errors,
        /// exactly as the page's structure promises.
        /// </para>
        /// </summary>
        private void KeepModelState(params string[] prefixes)
        {
            var remove = ModelState.Keys
                .Where(key => !prefixes.Any(prefix =>
                    key.Equals(prefix, StringComparison.Ordinal)
                    || key.StartsWith(prefix + ".", StringComparison.Ordinal)))
                .ToList();

            foreach (var key in remove)
                ModelState.Remove(key);
        }

        private async Task LoadAsync(AppUser user)
        {
            Email = user.Email ?? "";
            NewDisplayName = user.DisplayName ?? "";
            NewLeaderboardOptIn = user.LeaderboardOptIn;
            TwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(user);
        }

        private void Audit(AppUser user, SecurityEventKind kind, string message) =>
            _audit.Record(new SecurityEvent(
                _time.GetUtcNow().UtcDateTime, kind, "auth", message,
                new Dictionary<string, string>
                {
                    ["ip"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    ["email"] = user.Email ?? "",
                }));
    }
}
