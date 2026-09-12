namespace AccessibleTrader.Core.Services.Notifications
{
    /// <summary>
    /// A desktop notification — the operating system's own toast — behind one seam.
    ///
    /// <para>
    /// Cody, 2026-09-05: "is it possible for the webhost to send desktop notifications using
    /// the mate notification center? How about the maui head, can it be added here for
    /// windows toast notifications?" Yes to both, and they are the same feature seen from two
    /// heads: <see cref="DesktopNotificationService"/> decides WHAT is worth a notification and
    /// WHEN — since 2026-09-11 that is <see cref="NotificationPolicy"/>'s rule (the chart in
    /// front of the trader is spoken and never notified; everything else is notified) behind ONE
    /// switch, not the three it used to read — and an implementation of this interface owns only
    /// the delivery, which is the part that needs a real desktop. On the
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
        /// Whether this head can show a notification at all. False on hosted, demo, and any
        /// desktop without a notification path.
        ///
        /// <para>It no longer hides the settings panel, and that was a real defect: a desktop
        /// with no <c>notify-send</c> lost the background-tab speech switch and the timeframe
        /// floor along with the notification switch — backwards, because on that machine speech
        /// IS the delivery channel (<c>DesktopAnnouncement.Present</c> speaks exactly where
        /// nothing can read a notification). The panel now renders and says so.</para>
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
