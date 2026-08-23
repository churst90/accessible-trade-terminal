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
    /// <summary>
    /// The submission ports SMTP legitimately uses. Enforced only when
    /// <see cref="DemoPolicy.BlockPrivateNetworkTargets"/> is on: with a free
    /// choice of port, `new SmtpClient(host, port)` from the hosted server is an
    /// arbitrary outbound TCP connect — a port scanner whose result is spoken
    /// back to the user as delivery success or failure.
    /// </summary>
    private static readonly int[] AllowedSmtpPorts = { 25, 465, 587, 2525 };

    private readonly Func<EmailAlertChannelConfig?> _configProvider;
    private readonly DemoPolicy? _demo;

    public EmailAlertChannel(Func<EmailAlertChannelConfig?> configProvider, DemoPolicy? demo = null)
    {
        _configProvider = configProvider;
        _demo = demo;
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

        // A port outside 1–65535 would surface as ArgumentOutOfRangeException from
        // deep inside SmtpClient; refuse it here with a message that names the field.
        if (cfg.Port is < 1 or > 65535)
            throw new InvalidOperationException($"SMTP port {cfg.Port} is not a valid port.");

        if (_demo?.BlockPrivateNetworkTargets == true)
        {
            if (Array.IndexOf(AllowedSmtpPorts, cfg.Port) < 0)
                throw new InvalidOperationException(
                    $"SMTP port {cfg.Port} is not allowed on this host — use a mail submission " +
                    "port (25, 465, 587 or 2525).");
            // Resolve-and-check BEFORE SmtpClient does its own lookup. SmtpClient
            // offers no connect hook, so a DNS record could in principle change
            // between this check and the connect; the port allow-list above is what
            // keeps even that residual race from being a useful scanner.
            await OutboundNetworkGuard.ResolvePublicOrThrowAsync(cfg.Host!, ct).ConfigureAwait(false);
        }

        using var msg = new MailMessage(cfg.FromAddress!, cfg.ToAddress!)
        {
            Subject = $"[AccessibleTrader] {alert.Definition.Name}",
            // SpeechPriceFormatter, not :F6 — the fixed form collapsed sub-penny values
            // to "0.000000" and followed the OS locale.
            Body    = $"{alert.SpeechText}\n\nTriggering value: {Accessibility.SpeechPriceFormatter.FormatPrice(alert.TriggeringValue)}" +
                      (alert.PreviousValue.HasValue ? $"\nPrevious value: {Accessibility.SpeechPriceFormatter.FormatPrice(alert.PreviousValue.Value)}" : string.Empty),
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
