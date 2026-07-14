using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Alerts;

namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>Configuration for the generic webhook channel; read lazily on each send.</summary>
    public sealed record WebhookAlertChannelConfig
    {
        /// <summary>Full endpoint URL — a Discord webhook, Slack incoming webhook, or any HTTP service.</summary>
        public string? WebhookUrl { get; init; }
        /// <summary>Optional value for the Authorization header (e.g. "Bearer abc123").</summary>
        public string? AuthHeader { get; init; }
    }

    /// <summary>
    /// Generic JSON-POST alert channel. One channel covers three ecosystems because
    /// the payload carries each service's expected field simultaneously:
    ///   • Discord webhooks read <c>content</c>
    ///   • Slack incoming webhooks read <c>text</c>
    ///   • custom endpoints get the full structured object
    /// Unknown fields are ignored by all three, so no per-service mode switch is needed.
    /// </summary>
    public sealed class WebhookAlertChannel : IAlertChannel
    {
        private readonly HttpClient _http;
        private readonly Func<WebhookAlertChannelConfig?> _configProvider;

        public WebhookAlertChannel(HttpClient http, Func<WebhookAlertChannelConfig?> configProvider)
        {
            _http = http;
            _configProvider = configProvider;
        }

        public string Id => "webhook";
        public string DisplayName => "Webhook (Discord / Slack / custom)";

        public bool IsConfigured
        {
            get
            {
                var cfg = _configProvider();
                return cfg != null
                    && !string.IsNullOrWhiteSpace(cfg.WebhookUrl)
                    && Uri.TryCreate(cfg.WebhookUrl, UriKind.Absolute, out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps; // alerts may carry position info — never plaintext
            }
        }

        public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
        {
            var cfg = _configProvider();
            if (cfg == null || !IsConfigured) return;

            string message = $"🔔 {alert.Definition.Name}: {alert.SpeechText}";
            var payload = new
            {
                content = message,                       // Discord
                text = message,                          // Slack
                alert_name = alert.Definition.Name,
                speech_text = alert.SpeechText,
                triggering_value = alert.TriggeringValue,
                previous_value = alert.PreviousValue,
                timestamp_utc = DateTime.UtcNow,
                source = "AccessibleTradeTerminal",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, cfg.WebhookUrl)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(cfg.AuthHeader))
                req.Headers.TryAddWithoutValidation("Authorization", cfg.AuthHeader);

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }
    }
}
