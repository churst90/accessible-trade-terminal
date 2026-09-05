using System.Reactive.Disposables;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// Turns three kinds of event into a desktop notification, each behind its own switch.
    ///
    /// <para>
    /// ── Which events, and why these three ─────────────────────────────────────
    /// A toast exists for the moment the terminal is NOT the window you are in. Three things
    /// are worth being pulled back for: an alert you set firing, an order filling (or a stop or
    /// take-profit being hit — a fill you did not press a key for), and, on a slow chart, a bar
    /// closing. Everything else already reaches you as speech while you are here, and would be
    /// noise when you are not.
    /// </para>
    ///
    /// <para>
    /// ── All three default OFF ─────────────────────────────────────────────────
    /// New bars especially: a one-minute chart is a toast a minute, and on the local WebHost the
    /// MATE daemon queues them. Opt-in also means a bare settings substitute gets the default,
    /// so a test that never touched a switch sees no toast. Read per event, not cached, so the
    /// checkbox takes effect on the next event with no restart — the same rule the background
    /// monitor's switch follows.
    /// </para>
    ///
    /// <para>
    /// ── What it is not ────────────────────────────────────────────────────────
    /// Not speech: the in-session announcements are untouched, and a toast never replaces
    /// them. Not the background monitor: <c>LocalBackgroundMonitor</c> toasts alerts while the
    /// browser is CLOSED and pauses while a circuit is open; this service is the circuit. The
    /// two cannot double up. Not playback: a bar "closing" under Space is the sequencer, not
    /// the market, and is skipped.
    /// </para>
    /// </summary>
    public sealed class DesktopNotificationService : IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly ISettingsManager _settings;
        private readonly IDesktopNotifier _notifier;
        private readonly ILogger<DesktopNotificationService>? _logger;
        private readonly CompositeDisposable _subs = new();

        public DesktopNotificationService(
            IEventBus bus,
            IWorkspaceStore store,
            ISettingsManager settings,
            IDesktopNotifier notifier,
            ILogger<DesktopNotificationService>? logger = null)
        {
            _store = store;
            _settings = settings;
            _notifier = notifier;
            _logger = logger;

            _subs.Add(bus.Subscribe<AlertFiredEvent>(OnAlertFired));
            _subs.Add(bus.Subscribe<NewBarEvent>(OnNewBar));
            _subs.Add(bus.Subscribe<OrderFilledEvent>(e => OnFill("Order filled", e.Order)));
            _subs.Add(bus.Subscribe<StopHitEvent>(e => OnFill(e.Order.Trailing ? "Trailing stop hit" : "Stop loss hit", e.Order)));
            _subs.Add(bus.Subscribe<TakeProfitHitEvent>(e => OnFill(e.Order.Trailing ? "Trailing take profit hit" : "Take profit hit", e.Order)));

            if (notifier.IsAvailable)
                _logger?.LogInformation("Desktop notifications available ({Delivery}). Switches live under Alerts → Delivery settings.", notifier.Describe());
        }

        private bool Enabled(string key) => _settings.GetSetting(key)?.ToObject<bool>() ?? false;

        private void OnAlertFired(AlertFiredEvent e)
        {
            if (!_notifier.IsAvailable || !Enabled(SettingsKeys.DesktopNotifyAlerts)) return;
            Send(AlertTitle(e.Alert.Definition.Name), e.Alert.SpeechText);
        }

        private void OnNewBar(NewBarEvent e)
        {
            if (!_notifier.IsAvailable || !Enabled(SettingsKeys.DesktopNotifyNewBars)) return;
            var state = _store.State;
            if (state.IsPlaying) return;
            Send(NewBarTitle(state), NewBarBody(state, e.ClosedBar));
        }

        private void OnFill(string prefix, Sdk.Trading.OrderUpdate order)
        {
            if (!_notifier.IsAvailable || !Enabled(SettingsKeys.DesktopNotifyOrderFills)) return;
            // The same sentence the speech layer says, minus its own prefix — so what the
            // toast reads and what the journal recorded agree word for word.
            Send(prefix, FillBody(prefix, order));
        }

        private void Send(string title, string body)
        {
            try { _notifier.Notify(title, body); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Desktop notification failed: {Title}", title); }
        }

        // ── Wording, kept static so the tests can pin it ─────────────────────

        internal static string AlertTitle(string alertName)
            => string.IsNullOrWhiteSpace(alertName) ? "Alert" : $"Alert: {alertName.Trim()}";

        internal static string NewBarTitle(WorkspaceState state)
        {
            string symbol = !string.IsNullOrWhiteSpace(state.SymbolDisplayName)
                ? state.SymbolDisplayName
                : state.Identity.Symbol ?? "";
            string tf = state.Identity.Timeframe ?? "";
            string what = string.Join(" ", new[] { symbol, tf }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return what.Length == 0 ? "Bar closed" : $"{what}: bar closed";
        }

        /// <summary>"Close 1,234.5 at 09:31." — the new-bar announcement's own clock rule: time of day intraday, the date on a daily chart.</summary>
        internal static string NewBarBody(WorkspaceState state, Ohlcv closed)
        {
            int barSeconds = PlaybackNarration.BarSeconds(state);
            string stamp = SpeechTimeFormatter.FormatBarClock(closed.Date, barSeconds);
            string when = barSeconds < 86400 ? $" at {stamp}" : $" on {stamp}";
            return $"Close {SpeechPriceFormatter.FormatPrice(closed.Close)}{when}.";
        }

        internal static string FillBody(string prefix, Sdk.Trading.OrderUpdate order)
        {
            string whole = AccessibilityFeedbackCoordinator.FormatFill(prefix, order);
            string lead = prefix + ". ";
            return whole.StartsWith(lead, StringComparison.Ordinal) ? whole[lead.Length..] : whole;
        }

        public void Dispose() => _subs.Dispose();
    }
}
