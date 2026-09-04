using System.Collections.Immutable;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Models
{
    public enum InteractionContext { Series, Component, Bin }
    public enum PlaybackScope { Chart, Series, Component }
    public enum InitializationStatus { Booting, Loading, Resetting, Ready, Error }
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
        string SymbolDisplayName = ""
    );

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
        // an opt-in — the per-series Ctrl+Alt+Shift+N flag for signals, DescribeChartPatterns
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
        bool NarrateDuringPlayback = true
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
    public record ToggleNarrationAction(string? SeriesId = null) : WorkspaceAction;
    public record SetPlaybackAction(bool IsPlaying, PlaybackScope Scope = PlaybackScope.Chart) : WorkspaceAction;
    public record TogglePauseAction() : WorkspaceAction;
    public record ToggleHeikinAshiAction() : WorkspaceAction;
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
