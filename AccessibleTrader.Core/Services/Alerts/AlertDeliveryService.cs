using System.Reactive.Disposables;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Core.Services.Notifications;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services.Alerts;

/// <summary>
/// Subscribes to <see cref="AlertFiredEvent"/> and fans each fired alert out to every
/// configured <see cref="IAlertChannel"/>. One channel failing does not starve the others —
/// exceptions are swallowed per-channel and recorded in the security event log so ops can
/// diagnose silent non-delivery. Singleton lifetime; registered in DI alongside the
/// channel instances.
/// </summary>
public sealed class AlertDeliveryService : IDisposable
{
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly ILogger<AlertDeliveryService>? _logger;
    private readonly IUserPresence? _presence;
    private readonly CompositeDisposable _subs = new();

    /// <param name="presence">Optional, and null means "always present" — a missing presence
    /// service must never mean silence. On the WebHost it is the circuit's own connection: a
    /// disconnected circuit's scope stays alive for about three minutes, and during that window
    /// the headless session has ALREADY taken ownership of this user's alerts, so fanning out
    /// here as well sends two emails, two Telegrams and two webhook POSTs for one alert.</param>
    public AlertDeliveryService(
        IEnumerable<IAlertChannel> channels,
        IEventBus bus,
        ILogger<AlertDeliveryService>? logger = null,
        IUserPresence? presence = null)
    {
        _channels = channels;
        _logger   = logger;
        _presence = presence;
        _subs.Add(bus.Subscribe<AlertFiredEvent>(OnAlertFired));
    }

    private void OnAlertFired(AlertFiredEvent evt)
    {
        // A circuit that cannot reach its user has already handed this alert to the headless
        // session. See IUserPresence — a duplicate webhook can place a duplicate order.
        if (_presence is { CanReachUser: false })
        {
            _logger?.LogDebug("Alert delivery skipped: this circuit is disconnected and the headless session owns it.");
            return;
        }

        // Fan out to every configured channel in parallel. Each dispatch is fire-and-
        // forget — the alert pipeline must not wait on network round-trips. Channel
        // failures are logged + security-event-recorded so the user can see why a
        // Telegram delivery didn't land without the UI thread stalling on timeout.
        foreach (var ch in _channels)
        {
            if (!ch.IsConfigured) continue;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await ch.SendAsync(evt.Alert, cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "alert channel {Id} failed to deliver '{AlertName}': {Message}",
                        ch.Id, evt.Alert.Definition.Name, ex.Message);
                    PluginHostServices.SecurityEvents?.Record(new SecurityEvent(
                        DateTime.UtcNow,
                        SecurityEventKind.TokenCleanupFailed,
                        Source:  $"AlertChannel[{ch.Id}]",
                        Message: $"delivery failed: {ex.Message}",
                        Data:    new Dictionary<string, string> { ["alert"] = evt.Alert.Definition.Name }));
                }
            });
        }
    }

    public void Dispose() => _subs.Dispose();
}
