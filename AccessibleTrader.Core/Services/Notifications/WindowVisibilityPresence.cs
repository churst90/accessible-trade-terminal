namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// <b>The desktop app's answer to "can the user see me?"</b> — a singleton the window code
    /// flips when the app hides to the tray and restores.
    ///
    /// <para>
    /// Cody, 2026-09-11: <i>"On the MAUI heads, if the person closes the application with the X
    /// in the upper corner or Alt+F4, then it should, by default, minimize to tray and toast
    /// notifications should be sent."</i> Hiding to the tray is that head's "browser closed":
    /// the app is still running, still watching, still evaluating — but its live region is
    /// reaching nobody, so every terminal event has to take the notification channel instead,
    /// the focused chart's own bar close included.
    /// </para>
    ///
    /// <para>
    /// <see cref="CanReachUser"/> stays TRUE while hidden, and that is not an oversight: on this
    /// head there is no second process waiting to take over, so "cannot reach" would mean total
    /// silence. The two questions are genuinely different — see <see cref="IUserPresence"/>.
    /// </para>
    ///
    /// <para>
    /// It lives in Core rather than in the MAUI project so the behaviour it drives can be tested
    /// on a Linux box, where the Windows TFM does not build. The window code is one line; the
    /// rule is everything else.
    /// </para>
    /// </summary>
    public sealed class WindowVisibilityPresence : IUserPresence
    {
        private volatile bool _visible = true;

        /// <summary>Always true on a desktop head: the process IS the delivery.</summary>
        public bool CanReachUser => true;

        public bool IsAppVisible => _visible;

        /// <summary>The window was hidden to the tray, or restored from it.</summary>
        public void SetVisible(bool visible) => _visible = visible;
    }
}
