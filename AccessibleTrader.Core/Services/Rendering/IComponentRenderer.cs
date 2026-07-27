using SkiaSharp;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Encapsulates all state required to render a single chart component.
    /// This prevents "argument bloat" in the rendering strategy methods.
    /// </summary>
    public record RenderContext(
        SKCanvas Canvas,
        SKRect PaneRect,
        IReadOnlyList<Ohlcv> Data,
        int ViewportStart,
        int ViewportLength,
        double Min,
        double Max,
        bool IsLogScale,
        float ItemWidth,
        float Density,
        string PaneName,
        int LocalCursorIndex,
        ChartTheme Theme,
        /// <summary>
        /// When null: render only main-area components (SubPaneName is null/empty).
        /// When non-null: render only components whose SubPaneName matches this value.
        /// </summary>
        string? SubPaneFilter = null,
        /// <summary>
        /// The whole stacked-pane area (price + every indicator pane), or null to mean "this pane".
        ///
        /// <para>
        /// Only the background gradient uses it. Anchoring the gradient to each PaneRect made every
        /// pane repeat the full light-to-dark fade, so a chart with a volume pane showed a hard
        /// seam where the volume pane restarted at the light end. The fade has to be computed
        /// against the whole canvas and merely CLIPPED to each pane.
        /// </para>
        /// </summary>
        SKRect? ChartRect = null
    ) {
        public float Width => PaneRect.Width;
        public float Height => PaneRect.Height;
        public float Top => PaneRect.Top;
        public float Bottom => PaneRect.Bottom;
        public IReadOnlyList<Ohlcv> VisibleData => Data; // Backwards compatibility for some layers

        /// <summary>The gradient span: the whole chart when known, this pane otherwise.</summary>
        public SKRect GradientRect => ChartRect ?? PaneRect;
    }

    public interface IComponentRenderer
    {
        void Render(RenderContext ctx, ChartSeries series);
    }
}
