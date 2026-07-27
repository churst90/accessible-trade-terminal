using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;

using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;
using SkiaSharp;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// How big a marker actually comes out, and how wide the legend is allowed to get.
    ///
    /// <para>
    /// Both of these were caught by looking at a rendered chart rather than by any test, which is
    /// what these are here to change. Marker thickness is authored for a normal zoom; at 330 bars
    /// in view the bars shrink and the glyphs do not, so swing squares came out roughly four times
    /// a candle wide and buried the price action. Separately, the collapsed legend row used the
    /// series' full instance name — metadata name, subtitle, and every parameter value — and
    /// stretched the box across a third of the chart.
    /// </para>
    ///
    /// <para>
    /// The pixel test at the bottom exists for a third reason: <c>SKCanvas.DrawRect</c>'s four-float
    /// overload is <c>(x, y, width, height)</c>, and two call sites in the renderer were passing
    /// <c>(left, top, right, bottom)</c>. An 8px legend swatch and a 4x3px axis tick were both
    /// painting as huge blocks. Nothing threw, and it read as a styling choice.
    /// </para>
    /// </summary>
    public class MarkerSizingTests
    {
        private static RenderContext Context(SKCanvas canvas, int viewportLength, float width = 800f, float density = 1f) =>
            new(
                Canvas: canvas,
                PaneRect: new SKRect(0, 0, width, 400f),
                Data: Array.Empty<Ohlcv>(),
                ViewportStart: 0,
                ViewportLength: viewportLength,
                Min: 0,
                Max: 100,
                IsLogScale: false,
                ItemWidth: width / viewportLength,
                Density: density,
                PaneName: "Main",
                LocalCursorIndex: 0,
                Theme: DefaultTheme);

        // The marker renderers do not consult the theme, but RenderContext requires one. Built
        // from the real service so the test never drifts from ChartTheme's required members.
        private static readonly ChartTheme DefaultTheme =
            new ThemeService(Substitute.For<ISettingsManager>()).Current;

        // ── Size clamping ────────────────────────────────────────────────

        [Fact]
        public void A_marker_never_grows_wider_than_about_two_bars()
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));

            // 330 bars across 800px — the weekly BTC view. Bars are ~2.4px.
            var wide = Context(surface.Canvas, viewportLength: 330);
            float barWidth = wide.Width / 330f;

            // The cap is 1.8 bar widths OR the visibility floor, whichever is larger — at this
            // zoom 1.8 x 2.42px is under the floor, so the floor is what a mark is allowed to be.
            Assert.True(StandardRenderers.ClampMarkerExtent(40f, wide) <= Math.Max(6f, barWidth * 1.8f) + 0.01f,
                "A glyph authored at 40px must shrink when the bars are 2px wide.");
            Assert.True(StandardRenderers.ClampMarkerExtent(40f, wide) < 40f,
                "It must actually have shrunk.");
        }

        [Fact]
        public void Half_extent_and_full_extent_callers_agree_on_the_drawn_size()
        {
            // The bug this pins: a triangle's arrowSize is the WHOLE height, while a square's
            // half, a diamond's half, a cross's arm and a dot's radius are all half of it. Passing
            // all five through one full-extent clamp made those four draw exactly twice as large,
            // which is why the squares and crosses still looked heavy after the first attempt.
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            var wide = Context(surface.Canvas, viewportLength: 330);

            float full = StandardRenderers.ClampMarkerExtent(40f, wide);
            float half = StandardRenderers.ClampMarkerHalfExtent(20f, wide);

            Assert.Equal(full, half * 2f, 3);
        }

        [Fact]
        public void At_a_normal_zoom_the_authored_thickness_is_left_alone()
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));

            // 40 bars across 800px — 20px bars, plenty of room.
            var normal = Context(surface.Canvas, viewportLength: 40);

            Assert.Equal(18f, StandardRenderers.ClampMarkerExtent(18f, normal));
        }

        [Fact]
        public void The_clamp_only_ever_shrinks_a_marker_never_inflates_it()
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            var normal = Context(surface.Canvas, viewportLength: 20);   // 40px bars

            // A deliberately tiny marker stays tiny: the clamp is a ceiling, not a target.
            Assert.Equal(9f, StandardRenderers.ClampMarkerExtent(9f, normal));
        }

        [Fact]
        public void A_marker_never_shrinks_to_invisibility_at_extreme_zoom()
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));

            // 4000 bars across 800px — a fifth of a pixel each. A mark you cannot see is the
            // same as no mark, and worse, because the chart claims to be showing you something.
            var extreme = Context(surface.Canvas, viewportLength: 4000);

            Assert.True(StandardRenderers.ClampMarkerExtent(12f, extreme) >= 6f,
                "Markers must keep a visible floor however far out the view goes.");
        }

        [Fact]
        public void The_ceiling_and_the_floor_both_scale_with_display_density()
        {
            using var surface = SKSurface.Create(new SKImageInfo(1600, 800));

            var oneX = Context(surface.Canvas, viewportLength: 4000, width: 800f, density: 1f);
            var twoX = Context(surface.Canvas, viewportLength: 4000, width: 1600f, density: 2f);

            // On a HiDPI display the floor is in device pixels, so it must double or the marker
            // is physically half the size it is on a standard screen.
            Assert.Equal(StandardRenderers.ClampMarkerExtent(1f, oneX) * 2f,
                         StandardRenderers.ClampMarkerExtent(1f, twoX));
        }

        // ── Legend labels ────────────────────────────────────────────────

        [Theory]
        // The two that caused it — series are named "{metadata name} {each parameter value}".
        [InlineData("Value Deviation (support / resistance zones) 240 5 2 2 1", "Value Deviation")]
        [InlineData("Market Structure (HH/HL/LH/LL) 5 1", "Market Structure")]
        // No subtitle, just trailing parameters.
        [InlineData("Bollinger Bands 20 2", "Bollinger Bands")]
        // Nothing to strip.
        [InlineData("Candles", "Candles")]
        [InlineData("Volume", "Volume")]
        public void ShortSeriesName_drops_the_subtitle_and_the_parameter_values(string input, string expected)
        {
            Assert.Equal(expected, ChartRenderer.ShortSeriesName(input));
        }

        [Fact]
        public void ShortSeriesName_keeps_a_string_parameter_because_it_is_usually_the_point()
        {
            // "Funding Rate" alone would be useless with four of them on one chart.
            Assert.Equal("Funding Rate BTC-USDT-SWAP",
                ChartRenderer.ShortSeriesName("Funding Rate BTC-USDT-SWAP"));
        }

        [Fact]
        public void ShortSeriesName_never_strips_a_name_down_to_nothing()
        {
            // A name that is entirely numeric has nothing to strip TO, so it is left as it is
            // rather than becoming an empty legend row.
            Assert.Equal("50", ChartRenderer.ShortSeriesName("50"));
            Assert.Equal("", ChartRenderer.ShortSeriesName(""));
            Assert.Equal("", ChartRenderer.ShortSeriesName(null));
        }

        [Fact]
        public void The_collapsed_legend_row_uses_the_short_name()
        {
            var s = new ChartSeries();
            s.Config.Name = "Value Deviation (support / resistance zones) 240 5 2 2 1";
            foreach (var t in new[] { ComponentDisplayType.TriangleUp, ComponentDisplayType.Dot, ComponentDisplayType.Diamond })
                s.Components.Add(new ComponentConfig
                {
                    Name = t.ToString(), DisplayName = t.ToString(), DisplayType = t,
                    ColorHex = "#FFFFFF", IsVisible = true,
                });

            var rows = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, 400f, 16f, 4f);

            Assert.Equal("Value Deviation — 3 marks", rows.Single().Label);
        }

        // ── DrawRect geometry ────────────────────────────────────────────

        [Fact]
        public void A_square_marker_paints_a_small_square_not_a_block_running_off_the_pane()
        {
            // Pins SKCanvas.DrawRect's (x, y, width, height) contract at the one call site that
            // draws a filled rectangle for a marker. Passing bounds instead produced a rect as
            // wide as the canvas — which is exactly what the legend swatch and the axis tick were
            // doing elsewhere, silently, for as long as they had existed.
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            surface.Canvas.Clear(SKColors.Black);

            var series = new ChartSeries();
            series.Config.Name = "Test";
            var comp = new ComponentConfig
            {
                Name = "Mark", DisplayName = "Mark",
                DisplayType = ComponentDisplayType.Square,
                ColorHex = "#FFFFFF", Thickness = 6f, IsVisible = true,
            };
            series.Components.Add(comp);

            // One marker at bar 20 of 40; every other bar is NaN.
            var data = Enumerable.Repeat(double.NaN, 40).ToArray();
            data[20] = 50;
            series.Data.ComponentData["Mark"] = data;

            var ctx = Context(surface.Canvas, viewportLength: 40);
            using var paint = new SKPaint { Color = SKColors.White };
            StandardRenderers.RenderSquare(ctx, series, comp, paint);

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, painted = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).Red > 128)
                    {
                        painted++;
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }

            Assert.True(painted > 0, "Nothing was drawn at all.");

            // Thickness 6 at density 1 means a half of 6, so a 12x12 square. 20px bars leave
            // room for a full extent of 36, so the clamp does not bite here.
            int width = maxX - minX + 1, height = maxY - minY + 1;
            Assert.InRange(width, 10, 14);
            Assert.InRange(height, 10, 14);

            // And the whole thing is confined to its own bar, not smeared across the pane.
            Assert.True(painted < 200, $"Square marker painted {painted} pixels — it is not a small glyph.");
        }

        [Fact]
        public void A_square_marker_shrinks_with_the_bars_when_the_view_is_wide()
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            surface.Canvas.Clear(SKColors.Black);

            var series = new ChartSeries();
            series.Config.Name = "Test";
            var comp = new ComponentConfig
            {
                Name = "Mark", DisplayName = "Mark",
                DisplayType = ComponentDisplayType.Square,
                ColorHex = "#FFFFFF", Thickness = 8f, IsVisible = true,
            };
            series.Components.Add(comp);

            var data = Enumerable.Repeat(double.NaN, 330).ToArray();
            data[100] = 50;
            series.Data.ComponentData["Mark"] = data;

            using var paint = new SKPaint { Color = SKColors.White };
            StandardRenderers.RenderSquare(Context(surface.Canvas, viewportLength: 330), series, comp, paint);

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);

            int painted = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).Red > 128) painted++;

            // 800px / 330 bars = 2.42px per bar, so the full extent caps at 6px (the floor beats
            // 1.8 x 2.42 = 4.4) and the square is at most 6x6 = 36 pixels. Unclamped it would have
            // been 16x16 and nearly seven bars wide.
            Assert.True(painted > 0, "The marker vanished — the floor is not doing its job.");
            Assert.True(painted <= 49, $"Square painted {painted} pixels at a 330-bar zoom; the clamp is not applied.");
        }
    }
}
