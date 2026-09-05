#if WINDOWS
using AccessibleTrader.Core.Services.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AccessibleTrader.BlazorClient.Platforms.Windows
{
    /// <summary>
    /// Windows toast notifications for the MAUI head, through the Windows App SDK's
    /// <c>AppNotificationManager</c> — the API that works for an UNPACKAGED app, which this
    /// one is (<c>WindowsPackageType=None</c> in the csproj). Narrator, NVDA and JAWS read
    /// toasts natively, and the toast lands in the Action Center so a missed one can be read
    /// back later.
    ///
    /// <para>
    /// <b>Registration happens on first use, not at startup.</b> <c>Register()</c> is what
    /// tells Windows which process the toast belongs to; doing it lazily means a user who never
    /// switches a notification on never registers anything. It is idempotent per process.
    /// Unregister is deliberately not called: with the switch on, the process owning the toast
    /// is the one still running.
    /// </para>
    ///
    /// <para>
    /// <b>Not built on this repo's Linux CI.</b> The Windows TFM is excluded there (see the
    /// csproj), so this file compiles only on a Windows build. The Windows App SDK ships with
    /// Microsoft.Maui.Controls on that TFM; no extra package. If <c>Register()</c> throws
    /// (a Windows build older than 10.0.17763, or a broken AppSDK runtime), the notifier reports
    /// itself unavailable and the delivery panel hides the switches rather than offering three
    /// that do nothing.
    /// </para>
    /// </summary>
    public sealed class WindowsDesktopNotifier : IDesktopNotifier
    {
        private readonly ILogger<WindowsDesktopNotifier>? _logger;
        private bool _registered;
        private bool _broken;

        public WindowsDesktopNotifier(ILogger<WindowsDesktopNotifier>? logger = null) => _logger = logger;

        public bool IsAvailable => !_broken;

        public string Describe() => "Windows toast notifications (Windows App SDK)";

        public void Notify(string title, string body)
        {
            if (_broken) return;
            try
            {
                if (!_registered)
                {
                    AppNotificationManager.Default.Register();
                    _registered = true;
                }
                var toast = new AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body)
                    .BuildNotification();
                AppNotificationManager.Default.Show(toast);
            }
            catch (Exception ex)
            {
                // One failure is a log line; a failure to REGISTER means every later call
                // would fail the same way, so stop offering the feature.
                _logger?.LogWarning(ex, "Windows toast failed: {Title}", title);
                if (!_registered) _broken = true;
            }
        }
    }
}
#endif
