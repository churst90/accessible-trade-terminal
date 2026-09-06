using AccessibleTrader.Core.Services.Notifications;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The local WebHost's desktop toast, whatever this desktop's toast happens to be, through
    /// the same <see cref="IDesktopAlertPresenter"/> the background monitor uses.
    ///
    /// <para>
    /// Was <c>NotifySendDesktopNotifier</c> until 2026-09-06, and the rename is the fix: the
    /// class was registered for <c>HostMode.Full</c> on every operating system, but everything
    /// underneath it probed for Linux binaries only, so a Windows or macOS user got a notifier
    /// that reported itself unavailable and a delivery panel that hid its switches with no
    /// explanation. The per-OS routing now lives in <see cref="DesktopDeliveryPlan"/> — on Linux
    /// <c>notify-send</c> (MATE, GNOME, KDE and anything else implementing
    /// <c>org.freedesktop.Notifications</c>), on macOS <c>terminal-notifier</c> or
    /// <c>osascript</c> into Notification Center, on Windows a PowerShell toast into the Action
    /// Center.
    /// </para>
    ///
    /// <para>
    /// Normal urgency always. Critical urgency is the monitor's word for "I can no longer watch
    /// a feed"; an alert firing normally, a fill, or a bar closing is ordinary news.
    /// </para>
    /// </summary>
    public sealed class LocalDesktopNotifier : IDesktopNotifier
    {
        private readonly IDesktopAlertPresenter _presenter;

        public LocalDesktopNotifier(IDesktopAlertPresenter presenter) => _presenter = presenter;

        public bool IsAvailable => _presenter.CanNotify;

        public string Describe() => _presenter.DescribeToast();

        public void Notify(string title, string body) => _presenter.Notify(title, body, urgent: false);
    }
}
