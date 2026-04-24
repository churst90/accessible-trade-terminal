using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the magnitude-aware price-formatting contract so sub-cent assets
    /// (SHIB, PEPE, KAS fractions-of-a-dollar) don't collapse to "0.00" in
    /// speech and drawing narration. The project ships with <c>SpeechPriceFormatter</c>
    /// (magnitude → precision mapping) but earlier call sites bypassed it with
    /// fixed F0 / F2 / F4 formats. These tests:
    ///
    ///   (1) pin <c>SpeechPriceFormatter.FormatPrice</c>'s scaling across the
    ///       magnitude range traders actually encounter (trillions → 1e-9);
    ///   (2) pin the <c>{price}</c> token in <c>SignalSpeechTemplate</c>s
    ///       (reached via <c>SpeechFormatter</c>'s MarkerSignalStrategy) routes
    ///       through the formatter, not through F0; and
    ///   (3) pin the new <c>{value:price}</c> template token that's now used by
    ///       provider templates (Regime's Close-minus-SMA) to get magnitude-
    ///       aware formatting for price-space values on non-price series.
    /// </summary>
    public class PricePrecisionTests
    {
        // ── SpeechPriceFormatter magnitude scaling ───────────────────────────

        [Theory]
        [InlineData(50_000.00,  "50000.00")]   // BTC range — 2 dp
        [InlineData(   100.00,     "100.00")]  // ETH range — 2 dp
        [InlineData(    10.00,      "10.00")]  // mid-cap dollars — 2 dp
        [InlineData(     1.00,       "1.00")]  // dollar — 2 dp
        [InlineData(     0.10,       "0.100")] // dime — 3 dp
        [InlineData(     0.01,       "0.0100")] // penny — 4 dp
        [InlineData(     0.001,      "0.00100")] // KAS range — 5 dp
        [InlineData(     0.0001,     "0.000100")] // DOGE low — 6 dp
        [InlineData(     0.00003,    "0.0000300")] // SHIB range — 7 dp
        [InlineData(     0.0000009,  "0.000000900")] // PEPE deep — 9 dp
        public void FormatPrice_ScalesPrecisionByMagnitude(double value, string expected)
        {
            // Every band carries ~3 significant figures. Sub-cent assets never
            // round to 0; high-dollar assets stay at 2 dp like a dollar price.
            string actual = InvokeFormatPrice(value);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void FormatPrice_Zero_RendersPlainZero()
        {
            Assert.Equal("0", InvokeFormatPrice(0.0));
        }

        [Fact]
        public void FormatPrice_NegativeValue_RetainsSignAndMagnitudePrecision()
        {
            // Negative price-space deltas (e.g. Close - SMA200) must scale the
            // same as positive ones. Absolute value drives the precision pick.
            Assert.Equal("-0.0000300", InvokeFormatPrice(-0.00003));
        }

        [Fact]
        public void FormatPrice_NaN_ReturnsNoData()
        {
            Assert.Equal("no data", InvokeFormatPrice(double.NaN));
        }

        [Fact]
        public void FormatPrice_Infinity_ReturnsNoData()
        {
            Assert.Equal("no data", InvokeFormatPrice(double.PositiveInfinity));
        }

        // ── MarkerSignal {price} token integration ───────────────────────────

        [Fact]
        public void MarkerSignal_PriceToken_UsesMagnitudeAwareFormatting_ForSubCentAsset()
        {
            // The critical regression: a SHIB-class asset firing a Buy signal was
            // previously announced as "Buy Signal at 0" because {price} routed
            // through F0. Now it carries real precision.
            string msg = FormatSignal(template: "{name} at {price}", value: 0.00003);
            Assert.Equal("Buy Signal at 0.0000300", msg);
        }

        [Fact]
        public void MarkerSignal_PriceToken_DollarAsset_StillFormatsAsTwoDecimal()
        {
            // Regression guard against over-correcting — BTC-class assets must
            // still say "50000.00", not "50000" or "50000.00000".
            string msg = FormatSignal(template: "{name} at {price}", value: 50_000.00);
            Assert.Equal("Buy Signal at 50000.00", msg);
        }

        [Fact]
        public void MarkerSignal_PriceToken_DimeRange_UsesThreeDecimals()
        {
            // KAS / low-cap values around $0.1 need 3 dp to distinguish $0.123
            // from $0.125 — the user can't trade a 2-tick difference if speech
            // rounds both to "0.12".
            string msg = FormatSignal(template: "{name} at {price}", value: 0.123);
            Assert.Equal("Buy Signal at 0.123", msg);
        }

        // ── StandardTemplate {value:price} token ─────────────────────────────

        [Fact]
        public void StandardTemplate_ValuePriceToken_RoutesThroughMagnitudeAwareFormatter()
        {
            // Regime's Close-minus-SMA200 is in quote-currency units. Sub-cent
            // assets would collapse to "0.00" through {value:F2}. The new
            // {value:price} token routes through SpeechPriceFormatter regardless
            // of series id.
            string msg = FormatStandardLine(
                template: "Close minus SMA {value:price}.",
                value: -0.00002,
                isPriceSeries: false);
            Assert.Equal("Close minus SMA -0.0000200.", msg);
        }

        [Fact]
        public void StandardTemplate_ValuePriceToken_NaN_ReturnsNoData()
        {
            string msg = FormatStandardLine(
                template: "Close minus SMA {value:price}.",
                value: double.NaN,
                isPriceSeries: false);
            Assert.Equal("Close minus SMA no data.", msg);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="SpeechPriceFormatter.FormatPrice"/> is <c>internal static</c>;
        /// tests reach it through <c>InternalsVisibleTo</c>, but because the type
        /// is in the Core assembly and the test assembly already has access, we
        /// call via reflection to avoid a hard typing dependency in the test.
        /// </summary>
        private static string InvokeFormatPrice(double value)
        {
            var type = typeof(AccessibleTrader.Core.Services.Accessibility.SpeechFormatter)
                .Assembly
                .GetType("AccessibleTrader.Core.Services.Accessibility.SpeechPriceFormatter")
                ?? throw new InvalidOperationException("SpeechPriceFormatter not found.");
            var method = type.GetMethod("FormatPrice",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null, types: new[] { typeof(double) }, modifiers: null)
                ?? throw new MissingMethodException("FormatPrice not found.");
            return (string)method.Invoke(null, new object[] { value })!;
        }

        /// <summary>
        /// Drives MarkerSignalStrategy end-to-end: one Dot component with the
        /// supplied <paramref name="template"/> and value, exercised through
        /// <c>SpeechFormatter.FormatPointFeedback</c> in point-focus mode.
        /// </summary>
        private static string FormatSignal(string template, double value)
        {
            var cfg = new SeriesConfig { Id = "s", Name = "s", IndicatorCode = "S", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "signal",
                DisplayName = "Buy Signal",
                DisplayType = ComponentDisplayType.Dot,
                IsVisible = true,
                SignalSpeechTemplate = template,
            });
            var buf = new SeriesDataBuffer { SeriesId = "s" };
            buf.ComponentData["signal"] = new[] { value };
            var series = new ChartSeries(cfg, buf);

            return FormatThroughSpeechFormatter(series);
        }

        private static string FormatStandardLine(string template, double value, bool isPriceSeries)
        {
            // When isPriceSeries is false, a plain Line component routes through
            // StandardTemplateStrategy — this is the path Regime uses for its
            // Close-minus-SMA component.
            var cfg = new SeriesConfig
            {
                Id = isPriceSeries ? "candles" : "regime",
                Name = "regime",
                IndicatorCode = "REGIME",
                Pane = "Regime",
            };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "value",
                DisplayName = "Close minus SMA",
                DisplayType = ComponentDisplayType.Line,
                IsVisible = true,
                SpeechTemplate = template,
            });
            var buf = new SeriesDataBuffer { SeriesId = cfg.Id };
            buf.ComponentData["value"] = new[] { value };
            var series = new ChartSeries(cfg, buf);

            return FormatThroughSpeechFormatter(series);
        }

        private static string FormatThroughSpeechFormatter(ChartSeries series)
        {
            var formatter = new SpeechFormatter();
            var pt = new Ohlcv(
                new DateTime(2026, 04, 23, 9, 30, 0, DateTimeKind.Utc),
                100, 101, 99, 100, 1000);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { pt }),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = 0,
                ReadColumnHeaders = true,
                SpeechOrder = "HeaderValue",
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Component,
            };

            return formatter.FormatPointFeedback(state, isXMove: false, isYMove: true, series, pt, prefixMessage: "");
        }
    }
}
