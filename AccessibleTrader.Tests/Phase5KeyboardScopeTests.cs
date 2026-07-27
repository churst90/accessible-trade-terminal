using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Input;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pin tests for the Phase 5 keyboard-scope categorization. The user's rule
    /// (2026-04-27): F-keys (except Shift+F12 which binds to OpenProperties) and
    /// "global" workflow commands fire from any focus location; everything else
    /// requires the chart element to actually have keyboard focus.
    ///
    /// Adding a new SystemCommand without slotting it into IsChartScopedCommand
    /// will silently default it to "global." The Categorization_AllValuesCovered
    /// test exists to surface that omission immediately — review and decide
    /// explicitly which scope the new command belongs to.
    /// </summary>
    public class Phase5KeyboardScopeTests
    {
        // ── Chart-scoped: navigation, viewport, drawing, etc. ──────────────────

        [Theory]
        // Navigation
        [InlineData(SystemCommand.NavLeft)]
        [InlineData(SystemCommand.NavRight)]
        [InlineData(SystemCommand.NavUp)]
        [InlineData(SystemCommand.NavDown)]
        [InlineData(SystemCommand.NavHome)]
        [InlineData(SystemCommand.NavEnd)]
        [InlineData(SystemCommand.NavPageUp)]
        [InlineData(SystemCommand.NavPageDown)]
        [InlineData(SystemCommand.NavLeftJump)]
        [InlineData(SystemCommand.NavRightJump)]
        [InlineData(SystemCommand.JumpToLatest)]
        [InlineData(SystemCommand.NavSubPaneNext)]
        [InlineData(SystemCommand.NavSubPanePrev)]
        [InlineData(SystemCommand.NavComponentInPaneNext)]
        [InlineData(SystemCommand.NavComponentInPanePrev)]
        [InlineData(SystemCommand.SelectNextSeries)]
        [InlineData(SystemCommand.SelectPrevSeries)]
        // Viewport
        [InlineData(SystemCommand.ZoomIn)]
        [InlineData(SystemCommand.ZoomOut)]
        [InlineData(SystemCommand.PanLeft)]
        [InlineData(SystemCommand.PanRight)]
        [InlineData(SystemCommand.GranularityUp)]
        [InlineData(SystemCommand.GranularityDown)]
        [InlineData(SystemCommand.ScrollPanesUp)]
        [InlineData(SystemCommand.ScrollPanesDown)]
        // Playback
        [InlineData(SystemCommand.PlayChart)]
        [InlineData(SystemCommand.PlaySeries)]
        [InlineData(SystemCommand.PlayComponent)]
        [InlineData(SystemCommand.PlayPause)]
        [InlineData(SystemCommand.PlayStop)]
        [InlineData(SystemCommand.PlaySpeedUp)]
        [InlineData(SystemCommand.PlaySpeedDown)]
        // Indicator / display toggles
        [InlineData(SystemCommand.ToggleHeikinAshi)]
        [InlineData(SystemCommand.ToggleLogScale)]
        [InlineData(SystemCommand.ToggleHeatmap)]
        [InlineData(SystemCommand.ToggleIndicatorVisibility)]
        [InlineData(SystemCommand.ToggleIndicatorAudio)]
        [InlineData(SystemCommand.ToggleNarration)]
        [InlineData(SystemCommand.AddReferenceLevel)]
        // OpenProperties — the F-key exception (Shift+F12)
        [InlineData(SystemCommand.OpenProperties)]
        // Series management
        [InlineData(SystemCommand.RemoveSelectedSeries)]
        // Detail / drawing
        [InlineData(SystemCommand.DetailedPointSummary)]
        [InlineData(SystemCommand.CancelDrawing)]
        [InlineData(SystemCommand.ConfirmCoordinateEntry)]
        [InlineData(SystemCommand.DrawTrend)]
        [InlineData(SystemCommand.DrawHorizontal)]
        [InlineData(SystemCommand.DrawVertical)]
        [InlineData(SystemCommand.DrawChannel)]
        [InlineData(SystemCommand.DrawFibonacci)]
        [InlineData(SystemCommand.DrawLabel)]
        [InlineData(SystemCommand.DrawFibExtension)]
        [InlineData(SystemCommand.DrawRectangle)]
        [InlineData(SystemCommand.DrawGannFan)]
        [InlineData(SystemCommand.DrawRiskReward)]
        [InlineData(SystemCommand.DrawAnchoredVwap)]
        [InlineData(SystemCommand.DrawMeasure)]
        [InlineData(SystemCommand.DrawGannBox)]
        [InlineData(SystemCommand.DrawPitchfork)]
        [InlineData(SystemCommand.DrawAngleFib)]
        // Drawing context menu — keyboard equivalent of right-click; must require focus
        [InlineData(SystemCommand.OpenDrawingContextMenu)]
        public void ChartScopedCommand_RequiresFocus(SystemCommand cmd)
        {
            Assert.True(CommandDispatcher.IsChartScopedCommand(cmd),
                $"{cmd} should be chart-scoped per Phase 5 keyboard rule.");
        }

        // ── Global: F-keys, modal opens, accessibility, volume, tabs ───────────

        [Theory]
        // Modal opens (must work from any focus location)
        [InlineData(SystemCommand.OpenSettings)]       // F12
        [InlineData(SystemCommand.OpenHelp)]           // F1
        [InlineData(SystemCommand.OpenObjectTree)]
        [InlineData(SystemCommand.OpenTradingDashboard)]
        [InlineData(SystemCommand.OpenOrderBook)]
        [InlineData(SystemCommand.OpenApiKeys)]
        [InlineData(SystemCommand.OpenAlerts)]
        [InlineData(SystemCommand.OpenIndicators)]
        [InlineData(SystemCommand.OpenDrawingTools)]
        [InlineData(SystemCommand.OpenStrategies)]
        [InlineData(SystemCommand.OpenCustomScripts)]
        [InlineData(SystemCommand.OpenSoundDesigner)]
        [InlineData(SystemCommand.OpenJournal)]
        [InlineData(SystemCommand.OpenAIAnalyst)]
        [InlineData(SystemCommand.OpenWatchlist)]
        [InlineData(SystemCommand.OpenLevelReport)]
        // Bar replay transport — global so the keys work while focus sits in a side panel.
        [InlineData(SystemCommand.ReplayToggle)]
        [InlineData(SystemCommand.ReplayStepForward)]
        [InlineData(SystemCommand.ReplayStepBack)]
        [InlineData(SystemCommand.ReplayPlayPause)]
        // Split view — global; it changes layout, not chart cursor state.
        [InlineData(SystemCommand.SplitViewToggle)]
        [InlineData(SystemCommand.SplitViewCycle)]
        [InlineData(SystemCommand.SplitViewOrientation)]
        // Accessibility toggles (F2, F3)
        [InlineData(SystemCommand.ToggleSpeech)]
        [InlineData(SystemCommand.ToggleSonification)]
        // Volume (F5/F6/F7)
        [InlineData(SystemCommand.VolCompUp)]
        [InlineData(SystemCommand.VolCompDown)]
        [InlineData(SystemCommand.VolSeriesUp)]
        [InlineData(SystemCommand.VolSeriesDown)]
        [InlineData(SystemCommand.VolChartUp)]
        [InlineData(SystemCommand.VolChartDown)]
        // Context summary (F4)
        [InlineData(SystemCommand.ContextSummary)]
        // Background monitoring status (Ctrl+Alt+Shift+M) — a status announcement,
        // must be reachable from any focus location
        [InlineData(SystemCommand.MonitoringStatus)]
        // ChartFocus itself MUST be global — it's the way to get focus to the chart
        [InlineData(SystemCommand.ChartFocus)]
        // Tab management
        [InlineData(SystemCommand.AddTab)]
        [InlineData(SystemCommand.CloseTab)]
        [InlineData(SystemCommand.SwitchTabNext)]
        [InlineData(SystemCommand.SwitchTabPrev)]
        [InlineData(SystemCommand.FocusTabBar)]   // Ctrl+Alt+Shift+T: focus the tab switcher bar
        // Workspace management
        [InlineData(SystemCommand.SaveWorkspace)]
        [InlineData(SystemCommand.LoadWorkspace)]
        [InlineData(SystemCommand.LoadChart)]
        // Data-source change commands (toolbar-driven, no key binding today)
        [InlineData(SystemCommand.ChangeProvider)]
        [InlineData(SystemCommand.ChangeSymbol)]
        [InlineData(SystemCommand.ChangeTimeframe)]
        [InlineData(SystemCommand.RefreshData)]
        // CloseModal — global so it can fire from any focus location while a modal is open
        [InlineData(SystemCommand.CloseModal)]
        // Sentinel
        [InlineData(SystemCommand.None)]
        public void GlobalCommand_DoesNotRequireFocus(SystemCommand cmd)
        {
            Assert.False(CommandDispatcher.IsChartScopedCommand(cmd),
                $"{cmd} should be global per Phase 5 keyboard rule.");
        }

        /// <summary>
        /// Sentinel: every defined SystemCommand value must be referenced by one
        /// of the two theories above. If you add a new SystemCommand and this
        /// test fails, decide explicitly which scope the new command belongs to
        /// and add it to the appropriate InlineData list.
        /// </summary>
        [Fact]
        public void Categorization_AllValuesCovered()
        {
            // Hand-maintained: must include every SystemCommand referenced in either
            // ChartScopedCommand_RequiresFocus or GlobalCommand_DoesNotRequireFocus.
            var covered = new System.Collections.Generic.HashSet<SystemCommand>
            {
                // Chart-scoped
                SystemCommand.NavLeft, SystemCommand.NavRight, SystemCommand.NavUp,
                SystemCommand.NavDown, SystemCommand.NavHome, SystemCommand.NavEnd,
                SystemCommand.NavPageUp, SystemCommand.NavPageDown,
                SystemCommand.NavLeftJump, SystemCommand.NavRightJump,
                SystemCommand.JumpToLatest, SystemCommand.NavSubPaneNext,
                SystemCommand.NavSubPanePrev, SystemCommand.NavComponentInPaneNext,
                SystemCommand.NavComponentInPanePrev, SystemCommand.SelectNextSeries,
                SystemCommand.SelectPrevSeries, SystemCommand.ZoomIn, SystemCommand.ZoomOut,
                SystemCommand.PanLeft, SystemCommand.PanRight,
                SystemCommand.GranularityUp, SystemCommand.GranularityDown,
                SystemCommand.ScrollPanesUp, SystemCommand.ScrollPanesDown,
                SystemCommand.PlayChart, SystemCommand.PlaySeries, SystemCommand.PlayComponent,
                SystemCommand.PlayPause, SystemCommand.PlayStop,
                SystemCommand.PlaySpeedUp, SystemCommand.PlaySpeedDown,
                SystemCommand.ToggleHeikinAshi, SystemCommand.ToggleLogScale,
                SystemCommand.ToggleHeatmap, SystemCommand.ToggleIndicatorVisibility,
                SystemCommand.ToggleIndicatorAudio, SystemCommand.ToggleNarration,
                SystemCommand.AddReferenceLevel, SystemCommand.OpenProperties,
                SystemCommand.RemoveSelectedSeries, SystemCommand.DetailedPointSummary,
                SystemCommand.CancelDrawing, SystemCommand.ConfirmCoordinateEntry,
                SystemCommand.DrawTrend, SystemCommand.DrawHorizontal,
                SystemCommand.DrawVertical, SystemCommand.DrawChannel,
                SystemCommand.DrawFibonacci, SystemCommand.DrawLabel,
                SystemCommand.DrawFibExtension, SystemCommand.DrawRectangle,
                SystemCommand.DrawGannFan, SystemCommand.DrawRiskReward,
                SystemCommand.DrawAnchoredVwap, SystemCommand.DrawMeasure,
                SystemCommand.DrawGannBox, SystemCommand.DrawPitchfork,
                SystemCommand.DrawAngleFib, SystemCommand.OpenDrawingContextMenu,
                // Global
                SystemCommand.OpenSettings, SystemCommand.OpenHelp,
                SystemCommand.OpenObjectTree, SystemCommand.OpenTradingDashboard,
                SystemCommand.OpenOrderBook, SystemCommand.OpenApiKeys,
                SystemCommand.OpenAlerts, SystemCommand.OpenIndicators,
                SystemCommand.OpenDrawingTools, SystemCommand.OpenStrategies,
                SystemCommand.OpenCustomScripts, SystemCommand.OpenSoundDesigner,
                SystemCommand.OpenJournal, SystemCommand.OpenAIAnalyst,
                SystemCommand.ToggleSpeech, SystemCommand.ToggleSonification,
                SystemCommand.VolCompUp, SystemCommand.VolCompDown,
                SystemCommand.VolSeriesUp, SystemCommand.VolSeriesDown,
                SystemCommand.VolChartUp, SystemCommand.VolChartDown,
                SystemCommand.ContextSummary, SystemCommand.MonitoringStatus,
                SystemCommand.ChartFocus,
                SystemCommand.AddTab, SystemCommand.CloseTab,
                SystemCommand.SwitchTabNext, SystemCommand.SwitchTabPrev,
                SystemCommand.FocusTabBar,
                SystemCommand.SaveWorkspace, SystemCommand.LoadWorkspace, SystemCommand.LoadChart,
                SystemCommand.ToggleEventSpeech, SystemCommand.ToggleEarcons, SystemCommand.OpenMyData,
                SystemCommand.OpenWatchlist, SystemCommand.OpenLevelReport,
                SystemCommand.ReplayToggle, SystemCommand.ReplayStepForward,
                SystemCommand.ReplayStepBack, SystemCommand.ReplayPlayPause,
                SystemCommand.SplitViewToggle, SystemCommand.SplitViewCycle,
                SystemCommand.SplitViewOrientation,
                SystemCommand.ToggleBraille, SystemCommand.OpenBrailleSettings,
                SystemCommand.ChangeProvider, SystemCommand.ChangeSymbol,
                SystemCommand.ChangeTimeframe, SystemCommand.RefreshData,
                SystemCommand.CloseModal,
                // Orientation and recovery — global, like the other Alt+Shift commands: you ask
                // "what am I looking at?" and "show everything again" from wherever you are.
                SystemCommand.SpeakChartLayout,
                SystemCommand.ShowAllComponents, SystemCommand.UnmuteAllComponents,
                SystemCommand.None,
            };

            var allValues = (SystemCommand[])System.Enum.GetValues(typeof(SystemCommand));
            var missing = new System.Collections.Generic.List<SystemCommand>();
            foreach (var v in allValues)
            {
                if (!covered.Contains(v)) missing.Add(v);
            }

            Assert.True(missing.Count == 0,
                "New SystemCommand values not categorized in Phase5KeyboardScopeTests: "
                + string.Join(", ", missing));
        }
    }
}
