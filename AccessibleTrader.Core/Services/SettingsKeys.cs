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

        // ── The two earcon families ──────────────────────────────────────────
        // Shift+F3 (WorkspaceState.IsEarconsEnabled) is the master mute over both.
        // These two decide which family it is muting when it is OFF, and both
        // default TRUE so an untouched install sounds exactly as it did before.
        //
        // CHART earcons are about the market: an alert firing, a bar opening, a
        // strategy setup arming. INTERFACE earcons are about the terminal: the
        // edge of the data, a mode toggled, an action that succeeded. They are
        // separable because they answer to different appetites — a user who wants
        // every setup bell may not want a beep for each end-of-chart, and the one
        // that fires most often is not the one carrying the most information.
        public const string ChartEarconsEnabled     = "accessibility.earcons.chart";
        public const string InterfaceEarconsEnabled = "accessibility.earcons.interface";

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
        public const string SpeakDateOnEveryBar  = "speech.dateOnEveryBar";
        public const string TimestampReadLocation = "speech.timestampReadLocation";
        public const string ReadColumnHeaders     = "speech.readColumnHeaders";
        public const string SpeechOrder           = "speech.speechOrder";
        public const string AnnounceNewBars       = "speech.announceNewBars";

        // Chart-pattern description. OFF by default: it adds speech to a navigation action the user
        // already knows the shape of, so it has to be asked for rather than imposed — the same
        // opt-in rule the visual-accessibility additions follow.
        public const string DescribeChartPatterns = "speech.describeChartPatterns";
        public const string DescribeCandlePatterns = "speech.describeCandlePatterns";

        // ── Narration (Settings → Narration): what the terminal says when the user
        // pressed NOTHING. The Speech tab governs how it says what you ASKED for; these
        // two govern the unprompted channel. The split is by TRIGGER, not by topic —
        // that is what keeps "Describe chart patterns" in Speech (it also changes what
        // the arrow keys say) while "Announce new bars" belongs here.

        /// <summary>
        /// Master switch over the bar-close narrator (<c>AutoNarrationService</c>). Default ON,
        /// because it changes nothing on its own: the narrator only ever scans series the user
        /// flagged with N, so on a chart with none flagged this switch has nothing
        /// to gate. N picks WHAT speaks; this says WHETHER any of it does.
        /// </summary>
        public const string NarrateSignalsOnBarClose = "narration.signalsOnBarClose";

        // ── ABILITY vs WHEN IT SPEAKS (Cody, 2026-09-11) ─────────────────────
        //
        // "Describe candle patterns" and "Describe chart patterns" live on the Speech tab and
        // are the ABILITY: they decide whether a pattern is ever named — on the arrow keys, in
        // the bar-close suffix, in the detail summary. That was also, accidentally, the only
        // switch over the LIVE intra-bar commentary, so a user who wanted pattern names while
        // arrowing over the chart had no way to refuse a running commentary on the forming bar.
        //
        // These two are the WHEN: they govern only the unprompted, intra-bar half, and they
        // require the ability above. "I may want to hear the patterns as I arrow over them but
        // maybe not during narration" — which is exactly the split the Narration tab exists for.

        /// <summary>
        /// Speak the candle pattern on the bar that is still FORMING, as it changes.
        /// <b>Default ON</b> — it is what shipped, and what is new here is the ability to turn it
        /// off without also losing pattern names on the arrow keys.
        /// </summary>
        public const string NarrateFormingCandlePatterns = "narration.formingCandlePatterns";

        /// <summary>
        /// Speak a chart FORMATION — double top, head and shoulders, triangle, flag — while it is
        /// still forming on the live bar, with the level that would confirm it.
        /// <b>Default OFF.</b> It is a new occasion for speech rather than a clause on one the
        /// user already opted into, and the standing rule here is that continuous speech is
        /// asked for rather than imposed. Same call as DescribeChartPatterns' own default.
        /// </summary>
        public const string NarrateFormingChartPatterns = "narration.formingChartPatterns";

        /// <summary>
        /// Whether playback speaks at all beyond its own start/pause/stop/speed confirmations —
        /// time landmarks, marker signals, and chart-pattern outcomes. Default ON for the same
        /// reason: ON preserves exactly what shipped (landmarks), and everything it ADDS is
        /// already behind an opt-in (the per-series narration flag, and "Describe chart patterns"
        /// for the outcomes). OFF is the thing nobody had before — playback as pure tones.
        /// </summary>
        public const string NarrateDuringPlayback = "narration.duringPlayback";
        public const string SpeakPlaybackLandmarks = "narration.playbackLandmarks";

        /// <summary>The playback speed Shift+= and Shift+- set. A preference, so it survives a restart.</summary>
        public const string PlaybackSpeed = "playback.speed";

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

        // ── Notifications for what you CANNOT SEE (the OS toast) ─────────────
        //
        // ONE switch, and it defaults TRUE. Cody, 2026-09-11.
        //
        // The rule it implements: the channel is decided by the event's SUBJECT. Whatever is
        // happening on the chart in front of you is spoken in the browser's live region and
        // never toasted — you are already there. Everything else — a bar closing on another
        // open tab, an alert or a fill on a symbol with no tab open, and every terminal event
        // while the browser is closed — arrives as a system notification, because that is the
        // only way it can reach you.
        //
        // It replaces three switches (notifications.desktop.alerts / .newBars / .orderFills)
        // which all defaulted FALSE and lived in a different dialog from the thing they gated.
        // That arrangement produced the thirty-ninth pass's incident — a feature reported as
        // broken that was merely switched off — and for a blind user an accidental silence has
        // no compensating channel. One switch, on by default, and the timeframe floor below as
        // the volume control.
        // The default (ON) lives on NotificationPolicy, not here: two guards in AppSettingsTests
        // read every public literal of this class AS A STRING, so a bool constant among them is
        // an InvalidCastException. This class is string keys and nothing else.
        public const string NotifyUnseenEvents = "notifications.unseen";

        // Retired 2026-09-11. Read ONLY by NotificationPolicy, and only when the new key is
        // absent, so a user who had deliberately turned all three OFF is not switched back on
        // by the new default. Nothing writes them.
        internal const string LegacyDesktopNotifyAlerts     = "notifications.desktop.alerts";
        internal const string LegacyDesktopNotifyNewBars    = "notifications.desktop.newBars";
        internal const string LegacyDesktopNotifyOrderFills = "notifications.desktop.orderFills";

        // ── Bars closing on a LIVE BACKGROUND TAB (a chart you have open, not the one you are
        // looking at). The notification rides NotifyUnseenEvents above; the earcon plays
        // UNCONDITIONALLY, as an ambient "something happened elsewhere" — this comment used to
        // say it rode the switches above, and it never has. SPEECH is its own opt-in and defaults
        // FALSE, because a bar close on a chart you are not looking at interrupting the chart
        // you ARE is how a feature gets switched off for good. Cody, 2026-09-08.
        public const string SpeakBackgroundTabBars = "notifications.backgroundTabBars.speak";

        /// <summary>
        /// The shortest timeframe whose bar closes are announced with the BROWSER CLOSED — and,
        /// since 2026-09-11, whose NARRATION LADDER is spoken there too (Cody: the floor means
        /// "do not talk to me about charts faster than this", which is what the settings hint
        /// had always promised while the ladder ignored it and recited every minute).
        ///
        /// <para>Default "1m" — i.e. every timeframe announces. NEW BARS AND THE LADDER ONLY:
        /// it must never gate an alert or a trade event, which are per-occurrence.</para>
        /// </summary>
        public const string HeadlessNewBarMinTimeframe = "notifications.newBars.minTimeframe";

        // ── The browser-closed master switch ─────────────────────────────────
        //
        // "Keep monitoring when the browser is closed" (Settings → General, and the tray).
        // It had NO constant until 2026-09-11: it was a const on LocalBackgroundMonitor in the
        // WebHost project and a raw string literal twice in SettingsModal.razor, so the master
        // switch of the whole browser-closed half was typo-exposed across a project boundary.
        public const string BackgroundLocalMonitoring = "monitoring.backgroundLocal";

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
