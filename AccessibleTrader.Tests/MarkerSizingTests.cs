using System.Reflection;
using AccessibleTrader.Core.Services;

using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;
using SkiaSharp;

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
        private static RenderContext Context(SKCanvas canvas, int viewportLength, float width = 800f, float density = 1f,
            IReadOnlyList<Ohlcv>? data = null) =>
            new(
                Canvas: canvas,
                PaneRect: new SKRect(0, 0, width, 400f),
                Data: data ?? Array.Empty<Ohlcv>(),
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

        private static ComponentDisplayType Shape(string name) => Enum.Parse<ComponentDisplayType>(name);

        private static void SetMarkerText(ComponentConfig component, string? text)
        {
            var property = typeof(ComponentConfig).GetProperty("MarkerText");
            Assert.NotNull(property);
            property!.SetValue(component, text);
        }

        private static void RenderNewShape(string name, RenderContext ctx, ChartSeries series, ComponentConfig component, SKPaint paint)
        {
            var renderer = typeof(StandardRenderers).GetMethod("Render" + name, BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(renderer);
            renderer!.Invoke(null, new object?[] { ctx, series, component, paint });
        }

        private static int CountPixels(SKBitmap bitmap, Func<SKColor, bool> matches, int left, int top, int right, int bottom)
        {
            int count = 0;
            for (int y = Math.Max(0, top); y < Math.Min(bitmap.Height, bottom); y++)
                for (int x = Math.Max(0, left); x < Math.Min(bitmap.Width, right); x++)
                    if (matches(bitmap.GetPixel(x, y))) count++;
            return count;
        }

        private static (int MinY, int MaxY, int Count) InkBounds(SKBitmap bitmap, SKColor ink)
        {
            int minY = int.MaxValue, maxY = int.MinValue, count = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (Math.Abs(pixel.Red - ink.Red) > 35 || Math.Abs(pixel.Green - ink.Green) > 35 ||
                        Math.Abs(pixel.Blue - ink.Blue) > 35 || pixel.Alpha == 0) continue;
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    count++;
                }
            return (minY, maxY, count);
        }

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

        // ── Status line titles ───────────────────────────────────────────
        //
        // These pinned ChartRenderer.ShortSeriesName, the boxed legend's label rule, until the
        // legend was retired on 2026-10-17 (Cody's decision: TradingView status line). The status
        // line keeps the same title rule and shows what it used to strip as the line's inputs, so
        // the cases are ported to ChartStatusLine.TitleAndInputs, inputs asserted too.

        [Theory]
        // The two that caused it.
        [InlineData("Value Deviation (support / resistance zones) 240 5 2 2 1", "Value Deviation", "240 5 2 2 1")]
        [InlineData("Market Structure (HH/HL/LH/LL) 5 1", "Market Structure", "5 1")]
        // No subtitle, just trailing parameters.
        [InlineData("Bollinger Bands 20 2", "Bollinger Bands", "20 2")]
        // Nothing to strip.
        [InlineData("Candles", "Candles", "")]
        [InlineData("Volume", "Volume", "")]
        public void TitleAndInputs_drops_the_subtitle_and_splits_off_the_parameter_values(string input, string title, string inputs)
        {
            Assert.Equal((title, inputs), ChartStatusLine.TitleAndInputs(input));
        }

        [Fact]
        public void TitleAndInputs_keeps_a_string_parameter_in_the_title_because_it_is_usually_the_point()
        {
            // "Funding Rate" alone would be useless with four of them on one chart.
            Assert.Equal(("Funding Rate BTC-USDT-SWAP", ""),
                ChartStatusLine.TitleAndInputs("Funding Rate BTC-USDT-SWAP"));
        }

        [Fact]
        public void TitleAndInputs_never_strips_a_name_down_to_nothing()
        {
            // A name that is entirely numeric has nothing to strip TO, so it is left as it is
            // rather than becoming an empty status line.
            Assert.Equal(("50", ""), ChartStatusLine.TitleAndInputs("50"));
            Assert.Equal(("", ""), ChartStatusLine.TitleAndInputs(""));
            Assert.Equal(("", ""), ChartStatusLine.TitleAndInputs(null));
        }

        [Fact]
        public void The_collapsed_marker_line_uses_the_short_name()
        {
            var s = new ChartSeries();
            s.Config.Name = "Value Deviation (support / resistance zones) 240 5 2 2 1";
            foreach (var t in new[] { ComponentDisplayType.TriangleUp, ComponentDisplayType.Dot, ComponentDisplayType.Diamond })
                s.Components.Add(new ComponentConfig
                {
                    Name = t.ToString(), DisplayName = t.ToString(), DisplayType = t,
                    ColorHex = "#FFFFFF", IsVisible = true,
                });

            var theme = new ThemeService(Substitute.For<ISettingsManager>()).Current;
            var line = ChartStatusLine.Build("Main", new[] { s }, 0,
                new ChartStatusLine.Context(theme, ChartStatusLine.DefaultBackingAlpha, null, null),
                400f, 2000f, 17f, t => t.Length * 7f).Lines.Single();

            Assert.Equal("Value Deviation", line.Title);
            Assert.Equal("3 mark types", line.Note);
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

        [Fact]
        public void New_marker_shape_ordinals_follow_ValueLabel_without_renumbering_it()
        {
            Assert.Equal(25, (int)ComponentDisplayType.ValueLabel);
            Assert.Equal("ValueLabel", Enum.GetName(typeof(ComponentDisplayType), 25));
            Assert.Equal("CheckBox", Enum.GetName(typeof(ComponentDisplayType), 26));
            Assert.Equal("RefreshBox", Enum.GetName(typeof(ComponentDisplayType), 27));
            Assert.Equal("TriangleUpRing", Enum.GetName(typeof(ComponentDisplayType), 28));
        }

        [Theory]
        [InlineData(ThemeType.Blackout)]
        [InlineData(ThemeType.Paper)]
        public void CheckBox_has_component_colour_and_white_check_pixels_while_plain_Square_has_no_white(ThemeType themeType)
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            var theme = DbsiChartHarness.Themes(themeType).Current;
            surface.Canvas.Clear(theme.Background);
            var color = SKColor.Parse("#00A040");
            var series = new ChartSeries();
            var marks = new[]
            {
                new ComponentConfig { Name = "Check", DisplayName = "Check", DisplayType = Shape("CheckBox"), ColorHex = "#00A040", Thickness = 6f },
                new ComponentConfig { Name = "Square", DisplayName = "Square", DisplayType = ComponentDisplayType.Square, ColorHex = "#00A040", Thickness = 6f },
            };
            foreach (var comp in marks)
            {
                var data = Enumerable.Repeat(double.NaN, 40).ToArray();
                data[comp.Name == "Check" ? 10 : 30] = 50;
                series.Data.ComponentData[comp.Name] = data;
            }
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var ctx = Context(surface.Canvas, viewportLength: 40) with { Theme = theme };

            RenderNewShape("CheckBox", ctx, series, marks[0], paint);
            StandardRenderers.RenderSquare(ctx, series, marks[1], paint);

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            int checkGreen = CountPixels(bitmap, c => Math.Abs(c.Red - color.Red) < 20 && Math.Abs(c.Green - color.Green) < 20 && Math.Abs(c.Blue - color.Blue) < 20,
                206, 196, 214, 204);
            int checkWhite = CountPixels(bitmap, c => c.Red > 230 && c.Green > 230 && c.Blue > 230,
                206, 196, 214, 204);
            int squareWhite = CountPixels(bitmap, c => c.Red > 230 && c.Green > 230 && c.Blue > 230,
                606, 196, 614, 204);

            Assert.True(checkGreen > 0, "The rounded box must use the component colour.");
            Assert.True(checkWhite > 0, "The check mark must stay white inside its coloured box.");
            Assert.Equal(0, squareWhite);
        }

        [Theory]
        [InlineData(ThemeType.Blackout)]
        [InlineData(ThemeType.Paper)]
        public void RefreshBox_has_component_colour_and_white_circular_arrow_pixels_inside_its_box(ThemeType themeType)
        {
            using var surface = SKSurface.Create(new SKImageInfo(800, 400));
            var theme = DbsiChartHarness.Themes(themeType).Current;
            surface.Canvas.Clear(theme.Background);
            var color = SKColor.Parse("#00A040");
            var series = new ChartSeries();
            var comp = new ComponentConfig
            {
                Name = "Refresh", DisplayName = "Refresh", DisplayType = Shape("RefreshBox"),
                ColorHex = "#00A040", Thickness = 6f,
            };
            var data = Enumerable.Repeat(double.NaN, 40).ToArray();
            data[20] = 50;
            series.Data.ComponentData[comp.Name] = data;
            using var paint = new SKPaint { Color = color, IsAntialias = true };

            RenderNewShape("RefreshBox", Context(surface.Canvas, 40) with { Theme = theme }, series, comp, paint);

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            int green = CountPixels(bitmap, c => Math.Abs(c.Red - color.Red) < 20 && Math.Abs(c.Green - color.Green) < 20 && Math.Abs(c.Blue - color.Blue) < 20,
                406, 196, 414, 204);
            int white = CountPixels(bitmap, c => c.Red > 230 && c.Green > 230 && c.Blue > 230,
                406, 196, 414, 204);

            Assert.True(green > 0, "The rounded box must use the component colour.");
            Assert.True(white > 0, "The refresh arrows must stay white inside their coloured box.");
        }

        [Theory]
        [InlineData(MarkerAnchor.BelowBar)]
        [InlineData(MarkerAnchor.AboveBar)]
        public void TriangleUpRing_places_its_lettered_ring_one_marker_height_beyond_the_triangle(MarkerAnchor anchor)
        {
            var bars = Enumerable.Range(0, 40)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 50, 55, 45, 50, 1000))
                .ToList();
            var values = Enumerable.Repeat(double.NaN, 40).ToArray();
            values[20] = 50;
            var color = SKColor.Parse("#00E676");
            var ringComp = new ComponentConfig
            {
                Name = "Ring", DisplayName = "Ring", DisplayType = Shape("TriangleUpRing"),
                MarkerAnchor = anchor, ColorHex = "#00E676", Thickness = 4f,
            };
            SetMarkerText(ringComp, "D");
            var plainComp = new ComponentConfig
            {
                Name = "Triangle", DisplayName = "Triangle", DisplayType = ComponentDisplayType.TriangleUp,
                MarkerAnchor = anchor, ColorHex = "#00E676", Thickness = 4f,
            };
            var ringSeries = new ChartSeries();
            ringSeries.Data.ComponentData[ringComp.Name] = values;
            var plainSeries = new ChartSeries();
            plainSeries.Data.ComponentData[plainComp.Name] = values;

            (int MinY, int MaxY, int Count) DrawBounds(bool withRing)
            {
                using var surface = SKSurface.Create(new SKImageInfo(800, 400));
                surface.Canvas.Clear(SKColors.Black);
                var ctx = Context(surface.Canvas, 40, data: bars);
                using var paint = new SKPaint { Color = color, IsAntialias = true };
                if (withRing) RenderNewShape("TriangleUpRing", ctx, ringSeries, ringComp, paint);
                else StandardRenderers.RenderTriangleUp(ctx, plainSeries, plainComp, paint);
                using var image = surface.Snapshot();
                using var bitmap = SKBitmap.FromImage(image);
                return InkBounds(bitmap, color);
            }

            var composite = DrawBounds(withRing: true);
            var triangle = DrawBounds(withRing: false);
            using var ySurface = SKSurface.Create(new SKImageInfo(800, 400));
            var ctxForY = Context(ySurface.Canvas, 40, data: bars);
            float markerY = StandardRenderers.ResolveMarkerY(ctxForY, ringComp, 20, 50);
            float markerHeight = StandardRenderers.ClampMarkerExtent(Math.Max(ringComp.Thickness * 3f, 6f), ctxForY);
            Assert.True(composite.Count > triangle.Count, "The ring and letter must add visible ink.");
            if (anchor == MarkerAnchor.BelowBar)
            {
                Assert.True(triangle.MaxY <= markerY + 1f, "A plain upward triangle has no ink beyond its base.");
                Assert.True(composite.MaxY - triangle.MaxY >= markerHeight,
                    $"The ring's far edge extends only {composite.MaxY - triangle.MaxY}px beyond the triangle; expected at least {markerHeight}px.");
            }
            else
            {
                Assert.True(triangle.MinY >= markerY - markerHeight - 1f, "A plain upward triangle has no ink above its tip.");
                Assert.True(triangle.MinY - composite.MinY >= markerHeight,
                    $"The ring's far edge extends only {triangle.MinY - composite.MinY}px beyond the triangle; expected at least {markerHeight}px.");
            }
        }

        [Fact]
        public void CF_shape_review_PNGs_are_written_only_when_requested()
        {
            string? directory = Environment.GetEnvironmentVariable("AT_CF_SHAPES_PNG_DIR");
            if (string.IsNullOrWhiteSpace(directory)) return;

            Directory.CreateDirectory(directory);
            RenderReviewPng(directory, ThemeType.Blackout, "cf-shapes-blackout-2x.png");
            RenderReviewPng(directory, ThemeType.Paper, "cf-shapes-paper-2x.png");
        }

        private static void RenderReviewPng(string directory, ThemeType themeType, string fileName)
        {
            const int width = 1600, height = 800, count = 40;
            const float density = 2f;
            var theme = DbsiChartHarness.Themes(themeType).Current;
            var bars = Enumerable.Range(0, count)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), 48, 55, 45, 52, 1000))
                .ToList();
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(theme.Background);
            var ctx = new RenderContext(canvas, new SKRect(0, 0, width, height), bars,
                ViewportStart: 0, ViewportLength: count, Min: 0, Max: 100, IsLogScale: false,
                ItemWidth: width / (float)count, Density: density, PaneName: "Main", LocalCursorIndex: 25, Theme: theme);

            var candles = new ChartSeries();
            candles.Components.Add(new ComponentConfig { Name = "body", DisplayType = ComponentDisplayType.Candle, Role = ComponentRole.Body });
            using (var candlePaint = new SKPaint { Color = theme.CandleBullishBody, IsAntialias = true })
                StandardRenderers.RenderCandles(ctx, candles, candlePaint);

            var series = new ChartSeries();
            ComponentConfig Add(string name, ComponentDisplayType shape, string color, MarkerAnchor anchor, int bar, double value,
                float thickness = 3f, string? markerText = null)
            {
                var component = new ComponentConfig
                {
                    Name = name, DisplayName = name, DisplayType = shape, ColorHex = color,
                    MarkerAnchor = anchor, Thickness = thickness, MarkerText = markerText,
                };
                series.Components.Add(component);
                var data = Enumerable.Repeat(double.NaN, count).ToArray();
                data[bar] = value;
                series.Data.ComponentData[name] = data;
                return component;
            }

            var check = Add("CheckBox", ComponentDisplayType.CheckBox, "#00E676", MarkerAnchor.Value, 5, 50);
            var refresh = Add("RefreshBox", ComponentDisplayType.RefreshBox, "#78909C", MarkerAnchor.Value, 12, 50);
            var ring = Add("TriangleUpRing", ComponentDisplayType.TriangleUpRing, "#00E676", MarkerAnchor.BelowBar, 19, 45, markerText: "D");
            var stackRing = Add("DCL ring D", ComponentDisplayType.TriangleUpRing, "#00E676", MarkerAnchor.BelowBar, 25, 45, markerText: "D");
            var countLabel = Add("DCL count 49D", ComponentDisplayType.ValueLabel, "#00E676", MarkerAnchor.AboveBar, 25, 49, markerText: "D");
            var confirmed = Add("DCL confirmed", ComponentDisplayType.CheckBox, "#00E676", MarkerAnchor.AboveBar, 31, 50);
            var pending = Add("DCL pending", ComponentDisplayType.RefreshBox, "#78909C", MarkerAnchor.AboveBar, 36, 50);

            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = SKColor.Parse(check.ColorHex);
            StandardRenderers.RenderCheckBox(ctx, series, check, paint);
            paint.Color = SKColor.Parse(refresh.ColorHex);
            StandardRenderers.RenderRefreshBox(ctx, series, refresh, paint);
            paint.Color = SKColor.Parse(ring.ColorHex);
            StandardRenderers.RenderTriangleUpRing(ctx, series, ring, paint);
            paint.Color = SKColor.Parse(stackRing.ColorHex);
            StandardRenderers.RenderTriangleUpRing(ctx, series, stackRing, paint);
            ValueLabelRenderer.Render(ctx, series, countLabel);
            paint.Color = SKColor.Parse(confirmed.ColorHex);
            StandardRenderers.RenderCheckBox(ctx, series, confirmed, paint);
            paint.Color = SKColor.Parse(pending.ColorHex);
            StandardRenderers.RenderRefreshBox(ctx, series, pending, paint);

            SKColor titleColor = themeType == ThemeType.Paper ? SKColors.Black : SKColors.White;
            using var titleFont = new SKFont(SKTypeface.Default, 16f * density);
            using var titlePaint = new SKPaint { Color = titleColor, IsAntialias = true };
            canvas.DrawText("CheckBox", 5 * 40f + 20f, 80f, SKTextAlign.Center, titleFont, titlePaint);
            canvas.DrawText("RefreshBox", 12 * 40f + 20f, 80f, SKTextAlign.Center, titleFont, titlePaint);
            canvas.DrawText("TriangleUpRing", 19 * 40f + 20f, 80f, SKTextAlign.Center, titleFont, titlePaint);
            canvas.DrawText("DCL stack", 31 * 40f + 20f, 80f, SKTextAlign.Center, titleFont, titlePaint);

            using var image = SKImage.FromBitmap(bitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(directory, fileName), png.ToArray());
        }
    }
}
