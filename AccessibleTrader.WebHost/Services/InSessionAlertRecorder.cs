using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Bridges in-session (browser-open) alerts into the singleton <see cref="RecentAlertsBuffer"/>
    /// so the tray's recent-alerts list and unread label include them — not just the ones the
    /// background monitor fires with the browser closed. Scoped, one per circuit: it subscribes
    /// to the circuit's own <see cref="IEventBus"/> (which is where the in-session alert pipeline
    /// publishes), and its subscription is disposed with the circuit.
    ///
    /// No double-counting with the background monitor. That used to be true because the monitor
    /// stood down entirely while any circuit was connected; since Phase 1 (2026-09-06) it is
    /// true for a better reason — the monitor takes only the symbols no open circuit is
    /// watching (see <see cref="CircuitAlertCoverage"/>), so a given fire has exactly one
    /// producer and therefore exactly one recorder. The headless session deliberately does NOT
    /// resolve this class: <c>LocalBackgroundMonitor</c> files its own alerts into the buffer
    /// directly, and a recorder on that bus would file them a second time.
    /// </summary>
    public sealed class InSessionAlertRecorder : IDisposable
    {
        private readonly IDisposable _sub;

        public InSessionAlertRecorder(IEventBus bus, RecentAlertsBuffer buffer)
        {
            _sub = bus.Subscribe<AlertFiredEvent>(e => buffer.Add(e.Alert.SpeechText, e.Symbol));
        }

        public void Dispose() => _sub.Dispose();
    }
}
