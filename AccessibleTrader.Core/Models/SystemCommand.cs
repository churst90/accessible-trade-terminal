namespace AccessibleTrader.Core.Models
{
    public enum SystemCommand
    {
        None,
        
        // Global UI
        OpenSettings,
        OpenProperties,
        OpenObjectTree,     // Alt+O

        /// <summary>
        /// Alt+I — the asset dossier for whatever is loaded on the active chart.
        /// I for Instrument / Info. Verified free: the plain-Alt letters already taken are
        /// A, B, C, D, H, J, K, L, M, O, R, S, T, W and comma.
        /// </summary>
        OpenAssetDossier,   // Alt+I
        OpenTradingDashboard, // Alt+T
        OpenOrderBook,      // Alt+B
        OpenHelp,           // F1
        OpenApiKeys,        // Alt+K
        OpenAlerts,         // Alt+J
        OpenIndicators,     // Alt+A
        OpenDrawingTools,
        OpenStrategies,     // Alt+S
        OpenCustomScripts,  // Alt+,
        OpenSoundDesigner,  // Alt+W
        OpenJournal,        // Ctrl+Alt+Shift+J: open the alert / speech journal modal
        OpenMyData,         // Ctrl+Alt+Shift+I: import/manage My Data CSV datasets
        OpenWatchlist,      // Alt+M: market watch — watchlists + the screener
        OpenLevelReport,    // Alt+R: respect report — which lines this market actually honours
        ReplayToggle,       // Ctrl+Alt+Shift+P: start bar replay at the cursor / stop it
        ReplayStepForward,  // F9: reveal the next bar
        ReplayStepBack,     // Shift+F9: hide the last revealed bar
        ReplayPlayPause,    // F8: auto-advance on/off
        SplitViewToggle,    // Ctrl+Alt+Shift+S: show a second tab beside this one
        SplitViewCycle,     // Ctrl+Alt+Shift+E: move the second pane to the next tab
        SplitViewOrientation, // Ctrl+Alt+Shift+O: side-by-side <-> stacked
        ContextSummary,
        MonitoringStatus,   // Ctrl+Alt+Shift+M: speak the background-workspace monitoring summary
        ChartFocus,         // Ctrl+Alt+Shift+C: explicit chart focus + context summary
        
        // Accessibility Toggles
        ToggleSpeech,        // F2: interactive/command speech
        ToggleSonification,  // F3: chart sonification (nav tones, playback)
        ToggleEventSpeech,   // Shift+F2: ambient/event speech (alerts, monitoring, new bars)
        ToggleEarcons,       // Shift+F3: earcons (order-outcome + error earcons break through)
        ToggleBraille,       // F4: braille / tactile display output on/off
        OpenBrailleSettings, // Shift+F4: braille display settings (Settings dialog)
        ToggleNarration,    // Ctrl+Alt+Shift+N: toggle auto-narration for the focused series
        // Navigation (Historical/Static)
        NavLeft,
        NavRight,
        NavUp,
        NavDown,
        NavHome,
        NavEnd,
        NavPageUp,
        NavPageDown,
        NavLeftJump,   // Ctrl+Left: jump to previous trendline/price crossing
        NavRightJump,  // Ctrl+Right: jump to next trendline/price crossing

        /// <summary>
        /// Comma — step back to the previous chart-formation edge (the start of a formation, or
        /// the bar one resolved on).
        /// <para>
        /// Bare comma and period, not a chord: a chart can hold dozens of formations and this is a
        /// key the user presses repeatedly while reading. Both were free — the unshifted single
        /// characters already bound are H, M, 0, P, bracket, minus, equals and backslash.
        /// </para>
        /// </summary>
        NavPatternPrev,

        /// <summary>Period — step forward to the next chart-formation edge.</summary>
        NavPatternNext,

        /// <summary>
        /// Semicolon — cycle which of the overlapping formations at this bar leads the readout.
        /// Press again to move to the next; Shift+semicolon clears the choice.
        /// <para>
        /// The terminal ranks overlapping shapes by size because that is the only tie-break that is
        /// not a directional opinion. This lets the user override that ranking with their own,
        /// without the application acquiring one.
        /// </para>
        /// </summary>
        CyclePatternFocus,

        /// <summary>Shift+semicolon — stop pinning; return to the size ranking.</summary>
        ClearPatternFocus,

        // ── Quick trade from the chart ────────────────────────────────────────
        // Arm a risk budget, set a stop from a bar, place. See QuickTradeService for
        // why the stop must come before the size.
        QuickArmRisk1,      // Ctrl+Alt+Shift+1 — 0.5%
        QuickArmRisk2,      // Ctrl+Alt+Shift+2 — 1%
        QuickArmRisk3,      // Ctrl+Alt+Shift+3 — 2%
        QuickSetStop,       // Ctrl+Alt+Shift+S — the bar under the cursor becomes the stop
        QuickPlaceLimit,    // Shift+Enter — limit at the cursor bar
        QuickPlaceMarket,   // Ctrl+Enter — market now
        QuickDisarm,        // Ctrl+Alt+Shift+0 — cancel
        QuickArmStatus,     // Ctrl+Alt+Shift+Q — what am I armed with?


        // View/Zoom
        ZoomIn,
        ZoomOut,
        PanLeft,
        PanRight,
        JumpToLatest,
        GranularityUp,   // Shift+[ : widen pan step
        GranularityDown, // Shift+] : narrow pan step
        ScrollPanesUp,   // Alt+Up  : scroll indicator panes up (reveal panes above)
        ScrollPanesDown, // Alt+Down: scroll indicator panes down (reveal panes below)
        
        // Playback (Live/Dynamic)
        PlayChart,
        PlaySeries,
        PlayComponent,
        PlayPause,
        PlayStop,
        PlaySpeedUp,
        PlaySpeedDown,
        
        // Volume
        VolCompUp,
        VolCompDown,
        VolSeriesUp,
        VolSeriesDown,
        VolChartUp,
        VolChartDown,
        
        // Chart Settings
        ToggleHeikinAshi,
        ToggleLogScale,
        ToggleIndicatorVisibility, // H
        ToggleIndicatorAudio, // M
        ToggleHeatmap, // Alt+H
        AddReferenceLevel, // 0
        
        // Drawings
        DrawTrend,
        DrawHorizontal,
        DrawVertical,
        DrawChannel,
        DrawFibonacci,
        DrawLabel,
        DrawFibExtension,   // Ctrl+Shift+E
        DrawRectangle,      // Ctrl+Shift+R
        DrawGannFan,        // Ctrl+Shift+G
        DrawRiskReward,     // Ctrl+Shift+P
        DrawAnchoredVwap,   // Ctrl+Shift+W
        DrawMeasure,        // Ctrl+Shift+M
        DrawGannBox,        // Ctrl+Shift+B
        DrawPitchfork,      // Ctrl+Shift+A
        DrawAngleFib,       // Ctrl+Shift+J
        
        // Series Management
        RemoveSelectedSeries, // Delete: remove the currently focused indicator series
        SelectNextSeries,     // cycle focus to the next series (no default binding)
        SelectPrevSeries,     // cycle focus to the previous series (no default binding)

        // Detail / Drawing
        DetailedPointSummary, // Ctrl+Shift+D: speak full candle pattern analysis
        CancelDrawing,        // Escape (no modal): cancel an in-progress drawing placement
        ConfirmCoordinateEntry, // RESERVED / unused: drawings are placed by re-pressing the tool shortcut at each anchor (see DrawingInteractionManager). No default key binding and no dispatch handler; kept for profile/back-compat only.
        OpenDrawingContextMenu, // ContextMenu key / Shift+F10: open the drawing context menu on the focused drawing
        CloseModal,           // Escape (modal open): close the topmost open modal

        // Data/Market
        ChangeProvider,
        ChangeSymbol,
        ChangeTimeframe,
        RefreshData,

        // Multi-tab
        AddTab,          // Ctrl+T: open new tab
        CloseTab,        // Ctrl+W: close active tab
        SwitchTabNext,   // Ctrl+Tab: cycle to next tab
        SwitchTabPrev,   // Ctrl+Shift+Tab: cycle to previous tab
        FocusTabBar,     // Ctrl+Alt+Shift+T: move keyboard focus onto the workspace tab switcher bar

        // AI Analyst
        OpenAIAnalyst,   // Ctrl+Alt+Shift+A: open AI Technical Analyst

        // Workspace management
        SaveWorkspace,   // Ctrl+Alt+Shift+W: save workspace profile
        LoadWorkspace,   // Ctrl+Alt+W: load workspace profile
        LoadChart,       // Ctrl+Alt+Shift+L: load the chart for the toolbar's selected symbol

        // Sub-pane navigation
        NavSubPaneNext,        // Ctrl+PageDown: jump to first component of next sub-pane
        NavSubPanePrev,        // Ctrl+PageUp:   jump to first component of previous sub-pane

        // Intra-pane component navigation (cycles only within the focused component's pane)
        NavComponentInPaneNext, // Ctrl+Down: next component within the same pane (wraps)
        NavComponentInPanePrev, // Ctrl+Up:   previous component within the same pane (wraps)

        // Orientation and recovery
        /// <summary>Alt+Shift+L: describe the chart's LAYOUT — axes, scales, panes, series counts.
        /// The one thing a sighted user gets for free by glancing at the screen.</summary>
        SpeakChartLayout,
        /// <summary>Ctrl+Alt+Shift+H: make every hidden component visible again.</summary>
        ShowAllComponents,
        /// <summary>Ctrl+Alt+Shift+M: unmute every muted component.</summary>
        UnmuteAllComponents,

        // Undo / redo
        /// <summary>Ctrl+Z: reverse the last chart edit (drawing anchor move, series delete).
        /// Before 2026-08-27 nothing in the repo implemented undo at all, so a drawing grabbed
        /// by accident — the anchor tolerance is 10 px — was gone with no way back.</summary>
        UndoChartEdit,
        /// <summary>Ctrl+Y: re-apply the last undone chart edit.</summary>
        RedoChartEdit
    }
}
