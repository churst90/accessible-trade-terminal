using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Causality guards for the strategy CONSUMER layer.
    ///
    /// <para>
    /// <c>IndicatorCausalityTests</c> proves that each indicator's own output is causal — that
    /// bar <c>i</c>'s value was computable from bars <c>0..i</c>. That contract says nothing
    /// about which bar a consumer then reads. Everything in this file is the second half: given
    /// a perfectly causal array, the strategy stack must read the bar it is standing on.
    /// </para>
    ///
    /// <para>
    /// Four paths violated it, all invisible in live evaluation (where "the last bar of the
    /// array" and "the current bar" are the same bar) and all wrong in every backtest:
    /// </para>
    /// <list type="bullet">
    ///   <item>a date-filtered window left the workspace's arrays indexed off the FULL chart,
    ///         so bar 0 of a walk-forward's second half read bar 0 of the first half;</item>
    ///   <item><c>CrossesLine</c> read the second descriptor at the chart's last bar;</item>
    ///   <item><c>PriceVsCloud</c> read the chart's final cloud at every historical bar;</item>
    ///   <item><c>RiskPlanResolver</c> placed stops and targets from the chart's last bar,
    ///         which also sized the position and gated the reward:risk check.</item>
    /// </list>
    ///
    /// <para>
    /// Each guard is written as a PAIR wherever the direction of the error can flip a leaf both
    /// ways: one case that the future-read makes false and the causal read makes true, and one
    /// the other way round. A single-direction test here would pass on a "return false always"
    /// regression.
    /// </para>
    /// </summary>
    public class StrategyConsumerCausalityTests
    {
        // ── Shared builders ──────────────────────────────────────────────────

        private static IReadOnlyList<Ohlcv> Bars(int count, double close = 100)
        {
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Ohlcv(t0.AddMinutes(i), close, close, close, close, 1000));
            return list;
        }

        /// <summary>A series carrying one or more named component arrays under an indicator code.</summary>
        private static ChartSeries Series(string indicatorCode, params (string Name, double[] Data)[] components)
        {
            var config = new SeriesConfig { IndicatorCode = indicatorCode, Name = indicatorCode };
            var buffer = new SeriesDataBuffer { SeriesId = config.Id };
            foreach (var (name, data) in components)
            {
                config.Components.Add(new ComponentConfig { Name = name });
                buffer.ComponentData[name] = data;
            }
            return new ChartSeries(config, buffer);
        }

        private static WorkspaceState StateWith(params ChartSeries[] series) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "binance", "BTC/USDT", "5m"),
                ActiveSeries = ImmutableList.CreateRange(series),
            };

        /// <summary>An array of <paramref name="length"/> filled with <paramref name="fill"/>, then overridden at named indices.</summary>
        private static double[] Arr(int length, double fill, params (int Index, double Value)[] overrides)
        {
            var a = new double[length];
            for (int i = 0; i < length; i++) a[i] = fill;
            foreach (var (idx, v) in overrides) a[idx] = v;
            return a;
        }

        private sealed class StubCatalog : ISignalCatalog
        {
            private readonly List<SignalDescriptor> _all = new();
            public StubCatalog Add(string id, string indicatorCode, string componentName)
            {
                _all.Add(new SignalDescriptor(id, indicatorCode, componentName, SignalKind.Line, id));
                return this;
            }
            public IReadOnlyList<SignalDescriptor> All => _all;
            public SignalDescriptor? GetById(string id) => _all.FirstOrDefault(d => d.Id == id);
            public IReadOnlyList<SignalDescriptor> GetForIndicator(string code)
                => _all.Where(d => d.IndicatorCode == code).ToList();
            public void Refresh() { }
        }

        // ── 1. CrossesLine reads the second descriptor at the CURRENT bar ────

        // history is 50 bars; the arrays run to 100. The cross the user configured happens at
        // bar 49 (the current bar). Reading the second descriptor at bar 99 instead sees a line
        // 100 points higher and denies the cross.
        private const int Hist = 50;
        private const int Full = 100;

        private static (StubCatalog Catalog, WorkspaceState State) CrossSetup(double slowAtCurrent, double slowAtEnd)
        {
            var fast = Arr(Full, 1.0, (Hist - 2, 5.0), (Hist - 1, 15.0));
            var slow = Arr(Full, slowAtEnd, (Hist - 2, slowAtCurrent), (Hist - 1, slowAtCurrent));
            var catalog = new StubCatalog()
                .Add("FAST.Value", "FAST", "Value")
                .Add("SLOW.Value", "SLOW", "Value");
            var state = StateWith(
                Series("FAST", ("Value", fast)),
                Series("SLOW", ("Value", slow)));
            return (catalog, state);
        }

        private static ConditionLeaf CrossLeaf() => new(
            Id: "cross",
            SignalDescriptorId: "FAST.Value",
            Operator: LeafOperator.CrossesAboveLine,
            SecondSignalDescriptorId: "SLOW.Value");

        [Fact]
        public void CrossesLine_ReadsTheSecondLineAtTheCurrentBar_NotTheEndOfTheChart()
        {
            // Slow line sits at 10 around the current bar (fast crosses 5 → 15 through it)
            // and at 100 by the end of the chart. The cross IS happening now.
            var (catalog, state) = CrossSetup(slowAtCurrent: 10.0, slowAtEnd: 100.0);
            var eval = new ConditionEvaluator(catalog);

            var result = eval.Evaluate(CrossLeaf(), Bars(Hist), state);

            Assert.True(result.OverallTrue);
        }

        [Fact]
        public void CrossesLine_DoesNotInventACross_FromTheEndOfTheChart()
        {
            // The mirror. Slow sits at 20 around the current bar, so fast's 5 → 15 never
            // reaches it and there is NO cross. At the end of the chart slow has fallen to 6,
            // where the same 5 → 15 move would look like a clean cross above. Reading the end
            // reports a cross that did not happen — the failure that flatters a backtest.
            var (catalog, state) = CrossSetup(slowAtCurrent: 20.0, slowAtEnd: 6.0);
            var eval = new ConditionEvaluator(catalog);

            var result = eval.Evaluate(CrossLeaf(), Bars(Hist), state);

            Assert.False(result.OverallTrue);
        }

        // ── 2. PriceVsCloud clips both boundaries to the current bar ─────────

        private static WorkspaceState CloudState(
            double spanAtCurrent, double spanBAtCurrent, double spanAtEnd, double spanBAtEnd)
        {
            var upper = Arr(Full, spanAtEnd,  (Hist - 1, spanAtCurrent));
            var lower = Arr(Full, spanBAtEnd, (Hist - 1, spanBAtCurrent));
            var series = Series("ICHIMOKU", ("Senkou Span A", upper), ("Senkou Span B", lower));
            series.CloudFills.Add(new CloudFillConfig
            {
                UpperComponentName = "Senkou Span A",
                LowerComponentName = "Senkou Span B",
            });
            return StateWith(series);
        }

        private static ConditionLeaf CloudLeaf(LeafOperator op) => new(
            Id: "cloud", SignalDescriptorId: "ICH.SpanA", Operator: op);

        private static StubCatalog CloudCatalog() =>
            new StubCatalog().Add("ICH.SpanA", "ICHIMOKU", "Senkou Span A");

        [Fact]
        public void PriceVsCloud_JudgesPriceAgainstTodaysCloud_NotTheChartsFinalCloud()
        {
            // Close 100 is above the cloud AT THE CURRENT BAR (90 / 80) and far below the
            // cloud at the end of the chart (200 / 190).
            var state = CloudState(spanAtCurrent: 90, spanBAtCurrent: 80, spanAtEnd: 200, spanBAtEnd: 190);
            var eval = new ConditionEvaluator(CloudCatalog());

            var result = eval.Evaluate(CloudLeaf(LeafOperator.AboveCloud), Bars(Hist, close: 100), state);

            Assert.True(result.OverallTrue);
        }

        [Fact]
        public void PriceVsCloud_DoesNotPlacePriceAboveACloud_ItIsActuallyInsideOf()
        {
            // The mirror. At the current bar the cloud is 120 / 80 and close 100 sits INSIDE
            // it — not above. Only the chart's final cloud (10 / 5) would put price above.
            var state = CloudState(spanAtCurrent: 120, spanBAtCurrent: 80, spanAtEnd: 10, spanBAtEnd: 5);
            var eval = new ConditionEvaluator(CloudCatalog());

            var above  = eval.Evaluate(CloudLeaf(LeafOperator.AboveCloud),  Bars(Hist, close: 100), state);
            var inside = eval.Evaluate(CloudLeaf(LeafOperator.InsideCloud), Bars(Hist, close: 100), state);

            Assert.False(above.OverallTrue);
            Assert.True(inside.OverallTrue);
        }

        [Fact]
        public void PriceVsCloud_SingleBoundaryFallback_AlsoReadsTheCurrentBar()
        {
            // No cloud fill declared → the operator falls back to the named component as a
            // single boundary. That branch had the same [^1] read and needs its own guard.
            var comp = Arr(Full, 200.0, (Hist - 1, 90.0));
            var state = StateWith(Series("ICHIMOKU", ("Kijun-sen", comp)));
            var eval = new ConditionEvaluator(new StubCatalog().Add("ICH.Kijun", "ICHIMOKU", "Kijun-sen"));
            var leaf = new ConditionLeaf("k", "ICH.Kijun", LeafOperator.AboveCloud);

            var result = eval.Evaluate(leaf, Bars(Hist, close: 100), state);

            Assert.True(result.OverallTrue);
        }

        // ── 3. RiskPlanResolver places the stop from the current bar ─────────

        private static RiskPlan ComponentStopPlan() => new(
            Stop: new StopSource(StopSourceKind.BelowComponent, IndicatorCode: "EMA", ComponentName: "EMA 50"),
            TpLadder: new[] { new TpLadderRung(TargetSourceKind.RiskRewardMultiple, Multiple: 2.0, ClosePortion: 1.0) },
            Sizing: new PositionSizing(SizingMode.FixedRiskCash, RiskCash: 100),
            Entry: new EntryTrigger(),
            MinRewardRiskRatio: 1.5);

        [Fact]
        public void RiskPlanResolver_PlacesTheStopAtTheCurrentBarsComponentValue()
        {
            // EMA 50 is 95 at the current bar and 50 at the end of the chart. Entry (last
            // close) is 100. The stop belongs at 95 — a 5-point risk. Reading the chart's end
            // puts it at 50, a 50-point risk: the R:R check, the position size and the reported
            // risk cash are all computed from that one number, so all three were wrong together.
            var arr = Arr(Full, 50.0, (Hist - 1, 95.0));
            var state = StateWith(Series("EMA", ("EMA 50", arr)));
            var resolver = new RiskPlanResolver();

            var plan = resolver.Resolve(ComponentStopPlan(), OrderSide.Buy, Bars(Hist, close: 100), state);

            Assert.NotNull(plan);
            Assert.Equal(95.0, plan!.StopPrice, 6);
            // qty = riskCash / riskPerUnit = 100 / 5. The end-of-chart read gives 100 / 50 = 2.
            Assert.Equal(20.0, plan.Quantity, 6);
        }

        [Fact]
        public void RiskPlanResolver_RejectsAPlanWhoseStopIsOnTheWrongSideOfEntryToday()
        {
            // The mirror, and the one that actually costs money: at the current bar EMA 50 is
            // 105 — ABOVE a long's entry of 100, so the plan is invalid and must be refused.
            // At the end of the chart the same EMA is 95, which would make it look fine.
            var arr = Arr(Full, 95.0, (Hist - 1, 105.0));
            var state = StateWith(Series("EMA", ("EMA 50", arr)));
            var resolver = new RiskPlanResolver();

            var plan = resolver.Resolve(ComponentStopPlan(), OrderSide.Buy, Bars(Hist, close: 100), state);

            Assert.Null(plan);
        }

        [Fact]
        public void RiskPlanResolver_NanScanStaysBehindTheCurrentBar()
        {
            // The backward NaN walk exists for a live pre-warm gap. It must scan backward from
            // the CURRENT bar, not from the end of the array — otherwise a NaN at the current
            // bar is "repaired" with a future value instead of failing the plan.
            var arr = Arr(Full, 50.0);
            arr[Hist - 1] = double.NaN;
            arr[Hist - 2] = 95.0;
            var state = StateWith(Series("EMA", ("EMA 50", arr)));
            var resolver = new RiskPlanResolver();

            var plan = resolver.Resolve(ComponentStopPlan(), OrderSide.Buy, Bars(Hist, close: 100), state);

            Assert.NotNull(plan);
            Assert.Equal(95.0, plan!.StopPrice, 6);
        }

        // ── 4. A date-filtered backtest re-bases the workspace's arrays ──────

        /// <summary>
        /// Records, for every bar it is asked about, the component value the strategy stack
        /// would read at that bar — <c>arr[history.Count - 1]</c>, which is the identity every
        /// consumer in the stack relies on. Emits no signals.
        /// </summary>
        private sealed class ComponentReadingStrategy : ITradingStrategy
        {
            public List<double> Seen { get; } = new();
            public string Id => "READER";
            public string Name => "Reader";
            public string Description => "records what it reads";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> history, WorkspaceState state, IDictionary<string, object> parameterValues) { }
            public StrategySignal? OnBar(Ohlcv newBar, IReadOnlyList<Ohlcv> history, WorkspaceState state)
            {
                var arr = state.ActiveSeries[0].GetComponentData("AbsoluteIndex");
                int idx = Math.Min(history.Count, arr.Length) - 1;
                Seen.Add(idx >= 0 ? arr[idx] : double.NaN);
                return null;
            }
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new StrategyMetrics(0, 0, 0, 0, 0, 0);
        }

        [Fact]
        public async Task DateFilteredBacktest_ReadsIndicatorValuesFromTheWindowsOwnBars()
        {
            // The component array literally records its own absolute bar index, so a wrong read
            // names the bar it came from. The window starts at absolute bar 120; the first bar
            // of the run must therefore read 120, not 0.
            const int total = 200;
            const int windowStart = 120;
            var bars = Bars(total);
            var arr = new double[total];
            for (int i = 0; i < total; i++) arr[i] = i;

            var state = StateWith(Series("MARK", ("AbsoluteIndex", arr)));
            var strategy = new ComponentReadingStrategy();
            var config = new BacktestConfig(
                WarmupBars: 0,
                ReplayProfiles: false,
                StartDate: bars[windowStart].Date);

            await new StrategyBacktester().RunAsync(strategy, bars, config, state);

            Assert.NotEmpty(strategy.Seen);
            Assert.Equal(windowStart, strategy.Seen[0]);
            for (int i = 0; i < strategy.Seen.Count; i++)
                Assert.Equal(windowStart + i, strategy.Seen[i]);
        }

        [Fact]
        public async Task UnfilteredBacktest_StillReadsBarZeroFirst()
        {
            // Vacuity check on the guard above: with no date filter there is no offset, and the
            // run must still start at absolute bar 0. A "slice by a constant" regression would
            // pass the filtered test and fail this one.
            const int total = 60;
            var bars = Bars(total);
            var arr = new double[total];
            for (int i = 0; i < total; i++) arr[i] = i;

            var state = StateWith(Series("MARK", ("AbsoluteIndex", arr)));
            var strategy = new ComponentReadingStrategy();

            await new StrategyBacktester().RunAsync(
                strategy, bars, new BacktestConfig(WarmupBars: 0, ReplayProfiles: false), state);

            Assert.Equal(0, strategy.Seen[0]);
            Assert.Equal(1, strategy.Seen[1]);
        }

        [Fact]
        public async Task DateFilteredBacktest_DoesNotMutateTheCallersSeries()
        {
            // The workspace state handed in belongs to the live chart. Re-basing must produce
            // new buffers, not shorten the ones the chart is rendering from.
            const int total = 120;
            var bars = Bars(total);
            var arr = new double[total];
            for (int i = 0; i < total; i++) arr[i] = i;

            var series = Series("MARK", ("AbsoluteIndex", arr));
            var state = StateWith(series);

            await new StrategyBacktester().RunAsync(
                new ComponentReadingStrategy(), bars,
                new BacktestConfig(WarmupBars: 0, ReplayProfiles: false, StartDate: bars[60].Date),
                state);

            Assert.Equal(total, series.GetComponentData("AbsoluteIndex").Length);
            Assert.Equal(0.0, series.GetComponentData("AbsoluteIndex")[0]);
        }
    }
}
