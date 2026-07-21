using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Xunit;

namespace AccessibleTrader.Tests
{
    // ─── Spy helpers ──────────────────────────────────────────────────────────

    /// <summary>Captures every SpeakPoint call so tests can assert on it.</summary>
    internal class SpySpeechRouter : ISpeechFeedbackRouter
    {
        public List<string> SpokenTexts { get; } = new();
        public int SpeakCallCount { get; private set; }

        public void Speak(string message, bool interrupt = true, SpeechChannel channel = SpeechChannel.Manual)
        {
            SpeakCallCount++;
            SpokenTexts.Add(message);
        }

        public void SpeakPoint(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, Ohlcv point, string prefix = "")
        {
            SpeakCallCount++;
            SpokenTexts.Add(prefix);
        }

        public void SpeakProfile(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int binIndex, string prefix = "") { }
        public void SpeakHeatmap(WorkspaceState state, WorkspaceState? previousState, ChartSeries series, int dataIndex, int binIndex, string prefix = "") { }
    }

    // ─── SpeechFormatter tests ─────────────────────────────────────────────────

    public class SpeechFormatterTests
    {
        private static readonly SpeechFormatter _formatter = new();

        private static ChartSeries MakeSeries(string indicatorCode, params ComponentConfig[] components)
        {
            var config = new SeriesConfig { IndicatorCode = indicatorCode, Name = indicatorCode };
            if (new[] { "candles", "price", "volume" }.Contains(indicatorCode.ToLowerInvariant()))
            {
                config = new SeriesConfig { Id = indicatorCode.ToLowerInvariant(), IndicatorCode = indicatorCode, Name = indicatorCode };
            }

            var data = new SeriesDataBuffer { SeriesId = config.Id };
            foreach (var c in components)
            {
                config.Components.Add(c);
                // Only initialize if the component isn't "Volume" for the specific fallback test,
                // or just initialize with NaN to force fallback if that's the logic.
                // Actually, the test wants to verify the branch that uses p.Volume when NO component data exists.
                if (indicatorCode.ToLowerInvariant() == "volume" && c.Name == "Volume")
                {
                    // Skip initialization for this specific test case
                }
                else
                {
                    data.ComponentData[c.Name] = new double[] { 0 };
                }
            }
            return new ChartSeries(config, data);
        }

        private static ComponentConfig MakeComponent(string name)
            => new() { Name = name, DisplayName = name };

        private static Ohlcv MakeBar(double open, double close, double high, double low, double volume)
            => new(DateTime.UtcNow, open, high, low, close, volume);

