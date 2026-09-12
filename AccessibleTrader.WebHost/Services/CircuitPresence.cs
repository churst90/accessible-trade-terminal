using AccessibleTrader.Core.Services.Notifications;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>One browser circuit's answer to "can I still reach my user?"</b> Scoped, so every
    /// deliverer in a circuit's DI scope shares one instance, and the circuit handler flips it.
    ///
    /// <para>
    /// It exists because a closed tab's circuit does not die with the tab: Blazor retains it for
    /// about three minutes in case the client reconnects, and every service in its scope goes on
    /// running. The headless session takes ownership the instant the connection drops, so for
    /// those three minutes the circuit's <c>AlertDeliveryService</c> and its
    /// <c>DesktopNotificationService</c> were delivering the same alerts a second time —
    /// including a second email, a second Telegram message and a second webhook POST, which can
    /// place a duplicate order at the far end.
    /// </para>
    ///
    /// <para>
    /// <b>Starts true.</b> A circuit exists because a browser asked for one, and
    /// <c>OnConnectionUpAsync</c> may not have run yet when the first event arrives. Failing
    /// towards "audible" is the rule everywhere in this codebase: an unheard alert has no
    /// second channel for this user.
    /// </para>
    /// </summary>
    public sealed class CircuitPresence : IUserPresence
    {
        private volatile bool _connected = true;

        public bool CanReachUser => _connected;

        internal void SetConnected(bool connected) => _connected = connected;
    }
}
