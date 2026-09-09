using System.Reactive.Disposables;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// Which of the three toast categories an instance of
    /// <see cref="DesktopNotificationService"/> owns.
    ///
    /// <para>
    /// It exists because of Phase 1 of the background-monitor work: there are now TWO of these
    /// services alive in one process — one per browser circuit, and one in the long-lived
    /// headless session. Both would happily toast the same fill twice. The mask is how "exactly
    /// one delivery owner at a time" is stated in code: the headless instance is built without
    /// <see cref="Alerts"/>, because <c>LocalBackgroundMonitor</c> already delivers the alert it
    /// fired — sound, toast and speech — under its OWN opt-in switch, and routing it through
    /// here would put a feature the user has already turned on behind a second switch that
    /// defaults off.
    /// </para>
    /// </summary>
    [Flags]
    public enum DesktopNotificationCategories
    {
        None = 0,
        Alerts = 1,
        OrderFills = 2,
        NewBars = 4,
        All = Alerts | OrderFills | NewBars,
    }

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

        /// <summary>Which categories THIS instance owns. See <see cref="DesktopNotificationCategories"/>.</summary>
        public DesktopNotificationCategories Categories { get; }

        public DesktopNotificationService(
            IEventBus bus,
            IWorkspaceStore store,
            ISettingsManager settings,
            IDesktopNotifier notifier,
            ILogger<DesktopNotificationService>? logger = null)
            : this(bus, store, settings, notifier, DesktopNotificationCategories.All, logger)
        {
        }

        public DesktopNotificationService(
            IEventBus bus,
            IWorkspaceStore store,
            ISettingsManager settings,
            IDesktopNotifier notifier,
            DesktopNotificationCategories categories,
            ILogger<DesktopNotificationService>? logger = null)
        {
            _store = store;
            _settings = settings;
            _notifier = notifier;
            _logger = logger;
            Categories = categories;

            // The mask decides whether the SUBSCRIPTION exists, not whether the handler
            // returns early. An unowned category is then unreachable rather than merely
            // unhandled, which is the difference between a routing rule and a comment.
            if (Owns(DesktopNotificationCategories.Alerts))
                _subs.Add(bus.Subscribe<AlertFiredEvent>(OnAlertFired));
            if (Owns(DesktopNotificationCategories.NewBars))
            {
                _subs.Add(bus.Subscribe<NewBarEvent>(OnNewBar));
                // A bar closing on a live BACKGROUND tab rides the same category and the same
                // user switch — it is the same kind of news about a different chart, and asking
                // the user to find two checkboxes for one idea would be the wrong seam.
                _subs.Add(bus.Subscribe<BackgroundBarClosedEvent>(OnBackgroundBarClosed));
            }
            if (Owns(DesktopNotificationCategories.OrderFills))
            {
                _subs.Add(bus.Subscribe<OrderFilledEvent>(e => OnFill("Order filled", e.Order)));
                _subs.Add(bus.Subscribe<StopHitEvent>(e => OnFill(e.Order.Trailing ? "Trailing stop hit" : "Stop loss hit", e.Order)));
                _subs.Add(bus.Subscribe<TakeProfitHitEvent>(e => OnFill(e.Order.Trailing ? "Trailing take profit hit" : "Take profit hit", e.Order)));
            }

            if (notifier.IsAvailable)
                _logger?.LogInformation("Desktop notifications available ({Delivery}). Switches live under Alerts → Delivery settings.", notifier.Describe());
        }

        private bool Owns(DesktopNotificationCategories c) => (Categories & c) == c;

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

        /// <summary>
        /// A bar closed on a chart the user has open but is not looking at.
        ///
        /// <para>The title names the event's OWN symbol and timeframe, never the store's: the
        /// store is describing a different chart, and a toast that said "BTC/USD 1h: bar closed"
        /// for an ETH bar close would be worse than silence. It is the one thing that must not
        /// be copied from <see cref="OnNewBar"/>.</para>
        /// </summary>
        private void OnBackgroundBarClosed(BackgroundBarClosedEvent e)
        {
            if (!_notifier.IsAvailable || !Enabled(SettingsKeys.DesktopNotifyNewBars)) return;
            if (_store.State.IsPlaying) return;

            int barSeconds = TimeframeUtility.ToSeconds(e.Identity.Timeframe ?? "");
            if (barSeconds <= 0) barSeconds = BarSecondsFromDates(e.ClosedBar, e.NewBar);

            Send(NewBarTitle(e.Identity.Symbol ?? "", e.Identity.Timeframe ?? ""),
                 NewBarBody(barSeconds, e.ClosedBar));
        }

        /// <summary>
        /// Bar length from two adjacent bars, for the case where the identity carries no usable
        /// timeframe string. Mirrors <c>PlaybackNarration.BarSeconds</c>'s fallback; a daily
        /// default is the safe one, because it prints a DATE rather than a misleading clock time.
        /// </summary>
        private static int BarSecondsFromDates(Ohlcv closed, Ohlcv opened)
        {
            double gap = (opened.Date - closed.Date).TotalSeconds;
            return gap > 0 ? (int)gap : 86400;
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
            => NewBarTitle(
                !string.IsNullOrWhiteSpace(state.SymbolDisplayName)
                    ? state.SymbolDisplayName
                    : state.Identity.Symbol ?? "",
                state.Identity.Timeframe ?? "");

        /// <summary>
        /// The title, from the two things it actually needs.
        ///
        /// <para>Taking a <see cref="WorkspaceState"/> made this unusable from the one caller
        /// that has no state to give it: a bar closing on a LIVE BACKGROUND TAB is about a chart
        /// the store is not describing. Same shape as the Phase 0 lesson — the reachable half of
        /// the problem was the DECISION, and the decision only ever needed a symbol and a
        /// timeframe. Both routes now speak the same sentence by construction rather than by two
        /// people remembering to.</para>
        /// </summary>
        /// <remarks>PUBLIC, unlike its siblings: three routes now speak this sentence — the
        /// focused chart, a live background tab, and the headless monitor in the WebHost project
        /// — and one shared vocabulary is the only thing that keeps them agreeing word for word
        /// as any of them changes.</remarks>
        public static string NewBarTitle(string symbol, string timeframe)
        {
            string what = string.Join(" ", new[] { symbol, timeframe }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return what.Length == 0 ? "Bar closed" : $"{what}: bar closed";
        }

        /// <summary>"Close 1,234.5 at 09:31." — the new-bar announcement's own clock rule: time of day intraday, the date on a daily chart.</summary>
        internal static string NewBarBody(WorkspaceState state, Ohlcv closed)
            => NewBarBody(PlaybackNarration.BarSeconds(state), closed);

        /// <summary>
        /// The body, from the bar length rather than the whole workspace. <paramref name="barSeconds"/>
        /// is what decides the clock unit, and a background tab knows its own timeframe.
        /// </summary>
        /// <inheritdoc cref="NewBarTitle(string, string)" path="/remarks"/>
        public static string NewBarBody(int barSeconds, Ohlcv closed)
        {
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
