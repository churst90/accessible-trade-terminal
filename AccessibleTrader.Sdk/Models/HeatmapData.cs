namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// Represents a single cell in the liquidity grid for a specific price bucket and time slice.
    /// Used by <see cref="HeatmapData"/> to visualize market depth history.
    /// </summary>
    public readonly record struct HeatmapCell(DateTime Time, double Price, double Volume);

    /// <summary>
    /// Encapsulates a grid of liquidity (Order Book Depth) over a specific time and price range.
    /// This is used to render heatmaps and perform audio grain sonification.
    /// </summary>
    public record HeatmapData
    {
        /// <summary>The flattened list of cells (Price vs Time) forming the grid.</summary>
        public List<HeatmapCell> Cells { get; init; } = new();

        /// <summary>The lowest price represented in this data set.</summary>
        public double MinPrice { get; init; }

        /// <summary>The highest price represented in this data set.</summary>
        public double MaxPrice { get; init; }

        /// <summary>The start of the time range covered by this data set.</summary>
        public DateTime StartTime { get; init; }

        /// <summary>The end of the time range covered by this data set.</summary>
        public DateTime EndTime { get; init; }

        /// <summary>The maximum volume value across all cells (used for normalization during rendering).</summary>
        public double MaxVolume { get; init; }

        /// <summary>Optional: The resolution (bin size) of the price axis (e.g., 0.5 for $0.50 buckets).</summary>
        public double PriceStep { get; init; }
    }
}
