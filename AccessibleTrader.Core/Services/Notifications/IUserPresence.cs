namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// <b>Can this instance of the app still reach the person using it?</b>
    ///
    /// <para>
    /// On every head but the Blazor Server WebHost the answer is always yes: the app and its
    /// user interface are the same process, and if the process is running the user can be
    /// reached. On the WebHost they are not. Closing a browser tab does not close its circuit —
    /// Blazor retains a disconnected circuit for about three minutes in case the client comes
    /// back — and for those three minutes every service in that circuit's DI scope is alive and
    /// running: the alert orchestrator, the delivery fan-out, the toaster, the bar announcer.
    /// </para>
    ///
    /// <para>
    /// ── What that cost, before this existed ───────────────────────────────────
    /// The headless monitor correctly takes ownership the instant the connection drops (a
    /// circuit that cannot reach the user covers nothing). So for up to three minutes an alert
    /// fired in BOTH pipelines: two desktop notifications, two entries in the recent-alerts
    /// list, and — the one that is not merely annoying — <b>two emails, two Telegram messages
    /// and two webhook POSTs</b>. A duplicated outbound webhook can place a duplicate order on
    /// whatever is listening at the other end.
    /// </para>
    ///
    /// <para>
    /// Anything that DELIVERS to the user must consult this. Anything that merely computes
    /// must not: the circuit keeps evaluating so that a reconnect finds warm state, exactly as
    /// the headless monitor keeps observing charts a browser is covering.
    /// </para>
    /// </summary>
    public interface IUserPresence
    {
        /// <summary>False only while a browser circuit is disconnected but not yet disposed.</summary>
        bool CanReachUser { get; }

        /// <summary>
        /// <b>Can the user actually SEE this app's own window right now?</b> A different question
        /// from <see cref="CanReachUser"/>, and the two are not opposites.
        ///
        /// <para>
        /// It decides one thing: whether the chart in front of the trader may be toasted. While
        /// the app is visible it may not be — the live region is already saying it, and a toast
        /// for something you are being told anyway is the "notification a minute" Cody reported.
        /// While the app is HIDDEN, it must be, because the live region is reaching nobody.
        /// </para>
        ///
        /// <para>
        /// On the WebHost this tracks the circuit connection: <b>minimised counts as visible</b>
        /// (Cody, 2026-09-11 — "if the browser is open… that includes if the browser is
        /// minimized"), because the browser is still rendering the live region and the screen
        /// reader is still reading it. On the MAUI heads it is false once the window has been
        /// hidden to the tray, which is that head's "browser closed".
        /// </para>
        ///
        /// <para>Defaulted to true so an existing implementation keeps compiling and keeps the
        /// quieter behaviour: a presence service that has not thought about visibility must not
        /// start toasting the focused chart.</para>
        /// </summary>
        bool IsAppVisible => true;
    }

    /// <summary>
    /// The answer on a head where the UI is the process: MAUI, and any test that does not care.
    /// Registered as the default so an unregistered <see cref="IUserPresence"/> can never mean
    /// "silent" — a missing presence service must fail towards being heard.
    /// </summary>
    public sealed class AlwaysPresent : IUserPresence
    {
        public static readonly AlwaysPresent Instance = new();
        public bool CanReachUser => true;
        public bool IsAppVisible => true;
    }
}
