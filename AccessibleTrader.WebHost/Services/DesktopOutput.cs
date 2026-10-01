namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// <b>The one switch that keeps a WebHost off the desktop it runs on.</b> With
    /// <c>ACCESSIBLETRADER_NO_DESKTOP_OUTPUT=1</c> the host starts no local audio player (chart
    /// tones go to the browser instead), does not speak through spd-say or Orca (speech goes to the
    /// browser), raises no notifications and plays no notification sounds, gives the tray no
    /// tools to run, and does not open a browser at startup.
    ///
    /// <para>
    /// <b>Why it exists.</b> Both test projects set it as they load. Before 2026-10-01 a test run
    /// on Cody's machine played chart tones through his speakers via <c>pacat</c>, spoke over his
    /// screen reader, and pulled his keyboard focus away while he worked. A process watch during
    /// a campaign found <c>pacat</c> children of both test hosts. The test hosts already pinned
    /// speech to the browser by hand; the audio driver, the notification presenter, the tray and
    /// the launch each had their own probe, so each one had to be remembered separately. One
    /// switch, read at every probe, is the version that cannot forget one.
    /// </para>
    ///
    /// <para>Never set it in production. The terminal exists to speak and sound.</para>
    /// </summary>
    public static class DesktopOutput
    {
        public const string Variable = "ACCESSIBLETRADER_NO_DESKTOP_OUTPUT";

        /// <summary>True when the host must not touch the local desktop. Read on every call, so a
        /// test that sets or clears it sees the change.</summary>
        public static bool Suppressed => Environment.GetEnvironmentVariable(Variable) == "1";

        /// <summary>The file probe to hand a local-tool search: the real one, or one that finds
        /// nothing while output is suppressed.</summary>
        public static Func<string, bool> FileProbe => Suppressed ? _ => false : File.Exists;
    }
}
