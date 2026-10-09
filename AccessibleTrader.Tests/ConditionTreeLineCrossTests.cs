using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The condition tree's line-cross and higher-timeframe paths, as an advanced alert uses them.
    /// Cody, 2026-10: "how do I get an alert when price touches the 50-week SMA?" — and the tree
    /// could not say it. The editor never offered the line-cross operators; the line a leaf
    /// crossed could only be another component of the SAME indicator; two SMAs on one chart were
    /// one SMA to the evaluator (the first by code); an HTF leaf ran its indicator with DEFAULT
    /// parameters (so "1w SMA 50" was 1w SMA 20); nothing in the alert path loaded HTF data at
    /// all; and an HTF indicator leaf with no computed indicator quietly tested the weekly CLOSE.
    /// Each test below pins one of those.
    /// </summary>
    public class ConditionTreeLineCrossTests
    {
        // ── fixtures ─────────────────────────────────────────────────────────

        private static readonly DateTime T0 = new(2026, 1, 5); // a Monday

        private static readonly IReadOnlyDictionary<string, double> P20 = new Dictionary<string, double> { ["lookbackPeriods"] = 20 };
        private static readonly IReadOnlyDictionary<string, double> P50 = new Dictionary<string, double> { ["lookbackPeriods"] = 50 };

        /// <summary>Daily bars from <paramref name="start"/> (default <see cref="T0"/>); high = close + 1, low = close - 1.</summary>
        internal static List<Ohlcv> Daily(double[] closes, DateTime? start = null)
        {
            var t = start ?? T0;
            var list = new List<Ohlcv>(closes.Length);
            for (int i = 0; i < closes.Length; i++)
                list.Add(new Ohlcv(t.AddDays(i), closes[i], closes[i] + 1, closes[i] - 1, closes[i], 100));
            return list;
        }

        private static List<Ohlcv> Daily(params double[] closes) => Daily(closes, null);

        internal static ChartSeries Series(string code, double period, double[] values, string component = "Sma")
        {
            var config = new SeriesConfig
            {
                IndicatorCode = code,
                Name = $"{code} {period}",
                FriendlyName = $"{code} {period}",
                Parameters = new Dictionary<string, double> { ["lookbackPeriods"] = period },
            };
            return new ChartSeries(config, new SeriesDataBuffer
            {
                SeriesId = config.Id,
                ComponentData = new Dictionary<string, double[]> { [component] = values },
            });
        }

        internal static WorkspaceState State(IReadOnlyList<Ohlcv> bars, params ChartSeries[] series) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1d"),
                SymbolDisplayName = "BTC/USDT",
                Data = new TimeSeriesBuffer<Ohlcv>(bars.ToList()),
                CurrentDataIndex = bars.Count - 1,
                InitStatus = InitializationStatus.Ready,
                ActiveSeries = System.Collections.Immutable.ImmutableList.CreateRange(series),
            };

        internal sealed class Catalog : ISignalCatalog
        {
            public IReadOnlyList<SignalDescriptor> All { get; } = new[]
            {
                new SignalDescriptor("CANDLES.body", "CANDLES", "body", SignalKind.Line, "Candles — Body (close)"),
                new SignalDescriptor("CANDLES.upper_wick", "CANDLES", "upper_wick", SignalKind.Line, "Candles — Upper Wick (high)"),
                new SignalDescriptor("CANDLES.lower_wick", "CANDLES", "lower_wick", SignalKind.Line, "Candles — Lower Wick (low)"),
                new SignalDescriptor("Sma.Sma", "Sma", "Sma", SignalKind.Line, "SMA — Sma"),
                new SignalDescriptor("Ema.Ema", "Ema", "Ema", SignalKind.Line, "EMA — Ema"),
                // Never on the editor tests' chart: a line and a marker, for operator reconcile.
                new SignalDescriptor("Cipher.Wave", "Cipher", "Wave", SignalKind.Line, "Cipher — Wave"),
                new SignalDescriptor("Cipher.Buy", "Cipher", "Buy", SignalKind.MarkerFire, "Cipher — Buy"),
            };
            public SignalDescriptor? GetById(string id) => All.FirstOrDefault(d => d.Id == id);
            public IReadOnlyList<SignalDescriptor> GetForIndicator(string code) =>
                All.Where(d => string.Equals(d.IndicatorCode, code, StringComparison.OrdinalIgnoreCase)).ToList();
            public void Refresh() { }
        }

        /// <summary>A timeframe service whose cache the test fills by hand, whose loads wait on
        /// <see cref="Gate"/>, and which records every load it is asked for.</summary>
        internal sealed class Mtf : IMultiTimeframeDataService
        {
            public Dictionary<string, IReadOnlyList<Ohlcv>> Bars { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, Dictionary<string, double[]>> Indicators { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> Requests { get; } = new();
            public TaskCompletionSource Gate { get; set; } = Opened();

            internal static TaskCompletionSource Opened()
            {
                var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                t.SetResult();
                return t;
            }

            public static string Key(string tf, string code, IReadOnlyDictionary<string, double>? p = null) =>
                $"{tf}|{code}|{(p == null ? "" : string.Join(",", p.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")))}";

            public async Task<IReadOnlyList<Ohlcv>> GetBarsAsync(string market, string provider, string symbol, string timeframe, int count)
            {
                lock (Requests) Requests.Add($"bars {timeframe}");
                await Gate.Task;
                return GetCachedBars(provider, symbol, timeframe);
            }

            public IReadOnlyList<Ohlcv> GetCachedBars(string provider, string symbol, string timeframe) =>
                Bars.TryGetValue(timeframe, out var b) ? b : Array.Empty<Ohlcv>();

            public void Clear() { Bars.Clear(); Indicators.Clear(); }

            public async Task PrewarmIndicatorAsync(string market, string provider, string symbol, string timeframe,
                string indicatorCode, Dictionary<string, object> parameters, int count)
            {
                lock (Requests) Requests.Add($"indicator {timeframe} {indicatorCode} " +
                    string.Join(",", parameters.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
                await Gate.Task;
            }

            public Dictionary<string, double[]>? GetCachedIndicator(string provider, string symbol, string timeframe, string indicatorCode) =>
                Indicators.TryGetValue(Key(timeframe, indicatorCode), out var v) ? v : null;

            public Dictionary<string, double[]>? GetCachedIndicator(string provider, string symbol, string timeframe,
                string indicatorCode, IReadOnlyDictionary<string, double>? parameters) =>
                Indicators.TryGetValue(Key(timeframe, indicatorCode, parameters is { Count: > 0 } ? parameters : null), out var v) ? v : null;
        }

        private static ConditionLeaf Cross(string first, string second, bool above = true,
            IReadOnlyDictionary<string, double>? p1 = null, IReadOnlyDictionary<string, double>? p2 = null,
            string? tf = null, string? secondTf = null) =>
            new("x", first, above ? LeafOperator.CrossesAboveLine : LeafOperator.CrossesBelowLine,
                Timeframe: tf, SecondSignalDescriptorId: second,
                Parameters: p1, SecondParameters: p2, SecondTimeframe: secondTf);

        // ── chart timeframe: price against an indicator, indicator against indicator ──

        [Fact]
        public void Close_crosses_above_and_below_an_sma_on_the_chart()
        {
            var bars = Daily(10, 12);
            var state = State(bars, Series("Sma", 50, new[] { 11.0, 11.0 }));
            var eval = new ConditionEvaluator(new Catalog());

            Assert.True(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma"), bars, state).OverallTrue,
                "close 10 -> 12 against an SMA at 11 is a cross above");
            Assert.False(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", above: false), bars, state).OverallTrue);

            var down = Daily(12, 10);
            Assert.True(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", above: false), down, State(down, Series("Sma", 50, new[] { 11.0, 11.0 }))).OverallTrue);
        }

        [Fact]
        public void An_sma_crossing_price_reads_the_bars_when_no_candle_series_carries_them()
        {
            // The background tab monitor recomputes every series through the indicator engine,
            // where the Candles pseudo-indicator yields nothing — so "close crosses the SMA" had
            // no close to read there. With no Candles series at all, the close is the bars'.
            var bars = Daily(12, 10);
            var state = State(bars, Series("Sma", 50, new[] { 11.0, 11.0 }));
            var eval = new ConditionEvaluator(new Catalog());

            Assert.True(eval.Evaluate(Cross("Sma.Sma", "CANDLES.body"), bars, state).OverallTrue,
                "SMA at 11 against a close falling 12 -> 10 is the SMA crossing above price");
        }

        [Fact]
        public void Sma_20_crosses_sma_50_reads_the_two_instances_not_the_first_twice()
        {
            // SMA 50 is FIRST on the chart: an unbound leaf would read it for both lines and could
            // never cross. Bound by parameters, each line is its own SMA.
            var bars = Daily(10, 10);
            var sma50 = Series("Sma", 50, new[] { 10.0, 11.0 });
            var sma20 = Series("Sma", 20, new[] { 9.0, 12.0 });
            var state = State(bars, sma50, sma20);
            var eval = new ConditionEvaluator(new Catalog());

            Assert.True(eval.Evaluate(Cross("Sma.Sma", "Sma.Sma", p1: P20, p2: P50), bars, state).OverallTrue,
                "SMA 20 9 -> 12 against SMA 50 10 -> 11 is a cross above");
            Assert.False(eval.Evaluate(Cross("Sma.Sma", "Sma.Sma", p1: P50, p2: P20), bars, state).OverallTrue);
        }

        [Fact]
        public void An_instance_that_is_no_longer_on_the_chart_is_said_not_substituted()
        {
            var bars = Daily(10, 12);
            var state = State(bars, Series("Sma", 60, new[] { 11.0, 11.0 }));
            var eval = new ConditionEvaluator(new Catalog());

            var result = eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", p2: P50), bars, state);

            Assert.False(result.OverallTrue, "the SMA 60 must not stand in for the SMA 50 the leaf names");
            Assert.NotNull(eval.LastDegradation);
            Assert.Contains("lookbackPeriods 50", eval.LastDegradation!);
        }

        // ── higher timeframe: never a different series than the one chosen ──

        [Fact]
        public void Htf_indicator_leaf_with_only_raw_bars_cached_does_not_test_the_close()
        {
            // Weekly closes are far above 100; the weekly SMA was never computed. "1w SMA > 100"
            // must not come back true on the strength of the weekly CLOSE.
            var mtf = new Mtf();
            mtf.Bars["1w"] = new List<Ohlcv>
            {
                new(T0.AddDays(-14), 500, 500, 500, 500, 0),
                new(T0.AddDays(-7), 500, 500, 500, 500, 0),
            };
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var leaf = new ConditionLeaf("w", "Sma.Sma", LeafOperator.GreaterThan, Value: 100, Timeframe: "1w");
            var bars = Daily(10, 11, 12);

            var result = eval.Evaluate(leaf, bars, State(bars));

            Assert.False(result.OverallTrue);
            Assert.NotNull(eval.LastDegradation);
            Assert.Contains("1w", eval.LastDegradation!);
        }

        [Fact]
        public void Htf_indicator_leaf_reads_the_instance_it_is_bound_to()
        {
            // The default SMA (20) is computed and above 100; the SMA 50 the leaf names is at 80.
            var mtf = new Mtf();
            mtf.Bars["1w"] = new List<Ohlcv> { new(T0.AddDays(-14), 500, 500, 500, 500, 0), new(T0.AddDays(-7), 500, 500, 500, 500, 0) };
            mtf.Indicators[Mtf.Key("1w", "Sma")] = new() { ["Sma"] = new[] { 150.0, 150.0 } };
            mtf.Indicators[Mtf.Key("1w", "Sma", P50)] = new() { ["Sma"] = new[] { 80.0, 80.0 } };
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var bars = Daily(10, 11, 12);

            var bound = new ConditionLeaf("w", "Sma.Sma", LeafOperator.GreaterThan, Value: 100, Timeframe: "1w", Parameters: P50);
            Assert.False(eval.Evaluate(bound, bars, State(bars)).OverallTrue, "the 1w SMA 50 is 80, not above 100");

            var unbound = bound with { Parameters = null };
            Assert.True(eval.Evaluate(unbound, bars, State(bars)).OverallTrue, "vacuity: the default instance IS above 100");
        }

        [Fact]
        public void Htf_leaf_inside_a_sequence_does_not_read_the_chart_timeframe_series()
        {
            // The chart's daily SMA is above 100 on every bar; the leaf asks about the WEEKLY SMA,
            // which nothing has computed. Inside a Sequence the leaf used to read the daily series.
            var mtf = new Mtf();
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var leaf = new ConditionLeaf("w", "Sma.Sma", LeafOperator.GreaterThan, Value: 100, Timeframe: "1w");
            var seq = new ConditionGroup("g", LogicOperator.Sequence, new ConditionNode[] { leaf });
            var bars = Daily(10, 11, 12);

            var result = eval.Evaluate(seq, bars, State(bars, Series("Sma", 50, new[] { 200.0, 200, 200 })));

            Assert.False(result.OverallTrue);
            Assert.NotNull(eval.LastDegradation);
        }

        [Fact]
        public void Daily_close_crosses_the_weekly_sma_read_as_a_step_as_of_each_bar()
        {
            // Weekly SMA 50: 100 through the week of T0-7, 200 from the week opening T0. The day
            // before T0 sees 100; the day after sees 200. Close 150 -> 180 is therefore a cross
            // BELOW the weekly line — and would be no cross at all if both bars read the latest 200.
            var mtf = new Mtf();
            mtf.Bars["1w"] = new List<Ohlcv>
            {
                new(T0.AddDays(-14), 1, 1, 1, 1, 0),
                new(T0.AddDays(-7), 1, 1, 1, 1, 0),
                new(T0, 1, 1, 1, 1, 0),
            };
            mtf.Indicators[Mtf.Key("1w", "Sma", P50)] = new() { ["Sma"] = new[] { 50.0, 100.0, 200.0 } };
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var bars = new List<Ohlcv>
            {
                new(T0.AddDays(-1), 150, 151, 149, 150, 1),
                new(T0.AddDays(1), 180, 181, 179, 180, 1),
            };
            var state = State(bars);

            Assert.True(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", above: false, p2: P50, secondTf: "1w"), bars, state).OverallTrue,
                $"expected a cross below the 1w SMA 50; degradation: {eval.LastDegradation}");
            Assert.False(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", above: true, p2: P50, secondTf: "1w"), bars, state).OverallTrue);
        }

        [Fact]
        public void Weekly_close_crosses_the_weekly_sma_on_the_weekly_bars()
        {
            var mtf = new Mtf();
            mtf.Bars["1w"] = new List<Ohlcv>
            {
                new(T0.AddDays(-14), 90, 90, 90, 90, 0),
                new(T0.AddDays(-7), 110, 110, 110, 110, 0),
            };
            mtf.Indicators[Mtf.Key("1w", "Sma", P50)] = new() { ["Sma"] = new[] { 100.0, 100.0 } };
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var bars = Daily(1, 1, 1);

            Assert.True(eval.Evaluate(Cross("CANDLES.body", "Sma.Sma", p2: P50, tf: "1w"), bars, State(bars)).OverallTrue,
                $"weekly close 90 -> 110 crosses a weekly SMA of 100; degradation: {eval.LastDegradation}");
        }

        [Fact]
        public void A_weekly_condition_crossing_a_daily_line_is_refused_out_loud()
        {
            var mtf = new Mtf();
            mtf.Bars["1w"] = new List<Ohlcv> { new(T0.AddDays(-14), 90, 90, 90, 90, 0), new(T0.AddDays(-7), 110, 110, 110, 110, 0) };
            mtf.Bars["1d"] = Daily(new double[] { 100, 100 }, T0.AddDays(-9));
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var bars = Daily(1, 1, 1);

            var result = eval.Evaluate(Cross("CANDLES.body", "CANDLES.upper_wick", tf: "1w", secondTf: "1d"), bars, State(bars));

            Assert.False(result.OverallTrue);
            Assert.NotNull(eval.LastDegradation);
        }

        // ── the alert path loads what HTF leaves read ─────────────────────────

        [Fact]
        public void PrepareTimeframes_loads_the_bound_instance_and_holds_back_until_it_lands()
        {
            var mtf = new Mtf { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var eval = new ConditionEvaluator(new Catalog(), mtf);
            var tree = Cross("CANDLES.body", "Sma.Sma", p2: P50, secondTf: "1w");
            var identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "1d");

            Assert.False(eval.PrepareTimeframes(tree, identity, 300), "a load in flight is not ready");
            Assert.Contains("indicator 1w Sma lookbackPeriods=50", mtf.Requests);

            mtf.Gate.SetResult();
            Assert.True(SpinWait.SpinUntil(() => eval.PrepareTimeframes(tree, identity, 300), TimeSpan.FromSeconds(5)),
                "once the load has landed the tree may be evaluated");
            Assert.Single(mtf.Requests, r => r.StartsWith("indicator"));
        }

        private static AlertDefinition WeeklySmaAlert() => new()
        {
            Id = "weekly-sma",
            Name = "Close crosses the weekly SMA",
            Target = AlertTarget.Indicator,
            Condition = AlertCondition.CrossesAbove, // placeholder — the tree replaces it
            Delivery = AlertDelivery.Speech,
            Symbol = "BTC/USDT",
            ConditionTree = Cross("CANDLES.body", "Sma.Sma", p2: P50, secondTf: "1w"),
        };

        [Fact]
        public void A_weekly_sma_tree_alert_fires_through_the_in_session_alert_path()
        {
            var mtf = new Mtf { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
            var conditions = new ConditionEvaluator(new Catalog(), mtf);
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(), Substitute.For<IIndicatorContextAnalyzer>(),
                levels: null, conditionEvaluator: conditions);
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition> { WeeklySmaAlert() });
            var orch = new AlertOrchestrator(store, evaluator, bus, library,
                NullLogger<AlertOrchestrator>.Instance, conditions);
            orch.Start();

            store.EmitState(State(Daily(new double[] { 90, 95 }, T0.AddDays(1))));        // warm-up tick
            store.EmitState(State(Daily(new double[] { 90, 95, 96 }, T0.AddDays(1))));    // load in flight: held back

            Assert.Empty(bus.Log.OfType<AlertFiredEvent>());
            Assert.DoesNotContain(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Type == FeedbackType.Error);

            mtf.Bars["1w"] = new List<Ohlcv> { new(T0.AddDays(-7), 1, 1, 1, 1, 0), new(T0, 1, 1, 1, 1, 0) };
            mtf.Indicators[Mtf.Key("1w", "Sma", P50)] = new() { ["Sma"] = new[] { 100.0, 100.0 } };
            mtf.Gate.SetResult();
            SpinWait.SpinUntil(() => conditions.PrepareTimeframes(WeeklySmaAlert().ConditionTree!,
                new ChartIdentity("Spot", "binance", "BTC/USDT", "1d"), 3), TimeSpan.FromSeconds(5));

            store.EmitState(State(Daily(new double[] { 90, 95, 96, 105 }, T0.AddDays(1)))); // 96 -> 105 over 100

            var fired = Assert.Single(bus.Log.OfType<AlertFiredEvent>());
            Assert.Equal("weekly-sma", fired.Alert.Definition.Id);
            Assert.DoesNotContain(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Type == FeedbackType.Error);
        }

        [Fact]
        public void The_real_catalog_offers_price_and_says_which_bar_column_each_line_is()
        {
            // The price lines a cross can use come from CoreIndicatorProvider — they were always
            // in the catalog, as "Candles — Body", which does not say "close".
            var catalog = new SignalCatalog(new AccessibleTrader.Sdk.Interfaces.IIndicatorProvider[]
                { new AccessibleTrader.Core.Services.Indicators.CoreIndicatorProvider() });

            Assert.Contains("(close)", catalog.GetById("CANDLES.body")?.DisplayLabel ?? "");
            Assert.Contains("(high)", catalog.GetById("CANDLES.upper_wick")?.DisplayLabel ?? "");
            Assert.Contains("(low)", catalog.GetById("CANDLES.lower_wick")?.DisplayLabel ?? "");
            Assert.Contains(catalog.All, d => d.Id == "CANDLES.body");
        }

        [Fact]
        public void A_higher_timeframe_load_failure_is_not_blamed_on_the_chart()
        {
            // The weekly SMA never arrives. The spoken reason must say so — not "check the
            // indicator it references is on this chart", which no chart change fixes.
            var conditions = new ConditionEvaluator(new Catalog(), new Mtf());
            var evaluator = new AlertEvaluator(
                Substitute.For<ISdkCandlePatternAnalyzer>(), Substitute.For<IIndicatorContextAnalyzer>(),
                levels: null, conditionEvaluator: conditions);
            var bus = new SpyEventBus();
            var store = new MockWorkspaceStore();
            var library = Substitute.For<IWorkspaceLibraryService>();
            library.LoadAlerts().Returns(new List<AlertDefinition> { WeeklySmaAlert() });
            var orch = new AlertOrchestrator(store, evaluator, bus, library,
                NullLogger<AlertOrchestrator>.Instance, conditions);
            orch.Start();

            store.EmitState(State(Daily(new double[] { 90, 95 }, T0.AddDays(1))));
            store.EmitState(State(Daily(new double[] { 90, 95, 96 }, T0.AddDays(1))));

            var error = Assert.Single(bus.Log.OfType<FeedbackRequestEvent>(), e => e.Type == FeedbackType.Error);
            Assert.Contains("1w data", error.Message);
            Assert.Contains("could not be loaded", error.Message);
            Assert.DoesNotContain("on this chart", error.Message);
        }

        // ── refused at creation: leaves that can never be true ───────────────

        [Fact]
        public void Trees_that_can_never_fire_are_refused_at_creation_with_a_reason()
        {
            static string? Why(ConditionNode tree, string? tf = "1d") =>
                BackgroundWatchability.WhyUnfireable(new AlertDefinition
                {
                    Id = "a", Name = "a", Target = AlertTarget.Indicator, Condition = AlertCondition.CrossesAbove, Delivery = AlertDelivery.Speech,
                    Timeframe = tf, ConditionTree = tree,
                });

            Assert.Null(Why(Cross("CANDLES.body", "Sma.Sma", p2: P50, secondTf: "1w")));
            Assert.Null(Why(Cross("Sma.Sma", "Sma.Sma", p1: P20, p2: P50)));

            Assert.Contains("no line to cross", Why(Cross("CANDLES.body", "")) ?? "");
            Assert.Contains("with itself", Why(Cross("Sma.Sma", "Sma.Sma", p1: P50, p2: P50)) ?? "");
            Assert.Contains("lower than", Why(Cross("CANDLES.body", "Sma.Sma", secondTf: "1h")) ?? "");
            Assert.NotNull(Why(Cross("CANDLES.body", "Sma.Sma", tf: "1w", secondTf: "1d")));
            Assert.Contains("no indicator", Why(new ConditionLeaf("e", "", LeafOperator.GreaterThan)) ?? "");
            Assert.Contains("Sequence", Why(new ConditionGroup("s", LogicOperator.Sequence, new ConditionNode[]
                { new ConditionLeaf("w", "Sma.Sma", LeafOperator.GreaterThan, Timeframe: "1w") })) ?? "");
        }
    }
}
