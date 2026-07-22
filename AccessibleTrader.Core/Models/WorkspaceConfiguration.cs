using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Models
{
    /// <summary>
    /// Serializable configuration for a single chart tab within a workspace profile.
    /// Contains only static configuration — computed data arrays are never persisted.
    /// </summary>
    public class TabConfiguration
    {
        public string Market { get; set; } = "";
        public string Provider { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public int ViewportStartIndex { get; set; }
        public int ViewportLength { get; set; }
        public bool IsHeikinAshi { get; set; }
        public bool IsLogScale { get; set; }
        public List<SeriesConfig> Series { get; set; } = new();
        public Dictionary<string, float> PaneHeightRatios { get; set; } = new();

    }

    /// <summary>
    /// One active strategy captured at save time. WORKSPACE-level (not per-tab)
    /// because the engine is global with per-symbol bindings — a strategy bound
    /// to "BTC/USD" restores against whichever tab shows that symbol, exactly
    /// how the engine routes it live. Only library-backed strategies (SpecId set)
    /// can be persisted; ad-hoc compiled scripts are session-only.
    /// </summary>
    public class SavedActiveStrategy
    {
        public string SpecId { get; set; } = "";
        public string? Symbol { get; set; }
        public string ExecutionMode { get; set; } = "Suggestion";
        public bool IsPaused { get; set; }
    }

    /// <summary>
    /// Named workspace profile that captures the full restorable state of the application,
    /// including all open tabs and their configurations.
    /// </summary>
    public class WorkspaceConfiguration
    {
        public TerminalMode Mode { get; set; } = TerminalMode.Trading;
        public MarketType SelectedMarketType { get; set; } = MarketType.Crypto;
        /// <summary>Index of the tab that was active when the workspace was saved.</summary>
        public int ActiveTabIndex { get; set; }
        /// <summary>
        /// All tabs in the workspace. Each tab stores its own chart identity,
        /// series configurations, viewport state, and display toggles.
        /// </summary>
        public List<TabConfiguration> Tabs { get; set; } = new();

        /// <summary>Active strategies at save time (workspace-level; see
        /// <see cref="SavedActiveStrategy"/> for why not per-tab). Restored by
        /// WorkspaceInitializer after tabs are rebuilt.</summary>
        public List<SavedActiveStrategy> ActiveStrategies { get; set; } = new();

        // ── Legacy flat fields (kept for backward compatibility with old default.json) ──
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "1h";
        public string Market { get; set; } = "Spot";
        public string Provider { get; set; } = "Bitstamp";
        public int ViewportStartIndex { get; set; }
        public int ViewportLength { get; set; }
        /// <summary>
        /// Persisted series configurations (layout, colors, levels, parameters).
        /// SeriesConfig is used here instead of ChartSeries so that computed data
        /// arrays (SeriesDataBuffer) are never written to disk.
        /// </summary>
        public List<SeriesConfig> Series { get; set; } = new();
        /// <summary>
        /// Per-pane height ratios saved from the last session.
        /// Key = pane name; value = fraction of totalPaneHeight (0.05–0.60).
        /// Absent key means auto-layout for that pane.
        /// </summary>
        public Dictionary<string, float> PaneHeightRatios { get; set; } = new();

        /// <summary>
        /// Returns true if this config uses the new multi-tab format (Tabs list populated).
        /// False means it's a legacy single-tab config using the flat fields.
        /// </summary>
        public bool IsMultiTab => Tabs?.Count > 0;
    }
}
