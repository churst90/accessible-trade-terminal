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
        string? SubPaneFilter = null
    ) {
        public float Width => PaneRect.Width;
        public float Height => PaneRect.Height;
        public float Top => PaneRect.Top;
        public float Bottom => PaneRect.Bottom;
        public IReadOnlyList<Ohlcv> VisibleData => Data; // Backwards compatibility for some layers
    }

    public interface IComponentRenderer
    {
        void Render(RenderContext ctx, ChartSeries series);
    }
}
