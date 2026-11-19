using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Models
{
    /// <summary>
    /// A chart's indicator stack, with its settings and pane heights, saved without a symbol,
    /// timeframe or viewport. Applying one replaces the indicators on the chart in front.
    /// </summary>
    public class IndicatorTemplate
    {
        public int Version { get; set; } = 1;
        public List<SeriesConfig> Series { get; set; } = new();
        public Dictionary<string, float> PaneHeightRatios { get; set; } = new();
        /// <summary>The "Kraken BTCUSDT 1h" chart this was captured from, for the dialog only.</summary>
        public string? SavedFrom { get; set; }
        public DateTime SavedUtc { get; set; }
    }
}
