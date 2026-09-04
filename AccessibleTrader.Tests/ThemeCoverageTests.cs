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

            // The failure that actually happened was a theme pairing light text with a light
            // toolbar because only the chart half was considered. Until 2026-09-02 this was a
            // gamma-less luminance delta against a hand-picked 0.35; it is now the WCAG ratio
            // against the floor for body text, the same measure the theme editor applies.
            double ratio = WcagContrast.Ratio(theme.TextPrimary, theme.SurfaceRaised);

            Assert.True(ratio >= WcagContrast.TextMinimum,
                $"{type}: toolbar text is {WcagContrast.Format(ratio)} against the toolbar; " +
                $"{WcagContrast.Format(WcagContrast.TextMinimum)} is the floor for body text.");
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_getsAFocusRingThatContrastsWithItsChrome(ThemeType type)
        {
            var theme = Build(type);

            // A fixed yellow ring is excellent on black and nearly invisible on the light theme's
            // near-white toolbar — and a focus ring you cannot see is the same as no keyboard
            // navigation at all for a low-vision user.
            double ratio = WcagContrast.Ratio(ThemeCssBridge.FocusRingFor(theme), theme.SurfaceRaised);

            Assert.True(ratio >= WcagContrast.GraphicsMinimum,
                $"{type}: the focus ring is {WcagContrast.Format(ratio)} against the toolbar; " +
                $"{WcagContrast.Format(WcagContrast.GraphicsMinimum)} is the floor for a UI component.");
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
            double ratio = WcagContrast.Ratio(ThemeCssBridge.InkOn(theme.Accent), theme.Accent);

            Assert.True(ratio >= WcagContrast.TextMinimum,
                $"{type}: the primary button's label is {WcagContrast.Format(ratio)} against the " +
                $"button; {WcagContrast.Format(WcagContrast.TextMinimum)} is the floor for body text.");
        }

        // ── The pairs the editor promises are safe ───────────────────────

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_keepsEveryTextPairAtTheWcagFloor(ThemeType type)
        {
            // ThemeContrastChecks is the ONE list of pairs — the editor refuses to save a theme
            // that fails a text pair on it, and this runs the same list over every built-in. The
            // editor's docstring says a preset is always safe; this is the sentence that makes
            // it true. Measured against the undecorated base theme, which is what the editor
            // starts from.
            var theme = BaseThemeResolver.Resolve(type);

            var failing = ThemeContrastChecks.Failures(theme)
                .Where(f => f.Severity == ThemeContrastChecks.Severity.Blocking)
                .Select(f => f.What)
                .ToList();

            Assert.True(failing.Count == 0,
                $"{type} would be refused by the theme editor:\n  " + string.Join("\n  ", failing));
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void EveryTheme_keepsItsCandlesAndFocusRingAboveTheGraphicsFloor(ThemeType type)
        {
            // WCAG 1.4.11: a graphical object needs 3:1 against what it sits on, and a candle
            // can sit anywhere on the chart gradient, so both ends are measured. The one
            // recorded exception is pinned in its own test below so it cannot outlive its reason.
            var theme = BaseThemeResolver.Resolve(type);

            var failing = ThemeContrastChecks.Failures(theme)
                .Where(f => f.Severity == ThemeContrastChecks.Severity.Advisory)
                .Where(f => !(type == ThemeType.SteelGray && f.Key is "bearTop" or "bearBottom"))
                .Select(f => f.What)
                .ToList();

            Assert.True(failing.Count == 0,
                $"{type}:\n  " + string.Join("\n  ", failing));
        }

        [Fact]
        public void SteelGray_fallingCandleIsBelowTheGraphicsFloor_recordedNotFixed()
        {
            // Steel Gray's falling candle is #DD0000 on a chart that fades from
            // #4E545E to #22252A. Measured 2026-09-02: 1.48:1 at the top of the chart and
            // 2.98:1 at the bottom, against a 3:1 floor. No red reaches 3:1 on that grey — it
            // takes a pink (#FF8080 is 3.15:1) — and #DD0000 is a chosen colour
            // (SteelGray_usesTheChosenBullAndBearColours), so changing it is a product decision
            // and not something to slip in under a contrast pass. This test pins the DEFECT: the
            // day the colour changes it goes red, the exemption above is deleted with it, and
            // the exemption can never quietly outlive its reason.
            var theme = BaseThemeResolver.Resolve(ThemeType.SteelGray);
            var bear = ThemeContrastChecks.Measure(theme).Single(f => f.Key == "bearTop");

            Assert.False(bear.Passes, "SteelGray's falling candle now clears 3:1 — delete this test " +
                                      "and the exemption in EveryTheme_keepsItsCandlesAndFocusRingAboveTheGraphicsFloor.");
            Assert.InRange(bear.Ratio, 1.4, 1.6);
        }

        [Fact]
        public void TheShippedDefaultIsClassic_andItCarriesNoContrastExemption()
        {
            // Two claims in one test, because separately either would be misleading.
            //
            // (1) WHICH theme ships. Changed from SteelGray to Classic on 2026-09-03 (Cody's
            //     call — the dark navy-and-teal most charting sites use). Pinned here so the
            //     decision cannot drift back through a fallback expression nobody re-reads;
            //     ThemeService.DefaultTheme is the single authority and both fallbacks in that
            //     file now route through it.
            //
            // (2) That the theme a NEW USER meets is not the one carrying the known exemption.
            //     SteelGray_fallingCandleIsBelowTheGraphicsFloor_recordedNotFixed above pins a
            //     falling candle at 1.48:1 against a 3:1 floor. That exemption is fine for a
            //     theme somebody chose; it is not fine for the one they are handed. So the
            //     default is measured against the SAME list, with no exemptions applied.
            Assert.Equal(ThemeType.Classic, ThemeService.DefaultTheme);   // see Classic_isWhatANewUserGets for the wiring half

            var theme = BaseThemeResolver.Resolve(ThemeService.DefaultTheme);
            var measured = ThemeContrastChecks.Measure(theme).ToList();

            // The floor under the sweep: an empty list would make "nothing failed" agree with
            // "nothing was measured".
            Assert.NotEmpty(measured);

            var failures = measured.Where(f => !f.Passes).ToList();
            Assert.True(failures.Count == 0,
                "The shipped default theme must clear every contrast check with no exemption. Failing: "
                + string.Join(", ", failures.Select(f => $"{f.Key} at {f.Ratio:F2}:1")));
        }

        [Fact]
        public void DialogHintText_isMeasuredAsTheDialogInkThroughGlass_notAsTextMuted()
        {
            // app.css scopes --text-muted inside .modal-content to the dialog ink at 68% alpha,
            // so the colour a dialog's hints are actually drawn in is TextOnDialog composited
            // over the dialog surface — and 68% alpha always erodes the ratio. A list that
            // measured theme.TextMuted here would let a theme whose dialog text just clears
            // 4.5:1 save with every hint in every dialog below it.
            var theme = BaseThemeResolver.Resolve(ThemeType.SteelGray);
            var hint = ThemeContrastChecks.Measure(theme).Single(f => f.Key == "secondaryText");
            var text = ThemeContrastChecks.Measure(theme).Single(f => f.Key == "dialogText");

            Assert.Equal(WcagContrast.Ratio(theme.TextOnDialog.WithAlpha(173), theme.SurfaceSunken), hint.Ratio, 6);
            Assert.True(hint.Ratio < text.Ratio, "the hint is the same ink through glass; it must measure lower");
            Assert.NotEqual(WcagContrast.Ratio(theme.TextMuted, theme.SurfaceSunken), hint.Ratio, 3);
        }

        [Fact]
        public void TheChecks_coverEveryPairTheEditorPromises()
        {
            // A vacuity floor for the two theories above: a pair dropped from
            // ThemeContrastChecks.Measure would make them pass by asking less, not by the
            // themes getting better.
            var keys = ThemeContrastChecks.Measure(BaseThemeResolver.Resolve(ThemeType.SteelGray))
                .Select(f => f.Key).ToHashSet();

            foreach (var expected in new[]
                     {
                         "toolbarText", "toolbarTextBottom", "dialogText", "secondaryText", "tabLabels", "footerText",
                         "axisTextTop", "axisTextBottom", "accentInk",
                         "bullTop", "bullBottom", "bearTop", "bearBottom", "crosshairTop", "crosshairBottom", "focusRing",
                         ThemeContrastChecks.GridKey,
                     })
            {
                Assert.Contains(expected, keys);
            }
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
        public void Each_theme_keeps_its_own_up_and_down_pair()
        {
            // High Contrast Dark's white-on-red is a deliberate accessibility choice, not a
            // default waiting to be replaced. The app-level up/down pair that used to outrank it
            // was retired 2026-09-03 (VisualAccessibilityTests.Retired_colour_overrides_are_ignored).
            var service = new ThemeService(Substitute.For<ISettingsManager>());
            service.SetTheme(ThemeType.HighContrastDark);

            Assert.Equal(SKColors.White, service.Current.CandleBullishBody);
        }

        // ── The default theme ────────────────────────────────────────────

        [Fact]
        public void Classic_isWhatANewUserGets()
        {
            // No saved preference at all — a substituted settings manager returns null.
            // Changed from SteelGray to Classic on 2026-09-03; see
            // TheShippedDefaultIsClassic_andItCarriesNoContrastExemption for why.
            //
            // This asserts through a REAL ThemeService rather than reading the constant, so it
            // still fails if the constructor's fallback stops routing through DefaultTheme.
            var service = new ThemeService(Substitute.For<ISettingsManager>());

            Assert.Equal(ThemeType.Classic, service.Current.ThemeType);
            Assert.Equal(ThemeService.DefaultTheme, service.Current.ThemeType);
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