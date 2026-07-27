using System;
using System.Collections.Immutable;
using System.Linq;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The chart-layout summary (Alt+Shift+L).
    ///
    /// <para>
    /// Every other spoken message in the terminal answers "what is the value here?". This one
    /// answers "what am I looking at?" — the question you have BEFORE that one, and the one a
    /// sighted user answers by glancing at the screen. Until this existed the only way to learn
    /// how many panes a chart had was to navigate all of them and count.
    /// </para>
    ///
    /// <para>
    /// It is spoken, so what it says is the whole product. These tests assert the wording, not
    /// just that a string came back.
    /// </para>
    /// </summary>
    public class ChartLayoutDescriberTests
    {
        private static readonly DateTime Start = new(2026, 1, 1);

        private static WorkspaceState StateWith(int barCount = 200, int viewportStart = 0,
            int viewportLength = 60, params ChartSeries[] series)
        {
            var buffer = new TimeSeriesBuffer<Ohlcv>(
                Enumerable.Range(0, barCount)
                    .Select(i => new Ohlcv(Start.AddDays(i), 100, 110, 90, 105, 1000)));

            return WorkspaceState.Initial with
            {
                Data = buffer,
                ActiveSeries = series.ToImmutableList(),
                ViewportStartIndex = viewportStart,
                ViewportLength = viewportLength,
                ViewportRange = (20000, 130000),
            };
        }

        private static ChartSeries Series(string name, string pane, params string[] components)
        {
            var s = new ChartSeries();
            s.Config.Name = name;
            s.Config.Pane = pane;
            foreach (var c in components)
                s.Components.Add(new ComponentConfig { Name = c, DisplayName = c, IsVisible = true });
            return s;
        }

        // ── The basics ───────────────────────────────────────────────────

        [Fact]
        public void WithNoData_itSaysSoRatherThanDescribingAnEmptyChart()
        {
            Assert.Equal("No chart loaded.", ChartLayoutDescriber.Describe(WorkspaceState.Initial));
            Assert.Equal("No chart loaded.", ChartLayoutDescriber.Describe(null!));
        }

        [Fact]
        public void ItReportsHowMuchTimeIsOnScreenAndHowMuchIsLoaded()
        {
            // "60 bars in view of 200 loaded" tells you both what you can navigate now and what
            // panning left would reach. Either number alone leaves half the question open.
            string text = ChartLayoutDescriber.Describe(
                StateWith(barCount: 200, viewportLength: 60, series: Series("Candles", "Main", "Close")));

            Assert.Contains("60 bars in view of 200 loaded", text);
            Assert.Contains("January 1 2026", text);
        }

        [Fact]
        public void ItReportsThePriceRangeAndTheGridStep()
        {
            string text = ChartLayoutDescriber.Describe(
                StateWith(series: Series("Candles", "Main", "Close")));

            Assert.Contains("Y axis, price", text);
            Assert.Contains("20,000", text);
            Assert.Contains("130,000", text);
            Assert.Contains("between gridlines", text);
            Assert.Contains("linear scale", text);
        }

        [Fact]
        public void ItNamesTheScaleWhenLogarithmic()
        {
            var state = StateWith(series: Series("Candles", "Main", "Close")) with { IsLogScale = true };

            Assert.Contains("logarithmic scale", ChartLayoutDescriber.Describe(state));
        }

        // ── Panes ────────────────────────────────────────────────────────

        [Fact]
        public void ItCountsTheSeriesInTheMainPaneAndNamesEachIndicatorPane()
        {
            string text = ChartLayoutDescriber.Describe(StateWith(series: new[]
            {
                Series("Candles", "Main", "Close"),
                Series("EMA 50", "Main", "Line"),
                Series("RSI", "RSI", "Value"),
                Series("MACD", "MACD", "Hist"),
            }));

            Assert.Contains("Main pane: 2 series", text);
            Assert.Contains("2 indicator panes", text);
            Assert.Contains("RSI with 1 series", text);
            Assert.Contains("MACD with 1 series", text);
        }

        [Fact]
        public void ItSaysSoWhenThereAreNoIndicatorPanes()
        {
            // Silence would be ambiguous — "did it not check, or are there none?"
            string text = ChartLayoutDescriber.Describe(
                StateWith(series: Series("Candles", "Main", "Close")));

            Assert.Contains("No separate indicator panes", text);
        }

        [Fact]
        public void HiddenSeriesAreNotCountedAsPanesOnScreen()
        {
            var hidden = Series("RSI", "RSI", "Value");
            hidden.Config.IsVisible = false;

            string text = ChartLayoutDescriber.Describe(
                StateWith(series: new[] { Series("Candles", "Main", "Close"), hidden }));

            Assert.Contains("No separate indicator panes", text);
        }

        // ── What is switched off ─────────────────────────────────────────

        [Fact]
        public void ItReportsHiddenAndMutedComponents()
        {
            // This is why the summary is worth having beyond orientation: it explains a chart that
            // looks or sounds emptier than expected, and it tells the user there is something for
            // the recovery shortcuts to recover.
            var s = Series("Cipher B", "Cipher", "Buy", "Sell", "Wave");
            s.Components[0].IsVisible = false;
            s.Components[1].IsMuted = true;
            s.Components[2].IsMuted = true;

            string text = ChartLayoutDescriber.Describe(
                StateWith(series: new[] { Series("Candles", "Main", "Close"), s }));

            Assert.Contains("1 component hidden", text);
            Assert.Contains("2 components muted", text);
        }

        [Fact]
        public void ItStaysQuietAboutHiddenAndMutedWhenThereAreNone()
        {
            string text = ChartLayoutDescriber.Describe(
                StateWith(series: Series("Candles", "Main", "Close")));

            Assert.DoesNotContain("hidden", text);
            Assert.DoesNotContain("muted", text);
        }

        [Fact]
        public void ItMentionsHeikinAshiBecauseItChangesWhatEveryBarMeans()
        {
            var state = StateWith(series: Series("Candles", "Main", "Close")) with { IsHeikinAshi = true };

            Assert.Contains("Heikin Ashi", ChartLayoutDescriber.Describe(state));
        }

        // ── Wording ──────────────────────────────────────────────────────

        [Theory]
        [InlineData("1d", "1 day")]
        [InlineData("4h", "4 hours")]
        [InlineData("15m", "15 minutes")]
        [InlineData("1w", "1 week")]
        [InlineData("3d", "3 days")]
        public void TimeframesAreSpokenAsWordsNotCodes(string code, string spoken)
        {
            // "one d" is what a screen reader makes of "1d".
            Assert.Equal(spoken, ChartLayoutDescriber.SpokenTimeframe(code));
        }

        [Fact]
        public void AnUnrecognisedTimeframeIsPassedThroughRatherThanMangled()
        {
            Assert.Equal("weird", ChartLayoutDescriber.SpokenTimeframe("weird"));
            Assert.Equal("", ChartLayoutDescriber.SpokenTimeframe(""));
        }

        [Theory]
        [InlineData(110000, 20000)]   // rough 22,000 → fraction 2.2 → 2 x 10,000
        [InlineData(100, 20)]         // rough 20     → fraction 2.0 → 2 x 10
        [InlineData(4.5, 1)]          // rough 0.9    → fraction 9.0 → 10 x 0.1
        [InlineData(600, 100)]        // rough 120    → fraction 1.2 → 1 x 100
        public void TheGridStepMatchesWhatTheAxisActuallyLabels(double range, double expected)
        {
            // "about 18,432.7 between gridlines" is a number nobody asked for — but a ROUND number
            // that disagrees with the axis is worse, because it is confidently wrong about
            // something the user cannot independently check. This shares the renderer's exact
            // nice-number thresholds, and this test is what keeps them together.
            Assert.Equal(expected, ChartLayoutDescriber.GridStep(range));
        }

        [Fact]
        public void TheGridStepSurvivesADegenerateRange()
        {
            Assert.Equal(0, ChartLayoutDescriber.GridStep(0));
            Assert.Equal(0, ChartLayoutDescriber.GridStep(-5));
            Assert.Equal(0, ChartLayoutDescriber.GridStep(double.NaN));
        }

        [Fact]
        public void SeriesIsNotPluralisedIntoSeriess()
        {
            string text = ChartLayoutDescriber.Describe(
                StateWith(series: Series("Candles", "Main", "Close")));

            Assert.Contains("1 series", text);
            Assert.DoesNotContain("seriess", text);
            Assert.DoesNotContain("(s)", text);
        }
    }
}
