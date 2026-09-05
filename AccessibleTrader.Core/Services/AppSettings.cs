using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Strongly-typed facade over the settings.json store (debt item 3, stage a).
    ///
    /// Why: preferences were read via <c>GetSetting("alerts.setups.enabled")?.ToObject&lt;bool&gt;() ?? false</c>
    /// at every call site — stringly-typed keys (typos silently default), repeated
    /// null-coalescing, and repeated default values that could drift apart between
    /// readers. Here each preference is one property: the key, the type, and the
    /// default live in exactly one place, and a typo is a compile error.
    ///
    /// Setters write through immediately but do NOT save; call <see cref="Save"/>
    /// once after a batch (mirrors the SettingsManager contract). Structured blobs
    /// (the named webhook list, the per-symbol setup routing map) stay on
    /// <see cref="ISettingsManager"/> via their loaders — this facade covers the
    /// scalar preferences.
    ///
    /// Stage (b) — migrating the preferences that currently live on WorkspaceState
    /// (speak timestamps, WASAPI latency, …) into this facade — is tracked in
    /// docs/TODO.md and lands separately.
    /// </summary>
    public interface IAppSettings
    {
        // Accessibility
        bool BrailleEnabled { get; set; }
        /// <summary>Shift-tier mutes also silence order-execution outcomes. Default false.</summary>
        bool MuteIncludesOrderEvents { get; set; }
        bool HoverSonification { get; set; }
        bool VisualEarcons { get; set; }

        /// <summary>
        /// Earcons about the MARKET — an alert firing, a new bar opening, a strategy setup
        /// arming or reaching its entry. Default true.
        /// </summary>
        bool ChartEarconsEnabled { get; set; }

        /// <summary>
        /// Earcons about the TERMINAL — the edge of the data, a mode toggled, an action that
        /// succeeded, a connection state. Default true.
        /// </summary>
        bool InterfaceEarconsEnabled { get; set; }

        // Appearance
        bool ColorVisionSafe { get; set; }
        bool HollowUpCandles { get; set; }

        // Analysis
        /// <summary>
        /// Seed the Market Structure overlay (HH/HL/LH/LL) on new OHLCV charts. Defaults to FALSE
        /// since 2026-09-05 — Cody: "I don't want it, only show candles, price and volume for a
        /// clean new default chart layout." It shipped ON from 2026-08 on the argument that
        /// structure is the orientation layer a sighted trader gets free from chart shape; the
        /// counter-argument, from use, is that a new chart should start with nothing the user did
        /// not put there. Opt-IN now, so a bare substitute returns the default.
        /// </summary>
        bool MarketStructureOnByDefault { get; set; }
        int UiScale { get; set; }

        // Audio
        string SoundTheme { get; set; }
        int WasapiLatency { get; set; }

        // Speech (persisted source for the WorkspaceState mirrors — see
        // PreferencePersistenceService, which seeds the store at startup and
        // writes changes back here)
        bool SpeakTimestamps { get; set; }
        bool SpeakDateOnEveryBar { get; set; }
        string TimestampReadLocation { get; set; }
        bool ReadColumnHeaders { get; set; }
        string SpeechOrder { get; set; }
        bool AnnounceNewBars { get; set; }
        bool DescribeChartPatterns { get; set; }
        bool DescribeCandlePatterns { get; set; }
        bool ShowChartPatternVisuals { get; set; }

        // Narration (Settings → Narration) — the unprompted speech channel.
        bool NarrateSignalsOnBarClose { get; set; }
        bool NarrateDuringPlayback { get; set; }
        bool SpeakPlaybackLandmarks { get; set; }

        // Viewport
        int PanningGranularity { get; set; }

        /// <summary>Touch navigation bar: "auto" (coarse-pointer devices only), "show", "hide".</summary>
        string TouchNavBarMode { get; set; }

        // Drawing
        bool MagnetSnap { get; set; }

        // Trading
        bool PaperTradingMode { get; set; }

        // Workspace
        bool BackgroundMonitoring { get; set; }
        int MonitorPollSeconds { get; set; }
        bool LiveBackgroundTabs { get; set; }
        bool ResumeLastSession { get; set; }

        // Alerts — email
        string EmailHost { get; set; }
        int EmailPort { get; set; }
        bool EmailUseTls { get; set; }
        string EmailUsername { get; set; }
        string EmailPassword { get; set; }
        string EmailFromAddress { get; set; }
        string EmailToAddress { get; set; }

        // Alerts — Telegram
        string TelegramBotToken { get; set; }
        string TelegramChatId { get; set; }

        // Alerts — strategy setup delivery
        bool SetupAlertsEnabled { get; set; }
        string SetupWebhookTarget { get; set; }

        /// <summary>Persists all pending writes (delegates to SettingsManager.SaveSettings).</summary>
        void Save();
    }

    public sealed class AppSettings : IAppSettings
    {
        private readonly ISettingsManager _sm;

        public AppSettings(ISettingsManager settingsManager) => _sm = settingsManager;

        private bool GetBool(string key, bool def = false) => _sm.GetSetting(key)?.ToObject<bool>() ?? def;
        private int GetInt(string key, int def) => _sm.GetSetting(key)?.ToObject<int>() ?? def;
        private string GetString(string key, string def = "") => _sm.GetSetting(key)?.ToString() ?? def;
        private void Set<T>(string key, T value) where T : notnull => _sm.SetSetting(key, JToken.FromObject(value));

        public bool BrailleEnabled
        {
            get => GetBool(SettingsKeys.BrailleEnabled);
            set => Set(SettingsKeys.BrailleEnabled, value);
        }

        public bool MuteIncludesOrderEvents
        {
            get => GetBool(SettingsKeys.MuteIncludesOrderEvents);
            set => Set(SettingsKeys.MuteIncludesOrderEvents, value);
        }
        public bool HoverSonification
        {
            get => GetBool(SettingsKeys.HoverSonification);
            set => Set(SettingsKeys.HoverSonification, value);
        }
        public bool VisualEarcons
        {
            get => GetBool(SettingsKeys.VisualEarcons);
            set => Set(SettingsKeys.VisualEarcons, value);
        }

        // Both opt-OUT: an install that has never opened Settings must sound exactly as it
        // did before the split, so the default has to be the pre-split behaviour.
        public bool ChartEarconsEnabled
        {
            get => GetBool(SettingsKeys.ChartEarconsEnabled, def: true);
            set => Set(SettingsKeys.ChartEarconsEnabled, value);
        }
        public bool InterfaceEarconsEnabled
        {
            get => GetBool(SettingsKeys.InterfaceEarconsEnabled, def: true);
            set => Set(SettingsKeys.InterfaceEarconsEnabled, value);
        }

        public bool ColorVisionSafe
        {
            get => GetBool(SettingsKeys.ColorVisionSafe);
            set => Set(SettingsKeys.ColorVisionSafe, value);
        }
        public bool MarketStructureOnByDefault
        {
            // Default false since 2026-09-05 — see the interface docs.
            get => GetBool(SettingsKeys.MarketStructureOnByDefault, def: false);
            set => Set(SettingsKeys.MarketStructureOnByDefault, value);
        }

        public bool HollowUpCandles
        {
            get => GetBool(SettingsKeys.HollowUpCandles);
            set => Set(SettingsKeys.HollowUpCandles, value);
        }
        public int UiScale
        {
            get => GetInt(SettingsKeys.UiScale, 100);
            set => Set(SettingsKeys.UiScale, value);
        }

        public string SoundTheme
        {
            get => GetString(SettingsKeys.SoundTheme, Audio.SoundThemes.ClassicId);
            set => Set(SettingsKeys.SoundTheme, value);
        }
        public int WasapiLatency
        {
            get => GetInt(SettingsKeys.WasapiLatency, 100);
            set => Set(SettingsKeys.WasapiLatency, value);
        }

        // Defaults mirror WorkspaceState.Initial — the store seeds from here at startup.
        public bool SpeakTimestamps
        {
            get => GetBool(SettingsKeys.SpeakTimestamps, def: true);
            set => Set(SettingsKeys.SpeakTimestamps, value);
        }

        /// <summary>
        /// Whether every bar's timestamp carries the date, or only the ones that cross into a new
        /// day. Default FALSE — boundaries only, which is what Cody asked for and the quieter
        /// reading: on an hour chart the date is the same for twenty-four bars in a row.
        ///
        /// <para>
        /// Opt-IN by design, so a bare substitute returning false gives the default behaviour —
        /// an opt-OUT setting would silently invert in every test that does not configure it.
        /// </para>
        /// </summary>
        public bool SpeakDateOnEveryBar
        {
            get => GetBool(SettingsKeys.SpeakDateOnEveryBar, def: false);
            set => Set(SettingsKeys.SpeakDateOnEveryBar, value);
        }
        public string TimestampReadLocation
        {
            get => GetString(SettingsKeys.TimestampReadLocation, "Along X Axis");
            set => Set(SettingsKeys.TimestampReadLocation, value);
        }
        public bool ReadColumnHeaders
        {
            get => GetBool(SettingsKeys.ReadColumnHeaders, def: true);
            set => Set(SettingsKeys.ReadColumnHeaders, value);
        }
        public string SpeechOrder
        {
            get => GetString(SettingsKeys.SpeechOrder, "HeaderValue");
            set => Set(SettingsKeys.SpeechOrder, value);
        }
        public bool AnnounceNewBars
        {
            get => GetBool(SettingsKeys.AnnounceNewBars, def: true);
            set => Set(SettingsKeys.AnnounceNewBars, value);
        }

        /// <summary>
        /// Speak classical chart formations (double top, head and shoulders, triangles, flags) when
        /// navigation lands inside one. Both patterns still forming and patterns already completed
        /// are announced, with the forming ones hedged and carrying their trigger level.
        ///
        /// <para>
        /// Default OFF. It adds continuous speech to a navigation action, and the standing
        /// convention here is that extra narration is opted into, never imposed.
        /// </para>
        /// </summary>
        public bool DescribeChartPatterns
        {
            get => GetBool(SettingsKeys.DescribeChartPatterns, def: false);
            set => Set(SettingsKeys.DescribeChartPatterns, value);
        }

        /// <summary>
        /// Whether a bar's CANDLE pattern is spoken — the closed bar's on a new-bar
        /// announcement, and the live bar's while it is still forming.
        ///
        /// <para>
        /// Not the same thing as <see cref="DescribeChartPatterns"/>, which is about multi-bar
        /// formations (double tops, triangles, head and shoulders). The two were a single
        /// undifferentiated idea until 2026-09-04, when only one of them had a switch and the
        /// Narration tab's own help text promised the other unconditionally.
        /// </para>
        ///
        /// <para>
        /// Default ON, the opposite of <see cref="DescribeChartPatterns"/> and for the reason the
        /// narration switches give: ON is what already shipped, and the clause rides on an
        /// announcement the user opted into (<see cref="AnnounceNewBars"/>) rather than creating
        /// a new occasion to speak. Turning it OFF is the new capability.
        /// </para>
        /// </summary>
        public bool DescribeCandlePatterns
        {
            get => GetBool(SettingsKeys.DescribeCandlePatterns, def: true);
            set => Set(SettingsKeys.DescribeCandlePatterns, value);
        }

        /// <summary>
        /// Master switch over the bar-close narrator. Default ON, and that is not the same
        /// decision as <see cref="DescribeChartPatterns"/>'s default OFF: the narrator speaks
        /// only about series the user has already opted in per-series with Ctrl+Alt+Shift+N, so
        /// ON adds nothing to a chart where nothing is flagged. It exists so the whole channel
        /// can be silenced in one place without un-flagging every series one at a time.
        /// </summary>
        public bool NarrateSignalsOnBarClose
        {
            get => GetBool(SettingsKeys.NarrateSignalsOnBarClose, def: true);
            set => Set(SettingsKeys.NarrateSignalsOnBarClose, value);
        }

        /// <summary>
        /// Whether playback speaks time landmarks, marker signals and chart-pattern outcomes
        /// while it runs. Default ON because ON is what already shipped — landmarks have spoken
        /// since 2026-09-02 — and because the signals it adds are behind the per-series narration
        /// flag. Turning it OFF is the new capability: playback as tones and nothing else.
        /// </summary>
        public bool NarrateDuringPlayback
        {
            get => GetBool(SettingsKeys.NarrateDuringPlayback, def: true);
            set => Set(SettingsKeys.NarrateDuringPlayback, value);
        }

        /// <summary>
        /// Draw the formations on the chart. Default OFF — a chart carrying five formations at once
        /// becomes unreadable, and the audience for the drawing is the secondary one.
        /// </summary>
        public bool ShowChartPatternVisuals
        {
            get => GetBool(SettingsKeys.ShowChartPatternVisuals, def: false);
            set => Set(SettingsKeys.ShowChartPatternVisuals, value);
        }

        /// <summary>
        /// The date or hour spoken as playback crosses a boundary. Default ON — it has spoken
        /// since 2026-09-02 and is the only thing that says WHERE IN TIME the tones are.
        /// </summary>
        public bool SpeakPlaybackLandmarks
        {
            get => GetBool(SettingsKeys.SpeakPlaybackLandmarks, def: true);
            set => Set(SettingsKeys.SpeakPlaybackLandmarks, value);
        }

        public int PanningGranularity
        {
            get => GetInt(SettingsKeys.PanningGranularity, 10);
            set => Set(SettingsKeys.PanningGranularity, value);
        }

        public string TouchNavBarMode
        {
            get => GetString(SettingsKeys.TouchNavBar, "auto");
            set => Set(SettingsKeys.TouchNavBar, value);
        }

        public bool MagnetSnap
        {
            get => GetBool(SettingsKeys.MagnetSnap);
            set => Set(SettingsKeys.MagnetSnap, value);
        }

        public bool PaperTradingMode
        {
            get => GetBool(SettingsKeys.PaperTradingMode);
            set => Set(SettingsKeys.PaperTradingMode, value);
        }

        public bool BackgroundMonitoring
        {
            get => GetBool(SettingsKeys.BackgroundMonitoring);
            set => Set(SettingsKeys.BackgroundMonitoring, value);
        }
        public bool LiveBackgroundTabs
        {
            get => GetBool(SettingsKeys.LiveBackgroundTabs);
            set => Set(SettingsKeys.LiveBackgroundTabs, value);
        }
        public bool ResumeLastSession
        {
            get => GetBool(SettingsKeys.ResumeLastSession, def: true);
            set => Set(SettingsKeys.ResumeLastSession, value);
        }
        public int MonitorPollSeconds
        {
            get => GetInt(SettingsKeys.MonitorPollSeconds, 30);
            set => Set(SettingsKeys.MonitorPollSeconds, value);
        }

        public string EmailHost
        {
            get => GetString(SettingsKeys.EmailHost);
            set => Set(SettingsKeys.EmailHost, value);
        }
        public int EmailPort
        {
            get => GetInt(SettingsKeys.EmailPort, 587);
            set => Set(SettingsKeys.EmailPort, value);
        }
        public bool EmailUseTls
        {
            get => GetBool(SettingsKeys.EmailUseTls, def: true);
            set => Set(SettingsKeys.EmailUseTls, value);
        }
        public string EmailUsername
        {
            get => GetString(SettingsKeys.EmailUsername);
            set => Set(SettingsKeys.EmailUsername, value);
        }
        public string EmailPassword
        {
            get => GetString(SettingsKeys.EmailPassword);
            set => Set(SettingsKeys.EmailPassword, value);
        }
        public string EmailFromAddress
        {
            get => GetString(SettingsKeys.EmailFromAddress);
            set => Set(SettingsKeys.EmailFromAddress, value);
        }
        public string EmailToAddress
        {
            get => GetString(SettingsKeys.EmailToAddress);
            set => Set(SettingsKeys.EmailToAddress, value);
        }

        public string TelegramBotToken
        {
            get => GetString(SettingsKeys.TelegramBotToken);
            set => Set(SettingsKeys.TelegramBotToken, value);
        }
        public string TelegramChatId
        {
            get => GetString(SettingsKeys.TelegramChatId);
            set => Set(SettingsKeys.TelegramChatId, value);
        }

        public bool SetupAlertsEnabled
        {
            get => GetBool(SettingsKeys.SetupAlertsEnabled);
            set => Set(SettingsKeys.SetupAlertsEnabled, value);
        }
        public string SetupWebhookTarget
        {
            get => GetString(SettingsKeys.SetupWebhookTarget);
            set => Set(SettingsKeys.SetupWebhookTarget, value);
        }

        public void Save() => _sm.SaveSettings();
    }
}
