using SkiaSharp;
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
        // When null: render only main-area components (SubPaneName is null/empty).
        // When non-null: render only components whose SubPaneName matches this value.
        string? SubPaneFilter = null,
        // The whole stacked-pane area (price + every indicator pane), or null to mean "this pane".
        //
        //
        // Only the background gradient uses it. Anchoring the gradient to each PaneRect made every
        // pane repeat the full light-to-dark fade, so a chart with a volume pane showed a hard
        // seam where the volume pane restarted at the light end. The fade has to be computed
        // against the whole canvas and merely CLIPPED to each pane.
        SKRect? ChartRect = null,
        // A rect something else will paint OVER this pane after the layers run — today the
        // pane legend, which the renderer measures before the pane draws. Layers that place
        // text (the formation layer) keep their labels out of it. Null means nothing to avoid.
        SKRect? Avoid = null,
        // The pane's whole frame when it is taller than PaneRect: the plot PLUS the status band
        // above it (2026-10-17), which the background fills and borders so the band reads as the
        // top of the pane. Values are mapped into PaneRect only. Null means PaneRect.
        SKRect? FrameRect = null,
        // Text another layer has already printed on this pane — today the value labels
        // (ValueLabelRenderer.Plates) — which the formation layer's label placement treats as
        // covered ground, like candle ink. Null means nothing to avoid.
        IReadOnlyList<SKRect>? TextObstacles = null
    ) {
        public float Width => PaneRect.Width;
        public float Height => PaneRect.Height;
        public float Top => PaneRect.Top;
        public float Bottom => PaneRect.Bottom;
        public IReadOnlyList<Ohlcv> VisibleData => Data; // Backwards compatibility for some layers

        /// <summary>What the background fills and borders: the frame when there is one, the pane otherwise.</summary>
        public SKRect BackgroundRect => FrameRect ?? PaneRect;

        /// <summary>The gradient span: the whole chart when known, this pane otherwise.</summary>
        public SKRect GradientRect => ChartRect ?? PaneRect;
    }

    public interface IComponentRenderer
    {
        void Render(RenderContext ctx, ChartSeries series);
    }
}
