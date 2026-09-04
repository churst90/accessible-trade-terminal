using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Draws the chart onto a canvas: one chart, full size, every frame.
    ///
    /// <para>
    /// It exists because a frame is not just <c>ChartRenderer.Render</c> — the formations layer
    /// has to be resolved from the pattern cache against the chart's identity and the whole
    /// series, which the renderer cannot do because it only ever sees the visible slice, and
    /// detecting on a moving window would make formations appear and disappear as the user pans.
    /// Two heads draw the chart (the WebHost's offscreen surface and the MAUI head's
    /// <c>SKCanvasView</c>), so that resolution lives here rather than being written twice.
    /// </para>
    ///
    /// <para>
    /// This replaces <c>SplitViewCoordinator</c>, which drew a second tab's frozen snapshot beside
    /// the active chart. Split view is gone: the second pane was READ-ONLY by construction —
    /// keyboard navigation, speech, sonification and trading all continued to address the active
    /// tab — so the terminal drew a chart it could not say anything about, and the one user who
    /// most needs to compare two markets was the one user it did not serve. The comparison itself
    /// is worth having; the shape it should take is an OVERLAY series on the existing axis, which
    /// Page Up / Page Down already reaches, Up / Down already walks, and the sonifier already
    /// plays against the same viewport.
    /// </para>
    /// </summary>
    public interface IChartFrameRenderer
    {
        /// <summary>Draws the active chart across the whole canvas.</summary>
        void Render(SKCanvas canvas, int width, int height, WorkspaceState state, float density);
    }

    /// <inheritdoc cref="IChartFrameRenderer"/>
    public sealed class ChartFrameRenderer : IChartFrameRenderer
    {
        // Nullable so a caller can construct one without standing up the renderer's six-service
        // dependency graph; a null renderer resolves the formations and draws nothing.
        private readonly ChartRenderer? _renderer;
        private readonly Analysis.IChartPatternCache? _patternCache;
        private readonly IAppSettings? _settings;

        public ChartFrameRenderer(
            ChartRenderer? renderer,
            Analysis.IChartPatternCache? patternCache = null,
            IAppSettings? settings = null)
        {
            _renderer = renderer;
            _patternCache = patternCache;
            _settings = settings;
        }

        public void Render(SKCanvas canvas, int width, int height, WorkspaceState state, float density)
        {
            if (width <= 0 || height <= 0) return;

            // A render throwing must never escape onto the Blazor dispatcher — an unhandled
            // exception there tears down the whole circuit, which kills keyboard input and
            // freezes the session. Keep the last good frame instead.
            try
            {
                _renderer?.Render(
                    canvas, width, height, state.Data, state.ActiveSeries, state.CurrentDataIndex,
                    state.ViewportStartIndex, state.ViewportLength, state.ViewportRange, state.PaneRanges,
                    state.IsHeikinAshi, state.IsLogScale, density, state.PaneHeightRatios,
                    state.RightMarginBars, Formations(state.Identity, state.Data));
            }
            catch { /* keep the last good frame */ }
        }

        /// <summary>
        /// The formations to draw this frame, or null when the drawing is switched off.
        ///
        /// <para>
        /// Resolved here rather than inside the renderer because only this layer has the whole
        /// series and the chart's identity. The renderer sees just the visible slice, and
        /// detecting on a moving window would make formations appear and disappear as the user
        /// pans.
        /// </para>
        /// </summary>
        private IReadOnlyList<Analysis.ChartPattern>? Formations(ChartIdentity id, IReadOnlyList<Ohlcv>? data)
        {
            if (_patternCache == null || data == null) return null;
            if (_settings?.ShowChartPatternVisuals != true) return null;
            return _patternCache.For(id, data);
        }
    }
}
