using System.Net.Http.Json;
using AccessibleTrader.Sdk.Alerts;

using Microsoft.Extensions.Logging;
namespace AccessibleTrader.Core.Services.Alerts
{
    /// <summary>
    /// A single named webhook endpoint. <see cref="Name"/> is matched against an
    /// alert's <c>WebhookTarget</c> to route per-asset (e.g. "BTC channel" → #btc).
    /// </summary>
    public sealed record NamedWebhook
    {
        public required string Name { get; init; }
        /// <summary>Full endpoint URL — a Discord webhook, Slack incoming webhook, or any HTTPS service.</summary>
        public required string Url { get; init; }
        /// <summary>Optional value for the Authorization header (e.g. "Bearer abc123").</summary>
        public string? AuthHeader { get; init; }
    }

    /// <summary>
    /// Configuration for the generic webhook channel; read lazily on each send.
    /// Holds a named list so alerts can route to per-asset channels (Part B). The
    /// legacy single <c>alerts.webhook.url</c> is migrated into a <c>{Name:"Default"}</c>
    /// entry on load (see <see cref="WebhookAlertConfigLoader"/>).
    /// </summary>
    public sealed record WebhookAlertChannelConfig
    {
        /// <summary>The configured named webhooks. Empty = channel unconfigured.</summary>
        public IReadOnlyList<NamedWebhook> Webhooks { get; init; } = Array.Empty<NamedWebhook>();
    }

    /// <summary>
    /// Generic JSON-POST alert channel. One channel covers three ecosystems because
    /// the payload carries each service's expected field simultaneously:
    ///   • Discord webhooks read <c>content</c>
    ///   • Slack incoming webhooks read <c>text</c>
    ///   • custom endpoints get the full structured object
    /// Unknown fields are ignored by all three, so no per-service mode switch is needed.
    ///
    /// Routing (Part B): each fired alert is posted only to the webhook whose
    /// <see cref="NamedWebhook.Name"/> equals the alert's <c>WebhookTarget</c>. An
    /// alert with a null <c>WebhookTarget</c> is not posted to any webhook.
    /// </summary>
    public sealed class WebhookAlertChannel : IAlertChannel
    {
        private readonly HttpClient _http;
        private readonly Func<WebhookAlertChannelConfig?> _configProvider;
        // Optional (older construction sites/tests pass neither): missing-target
        // diagnostics. One spoken warning per missing webhook NAME; every miss logs.
        private readonly Microsoft.Extensions.Logging.ILogger? _logger;
        private readonly IEventBus? _eventBus;
        private readonly HashSet<string> _warnedMissingTargets = new(StringComparer.OrdinalIgnoreCase);
        // Targets we've already spoken a delivery-failure warning for; cleared on the next
        // success so a recovered-then-broken endpoint warns again.
        private readonly HashSet<string> _warnedFailingTargets = new(StringComparer.OrdinalIgnoreCase);

        public WebhookAlertChannel(HttpClient http, Func<WebhookAlertChannelConfig?> configProvider,
            Microsoft.Extensions.Logging.ILogger? logger = null, IEventBus? eventBus = null)
        {
            _http = http;
            _configProvider = configProvider;
            _logger = logger;
            _eventBus = eventBus;
        }

        public string Id => "webhook";
        public string DisplayName => "Webhook (Discord / Slack / custom)";

        public bool IsConfigured
        {
            get
            {
                var cfg = _configProvider();
                return cfg != null && cfg.Webhooks.Any(IsValid);
            }
        }

        private static bool IsValid(NamedWebhook w) =>
            w != null
            && !string.IsNullOrWhiteSpace(w.Name)
            && !string.IsNullOrWhiteSpace(w.Url)
            && Uri.TryCreate(w.Url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps; // alerts may carry position info — never plaintext

        public async Task SendAsync(AlertFired alert, CancellationToken ct = default)
        {
            var cfg = _configProvider();
            if (cfg == null) return;

            // Route to the webhook named by the alert. A null target means "no webhook
            // for this alert" — email/Telegram fan-out still happens via their channels.
            var target = alert.Definition.WebhookTarget;
            if (string.IsNullOrWhiteSpace(target)) return;

            var hook = cfg.Webhooks.FirstOrDefault(w =>
                IsValid(w) && string.Equals(w.Name, target, StringComparison.OrdinalIgnoreCase));
            if (hook == null)
            {
                // The named webhook was deleted/renamed after the alert was created.
                // Silent skips here meant "my Discord alert stopped working" with no
                // clue why — log every time, warn the user once per missing name.
                _logger?.LogWarning(
                    "Alert '{Alert}' routes to webhook '{Target}', which no longer exists — not delivered.",
                    alert.Definition.Name, target);
                if (_warnedMissingTargets.Add(target!))
                    _eventBus?.Publish(new Models.FeedbackRequestEvent(Models.FeedbackType.Error,
                        $"Alert webhook '{target}' no longer exists. {alert.Definition.Name} was not delivered — fix it in the Alerts dialog.",
                        true));
                return;
            }

            string symbolPrefix = string.IsNullOrWhiteSpace(alert.Symbol) ? "" : $"[{alert.Symbol}] ";
            string message = $"🔔 {symbolPrefix}{alert.Definition.Name}: {alert.SpeechText}";
            var payload = new
            {
                content = message,                       // Discord
                text = message,                          // Slack
                alert_name = alert.Definition.Name,
                symbol = alert.Symbol,
                condition = alert.Definition.Condition.ToString(),
                speech_text = alert.SpeechText,
                triggering_value = alert.TriggeringValue,
                previous_value = alert.PreviousValue,
                timestamp_utc = DateTime.UtcNow,
                source = "AccessibleTradeTerminal",
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, hook.Url)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(hook.AuthHeader))
                req.Headers.TryAddWithoutValidation("Authorization", hook.AuthHeader);

            try
            {
                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                // A previously-failing target that now succeeds should be able to warn
                // again if it breaks later.
                lock (_warnedFailingTargets) _warnedFailingTargets.Remove(hook.Name);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Delivery failure (network, 4xx/5xx, timeout). Previously this only threw
                // up to AlertDeliveryService, which logs + records a SecurityEvent — neither
                // of which a blind user ever hears. Speak it once per target until it recovers,
                // then rethrow so the existing log/SecurityEvent path still runs.
                _logger?.LogWarning(ex,
                    "Alert '{Alert}' webhook '{Target}' delivery failed.", alert.Definition.Name, hook.Name);
                bool firstFailure;
                lock (_warnedFailingTargets) firstFailure = _warnedFailingTargets.Add(hook.Name);
                if (firstFailure)
                    _eventBus?.Publish(new Models.FeedbackRequestEvent(Models.FeedbackType.Error,
                        $"Alert webhook '{hook.Name}' is failing to deliver. {alert.Definition.Name} did not send — check the endpoint in the Alerts dialog.",
                        true));
                throw;
            }
        }
    }
}
