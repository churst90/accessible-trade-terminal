namespace AccessibleTrader.Core.Services.Settings
{
    /// <summary>
    /// One user-facing setting as the Settings search box and the Go bar know it: a spoken
    /// label, the tab that holds it, the control to focus and the extra words that should find
    /// it. The label and control id are checked against the dialog's markup by
    /// <c>SettingsSearchRegistryTests</c>; the markers that say whether a host renders a row
    /// live in <see cref="IsAvailable"/>.
    /// </summary>
    public sealed record SettingEntry(string Label, string Tab, string ControlId, string Keywords);

    /// <summary>
    /// The flags <see cref="SettingsRegistry.IsAvailable"/> reads. They are the same
    /// conditions the Settings dialog's markup uses, collected once so Core can answer for
    /// the Go bar without a reference to the Razor component.
    /// </summary>
    public sealed record SettingsHost(
        bool AllowLiveTrading,
        bool AllowBackgroundMonitoring,
        bool IsBrowserHost,
        bool IsWindows,
        bool IsDemo,
        bool IsHosted,
        bool IsBrowserTtsBackend)
    {
        public static SettingsHost From(
            DemoPolicy? demo, IRuntimePlatform? platform, IBrowserSpeechOutput? speechOutput) =>
            new(
                AllowLiveTrading: demo?.AllowLiveTrading ?? false,
                AllowBackgroundMonitoring: demo?.AllowBackgroundMonitoring ?? false,
                IsBrowserHost: platform?.IsBrowserHost ?? false,
                IsWindows: platform?.IsWindows ?? false,
                IsDemo: demo?.IsDemo ?? false,
                IsHosted: demo?.IsHosted ?? false,
                IsBrowserTtsBackend: speechOutput?.IsBrowserTtsBackend ?? false);
    }

    /// <summary>
    /// Every setting the Settings dialog offers, in the dialog's own order. Moved out of
    /// <c>SettingsModal.razor</c> in stage 6c so the Go bar and the dialog answer from one list;
    /// the rows and their text are unchanged.
    /// </summary>
    public static class SettingsRegistry
    {
        // One registry row per user-facing setting: label + owning tab + control id
        // (focused after the jump) + extra match keywords.
        public static IReadOnlyList<SettingEntry> All { get; } = new List<SettingEntry>
        {
            new("Paper trading", "General", "s-paper-trading", "simulated practice demo"),
            new("Quick trade sizing", "General", "s-quick-sizing", "position size risk percent notional allocation stop"),
            new("Reset paper account", "General", "s-paper-reset", "balance restart"),
            new("Reset all settings to defaults", "General", "s-factory-reset", "factory reset defaults erase wipe personalization start over clean"),
            new("Resume last session at startup", "General", "s-resume-session", "autosave restore workspace"),
            new("Keep watching other tabs", "General", "s-bg-monitor", "background monitoring alerts strategies"),
            new("Check every", "General", "s-bg-poll", "background poll seconds interval refresh"),
            new("Live-stream other tabs", "General", "s-bg-live-tabs", "background streaming tick fresh"),
            new("Keep monitoring when browser closed", "General", "s-bg-local", "background headless orca notify alert service"),
            new("Minimize to tray on exit", "General", "s-minimize-to-tray", "window close tray notification area quit background keep running"),
            new("Braille display", "Braille", "s-braille-enabled", "dot pad tactile"),
            new("Touch toolbar", "General", "s-touch-nav", "touchscreen buttons navigation bar"),
            new("Describe candle patterns", "General", "s-candle-patterns", "engulfing harami doji hammer bar close forming narration speech"),
            new("Describe chart patterns", "General", "s-chart-patterns", "formation double top triangle head shoulders flag narration new bar playback speech"),
            new("Magnet snap", "General", "s-magnet-snap", "drawing anchors snap open high low close"),
            new("Snap swing points to the wick", "General", "s-swing-snap", "drawing fibonacci pattern anchor low high wick swing snap"),
            new("Panning step", "General", "s-granularity", "step scroll viewport granularity"),
            new("Chart speech and narration", "Speech", "s-speech-enabled", "voice talk announce tts mute f2 narration"),
            new("Speak timestamps", "Speech", "s-speak-timestamps", "time date spoken"),
            new("Speak the time", "Speech", "s-timestamp-where", "timestamp when moving through time series"),
            new("Speak the date on every bar", "Speech", "s-date-every-bar", "date day boundary intraday timestamp"),
            new("Times of day are read in", "Speech", "s-time-zone", "time zone utc local gmt clock candle timestamp daylight saving"),
            new("12-hour clock with AM and PM", "Speech", "s-twelve-hour", "12 hour 24 hour am pm clock time format"),
            new("Speak other signals while arrowing", "Speech", "s-speak-other-signals", "other markers signals same bar indicators focused component only cross series"),
            new("Say what lines and signals look like", "Speech", "s-describe-signal-looks", "describe colour color shape size dot triangle marker line appearance sighted"),
            new("Speak values as", "Speech", "s-speech-order", "name value order header"),
            new("Read column headers", "Speech", "s-read-headers", "table grid"),

            new("Speech output on this device", "Speech", "s-speech-output", "double speech screen reader browser voice orca tts"),
            new("Event mute includes order outcomes", "Speech", "s-mute-includes-orders", "silence mute stop take profit shift fills"),
            new("Announce new bars", "Narration", "s-announce-bars", "live candle close narration"),
            new("Narrate signals on bar close", "Narration", "s-narrate-signals", "narration indicator signal auto master switch"),
            new("Narrate candle patterns as they form", "Narration", "s-narrate-forming-candles", "narration candle pattern forming live intra-bar hammer engulfing doji"),
            new("Narrate chart formations as they form", "Narration", "s-narrate-forming-charts", "narration chart pattern formation forming live double top head shoulders triangle flag"),
            new("Narrate during playback", "Narration", "s-narrate-playback", "narration playback signal speech space master"),
            new("Speak time landmarks during playback", "Narration", "s-playback-landmarks", "narration playback landmark date hour time calendar boundary"),
            new("Market events on the chart", "Narration", "s-market-events-level", "market events cpi jobs report fed decision options expiry holiday bitcoin halving economy off important medium all"),
            new("Economy events", "Narration", "s-market-events-economy", "market events economy cpi jobs report nfp ppi gdp release"),
            new("Fed events", "Narration", "s-market-events-fed", "market events fed fomc decision"),
            new("Expiry events", "Narration", "s-market-events-expiries", "market events options expiry quarterly crypto"),
            new("Holiday events", "Narration", "s-market-events-holidays", "market events holidays market closed thanksgiving christmas"),
            new("Bitcoin halving events", "Narration", "s-market-events-halvings", "market events bitcoin halving crypto"),
            new("Company earnings events", "Narration", "s-market-events-earnings", "market events earnings eps revenue estimates beat missed report quarter finnhub sec edgar before the open after the close stock company"),
            new("Sound on event bars", "Narration", "s-market-events-sound", "market events bell earcon landing sound event bars"),
            new("Countdowns before events", "Narration", "s-market-events-countdowns", "market events countdown warning minutes before cpi jobs fed decision 15 5 1 30 off"),
            new("Headlines level", "Narration", "s-headlines-level", "headlines news sec edgar filings 8-k earnings results insider form 4 10-q 10-k company off important medium all"),
            new("Include news wire stories", "Narration", "s-headlines-news-wire", "headlines news wire benzinga fiscal.ai alpaca finnhub yahoo cnbc cointelegraph coindesk stories press release analyst upgrade downgrade crypto coin"),
            new("Also read Alpaca's news wire", "Narration", "s-headlines-alpaca", "headlines alpaca fiscal.ai news wire second source half hour batches"),
            new("Pulse: crypto change measured from", "Narration", "s-market-pulse-crypto-start", "market pulse alt+p crypto bitcoin ethereum midnight utc last 24 hours rolling day change"),
            new("Offer a catch-up when I come back", "Narration", "s-change-log-offer", "what changed alt+u catch up away return prompt change log money alerts events connection"),
            new("Sonification enabled", "Sonification", "s-sonif-enabled", "tones audio sound music"),
            new("Sound under the mouse", "Sonification", "s-hover-sonif", "hover pointer sonification"),
            new("Tick other signals while arrowing", "Sonification", "s-tick-other-signals", "cluster ticks markers dots crosses arrow navigation"),
            new("Sound theme", "Sonification", "s-sound-theme", "instrument voices patch orchestra"),
            new("Market earcons", "Sonification", "s-earcons-chart", "earcon alert new bar setup bell signal indicator tone beep chart"),
            new("Interface earcons", "Sonification", "s-earcons-interface", "earcon boundary edge beep success retry info connection modal toggle terminal application tone"),
            new("Theme", "Appearance", "s-theme", "dark light contrast solarized braille colors background gradient new clone edit"),
            new("Draw chart formations", "Appearance", "s-pattern-visuals", "pattern shape double top triangle target trigger visual low vision"),
            new("Visual earcons", "Appearance", "s-visual-earcons", "deaf hearing badge flash cue notification"),
            new("Color-vision-safe chart colors", "Appearance", "s-colorvision", "colorblind deuteranopia protanopia red green blue orange"),
            new("Hollow up-candles", "Appearance", "s-hollow-candles", "colorblind outline shape candle"),
            new("Toolbar captions", "Appearance", "s-toolbar-captions", "label text icon button caption chart height room space"),
            new("Text size", "Appearance", "s-ui-scale", "font zoom scale magnify low vision bigger"),
            new("Status line background", "Appearance", "s-status-backing", "legend status line indicator values transparent opacity backing"),
            new("Trade status bar", "Appearance", "s-trade-status-bar", "position open trades profit loss pnl green red bottom strip glance"),
            new("Each trade shows", "Appearance", "s-trade-status-figure", "position percent amount profit loss pnl trade status"),
            new("Keyboard shortcuts", "Keyboard", "tab-keyboard", "rebind keys hotkey remap reference"),
            new("License", "License", "tab-license", "tier supporter key gpl donate donation support cash app paypal"),
        };

        /// <summary>
        /// Whether this host renders the control a registry row points at. Seven rows sit under
        /// an <c>@if</c> — paper trading is desktop-only, background monitoring and the braille
        /// display are gated per host, the speech-output picker exists only on a browser-TTS
        /// head — and a result for a control that is not there sends the user to a tab and drops
        /// focus on the body. The conditions are the same expressions the dialog's markup uses;
        /// <c>SettingsSearchRegistryTests</c> cannot see an <c>@if</c>, so
        /// <c>SettingsModal_SearchResults_NameOnlyControlsThatRender</c> checks this in the DOM.
        /// </summary>
        public static bool IsAvailable(string controlId, SettingsHost host) => controlId switch
        {
            "s-paper-trading"  => host.AllowLiveTrading,
            "s-bg-monitor" or "s-bg-poll" or "s-bg-live-tabs" => host.AllowBackgroundMonitoring,
            "s-bg-local"       => host.AllowBackgroundMonitoring && host.IsBrowserHost && !host.IsDemo && !host.IsHosted,
            "s-minimize-to-tray" => !host.IsBrowserHost && host.IsWindows,
            "s-braille-enabled" => !host.IsBrowserHost,
            "s-speech-output"  => host.IsBrowserTtsBackend,
            _ => true,
        };

        public static SettingEntry? Find(string? controlId) =>
            string.IsNullOrEmpty(controlId)
                ? null
                : All.FirstOrDefault(s => string.Equals(s.ControlId, controlId, StringComparison.Ordinal));
    }
}
