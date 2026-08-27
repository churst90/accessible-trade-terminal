using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A series with NO components, and a component index out of range.</b>
    ///
    /// <para>
    /// ── Why this file exists ───────────────────────────────────────────────────
    /// The A2 test-suite audit found that <b>no test anywhere constructed a series with an
    /// empty <c>Components</c> list</b>: every <c>ChartSeries</c> fixture in the speech tests
    /// added at least one <c>ComponentConfig</c>, and <c>FocusedComponentIndex</c> was never
    /// set out of range. So the paths that handle those states were entirely unguarded, and
    /// two of them were silent-failure paths.
    /// </para>
    ///
    /// <para>
    /// ── What was wrong on those paths ──────────────────────────────────────────
    /// <c>SpeechFormatter</c> returned <c>""</c> for a zero-component series <i>before</i> the
    /// <c>timestamp + prefixMessage + msg</c> concatenation, so the caller's PREFIX — a
    /// series-switch announcement, a pane label, "Home"/"End" — was discarded along with the
    /// value. Pressing Home on such a series said nothing at all rather than "Home".
    /// </para>
    /// </summary>
    public class EmptyComponentSpeechTests
    {
        /// <summary>A real series carrying data but declaring no components at all.</summary>
        private static ChartSeries ComponentlessSeries()
        {
            var config = new SeriesConfig { Id = "empty", Name = "Empty", IndicatorCode = "EMPTY" };
            // Deliberately no config.Components.Add(...) — that omission is the whole point.
            return new ChartSeries(config, new SeriesDataBuffer { SeriesId = "empty" });
        }

        private static WorkspaceState StateWith(ChartSeries series, int focusedComponentIndex = 0)
        {
            var bars = new TimeSeriesBuffer<Ohlcv>(new[]
            {
                new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 110, 95, 105, 10),
            });
            return WorkspaceState.Initial with
            {
                Data = bars,
                CurrentDataIndex = 0,
                ActiveSeries = ImmutableList.Create(series),
                FocusedSeriesId = series.Id,
                PrimarySeriesId = series.Id,
                FocusedComponentIndex = focusedComponentIndex,
            };
        }

        private static SpeechFormatter NewFormatter() => new();

        [Fact]
        public void A_componentless_series_still_speaks_the_callers_prefix()
        {
            // The defect: the early `return ""` threw the prefix away with the value.
            var series = ComponentlessSeries();
            var state = StateWith(series);

            string spoken = NewFormatter().FormatPointFeedback(
                state, isXMove: false, isYMove: false, series, state.Data[0], "Home.");

            Assert.Contains("Home", spoken, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_componentless_series_with_no_prefix_still_says_nothing()
        {
            // The other half, and it must stay true: there is genuinely no value to read, so
            // inventing one would be worse than silence. Only the PREFIX was being lost.
            var series = ComponentlessSeries();
            var state = StateWith(series);

            string spoken = NewFormatter().FormatPointFeedback(
                state, isXMove: false, isYMove: false, series, state.Data[0], "");

            Assert.Equal("", spoken);
        }

        [Theory]
        [InlineData(-5)]
        [InlineData(0)]
        [InlineData(99)]
        public void A_component_index_out_of_range_does_not_throw(int index)
        {
            // FocusedComponentIndex was never set out of range by any fixture, so nothing
            // checked that the formatter survives it. A throw here would take out the whole
            // speech path on a keypress.
            var series = ComponentlessSeries();
            var state = StateWith(series, focusedComponentIndex: index);

            var ex = Record.Exception(() => NewFormatter().FormatPointFeedback(
                state, isXMove: false, isYMove: false, series, state.Data[0], "Home."));

            Assert.Null(ex);
        }

        [Fact]
        public void Navigation_feedback_survives_a_componentless_series()
        {
            // HandleNavigationFeedback was never driven against one either.
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            var manager = new NavigationFeedbackManager(speech, NewFormatter());
            var series = ComponentlessSeries();

            var ex = Record.Exception(() => manager.HandleNavigationFeedback(
                StateWith(series), isXMove: true, isYMove: false, prefixMessage: ""));

            Assert.Null(ex);
        }

        [Fact]
        public void Navigation_feedback_survives_empty_data()
        {
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            var manager = new NavigationFeedbackManager(speech, NewFormatter());

            var ex = Record.Exception(() => manager.HandleNavigationFeedback(
                WorkspaceState.Initial, isXMove: true, isYMove: false, prefixMessage: ""));

            Assert.Null(ex);
        }

        [Fact]
        public void An_arrow_key_before_data_loads_says_so_rather_than_nothing()
        {
            // This was a bare `return`, so every arrow key before data arrived was total
            // silence and the user could not tell an empty chart from a dead keyboard.
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            var manager = new NavigationFeedbackManager(speech, NewFormatter());

            manager.HandleNavigationFeedback(
                WorkspaceState.Initial, isXMove: true, isYMove: false,
                prefixMessage: "", isUserInitiated: true);

            speech.Received().Speak(
                Arg.Is<string>(m => m.Contains("No chart data", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<bool>(), Arg.Any<SpeechChannel>());
        }

        [Fact]
        public void A_state_change_that_nobody_asked_for_stays_quiet()
        {
            // Vacuity check for the test above, and a real requirement: a state arriving on
            // its own has no keypress to answer, and announcing "No chart data yet" every time
            // the store ticks would be its own defect.
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            var manager = new NavigationFeedbackManager(speech, NewFormatter());

            manager.HandleNavigationFeedback(
                WorkspaceState.Initial, isXMove: true, isYMove: false,
                prefixMessage: "", isUserInitiated: false);

            speech.DidNotReceiveWithAnyArgs().Speak(default!, default, default);
        }

        [Fact]
        public void An_unresolvable_focused_series_says_why_rather_than_nothing()
        {
            var speech = Substitute.For<ISpeechFeedbackRouter>();
            var manager = new NavigationFeedbackManager(speech, NewFormatter());

            var state = StateWith(ComponentlessSeries()) with
            {
                FocusedSeriesId = "does-not-exist",
                PrimarySeriesId = "does-not-exist",
            };

            manager.HandleNavigationFeedback(state, isXMove: true, isYMove: false,
                prefixMessage: "", isUserInitiated: true);

            speech.Received().Speak(
                Arg.Is<string>(m => m.Contains("No series in focus", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<bool>(), Arg.Any<SpeechChannel>());
        }
    }
}
