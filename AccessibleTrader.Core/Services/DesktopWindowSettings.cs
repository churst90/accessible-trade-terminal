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
        /// <b>Default ON since 2026-09-11 (Cody).</b> <i>"On the MAUI heads, if the person closes
        /// the application with the X in the upper corner or Alt+F4, then it should, by default,
        /// minimize to tray and toast notifications should be sent."</i> This makes the MAUI head
        /// behave like the WebHost, where closing the browser hands every terminal event to the
        /// notification channel rather than ending the watch.
        /// </para>
        ///
        /// <para>
        /// It reverses the 2026-09-06 default, and the reason recorded then was real: an app
        /// that does not close when you close it is a surprise, and for a screen-reader user a
        /// surprise with no announcement is worse than an extra keystroke. The answer is the
        /// announcement, not the extra keystroke — hiding to the tray now says so, and the tray
        /// menu carries a Quit. Read through <see cref="MinimizeToTray"/> so all three readers
        /// agree about what "absent" means.
        /// </para>
        /// </summary>
        public const string MinimizeToTrayKey = "app.minimizeToTray";

        /// <summary>The default for <see cref="MinimizeToTrayKey"/>: ON.</summary>
        public const bool MinimizeToTrayDefault = true;

        /// <summary>
        /// The one reader. Three places asked this question — the Settings checkbox, the Windows
        /// tray applet and the tests — and each carried its own <c>?? false</c>, which is three
        /// places to change a default and two chances to miss one.
        /// </summary>
        public static bool MinimizeToTray(ISettingsManager? settings)
        {
            if (settings == null) return MinimizeToTrayDefault;
            try { return settings.GetSetting(MinimizeToTrayKey)?.ToObject<bool>() ?? MinimizeToTrayDefault; }
            catch { return MinimizeToTrayDefault; }
        }
    }
}
