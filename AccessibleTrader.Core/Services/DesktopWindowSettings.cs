namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Settings that govern the desktop window itself, rather than anything drawn inside it.
    ///
    /// <para>
    /// One constant with two readers a project apart — the Settings → General checkbox in the
    /// component library and the Windows tray applet in the MAUI head — is exactly the shape
    /// that has drifted in this repo before (four separate provider-name drifts, one of which
    /// saved API keys under a name nothing answered to). The key lives here so there is one
    /// spelling of it.
    /// </para>
    /// </summary>
    public static class DesktopWindowSettings
    {
        /// <summary>
        /// "Minimize to tray on exit" — closing the window hides the app to the notification
        /// area instead of quitting, so alerts, fills and feeds keep running.
        ///
        /// <para>
        /// <b>Default OFF, deliberately (Cody, 2026-09-06).</b> An app that does not close when
        /// you close it is a surprise, and for a screen-reader user a surprise with no
        /// announcement is worse than an extra keystroke. Absent means off: every reader uses
        /// <c>?? false</c>, so a settings file written before this key existed behaves the way
        /// the app always did.
        /// </para>
        /// </summary>
        public const string MinimizeToTrayKey = "app.minimizeToTray";
    }
}
