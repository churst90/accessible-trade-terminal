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
    /// No double-counting with the background monitor: that monitor only evaluates when zero
    /// circuits are connected, so exactly one of the two records any given fire.
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
