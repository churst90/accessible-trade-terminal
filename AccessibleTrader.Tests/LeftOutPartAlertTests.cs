using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cody, 2026-11-17 ("7. Yes"): the Alerts dialog lists a left-out part, so an alert can
    /// watch it without putting it on the chart. A left-out part is still computed, but every
    /// alert path looked parts up in <c>Components</c> alone: the evaluator said the indicator
    /// "has no X any more", a zone alert read every level instead of the part's own, and the
    /// bar-before snapshot skipped the part, so a cross on it could never be known. These pin
    /// each path finding the part among the left-out ones too.
    /// </summary>
    public class LeftOutPartAlertTests
    {
        private static Ohlcv Bar(int hour) =>
            new(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc).AddHours(hour), 100, 100, 100, 100, 0);

        /// <summary>
        /// A "WT" series whose "Wave" is on the chart and whose "Signal" is left out. Signal's
        /// value at the last bar is <paramref name="signalNow"/>, the bar before 40.
        /// </summary>
        private static WorkspaceState State(double signalNow, int bars = 10)
        {
            var buffer = new SeriesDataBuffer { SeriesId = "wt-1" };
            buffer.ComponentData["Wave"] = Enumerable.Repeat(10.0, bars).ToArray();
            buffer.ComponentData["Signal"] = Enumerable.Repeat(40.0, bars - 1).Append(signalNow).ToArray();
            var config = new SeriesConfig { Id = "wt-1", Name = "WT", IndicatorCode = "WT" };
            config.Components.Add(new ComponentConfig { Name = "Wave", DisplayName = "Wave" });
            config.ExcludedComponents.Add(new ComponentConfig { Name = "Signal", DisplayName = "Signal", IsEnabled = false });

            return WorkspaceState.Initial with
            {
                SymbolDisplayName = "BTC/USD",
                Data = new TimeSeriesBuffer<Ohlcv>(Enumerable.Range(0, bars).Select(Bar).ToArray()),
                ActiveSeries = ImmutableList.Create(new ChartSeries(config, buffer)),
                CurrentDataIndex = bars - 1,
                InitStatus = InitializationStatus.Ready,
            };
        }

        private static AlertDefinition SignalCross() => new()
        {
            Id = "signal-50",
            Name = "Signal over 50",
            IsActive = true,
            Delivery = AlertDelivery.Speech,
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove,
            Threshold = 50,
            IndicatorCode = "WT",
            ComponentName = "Signal",
        };

        [Fact]
        public void An_alert_on_a_left_out_part_fires_on_its_cross()
        {
            var state = State(signalNow: 60);
            var eval = new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer());
            string? degraded = null;
            eval.EvaluationDegraded += (_, reason) => degraded = reason;

            var fired = eval.EvaluateAlerts(new[] { SignalCross() }, state, state.Data[^1], state.Data[^2],
                new Dictionary<string, double> { ["WT.Signal"] = 40 }).ToList();

            Assert.Null(degraded);
            Assert.Equal(60, Assert.Single(fired).TriggeringValue);
        }

        [Fact]
        public void The_chart_alert_loop_keeps_a_left_out_parts_bar_before_so_its_cross_fires()
        {
            // The first state seeds the bar-before values, the second crosses, the third falls
            // back, the fourth crosses again. Without the left-out part in the first-tick seed the
            // first cross is missed; without it in the per-tick snapshot the second is.
            var store = new MockWorkspaceStore();
            var bus = new SpyEventBus();
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition> { SignalCross() });
            var orch = new AlertOrchestrator(store,
                new AlertEvaluator(new SdkCandlePatternAnalyzer(), new IndicatorContextAnalyzer()),
                bus, library, NullLogger<AlertOrchestrator>.Instance);
            orch.Start();

            store.EmitState(State(signalNow: 40, bars: 10));
            store.EmitState(State(signalNow: 60, bars: 11));
            store.EmitState(State(signalNow: 40, bars: 12));
            store.EmitState(State(signalNow: 60, bars: 13));

            var fired = bus.Log.OfType<AlertFiredEvent>().ToList();
            Assert.Equal(2, fired.Count);
            Assert.All(fired, f => Assert.Equal("signal-50", f.Alert.Definition.Id));
        }

        [Fact]
        public void A_zone_alert_on_a_left_out_part_reads_that_parts_own_lines()
        {
            // Wave answers to the 80 line, Signal to the 90 line. Not finding Signal read every
            // overbought line, so the zone started at 80.
            var config = new SeriesConfig { Id = "wt-1", Name = "WT", IndicatorCode = "WT" };
            config.Levels.Add(new LevelConfig { Name = "Overbought", Value = 80 });
            config.Levels.Add(new LevelConfig { Name = "Extreme Overbought", Value = 90 });
            config.Components.Add(new ComponentConfig { Name = "Wave", SubscribedLevelNames = new[] { "Overbought" } });
            config.ExcludedComponents.Add(new ComponentConfig { Name = "Signal", SubscribedLevelNames = new[] { "Extreme Overbought" } });

            Assert.Equal(90, AlertZones.Threshold(config, "Signal", AlertZone.Overbought));
            Assert.Equal(80, AlertZones.Threshold(config, "Wave", AlertZone.Overbought));
        }
    }
}
