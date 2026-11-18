using System.Net;
using System.Net.Mail;
using AccessibleTrader.WebHost.Account;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Tests.WebHost;

/// <summary>
/// The outbound account-mail seam and the ForgotPassword flow that uses it. No test opens
/// a socket: the sender's network call is replaced, and the integration host swaps in
/// <see cref="RecordingAccountEmailSender"/>.
/// </summary>
public class AccountEmailSenderTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _lines;
        public CapturingLogger(List<string> lines) => _lines = lines;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => _lines.Add(formatter(state, exception));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public async Task A_sent_token_never_reaches_the_log_and_only_the_domain_is_named()
    {
        const string token = "SECRET-RESET-TOKEN-9f3a";
        var logs = new List<string>();
        var config = Config(
            ("AccountEmail:User", "brevo-user"),
            ("AccountEmail:Password", "brevo-password"),
            ("Accounts:SupportEmail", "help@example.test"));
        MailMessage? delivered = null;
        var sender = new SmtpAccountEmailSender(
            config,
            new CapturingLogger<SmtpAccountEmailSender>(logs),
            (message, _) =>
            {
                delivered = message;
                return Task.CompletedTask;
            });

        var ok = await sender.SendAsync(
            "reader@gmail.com",
            "Reset your Accessible Trader password",
            "Open https://accessibletrader.com/terminal/account/resetpassword?token=" + token);

        Assert.True(ok);
        Assert.NotNull(delivered);
        Assert.Contains(token, delivered!.Body);   // the mail really carried it
        Assert.Contains("help@example.test", delivered.Body);
        Assert.Contains("This address does not receive replies.", delivered.Body);

        var log = string.Join("\n", logs);
        Assert.DoesNotContain(token, log);
        Assert.DoesNotContain("reader@gmail.com", log);   // local part is the person
        Assert.Contains("gmail.com", log);
        Assert.Contains("Reset your Accessible Trader password", log);
        Assert.Contains("was sent", log);
    }

    [Fact]
    public async Task Unconfigured_send_returns_false_without_touching_the_network()
    {
        var logs = new List<string>();
        var config = Config(("AccountEmail:User", "brevo-user"));   // no password
        bool called = false;
        var sender = new SmtpAccountEmailSender(
            config,
            new CapturingLogger<SmtpAccountEmailSender>(logs),
            (_, _) => { called = true; return Task.CompletedTask; });

        Assert.False(sender.IsConfigured);
        Assert.False(await sender.SendAsync("reader@gmail.com", "Subject", "Body"));
        Assert.False(called);
    }

    [Fact]
    public async Task A_failed_send_returns_false_and_logs_no_body()
    {
        const string token = "SECRET-RESET-TOKEN-9f3a";
        var logs = new List<string>();
        var config = Config(
            ("AccountEmail:User", "u"),
            ("AccountEmail:Password", "p"));
        var sender = new SmtpAccountEmailSender(
            config,
            new CapturingLogger<SmtpAccountEmailSender>(logs),
            (_, _) => throw new InvalidOperationException("relay refused"));

        Assert.False(await sender.SendAsync("reader@gmail.com", "Subject", "token=" + token));

        var log = string.Join("\n", logs);
        Assert.DoesNotContain(token, log);
        Assert.Contains("failed", log);
    }
}

[Collection("ProviderCredentialBridge")]
public sealed class ForgotPasswordEmailTests : IClassFixture<HostedEmailWebHostFixture>
{
    private readonly HostedEmailWebHostFixture _host;

    public ForgotPasswordEmailTests(HostedEmailWebHostFixture host) => _host = host;

    private static async Task<HttpResponseMessage> RequestResetAsync(HttpClient client, string email)
    {
        var token = await WebHostIntegration.GetAntiforgeryTokenAsync(
            client, "/terminal/account/forgotpassword");
        return await client.PostAsync("/terminal/account/forgotpassword",
            WebHostIntegration.Form(("__RequestVerificationToken", token), ("Input.Email", email)));
    }

    [Fact]
    public async Task It_emails_the_reset_link_to_a_real_account_and_still_says_nothing()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "mail-real@example.test", "Correct-h0rse-battery");

        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await RequestResetAsync(client, "mail-real@example.test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("If that account exists, we've emailed a reset link to it.", html);
        Assert.DoesNotContain("mail-real@example.test", html);

        var sent = Assert.Single(_host.Email.Snapshot(),
            m => m.To == "mail-real@example.test"
              && m.Subject == "Reset your Accessible Trader password");
        Assert.Contains("/account/resetpassword?email=mail-real%40example.test&token=", sent.Body);
    }

    [Fact]
    public async Task The_page_does_not_wait_for_the_mail_server()
    {
        // Only a real account sends mail, so a response that waited for SMTP would be
        // seconds slower for registered addresses than for unknown ones.
        await WebHostIntegration.SeedUserAsync(_host.Factory, "mail-slow@example.test", "Correct-h0rse-battery");
        var never = new TaskCompletionSource();
        _host.Email.Hold = never.Task;
        try
        {
            using var client = WebHostIntegration.NewClient(_host.Factory);
            var post = RequestResetAsync(client, "mail-slow@example.test");
            var first = await Task.WhenAny(post, Task.Delay(TimeSpan.FromSeconds(20)));

            Assert.Same(post, first);
            Assert.Equal(HttpStatusCode.OK, (await post).StatusCode);
            Assert.Single(_host.Email.Snapshot(), m => m.To == "mail-slow@example.test");
        }
        finally
        {
            _host.Email.Hold = null;
            never.SetResult();
        }
    }

    [Fact]
    public async Task An_unknown_address_gets_the_same_page_and_no_email()
    {
        using var client = WebHostIntegration.NewClient(_host.Factory);
        var resp = await RequestResetAsync(client, "nobody-here@example.test");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("If that account exists, we've emailed a reset link to it.", html);
        Assert.DoesNotContain(_host.Email.Snapshot(), m => m.To == "nobody-here@example.test");
    }

    [Fact]
    public async Task A_second_request_inside_five_minutes_is_silently_throttled()
    {
        await WebHostIntegration.SeedUserAsync(_host.Factory, "mail-throttle@example.test", "Correct-h0rse-battery");

        using var client = WebHostIntegration.NewClient(_host.Factory);
        await RequestResetAsync(client, "mail-throttle@example.test");
        await RequestResetAsync(client, "mail-throttle@example.test");

        Assert.Single(_host.Email.Snapshot(), m => m.To == "mail-throttle@example.test");

        _host.Clock.Advance(PasswordResetEmailThrottle.Window + TimeSpan.FromSeconds(1));
        await RequestResetAsync(client, "mail-throttle@example.test");

        Assert.Equal(2, _host.Email.Snapshot().Count(m => m.To == "mail-throttle@example.test"));
    }
}
