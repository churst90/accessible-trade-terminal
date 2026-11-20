using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// What the arrow keys land on when the chart type changes what is drawn.
    ///
    /// <para>
    /// The price series itself takes the chart type's shape (<c>PriceSeriesShape</c>): on Line and
    /// Area its one part is the close line, on Renko its one part is the brick. So the arrows
    /// always walk the price series, whatever the type. Cody, 2026-11-19: "if a line is drawn then
    /// the candles series should be replaced with a line series and same for the bricks".
    /// </para>
    ///
    /// <para>
    /// The separate Price series is a second line through the closes. Line and Area already draw
    /// that line, so its ink is skipped there (<c>DataLayer.IsClosePriceLineHiddenByChartStyle</c>)
    /// — and what is not drawn is not walked: it leaves the rotation, and focus asked of it lands
    /// on the price series. A Price line remapped to the high is a different line and stays.
    /// (The first version of this went the other way, sending the arrows from the candles to the
    /// Price line — which Cody's charts do not have, so he walked a line chart's wicks.)
    /// </para>
    /// </summary>
    public static class ChartTypeFocus
    {
        /// <summary>True on Line, Area, Baseline and HLC area when the Price series only repeats
        /// the close line.</summary>
        public static bool PriceLineOffScreen(WorkspaceState state) =>
            Charts.PriceSeriesShape.DrawsCloseLine(state.ChartStyle)
            && ClosePriceSeries(state) != null
            && state.ActiveSeries.Any(s => s.Id == CoreSeriesIds.Candles);

        /// <summary>The Price series when its visible parts all draw the close, else null.</summary>
        public static ChartSeries? ClosePriceSeries(WorkspaceState state)
        {
            var price = state.ActiveSeries.FirstOrDefault(s => s.Id == CoreSeriesIds.Price);
            if (price == null) return null;
            var visible = price.Components.Where(c => c.IsVisible).ToList();
            return visible.Count > 0
                && visible.All(c => string.Equals(c.DataMapping, "close", StringComparison.OrdinalIgnoreCase))
                ? price
                : null;
        }

        /// <summary>Whether the arrow keys may focus <paramref name="series"/>.</summary>
        public static bool IsNavigable(WorkspaceState state, ChartSeries series) =>
            !(series.Id == CoreSeriesIds.Price && PriceLineOffScreen(state));

        /// <summary>
        /// The series a focus request for <paramref name="seriesId"/> really lands on: the price
        /// series in place of an undrawn Price line, otherwise the request unchanged.
        /// </summary>
        public static string? Redirect(WorkspaceState state, string? seriesId) =>
            seriesId == CoreSeriesIds.Price && PriceLineOffScreen(state) ? CoreSeriesIds.Candles : seriesId;
    }
}
