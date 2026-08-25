using System.Net.Http.Json;
using System.Text;
using AccessibleTrader.Core.Services.Accessibility;
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

        // Alert name and speech text are user-authored. With parse_mode=Markdown an
        // unbalanced entity character in either ("BTC_USD breakout") makes the Bot API
        // reject the whole message with 400 "can't parse entities" — the alert simply
        // never arrives. Escape them so any name is deliverable. The value goes through
        // SpeechPriceFormatter, not a fixed :F6 which read "0.000000" for sub-penny
        // assets and follows the OS locale.
        var payload = new
        {
            chat_id = cfg.ChatId!,
            text    = $"🔔 *{EscapeMarkdown(alert.Definition.Name)}*\n{EscapeMarkdown(alert.SpeechText)}\n" +
                      $"Value: {SpeechPriceFormatter.FormatPrice(alert.TriggeringValue)}",
            parse_mode = "Markdown",
        };

        using var resp = await _http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{cfg.BotToken}/sendMessage",
            payload, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Backslash-escapes the four characters legacy Telegram Markdown treats as entity
    /// openers (<c>_ * ` [</c>).
    /// </summary>
    internal static string EscapeMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '_' or '*' or '`' or '[') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
