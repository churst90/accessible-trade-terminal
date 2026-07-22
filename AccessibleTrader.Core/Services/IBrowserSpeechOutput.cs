namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// How the web head's speech reaches the user when the browser is the last
    /// hop. Browsers deliberately do NOT expose whether a screen reader is
    /// running (privacy), so this is an explicit user choice, never a detection.
    /// </summary>
    public enum SpeechOutputMode
    {
        /// <summary>ARIA live region AND browser SpeechSynthesis — the historical
        /// default. A screen-reader user hears everything twice; kept as the
        /// pre-choice default so no one gets silence.</summary>
        Both,
        /// <summary>ARIA live region only — for screen-reader users (Orca, NVDA,
        /// JAWS, VoiceOver read the live region themselves).</summary>
        ScreenReader,
        /// <summary>Browser SpeechSynthesis only — for users without a screen
        /// reader; the live region stays empty so a screen reader that IS
        /// running won't double-speak.</summary>
        BrowserVoice,
    }

    /// <summary>
    /// Optional capability of the web head's speech manager. Registered only by
    /// the WebHost (and only meaningful there when the speech backend is browser
    /// TTS); the Settings UI resolves it optionally, so the desktop heads never
    /// see the control.
    /// </summary>
    public interface IBrowserSpeechOutput
    {
        /// <summary>True when the last speech hop is the visitor's browser
        /// (no server-side Orca / speech-dispatcher) — the only situation where
        /// the mode matters and where its UI should appear.</summary>
        bool IsBrowserTtsBackend { get; }

        /// <summary>Current output mode for this circuit. Persisting the choice
        /// (localStorage — it describes THIS browser's assistive stack, not the
        /// account) is the caller's job.</summary>
        SpeechOutputMode Mode { get; set; }
    }
}
