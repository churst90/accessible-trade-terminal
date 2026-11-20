using System.Collections.Immutable;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Models
{
    public enum InteractionContext { Series, Component, Bin }
    public enum PlaybackScope { Chart, Series, Component }
    public enum InitializationStatus { Booting, Loading, Resetting, Ready, Error }

    /// <summary>
    /// How the price bars are DRAWN and READ on the price pane: the four chart types of
    /// TradingView's model. <see cref="Candles"/> is 0 so every workspace saved before this
    /// field existed loads as the candles it was showing.
    ///
    /// <para>
    /// Heikin-Ashi stays the separate <see cref="WorkspaceState.IsHeikinAshi"/> flag: it is a
    /// transform of the candles, not a fifth member of this enum, and seventy files already
    /// read it. To the user the two are one choice — Candles, Heikin Ashi, Line or Area — and
    /// the invariants are held by the reducer: Line/Area clear the flag, and Heikin-Ashi sets
    /// this back to <see cref="Candles"/>.
    /// </para>
    /// </summary>
    /// <remarks>Members are saved as numbers: new ones go on the end.</remarks>
    public enum PriceChartStyle { Candles = 0, Line = 1, Area = 2, Renko = 3, RangeBars = 4, LineBreak = 5, Kagi = 6,
        PointAndFigure = 7, Bars = 8, HollowCandles = 9, Baseline = 10, HighLow = 11, HlcArea = 12, VolumeCandles = 13 }

    /// <summary>
    /// True for the chart types drawn from BOUNDARIES of price movement rather than from the
    /// clock: Renko, range bars, line break, Kagi and point and figure. Those charts drop time from the index axis, so every index-based consumer runs on
    /// the drawn bars, while every money and feed path keeps the real time bars
    /// (<see cref="WorkspaceState.MarketBars"/>). "Brick" in the plumbing's names means any of
    /// them; <see cref="IsBlockStyle"/> is the narrower "drawn as blocks with no wicks".
    /// </summary>
    public static class PriceChartStyleExtensions
    {
        public static bool IsBrickStyle(this PriceChartStyle style) =>
            style is PriceChartStyle.Renko or PriceChartStyle.RangeBars or PriceChartStyle.LineBreak
                or PriceChartStyle.Kagi or PriceChartStyle.PointAndFigure;

        /// <summary>
        /// Renko, line break, Kagi and point and figure: each drawn bar runs between two prices (a brick, a block, a
        /// Kagi line, a column of X or O), with no wicks, and the price series has one part. Range bars are not: each
        /// is a real candle (open, high, low, close) and keeps the candle parts.
        /// </summary>
        public static bool IsBlockStyle(this PriceChartStyle style) =>
            style is PriceChartStyle.Renko or PriceChartStyle.LineBreak or PriceChartStyle.Kagi
                or PriceChartStyle.PointAndFigure;
    }

    /// <summary>How a Renko chart picks its brick size. TradingView's default is ATR 14.
    /// <see cref="Percent"/> is Kagi's (TradingView's "Percentage" reversal: the size is a
    /// percentage of the line's end, not a price) and point and figure's (TradingView's
    /// "Percentage (LTP)": the box is that percentage of the last price when it is chosen). Saved in files, so only ever appended to.</summary>
    public enum RenkoBoxMode { Atr = 0, Fixed = 1, Percent = 2 }

    /// <summary>
    /// Per-tab settings of the charts built off the clock. The first three are Renko's box:
    /// <see cref="BoxSize"/> is used only in <see cref="RenkoBoxMode.Fixed"/> mode. The range
    /// bars' size is chosen the same way and kept apart (<see cref="RangeMode"/>,
    /// <see cref="RangeSize"/>, <see cref="RangeAtrLength"/>), so choosing one never moves the
    /// other. <see cref="LineBreakLines"/> is how many lines a line-break reversal must break
    /// (TradingView's default, three). Kagi's reversal amount is chosen like the range and kept
    /// apart too (<see cref="KagiMode"/>, <see cref="KagiSize"/>, <see cref="KagiAtrLength"/>), and
    /// so is point and figure's box (<see cref="PnfMode"/>, <see cref="PnfSize"/>,
    /// <see cref="PnfAtrLength"/>), with <see cref="PnfReversal"/> the boxes a turn takes
    /// (TradingView's default, three), and <see cref="PnfHighLow"/> TradingView's "High/Low"
    /// source in place of the close. <see cref="RenkoBoxMode.Percent"/> on <see cref="PnfMode"/>
    /// is TradingView's "Percentage (LTP)" box: <see cref="PnfSize"/> percent of the last price.
    /// <see cref="BaselinePrice"/> is the Baseline chart's base: a price, or 0 for the first
    /// visible close (it is a time chart, but its one setting rides here with the others).
    /// One record so every save, tab snapshot and background
    /// monitor that already carries Renko's settings carries all of them; a file saved before a
    /// field existed reads its default.
    /// </summary>
    public sealed record RenkoSettings(
        RenkoBoxMode Mode = RenkoBoxMode.Atr,
        double BoxSize = 0,
        int AtrLength = 14,
        RenkoBoxMode RangeMode = RenkoBoxMode.Atr,
        double RangeSize = 0,
        int RangeAtrLength = 14,
        int LineBreakLines = 3,
        RenkoBoxMode KagiMode = RenkoBoxMode.Atr,
        double KagiSize = 0,
        int KagiAtrLength = 14,
        RenkoBoxMode PnfMode = RenkoBoxMode.Atr,
        double PnfSize = 0,
        int PnfAtrLength = 14,
        int PnfReversal = 3,
        bool PnfHighLow = false,
        double BaselinePrice = 0);

    /// <summary>
    /// The real time window a brick formed in. <see cref="FirstBar"/> and
    /// <see cref="LastBar"/> are indexes into <see cref="WorkspaceState.TimeBars"/>; several
    /// bricks may share a completion bar, and therefore a <see cref="End"/>.
    /// </summary>
    public readonly record struct BrickSpan(
        DateTime Start,
        DateTime End,
        int FirstBar,
        int LastBar,
        bool Up);

    /// <summary>
    /// What the price pane's Y axis is MEASURED in: raw prices (Regular), percentage change from
    /// the first visible close (Percent), or the first visible close set to 100
    /// (<see cref="IndexedTo100"/>). TradingView's price-scale menu has four entries and so has
    /// this: Regular, Logarithmic, Percent, Indexed to 100 — but Logarithmic is still the
    /// existing <see cref="WorkspaceState.IsLogScale"/> flag, which thirty files read, rather
    /// than a fourth member here.
    ///
    /// <para>
    /// To the user it is ONE choice, so the reducer holds the pairing: Percent and Indexed clear
    /// the log flag, turning log on returns the mode to Regular, and
    /// <see cref="SetPriceScaleAction"/> writes both. <see cref="Regular"/> is 0 so every
    /// workspace saved before this field existed opens on the axis it was showing.
    /// </para>
    /// </summary>
    public enum PriceScaleMode { Regular = 0, Percent = 1, IndexedTo100 = 2 }

    /// <summary>
    /// What the chart's data is doing, as a value anyone can ASK for.
    ///
    /// <para><see cref="Stale"/> was added 2026-08-27 with the feed-honesty fix. Three
    /// watchdogs each spoke ONCE into a transient channel — <c>LiveStreamManager</c> announced
    /// connected-but-quiet once per subscription, <c>MarketFeedHub</c> announced
    /// background-feed quiet/restart/give-up, <c>DataOrchestrator</c>'s breaker announced a
    /// trip and a reset — and after that there was <b>no queryable state at all</b>. A user who
    /// missed the spoken line (a screen reader interrupted mid-sentence, an announcement fired
    /// while they were in a modal) had <b>no way to ask</b> whether the chart in front of them
    /// was live. <c>DataState</c> on the orchestrator has <c>Stalled</c> and
    /// <c>NetworkLagged</c>, but they are unreachable and its <c>StateChanged</c> has no
    /// consumers outside the class; <c>ConnectionManager</c> is dead.</para>
    /// </summary>
    public enum DataStatus
    {
        Idle,
        LoadingHistorical,
        Filling,
        Ready,
        Error,
        /// <summary>Connected, but nothing has arrived for long enough that the chart may no
        /// longer reflect the market. Distinct from <see cref="Error"/>: nothing failed.</summary>
        Stale,
    }

    /// <summary>
    /// Frozen snapshot of all per-chart state fields for an inactive tab.
    /// Created when a tab is deactivated; restored when the tab is re-activated.
    /// Global settings (volume, speech, playback speed, etc.) are NOT snapshotted —
    /// they remain on WorkspaceState directly and are shared across all tabs.
    /// </summary>
    public record TabSnapshot(
        int TabIndex,
        ChartIdentity Identity,
        TimeSeriesBuffer<Ohlcv> Data,
        ImmutableList<ChartSeries> ActiveSeries,
        int FocusedSeriesIndex,
        string? FocusedSeriesId,
        int FocusedComponentIndex,
        int FocusedBinIndex,
        int CurrentDataIndex,
        int ViewportStartIndex,
        int ViewportLength,
        int RightMarginBars,
        (double Min, double Max) ViewportRange,
        ImmutableDictionary<string, (double Min, double Max)> PaneRanges,
        bool IsHeikinAshi,
        bool IsLogScale,
        InteractionContext LastInteractionContext,
        ImmutableDictionary<string, float>? PaneHeightRatios,
        InitializationStatus InitStatus,
        DataStatus DataStatus,
        bool IsCoordinateEntryMode,
        DrawingType? PendingDrawingTool,
        int CoordinateEntryAnchorCount,
        int CoordinateEntryAnchor1Index,
        // ── Primary series indirection ────────────────────────────────────────
        // The series id that keyboard nav, speech, sonification, and rendering
        // treat as "the main data" of this chart. Set by WorkspaceInitializer
        // based on provider shape (OHLCV → candles; SingleValueLine → price).
        // Decouples consumers from hardcoding CoreSeriesIds.Candles.
        string PrimarySeriesId = "candles",
        // ── Current provider data shape (per-tab) ────────────────────────────
        // Mirrors the active provider's ProviderDataShape so the reconciler can
        // detect shape changes (OHLCV ↔ SingleValueLine) and UI code can gate
        // trading-specific controls. Default matches pre-refactor behavior.
        ProviderDataShape CurrentDataShape = ProviderDataShape.Ohlcv,
        // ── Human-readable label for the active symbol ───────────────────────
        // Resolved via IMarketDataProvider.GetSymbolDisplayName at load time.
        // Used to label the Price series on analytics tabs so speech/UI reads
        // "Fear and Greed Index" instead of generic "Price".
        string SymbolDisplayName = "",
        // ── How this tab opens when it was restored from a saved workspace ────
        // Null for a tab the user opened. See RestoredTabLoad.
        RestoredTabLoad? RestoredLoad = null
    )
    {
        /// <summary>The tab's price chart type. Candles for every snapshot made before the
        /// field existed and for a newly opened tab.</summary>
        public PriceChartStyle ChartStyle { get; init; } = PriceChartStyle.Candles;

        /// <summary>The tab's price-scale mode. Regular for every snapshot made before the
        /// field existed and for a newly opened tab.</summary>
        public PriceScaleMode ScaleMode { get; init; } = PriceScaleMode.Regular;

        /// <summary>The tab's settings for Renko, range bars and line break. Defaults for every
        /// snapshot made before the field existed and for a newly opened tab.</summary>
        public RenkoSettings Renko { get; init; } = new();

        /// <summary>The size a built-off-the-clock tab froze when its type was chosen (Renko's
        /// box, the range, or line break's line count), so switching tabs rebuilds the same bars
        /// without recomputing the ATR. Zero when the tab never resolved one.</summary>
        public double BrickBoxSize { get; init; }
    }

    /// <summary>
    /// How a tab restored from a saved workspace should open, carried until its data first
    /// loads. A restored tab that is never visited keeps it, so saving the workspace again
    /// still knows how much history the tab is meant to have.
    ///
    /// <para>
    /// <see cref="HistoryFrom"/> is the date its history should reach back to: the first bar
    /// the tab had when it was saved, or an older drawing anchor. Null loads the usual 200
    /// bars. Whatever it holds, a restored tab keeps its saved zoom width rather than the
    /// fresh-chart default.
    /// </para>
    /// <para>
    /// It carries the <see cref="Identity"/> it was restored with and applies only to that
    /// chart, so a Load of a different symbol before the restored data arrives gets a fresh
    /// chart rather than the old tab's depth.
    /// </para>
    /// </summary>
    public sealed record RestoredTabLoad(ChartIdentity Identity, DateTime? HistoryFrom);

    public record WorkspaceState(
        ChartIdentity Identity,
        TimeSeriesBuffer<Ohlcv> Data,
        ImmutableList<ChartSeries> ActiveSeries,
        int FocusedSeriesIndex,
        string? FocusedSeriesId,
        int FocusedComponentIndex,
        int FocusedBinIndex,
        int CurrentDataIndex,
        int ViewportStartIndex,
        int ViewportLength,
        (double Min, double Max) ViewportRange,
        ImmutableDictionary<string, (double Min, double Max)> PaneRanges,
        float ChartVolume,
        float PlaybackSpeed,
        // Panning step as a whole percentage of the viewport (5, 10, 15 … 100).
        // Example: 10 means each [ or ] press pans by 10% of the visible bars.
        // Shift+[ decreases this step; Shift+] increases it in 5% increments.
        int PanningGranularity,
        InteractionContext LastInteractionContext,
        bool IsHeikinAshi,
        bool IsLogScale,
        string BackgroundColor,
        bool SpeakTimestamps,
        string TimestampReadLocation,
        bool ReadColumnHeaders,
        string SpeechOrder,
        bool AnnounceNewBars,
        // Speak classical chart formations (double top, head and shoulders, triangles,
        // flags) when navigation lands inside one, both while they are still FORMING — with
        // the level that would confirm them — and once completed.
        //
        // Default FALSE. It adds narration to an action the user performs constantly, and
        // the standing convention here is that continuous speech is opted into rather than
        // imposed. Mirrors AppSettings.DescribeChartPatterns.
        bool DescribeChartPatterns,
        // Speak the CANDLE pattern of a bar that has just closed, and the one forming on the
        // live bar — engulfing, harami, doji, hammer and the rest. Distinct from the setting
        // above, which is about multi-bar chart FORMATIONS; these two were one undifferentiated
        // "patterns" idea until Cody separated them on 2026-09-04, and only one of them had a
        // switch. Mirrors AppSettings.DescribeCandlePatterns.
        //
        // Default TRUE, unlike DescribeChartPatterns, by the same rule the narration switches
        // follow: ON is what shipped, and what is new here is the ability to turn it OFF. The
        // clause it controls rides on an announcement the user already opted into
        // (AnnounceNewBars) rather than adding a new occasion for speech.
        bool DescribeCandlePatterns,
        // Number of empty future-space slots reserved on the right of the viewport.
        // The last real bar always lands at slot (ViewportLength - RightMarginBars - 1).
        // Allows trendlines and drawings to project into future space. Default: 10 bars
        // (was 20 before 2026-04-24; reduced after a screenshot review showed the 20-bar
        // margin was ~10% of viewport width on typical monitors, leaving most of the right
        // third of the chart blank).
        int RightMarginBars = 10,
        bool IsSpeechEnabled = true,
        bool IsSonificationEnabled = true,
        // Shift-tier mutes (session-only, like F2/F3 — never persisted so the
        // terminal can never START silent): Shift+F2 = ambient/event speech,
        // Shift+F3 = earcons. Unshifted keys own the interactive channel.
        bool IsEventSpeechEnabled = true,
        bool IsEarconsEnabled = true,
        TerminalMode Mode = TerminalMode.Trading,
        MarketType SelectedMarketType = MarketType.Crypto,
        bool IsPlaying = false,
        bool IsPaused = false,
        PlaybackScope PlaybackScope = PlaybackScope.Chart,
        string ReadXAxisHeaders = "Along X",
        int WasapiLatency = 100,
        InitializationStatus InitStatus = InitializationStatus.Booting,
        DataStatus DataStatus = DataStatus.Idle,
        /// <summary>
        /// When the last live tick arrived, or null when none has. The queryable half of
        /// <see cref="Models.DataStatus.Stale"/>: "is this live" needs an answer a
        /// speak-on-demand key can read, not only a status word — "no data for eleven
        /// minutes" is actionable in a way that "stale" is not.
        /// </summary>
        DateTime? LastTickUtc = null,
        // Per-pane height as a fraction of totalPaneHeight (canvas height minus x-axis).
        // Key = pane name (e.g. "Pane_RSI"). Absent key = use auto 30%-split layout.
        // Ratios are clamped to [0.05, 0.75] during dispatch.
        ImmutableDictionary<string, float>? PaneHeightRatios = null,
        // ── Coordinate Entry mode ──────────────────────────────────────────────
        // True when a drawing shortcut has been pressed and the user is navigating to place anchors.
        // Arrow keys navigate normally; Enter sets each anchor; Escape cancels.
        bool IsCoordinateEntryMode = false,
        // The drawing type being placed during Coordinate Entry mode.
        DrawingType? PendingDrawingTool = null,
        // Number of anchors confirmed so far (0 = no anchor set, 1 = first anchor set, waiting for second).
        int CoordinateEntryAnchorCount = 0,
        // Data index of the first anchor point (-1 when not yet set).
        int CoordinateEntryAnchor1Index = -1,
        // ── Multi-tab support ──────────────────────────────────────────────────
        // Frozen snapshots of inactive tabs. The active tab's state lives directly
        // in WorkspaceState fields; inactive tabs are stored here until re-activated.
        ImmutableList<TabSnapshot>? TabSnapshots = null,
        // Zero-based index of the currently active tab.
        int ActiveTabIndex = 0,
        // True when this state is being passed through the offline backtester replay loop.
        // Strategies that publish setup/audio events to the live IEventBus check this flag
        // and skip publication during backtest — otherwise replaying 3,000+ bars floods
        // SetupSonifier with bell/speech events meant for live trading.
        bool IsBacktesting = false,
        // The series id that keyboard nav, speech, sonification, and rendering treat
        // as "the main data" of this chart. Set by WorkspaceInitializer based on the
        // provider's data shape (OHLCV → candles; SingleValueLine → price). Consumers
        // should prefer this over hardcoding CoreSeriesIds.Candles.
        string PrimarySeriesId = "candles",
        // Mirrors the active provider's declared ProviderDataShape on
        // the current tab. Used by the reconciler in WorkspaceInitializer to
        // detect shape changes (which cause stripping of all non-core series) and by
        // the UI layer to hide trading-only controls on analytics tabs. Defaults to
        // ProviderDataShape.Ohlcv for backwards compatibility.
        ProviderDataShape CurrentDataShape = ProviderDataShape.Ohlcv,
        // Human-readable label for the currently-loaded symbol, resolved via
        // IMarketDataProvider.GetSymbolDisplayName. Flows into the
        // Price series FriendlyName + component DisplayName on analytics tabs so
        // the user hears "Fear and Greed Index, 47" instead of "Price, 47".
        // Empty string until the first load completes.
        string SymbolDisplayName = "",
        // True while bar replay is revealing history one bar at a time. Live data dispatches
        // are suppressed for the duration — otherwise the next incoming tick would overwrite
        // the replay prefix with the full series and end the exercise without warning.
        // Distinct from IsBacktesting: replay is an interactive user mode with
        // full speech and sonification, not an offline strategy loop.
        bool IsReplaying = false,
        // ── Narration: what the terminal says when the user pressed NOTHING ────
        // The Speech settings above govern how it says what you ASKED for (values order,
        // timestamps, headers). These two govern the unprompted channel, and the split is
        // by TRIGGER rather than by topic — which is why DescribeChartPatterns stays with
        // Speech (it also changes what the arrow keys say) and AnnounceNewBars does not.
        //
        // Both default TRUE, unlike DescribeChartPatterns, and deliberately: ON reproduces
        // exactly what shipped, and everything either of them lets through is ALREADY behind
        // an opt-in — the per-series N flag for signals, DescribeChartPatterns
        // for the pattern outcomes. A default of false would have silenced playback's time
        // landmarks, which have spoken since 2026-09-02, to prevent speech that the existing
        // opt-ins prevent anyway. Mirrors AppSettings.NarrateSignalsOnBarClose /
        // AppSettings.NarrateDuringPlayback.
        //
        // Master switch over the bar-close narrator (AutoNarrationService). N picks WHAT
        // speaks; this says WHETHER any of it does.
        bool NarrateSignalsOnBarClose = true,
        // Whether playback speaks anything beyond its own start / pause / stop / speed
        // confirmations: time landmarks, marker signals, chart-pattern outcomes.
        bool NarrateDuringPlayback = true,
        // The TIME LANDMARK alone — "June 3", "14:00" — spoken as playback crosses a boundary.
        // Split out of NarrateDuringPlayback on Cody's ask, 2026-09-04: the landmark and the
        // signals answer different questions (WHERE IN TIME the tones are, versus WHAT the
        // indicators printed), and someone who wants the signals may not want the calendar read
        // to them every few seconds. Default TRUE — it has spoken since 2026-09-02, and it is
        // subordinate to NarrateDuringPlayback, which remains the master switch.
        bool SpeakPlaybackLandmarks = true,
        /// <summary>Speak the date on every bar, rather than only when the day changes.</summary>
        bool SpeakDateOnEveryBar = false,
        /// <summary>Follow a spoken signal with what its marker looks like ("Large green
        /// dot."). Cody, 2026-10-22. See MarkerLook. Off by default: it lengthens every signal.</summary>
        bool DescribeSignalLooks = false,
        // ── Market events on the chart ─────────────────────────────────────
        // Off by default (Cody, 2026-11-18: "don't want to overwhelm new users"). The level
        // gates the kinds below; the five boxes are all ticked so that turning the level on
        // does not also require a second pass. Mirrors AppSettings.MarketEvents*.
        string MarketEventsLevel = "off",
        bool MarketEventsEconomy = true,
        bool MarketEventsFed = true,
        bool MarketEventsExpiries = true,
        bool MarketEventsHolidays = true,
        bool MarketEventsHalvings = true,
        bool MarketEventsSoundOnEventBars = true,
        // Which countdowns fire before a live event: "off", "1", "5,1", "15,1" or "30,15,1".
        // Default "15,1" matches the design doc's 15- and 1-minute leads. Read by the live
        // notice service; never carried into the script sandbox (see WorkspaceProjection).
        string MarketEventsCountdowns = "15,1",
        // Event ids the user marked "Alert me before" in the calendar. Watched events are
        // spoken even at level Off, outside the kind/importance filters and with no open or
        // held market; pruned by the writer once an event is more than two days past.
        IReadOnlyList<string>? MarketEventsWatched = null,
        // Where a crypto pulse change starts: "midnightUtc" (default) or "last24h". A host
        // preference, read by MarketPulseService; never carried into the script sandbox.
        string MarketPulseCryptoStart = "midnightUtc",
        // Offer the Alt+U catch-up prompt when coming back after five minutes. A host
        // narration preference; never carried into the script sandbox.
        bool ChangeLogOfferOnReturn = true,
        // "Headlines": off (default), importantOnly, importantAndMedium or all. A host narration
        // preference, read by the headline notices; never carried into the script sandbox.
        string HeadlinesLevel = "off",
        // "Headlines": also say news stories, not only filings (default on; nothing happens
        // without a Finnhub or Alpaca key and a level). Host narration preference, never in the
        // script sandbox.
        bool HeadlinesNewsWire = true,
        // "Headlines": read Alpaca's wire as well as Finnhub's (default off since 2026-11-20 —
        // half-hourly batches, stocks only). Host narration preference, never in the sandbox.
        bool HeadlinesAlpaca = false,
        // "Market events": companies' earnings on their own charts (stage 4b; needs a Finnhub
        // key). Host narration preference, never in the script sandbox.
        bool MarketEventsEarnings = true,
        // How the active tab opens if it was restored from a saved workspace and its data
        // has not loaded yet. Per tab: snapshotted with the tab. See RestoredTabLoad.
        RestoredTabLoad? RestoredLoad = null
        // ScalePriceOnly (Alt+F, "fit price only") lived here from 2026-09-21 to 2026-09-23 and
        // was retired at Cody's decision: the price pane fits every VISIBLE Main-pane component,
        // always. A user who wants the price line's full pitch range back hides the band — a
        // hidden component is not in the fit (ViewportRangeCalculator). An overlay allowed off
        // the pane is not silent, it CLAMPS to the pitch floor or ceiling, which sounds like a
        // band sitting still; that is worse than a compressed price line, and a mode that
        // produces it was a trap. Old saved workspaces carrying the field load unchanged — the
        // serialisers ignore an unknown member.
    )
    {
        public static WorkspaceState Initial => new WorkspaceState(
            Identity: ChartIdentity.Empty,
            Data: TimeSeriesBuffer<Ohlcv>.Empty,
            ActiveSeries: ImmutableList<ChartSeries>.Empty,
            FocusedSeriesIndex: 0,
            FocusedSeriesId: null,
            FocusedComponentIndex: 0,
            FocusedBinIndex: -1,
            CurrentDataIndex: -1,
            ViewportStartIndex: 0,
            ViewportLength: 100,
            RightMarginBars: 10,
            ViewportRange: (0, 0),
            PaneRanges: ImmutableDictionary<string, (double Min, double Max)>.Empty,
            ChartVolume: 0.5f,
            PlaybackSpeed: 1.0f,
            PanningGranularity: 10,
            LastInteractionContext: InteractionContext.Series,
            IsHeikinAshi: false,
            IsLogScale: false,
            BackgroundColor: "#000000",
            SpeakTimestamps: true,
            TimestampReadLocation: "Along X Axis",
            ReadColumnHeaders: true,
            SpeechOrder: "HeaderValue",
            AnnounceNewBars: true,
            DescribeChartPatterns: false,
            DescribeCandlePatterns: true,
            NarrateSignalsOnBarClose: true,
            NarrateDuringPlayback: true,
            SpeakPlaybackLandmarks: true,
            SpeakDateOnEveryBar: false,
            DescribeSignalLooks: false,
            MarketEventsLevel: "off",
            MarketEventsEconomy: true,
            MarketEventsFed: true,
            MarketEventsExpiries: true,
            MarketEventsHolidays: true,
            MarketEventsHalvings: true,
            MarketEventsSoundOnEventBars: true,
            MarketEventsCountdowns: "15,1",
            MarketEventsWatched: Array.Empty<string>(),
            MarketPulseCryptoStart: "midnightUtc",
            ChangeLogOfferOnReturn: true,
            HeadlinesLevel: "off",
            HeadlinesNewsWire: true,
            IsSpeechEnabled: true,
            IsSonificationEnabled: true,
            IsEventSpeechEnabled: true,
            IsEarconsEnabled: true,
            Mode: TerminalMode.Trading,
            SelectedMarketType: MarketType.Crypto,
            IsPlaying: false,
            IsPaused: false,
            PlaybackScope: PlaybackScope.Chart,
            ReadXAxisHeaders: "Along X",
            WasapiLatency: 100,
            InitStatus: InitializationStatus.Booting,
            DataStatus: DataStatus.Idle,
            TabSnapshots: ImmutableList<TabSnapshot>.Empty,
            ActiveTabIndex: 0,
            PrimarySeriesId: "candles",
            CurrentDataShape: ProviderDataShape.Ohlcv,
            SymbolDisplayName: ""
        );

        /// <summary>Number of open tabs (active tab + inactive snapshots).</summary>
        public int TabCount => (TabSnapshots?.Count ?? 0) + 1;

        /// <summary>
        /// The chart's series with every indicator computed on Heikin-Ashi candles, beside
        /// <see cref="ActiveSeries"/> computed on the real bars. Set only on a state handed to a
        /// strategy, when a strategy that reads its signals on Heikin-Ashi
        /// (<c>StrategySpec.SignalsOnHeikinAshi</c>) is running; null everywhere else, and never
        /// in the store. Its conditions read these, its stops and targets read
        /// <see cref="ActiveSeries"/>.
        /// </summary>
        public ImmutableList<ChartSeries>? HeikinAshiSeries { get; init; }

        /// <summary>
        /// How the price bars are drawn on this chart: Candles (the default), Line or Area.
        /// An init property rather than a positional parameter because this record is
        /// constructed positionally in many places and every one would have to be edited.
        /// It is per tab, saved with <see cref="IsHeikinAshi"/> wherever that goes.
        /// </summary>
        public PriceChartStyle ChartStyle { get; init; } = PriceChartStyle.Candles;

        /// <summary>
        /// What this price pane's ruler measures: raw prices (Regular), percent from the first
        /// visible close, or that close indexed to 100. An init property for the same reason as
        /// <see cref="ChartStyle"/>. Per tab, saved with <see cref="IsLogScale"/> wherever that
        /// goes; the log flag is the fourth menu entry, held beside it.
        /// </summary>
        public PriceScaleMode ScaleMode { get; init; } = PriceScaleMode.Regular;

        /// <summary>
        /// The real time bars behind the drawn bars. Null on every non-brick chart, and also
        /// while a brick chart has no data yet. On a brick chart <see cref="Data"/> holds the
        /// bricks and this holds the feed's own bars.
        /// </summary>
        public TimeSeriesBuffer<Ohlcv>? TimeBars { get; init; }

        /// <summary>One per brick in <see cref="Data"/>; empty when this is not a brick chart.</summary>
        public ImmutableArray<BrickSpan> BrickSpans { get; init; } = ImmutableArray<BrickSpan>.Empty;

        /// <summary>The box actually in use on a brick chart. The ATR value is FROZEN here when
        /// the chart is chosen, the symbol/timeframe first loads, or the settings change — never
        /// per tick and never on a history prepend.</summary>
        public double BrickBoxSize { get; init; }

        /// <summary>The tab's settings for Renko, range bars and line break; each type reads only
        /// its own fields.</summary>
        public RenkoSettings Renko { get; init; } = new();

        /// <summary>
        /// The REAL time bars, whatever is drawn. Anything that trades, prices an order, saves
        /// a tab or catches up a feed reads this, never <see cref="Data"/>: a brick's close is a
        /// box edge, not the market price.
        /// </summary>
        public TimeSeriesBuffer<Ohlcv> MarketBars => TimeBars ?? Data;

        /// <summary>
        /// The real bar (an index into <see cref="MarketBars"/>) that drawn bar
        /// <paramref name="drawnIndex"/> starts at: its span's first bar on a brick chart, the
        /// index itself on every other chart. "Near the start of the history" is measured in
        /// these: 50 point and figure columns can be years of daily bars, so asking for older
        /// history whenever the cursor was within 50 DRAWN bars of the start fetched on nearly
        /// every Left arrow until the whole history was in (Cody, 2026-11-20: "it loaded all
        /// the chart history on the daily chart ... and yet I'm only viewing 58 bars").
        /// </summary>
        public int RealBarIndex(int drawnIndex)
        {
            if (BrickSpans.IsDefaultOrEmpty || drawnIndex < 0) return drawnIndex;
            return BrickSpans[Math.Min(drawnIndex, BrickSpans.Length - 1)].FirstBar;
        }
    }

    public abstract record WorkspaceAction;
    public record SetIdentityAction(ChartIdentity Identity) : WorkspaceAction;
    public record ChangeModeAction(TerminalMode Mode) : WorkspaceAction;
    public record UpdateDataAction(TimeSeriesBuffer<Ohlcv> NewData, bool IsInitialLoad) : WorkspaceAction;
    public record NavigateAction(int NewIndex) : WorkspaceAction;
    /// <summary>
    /// Moves the cursor within the current viewport without any scroll logic.
    /// Used by Home/End which must never advance the viewport. Unlike
    /// <see cref="NavigateAction"/>, this does not recompute ViewportStartIndex.
    /// The index is clamped into <c>[ViewportStartIndex, ViewportStartIndex + cursorWindow - 1]</c>
    /// so it can never land past the last visible bar.
    /// </summary>
    public record SetCursorAction(int NewIndex) : WorkspaceAction;
    public record PanAction(int Delta) : WorkspaceAction;
    public record ZoomAction(int NewLength) : WorkspaceAction;
    /// <summary>
    /// Scroll-wheel zoom centred on a cursor position. <paramref name="Direction"/> is
    /// +1 (zoom in / shrink viewport) or -1 (zoom out / grow viewport).
    /// <paramref name="AnchorFraction"/> is the cursor's X position as a fraction of the
    /// viewport width [0..1] — the bar under that fraction stays pinned to the cursor as
    /// the viewport expands or contracts around it. Contrast with <see cref="ZoomAction"/>,
    /// which is cursor-agnostic and always re-anchors to the live edge.
    /// </summary>
    public record WheelZoomAction(int Direction, double AnchorFraction) : WorkspaceAction;
    public record JumpToLatestAction() : WorkspaceAction;
    /// <summary>Adjusts PanningGranularity by <paramref name="Delta"/> percentage points (e.g. +5 or -5).</summary>
    public record AdjustGranularityAction(int Delta) : WorkspaceAction;
    public record AdjustPlaybackSpeedAction(float Delta) : WorkspaceAction;

    /// <summary>
    /// Sets the playback speed outright, for restoring the saved preference at startup.
    /// <see cref="AdjustPlaybackSpeedAction"/> is relative and cannot express "put it back to
    /// what it was" without knowing where it is now.
    /// </summary>
    public record SetPlaybackSpeedAction(float Speed) : WorkspaceAction;
    public record ToggleSpeechAction() : WorkspaceAction;
    public record ToggleSonificationAction() : WorkspaceAction;
    public record ToggleEventSpeechAction() : WorkspaceAction;
    public record ToggleEarconsAction() : WorkspaceAction;
    public record SelectSeriesAction(string SeriesId) : WorkspaceAction;
    /// <summary>
    /// Sets the workspace's <see cref="WorkspaceState.PrimarySeriesId"/> — the series id that
    /// keyboard nav / speech / audio / render fallbacks treat as the chart's "main data".
    /// Dispatched by <c>WorkspaceInitializer</c> based on the provider's data shape.
    /// </summary>
    public record SetPrimarySeriesIdAction(string SeriesId) : WorkspaceAction;
    /// <summary>
    /// Sets the per-tab <see cref="WorkspaceState.CurrentDataShape"/> and
    /// <see cref="WorkspaceState.SymbolDisplayName"/> together. Dispatched by
    /// <c>WorkspaceInitializer</c> after reconciling the core series stack so the
    /// reducer sees the new shape/label atomically. Pairing them in one action avoids
    /// the intermediate "shape updated but display name stale" state.
    /// </summary>
    public record SetProviderContextAction(ProviderDataShape DataShape, string SymbolDisplayName) : WorkspaceAction;
    public record SelectComponentAction(int ComponentIndex) : WorkspaceAction;
    public record SelectBinAction(int BinIndex) : WorkspaceAction;
    public record SetInteractionContextAction(InteractionContext Context) : WorkspaceAction;
    public record ToggleMuteAction(string? SeriesId = null, string? ComponentName = null) : WorkspaceAction;
    public record ToggleHideAction(string? SeriesId = null, string? ComponentName = null) : WorkspaceAction;

    /// <summary>
    /// Makes every hidden component visible, or unmutes every muted one, across all series.
    ///
    /// <para>
    /// The escape hatch for the single-key H and M toggles. Hide or mute a handful of components
    /// across a few indicators and there is no practical way to find them again — you would have
    /// to walk every component of every series checking its state, and a screen-reader user pays
    /// that cost one utterance at a time. Without a reset the toggles are a one-way door.
    /// </para>
    /// </summary>
    /// <param name="Unhide">True to show all; false to unmute all.</param>
    public record RestoreAllComponentsAction(bool Unhide) : WorkspaceAction;
    /// <param name="ComponentName">Null toggles the SERIES flag (the master). A name toggles
    /// that component's place in the series' narration selection — see
    /// <c>SeriesNarrationScope</c> for how the two combine.</param>
    public record ToggleNarrationAction(string? SeriesId = null, string? ComponentName = null) : WorkspaceAction;
    /// <summary>
    /// The third escape hatch, beside <see cref="RestoreAllComponentsAction"/>'s two: switch
    /// narration off on every series and clear every component selection, so the chart is back
    /// in the state it loads in. N is per object like H and M, and a selection built up over a
    /// session across three indicators and a dozen components has no other way back.
    /// </summary>
    public record ClearAllNarrationAction() : WorkspaceAction;
    public record SetPlaybackAction(bool IsPlaying, PlaybackScope Scope = PlaybackScope.Chart) : WorkspaceAction;
    public record TogglePauseAction() : WorkspaceAction;
    public record ToggleHeikinAshiAction() : WorkspaceAction;

    /// <summary>
    /// The one user-facing chart-type choice: Candles, Heikin Ashi, Line or Area.
    /// <paramref name="HeikinAshi"/> is carried rather than read from the current state so
    /// choosing it in the select restores Candles+HA on a tab that was showing Line.
    /// </summary>
    public record SetChartStyleAction(PriceChartStyle Style, bool HeikinAshi) : WorkspaceAction;

    /// <summary>
    /// Changes the tab's built-chart settings. When the chart is Renko, range bars or line break
    /// the reducer refreezes that type's size from <see cref="WorkspaceState.TimeBars"/> and
    /// rebuilds. The command
    /// or dialog has already run the refusal checks. A restore carries the saved frozen box
    /// so the same bricks return instead of a fresh ATR box.
    /// </summary>
    public record SetRenkoSettingsAction(RenkoSettings Settings, double BrickBoxSize = 0) : WorkspaceAction;

    /// <summary>
    /// The one user-facing price-scale choice: Regular, Logarithmic, Percent or Indexed to 100.
    /// <paramref name="Log"/> carries the Logarithmic entry, because that is still the separate
    /// flag; the reducer keeps the pair consistent — any non-Regular mode clears log, and a log
    /// request only applies with <see cref="PriceScaleMode.Regular"/>.
    /// </summary>
    public record SetPriceScaleAction(PriceScaleMode Mode, bool Log) : WorkspaceAction;

    public record ToggleLogScaleAction() : WorkspaceAction;
    public record AddSeriesAction(ChartSeries Series) : WorkspaceAction;
    public record RemoveSeriesAction(string SeriesId) : WorkspaceAction;
    /// <summary>Adds a reference level line to an existing indicator series.</summary>
    public record AddLevelAction(string SeriesId, LevelConfig Level) : WorkspaceAction;

    /// <summary>
    /// Removes a reference level from a series by name.
    ///
    /// <para>
    /// There was no removal path of any kind until 2026-08-04 — levels could be added from the
    /// keyboard and edited in Properties, but never deleted. That is how a stray level at zero, added
    /// by an accidental keypress on the price series, survived in a maintainer's workspace and broke
    /// the price axis at every launch: there was literally no way to take it back out.
    /// </para>
    /// </summary>
    public record RemoveLevelAction(string SeriesId, string LevelName) : WorkspaceAction;

    /// <summary>
    /// Turns a reference level on or off — both halves of it at once.
    ///
    /// <para>
    /// A level is two things to this app: a dashed line on the pane, and a sound when the value
    /// crosses it. <see cref="LevelConfig.IsVisible"/> and <see cref="LevelConfig.PlayEarcon"/> are
    /// separate fields because the Properties dialog lets them be set separately, but from the
    /// keyboard they are one idea — "is this line switched on" — and moving them together is what
    /// makes the 0 key a toggle rather than a dialog.
    /// </para>
    ///
    /// <para>
    /// The target state travels IN the action rather than being computed in the reducer, so the
    /// sentence the user hears and the state they end up in cannot disagree: the caller decides
    /// once, says it, and dispatches it.
    /// </para>
    /// </summary>
    public record SetLevelAudibleAction(string SeriesId, string LevelName, bool Audible) : WorkspaceAction;
    public record UpdateSeriesAction(ImmutableList<ChartSeries> Series) : WorkspaceAction;
    public record UpdateSeriesDataAction(string SeriesId, SeriesDataBuffer Data) : WorkspaceAction;
    /// <summary>
    /// Surgical update: replaces the dynamic zone bands on an indicator series's Config.
    /// Called by IndicatorOrchestrator after Calculate() when the provider wrote zone bands via
    /// IIndicatorResultBuffer.WriteZoneBands(). Preserves all other Config and Data fields.
    /// </summary>
    public record UpdateSeriesZoneBandsAction(string SeriesId, IReadOnlyList<ZoneBandConfig> ZoneBands) : WorkspaceAction;
    /// <summary>
    /// Persists auto-detected parameter overrides (e.g. Cipher S adaptive window) back to the series.
    /// Merges only the supplied keys — unaffected parameters are left unchanged.
    /// </summary>
    public record UpdateSeriesParametersAction(string SeriesId, Dictionary<string, double> Updates) : WorkspaceAction;
    public record UpdateSettingsAction(Func<WorkspaceState, WorkspaceState> Updater) : WorkspaceAction;
    public record RequestInitializationStatusAction(InitializationStatus Status) : WorkspaceAction;
    public record SetDataStatusAction(DataStatus Status) : WorkspaceAction;

    /// <summary>
    /// A live tick arrived. Stamps <see cref="WorkspaceState.LastTickUtc"/> and clears
    /// <see cref="DataStatus.Stale"/> if it was set — recovery has to be as visible as the
    /// failure, or a user who heard "stale" keeps distrusting a feed that came back.
    /// </summary>
    public record LiveTickObservedAction(DateTime AtUtc) : WorkspaceAction;

    /// <summary>
    /// A watchdog decided the feed has gone quiet. Sets <see cref="DataStatus.Stale"/> without
    /// touching <see cref="WorkspaceState.LastTickUtc"/>, so "how long has it been quiet" stays
    /// answerable.
    /// </summary>
    public record MarkFeedStaleAction : WorkspaceAction;
    /// <summary>Enters or leaves bar-replay mode. See <see cref="WorkspaceState.IsReplaying"/>.</summary>
    public record SetReplayModeAction(bool Active) : WorkspaceAction;
    /// <summary>Adjusts the chart-level (master) sonification volume by <paramref name="Delta"/>.</summary>
    public record AdjustChartVolumeAction(string Target, float Delta) : WorkspaceAction;
    public record AdjustVolumeAction(string? SeriesId, string? ComponentName, float Delta) : WorkspaceAction;
    public record NavigateRelativeAction(int Delta) : WorkspaceAction;
    public record WorkspacePanEvent(int Direction) : WorkspaceAction;
    public record WorkspaceZoomEvent(string Direction) : WorkspaceAction;
    /// <summary>Adjusts an indicator pane's height by <paramref name="Delta"/> fraction of totalPaneHeight.</summary>
    public record ResizePaneAction(string PaneName, float Delta) : WorkspaceAction;
    /// <summary>Bulk-sets pane height ratios (used when restoring a saved workspace).</summary>
    public record SetPaneHeightRatiosAction(ImmutableDictionary<string, float> Ratios) : WorkspaceAction;
    /// <summary>Sets (or clears, with null) how the active tab opens. See <see cref="RestoredTabLoad"/>.</summary>
    public record SetRestoredLoadAction(RestoredTabLoad? Load) : WorkspaceAction;

    // ── Coordinate Entry mode ──────────────────────────────────────────────────
    /// <summary>Activates Coordinate Entry mode for keyboard-first drawing anchor placement.</summary>
    public record EnterCoordinateEntryAction(DrawingType Tool) : WorkspaceAction;
    /// <summary>Sets an anchor at the given data index. When the required anchor count is reached the drawing is completed externally.</summary>
    public record SetCoordinateEntryAnchorAction(int DataIndex) : WorkspaceAction;
    /// <summary>Exits Coordinate Entry mode without completing the drawing (Escape).</summary>
    public record ExitCoordinateEntryAction() : WorkspaceAction;

    // ── Multi-tab ─────────────────────────────────────────────────────────────
    /// <summary>Opens a new empty tab to the right of the active tab.</summary>
    public record AddTabAction() : WorkspaceAction;
    /// <summary>Closes the tab at <paramref name="TabIndex"/>. Ignored when only one tab is open.</summary>
    public record CloseTabAction(int TabIndex) : WorkspaceAction;
    /// <summary>Switches to the tab at <paramref name="TargetIndex"/>. Saves the current tab's state as a snapshot first.</summary>
    public record SwitchTabAction(int TargetIndex) : WorkspaceAction;
}
