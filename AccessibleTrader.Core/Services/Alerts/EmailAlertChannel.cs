using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.Alerts;

/// <summary>
/// Configuration for the SMTP alert channel. Stored via <see cref="IApiKeyService"/>
/// under a dedicated key so credentials ride the same encrypted store as provider API keys.
/// </summary>
public sealed record EmailAlertChannelConfig
{
    public string? Host { get; init; }
    public int Port { get; init; } = 587;
    public bool UseTls { get; init; } = true;
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? FromAddress { get; init; }
    public string? ToAddress { get; init; }
}

/// <summary>
/// SMTP delivery via <see cref="SmtpClient"/>. Reads its configuration lazily per send so
/// settings changes take effect immediately without a reload. Does no retry — SMTP servers
/// typically have their own queueing and <see cref="IAlertChannel.SendAsync"/> is fire-and-
/// forget from the alert-orchestrator's perspective.
/// </summary>
public sealed class EmailAlertChannel : IAlertChannel
{
    private readonly Func<EmailAlertChannelConfig?> _configProvider;

    public EmailAlertChannel(Func<EmailAlertChannelConfig?> configProvider)
    {
        _configProvider = configProvider;
    }

    public string Id => "email";
    public string DisplayName => "Email (SMTP)";

    public bool IsConfigured
    {
        get
        {
            var cfg = _configProvider();
            return cfg != null
                && !string.IsNullOrWhiteSpace(cfg.Host)
                && !string.IsNullOrWhiteSpace(cfg.FromAddress)
                && !string.IsNullOrWhiteSpace(cfg.ToAddress);
        }
    }

    public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
    {
        var cfg = _configProvider();
        if (cfg == null || !IsConfigured) return;

        using var msg = new MailMessage(cfg.FromAddress!, cfg.ToAddress!)
        {
            Subject = $"[AccessibleTrader] {alert.Definition.Name}",
            Body    = $"{alert.SpeechText}\n\nTriggering value: {alert.TriggeringValue:F6}" +
                      (alert.PreviousValue.HasValue ? $"\nPrevious value: {alert.PreviousValue:F6}" : string.Empty),
            IsBodyHtml = false,
        };

        using var client = new SmtpClient(cfg.Host!, cfg.Port)
        {
            EnableSsl = cfg.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrEmpty(cfg.Username))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(cfg.Username, cfg.Password ?? string.Empty);
        }

        await client.SendMailAsync(msg, ct).ConfigureAwait(false);
    }
}
