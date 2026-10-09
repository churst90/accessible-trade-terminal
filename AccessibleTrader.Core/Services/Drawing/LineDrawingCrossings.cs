using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Drawing
{
    /// <summary>
    /// Where price crosses a horizontal line drawing, and where the cursor crosses a vertical one —
    /// shared by the crossing earcon (<see cref="LevelCrossingMonitor"/>) and by Ctrl+Left /
    /// Ctrl+Right (<c>IndicatorCrossingEngine</c>), so the key lands exactly where the chirp sounds.
    ///
    /// <para>
    /// "Price" is the bar's raw close, which is what the trend-line stops have always used and what
    /// the price line speaks. A horizontal line is crossed on the bar whose close is on the other
    /// side of it from the bar before (<see cref="LevelCross.Direction"/> — the reference level's
    /// rule). A vertical line has no price to cross; it is crossed when the cursor moves onto its
    /// bar or past it.
    /// </para>
    /// </summary>
    public static class LineDrawingCrossings
    {
        /// <summary>A horizontal or vertical line that is switched on. Hidden drawings are neither
        /// heard nor stopped at: hiding one says you are not interested in it.</summary>
        public static bool IsCrossable(ChartSeries series)
            => series.IsVisible && series.Drawing is { Type: DrawingType.HorizontalLine or DrawingType.VerticalLine };

        /// <summary>The horizontal line's price, or null for anything else.</summary>
        public static double? HorizontalPrice(ChartSeries series)
            => series.Drawing is { Type: DrawingType.HorizontalLine, AnchorPrice1: double p } && double.IsFinite(p) ? p : null;

        /// <summary>The bar a vertical line stands on — the first bar at or after its date, as the
        /// renderer and <c>VerticalLineCalculator</c> place it — or −1.</summary>
        public static int VerticalBar(ChartSeries series, IReadOnlyList<Ohlcv>? data)
        {
            if (data == null || series.Drawing is not { Type: DrawingType.VerticalLine, AnchorDate1: DateTime at })
                return -1;
            for (int i = 0; i < data.Count; i++)
                if (data[i].Date >= at) return i;
            return -1;
        }

        /// <summary>Which way the close crossed <paramref name="price"/> arriving at bar
        /// <paramref name="index"/> from the bar before it: +1, −1 or 0.</summary>
        public static int HorizontalCrossAt(IReadOnlyList<Ohlcv> data, int index, double price)
            => index < 1 || index >= data.Count ? 0
             : LevelCross.Direction(data[index - 1].Close, data[index].Close, price);

        /// <summary>
        /// Whether moving the cursor from <paramref name="from"/> to <paramref name="to"/> moved onto
        /// or across <paramref name="bar"/>. Leaving the bar is not crossing it — only arriving, or
        /// passing over it in one jump, is — so stepping off a vertical line is silent and stepping
        /// back onto it chirps again.
        /// </summary>
        public static bool VerticalCrossed(int from, int to, int bar)
        {
            if (bar < 0 || from == to) return false;
            return from < to ? bar > from && bar <= to : bar < from && bar >= to;
        }
    }
}
