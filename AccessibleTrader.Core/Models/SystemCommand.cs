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
        ToggleNarration,    // N (chart) / Ctrl+Alt+Shift+N (global): narrate the focused component or series
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

        // Top-level pane navigation — a pane is a Y axis, so this is the key that moves
        // between the chart's horizontal bands (price → volume → Cipher B → …).
        NavPaneNext,           // Alt+PageDown: first series of the next pane down
        NavPanePrev,           // Alt+PageUp:   first series of the previous pane up

        // Intra-pane component navigation. Walks components in the CURRENT STRIP — the pane's
        // main area, or one sub-pane of it — across every series in the pane, which is what
        // finally puts candles and price on one key.
        NavComponentInPaneNext, // Ctrl+Down: next component in this strip
        NavComponentInPanePrev, // Ctrl+Up:   previous component in this strip

        // Orientation and recovery
        /// <summary>Ctrl+Alt+Shift+Y: describe the chart's LAYOUT — axes, scales, panes, series
        /// counts, and whether the feed has gone stale. The one thing a sighted user gets for
        /// free by glancing at the screen.
        /// <para>
        /// These three were Alt+Shift+L / H / M when they landed, and this comment still said so
        /// long after they moved. On the WebHost every Ctrl+Shift+letter chord is rewritten to
        /// Alt+Shift+letter, so all three sat on top of the Text Label, Horizontal Line and
        /// Measure tools; they moved to three-modifier chords, which the rewrite does not touch.
        /// Alt+Shift+L on the WebHost is the TEXT LABEL tool, and nothing else.
        /// </para></summary>
        SpeakChartLayout,
        /// <summary>
        /// Alt+Shift+/ : describe THE PANE THE CURSOR IS IN — what its two axes measure, the
        /// range each covers, and the step between gridlines.
        /// <para>
        /// <see cref="SpeakChartLayout"/> answers "what is on this chart"; this answers "what is
        /// this band of it, and what scale am I reading against". They are different questions
        /// and the second one arrives constantly: a value means nothing without the range it sits
        /// in, and a sighted user reads that range off an axis they can see. Per pane rather than
        /// per chart because each pane HAS its own Y axis — that is what makes it a pane.
        /// </para></summary>
        SpeakPaneInfo,
        /// <summary>Ctrl+Alt+Shift+K: make every hidden component visible again.</summary>
        ShowAllComponents,
        /// <summary>Ctrl+Alt+Shift+U: unmute every muted component.</summary>
        UnmuteAllComponents,

        // Undo / redo
        /// <summary>Ctrl+Z: reverse the last chart edit (drawing anchor move, series delete).
        /// Before 2026-08-27 nothing in the repo implemented undo at all, so a drawing grabbed
        /// by accident — the anchor tolerance is 10 px — was gone with no way back.</summary>
        UndoChartEdit,
        /// <summary>Ctrl+Y: re-apply the last undone chart edit.</summary>
        RedoChartEdit,

        // ── Keyboard nudge for drawing anchors ─────────────────────────────────
        // Before 2026-09-03 an existing drawing's anchors could be moved only by a 10-pixel
        // mouse drag or by typing an absolute value into Properties. These four move the
        // SELECTED anchor of the FOCUSED drawing (Page Up / Page Down focus a series) one
        // bar or one price step at a time. SHIFT+ARROW since 2026-09-03 — it shipped on
        // Alt+Shift+Arrow, which Orca takes for table-cell navigation, so the chord never
        // reached the app on the desktop this application is built for. The alternatives are
        // all taken: Ctrl+Alt+Arrow is the VoiceOver modifier on macOS and the workspace
        // switch on every Linux desktop, Ctrl+Shift+Arrow is select-by-word, plain Alt+Arrow
        // is pane scrolling and plain Ctrl+Arrow is the jump. The price of Shift+Arrow is
        // select-by-character, which keyboard.js pays by releasing the chord to form controls.
        /// <summary>Shift+Left: move the selected anchor one BAR earlier (a bar index,
        /// never date arithmetic, so weekends and halts are stepped over).</summary>
        NudgeAnchorEarlier,
        /// <summary>Shift+Right: one bar later; past the last bar it projects into the
        /// reserved right margin.</summary>
        NudgeAnchorLater,
        /// <summary>Shift+Up: raise the selected anchor's price by one step — 1% of the
        /// visible range, never less than one unit in the last spoken decimal place.</summary>
        NudgeAnchorUp,
        /// <summary>Shift+Down: lower it by the same step.</summary>
        NudgeAnchorDown,
        /// <summary>Ctrl+Alt+Shift+G: cycle which anchor of the focused drawing is selected
        /// for nudging; the first press on a newly focused drawing only says which is
        /// selected.</summary>
        CycleDrawingAnchor,
        /// <summary>Ctrl+Alt+Shift+B: snap the selected anchor's price onto its bar's high,
        /// low, open or close — nearest first, then cycling through the four.</summary>
        SnapAnchorToBar
    }
}
