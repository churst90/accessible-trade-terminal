using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 2 dispatch coverage for the five <c>IComponentSpeechStrategy</c> classes inside
    /// <see cref="SpeechFormatter"/>: <c>HiddenComponentStrategy</c>,
    /// <c>CloudComponentStrategy</c>, <c>PhaseNameStrategy</c>, <c>MarkerSignalStrategy</c>,
    /// and <c>StandardTemplateStrategy</c> (fallback).
    ///
    /// Strategies are consulted in registration order and the first
    /// <c>CanHandle</c> match wins. Every regression here pins one of two things:
    /// (1) the right strategy fires for a given component shape, and (2) their
    /// priority ordering is preserved when multiple could match.
    /// </summary>
    public class SpeechFormatterDispatchTests
    {
        // ── Strategy 1: HiddenComponentStrategy ───────────────────────────────

        [Fact]
        public void Dispatch_HiddenComponent_ReturnsHiddenMessage()
        {
            // IsVisible=false → strategy returns "{DisplayName}: hidden" and short-circuits
            // every subsequent strategy. Matters because Y-navigation still lands on hidden
            // components and the user needs to hear where they are.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = false;
            }, values: new[] { 64.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("RSI: hidden", msg);
        }

        [Fact]
        public void Dispatch_HiddenBeatsCloud_WhenBothMatch()
        {
            // Priority check: Cloud display + IsVisible=false → Hidden still wins because
            // it's first in the strategy list. A regression that reorders strategies would
            // fail here with "{name}. bullish, width ..." speech for a hidden cloud.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "kumo";
                c.DisplayName = "Kumo";
                c.DisplayType = ComponentDisplayType.Cloud;
                c.IsVisible = false;
            }, values: new[] { 3.5 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Kumo: hidden", msg);
        }

        // ── Strategy 2: CloudComponentStrategy ────────────────────────────────

        [Fact]
        public void Dispatch_CloudComponent_AnnouncesDirectionAndWidth()
        {
            // Cloud signed-width = +3.5 → bullish. UpperComponentName + LowerComponentName
            // route pricePosition; close=100 is below both upper=110 and lower=108 so
            // "Price below cloud." is appended.
            var cfg = new SeriesConfig { Id = "ichi", Name = "Ichimoku", IndicatorCode = "ICHI", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Kumo",
                DisplayName = "Kumo",
                DisplayType = ComponentDisplayType.Cloud,
                IsVisible = true,
                UpperComponentName = "SpanA",
                LowerComponentName = "SpanB",
            });
            cfg.Components.Add(new ComponentConfig { Name = "SpanA", DisplayName = "Span A", DisplayType = ComponentDisplayType.Line, IsVisible = true });
            cfg.Components.Add(new ComponentConfig { Name = "SpanB", DisplayName = "Span B", DisplayType = ComponentDisplayType.Line, IsVisible = true });
            var buf = new SeriesDataBuffer { SeriesId = "ichi" };
            buf.ComponentData["Kumo"]  = new[] { 3.5 };
            buf.ComponentData["SpanA"] = new[] { 110.0 };
            buf.ComponentData["SpanB"] = new[] { 108.0 };
            var series = new ChartSeries(cfg, buf);

            var msg = Format(series, focusedCompIndex: 0, point: OhlcvAt(0, close: 100));
            Assert.Contains("Kumo", msg);
            Assert.Contains("bullish", msg);
            Assert.Contains("width 3.50", msg);
            Assert.Contains("Price below cloud.", msg);
        }

        [Fact]
        public void Dispatch_CloudComponent_NaNValue_ReturnsNoData()
        {
            // Cloud with NaN signed width → "{DisplayName}: no data". Keeps silent-failure
            // rule: the user must still hear something when the indicator is warming up.
            var cfg = new SeriesConfig { Id = "ichi", Name = "Ichimoku", IndicatorCode = "ICHI", Pane = "Main" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Kumo",
                DisplayName = "Kumo",
                DisplayType = ComponentDisplayType.Cloud,
                IsVisible = true,
            });
            var buf = new SeriesDataBuffer { SeriesId = "ichi" };
            buf.ComponentData["Kumo"] = new[] { double.NaN };
            var series = new ChartSeries(cfg, buf);

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Kumo: no data", msg);
        }

        // ── Strategy 3: PhaseNameStrategy ─────────────────────────────────────

        [Fact]
        public void Dispatch_CandleColor_AnnouncesPhaseNameInsteadOfNumericValue()
        {
            // Raw value 5 → AudioConstants.PhaseNames[5] = "Neutral".
            // Prevents the user hearing "CandleColor 5" — nonsensical without the index map.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "phase";
                c.DisplayName = "Phase";
                c.DisplayType = ComponentDisplayType.CandleColor;
                c.IsVisible = true;
            }, values: new[] { 5.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Phase. Neutral.", msg);
        }

        [Fact]
        public void Dispatch_CandleColor_ValueClampedIntoPhaseRange()
        {
            // Out-of-range phase index → clamped to 10 (Max Euphoria). Protects against a
            // provider writing an off-by-one index into the CandleColor array.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "phase";
                c.DisplayName = "Phase";
                c.DisplayType = ComponentDisplayType.CandleColor;
                c.IsVisible = true;
            }, values: new[] { 42.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Phase. Max Euphoria.", msg);
        }

        // ── Strategy 4: MarkerSignalStrategy ──────────────────────────────────

        [Fact]
        public void Dispatch_MarkerWithSignalTemplate_ExpandsTokens()
        {
            // DisplayType=Dot + SignalSpeechTemplate set → template wins over the generic
            // SpeechTemplate. {name} = DisplayName; {price} routes through
            // SpeechPriceFormatter so sub-cent assets don't collapse to "0". At 12345.67
            // the formatter keeps 2 decimals (dollar-scale magnitude).
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "buy";
                c.DisplayName = "Buy Signal";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = "{name} at {price}";
            }, values: new[] { 12345.67 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Buy Signal at 12345.67", msg);
        }

        [Fact]
        public void Dispatch_MarkerWithSignalTemplate_NaNValue_ReturnsNoData()
        {
            // Marker signal not firing → "{DisplayName}: no data". The silent-failure
            // rule — the user must know nothing is there, not nothing at all.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "buy";
                c.DisplayName = "Buy Signal";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = "{name} at {price}";
            }, values: new[] { double.NaN });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Buy Signal: no data", msg);
        }

        [Fact]
        public void Dispatch_Marker_WithoutSignalTemplate_FallsThroughToStandardStrategy()
        {
            // DisplayType=Dot but SignalSpeechTemplate is null → MarkerSignalStrategy
            // refuses, fallback template takes over. Proves the two are properly
            // independent (regression: an earlier refactor coupled them).
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "dot";
                c.DisplayName = "Dot";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = null;
                c.SpeechTemplate = "{name} value {value:F1}";
            }, values: new[] { 7.34 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Dot value 7.3", msg);
        }

        // ── Strategy 5: StandardTemplateStrategy (fallback) ───────────────────

        [Fact]
        public void Dispatch_StandardTemplate_ReplacesNameTypeValue()
        {
            // A plain Line component with a standard provider template expands
            // {name}/{type}/{value}. Fallback strategy runs when none of the others match.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = true;
                c.SpeechTemplate = "{name}. {type}. {value}.";
            }, values: new[] { 64.32 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("RSI. line. 64.32.", msg);
        }

        [Fact]
        public void Dispatch_StandardTemplate_ValueOnlySpeechOrder_SkipsHeaders()
        {
            // SpeechOrder="ValueOnly" means: return just the value (no name/type/template
            // expansion). Used by dense sonification passes where every syllable is waste.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = true;
                c.SpeechTemplate = "{name}. {type}. {value}.";
            }, values: new[] { 64.32 });

            var msg = Format(series, focusedCompIndex: 0, speechOrder: "ValueOnly");
            Assert.Equal("64.32", msg);
        }

        [Fact]
        public void Dispatch_StandardTemplate_NaNValue_ReturnsNoData()
        {
            // NaN on fallback → value token becomes "no data" — matches the other strategies'
            // null-value contract so speech stays consistent during warmup bars.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = true;
                c.SpeechTemplate = "{name}: {value}";
            }, values: new[] { double.NaN });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("RSI: no data", msg);
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        /// <summary>
        /// Runs <see cref="SpeechFormatter.FormatPointFeedback"/> in point-focus mode
        /// (LastInteractionContext=Component) so the dispatcher routes through
        /// FormatTemplateValue. Disables timestamps so the assertion compares only the
        /// strategy's output.
        /// </summary>
        private static string Format(ChartSeries series, int focusedCompIndex, Ohlcv? point = null, string speechOrder = "HeaderValue")
        {
            var formatter = new SpeechFormatter();
            var pt = point ?? OhlcvAt(0, close: 100);

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new List<Ohlcv> { pt }),
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = focusedCompIndex,
                ReadColumnHeaders = true,
                SpeechOrder = speechOrder,
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Component,
            };

            return formatter.FormatPointFeedback(state, isXMove: false, isYMove: true, series, pt, prefixMessage: "");
        }

        private static ChartSeries SingleComponent(out ComponentConfig comp, Action<ComponentConfig> configure, double[] values)
        {
            comp = new ComponentConfig();
            configure(comp);
            var cfg = new SeriesConfig { Id = "s", Name = "s", IndicatorCode = "S", Pane = "Main" };
            cfg.Components.Add(comp);
            var buf = new SeriesDataBuffer { SeriesId = "s" };
            buf.ComponentData[comp.Name] = values;
            return new ChartSeries(cfg, buf);
        }

        private static Ohlcv OhlcvAt(int barIndex, double close)
        {
            var t = new DateTime(2026, 04, 23, 9, 30, 0, DateTimeKind.Utc).AddMinutes(barIndex);
            // Make it bullish by default so {trend} resolves predictably.
            return new Ohlcv(t, close, close + 1, close - 1, close, 1000);
        }
    }
}
