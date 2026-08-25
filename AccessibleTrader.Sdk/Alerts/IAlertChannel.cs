namespace AccessibleTrader.Sdk.Alerts;

/// <summary>
/// External alert delivery channel — takes a fired alert and dispatches it over a
/// provider-specific transport (SMTP, Telegram, webhook, etc.). Implementations are
/// registered via DI as <c>IEnumerable&lt;IAlertChannel&gt;</c>; the alert-fire path
/// fans out to every registered channel whose <see cref="IsConfigured"/> returns true.
///
/// Contract:
///   • <see cref="SendAsync"/> returns cleanly on success. Errors are recorded by the
///     caller (alert-orchestrator) as security-log entries — no silent failures.
///   • Channels should respect their own cooldown / deduplication; alert-level cooldown
///     still applies at the evaluator tier.
///   • Implementations must be thread-safe (multiple alerts can fire in parallel).
/// </summary>
public interface IAlertChannel
{
    /// <summary>Stable identifier for the channel (<c>"email"</c>, <c>"telegram"</c>, etc.).
    /// Used for logging and by the settings UI to distinguish channel config blocks.</summary>
    string Id { get; }

    /// <summary>Human-readable name for the settings UI.</summary>
    string DisplayName { get; }

    /// <summary>True when the channel has everything it needs to attempt a delivery
    /// (SMTP host + recipient set for email; bot token + chat id for Telegram). A
    /// channel that returns <c>false</c> is skipped by the alert-fire fanout.</summary>
    bool IsConfigured { get; }

    /// <summary>Deliver a fired alert. Implementations must handle their own retry
    /// logic and cancellation. Raised exceptions are logged by the caller; the caller
    /// does not re-raise so one channel failing doesn't starve the others.</summary>
    Task SendAsync(AlertFired alert, CancellationToken ct = default);
}
