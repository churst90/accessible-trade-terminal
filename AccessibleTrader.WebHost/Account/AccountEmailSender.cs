using System.Net;
using System.Net.Mail;

namespace AccessibleTrader.WebHost.Account
{
    /// <summary>
    /// Outbound account mail (password-reset links, "your password/email changed" notices,
    /// change-email confirmation links). Deliberately tiny: a page hands it a recipient, a
    /// subject and a plain-text body, and gets back whether delivery was accepted.
    ///
    /// <para>
    /// <b>It never throws to a page.</b> Mail is a side effect of an account action, never the
    /// action itself — a dead SMTP relay must not turn "change your password" into a 500. Every
    /// failure is logged and reported as <c>false</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Configuration:</b> section <c>AccountEmail</c> — <c>Host</c> (default
    /// smtp-relay.brevo.com), <c>Port</c> (587), <c>User</c>, <c>Password</c>, <c>From</c>
    /// (noreply@accessibletrader.com) and <c>FromName</c> (Accessible Trader). On the server
    /// these arrive as environment variables (<c>AccountEmail__Password</c>). Only
    /// <c>User</c> and <c>Password</c> decide <see cref="IsConfigured"/>: with no credentials
    /// there is nothing to authenticate with, and callers fall back to their old behaviour.
    /// </para>
    /// </summary>
    public interface IAccountEmailSender
    {
        /// <summary>True only when both the SMTP user and password are present.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Sends one plain-text email. Returns false on any failure (logged, never thrown).
        /// The shared sign-off and support address are appended by the sender.
        /// </summary>
        Task<bool> SendAsync(
            string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Brevo relay over STARTTLS on port 587. Mirrors
    /// <c>AccessibleTrader.Core/Services/Alerts/EmailAlertChannel.cs</c>, which has used
    /// <see cref="SmtpClient"/> for alert mail since before accounts existed.
    ///
    /// <para>
    /// <b>Never log what is in the mail.</b> Reset and change-email links carry Identity
    /// tokens; writing a body or a URL to the journal would persist a live credential. The
    /// logs say the recipient's DOMAIN, the subject and success/failure — enough to operate
    /// the relay, nothing that can be replayed.
    /// </para>
    /// </summary>
    public sealed class SmtpAccountEmailSender : IAccountEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpAccountEmailSender> _logger;
        private readonly Func<MailMessage, CancellationToken, Task>? _sendOverride;

        public SmtpAccountEmailSender(
            IConfiguration config,
            ILogger<SmtpAccountEmailSender> logger)
            : this(config, logger, sendOverride: null) { }

        /// <summary>Test seam: replaces the network call while keeping the real logging.</summary>
        internal SmtpAccountEmailSender(
            IConfiguration config,
            ILogger<SmtpAccountEmailSender> logger,
            Func<MailMessage, CancellationToken, Task>? sendOverride)
        {
            _config = config;
            _logger = logger;
            _sendOverride = sendOverride;
        }

        private string Host => Value("AccountEmail:Host", "smtp-relay.brevo.com");
        private int Port => int.TryParse(_config["AccountEmail:Port"], out var p) ? p : 587;
        private string? User => _config["AccountEmail:User"];
        private string? Password => _config["AccountEmail:Password"];
        private string From => Value("AccountEmail:From", "noreply@accessibletrader.com");
        private string FromName => Value("AccountEmail:FromName", "Accessible Trader");
        private string SupportEmail => Value("Accounts:SupportEmail", "support@accessibletrader.com");

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(User) && !string.IsNullOrWhiteSpace(Password);

        private string Value(string key, string fallback) =>
            string.IsNullOrWhiteSpace(_config[key]) ? fallback : _config[key]!;

        public async Task<bool> SendAsync(
            string toAddress, string subject, string plainTextBody, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning(
                    "Account email to {Domain} with subject {Subject} was not sent: SMTP credentials are not configured.",
                    DomainOf(toAddress), subject);
                return false;
            }

            string domain = DomainOf(toAddress);
            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(From, FromName),
                    Subject = subject,
                    Body = plainTextBody + Environment.NewLine + Environment.NewLine
                        + "This address does not receive replies. If you did not ask for this, "
                        + $"you can ignore this email, or contact {SupportEmail}.",
                    IsBodyHtml = false,
                };
                message.To.Add(toAddress);

                if (_sendOverride != null)
                {
                    await _sendOverride(message, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using var client = new SmtpClient(Host, Port)
                    {
                        EnableSsl = true,                  // STARTTLS on 587
                        Timeout = 20_000,
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                    };
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(User, Password);
                    await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Account email to {Domain} with subject {Subject} was sent.",
                    domain, subject);
                return true;
            }
            catch (Exception ex)
            {
                // The exception is logged for the operator, but the message body (which may
                // carry a token or link) is never part of it.
                _logger.LogWarning(ex,
                    "Account email to {Domain} with subject {Subject} failed.",
                    domain, subject);
                return false;
            }
        }

        /// <summary>
        /// The part after '@', or "(no domain)" for an address that has none. Only this half
        /// reaches the log — the local part is the person.
        /// </summary>
        internal static string DomainOf(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return "(no address)";
            int at = address.LastIndexOf('@');
            return at >= 0 && at + 1 < address.Length ? address[(at + 1)..] : "(no domain)";
        }
    }
}
