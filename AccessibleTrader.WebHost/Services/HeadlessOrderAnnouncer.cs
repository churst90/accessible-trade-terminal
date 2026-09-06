using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Notifications;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>Order events, spoken with no browser attached.</b> The delivery half of Phase 2:
    /// <see cref="HeadlessOrderWatch"/> keeps the venue streams hooked,
    /// <c>GeneralOrderService</c> classifies each update into a typed event on the headless
    /// bus, and this turns that event into a sound, a desktop toast and speech through
    /// whatever this desktop provides — the same three channels
    /// <see cref="LocalBackgroundMonitor"/> uses for an alert.
    ///
    /// <para>
    /// ── SAFETY LINE ───────────────────────────────────────────────────────────
    /// <b>Headless REPORTS; it never ACTS.</b> Nothing in this file places an order, moves a
    /// stop or touches a position. It says what the venue did.
    /// </para>
    ///
    /// <para>
    /// ── Exactly one owner, per venue ──────────────────────────────────────────
    /// A provider plugin is a singleton, so the circuit's order service and the headless one
    /// subscribe to the SAME stream and each publishes onto its own bus. If both delivered,
    /// the user hears every fill twice. <see cref="CircuitOrderCoverage"/> is the arbiter and
    /// it is asked AT DELIVERY TIME, not at subscription time: browsers open and close between
    /// a subscription and a fill, and the honest answer is the one true when the fill lands.
    /// A fill whose venue no circuit is covering — the browser is closed, or it is open but
    /// never hooked that venue — is ours.
    /// </para>
    ///
    /// <para>
    /// ── Why there is no second switch ─────────────────────────────────────────
    /// Delivery here rides on the ONE opt-in the user actually set,
    /// <c>monitoring.backgroundLocal</c> ("Keep monitoring when the browser is closed"),
    /// checked by the watch that arms the streams. It is deliberately NOT also gated on
    /// <c>notifications.desktop.orderFills</c>, which defaults OFF and exists to decide
    /// whether a toast interrupts you while you are sitting at the machine. Putting an
    /// already-opted-in delivery behind a second switch nobody set is how Phase 1 nearly
    /// un-shipped the alert toast — <b>a switch inherited from another caller is a policy
    /// nobody wrote down.</b> For the same reason the headless
    /// <see cref="DesktopNotificationService"/> is built WITHOUT the
    /// <see cref="DesktopNotificationCategories.OrderFills"/> category: it cannot ask
    /// <see cref="CircuitOrderCoverage"/> anything, so it would toast fills a circuit was
    /// already announcing.
    /// </para>
    ///
    /// <para>
    /// ── The wording is not a second copy ──────────────────────────────────────
    /// Every sentence comes from <see cref="AccessibilityFeedbackCoordinator"/> — the same
    /// words the in-session pipeline uses, so what the user hears at 03:00 and what they hear
    /// at their desk are the same sentence. A fill described two ways is a fill the user has
    /// to reconcile.
    /// </para>
    /// </summary>
    public sealed class HeadlessOrderAnnouncer : IDisposable
    {
        private readonly IDesktopAlertPresenter _presenter;
        private readonly ILogger<HeadlessOrderAnnouncer>? _logger;
        private readonly Func<string?, bool> _isCovered;
        private readonly List<IDisposable> _subs = new();

        /// <param name="isCovered">
        /// Whether an open browser session already announces this venue's fills. Injected so a
        /// test can drive both states — with a circuit covering the venue and with none — which
        /// is the only way the doubling hazard is actually proved. Defaults to
        /// <see cref="CircuitOrderCoverage.IsCovered"/>.
        /// </param>
        public HeadlessOrderAnnouncer(
            IEventBus bus,
            IDesktopAlertPresenter presenter,
            ILogger<HeadlessOrderAnnouncer>? logger = null,
            Func<string?, bool>? isCovered = null)
        {
            _presenter = presenter;
            _logger = logger;
            _isCovered = isCovered ?? CircuitOrderCoverage.IsCovered;

            // The money events, in the wording the in-session pipeline uses. Every one of
            // these is something that happened to the user's money while they were not
            // looking — which is the entire reason the headless session exists.
            _subs.Add(bus.Subscribe<OrderFilledEvent>(e =>
                Announce(e.Provider, "Order filled", AccessibilityFeedbackCoordinator.FormatFill("Order filled", e.Order))));
            _subs.Add(bus.Subscribe<OrderPartialFillEvent>(e =>
                Announce(e.Provider, "Partial fill", AccessibilityFeedbackCoordinator.FormatPartialFill(e.Order))));
            _subs.Add(bus.Subscribe<StopHitEvent>(e =>
            {
                string title = e.Order.Trailing ? "Trailing stop hit" : "Stop loss hit";
                Announce(e.Provider, title, AccessibilityFeedbackCoordinator.FormatFill(title, e.Order));
            }));
            _subs.Add(bus.Subscribe<TakeProfitHitEvent>(e =>
            {
                string title = e.Order.Trailing ? "Trailing take profit hit" : "Take profit hit";
                Announce(e.Provider, title, AccessibilityFeedbackCoordinator.FormatFill(title, e.Order));
            }));

            // An order that LEAVES the book unannounced is the same silence as a fill that
            // does: a trader who believes a stop is resting and finds it cancelled has been
            // unprotected for however long nobody said so. Rejections carry the venue's own
            // reason, because "it did not happen" without "why" is not actionable.
            _subs.Add(bus.Subscribe<OrderRejectedEvent>(e =>
            {
                string why = string.IsNullOrWhiteSpace(e.Reason) ? "" : " " + e.Reason.TrimEnd('.') + ".";
                Announce(e.Provider, "Order rejected", $"Order rejected for {e.Order.Symbol}.{why}");
            }));
            _subs.Add(bus.Subscribe<OrderCancelledEvent>(e =>
                Announce(e.Provider, "Order cancelled", AccessibilityFeedbackCoordinator.FormatTerminated("cancelled", e.Order))));
            _subs.Add(bus.Subscribe<OrderExpiredEvent>(e =>
                Announce(e.Provider, "Order expired", AccessibilityFeedbackCoordinator.FormatTerminated("expired", e.Order))));
            // Never "cancelled": a replaced order is STILL LIVE under a new id, and a trader
            // who hears "cancelled" believes they are flat, re-enters, and is double-sized.
            _subs.Add(bus.Subscribe<OrderReplacedEvent>(e =>
                Announce(e.Provider, "Order replaced",
                    $"Order replaced for {e.Order.Symbol}. It is still working under a new order id.")));
        }

        /// <summary>
        /// The one delivery path: skip when a browser session owns this venue, otherwise
        /// sound, toast and speech — in that order, so the cue lands before the sentence.
        /// </summary>
        private void Announce(string? provider, string title, string speech)
        {
            bool covered;
            try { covered = _isCovered(provider); }
            catch { covered = false; }   // an unanswerable coverage question is not coverage

            if (covered)
            {
                _logger?.LogDebug(
                    "Headless order announcement skipped for {Provider}: a browser session is announcing it.",
                    provider);
                return;
            }

            _logger?.LogInformation("Headless order event on {Provider}: {Text}", provider ?? "(unknown)", speech);

            // THREE separate attempts, not one. A machine with no audio player must still get the
            // toast and the speech; a desktop with no notification daemon must still get the
            // speech. Wrapping all three together would let the first broken channel take the
            // other two down with it — and the last of the three is the one a blind user actually
            // depends on.
            Try(() => _presenter.PlayNotificationSound(), "sound");
            Try(() => _presenter.Notify(title, ToastBody(title, speech), urgent: false), "toast");
            Try(() => _presenter.Speak(speech), "speech");
        }

        private void Try(Action deliver, string what)
        {
            try { deliver(); }
            catch (Exception ex)
            {
                // And never out to the bus: one broken channel must not cost every other
                // subscriber the event.
                _logger?.LogWarning(ex, "Headless order announcement could not deliver the {Channel}.", what);
            }
        }

        /// <summary>
        /// The toast BODY is the spoken sentence minus its own leading prefix, because that
        /// prefix is already the toast's TITLE — the same rule
        /// <c>DesktopNotificationService.FillBody</c> applies in a circuit, generalised from
        /// fills to every order event so all eight read the same way. A sentence whose lead
        /// is not the title (a rejection, a cancel) is shown whole rather than mangled.
        /// </summary>
        internal static string ToastBody(string title, string speech)
        {
            string lead = title + ". ";
            return speech.StartsWith(lead, StringComparison.Ordinal) ? speech[lead.Length..] : speech;
        }

        public void Dispose()
        {
            foreach (var s in _subs) { try { s.Dispose(); } catch { /* already gone */ } }
            _subs.Clear();
        }
    }
}
