using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Where a <see cref="ComponentDisplayType.Window"/> is open: one answer for the shading
    /// (<see cref="BackgroundTintPainter"/>), Ctrl+Left/Right (IndicatorCrossingEngine) and the
    /// Dot Pad (TactileCanvasCoordinator), so the three cannot disagree about where a window is.
    /// </summary>
    public static class WindowSpans
    {
        /// <summary>
        /// 1 on a bar where the window is open, 0 where it is closed, NaN where nothing is known.
        /// Open = the <see cref="ComponentCompanions.Tint"/> companion at or above the tint
        /// midline when the component has one; otherwise any bar whose value is not NaN.
        /// </summary>
        public static double[] OpenMask(ChartSeries series, ComponentConfig comp)
        {
            var tint = series.GetComponentData(comp.Name + ComponentCompanions.Tint);
            if (tint is { Length: > 0 })
            {
                double mid = comp.TintMidline ?? comp.ColorBaseline;
                var mask = new double[tint.Length];
                for (int i = 0; i < tint.Length; i++)
                    mask[i] = double.IsNaN(tint[i]) ? double.NaN : tint[i] >= mid ? 1 : 0;
                return mask;
            }
            var values = series.GetComponentData(comp.Name);
            var open = new double[values.Length];
            for (int i = 0; i < values.Length; i++) open[i] = double.IsNaN(values[i]) ? 0 : 1;
            return open;
        }

        /// <summary>
        /// Whether the window is open on one bar — <see cref="OpenMask"/> at <paramref name="index"/>
        /// without building the whole mask (playback asks once per bar).
        /// </summary>
        public static bool IsOpenAt(ChartSeries series, ComponentConfig comp, int index)
        {
            if (index < 0) return false;
            var tint = series.GetComponentData(comp.Name + ComponentCompanions.Tint);
            if (tint is { Length: > 0 })
                return index < tint.Length && !double.IsNaN(tint[index]) && tint[index] >= (comp.TintMidline ?? comp.ColorBaseline);
            var values = series.GetComponentData(comp.Name);
            return index < values.Length && !double.IsNaN(values[index]);
        }

        /// <summary>Whether the component has a <see cref="ComponentCompanions.Path"/> with anything in it.</summary>
        public static bool HasPath(ChartSeries? series, ComponentConfig comp) =>
            series?.GetComponentData(comp.Name + ComponentCompanions.Path) is { Length: > 0 } path
            && Array.Exists(path, v => !double.IsNaN(v));

        /// <summary>
        /// The bars a window opens (1) and closes (0) on, NaN elsewhere — the same shape as a
        /// <see cref="ComponentCompanions.Stops"/> companion. "Closes" is the first bar no longer
        /// open; a window open on bar 0 opens there.
        /// </summary>
        public static double[] Edges(double[] mask)
        {
            var edges = new double[mask.Length];
            Array.Fill(edges, double.NaN);
            bool wasOpen = false;
            for (int i = 0; i < mask.Length; i++)
            {
                bool open = mask[i] == 1;
                if (open && !wasOpen) edges[i] = 1;
                else if (!open && wasOpen) edges[i] = 0;
                wasOpen = open;
            }
            return edges;
        }
    }
}
