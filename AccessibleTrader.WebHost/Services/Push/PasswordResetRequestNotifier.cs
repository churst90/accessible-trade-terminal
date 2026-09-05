using AccessibleTrader.WebHost.Account;
using Microsoft.AspNetCore.Identity;

namespace AccessibleTrader.WebHost.Services.Push
{
    /// <summary>
    /// Tells the OPERATOR that someone asked for a password reset.
    ///
    /// <para>
    /// There is no mail server on the hosted box, so <c>ForgotPassword</c> cannot send a link;
    /// it records an <c>AuthPasswordResetRequested</c> security event and an admin runs the
    /// <c>--reset-link</c> CLI. That design is right (hosted notes §4c) and its operational
    /// consequence was not: the event sat in a log file until a human read it, and two
    /// requests went unread for ten days each. Neither was real — but only luck made that so.
    /// </para>
    ///
    /// <para>
    /// The notifier is the missing half: the request goes to the owner account's Web Push
    /// subscriptions — the same channel, keys and store the alert monitor already uses — and
    /// to the journal at Warning, which a log scrape can key on. It never touches the account
    /// the request named: looking that up would rebuild the enumeration oracle the page was
    /// written to avoid, and the admin verifies existence when they run the CLI anyway.
    /// </para>
    /// </summary>
    public interface IPasswordResetRequestNotifier
    {
        /// <summary>
        /// Fire-and-forget: returns at once, never throws, so the page's neutral response is
        /// the same whatever happens here. <paramref name="requestedEmail"/> is what the
        /// visitor typed, unverified; <paramref name="ip"/> the client address behind nginx.
        /// </summary>
        void Notify(string requestedEmail, string ip);
    }

    public sealed class OwnerPushResetRequestNotifier : IPasswordResetRequestNotifier
    {
        /// <summary>Configuration key naming the account that receives the push.</summary>
        public const string OwnerEmailKey = "Accounts:OwnerEmail";

        private readonly string? _ownerEmail;
        private readonly IServiceScopeFactory _scopes;
        private readonly IWebPushSender _push;
        private readonly ILogger<OwnerPushResetRequestNotifier> _logger;

        public OwnerPushResetRequestNotifier(
            IConfiguration config,
            IServiceScopeFactory scopes,
            IWebPushSender push,
            ILogger<OwnerPushResetRequestNotifier> logger)
        {
            // The seeded owner account is the operator by construction; a separate key lets a
            // deployment route the push elsewhere without re-seeding.
            _ownerEmail = config[OwnerEmailKey];
            if (string.IsNullOrWhiteSpace(_ownerEmail))
                _ownerEmail = Environment.GetEnvironmentVariable("ACCOUNTS_SEED_EMAIL");
            _scopes = scopes;
            _push = push;
            _logger = logger;
        }

        /// <summary>The address the push goes to, for the operator's own diagnostics.</summary>
        public string? OwnerEmail => _ownerEmail;

        public void Notify(string requestedEmail, string ip)
        {
            // Warning, not Information: the journal is the fallback channel, and the point of
            // this line is that a scrape keyed on severity finds it.
            _logger.LogWarning(
                "Password reset requested for {Email} from {Ip}. Deliver a link with: --accounts --reset-link {Email}",
                requestedEmail, ip, requestedEmail);

            if (string.IsNullOrWhiteSpace(_ownerEmail))
            {
                _logger.LogWarning(
                    "No owner account is configured ({Key} / ACCOUNTS_SEED_EMAIL), so the reset request "
                    + "was NOT pushed to anyone. It is in this journal and nowhere else.", OwnerEmailKey);
                return;
            }

            _ = Task.Run(() => PushToOwnerAsync(requestedEmail, ip));
        }

        private async Task PushToOwnerAsync(string requestedEmail, string ip)
        {
            try
            {
                string? ownerId;
                using (var scope = _scopes.CreateScope())
                {
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
                    ownerId = (await users.FindByEmailAsync(_ownerEmail!))?.Id;
                }
                if (ownerId == null)
                {
                    _logger.LogWarning("Owner account {Owner} does not exist; the reset request was not pushed.", _ownerEmail);
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await _push.SendToUserAsync(ownerId,
                    "Password reset requested",
                    $"{requestedEmail} asked for a reset link from {ip}. Run --reset-link {requestedEmail} on the server.",
                    cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pushing the password-reset request to the owner failed.");
            }
        }
    }
}
