namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// A desktop notification — the operating system's own toast — behind one seam.
    ///
    /// <para>
    /// Cody, 2026-09-05: "is it possible for the webhost to send desktop notifications using
    /// the mate notification center? How about the maui head, can it be added here for
    /// windows toast notifications?" Yes to both, and they are the same feature seen from two
    /// heads: <see cref="DesktopNotificationService"/> decides WHAT is worth a toast and WHEN
    /// (the three switches under the alert delivery panel), and an implementation of this
    /// interface owns only the delivery, which is the part that needs a real desktop. On the
    /// local WebHost that is <c>notify-send</c>, which the MATE notification daemon shows like
    /// any other freedesktop notification and Orca can present. On the Windows MAUI head it is
    /// the Windows App SDK's <c>AppNotificationManager</c>, which Narrator, NVDA and JAWS read
    /// natively. Hosted and demo servers have no desktop that reaches the user, and register the
    /// <see cref="NullDesktopNotifier"/>; the hosted terminal's Web Push path is separate and
    /// unchanged.
    /// </para>
    /// </summary>
    public interface IDesktopNotifier
    {
        /// <summary>
        /// Whether this head can show a toast at all. False on hosted, demo, and any desktop
        /// without a notification path — the settings panel hides its switches then, rather
        /// than offering three checkboxes that do nothing.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>What delivers the toast, for a log line and the settings panel's hint.</summary>
        string Describe();

        /// <summary>Show one notification. Must not throw: a toast that fails is a log line, never a crash on the event bus.</summary>
        void Notify(string title, string body);
    }

    /// <summary>The head has no toast path. Everything stays in-session speech.</summary>
    public sealed class NullDesktopNotifier : IDesktopNotifier
    {
        public bool IsAvailable => false;
        public string Describe() => "no desktop notification path on this host";
        public void Notify(string title, string body) { }
    }
}
