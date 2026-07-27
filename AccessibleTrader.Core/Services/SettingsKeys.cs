namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Every settings.json key path, in one place. A key that exists only as a
    /// string literal at its call sites can be typo'd into a silent default —
    /// "alerts.setup.enabled" instead of "alerts.setups.enabled" compiles clean
    /// and simply never works. All reads and writes should go through these
    /// constants (or better, through <see cref="IAppSettings"/>, which uses them).
    ///
    /// Legacy per-service constants (ThemeService.ColorVisionSafeKey,
    /// BackgroundMonitoringService.EnabledKey, …) now alias into this class, so
    /// existing call sites keep compiling while new code has one home to look in.
    /// </summary>
    public static class SettingsKeys
    {
        // ── Accessibility ────────────────────────────────────────────────────
        public const string BrailleEnabled     = "accessibility.braille.enabled";
        // Opt-in: Shift+F2 / Shift+F3 mutes ALSO silence order-execution outcomes
        // (fills, stops, TPs). Default false — money events break through mutes.
        public const string MuteIncludesOrderEvents = "speech.muteIncludesOrderEvents";
        public const string HoverSonification  = "accessibility.hoverSonification";
        public const string VisualEarcons      = "accessibility.visualEarcons";

        // ── Appearance ───────────────────────────────────────────────────────
        public const string BackgroundColor    = "appearance.backgroundColor";
        public const string BackgroundGradient = "appearance.backgroundGradient";   // bool: gradient vs solid
        public const string BackgroundColor2   = "appearance.backgroundColor2";     // gradient end (bottom) color

        // Up/down colours for candles and volume. These are an APP-LEVEL preference, not a
        // per-theme one: which colour means "up" is a habit a trader carries between themes,
        // and having it change under them when they try a new look is exactly wrong. Absent
        // means "use the theme's own pair", which is how the accessibility themes keep their
        // deliberate high-contrast choices.
        public const string BullishColor       = "appearance.bullishColor";
        public const string BearishColor       = "appearance.bearishColor";

        // Treat the toolbar band, the chart and the footer band as slices of ONE top-to-bottom
        // fade instead of three independently coloured regions. Off by default: a theme decides
        // its own look, and this overrides all three at once.
        public const string UnifiedGradient       = "appearance.unifiedGradient";
        public const string UnifiedGradientTop    = "appearance.unifiedGradientTop";
        public const string UnifiedGradientBottom = "appearance.unifiedGradientBottom";
        public const string ColorVisionSafe    = "appearance.colorVisionSafe";
        public const string HollowUpCandles    = "appearance.hollowUpCandles";
        /// <summary>Auto-add the Market Structure overlay to new OHLCV charts. Default TRUE.</summary>
        public const string MarketStructureOnByDefault = "analysis.marketStructureDefault";
        public const string UiScale            = "appearance.uiScale";
        public const string UiTheme            = "ui.theme";
        /// <summary>"auto" (touch devices only), "show", or "hide".</summary>
        public const string TouchNavBar        = "ui.touchNavBar";

        // ── Audio ────────────────────────────────────────────────────────────
        public const string SoundTheme         = "audio.soundTheme";
        public const string WasapiLatency      = "audio.wasapiLatency";

        // ── Speech (stage b: persisted mirrors of WorkspaceState preferences) ─
        public const string SpeakTimestamps       = "speech.speakTimestamps";
        public const string TimestampReadLocation = "speech.timestampReadLocation";
        public const string ReadColumnHeaders     = "speech.readColumnHeaders";
        public const string SpeechOrder           = "speech.speechOrder";
        public const string AnnounceNewBars       = "speech.announceNewBars";

        // ── Viewport ─────────────────────────────────────────────────────────
        public const string PanningGranularity  = "viewport.panningGranularity";

        // ── Drawing ──────────────────────────────────────────────────────────
        public const string MagnetSnap         = "drawing.magnetSnap";

        // ── Trading ──────────────────────────────────────────────────────────
        public const string PaperTradingMode   = "trading.paperTradingMode";

        // ── Workspace ────────────────────────────────────────────────────────
        public const string BackgroundMonitoring = "workspace.backgroundMonitoring";
        public const string MonitorPollSeconds   = "workspace.monitorPollSeconds";
        public const string LiveBackgroundTabs   = "workspace.liveBackgroundTabs";
        public const string ResumeLastSession    = "workspace.resumeLastSession";

        // ── Alerts: email (SMTP) ─────────────────────────────────────────────
        public const string EmailHost          = "alerts.email.host";
        public const string EmailPort          = "alerts.email.port";
        public const string EmailUseTls        = "alerts.email.useTls";
        public const string EmailUsername      = "alerts.email.username";
        public const string EmailPassword      = "alerts.email.password";
        public const string EmailFromAddress   = "alerts.email.fromAddress";
        public const string EmailToAddress     = "alerts.email.toAddress";

        // ── Alerts: Telegram ─────────────────────────────────────────────────
        public const string TelegramBotToken   = "alerts.telegram.botToken";
        public const string TelegramChatId     = "alerts.telegram.chatId";

        // ── Alerts: webhooks ─────────────────────────────────────────────────
        /// <summary>Named webhook list (JSON array — parsed by WebhookAlertConfigLoader).</summary>
        public const string Webhooks           = "alerts.webhooks";
        /// <summary>Legacy single webhook URL (auto-migrated into <see cref="Webhooks"/>).</summary>
        public const string LegacyWebhookUrl   = "alerts.webhook.url";
        /// <summary>Legacy single webhook auth header.</summary>
        public const string LegacyWebhookAuth  = "alerts.webhook.authHeader";

        // ── Alerts: strategy setup delivery ──────────────────────────────────
        public const string SetupAlertsEnabled = "alerts.setups.enabled";
        /// <summary>Per-symbol webhook routing map (JSON object {"BTC/USD": "BTC channel"}).</summary>
        public const string SetupWebhookMap    = "alerts.setups.webhookMap";
        public const string SetupWebhookTarget = "alerts.setups.webhookTarget";
    }
}
