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
    /// It was introduced for Phase 1 of the background-monitor work, when a second instance was
    /// built inside the headless session and the mask stated "exactly one delivery owner at a
    /// time" in code. <b>That second instance no longer exists</b> (it was a subscriber with no
    /// producer — see <c>HeadlessSession</c>), so in production only <see cref="All"/> is ever
    /// constructed and the mask survives as a test seam and as a statement that an unowned
    /// category is UNREACHABLE rather than merely unhandled. Delete it the day something else
    /// wants that guarantee stated differently; do not delete it as "dead", because the property
    /// it pins — a subscription that does not exist cannot fire — is the one a returning early
    /// handler quietly loses.
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
    /// Turns a terminal event the trader <b>cannot see</b> into a desktop notification.
    ///
    /// <para>
    /// ── The rule, and it is about the event's SUBJECT ─────────────────────────
    /// Cody, 2026-09-11: <i>"When monitoring other tabs that are open in the browser, receiving
    /// toast notifications for those would be appropriate as that's the only way you'll see
    /// them. The aria live region should only be used for the tab currently open in front of
    /// the trader."</i>
    /// </para>
    ///
    /// <para>
    /// So: whatever happens on the chart in front of you is spoken in the live region and
    /// earconed, and <b>never toasted</b> — you are already being told. A bar closing on
    /// another open tab, and an alert or a fill on a symbol with no tab open, are toasted,
    /// because nothing else can reach you. See <see cref="NotificationPolicy"/>, which owns
    /// both halves of that decision.
    /// </para>
    ///
    /// <para>
    /// ── What changed, and what it fixes ───────────────────────────────────────
    /// Until 2026-09-11 this subscribed to <c>NewBarEvent</c> — the FOCUSED chart's bar close —
    /// and raised a toast for it. With a 1-minute chart open that is a MATE toast a minute for
    /// a bar the browser was announcing anyway, which is the report that started this pass
    /// ("the desktop notifications seem to fire even when the browser is open"). That
    /// subscription is gone; the focused chart's bar close belongs to
    /// <c>AccessibilityFeedbackCoordinator</c> alone.
    /// </para>
    ///
    /// <para>
    /// ── One switch, default ON ────────────────────────────────────────────────
    /// <see cref="SettingsKeys.NotifyUnseenEvents"/> replaces the three default-off switches
    /// this class used to read. Three switches in a dialog away from the feature they gated is
    /// how the thirty-ninth pass's "broken feature" turned out to be a switched-off one, and a
    /// blind user has no compensating channel for an accidental silence. Read per event, not
    /// cached, so the checkbox takes effect on the next event with no restart.
    /// </para>
    ///
    /// <para>
    /// ── What it is not ────────────────────────────────────────────────────────
    /// Not speech: the in-session announcements are untouched, and a toast never replaces them.
    /// Not the background monitor: <c>LocalBackgroundMonitor</c> owns every event on a symbol
    /// no circuit is covering, and delivers the same sentences through the same wording
    /// helpers. Not playback: a bar "closing" under Space is the sequencer, not the market.
    /// </para>
    /// </summary>
    public sealed class DesktopNotificationService : IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly ISettingsManager _settings;
        private readonly IDesktopNotifier _notifier;
        private readonly IUserPresence? _presence;
        private readonly ILogger<DesktopNotificationService>? _logger;
        private readonly CompositeDisposable _subs = new();

        /// <summary>Which categories THIS instance owns. See <see cref="DesktopNotificationCategories"/>.</summary>
        public DesktopNotificationCategories Categories { get; }

        public DesktopNotificationService(
            IEventBus bus,
            IWorkspaceStore store,
            ISettingsManager settings,
            IDesktopNotifier notifier,
            ILogger<DesktopNotificationService>? logger = null,
            IUserPresence? presence = null)
            : this(bus, store, settings, notifier, DesktopNotificationCategories.All, logger, presence)
        {
        }

        public DesktopNotificationService(
            IEventBus bus,
            IWorkspaceStore store,
            ISettingsManager settings,
            IDesktopNotifier notifier,
            DesktopNotificationCategories categories,
            ILogger<DesktopNotificationService>? logger = null,
            IUserPresence? presence = null)
        {
            _store = store;
            _settings = settings;
            _notifier = notifier;
            _logger = logger;
            _presence = presence;
            Categories = categories;

            // The mask decides whether the SUBSCRIPTION exists, not whether the handler
            // returns early. An unowned category is then unreachable rather than merely
            // unhandled, which is the difference between a routing rule and a comment.
            if (Owns(DesktopNotificationCategories.Alerts))
                _subs.Add(bus.Subscribe<AlertFiredEvent>(OnAlertFired));
            if (Owns(DesktopNotificationCategories.NewBars))
            {
                // The FOCUSED chart's bar close. Subscribed, but OnNewBar refuses while the app
                // is visible — that is the fix for "the desktop notifications fire even when the
                // browser is open", and it is a refusal rather than a missing subscription
                // because the MAUI head needs this event the moment its window hides to the
                // tray, where the live region reaches nobody. See IUserPresence.IsAppVisible.
                _subs.Add(bus.Subscribe<NewBarEvent>(OnNewBar));
                // A bar closing on a live BACKGROUND tab is the same news about a chart nobody
                // is looking at, and that one needs a notification whatever the window is doing.
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

        /// <summary>
        /// The one switch, read per event so the checkbox needs no restart — AND whether this
        /// instance can still reach its user at all.
        ///
        /// <para>On the WebHost a closed tab's circuit lives on for about three minutes, and the
        /// headless session takes ownership the moment the connection drops. Toasting from here
        /// during that window is the same alert twice. See <see cref="IUserPresence"/>.</para>
        /// </summary>
        private bool Enabled()
            => _presence is not { CanReachUser: false } && NotificationPolicy.NotifyUnseen(_settings);

        /// <summary>
        /// An alert fired. Toasted only when it is about a symbol the trader is NOT looking at.
        ///
        /// <para>In practice an in-session alert on the focused chart never gets here past the
        /// gate, which is correct and is also why the gate has to be the event's symbol rather
        /// than "am I in-session": <c>BackgroundWorkspaceMonitor</c> publishes the very same
        /// event for a non-focused TAB, stamped with that tab's symbol, and that one is news
        /// the user cannot see.</para>
        /// </summary>
        private void OnAlertFired(AlertFiredEvent e)
        {
            if (!_notifier.IsAvailable || !Enabled()) return;
            if (AlreadyBeingSaid(e.Alert.Symbol)) return;
            Send(AlertTitle(e.Alert.Definition.Name), e.Alert.SpeechText);
        }

        /// <summary>
        /// Is this event about the chart the trader is looking AT, on a window they can
        /// currently see? Only then is the live region already saying it.
        ///
        /// <para>The second half is what makes the MAUI head work: hidden to the tray, nothing
        /// is "on screen", so every event — the focused chart's bar close included — earns its
        /// notification. Cody, 2026-09-11: closing the MAUI window with X or Alt+F4 minimises to
        /// the tray and the terminal keeps notifying.</para>
        /// </summary>
        private bool AlreadyBeingSaid(string? symbol)
            => (_presence?.IsAppVisible ?? true)
               && NotificationPolicy.IsOnScreen(_store.State, symbol);

        /// <summary>
        /// The focused chart's bar close. Silent while the window is in front of the trader —
        /// the live region says it, and toasting a 1-minute chart as well is a notification a
        /// minute for news the user already has. Spoken to the OS the moment the window is not.
        /// </summary>
        private void OnNewBar(NewBarEvent e)
        {
            if (!_notifier.IsAvailable || !Enabled()) return;
            if (_presence?.IsAppVisible ?? true) return;
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
            if (!_notifier.IsAvailable || !Enabled()) return;
            if (_store.State.IsPlaying) return;
            // A background tab that has become the focused chart between the bar closing and
            // this handler running is being announced by the live region; do not double it.
            if (AlreadyBeingSaid(e.Identity.Symbol)) return;

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
            if (!_notifier.IsAvailable || !Enabled()) return;
            // A fill on the chart in front of the trader is spoken by the coordinator; a fill
            // on any other market is news that has nowhere else to go.
            if (AlreadyBeingSaid(order.Symbol)) return;
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