        private static WorkspaceState BuildState(ChartSeries s, int idx, bool summary = false)
        {
            return WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, 100, 110, 95, 105, 1000)),
                ActiveSeries = System.Collections.Immutable.ImmutableList.Create(s),
                FocusedSeriesId = s.Id,
                CurrentDataIndex = 0,
                LastInteractionContext = summary ? InteractionContext.Series : InteractionContext.Component,
                SpeakTimestamps = false,
                TimestampReadLocation = "None",
                ReadColumnHeaders = false,
                SpeechOrder = "ValueOnly"
            };
        }

        // ── Candle tests ──────────────────────────────────────────────────────

        [Fact]
        public void Candles_Summary_BullishBar_StartsWithBullish()
        {
            var body = MakeComponent("Body");
            var s = MakeSeries("candles", body);
            var pt = MakeBar(open: 100, close: 110, high: 115, low: 95, volume: 5000);
            var state = BuildState(s, 0, true);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.StartsWith("Bullish.", result);
            Assert.Contains("Close 110.00", result);
            Assert.Contains("Open 100.00", result);
        }

        [Fact]
        public void Candles_Summary_BearishBar_StartsWithBearish()
        {
            var body = MakeComponent("Body");
            var s = MakeSeries("candles", body);
            var pt = MakeBar(open: 110, close: 100, high: 115, low: 95, volume: 5000);
            var state = BuildState(s, 0, true);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.StartsWith("Bearish.", result);
        }

        // ── Volume tests ──────────────────────────────────────────────────────

        [Fact]
        public void Volume_BullishBar_SpeaksExactValueThenUp()
        {
            var comp = MakeComponent("Volume");
            var s = MakeSeries("volume", comp);
            var pt = MakeBar(open: 100, close: 105, high: 110, low: 98, volume: 12345.678);
            var state = BuildState(s, 0, true);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.Equal("12,345.68, up", result);
        }

        [Fact]
        public void Volume_BearishBar_SpeaksExactValueThenDown()
        {
            // Bullish and bearish volume bars carry the same number; speech marks
            // direction the way the bar's colour does (close vs open). Values are
            // exact — decimals kept when present, no fake ".00" on whole numbers.
            var comp = MakeComponent("Volume");
            var s = MakeSeries("volume", comp);
            var pt = MakeBar(open: 105, close: 100, high: 110, low: 98, volume: 12345.678);
            var state = BuildState(s, 0, true);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.Equal("12,345.68, down", result);
        }

        [Fact]
        public void Volume_WholeNumber_ReadsWithoutFakeDecimals()
        {
            var comp = MakeComponent("Volume");
            var s = MakeSeries("volume", comp);
            var pt = MakeBar(open: 100, close: 105, high: 110, low: 98, volume: 5000);
            var state = BuildState(s, 0, true);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.Equal("5,000, up", result);
        }

        [Fact]
        public void CandleBody_ComponentContext_SpeaksOpenAndClose()
        {
            // The body IS the open→close span — a single number can't convey its
            // size, so component-context navigation reads both ends.
            var body = MakeComponent("Body");
            body.Role = ComponentRole.Body;
            var s = MakeSeries("candles", body);
            var pt = MakeBar(open: 100, close: 110, high: 115, low: 95, volume: 5000);
            var state = BuildState(s, 0, summary: false);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.Contains("Open", result);
            Assert.Contains("close", result);
            Assert.Contains("100", result);
            Assert.Contains("110", result);
        }

        // ── Indicator data tests ──────────────────────────────────────────────

        [Fact]
        public void Indicator_WithDataAtIndex_ReturnsFormattedValue()
        {
            var data = new double[] { 65.5 };
            var comp = MakeComponent("Rsi");
            var s = MakeSeries("RSI", comp);
            s.Data.ComponentData[comp.Name] = data;
            var pt = MakeBar(100, 105, 110, 98, 1000);
            var state = BuildState(s, 0, false);

            string result = _formatter.FormatPointFeedback(state, true, false, s, pt, "");

            Assert.Equal("65.50", result);
        }

        // ── Viewport Description ─────────────────────────────────────────

        [Fact]
        public void FormatViewportDescription_ReturnsCorrectFormat()
        {
            var start = new DateTime(2024, 1, 5, 9, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2024, 3, 15, 9, 0, 0, DateTimeKind.Utc);

            string result = _formatter.FormatViewportDescription(77, start, end);

            Assert.Equal("Viewing 77 bars from January 5 2024 to March 15 2024", result);
        }
    }

    // ─── NavigationFeedbackManager tests ──────────────────────────────────────

    public class NavigationFeedbackManagerTests
    {
        private static WorkspaceState BuildState(ChartSeries series, int index, InteractionContext ctx = InteractionContext.Series)
        {
            var ohlcv = new TimeSeriesBuffer<Ohlcv>(
                new Ohlcv(DateTime.UtcNow, 100, 110, 95, 105, 1000),
                new Ohlcv(DateTime.UtcNow.AddHours(1), 105, 115, 100, 110, 1200));

            return WorkspaceState.Initial with
            {
                Data = ohlcv,
                ActiveSeries = System.Collections.Immutable.ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                CurrentDataIndex = index,
                InitStatus = InitializationStatus.Ready,
                LastInteractionContext = ctx,
                IsPlaying = false,
                IsPaused = false,
                SpeakTimestamps = false,
                TimestampReadLocation = "None",
                ReadColumnHeaders = false,
                SpeechOrder = "ValueOnly"
            };
        }

        private static ChartSeries MakeCandleSeries()
        {
            var config = new SeriesConfig { Id = "candles", IndicatorCode = "candles", Name = "Price" };
            var data = new SeriesDataBuffer { SeriesId = config.Id };
            var body = new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true };
            config.Components.Add(body);
            data.ComponentData[body.Name] = new double[] { 0, 0 };
            return new ChartSeries(config, data);
        }

        [Fact]
        public void XMove_CallsSpeakPoint()
        {
            var spy = new SpySpeechRouter();
            var audioRouter = new MockAudioRouter();
            var formatter = new SpeechFormatter();
            var eventBus = new SpyEventBus();
            var mgr = new NavigationFeedbackManager(spy, formatter, eventBus, new MockNavigationSonifier(), new MockIndicatorEngine());
            mgr.IsSpeechEnabled = true;

            var series = MakeCandleSeries();
            var state = BuildState(series, index: 1);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            Assert.True(spy.SpeakCallCount > 0, "Expected at least one speech call for X navigation");
        }

        [Fact]
        public void SpeechDisabled_DoesNotCallSpeak()
        {
            var spy = new SpySpeechRouter();
            var audioRouter = new MockAudioRouter();
            var formatter = new SpeechFormatter();
            var eventBus = new SpyEventBus();
            var mgr = new NavigationFeedbackManager(spy, formatter, eventBus, new MockNavigationSonifier(), new MockIndicatorEngine()) { IsSpeechEnabled = false };

            var series = MakeCandleSeries();
            var state = BuildState(series, index: 0);

            mgr.HandleNavigationFeedback(state, isXMove: true, isYMove: false, prefixMessage: "");

            Assert.Equal(0, spy.SpeakCallCount);
        }
    }
}





