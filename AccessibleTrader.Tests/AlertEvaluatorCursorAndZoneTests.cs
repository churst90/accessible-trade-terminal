using System.Collections.Immutable;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Two ways an indicator alert lied about the market.
    ///
    /// <para><b>It watched the reading cursor, not the live bar.</b>
    /// <c>AlertEvaluator</c> and <c>AlertOrchestrator</c> both read
    /// <c>state.CurrentDataIndex</c>, which is the user's KEYBOARD cursor:
    /// <c>PointNavigationStrategy</c> moves it on every arrow key and <c>ViewportReducer</c>
    /// explicitly refuses to move it on live data ("Preserve user focus — do NOT move the
    /// cursor based on live data arrivals"). Price alerts used <c>data[^1]</c>; indicator
    /// alerts evaluated whatever bar the user happened to be parked on. Arrow-key back 200
    /// bars to inspect history and "RSI crosses 70" watched bar N-200 forever.</para>
    ///
    /// <para>Worse, <c>_previousValues</c> was snapshotted at the OLD cursor and compared
    /// against the value at the NEW one — so moving the cursor across a threshold between two
    /// ticks <b>synthesised a crossover that never happened in the market</b>. An alert fired,
    /// and spoke, about a price movement that did not occur.</para>
    ///
    /// <para><b>Zone conditions were level tests, not transitions.</b>
    /// <c>EvaluateZone</c> took <c>double current, double prev</c> and referenced neither; the
    /// body was <c>return entering ? inZone : !inZone;</c>. An EntersZone alert fired on EVERY
    /// bar the indicator sat in the zone — RSI parked above 70 meant one spoken alert per bar
    /// — and an ExitsZone alert fired on every bar it was NOT in the zone, i.e. almost always.
    /// The comment above it described transition semantics the code did not implement.</para>
    /// </summary>
    public class AlertEvaluatorCursorAndZoneTests
    {
        private static AlertEvaluator NewEvaluator() =>
            new(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());

        private static Ohlcv Bar(double close, int hour) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hour),
                close, close, close, close, 0);

        /// <summary>
        /// A chart of <paramref name="count"/> bars with an RSI whose value at index i is
        /// simply i, so reading the wrong index is unmistakable — and a navigation cursor
        /// parked somewhere other than the live bar.
        /// </summary>
        private static WorkspaceState StateWithRsi(int count, int cursorAt)
        {
            var buffer = new SeriesDataBuffer { SeriesId = "rsi-1" };
            buffer.ComponentData["Rsi"] = Enumerable.Range(0, count).Select(i => (double)i).ToArray();
            var config = new SeriesConfig { Id = "rsi-1", Name = "RSI", IndicatorCode = "RSI" };
            config.Components.Add(new ComponentConfig { Name = "Rsi", DisplayName = "RSI" });

            var bars = Enumerable.Range(0, count).Select(i => Bar(100 + i, i)).ToArray();

            return WorkspaceState.Initial with
            {
                SymbolDisplayName = "BTC/USD",
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.Create(new ChartSeries(config, buffer)),
                CurrentDataIndex = cursorAt,
            };
        }

        private static AlertDefinition RsiCross(double threshold) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "RSI watch",
            IsActive = true,
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove,
            Threshold = threshold,
            IndicatorCode = "RSI",
            ComponentName = "Rsi",
        };

        [Fact]
        public void An_indicator_alert_reads_the_live_bar_however_far_back_the_cursor_is_parked()
        {
            // 700 bars, RSI[i] = i, so the live reading is 699 and the cursor's is 60.
            var state = StateWithRsi(700, cursorAt: 60);
            var alert = RsiCross(500);
            var prev = new Dictionary<string, double> { ["RSI.Rsi"] = 400 };

            var fired = NewEvaluator()
                .EvaluateAlerts(new[] { alert }, state, state.Data[^1], state.Data[^2], prev)
                .ToList();

            var f = Assert.Single(fired);
            Assert.Equal(699, f.TriggeringValue);   // the market, not the cursor
        }

        [Fact]
        public void Parking_the_cursor_below_the_threshold_does_not_silence_a_live_crossing()
        {
            // The user arrow-keys back to inspect history. Their alert must keep watching.
            var atLive = StateWithRsi(700, cursorAt: 699);
            var parked = StateWithRsi(700, cursorAt: 10);
            var prev = new Dictionary<string, double> { ["RSI.Rsi"] = 400 };

            Assert.Single(NewEvaluator().EvaluateAlerts(
                new[] { RsiCross(500) }, atLive, atLive.Data[^1], atLive.Data[^2], prev));
            Assert.Single(NewEvaluator().EvaluateAlerts(
                new[] { RsiCross(500) }, parked, parked.Data[^1], parked.Data[^2], prev));
        }

        [Fact]
        public void The_fixture_really_does_park_the_cursor_away_from_the_live_bar()
        {
            // Vacuity check: with the cursor at the live bar the buggy and correct readings
            // are identical, and everything above would pass for the wrong reason.
            var parked = StateWithRsi(700, cursorAt: 60);
            Assert.NotEqual(parked.Data.Count - 1, parked.CurrentDataIndex);
        }

        // ── Zone transitions ─────────────────────────────────────────────────

        private static AlertDefinition ZoneAlert(AlertCondition condition, AlertZone zone) => new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "RSI zone",
            IsActive = true,
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator,
            Condition = condition,
            IndicatorCode = "RSI",
            ComponentName = "Rsi",
            Zone = zone,
        };

        /// <summary>An RSI series pinned to one value across the whole chart, so the ZONE is
        /// constant and only the transition logic can make a difference.</summary>
        private static WorkspaceState StateWithFlatRsi(double value, int count = 50)
        {
            var buffer = new SeriesDataBuffer { SeriesId = "rsi-1" };
            buffer.ComponentData["Rsi"] = Enumerable.Repeat(value, count).ToArray();
            var config = new SeriesConfig { Id = "rsi-1", Name = "RSI", IndicatorCode = "RSI" };
            config.Components.Add(new ComponentConfig { Name = "Rsi", DisplayName = "RSI" });

            var bars = Enumerable.Range(0, count).Select(i => Bar(100 + i, i)).ToArray();

            return WorkspaceState.Initial with
            {
                SymbolDisplayName = "BTC/USD",
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ActiveSeries = ImmutableList.Create(new ChartSeries(config, buffer)),
                CurrentDataIndex = count - 1,
            };
        }

        [Fact]
        public void An_indicator_sitting_in_a_zone_does_not_fire_once_per_bar()
        {
            // RSI parked at 85 is overbought on every bar. Under the old level test that was
            // one spoken alert per bar for as long as it stayed there.
            var evaluator = NewEvaluator();
            var alert = ZoneAlert(AlertCondition.EntersZone, AlertZone.Overbought);
            var state = StateWithFlatRsi(85);
            var none = new Dictionary<string, double>();

            int fires = 0;
            for (int poll = 0; poll < 20; poll++)
            {
                fires += evaluator.EvaluateAlerts(
                    new[] { alert }, state, state.Data[^1], state.Data[^2], none).Count();
            }

            // At most one — the transition into the zone, if the first evaluation counted as
            // one. Never twenty.
            Assert.True(fires <= 1, $"an alert sitting in its zone fired {fires} times");
        }

        [Fact]
        public void An_ExitsZone_alert_does_not_fire_on_every_bar_it_is_simply_not_in_the_zone()
        {
            // The other half, and the worse one: `!inZone` is true almost always, so this
            // fired essentially forever.
            var evaluator = NewEvaluator();
            var alert = ZoneAlert(AlertCondition.ExitsZone, AlertZone.Overbought);
            var state = StateWithFlatRsi(50);   // neutral — never in the zone
            var none = new Dictionary<string, double>();

            int fires = 0;
            for (int poll = 0; poll < 20; poll++)
            {
                fires += evaluator.EvaluateAlerts(
                    new[] { alert }, state, state.Data[^1], state.Data[^2], none).Count();
            }

            Assert.Equal(0, fires);
        }

        [Fact]
        public void The_first_evaluation_has_no_before_and_so_is_not_a_transition()
        {
            // Firing here turns "RSI is overbought" into an alert the user never asked for,
            // on the very first bar after they open the chart.
            var evaluator = NewEvaluator();
            var alert = ZoneAlert(AlertCondition.EntersZone, AlertZone.Overbought);
            var state = StateWithFlatRsi(85);
            var none = new Dictionary<string, double>();

            Assert.Empty(evaluator.EvaluateAlerts(
                new[] { alert }, state, state.Data[^1], state.Data[^2], none));
        }
    }
}
