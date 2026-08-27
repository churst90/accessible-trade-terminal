using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A click resolves against the PLOT, not the canvas.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// The renderer does not draw into the whole canvas: a y-axis column
    /// (<c>theme.AxisWidth</c> 60 × density) runs down the right, an x-axis strip along the
    /// bottom, and indicator panes take a share of the height. Bars are laid across
    /// <c>width − axisWidth</c> and the price range is mapped into a main pane of
    /// <c>height − axisHeight − Σ indicatorHeights</c>.
    /// </para>
    ///
    /// <para>
    /// Every pointer mapping was handed the raw canvas rect from
    /// <c>keyboard.js</c>'s <c>getBoundingClientRect()</c> of <c>chart-interact-zone</c>, which
    /// is the entire chart div:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Horizontal.</b> On a 1280 px chart with a 120-bar viewport, a click on the
    /// rightmost candle (x ≈ 1220, truly bar 119) resolved to
    /// <c>round(1220/1280 × 119) = 113</c> — six bars out, with the error growing linearly
    /// left to right to about 5% of the viewport. It affected click-to-select, the hover
    /// crosshair readout, Shift+click range measurement, right-click "play from here", and
    /// every drawing anchor.</item>
    /// <item><b>Vertical.</b> With a volume pane on screen (equal split, main pane ≈ 47% of
    /// the canvas) plus an x-axis strip, a click at the visual bottom of the price pane
    /// returned <c>Min + 0.53 × (Max − Min)</c>. Every mouse-placed drawing anchor landed at
    /// a wrong price and the horizontal-line tool drew where the user did not click.</item>
    /// </list>
    ///
    /// <para>
    /// The tell that this was a bug rather than a convention:
    /// <c>ChartHitTester.HitTest</c> resolved pane BANDS correctly from
    /// <c>IPaneLayoutService</c> — and then spread the viewport across the full width anyway.
    /// So two code paths on the same click disagreed about what the user had pointed at, one
    /// of them pixel-correct vertically and six bars out horizontally.
    /// </para>
    /// </summary>
    public class PointerToPlotMappingTests
    {
        // A 1280×720 canvas with a 60 px y-axis and a 30 px x-axis strip.
        private const double CanvasW = 1280;
        private const double CanvasH = 720;
        private const float AxisWFrac = 60f / 1280f;
        private const float AxisHFrac = 30f / 720f;

        // ── Horizontal ───────────────────────────────────────────────────────

        [Fact]
        public void The_rightmost_candle_resolves_to_the_last_bar_in_the_viewport()
        {
            // x is the centre of the last bar's slot in the PLOT area.
            double plotW = ChartMath.PlotWidth(CanvasW, AxisWFrac);
            Assert.Equal(1220, plotW, 0);

            int last = ChartMath.MapXToIndex(plotW, plotW, startIndex: 100, length: 120);
            Assert.Equal(100 + 119, last);
        }

        [Fact]
        public void Mapping_against_the_whole_canvas_is_six_bars_out_at_the_right_edge()
        {
            // The defect, stated as arithmetic so the fix cannot be mistaken for a rounding
            // preference. A click at the right edge of the PLOT is bar 119 of the viewport.
            double plotW = ChartMath.PlotWidth(CanvasW, AxisWFrac);

            int correct = ChartMath.MapXToIndex(plotW, plotW, startIndex: 0, length: 120);
            int naive   = ChartMath.MapXToIndex(plotW, CanvasW, startIndex: 0, length: 120);

            Assert.Equal(119, correct);
            Assert.Equal(113, naive);
        }

        [Fact]
        public void The_error_grows_left_to_right_and_is_zero_at_the_left_edge()
        {
            double plotW = ChartMath.PlotWidth(CanvasW, AxisWFrac);

            Assert.Equal(
                ChartMath.MapXToIndex(0, plotW, 0, 120),
                ChartMath.MapXToIndex(0, CanvasW, 0, 120));

            // ...and is worst where the user is most likely to click: the live edge.
            int atRight = ChartMath.MapXToIndex(plotW, plotW, 0, 120)
                        - ChartMath.MapXToIndex(plotW, CanvasW, 0, 120);
            Assert.True(atRight >= 5, $"expected the right-edge error to be several bars, was {atRight}");
        }

        [Fact]
        public void PlotWidth_refuses_an_absurd_axis_fraction_rather_than_collapsing_the_plot()
        {
            // A bad fraction must not make every click resolve to bar 0.
            Assert.Equal(CanvasW * 0.5, ChartMath.PlotWidth(CanvasW, 0.99f), 6);
            Assert.Equal(CanvasW, ChartMath.PlotWidth(CanvasW, -1f), 6);
        }

        // ── Vertical ─────────────────────────────────────────────────────────

        /// <summary>Main pane down to 60% of the canvas, then a volume pane below it.</summary>
        private static readonly (string BelowPaneName, float DividerFraction)[] VolumePaneBelow =
            { ("Volume", 0.60f) };

        [Fact]
        public void The_bottom_of_the_price_pane_is_the_bottom_of_the_price_range()
        {
            // The main pane runs 0 .. 0.60 × 720 = 432 px.
            double atPaneBottom = ChartMath.MapYToPriceInPane(
                432, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false);

            // Precision 3, not 6: DividerFraction and the axis fractions are `float`, so
            // 0.60f x 720 is 432.0000171661377 and the price comes back 100.000004. Sub-pixel
            // is the tolerance that means anything here; demanding more is testing IEEE754.
            Assert.Equal(100, atPaneBottom, 3);
        }

        [Fact]
        public void The_top_of_the_price_pane_is_the_top_of_the_price_range()
        {
            Assert.Equal(200, ChartMath.MapYToPriceInPane(
                0, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false), 3);
        }

        [Fact]
        public void Mapping_against_the_whole_canvas_returns_a_price_from_halfway_up_the_range()
        {
            // The defect, again as arithmetic. At the visual bottom of the price pane the
            // naive mapping returns Min + (1 − 432/720) × (Max − Min) = 100 + 0.4 × 100.
            double naive = ChartMath.MapYToPrice(432, CanvasH, min: 100, max: 200, isLog: false);
            Assert.Equal(140, naive, 6);

            double correct = ChartMath.MapYToPriceInPane(
                432, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false);
            Assert.Equal(100, correct, 3);
        }

        [Fact]
        public void A_cursor_over_the_x_axis_strip_has_no_price()
        {
            // Reporting Min there would be a lie that reads as a real level.
            double overAxis = ChartMath.MapYToPriceInPane(
                CanvasH - 5, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false);

            Assert.True(double.IsNaN(overAxis));
        }

        [Fact]
        public void With_no_panes_the_mapping_still_excludes_the_x_axis_strip()
        {
            // No dividers means Main owns the whole PLOT — which is still not the whole canvas.
            double plotBottom = CanvasH * (1 - AxisHFrac);

            Assert.Equal(100, ChartMath.MapYToPriceInPane(
                plotBottom, CanvasH, dividers: null, AxisHFrac, min: 100, max: 200, isLog: false), 3);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(125.5)]
        [InlineData(199.9)]
        public void Price_to_canvas_Y_round_trips_through_the_pane_mapping(double price)
        {
            // The forward and inverse must agree, or an anchor HANDLE sits where the drawing
            // is not — and with a 10 px grab tolerance that is a drawing the user cannot
            // pick up with the mouse at all.
            double y = ChartMath.PriceToCanvasY(
                price, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false);
            double back = ChartMath.MapYToPriceInPane(
                y, CanvasH, VolumePaneBelow, AxisHFrac, min: 100, max: 200, isLog: false);

            Assert.Equal(price, back, 3);
        }

        [Fact]
        public void The_pane_band_walk_finds_the_volume_pane_below_the_divider()
        {
            // Vacuity check for everything above: if the band walk collapsed to "Main owns
            // everything", the price assertions would pass for the wrong reason on a chart
            // that has no indicator panes and prove nothing about one that does.
            var band = ChartMath.PaneBandPx(500, CanvasH, VolumePaneBelow, AxisHFrac);

            Assert.NotNull(band);
            Assert.Equal(432, band!.Value.Top, 3);      // starts at the divider
            Assert.Equal(690, band.Value.Bottom, 3);    // ends at the x-axis strip
        }
    }
}
