using AccessibleTrader.Core.Services.Notifications;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The local WebHost's desktop toast: <c>notify-send</c>, through the same
    /// <see cref="IDesktopAlertPresenter"/> the background monitor uses. On MATE the
    /// freedesktop notification goes to <c>mate-notification-daemon</c>, which shows it in the
    /// notification area and keeps it in the notification history; Orca presents notifications
    /// through its own notification-messages feature. GNOME, KDE and any other daemon that
    /// implements <c>org.freedesktop.Notifications</c> behave the same, which is why this does
    /// not talk to MATE specifically.
    ///
    /// <para>
    /// Normal urgency always. Critical urgency is the monitor's word for "I can no longer watch
    /// a feed"; an alert firing normally, a fill, or a bar closing is ordinary news.
    /// </para>
    /// </summary>
    public sealed class NotifySendDesktopNotifier : IDesktopNotifier
    {
        private readonly IDesktopAlertPresenter _presenter;

        public NotifySendDesktopNotifier(IDesktopAlertPresenter presenter) => _presenter = presenter;

        public bool IsAvailable => _presenter.CanNotify;

        public string Describe() => IsAvailable
            ? "notify-send (the desktop's notification daemon; MATE, GNOME and KDE all show it)"
            : "notify-send is not installed";

        public void Notify(string title, string body) => _presenter.Notify(title, body, urgent: false);
    }
}
