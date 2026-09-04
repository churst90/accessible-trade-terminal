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
        // RETIRED 2026-09-03 with the settings restructure, and deliberately NOT kept as
        // constants: appearance.backgroundColor, appearance.backgroundGradient,
        // appearance.backgroundColor2, appearance.bullishColor, appearance.bearishColor,
        // appearance.unifiedGradient, appearance.unifiedGradientTop and
        // appearance.unifiedGradientBottom. Each was an app-level colour layered over EVERY
        // theme. Colours are a property of a theme now — the same six surfaces are fields in
        // the theme editor and the window fade is one button there — and a stale value under
        // one of these paths in settings.json is ignored (ThemeService.WithAccessibilityOverrides).

        // Id of the user's own theme currently in use, or empty for a built-in one. Kept
        // separate from UiTheme rather than folded into it: a custom theme still records which
        // built-in it is BASED ON, so both facts have to survive a restart.
        public const string CustomThemeId        = "appearance.customThemeId";

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
        /// <summary>
        /// Persisted and mirrored into <c>WorkspaceState</c> and the audio profile, and read by
        /// NOTHING in the audio stack — no driver on any head consults it. Its Settings control was
        /// removed 2026-09-03; the key and the state field stay only so saved profiles and the
        /// sandbox wire format keep their shape. See <c>SettingsWiringAuditTests.NoDirectControl</c>.
        /// </summary>
        public const string WasapiLatency      = "audio.wasapiLatency";

        // ── Speech (stage b: persisted mirrors of WorkspaceState preferences) ─
        public const string SpeakTimestamps       = "speech.speakTimestamps";
        public const string TimestampReadLocation = "speech.timestampReadLocation";
        public const string ReadColumnHeaders     = "speech.readColumnHeaders";
        public const string SpeechOrder           = "speech.speechOrder";
        public const string AnnounceNewBars       = "speech.announceNewBars";

        // Chart-pattern description. OFF by default: it adds speech to a navigation action the user
        // already knows the shape of, so it has to be asked for rather than imposed — the same
        // opt-in rule the visual-accessibility additions follow.
        public const string DescribeChartPatterns = "speech.describeChartPatterns";

        // ── Narration (Settings → Narration): what the terminal says when the user
        // pressed NOTHING. The Speech tab governs how it says what you ASKED for; these
        // two govern the unprompted channel. The split is by TRIGGER, not by topic —
        // that is what keeps "Describe chart patterns" in Speech (it also changes what
        // the arrow keys say) while "Announce new bars" belongs here.

        /// <summary>
        /// Master switch over the bar-close narrator (<c>AutoNarrationService</c>). Default ON,
        /// because it changes nothing on its own: the narrator only ever scans series the user
        /// flagged with Ctrl+Alt+Shift+N, so on a chart with none flagged this switch has nothing
        /// to gate. Ctrl+Alt+Shift+N picks WHAT speaks; this says WHETHER any of it does.
        /// </summary>
        public const string NarrateSignalsOnBarClose = "narration.signalsOnBarClose";

        /// <summary>
        /// Whether playback speaks at all beyond its own start/pause/stop/speed confirmations —
        /// time landmarks, marker signals, and chart-pattern outcomes. Default ON for the same
        /// reason: ON preserves exactly what shipped (landmarks), and everything it ADDS is
        /// already behind an opt-in (the per-series narration flag, and "Describe chart patterns"
        /// for the outcomes). OFF is the thing nobody had before — playback as pure tones.
        /// </summary>
        public const string NarrateDuringPlayback = "narration.duringPlayback";

        /// <summary>
        /// Draw chart formations on the canvas. Appearance rather than speech: the audience is a
        /// low-vision or sighted viewer, since a blind user already has the whole formation by ear.
        /// </summary>
        public const string ShowChartPatternVisuals = "appearance.showChartPatternVisuals";

        // ── Viewport ─────────────────────────────────────────────────────────
        public const string PanningGranularity  = "viewport.panningGranularity";

        // ── Drawing ──────────────────────────────────────────────────────────
        public const string MagnetSnap         = "drawing.magnetSnap";

        // ── Trading ──────────────────────────────────────────────────────────
        public const string PaperTradingMode   = "trading.paperTradingMode";

        /// <summary>
        /// What the quick-trade risk percentage is a percentage OF — see
        /// <see cref="Trading.QuickTradeSizingMode"/>. Stored as the enum's integer value.
        /// Defaults to position value, which is what an exchange order ticket does and what most
        /// people mean by "half a percent".
        /// </summary>
        public const string QuickTradeSizingMode = "trading.quickTradeSizingMode";

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
