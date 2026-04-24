using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Sdk.Models
{
    public enum InteractionContext { Series, Component, Bin }
    public enum PlaybackScope { Chart, Series, Component }
    public enum InitializationStatus { Booting, Loading, Resetting, Ready, Error }
    public enum DataStatus { Idle, LoadingHistorical, Filling, Ready, Error }

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
        int IndicatorPaneScrollIndex,
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
        /// <summary>
        /// Panning step as a whole percentage of the viewport (5, 10, 15 … 100).
        /// Example: 10 means each [ or ] press pans by 10% of the visible bars.
        /// Shift+[ decreases this step; Shift+] increases it in 5% increments.
        /// </summary>
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
        /// <summary>
        /// Number of empty future-space slots reserved on the right of the viewport.
        /// The last real bar always lands at slot (ViewportLength - RightMarginBars - 1).
        /// Allows trendlines and drawings to project into future space. Default: 20 bars.
        /// </summary>
        int RightMarginBars = 20,
        bool IsSpeechEnabled = true,
        bool IsSonificationEnabled = true,
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
        /// Per-pane height as a fraction of totalPaneHeight (canvas height minus x-axis).
        /// Key = pane name (e.g. "Pane_RSI"). Absent key = use auto 30%-split layout.
        /// Ratios are clamped to [0.05, 0.75] during dispatch.
        /// </summary>
        ImmutableDictionary<string, float>? PaneHeightRatios = null,
        /// <summary>
        /// Number of indicator pane groups to skip from the top.
        /// Alt+Down increments (scroll down), Alt+Up decrements. Clamped to [0, paneCount-1].
        /// </summary>
        int IndicatorPaneScrollIndex = 0,
        // ── Coordinate Entry mode ──────────────────────────────────────────────
        /// <summary>
        /// True when a drawing shortcut has been pressed and the user is navigating to place anchors.
        /// Arrow keys navigate normally; Enter sets each anchor; Escape cancels.
        /// </summary>
        bool IsCoordinateEntryMode = false,
        /// <summary>The drawing type being placed during Coordinate Entry mode.</summary>
        DrawingType? PendingDrawingTool = null,
        /// <summary>
        /// Number of anchors confirmed so far (0 = no anchor set, 1 = first anchor set, waiting for second).
        /// </summary>
        int CoordinateEntryAnchorCount = 0,
        /// <summary>Data index of the first anchor point (-1 when not yet set).</summary>
        int CoordinateEntryAnchor1Index = -1,
        // ── Multi-tab support ──────────────────────────────────────────────────
        /// <summary>
        /// Frozen snapshots of inactive tabs. The active tab's state lives directly
        /// in WorkspaceState fields; inactive tabs are stored here until re-activated.
        /// </summary>
        ImmutableList<TabSnapshot>? TabSnapshots = null,
        /// <summary>Zero-based index of the currently active tab.</summary>
        int ActiveTabIndex = 0,
        /// <summary>
        /// True when this state is being passed through the offline backtester replay loop.
        /// Strategies that publish setup/audio events to the live IEventBus check this flag
        /// and skip publication during backtest — otherwise replaying 3,000+ bars floods
        /// SetupSonifier with bell/speech events meant for live trading.
        /// </summary>
        bool IsBacktesting = false,
        /// <summary>
        /// The series id that keyboard nav, speech, sonification, and rendering treat
        /// as "the main data" of this chart. Set by WorkspaceInitializer based on the
        /// provider's data shape (OHLCV → candles; SingleValueLine → price). Consumers
        /// should prefer this over hardcoding CoreSeriesIds.Candles.
        /// </summary>
        string PrimarySeriesId = "candles",
        /// <summary>
        /// Mirrors the active provider's declared <see cref="ProviderDataShape"/> on
        /// the current tab. Used by the reconciler in <c>WorkspaceInitializer</c> to
        /// detect shape changes (which cause stripping of all non-core series) and by
        /// the UI layer to hide trading-only controls on analytics tabs. Defaults to
        /// <see cref="ProviderDataShape.Ohlcv"/> for backwards compatibility.
        /// </summary>
        ProviderDataShape CurrentDataShape = ProviderDataShape.Ohlcv,
        /// <summary>
        /// Human-readable label for the currently-loaded symbol, resolved via
        /// <see cref="IMarketDataProvider.GetSymbolDisplayName"/>. Flows into the
        /// Price series FriendlyName + component DisplayName on analytics tabs so
        /// the user hears "Fear and Greed Index, 47" instead of "Price, 47".
        /// Empty string until the first load completes.
        /// </summary>
        string SymbolDisplayName = ""
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
            RightMarginBars: 20,
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
            IsSpeechEnabled: true,
            IsSonificationEnabled: true,
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
    public record ToggleNarrationAction(string? SeriesId = null) : WorkspaceAction;
    public record SetPlaybackAction(bool IsPlaying, PlaybackScope Scope = PlaybackScope.Chart) : WorkspaceAction;
    public record TogglePauseAction() : WorkspaceAction;
    public record ToggleHeikinAshiAction() : WorkspaceAction;
    public record ToggleLogScaleAction() : WorkspaceAction;
    public record AddSeriesAction(ChartSeries Series) : WorkspaceAction;
    public record RemoveSeriesAction(string SeriesId) : WorkspaceAction;
    /// <summary>Adds a reference level line to an existing indicator series.</summary>
    public record AddLevelAction(string SeriesId, LevelConfig Level) : WorkspaceAction;
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
    /// <summary>Adjusts the chart-level (master) sonification volume by <paramref name="Delta"/>.</summary>
    public record AdjustChartVolumeAction(string Target, float Delta) : WorkspaceAction;
    public record AdjustVolumeAction(string? SeriesId, string? ComponentName, float Delta) : WorkspaceAction;
    public record NavigateRelativeAction(int Delta) : WorkspaceAction;
    public record WorkspacePanEvent(int Direction) : WorkspaceAction;
    public record WorkspaceZoomEvent(string Direction) : WorkspaceAction;
    /// <summary>Adjusts an indicator pane's height by <paramref name="Delta"/> fraction of totalPaneHeight.</summary>
    public record ResizePaneAction(string PaneName, float Delta) : WorkspaceAction;
    /// <summary>Scrolls the indicator pane list by <paramref name="Delta"/> steps (+1 = down/reveal lower panes).</summary>
    public record ScrollIndicatorPanesAction(int Delta) : WorkspaceAction;
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
