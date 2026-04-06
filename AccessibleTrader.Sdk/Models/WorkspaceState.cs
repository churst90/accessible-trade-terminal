using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;

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
        int CoordinateEntryAnchor1Index
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
        ImmutableList<TabSnapshot> TabSnapshots = default!,
        /// <summary>Zero-based index of the currently active tab.</summary>
        int ActiveTabIndex = 0
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
            ActiveTabIndex: 0
        );

        /// <summary>Number of open tabs (active tab + inactive snapshots).</summary>
        public int TabCount => (TabSnapshots?.Count ?? 0) + 1;
    }

    public abstract record WorkspaceAction;
    public record SetIdentityAction(ChartIdentity Identity) : WorkspaceAction;
    public record ChangeModeAction(TerminalMode Mode) : WorkspaceAction;
    public record UpdateDataAction(TimeSeriesBuffer<Ohlcv> NewData, bool IsInitialLoad) : WorkspaceAction;
    public record NavigateAction(int NewIndex) : WorkspaceAction;
    public record PanAction(int Delta) : WorkspaceAction;
    public record ZoomAction(int NewLength) : WorkspaceAction;
    public record JumpToLatestAction() : WorkspaceAction;
    /// <summary>Adjusts PanningGranularity by <paramref name="Delta"/> percentage points (e.g. +5 or -5).</summary>
    public record AdjustGranularityAction(int Delta) : WorkspaceAction;
    public record AdjustPlaybackSpeedAction(float Delta) : WorkspaceAction;
    public record ToggleSpeechAction() : WorkspaceAction;
    public record ToggleSonificationAction() : WorkspaceAction;
    public record SelectSeriesAction(string SeriesId) : WorkspaceAction;
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
