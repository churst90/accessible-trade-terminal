using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Models
{
    public class WorkspaceConfiguration
    {
        public TerminalMode Mode { get; set; } = TerminalMode.Trading;
        public MarketType SelectedMarketType { get; set; } = MarketType.Crypto;
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
    }
}