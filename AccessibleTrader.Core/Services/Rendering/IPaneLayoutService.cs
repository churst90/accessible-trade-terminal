namespace AccessibleTrader.Core.Services.Rendering
{
    /// <summary>
    /// Shared service that ChartRenderer populates after each render pass and
    /// ChartArea.razor reads to position transparent pane-divider drag handles.
    /// Positions are expressed as fractions (0.0–1.0) of the total canvas size.
    ///
    /// <para>It is also the seam every pointer-to-data mapping goes through, because the
    /// renderer does not draw into the whole canvas: an x-axis strip sits along the bottom and
    /// a y-axis column along the right, and the plot area is what is left. Anything that turns
    /// a pixel back into a bar or a price has to subtract the same two strips the renderer
    /// added, and this is where their sizes live.</para>
    /// </summary>
    public interface IPaneLayoutService
    {
        /// <summary>
        /// Ordered list of dividers.  Each entry carries the pane that sits BELOW
        /// the divider so that a drag dispatches <see cref="Sdk.Models.ResizePaneAction"/> for
        /// the correct pane name.
        /// </summary>
        IReadOnlyList<(string BelowPaneName, float DividerFraction)> Dividers { get; }

        /// <summary>Fraction of the total canvas height consumed by the x-axis strip.</summary>
        float AxisHeightFraction { get; }

        /// <summary>
        /// Fraction of the total canvas width consumed by the y-axis column on the right.
        ///
        /// <para>Added 2026-08-27 with the pointer-mapping fix. <c>ChartMath.MapXToIndex</c>
        /// spread the viewport across the FULL canvas width while the renderer lays bars across
        /// <c>width - axisWidth</c>, so on a 1280 px chart with a 120-bar viewport a click on
        /// the rightmost candle resolved to bar 113 rather than 119 — six bars out, with the
        /// error growing linearly left to right to about 5% of the viewport. It affected
        /// click-to-select, the hover crosshair readout, Shift+click range measurement,
        /// right-click "play from here", and every drawing anchor. The project already knew the
        /// number: <c>ChartArea.razor</c> hardcodes <c>right: 65px</c> to leave room for the
        /// y-axis.</para>
        /// </summary>
        float AxisWidthFraction { get; }

        // MainLegendFraction (the price pane's boxed legend, which the hover readout box sat
        // beside) was retired on 2026-10-17 with both: Cody's decision, TradingView status line.
        // Each pane's status lines are DOM text placed from the dividers above.

        /// <summary>
        /// Each pane's STATUS BAND, by pane name ("Main" and each divider's pane): the strip at the
        /// pane's top that its status lines sit in and its plot starts below (2026-10-17). A pane
        /// whose band has no lines is listed with none. Empty until a frame has been drawn.
        ///
        /// <para>
        /// The renderer maps a pane's values into the pane MINUS this band, so anything that turns
        /// a pointer back into a value, or a value into a pointer position, has to start the pane's
        /// plot at the same place: <see cref="ChartMath.MapYToPriceInPane"/>,
        /// <see cref="ChartMath.PriceToCanvasY"/> and <c>ChartHitTester</c> take it, and ChartArea
        /// gives each pane's status block no more lines than its band has room for.
        /// </para>
        /// </summary>
        IReadOnlyDictionary<string, PaneStatusBand> StatusBands { get; }

        /// <summary>Called by ChartRenderer after pane heights are calculated.</summary>
        void Update(
            IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction,
            float axisWidthFraction,
            IReadOnlyDictionary<string, PaneStatusBand>? statusBands = null);

        /// <summary>Fires whenever <see cref="Update"/> is called with changed values.</summary>
        event Action? LayoutChanged;
    }

    /// <summary>
    /// A pane's status band: how many status lines it has room for, and its height as a fraction
    /// of the canvas height (0 with no lines). See <see cref="IPaneLayoutService.StatusBands"/>.
    /// </summary>
    public readonly record struct PaneStatusBand(int Lines, float HeightFraction);

    public class PaneLayoutService : IPaneLayoutService
    {
        private static readonly IReadOnlyDictionary<string, PaneStatusBand> NoBands = new Dictionary<string, PaneStatusBand>();

        private IReadOnlyList<(string, float)> _dividers = Array.Empty<(string, float)>();
        private float _axisHeightFraction;
        private float _axisWidthFraction;
        private IReadOnlyDictionary<string, PaneStatusBand> _statusBands = NoBands;

        public IReadOnlyList<(string BelowPaneName, float DividerFraction)> Dividers => _dividers;
        public float AxisHeightFraction => _axisHeightFraction;
        public float AxisWidthFraction => _axisWidthFraction;
        public IReadOnlyDictionary<string, PaneStatusBand> StatusBands => _statusBands;

        public event Action? LayoutChanged;

        public void Update(
            IReadOnlyList<(string BelowPaneName, float DividerFraction)> dividers,
            float axisHeightFraction,
            float axisWidthFraction,
            IReadOnlyDictionary<string, PaneStatusBand>? statusBands = null)
        {
            _dividers = dividers;
            _axisHeightFraction = axisHeightFraction;
            _axisWidthFraction = axisWidthFraction;
            _statusBands = statusBands ?? NoBands;
            LayoutChanged?.Invoke();
        }
    }
}
