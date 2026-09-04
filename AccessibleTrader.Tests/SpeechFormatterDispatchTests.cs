using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
            // IsVisible=false → HiddenComponentStrategy answers with the NAME and short-circuits
            // every subsequent strategy; the dispatcher puts the state word in front of it.
            // Matters because Y-navigation still lands on hidden components and the user needs to
            // hear where they are. The wording moved on 2026-09-04 ("RSI: hidden" → "Hidden. RSI")
            // when the qualifier became the dispatcher's job: it is the only place that knows
            // about BOTH flags, and the old per-strategy version could never say "hidden and
            // muted". State first because whatever interrupts cuts the end of a sentence.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "rsi";
                c.DisplayName = "RSI";
                c.DisplayType = ComponentDisplayType.Line;
                c.IsVisible = false;
            }, values: new[] { 64.0 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Hidden. RSI", msg);
        }

        [Fact]
        public void Dispatch_HiddenBeatsCloud_WhenBothMatch()
        {
            // Priority check: Cloud display + IsVisible=false → Hidden still wins because
            // it's ahead of Cloud in the strategy list. A regression that reorders strategies
            // would fail here with "{name}. bullish, width ..." speech for a hidden cloud.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "kumo";
                c.DisplayName = "Kumo";
                c.DisplayType = ComponentDisplayType.Cloud;
                c.IsVisible = false;
            }, values: new[] { 3.5 });

            var msg = Format(series, focusedCompIndex: 0);
            Assert.Equal("Hidden. Kumo", msg);
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
        public void Dispatch_MarkerWithSignalTemplate_XScan_SpeaksValueOnly()
        {
            // LEFT/RIGHT scan (isYMove:false) onto a FIRED bar → the value at this bar,
            // no count. {price} routes through SpeechPriceFormatter so sub-cent assets
            // don't collapse to "0"; at 12345.67 it keeps 2 decimals.
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "buy";
                c.DisplayName = "Buy Signal";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = "cross up at {price}";
            }, values: new[] { 12345.67 });

            var msg = Format(series, focusedCompIndex: 0, isYMove: false);
            Assert.Equal("Buy Signal: cross up at 12345.67", msg);
        }

        [Fact]
        public void Dispatch_MarkerWithSignalTemplate_Landing_LeadsWithSignalsInView_ThenLandedValue()
        {
            // UP/DOWN landing on the component: name + "N signals in view" (so the user
            // knows there ARE dots to jump to with Ctrl+←/→), then the value at the bar
            // actually landed on. Here five bars hold two lit signals and the cursor is on
            // a NaN (empty) bar → "Buy Signal. 2 signals in view. no data."
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "buy";
                c.DisplayName = "Buy Signal";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = "cross up at {price}";
            }, values: new[] { double.NaN, 100.0, double.NaN, double.NaN, 200.0 });

            var msg = Format(series, focusedCompIndex: 0, isYMove: true, viewportStart: 0, viewportLength: 5);
            Assert.Equal("Buy Signal. 2 signals in view. no data", msg);
        }

        [Fact]
        public void Dispatch_MarkerWithSignalTemplate_Landing_OnFiredBar_LeadsWithCount_ThenSignal()
        {
            // Same landing, but the cursor is ON one of the two lit bars → the value part
            // is the expanded template, not "no data".
            var series = SingleComponent(out var comp, c =>
            {
                c.Name = "buy";
                c.DisplayName = "Buy Signal";
                c.DisplayType = ComponentDisplayType.Dot;
                c.IsVisible = true;
                c.SignalSpeechTemplate = "cross up at {price}";
            }, values: new[] { 100.0, double.NaN, 200.0 });

            var msg = Format(series, focusedCompIndex: 0, isYMove: true, viewportStart: 0, viewportLength: 3);
            Assert.Equal("Buy Signal. 2 signals in view. cross up at 100.00", msg);
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
        // ── Regression: provider-backed sparse markers (Cipher A/B/C) ─────────
        //
        // The real bug Cody hit on the web demo: Cipher A/B/C implement their own
        // GetComponentSpeech that returns "no data" on a NaN bar. That runs as
        // ProviderSpeechStrategy (#1), ahead of MarkerSignalStrategy (#5), so the
        // count was never spoken for an actual indicator. The desired behaviour:
        // UP/DOWN landing → "Name. N signals in view. <value at the landed bar>";
        // LEFT/RIGHT scan → just the value at that bar (no count).

        [Fact]
        public void Dispatch_ProviderBackedMarker_Landing_OnEmptyBar_LeadsWithCount_ThenNoData()
        {
            // A Cipher-like provider that (like the real ones) says "no data" on NaN.
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Ohlcv>(),
                    Arg.Any<IReadOnlyDictionary<string, double[]>>(), Arg.Any<int>())
                .Returns("no data");
            var engine = Substitute.For<IIndicatorEngine>();
            engine.GetProvider(Arg.Any<string>()).Returns(provider);

            // A sparse signal dot: two lit signals across five bars, cursor on a NaN bar.
            var series = CipherLikeMarker(new[] { double.NaN, 12.0, double.NaN, double.NaN, 40.0 });

            var msg = FormatWithEngine(series, engine, currentIndex: 0, viewportStart: 0, viewportLength: 5, barCount: 5, isYMove: true);

            // Count of what's in view, then the value at the (empty) landed bar.
            Assert.Equal("Oversold Crossover. 2 signals in view. no data", msg);
        }

        [Fact]
        public void Dispatch_ProviderBackedMarker_Landing_OnFiredBar_LeadsWithCount_ThenSignal()
        {
            // Landing directly on a lit bar → count, then the provider's rich narrative.
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Ohlcv>(),
                    Arg.Any<IReadOnlyDictionary<string, double[]>>(), Arg.Any<int>())
                .Returns("Oversold crossover at -62.3, price 84,500");
            var engine = Substitute.For<IIndicatorEngine>();
            engine.GetProvider(Arg.Any<string>()).Returns(provider);

            var series = CipherLikeMarker(new[] { 12.0, double.NaN });

            var msg = FormatWithEngine(series, engine, currentIndex: 0, viewportStart: 0, viewportLength: 2, barCount: 2, isYMove: true);

            Assert.Equal("Oversold Crossover. 1 signal in view. Oversold crossover at -62.3, price 84,500", msg);
        }

        [Fact]
        public void Dispatch_ProviderBackedMarker_XScan_SpeaksValueOnly_NoCount()
        {
            // LEFT/RIGHT scanning between the dots → just the value at each bar, no count
            // (the count is a landing-only announcement).
            var provider = Substitute.For<IIndicatorProvider>();
            provider.GetComponentSpeech(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Ohlcv>(),
                    Arg.Any<IReadOnlyDictionary<string, double[]>>(), Arg.Any<int>())
                .Returns("no data");
            var engine = Substitute.For<IIndicatorEngine>();
            engine.GetProvider(Arg.Any<string>()).Returns(provider);

            var series = CipherLikeMarker(new[] { double.NaN, 12.0, double.NaN, double.NaN, 40.0 });

            var msg = FormatWithEngine(series, engine, currentIndex: 2, viewportStart: 0, viewportLength: 5, barCount: 5, isYMove: false);

            Assert.Equal("no data", msg);
        }

        private static ChartSeries CipherLikeMarker(double[] markerData)
        {
            var cfg = new SeriesConfig { Id = "cipherb", Name = "Cipher B", IndicatorCode = "CIPHERB", Pane = "Sub" };
            cfg.Components.Add(new ComponentConfig
            {
                Name = "Oversold Crossover",
                DisplayName = "Oversold Crossover",
                DisplayType = ComponentDisplayType.Dot,
                IsVisible = true,
                SignalSpeechTemplate = "Oversold crossover, long signal",
            });
            var buf = new SeriesDataBuffer { SeriesId = "cipherb" };
            buf.ComponentData["Oversold Crossover"] = markerData;
            return new ChartSeries(cfg, buf);
        }

        private static string FormatWithEngine(ChartSeries series, IIndicatorEngine engine,
            int currentIndex, int viewportStart, int viewportLength, int barCount, bool isYMove = true)
        {
            var formatter = new SpeechFormatter(NullLogger<SpeechFormatter>.Instance, engine);
            var bars = new List<Ohlcv>();
            for (int i = 0; i < barCount; i++) bars.Add(OhlcvAt(i, close: 100 + i));
            var pt = bars[Math.Clamp(currentIndex, 0, barCount - 1)];

            var state = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                CurrentDataIndex = currentIndex,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                FocusedComponentIndex = 0,
                ReadColumnHeaders = true,
                SpeechOrder = "HeaderValue",
                SpeakTimestamps = false,
                LastInteractionContext = InteractionContext.Component,
                ViewportStartIndex = viewportStart,
                ViewportLength = viewportLength,
            };

            return formatter.FormatPointFeedback(state, isXMove: !isYMove, isYMove: isYMove, series, pt, prefixMessage: "");
        }

        private static string Format(ChartSeries series, int focusedCompIndex, Ohlcv? point = null,
            string speechOrder = "HeaderValue", bool isYMove = true, int viewportStart = 0, int viewportLength = 0)
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
                ViewportStartIndex = viewportStart,
                ViewportLength = viewportLength,
            };

            return formatter.FormatPointFeedback(state, isXMove: !isYMove, isYMove: isYMove, series, pt, prefixMessage: "");
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
