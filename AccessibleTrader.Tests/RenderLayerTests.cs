using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The remaining untested rendering layers — BackgroundLayer, DataLayer, OverlayLayer,
    /// HeatmapRenderer, ProfileRenderLayer — plus SKPaintPool. Same approach as
    /// StandardRenderersSmokeTests: render against a REAL SKBitmap, assert behaviourally
    /// (drew / didn't draw / drew in the right region), never exact colours, so the tests
    /// survive Skia version bumps. These layers run on every frame, so "does not throw on
    /// degenerate input" is itself a hard product guarantee.
    /// </summary>
    public class RenderLayerTests
    {
        private const int W = 200, H = 200;
        private static readonly SKColor Sentinel = new(0xFF, 0x00, 0xFF); // magenta: no layer paints this

        // ── Fixtures ───────────────────────────────────────────────────────────

        private static ThemeService Theme(bool gradient = false)
        {
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(Arg.Any<string>(), Arg.Any<JToken?>()).Returns((JToken?)null);
            if (gradient)
            {
                settings.GetSetting(SettingsKeys.BackgroundGradient).Returns(new JValue(true));
                settings.GetSetting(SettingsKeys.BackgroundColor2).Returns(new JValue("#000000"));
                settings.GetSetting(SettingsKeys.BackgroundColor).Returns(new JValue("#FFFFFF"));
            }
            return new ThemeService(settings);
        }

        private static List<Ohlcv> Bars(int n)
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return Enumerable.Range(0, n)
                .Select(i => new Ohlcv(start.AddMinutes(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 1000))
                .ToList();
        }

        private static RenderContext Ctx(SKCanvas canvas, IReadOnlyList<Ohlcv> data, ChartTheme theme,
            double min = 90, double max = 130, string pane = "Main", int cursor = -1,
            string? subPaneFilter = null)
        {
            int vlen = Math.Max(1, data.Count);
            return new RenderContext(
                Canvas: canvas, PaneRect: SKRect.Create(0, 0, W, H), Data: data,
                ViewportStart: 0, ViewportLength: vlen, Min: min, Max: max,
                IsLogScale: false, ItemWidth: (float)W / vlen, Density: 1f,
                PaneName: pane, LocalCursorIndex: cursor, Theme: theme,
                SubPaneFilter: subPaneFilter);
        }

        private static SKBitmap Render(Action<SKCanvas> draw)
        {
            var bmp = new SKBitmap(W, H);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(Sentinel);
                draw(canvas);
            }
            return bmp;
        }

        private static int NonSentinelCount(SKBitmap bmp)
        {
            int count = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y) != Sentinel) count++;
            return count;
        }

        private static ChartSeries LineSeries(string comp, double[] values, string pane = "Main",
            string? subPane = null, bool visible = true)
        {
            var config = new SeriesConfig { Id = comp, Name = comp, Pane = pane, IsVisible = visible };
            config.Components.Add(new ComponentConfig
            {
                Name = comp,
                DisplayType = ComponentDisplayType.Line,
                ColorHex = "#00FFFF",
                SubPaneName = subPane ?? "",
            });
            var buffer = new SeriesDataBuffer { SeriesId = comp };
            buffer.ComponentData[comp] = values;
            return new ChartSeries(config, buffer);
        }

        // ── SKPaintPool ────────────────────────────────────────────────────────

        [Fact]
        public void PaintPool_ReusesTheReturnedInstance_AndResetsItsState()
        {
            SKPaint first;
            using (var rented = SKPaintPool.Rent())
            {
                first = rented.Paint;
                first.Color = SKColors.Red;
                first.StrokeWidth = 9;
            } // Dispose returns it to the thread-local pool

            using var again = SKPaintPool.Rent();
            // Same instance back (LIFO pool on one thread), but with no leaked properties —
            // a leaked colour would paint the NEXT unrelated shape red.
            Assert.Same(first, again.Paint);
            Assert.NotEqual(SKColors.Red, again.Paint.Color);
            Assert.NotEqual(9, again.Paint.StrokeWidth);
        }

        [Fact]
        public void PaintPool_RentsDistinctInstances_WhileBothAreHeld()
        {
            using var a = SKPaintPool.Rent();
            using var b = SKPaintPool.Rent();
            Assert.NotSame(a.Paint, b.Paint);
        }

        // ── BackgroundLayer ────────────────────────────────────────────────────

        [Fact]
        public void Background_FillsTheEntirePane_Opaquely()
        {
            // The layer's stated job: no compositor bleed-through. Every sentinel pixel
            // must be painted over.
            var theme = Theme();
            using var bmp = Render(c =>
                new BackgroundLayer(theme).Render(Ctx(c, Bars(10), theme.Current), Array.Empty<ChartSeries>()));

            Assert.Equal(W * H, NonSentinelCount(bmp));
        }

        [Fact]
        public void Background_GradientOptIn_FadesTopToBottom()
        {
            var theme = Theme(gradient: true); // white → black
            using var bmp = Render(c =>
                new BackgroundLayer(theme).Render(Ctx(c, Bars(10), theme.Current), Array.Empty<ChartSeries>()));

            // Sample mid-column, away from gridlines' single-pixel rows: collect the
            // row luminance and require a monotone-ish fall from top to bottom.
            var top = bmp.GetPixel(W / 2, 5);
            var bottom = bmp.GetPixel(W / 2, H - 5);
            int LumOf(SKColor px) => px.Red + px.Green + px.Blue;
            Assert.True(LumOf(top) > LumOf(bottom) + 100,
                $"expected a light→dark fade, got top={top} bottom={bottom}");
        }

        [Theory]
        [InlineData(100, 100)]                            // degenerate: Min == Max
        [InlineData(double.NaN, double.NaN)]              // NaN range
        [InlineData(double.NegativeInfinity, double.PositiveInfinity)] // infinite range
        public void Background_DegenerateRange_DoesNotThrow(double min, double max)
        {
            var theme = Theme();
            using var bmp = Render(c =>
                new BackgroundLayer(theme).Render(
                    Ctx(c, Bars(10), theme.Current, min: min, max: max), Array.Empty<ChartSeries>()));
            Assert.True(NonSentinelCount(bmp) > 0); // still filled the pane
        }

        // ── DataLayer ──────────────────────────────────────────────────────────

        private static DataLayer NewDataLayer()
        {
            // MockStylingService returns fill-style paints, which draw nothing for open
            // line paths; the real StylingService strokes. Substitute a stroking one.
            var styling = Substitute.For<IStylingService>();
            styling.GetPaint(Arg.Any<ComponentConfig>(), Arg.Any<float>())
                   .Returns(_ => new SKPaint { Color = SKColors.Cyan, StrokeWidth = 2, Style = SKPaintStyle.Stroke });
            return new DataLayer(styling);
        }

        [Fact]
        public void Data_VisibleLineSeriesInThePane_Draws()
        {
            var theme = Theme().Current;
            var values = Enumerable.Range(0, 20).Select(i => 100.0 + i).ToArray();
            using var bmp = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme), new[] { LineSeries("Ema", values) }));

            Assert.True(NonSentinelCount(bmp) > 0);
        }

        [Fact]
        public void Data_SeriesInAnotherPane_IsNotDrawn()
        {
            var theme = Theme().Current;
            var values = Enumerable.Range(0, 20).Select(i => 100.0 + i).ToArray();
            using var bmp = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme), new[] { LineSeries("Rsi", values, pane: "Oscillator") }));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        [Fact]
        public void Data_InvisibleSeries_IsNotDrawn()
        {
            var theme = Theme().Current;
            var values = Enumerable.Range(0, 20).Select(i => 100.0 + i).ToArray();
            using var bmp = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme), new[] { LineSeries("Ema", values, visible: false) }));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        [Fact]
        public void Data_SubPaneComponent_IsSkippedInTheMainPass_AndDrawnInItsOwnPass()
        {
            var theme = Theme().Current;
            var values = Enumerable.Range(0, 20).Select(i => 100.0 + i).ToArray();
            var series = LineSeries("Squeeze", values, subPane: "strip");

            using var mainPass = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme), new[] { series }));
            using var stripPass = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme, subPaneFilter: "strip"), new[] { series }));

            Assert.Equal(0, NonSentinelCount(mainPass));
            Assert.True(NonSentinelCount(stripPass) > 0);
        }

        [Fact]
        public void Data_VisibleLevel_DrawsAHorizontalLineAtItsPrice()
        {
            var theme = Theme().Current;
            var series = LineSeries("Rsi", new double[20]);
            series.Config.Levels.Add(new LevelConfig
            {
                Name = "Overbought", Value = 110, ColorHex = "#FF0000",
                Thickness = 2, DashStyle = DashStyle.Solid,
            });
            // Empty component data → only the level can draw.
            series.Data.ComponentData["Rsi"] = Array.Empty<double>();

            using var bmp = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(20), theme, min: 90, max: 130), new[] { series }));

            // 110 in [90,130] maps to half-way down the pane.
            float y = ChartMath.MapY(110, 0, H, 90, 130, false);
            Assert.NotEqual(Sentinel, bmp.GetPixel(W / 2, (int)y));
        }

        [Fact]
        public void Data_HeatmapComponent_RoutesToTheHeatmapRenderer()
        {
            var theme = Theme().Current;
            var config = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main" };
            config.Components.Add(new ComponentConfig { Name = "hm", DisplayType = ComponentDisplayType.Heatmap });
            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "hm" }) { Volume = 1f };
            series.HeatmapData = Enumerable.Range(0, 10).Select(_ => new List<ProfileBin>
            {
                new() { PriceLow = 100, PriceHigh = 110, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = false },
            }).ToList();

            using var bmp = Render(c =>
                NewDataLayer().Render(Ctx(c, Bars(10), theme), new[] { series }));

            Assert.True(NonSentinelCount(bmp) > 0);
        }

        // ── HeatmapRenderer ────────────────────────────────────────────────────

        [Fact]
        public void Heatmap_DrawsBinsInTheirPriceBand_ScaledByVolume()
        {
            var theme = Theme().Current;
            var config = new SeriesConfig { Id = "hm", Name = "hm", Pane = "Main" };
            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "hm" }) { Volume = 1f };
            series.HeatmapData = Enumerable.Range(0, 10).Select(_ => new List<ProfileBin>
            {
                new() { PriceLow = 120, PriceHigh = 128, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = false },
            }).ToList();

            using var bmp = Render(c => new HeatmapRenderer().Render(Ctx(c, Bars(10), theme), series));

            // The band 120–128 sits in the upper part of a 90–130 pane; the lower half
            // holds no bins and must stay untouched.
            float yIn = ChartMath.MapY(124, 0, H, 90, 130, false);
            float yOut = ChartMath.MapY(95, 0, H, 90, 130, false);
            Assert.NotEqual(Sentinel, bmp.GetPixel(W / 2, (int)yIn));
            Assert.Equal(Sentinel, bmp.GetPixel(W / 2, (int)yOut));
        }

        [Fact]
        public void Heatmap_NoData_IsANoOp()
        {
            var theme = Theme().Current;
            var series = new ChartSeries(new SeriesConfig { Id = "hm" }, new SeriesDataBuffer { SeriesId = "hm" });

            using var bmp = Render(c => new HeatmapRenderer().Render(Ctx(c, Bars(10), theme), series));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        // ── ProfileRenderLayer ─────────────────────────────────────────────────

        // ProfileRenderLayer takes no dependencies: it draws from ctx.Theme and the bins
        // already on the series. The theme parameter stays so callers read naturally, but
        // it reaches the layer through the RenderContext, not the constructor.
        private static ProfileRenderLayer NewProfileLayer(ThemeService theme) => new();

        private static ChartSeries ProfileSeries(params ProfileBin[] bins)
        {
            var config = new SeriesConfig { Id = "vpvr", Name = "vpvr", Pane = "Main" };
            var series = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "vpvr" }) { IsProfile = true };
            series.ProfileBins = bins.ToList();
            return series;
        }

        [Fact]
        public void Profile_DrawsRightAligned_WithinTheProfileStrip()
        {
            var theme = Theme();
            var series = ProfileSeries(
                new ProfileBin { PriceLow = 108, PriceHigh = 112, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = true });

            using var bmp = Render(c =>
                NewProfileLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { series }));

            // The max-volume bin spans the full 20% strip at the right edge of its band;
            // the left 80% of the pane stays untouched.
            float y = ChartMath.MapY(110, 0, H, 90, 130, false);
            Assert.NotEqual(Sentinel, bmp.GetPixel(W - 5, (int)y));
            Assert.Equal(Sentinel, bmp.GetPixel(W / 4, (int)y));
        }

        [Fact]
        public void Profile_PocBin_GetsALineAcrossTheWholeStrip()
        {
            var theme = Theme();
            var series = ProfileSeries(
                new ProfileBin { PriceLow = 108, PriceHigh = 112, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = false, IsValueArea = true },
                new ProfileBin { PriceLow = 100, PriceHigh = 104, TotalVolume = 10, TpoPeriodCount = 1, IsPOC = true, IsValueArea = true });

            using var bmp = Render(c =>
                NewProfileLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { series }));

            // The POC bin's own bar is tiny (10% of max volume), but the POC line spans the
            // whole 20% strip — so the strip's left edge is painted at the POC price even
            // though the bar itself never reaches there.
            float yPoc = ChartMath.MapY(102, 0, H, 90, 130, false);
            Assert.NotEqual(Sentinel, bmp.GetPixel((int)(W * 0.81f), (int)yPoc));
        }

        [Fact]
        public void Profile_NonProfileOrInvisibleSeries_AreSkipped()
        {
            var theme = Theme();
            var notProfile = ProfileSeries(
                new ProfileBin { PriceLow = 108, PriceHigh = 112, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = true, IsValueArea = true });
            notProfile.IsProfile = false;

            var invisible = ProfileSeries(
                new ProfileBin { PriceLow = 108, PriceHigh = 112, TotalVolume = 100, TpoPeriodCount = 1, IsPOC = true, IsValueArea = true });
            invisible.IsVisible = false;

            using var bmp = Render(c =>
                NewProfileLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { notProfile, invisible }));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        [Fact]
        public void Profile_AllZeroVolume_IsANoOp_NotADivideByZero()
        {
            var theme = Theme();
            var series = ProfileSeries(
                new ProfileBin { PriceLow = 108, PriceHigh = 112, TotalVolume = 0, TpoPeriodCount = 1, IsPOC = true, IsValueArea = true });

            using var bmp = Render(c =>
                NewProfileLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { series }));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        // ── OverlayLayer ───────────────────────────────────────────────────────

        [Fact]
        public void Overlay_DrawsTheCrosshair_AtTheCursorColumn()
        {
            var theme = Theme();
            var bars = Bars(10);
            using var bmp = Render(c =>
                new OverlayLayer(theme).Render(Ctx(c, bars, theme.Current, cursor: 5), Array.Empty<ChartSeries>()));

            // Vertical dashed line at the centre of bar 5's slot: at least one painted
            // pixel in that column, none in a far-away column (bar 0's left edge has the
            // horizontal line's row only).
            int x = (int)(5 * (W / 10f) + (W / 10f) / 2);
            int painted = 0;
            for (int y = 0; y < H; y++)
                if (bmp.GetPixel(x, y) != Sentinel) painted++;
            Assert.True(painted > 10, $"expected a crosshair column at x={x}, painted={painted}");
        }

        [Fact]
        public void Overlay_OutOfRangeCursor_DrawsNoCrosshair()
        {
            var theme = Theme();
            using var bmp = Render(c =>
                new OverlayLayer(theme).Render(Ctx(c, Bars(10), theme.Current, cursor: -1), Array.Empty<ChartSeries>()));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        [Fact]
        public void Overlay_IsMainPaneOnly()
        {
            var theme = Theme();
            using var bmp = Render(c =>
                new OverlayLayer(theme).Render(
                    Ctx(c, Bars(10), theme.Current, pane: "Oscillator", cursor: 5), Array.Empty<ChartSeries>()));

            Assert.Equal(0, NonSentinelCount(bmp));
        }

        [Fact]
        public void Overlay_TextLabelWithText_IsActuallyDrawn()
        {
            // Regression fence for "the text tool whose text was never drawn".
            var theme = Theme();
            var config = new SeriesConfig { Id = "lbl", Name = "lbl", Pane = "Main" };
            var buffer = new SeriesDataBuffer { SeriesId = "lbl" };
            var label = new double[10];
            Array.Fill(label, double.NaN);
            label[5] = 110;
            buffer.ComponentData["Label"] = label;
            var series = new ChartSeries(config, buffer)
            {
                Drawing = new DrawingData { Type = DrawingType.TextLabel, Text = "note" },
            };

            using var withText = Render(c =>
                new OverlayLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { series }));

            series.Drawing.Text = "";
            using var withoutText = Render(c =>
                new OverlayLayer(theme).Render(Ctx(c, Bars(10), theme.Current), new[] { series }));

            Assert.True(NonSentinelCount(withText) > 0);
            Assert.Equal(0, NonSentinelCount(withoutText));
        }
    }
}
