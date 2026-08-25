using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A theme has to cover the WHOLE window.
    ///
    /// <para>
    /// The chart is painted by Skia; every toolbar, dialog and label around it is HTML reading a
    /// fixed <c>:root</c> block. So switching to the light theme produced a white chart inside a
    /// near-black frame, and no amount of work in the renderer could fix it. <see
    /// cref="ThemeCssBridge"/> closes that gap by publishing each theme's chrome palette as CSS
    /// custom properties; these tests hold every part of the contract that can silently rot —
    /// a theme shipped without a chrome palette, a variable added to the bridge but missing from
    /// the stylesheet's fallback, or a focus ring that disappears on a light surface.
    /// </para>
    /// </summary>
    public class ThemeCoverageTests
    {
        private static ChartTheme Build(ThemeType type)
        {
            var settings = Substitute.For<ISettingsManager>();
            var service = new ThemeService(settings);
            service.SetTheme(type);
            return service.Current;
        }

        public static IEnumerable<object[]> AllThemes() =>
            Enum.GetValues<ThemeType>().Select(t => new object[] { t });

        // ── Every theme dresses the chrome ───────────────────────────────

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_definesAChromePaletteDistinctFromTheChart(ThemeType type)
        {
            var theme = Build(type);

            // The chrome defaults exist so an un-dressed theme still renders, but shipping one
            // that never sets them means its toolbars stay dark grey while its chart goes light.
            Assert.NotEqual(default, theme.SurfaceRaised);
            Assert.NotEqual(default, theme.TextPrimary);
            Assert.NotEqual(default, theme.ChromeBorder);
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_keepsItsChromeTextLegibleAgainstItsOwnToolbar(ThemeType type)
        {
            var theme = Build(type);

            // Not a full WCAG ratio — just the failure that actually happens, which is a theme
            // pairing light text with a light toolbar because only the chart half was considered.
            double surface = ThemeCssBridge.Luminance(theme.SurfaceRaised);
            double text    = ThemeCssBridge.Luminance(theme.TextPrimary);

            Assert.True(Math.Abs(surface - text) > 0.35,
                $"{type}: toolbar luminance {surface:0.00} vs text {text:0.00} — the chrome text " +
                "is too close in brightness to the surface behind it.");
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_getsAFocusRingThatContrastsWithItsChrome(ThemeType type)
        {
            var theme = Build(type);

            // A fixed yellow ring is excellent on black and nearly invisible on the light theme's
            // near-white toolbar — and a focus ring you cannot see is the same as no keyboard
            // navigation at all for a low-vision user.
            double surface = ThemeCssBridge.Luminance(theme.SurfaceRaised);
            double ring    = ThemeCssBridge.Luminance(ThemeCssBridge.FocusRingFor(theme));

            Assert.True(Math.Abs(surface - ring) > 0.3,
                $"{type}: focus ring luminance {ring:0.00} against a {surface:0.00} toolbar.");
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_getsReadableInkOnItsAccent(ThemeType type)
        {
            var theme = Build(type);

            // Primary buttons — Save, Place order, Confirm — are the accent colour with text on
            // top, and that text was a hardcoded near-black. Correct for the default accent and
            // wrong for any dark one, which is the label of the most consequential button in
            // every dialog. Measured per theme for the same reason as the focus ring: picking
            // it by hand is a thing to forget the next time a theme is added.
            double accent = ThemeCssBridge.Luminance(theme.Accent);
            double ink    = ThemeCssBridge.Luminance(ThemeCssBridge.InkOn(theme.Accent));

            Assert.True(Math.Abs(accent - ink) > 0.35,
                $"{type}: accent luminance {accent:0.00} vs its ink {ink:0.00} — the primary " +
                "button's label is too close in brightness to the button.");
        }

        // ── The bridge ───────────────────────────────────────────────────

        [Fact]
        public void TheBridge_emitsEveryVariableItAdvertises()
        {
            var vars = ThemeCssBridge.BuildVariables(Build(ThemeType.SteelGray));

            foreach (var name in ThemeCssBridge.VariableNames)
                Assert.True(vars.ContainsKey(name), $"{name} is advertised but never emitted.");

            Assert.Equal(ThemeCssBridge.VariableNames.Count, vars.Count);
        }

        [Fact]
        public void TheStylesheetFallback_declaresEveryVariableTheBridgeSets()
        {
            // If JS interop fails the bridge never runs, and the app must still render in a sane
            // palette rather than unstyled. That only holds while app.css declares every variable
            // the bridge would otherwise supply.
            foreach (var css in StylesheetPaths())
            {
                string text = File.ReadAllText(css);
                foreach (var name in ThemeCssBridge.VariableNames)
                    Assert.True(text.Contains(name + ":"),
                        $"{Path.GetFileName(Path.GetDirectoryName(css))}/app.css has no fallback for {name}.");
            }
        }

        [Fact]
        public void TheBridge_writesColoursAWebViewWillUnderstand()
        {
            var vars = ThemeCssBridge.BuildVariables(Build(ThemeType.SteelGray));

            foreach (var (name, value) in vars)
                Assert.True(value.StartsWith('#') || value.StartsWith("rgba(", StringComparison.Ordinal),
                    $"{name} = '{value}' is not a colour form every WebView parses.");
        }

        [Fact]
        public void TranslucentColoursBecomeRgbaBecauseNotEveryWebViewParsesEightDigitHex()
        {
            Assert.Equal("#204060", ThemeCssBridge.Css(new SKColor(0x20, 0x40, 0x60)));
            Assert.StartsWith("rgba(", ThemeCssBridge.Css(new SKColor(0x20, 0x40, 0x60, 128)));
        }

        [Fact]
        public void EachBandIsAddressableSoAThemeCanMakeThemThreeDifferentColours()
        {
            // The point of splitting the chrome into bands: a walnut header over a near-black
            // chart over a lighter footer has to be expressible. Previously all three were
            // derived from one fade and could not disagree.
            var theme = Build(ThemeType.SteelGray) with
            {
                SurfaceRaised   = new SKColor(0x6B, 0x4A, 0x2E),  // walnut
                ChromeTopEnd    = new SKColor(0x50, 0x37, 0x22),
                Background      = new SKColor(0x0E, 0x0E, 0x10),  // near-black chart
                BackgroundGradientEnd = new SKColor(0x08, 0x08, 0x0A),
                ChromeBottom    = new SKColor(0x3A, 0x2A, 0x1C),
                ChromeBottomEnd = new SKColor(0x2A, 0x1E, 0x14),
            };

            var vars = ThemeCssBridge.BuildVariables(theme);

            Assert.Equal("#6b4a2e", vars["--bg-toolbar"]);
            Assert.Equal("#503722", vars["--bg-toolbar-end"]);
            Assert.Equal("#0e0e10", vars["--chart-fade-top"]);
            Assert.Equal("#3a2a1c", vars["--bg-footer"]);
            Assert.Equal("#2a1e14", vars["--bg-footer-end"]);

            // Nothing forces the bands to agree with the chart.
            Assert.NotEqual(vars["--bg-toolbar-end"], vars["--chart-fade-top"]);
        }

        [Fact]
        public void ABandWithNoEndColourIsFlatRatherThanFadingToBlack()
        {
            // ChromeTopEnd / ChromeBottomEnd are optional. Absent must mean "flat band", not
            // "fade into whatever default", or every un-dressed theme grows a gradient it never
            // asked for.
            var theme = Build(ThemeType.SteelGray) with { ChromeTopEnd = null, ChromeBottomEnd = null };
            var vars = ThemeCssBridge.BuildVariables(theme);

            Assert.Equal(vars["--bg-toolbar"], vars["--bg-toolbar-end"]);
            Assert.Equal(vars["--bg-footer"],  vars["--bg-footer-end"]);
        }

        [Fact]
        public void TheWindowBackgroundIsTheVeryBottomOfTheWindow()
        {
            // Any gap around the canvas should read as more footer, not as a border in a colour
            // that appears nowhere else.
            var theme = Build(ThemeType.SteelGray);
            var vars = ThemeCssBridge.BuildVariables(theme);

            Assert.Equal(ThemeCssBridge.Css(theme.ChromeBottomEnd!.Value), vars["--bg-primary"]);
            Assert.Equal(ThemeCssBridge.Css(theme.Background), vars["--chart-fade-top"]);
        }

        [Fact]
        public void SteelGray_linesUpItsSeamsSoTheWindowReadsAsOneFade()
        {
            // Steel CHOOSES continuity — the toolbar ends exactly where the chart begins and the
            // footer starts exactly where the chart ends. Nothing requires it; this pins the
            // intent so a later tweak to one band doesn't quietly open a seam.
            var theme = Build(ThemeType.SteelGray);

            Assert.Equal(theme.Background, theme.ChromeTopEnd);
            Assert.Equal(theme.BackgroundGradientEnd, theme.ChromeBottom);
        }

        [Fact]
        public void Up_and_down_colours_are_an_app_preference_that_outranks_the_theme()
        {
            // "Which colour means up" is a habit carried between themes. It must not change
            // under the user when they try a new look.
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(SettingsKeys.BullishColor, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
                .Returns(new Newtonsoft.Json.Linq.JValue("#00A2FF"));
            settings.GetSetting(SettingsKeys.BearishColor, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
                .Returns(new Newtonsoft.Json.Linq.JValue("#FF7700"));

            var service = new ThemeService(settings);

            foreach (var type in Enum.GetValues<ThemeType>())
            {
                service.SetTheme(type);
                Assert.Equal(new SKColor(0x00, 0xA2, 0xFF), service.Current.CandleBullishBody);
                Assert.Equal(new SKColor(0xFF, 0x77, 0x00), service.Current.CandleBearishBody);
                // Volume follows, keeping its own alpha so it stays behind the candles.
                Assert.Equal(0x00, service.Current.VolumeBullish.Red);
                Assert.True(service.Current.VolumeBullish.Alpha < 255);
            }
        }

        [Fact]
        public void Without_that_preference_each_theme_keeps_its_own_pair()
        {
            // High Contrast Dark's white-on-red is a deliberate accessibility choice, not a
            // default waiting to be replaced.
            var service = new ThemeService(Substitute.For<ISettingsManager>());
            service.SetTheme(ThemeType.HighContrastDark);

            Assert.Equal(SKColors.White, service.Current.CandleBullishBody);
        }

        // ── The default theme ────────────────────────────────────────────

        [Fact]
        public void SteelGray_isWhatANewUserGets()
        {
            // No saved preference at all — a substituted settings manager returns null.
            var service = new ThemeService(Substitute.For<ISettingsManager>());

            Assert.Equal(ThemeType.SteelGray, service.Current.ThemeType);
        }

        [Fact]
        public void SteelGray_usesTheChosenBullAndBearColours()
        {
            var theme = Build(ThemeType.SteelGray);

            Assert.Equal(new SKColor(0x77, 0xFF, 0x77), theme.CandleBullishBody);
            Assert.Equal(new SKColor(0xDD, 0x00, 0x00), theme.CandleBearishBody);

            // Volume has to agree with the candles about what "up" looks like, or the two panes
            // tell different stories about the same bar.
            Assert.Equal(theme.CandleBullishBody.Red,   theme.VolumeBullish.Red);
            Assert.Equal(theme.CandleBullishBody.Green, theme.VolumeBullish.Green);
            Assert.Equal(theme.CandleBearishBody.Red,   theme.VolumeBearish.Red);
            Assert.Equal(theme.CandleBearishBody.Green, theme.VolumeBearish.Green);
        }

        [Fact]
        public void SteelGray_bullAndBearAreSeparatedByBrightnessNotOnlyByHue()
        {
            // Red-green deficiency is the common one. #DD0000 against #77FF77 stays legible
            // because the green is far brighter, not merely a different hue — so the pair still
            // carries direction when the hue difference does not.
            var theme = Build(ThemeType.SteelGray);

            double bull = ThemeCssBridge.Luminance(theme.CandleBullishBody);
            double bear = ThemeCssBridge.Luminance(theme.CandleBearishBody);

            Assert.True(bull - bear > 0.4,
                $"Bullish luminance {bull:0.00} vs bearish {bear:0.00} — too close to tell apart " +
                "without colour vision.");
        }

        [Fact]
        public void SteelGray_fadesUpwardSoTheChartMeetsTheToolbar()
        {
            var theme = Build(ThemeType.SteelGray);

            Assert.NotNull(theme.BackgroundGradientEnd);

            double top    = ThemeCssBridge.Luminance(theme.Background);
            double bottom = ThemeCssBridge.Luminance(theme.BackgroundGradientEnd!.Value);
            double toolbar = ThemeCssBridge.Luminance(theme.SurfaceRaised);

            Assert.True(top > bottom, "The chart must be lighter at the top, where it meets the toolbar.");
            Assert.True(toolbar > top, "The toolbar should sit above the chart, not frame it in something darker.");

            // Shallow on purpose: a wide swing washes out candles at the top of the pane, which is
            // exactly where price at the top of its range sits.
            Assert.True(top - bottom < 0.25, "The fade is steep enough to wash out candles near the top.");
        }

        [Fact]
        public void SteelGray_keepsCandlesReadableAgainstBothEndsOfItsGradient()
        {
            // The risk a light-topped chart carries: a candle that disappears into the background
            // at the top of the pane, which is exactly where price at the top of its range sits.
            //
            // Measured as RGB distance, NOT luminance. The first version of this test used
            // luminance alone and failed #DD0000 against the #383C42 top — the two are within
            // 0.05 of each other in brightness. But a saturated red against a neutral blue-grey
            // is plainly visible; what separates them is chroma, which luminance cannot see.
            // Luminance is the right measure for grey-on-grey (the doji, the chrome text) and the
            // wrong one for anything saturated.
            const int TooClose = 20_000;   // ~141 in RGB distance

            var theme = Build(ThemeType.SteelGray);

            foreach (var (label, background) in new[]
            {
                ("top of the gradient",    theme.Background),
                ("bottom of the gradient", theme.BackgroundGradientEnd!.Value),
            })
                foreach (var (name, colour) in new[]
                {
                    ("bullish", theme.CandleBullishBody),
                    ("bearish", theme.CandleBearishBody),
                    ("doji",    theme.CandleDojiBody),
                    ("axis text", theme.AxisText),
                })
                {
                    int d = DistanceSq(colour, background);
                    Assert.True(d > TooClose,
                        $"The {name} colour is indistinguishable from the {label} (distance {d}).");
                }
        }

        [Fact]
        public void SteelGray_gridIsVisibleWithoutCompetingWithTheData()
        {
            // Grid lines are the one thing that SHOULD sit close to the background — a grid as
            // loud as a candle is a wall, not a reference. So this checks a band rather than a
            // floor: present, but subordinate. (Including the grid in the candle-contrast check
            // was my mistake; it failed for being correctly quiet.)
            var theme = Build(ThemeType.SteelGray);

            int d = DistanceSq(theme.GridLine, theme.Background);

            Assert.True(d > 800,   $"The grid is invisible against the background (distance {d}).");
            Assert.True(d < 20_000, $"The grid competes with the price data (distance {d}).");
        }

        [Fact]
        public void SteelGray_dojiIsSeparatedFromTheBackgroundByBrightness()
        {
            // The doji body is neutral grey on a neutral grey background, so chroma cannot help
            // and luminance is the only thing keeping it visible.
            var theme = Build(ThemeType.SteelGray);

            Assert.True(ThemeCssBridge.Luminance(theme.CandleDojiBody)
                        - ThemeCssBridge.Luminance(theme.Background) > 0.3,
                "A neutral doji on a neutral background has only brightness to separate it.");
        }

        private static int DistanceSq(SKColor a, SKColor b)
        {
            int dr = a.Red - b.Red, dg = a.Green - b.Green, db = a.Blue - b.Blue;
            return dr * dr + dg * dg + db * db;
        }

        private static IEnumerable<string> StylesheetPaths()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);

            yield return Path.Combine(dir!.FullName, "AccessibleTrader.WebHost", "wwwroot", "app.css");
            yield return Path.Combine(dir.FullName, "AccessibleTrader.BlazorClient", "wwwroot", "app.css");
        }
        // ── The theme actually reaches the candles ───────────────────────

        [Fact]
        public void Candles_takeTheirColourFromTheThemeNotFromIndicatorMetadata()
        {
            // The bug: RenderCandles read the CANDLES component's ColorHex unconditionally, and
            // that comes from indicator metadata — a hardcoded #26A69A teal. So a theme could
            // repaint the background, grid, axes and the whole application chrome while the
            // candles stayed TradingView teal. The one element people actually look at was the
            // one element the theme could not touch.
            var (bull, bear) = RenderCandlePixels(userStyled: false);

            var theme = Build(ThemeType.SteelGray);
            AssertNear(theme.CandleBullishBody, bull, "bullish candle");
            AssertNear(theme.CandleBearishBody, bear, "bearish candle");
        }

        [Fact]
        public void A_hand_picked_candle_colour_survives_a_theme_change()
        {
            // The other half of the contract. Someone who deliberately recoloured their candles
            // must keep that choice — the renderer cannot tell a deliberate edit from a metadata
            // default by looking at the hex, which is what ComponentConfig.IsUserStyled is for.
            var (bull, _) = RenderCandlePixels(userStyled: true, bullHex: "#00A2FF");

            AssertNear(new SKColor(0x00, 0xA2, 0xFF), bull, "user-chosen bullish candle");
        }

        /// <summary>
        /// Renders one up bar and one down bar and reads the colour back off the surface. Pixels
        /// rather than a unit test of the colour-picking branch, because the failure mode here was
        /// a value being resolved correctly somewhere and then never reaching the paint.
        /// </summary>
        private static (SKColor Bull, SKColor Bear) RenderCandlePixels(bool userStyled, string bullHex = "#26A69A")
        {
            var theme = Build(ThemeType.SteelGray);

            var series = new AccessibleTrader.Sdk.Models.ChartSeries();
            series.Config.Name = "Candles";
            series.Components.Add(new AccessibleTrader.Sdk.Models.ComponentConfig
            {
                Name = "Candles", DisplayName = "Candles",
                DisplayType = AccessibleTrader.Sdk.Models.ComponentDisplayType.Candle,
                Role = AccessibleTrader.Sdk.Models.ComponentRole.PriceAction,
                ColorHex = bullHex, ColorHexSecondary = "#EF5350",
                IsUserStyled = userStyled, IsVisible = true,
            });

            var bars = new List<AccessibleTrader.Sdk.Models.Ohlcv>
            {
                new(new DateTime(2026, 1, 1), 10, 60, 10, 50, 1),   // up
                new(new DateTime(2026, 1, 2), 50, 60, 10, 10, 1),   // down
            };

            using var surface = SKSurface.Create(new SKImageInfo(200, 200));
            surface.Canvas.Clear(SKColors.Black);

            var ctx = new AccessibleTrader.Core.Services.Rendering.RenderContext(
                surface.Canvas, new SKRect(0, 0, 200, 200), bars, 0, 2, 0, 70, false,
                100f, 1f, "Main", 0, theme);

            using var paint = new SKPaint();
            AccessibleTrader.Core.Services.Rendering.StandardRenderers.RenderCandles(ctx, series, paint);

            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);

            // Mid-body of each candle: bar 0 occupies x 0..100, bar 1 occupies x 100..200.
            return (bitmap.GetPixel(50, 100), bitmap.GetPixel(150, 100));
        }

        private static void AssertNear(SKColor expected, SKColor actual, string what)
        {
            int d = DistanceSq(expected, actual);
            Assert.True(d < 900,
                $"The {what} painted {actual} but the theme asked for {expected} (distance {d}).");
        }
        // ── Every theme's own candles are legible on its own chart ───────

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_keepsItsOwnCandlesVisibleAgainstItsOwnBackground(ThemeType type)
        {
            // The failure this exists for: a light theme whose candles or wicks are white. Nothing
            // throws, nothing looks broken in the code, and the chart simply has invisible bars.
            // A preset is supposed to be a coherent SET — picking one should never require the
            // user to then go and fix the candles.
            const int TooClose = 12_000;

            var theme = Build(type);

            foreach (var background in new[] { theme.Background, theme.BackgroundGradientEnd ?? theme.Background })
                foreach (var (name, colour) in new[]
                {
                    ("bullish body", theme.CandleBullishBody),
                    ("bearish body", theme.CandleBearishBody),
                    ("bullish wick", theme.CandleBullishWick),
                    ("bearish wick", theme.CandleBearishWick),
                    ("doji body",    theme.CandleDojiBody),
                    ("axis text",    theme.AxisText),
                })
                {
                    int d = DistanceSq(colour, background);
                    Assert.True(d > TooClose,
                        $"{type}: the {name} ({colour}) is invisible against its own background " +
                        $"({background}) — distance {d}.");
                }
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_tellsUpFromDownWithoutRelyingOnColourVision(ThemeType type)
        {
            // Red-green deficiency is the common one, so a bull/bear pair that differs only in hue
            // stops carrying direction. Classic is exempt and says so in its own doc comment: it
            // exists to reproduce a familiar teal/salmon scheme, and that scheme has this flaw.
            if (type == ThemeType.Classic) return;

            var theme = Build(type);
            double bull = ThemeCssBridge.Luminance(theme.CandleBullishBody);
            double bear = ThemeCssBridge.Luminance(theme.CandleBearishBody);

            Assert.True(Math.Abs(bull - bear) > 0.25,
                $"{type}: bullish {bull:0.00} vs bearish {bear:0.00} — too close in brightness to " +
                "tell apart without colour vision.");
        }

        [Fact]
        public void Blackout_liftsItsDialogsOffTheBlackRatherThanMeltingIntoThem()
        {
            // On a black window only lightness can say "this is a separate surface". A dialog the
            // same colour as the background is a dialog with no edges.
            var theme = Build(ThemeType.Blackout);

            Assert.True(ThemeCssBridge.Luminance(theme.SurfaceSunken)
                        - ThemeCssBridge.Luminance(theme.Background) > 0.08,
                "The Blackout dialog surface is indistinguishable from its background.");
        }

        [Fact]
        public void Blackout_isFlatBecauseAGradientIsALitBackgroundByDegrees()
        {
            Assert.Null(Build(ThemeType.Blackout).BackgroundGradientEnd);
        }

        [Fact]
        public void A_custom_up_colour_that_collides_with_a_theme_is_a_thing_that_CAN_happen()
        {
            // Documenting the trap rather than pretending it away. Up/down colour survives theme
            // switches — correct for a habit — so someone can pick near-white candles and later
            // select a light theme and end up with an invisible chart, with neither choice wrong
            // on its own. The presets never collide with themselves; only a custom pair can.
            // Settings shows a live warning for exactly this, and deliberately does NOT correct
            // it: silently overriding someone's colour is worse than letting them see and decide.
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(SettingsKeys.BullishColor, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
                .Returns(new Newtonsoft.Json.Linq.JValue("#FFFFFF"));

            var service = new ThemeService(settings);
            service.SetTheme(ThemeType.HighContrastLight);   // white background

            int d = DistanceSq(service.Current.CandleBullishBody, service.Current.Background);

            Assert.True(d < 12_000,
                "This test exists to pin that the collision is possible and therefore worth warning " +
                "about. If it starts failing, the override behaviour changed and the warning in " +
                "Settings may no longer be reachable.");
        }

        // ── The unified-gradient option ──────────────────────────────────

        [Fact]
        public void UnifiedGradient_leavesNoSeamBetweenAdjacentBands()
        {
            // The property that matters. A hand-tuned palette usually gets one of these two
            // boundaries slightly wrong, which shows up as a visible line across the window.
            var theme = UnifiedGradient.Apply(Build(ThemeType.SteelGray),
                new SKColor(0x90, 0x90, 0x90), new SKColor(0x10, 0x10, 0x10));

            Assert.Equal(theme.Background, theme.ChromeTopEnd);
            Assert.Equal(theme.BackgroundGradientEnd, theme.ChromeBottom);
        }

        [Fact]
        public void UnifiedGradient_runsFromTheGivenTopToTheGivenBottom()
        {
            var top = new SKColor(0x90, 0x90, 0x90);
            var bottom = new SKColor(0x10, 0x10, 0x10);

            var theme = UnifiedGradient.Apply(Build(ThemeType.SteelGray), top, bottom);

            Assert.Equal(top, theme.SurfaceRaised);
            Assert.Equal(bottom, theme.ChromeBottomEnd);
        }

        [Fact]
        public void UnifiedGradient_darkensMonotonicallyDownTheWindow()
        {
            // Every band must be darker than the one above it. A non-monotonic result would mean
            // the stops are out of order, which reads as a band of the wrong colour rather than
            // as a fade.
            var theme = UnifiedGradient.Apply(Build(ThemeType.SteelGray),
                new SKColor(0x90, 0x90, 0x90), new SKColor(0x10, 0x10, 0x10));

            double[] stops =
            {
                ThemeCssBridge.Luminance(theme.SurfaceRaised),
                ThemeCssBridge.Luminance(theme.ChromeTopEnd!.Value),
                ThemeCssBridge.Luminance(theme.BackgroundGradientEnd!.Value),
                ThemeCssBridge.Luminance(theme.ChromeBottomEnd!.Value),
            };

            for (int i = 1; i < stops.Length; i++)
                Assert.True(stops[i] < stops[i - 1],
                    $"Band {i} is lighter than the band above it ({stops[i]:0.000} vs {stops[i - 1]:0.000}).");
        }

        [Fact]
        public void UnifiedGradient_isOffUnlessAskedFor()
        {
            // A theme decides its own look; this overrides all three bands at once, so it cannot
            // be something that happens by default.
            var settings = Substitute.For<ISettingsManager>();
            var service = new ThemeService(settings);

            // Steel happens to line its own seams up, so compare against a theme that does not.
            service.SetTheme(ThemeType.HighContrastDark);
            Assert.NotEqual(service.Current.SurfaceRaised, service.Current.Background);
        }

        [Fact]
        public void UnifiedGradient_withNoColoursChosenSmoothsTheThemesOwnEnds()
        {
            // Ticking the box alone has to do something sensible, rather than demanding two
            // colour choices before it will work.
            var settings = Substitute.For<ISettingsManager>();
            settings.GetSetting(SettingsKeys.UnifiedGradient, Arg.Any<Newtonsoft.Json.Linq.JToken?>())
                .Returns(new Newtonsoft.Json.Linq.JValue(true));

            var service = new ThemeService(settings);
            service.SetTheme(ThemeType.HighContrastDark);

            // HighContrastDark's own extremes become the ends, and the seams close up.
            Assert.Equal(service.Current.Background, service.Current.ChromeTopEnd);
            Assert.Equal(service.Current.BackgroundGradientEnd, service.Current.ChromeBottom);
        }

        [Fact]
        public void Lerp_hitsBothEndsExactlyAndTheMidpointBetweenThem()
        {
            var a = new SKColor(0, 0, 0);
            var b = new SKColor(200, 100, 50);

            Assert.Equal(a, UnifiedGradient.Lerp(a, b, 0));
            Assert.Equal(b, UnifiedGradient.Lerp(a, b, 1));
            Assert.Equal(new SKColor(100, 50, 25), UnifiedGradient.Lerp(a, b, 0.5));

            // Out-of-range t clamps rather than extrapolating into nonsense colours.
            Assert.Equal(a, UnifiedGradient.Lerp(a, b, -5));
            Assert.Equal(b, UnifiedGradient.Lerp(a, b, 5));
        }
    }
}