using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using NSubstitute;
using SkiaSharp;
using Xunit;

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
        public void TheWindowBackgroundMatchesTheDarkEndOfTheChartNotItsTop()
        {
            // Any gap around the canvas — scrollbar gutters, a resize sliver — should read as
            // more chart, not as a border in a fourth colour.
            var theme = Build(ThemeType.SteelGray);
            var vars = ThemeCssBridge.BuildVariables(theme);

            Assert.Equal(ThemeCssBridge.Css(theme.BackgroundGradientEnd!.Value), vars["--bg-primary"]);
            Assert.Equal(ThemeCssBridge.Css(theme.Background), vars["--chart-fade-top"]);
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
    }
}