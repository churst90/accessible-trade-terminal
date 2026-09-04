using System.Collections.Immutable;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

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
        // UTC, explicitly — that is what TimestampParser hands every real bar, and since
        // 2026-08-27 every spoken timestamp is converted to the user's zone before it is read.
        private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

        private static ChartSeries SeriesWithId(string id, string name, string pane, params string[] components)
        {
            var cfg = new SeriesConfig { Id = id, Name = name, Pane = pane };
            foreach (var c in components)
                cfg.Components.Add(new ComponentConfig { Name = c, DisplayName = c, IsVisible = true });
            return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = id });
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

        // ── DescribePane (Alt+Shift+/) — the PANE, not the chart ─────────

        /// <summary>
        /// Cody, 2026-09-04: <i>"can we have a shortcut … that will read out the meta data for
        /// that pane like 'main pane. Y axis price. X axis time. Price ranges from x to y, with
        /// an interval of z. Time ranges from x to y with an interval of z'".</i>
        ///
        /// <para>
        /// The reason it is a separate key from Ctrl+Alt+Shift+Y: a spoken VALUE is meaningless
        /// without the scale it sits against, and "what is on this chart" is asked once on
        /// arrival while "what am I reading against" is asked on every move into a new band.
        /// </para>
        /// </summary>
        [Fact]
        public void DescribePane_namesTheAxes_theirRanges_andTheGridlineStep()
        {
            var state = StateWith(200, 0, 60,
                Series("Candles", "Main", "Close"),
                Series("RSI", "Pane_RSI", "RSI"));

            string text = ChartLayoutDescriber.DescribePane(state with { FocusedSeriesId = null }, "1d");

            Assert.Contains("Main pane", text);
            Assert.Contains("Y axis, price", text);
            Assert.Contains("X axis, time", text);
            Assert.Contains("between gridlines", text);
            Assert.Contains("1 day", text);   // the X axis's step, spoken not spelled
        }

        /// <summary>
        /// It describes THE PANE THE CURSOR IS IN. Describing the Main pane wherever you stood
        /// would be the failure mode that makes the key useless exactly when it is needed.
        /// </summary>
        [Fact]
        public void DescribePane_followsTheCursorIntoAnIndicatorPane()
        {
            var rsi = SeriesWithId("rsi", "RSI", "Pane_RSI", "RSI");
            var state = StateWith(200, 0, 60, Series("Candles", "Main", "Close"), rsi) with
            {
                FocusedSeriesId = "rsi",
                PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                    .Add("Main", (20000, 130000))
                    .Add("Pane_RSI", (0, 100)),
            };

            string text = ChartLayoutDescriber.DescribePane(state, "1d");

            Assert.Contains("RSI pane", text);
            Assert.Contains("2 of 2", text);
            Assert.Contains("0.00 to 100.00", text);
            Assert.DoesNotContain("Y axis, price", text);
        }

        /// <summary>
        /// The Y range is read from the SAME dictionary the renderer scales the pane with, so
        /// the numbers spoken are the numbers drawn. Reading the viewport range for every pane
        /// would confidently describe an oscillator as running from 20,000 to 130,000.
        /// </summary>
        [Fact]
        public void DescribePane_readsTheRendererOwnPaneRange_notTheViewportRange()
        {
            var rsi = SeriesWithId("rsi", "RSI", "Pane_RSI", "RSI");
            var state = StateWith(200, 0, 60, rsi) with
            {
                FocusedSeriesId = "rsi",
                PaneRanges = ImmutableDictionary<string, (double Min, double Max)>.Empty
                    .Add("Pane_RSI", (0, 100)),
            };

            string text = ChartLayoutDescriber.DescribePane(state, "1d");

            Assert.DoesNotContain("130,000", text);
            Assert.DoesNotContain("20,000", text);
        }

        /// <summary>
        /// The ordinal follows the rule the rest of the file uses: a count is only information
        /// when it tells the user a key has somewhere to go. Never "1 of 1".
        /// </summary>
        [Fact]
        public void DescribePane_dropsTheOrdinalOnAOnePaneChart()
        {
            var state = StateWith(200, 0, 60, Series("Candles", "Main", "Close"));

            Assert.DoesNotContain("1 of 1", ChartLayoutDescriber.DescribePane(state, "1d"));
        }

        [Fact]
        public void DescribePane_withNoData_saysSo()
            => Assert.Equal("No chart loaded.", ChartLayoutDescriber.DescribePane(WorkspaceState.Initial));

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

            // The date is read in the USER'S zone, so the literal "January 1 2026" this used to
            // assert only held on a box at or east of UTC. Derived here through the BCL rather
            // than through SpeechTimeFormatter on purpose: asking production what it would print
            // and then asserting it printed that is not a test.
            string expectedDate = TimeZoneInfo.ConvertTimeFromUtc(Start, TimeZoneInfo.Local)
                .ToString("MMMM d yyyy", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(expectedDate, text);
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
