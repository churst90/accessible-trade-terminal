using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// What the chart menu's "here" is: a bar, a value in the pane it belongs to, and the pane's
    /// name. Built from the pointer for a right-click and from the keyboard's cursor otherwise,
    /// so every context-menu item resolves the same three things the same way.
    /// </summary>
    public readonly record struct ChartHere(
        int BarIndex,
        double Price,
        string Pane,
        string? SeriesId = null,
        int ComponentIndex = -1)
    {
        public bool HasBar => BarIndex >= 0;
        public bool HasPrice => !double.IsNaN(Price) && !double.IsInfinity(Price);
    }

    public static class ChartContextHere
    {
        public const string MainPane = "Main";

        /// <summary>
        /// The pointer's "here": the clicked bar, the value under the pointer on the clicked
        /// pane's own scale, and that pane. <paramref name="hitSeriesId"/> is the component hit
        /// under the pointer when there was one; otherwise a non-price pane resolves to its first
        /// visible non-drawing series so per-series actions still have a subject.
        /// </summary>
        public static ChartHere FromMouse(
            WorkspaceState state,
            int barIndex,
            double y,
            double canvasHeight,
            IReadOnlyList<(string BelowPaneName, float DividerFraction)>? dividers,
            float axisHeightFraction,
            IReadOnlyDictionary<string, Rendering.PaneStatusBand>? statusBands,
            string? hitSeriesId = null,
            int hitComponentIndex = -1)
        {
            if (state.Data == null || state.Data.Count == 0)
                return new ChartHere(-1, double.NaN, MainPane);
            if (barIndex < 0 || barIndex >= state.Data.Count)
                return new ChartHere(-1, double.NaN, MainPane);

            var area = ChartMath.PlotAreaAtPx(y, canvasHeight, dividers, axisHeightFraction, statusBands);
            if (area is not { } a)
                return new ChartHere(barIndex, double.NaN, MainPane);

            string pane = a.Pane;
            var (min, max, isLog) = ChartMath.RangeFor(state, pane);
            double price = ChartMath.MapYToPriceInPane(
                y, canvasHeight, dividers, axisHeightFraction, min, max, isLog, statusBands,
                ChartMath.IsInvertedFor(state, pane));

            var series = ResolveSeries(state, pane, hitSeriesId);
            int component = -1;
            if (series is not null)
            {
                int requested = series.Id == hitSeriesId && hitComponentIndex >= 0
                    ? hitComponentIndex
                    : 0;
                component = series.ClampComponent(requested);
            }

            return new ChartHere(barIndex, price, pane, series?.Id, component);
        }

        /// <summary>
        /// The keyboard's "here": the cursor bar. The value is the focused series' own component
        /// on that bar in its pane — or the bar's close when nothing is focused, or the focused
        /// series sits on the price pane.
        /// </summary>
        public static ChartHere FromKeyboard(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0)
                return new ChartHere(-1, double.NaN, MainPane);

            int bar = state.CurrentDataIndex;
            if (bar < 0 || bar >= data.Count)
                return new ChartHere(-1, double.NaN, MainPane);

            var focused = string.IsNullOrEmpty(state.FocusedSeriesId)
                ? null
                : state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);

            string pane = focused is null || string.IsNullOrEmpty(focused.Pane)
                ? MainPane
                : focused.Pane;

            int component = focused?.ClampComponent(state.FocusedComponentIndex) ?? -1;

            if (string.Equals(pane, MainPane, StringComparison.OrdinalIgnoreCase))
                return new ChartHere(bar, data[bar].Close, MainPane, focused?.Id, component);

            double value = component < 0
                ? double.NaN
                : ValueAt(focused!, bar, component) ?? double.NaN;
            return new ChartHere(bar, value, pane, focused!.Id, component);
        }

        /// <summary>The value a series' component carries on a bar, or null when it has none there.</summary>
        public static double? ValueAt(ChartSeries series, int bar, int componentIndex)
        {
            if (series.Components.Count == 0) return null;
            if (bar < 0) return null;
            int index = series.ClampComponent(componentIndex);
            if (index < 0) return null;
            var data = series.GetComponentData(series.Components[index].Name);
            if (data == null || bar >= data.Length) return null;
            double value = data[bar];
            return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
        }

        private static ChartSeries? ResolveSeries(WorkspaceState state, string pane, string? hitSeriesId)
        {
            bool main = string.Equals(pane, MainPane, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(hitSeriesId))
            {
                var hit = state.ActiveSeries.FirstOrDefault(s => s.Id == hitSeriesId);
                if (hit is not null && string.Equals(PaneOf(hit), pane, StringComparison.OrdinalIgnoreCase))
                    return hit;
            }

            if (main)
            {
                var focused = string.IsNullOrEmpty(state.FocusedSeriesId)
                    ? null
                    : state.ActiveSeries.FirstOrDefault(s => s.Id == state.FocusedSeriesId);
                return focused is not null && string.Equals(PaneOf(focused), pane, StringComparison.OrdinalIgnoreCase)
                    ? focused
                    : null;
            }

            return state.ActiveSeries.FirstOrDefault(s =>
                !s.IsDrawing && s.IsVisible
                && string.Equals(PaneOf(s), pane, StringComparison.OrdinalIgnoreCase)
                && s.Components.Count > 0);
        }

        private static string PaneOf(ChartSeries series) =>
            string.IsNullOrEmpty(series.Pane) ? MainPane : series.Pane;
    }
}
