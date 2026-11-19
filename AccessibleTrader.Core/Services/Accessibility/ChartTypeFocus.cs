using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// What the arrow keys land on when the chart type draws no candles.
    ///
    /// <para>
    /// Cody, 2026-11-19, after trying Line and Area: the picture changed and nothing he heard
    /// did. Only the candles' summary reading knew about the chart type, and the candles are
    /// not on screen on a Line or Area chart — the Price series is the line, so it is what the
    /// keys must walk. <i>"Whatever is selected should be mirrored visually and through sound
    /// and speech."</i> So on those types the candles leave the series rotation, focus that
    /// would land on them lands on the Price line instead, and the Price line reads the point
    /// on the line (<see cref="SpeechFormatter"/>).
    /// </para>
    ///
    /// <para>
    /// Only when the Price series is there to take over and draws the close: a Price line the
    /// user remapped to the high is a different line, and with no close line to walk the
    /// candles stay reachable rather than leaving nothing to arrow along.
    /// </para>
    /// </summary>
    public static class ChartTypeFocus
    {
        /// <summary>True on Line and Area when the Price series carries the close line.</summary>
        public static bool CandlesOffScreen(WorkspaceState state) =>
            state.ChartStyle is PriceChartStyle.Line or PriceChartStyle.Area
            && ClosePriceSeries(state) != null;

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
            !(series.Id == CoreSeriesIds.Candles && CandlesOffScreen(state));

        /// <summary>
        /// The series a focus request for <paramref name="seriesId"/> really lands on: the Price
        /// line in place of off-screen candles, otherwise the request unchanged.
        /// </summary>
        public static string? Redirect(WorkspaceState state, string? seriesId) =>
            seriesId == CoreSeriesIds.Candles && CandlesOffScreen(state) ? CoreSeriesIds.Price : seriesId;
    }
}
