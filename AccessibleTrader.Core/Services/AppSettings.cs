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

        // Appearance
        bool ColorVisionSafe { get; set; }
        bool HollowUpCandles { get; set; }

        // Analysis
        /// <summary>
        /// Seed the Market Structure overlay (HH/HL/LH/LL) on new OHLCV charts. Defaults to TRUE:
        /// structure is the orientation layer a sighted trader gets free from chart shape, so a
        /// blind user should not have to know it exists in order to receive it.
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
        string TimestampReadLocation { get; set; }
        bool ReadColumnHeaders { get; set; }
        string SpeechOrder { get; set; }
        bool AnnounceNewBars { get; set; }
        bool DescribeChartPatterns { get; set; }

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

        public bool ColorVisionSafe
        {
            get => GetBool(SettingsKeys.ColorVisionSafe);
            set => Set(SettingsKeys.ColorVisionSafe, value);
        }
        public bool MarketStructureOnByDefault
        {
            // Default true — see the interface docs for why this one is opt-OUT.
            get => GetBool(SettingsKeys.MarketStructureOnByDefault, def: true);
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
