using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Core.Services.Alerts;

/// <summary>
/// Configuration for the Telegram Bot API alert channel. <see cref="BotToken"/> is the
/// token returned by BotFather; <see cref="ChatId"/> is the numeric id of the target
/// chat (user DM or group). Both stored via the encrypted API-key service.
/// </summary>
public sealed record TelegramAlertChannelConfig
{
    public string? BotToken { get; init; }
    public string? ChatId { get; init; }
}

/// <summary>
/// Telegram Bot API delivery via <c>api.telegram.org/bot{token}/sendMessage</c>. The
/// shared <see cref="HttpClient"/> is provided by the caller so the plugin-host outbound
/// allow-list handler chain applies uniformly — no direct-construct HttpClient escapes
/// the security sweep.
/// </summary>
public sealed class TelegramAlertChannel : IAlertChannel
{
    private readonly HttpClient _http;
    private readonly Func<TelegramAlertChannelConfig?> _configProvider;

    public TelegramAlertChannel(HttpClient http, Func<TelegramAlertChannelConfig?> configProvider)
    {
        _http = http;
        _configProvider = configProvider;
    }

    public string Id => "telegram";
    public string DisplayName => "Telegram";

    public bool IsConfigured
    {
        get
        {
            var cfg = _configProvider();
            return cfg != null
                && !string.IsNullOrWhiteSpace(cfg.BotToken)
                && !string.IsNullOrWhiteSpace(cfg.ChatId);
        }
    }

    public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
    {
        var cfg = _configProvider();
        if (cfg == null || !IsConfigured) return;

        var payload = new
        {
            chat_id = cfg.ChatId!,
            text    = $"🔔 *{alert.Definition.Name}*\n{alert.SpeechText}\nValue: {alert.TriggeringValue:F6}",
            parse_mode = "Markdown",
        };

        using var resp = await _http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{cfg.BotToken}/sendMessage",
            payload, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }
}
