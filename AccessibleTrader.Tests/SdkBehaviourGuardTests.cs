using System.Net;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    // Guards written to close the A2p mutation campaign's survivors in AccessibleTrader.Sdk.
    // Each class states one behaviour a user meets; the expected values are worked out by hand
    // in the comments, never read back from the production constant or formula.

    /// <summary>
    /// <b>A venue refusing a request is told apart from a venue failing one.</b>
    /// A2p PR1: a 403 classified as a plain failure left the suite green. A key that lacks a
    /// scope (withdrawals, futures) answers 403, and the user then hears "failed" — which reads as
    /// "try again later" — instead of "refused — check the API key's permissions on the venue",
    /// the one message that tells them what to fix.
    /// </summary>
    public class ProviderRefusalClassificationTests
    {
        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public void A_typed_401_or_403_is_reported_as_a_permission_problem(HttpStatusCode status)
        {
            // The message HttpClient itself produces for EnsureSuccessStatusCode.
            var ex = new HttpRequestException(
                $"Response status code does not indicate success: {(int)status} ({status}).", null, status);

            var r = ProviderResult.FromException<List<string>>(ex, "Deposit address");

            Assert.Equal(ResultKind.NotPermitted, r.Kind);
            Assert.Contains("permissions", r.Reason);
        }

        [Fact]
        public void A_typed_server_error_is_a_failure_not_a_refusal()
        {
            // The other direction: a 503 must not send the user off to regenerate a key.
            var ex = new HttpRequestException(
                "Response status code does not indicate success: 503 (Service Unavailable).", null,
                HttpStatusCode.ServiceUnavailable);

            Assert.Equal(ResultKind.Failed, ProviderResult.FromException<List<string>>(ex, "Balances").Kind);
        }
    }

    /// <summary>
    /// <b>A component nobody has vetted is never offered as a strategy leaf.</b>
    /// A2p CC1: making <c>CausalityContract.IsPublishable</c> accept Undeclared left the suite green.
    /// In this repo nothing calls it — <c>SignalCatalog</c> asks <c>RefusalReason</c> — but it is
    /// public SDK surface for plugin authors, and the two answering differently is exactly the
    /// "assume causal" fake-edge the contract exists to refuse. So: Undeclared and Lookahead are
    /// refused by BOTH, Causal is accepted by both.
    /// </summary>
    public class CausalityPublicationContractTests
    {
        [Theory]
        [InlineData(ComponentCausality.Undeclared, false)]
        [InlineData(ComponentCausality.Lookahead, false)]
        [InlineData(ComponentCausality.Causal, true)]
        public void Only_a_declared_causal_component_is_publishable_and_both_questions_agree(
            ComponentCausality declared, bool publishable)
        {
            var indicator = new IndicatorMetadata { Causality = declared };
            var component = new IndicatorComponentMetadata();

            Assert.Equal(publishable, CausalityContract.IsPublishable(indicator, component));
            Assert.Equal(publishable, CausalityContract.RefusalReason(indicator, component) == null);
        }
    }

    /// <summary>
    /// <b>Replacing the forming bar never changes a buffer someone else already holds.</b>
    /// A2p TB1: writing the live tick into the SHARED backing array (the defect the
    /// <c>ReplaceLast</c> doc describes) left the suite green. <c>ChartFeed</c>'s readers — render,
    /// sonification, the paper-fill engine — deliberately take no lock on the strength of this
    /// type being immutable; a write into their array under them is how a torn bar (new close,
    /// old high) reached the fill engine.
    /// </summary>
    public class TimeSeriesBufferImmutabilityTests
    {
        private static Ohlcv Bar(int minute, double close) =>
            new(new DateTime(2026, 9, 25, 10, minute, 0, DateTimeKind.Utc), close, close + 1, close - 1, close, 10);

        [Fact]
        public void A_buffer_already_handed_out_keeps_its_last_bar_when_the_live_tick_replaces_it()
        {
            var published = new TimeSeriesBuffer<Ohlcv>(Bar(0, 100), Bar(1, 101), Bar(2, 102));

            var updated = published.ReplaceLast(Bar(2, 150));

            Assert.Equal(102, published[2].Close);   // the reader's snapshot is untouched
            Assert.Equal(150, updated[2].Close);     // the new snapshot has the tick
            Assert.Equal(101, updated[1].Close);     // and everything before it
            Assert.Equal(3, updated.Count);
        }
    }

    /// <summary>
    /// <b>A timeframe the venue does not serve is built from the COARSEST native bars that divide
    /// it.</b> A2p T03: picking the finest divisor left the suite green. The fetch is capped at the
    /// provider's MaxBarsPerRequest, so a 4-hour chart built from 1-minute bars gets 240× fewer
    /// 4-hour bars of history (1000 one-minute bars is barely four of them) — a chart that looks
    /// loaded and has no past.
    /// </summary>
    public class ResampleBaseTimeframeTests
    {
        [Theory]
        // target, venue's native list, the coarsest native interval that divides the target
        [InlineData("4h", "1m,5m,15m,1h,1d", "1h")]   // 1h×4; 15m and 5m and 1m also divide, but are finer
        [InlineData("2h", "1m,30m,1h", "1h")]
        [InlineData("1w", "1m,1h,1d", "1d")]          // 1d×7
        [InlineData("45m", "1m,5m,15m,30m", "15m")]   // 30m does not divide 45m; 15m×3 does
        public void The_coarsest_native_interval_that_divides_the_target_is_chosen(
            string target, string native, string expected)
        {
            var supported = native.Split(',').ToList();

            Assert.Equal(expected, TimeframeUtility.GetBestBaseTimeframe(target, supported));
        }
    }

    /// <summary>
    /// <b>True range and ATR, worked by hand.</b>
    /// A2p IM1 (the ATR seed averaging one bar short) and IM2 (true range ignoring a gap DOWN)
    /// both left the suite green. <c>IndicatorMath.Atr</c> sizes swing-structure and level
    /// tolerances, Cipher B's and Cipher SR's bands, and four StrategyLab commands; a gap-down
    /// true range that stops at the high understates volatility on exactly the bars where stops
    /// get run.
    /// </summary>
    public class AverageTrueRangeTests
    {
        private static Ohlcv B(double high, double low, double close) =>
            new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), close, high, low, close, 1);

        [Fact]
        public void A_gap_down_bar_reaches_back_to_the_previous_close()
        {
            // Previous close 100; the bar trades 95..94. High-low is 1, high to prior close is 5,
            // low to prior close is 6 — the true range is 6.
            var tr = IndicatorMath.TrueRange(new[] { B(101, 99, 100), B(95, 94, 94.5) });

            Assert.True(double.IsNaN(tr[0]));
            Assert.Equal(6.0, tr[1], 9);
        }

        [Fact]
        public void A_gap_up_bar_reaches_back_to_the_previous_close()
        {
            // Mirror image: prior close 100, bar trades 105..106 → 106 − 100 = 6.
            var tr = IndicatorMath.TrueRange(new[] { B(101, 99, 100), B(106, 105, 105.5) });

            Assert.Equal(6.0, tr[1], 9);
        }

        [Fact]
        public void Atr_seeds_on_the_mean_of_the_first_full_period_then_smooths_wilder_style()
        {
            // True ranges, by hand (prior close in brackets):
            //   bar1 11/10 [9.5]  → max(1, 1.5, 0.5) = 1.5
            //   bar2 12/10 [10.5] → max(2, 1.5, 0.5) = 2
            //   bar3 11.5/10.5 [11] → max(1, 0.5, 0.5) = 1
            //   bar4 13/11 [11]   → max(2, 2, 0)     = 2
            // ATR(3) first value at bar3 = (1.5 + 2 + 1) / 3 = 1.5
            // then bar4 = (1.5 × 2 + 2) / 3 = 5/3
            var bars = new[] { B(10, 9, 9.5), B(11, 10, 10.5), B(12, 10, 11), B(11.5, 10.5, 11), B(13, 11, 12) };

            var atr = IndicatorMath.Atr(bars, 3);

            Assert.True(double.IsNaN(atr[2]));
            Assert.Equal(1.5, atr[3], 9);
            Assert.Equal(5.0 / 3.0, atr[4], 9);
        }
    }

    /// <summary>
    /// <b>What a risk plan does when its author leaves the sizing and the reward/risk gate alone.</b>
    ///
    /// <para>
    /// A2p D1/D2: a 0.5 minimum reward/risk default, and a 5% (not 0.5%) risk default, both left the
    /// suite green — every in-repo caller passes both values explicitly. The defaults are still
    /// reached: by a plugin or Roslyn-compiled strategy that builds a <c>RiskPlan</c> the short way
    /// (StrategyModalCoordinator's Roslyn placeholder omits the gate), and by a hand-written
    /// strategies.json entry that omits the field. The documented contract is on the types:
    /// <c>PositionSizing</c> — "risk a fixed percent of equity per trade … e.g. 0.005 = 0.5%", and
    /// <c>RiskPlan</c> — a setup whose first target does not clear the minimum ratio "never rings
    /// the bell". Expected values are worked by hand below.
    /// </para>
    /// </summary>
    public class RiskPlanDefaultsTests
    {
        // A flat history at 100: entry = last close = 100.
        private static IReadOnlyList<Ohlcv> Flat() => Enumerable.Range(0, 5)
            .Select(i => new Ohlcv(new DateTime(2026, 9, 25, i, 0, 0, DateTimeKind.Utc), 100, 100, 100, 100, 1000))
            .ToList();

        // Stop 1% below 100 = 99, so one unit risks exactly 1.00.
        private static readonly StopSource OnePercentStop = new(StopSourceKind.PercentOfPrice, PercentValue: 1.0);

        [Fact]
        public void A_setup_that_targets_no_more_than_it_risks_is_refused_by_default()
        {
            // Target 1R = 101: reward 1.00 for risk 1.00. Nobody should be alerted into a trade that
            // pays at best what it risks unless they lowered the gate themselves.
            var plan = new RiskPlan(
                OnePercentStop,
                new[] { new TpLadderRung(TargetSourceKind.RiskRewardMultiple, Multiple: 1.0, ClosePortion: 1.0) },
                new PositionSizing(),
                new EntryTrigger());

            var resolved = new RiskPlanResolver().Resolve(plan, OrderSide.Buy, Flat(), WorkspaceState.Initial);

            Assert.Null(resolved);
        }

        [Fact]
        public void Default_sizing_risks_half_a_percent_of_equity()
        {
            // Equity 10,000 × 0.5% = 50.00 at risk; 1.00 risked per unit → 50 units. Target 3R so
            // the reward/risk gate is not what this is about.
            var plan = new RiskPlan(
                OnePercentStop,
                new[] { new TpLadderRung(TargetSourceKind.RiskRewardMultiple, Multiple: 3.0, ClosePortion: 1.0) },
                new PositionSizing(),
                new EntryTrigger(),
                NotionalEquity: 10_000);

            var resolved = new RiskPlanResolver().Resolve(plan, OrderSide.Buy, Flat(), WorkspaceState.Initial);

            Assert.NotNull(resolved);
            Assert.Equal(50.0, resolved!.Quantity, 6);
            Assert.Equal(50.0, resolved.RiskCash, 6);
        }
    }

    /// <summary>
    /// <b>A backtest that does not name a commission charges 0.1% per side.</b>
    /// A2p D6: a tenfold-too-low commission default left the suite green, because every backtest
    /// test sets the rate itself. Three StrategyLab commands (combo, combo-sweep, diagnostic) take
    /// the default, and the research corpus was computed at 0.1% per side — a lower default
    /// flatters every strategy those commands score. Flat market at 100, one unit in and out: the
    /// only thing that moves equity is commission, 0.10 on the way in and 0.10 on the way out.
    /// </summary>
    public class BacktestDefaultCommissionTests
    {
        private sealed class BuysOnce : ITradingStrategy
        {
            private int _bars;
            public string Id => "BUY_ONCE";
            public string Name => "Buy once";
            public string Description => "one long on the first bar";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> h, WorkspaceState s, IDictionary<string, object> p) { _bars = 0; }
            public StrategySignal? OnBar(Ohlcv b, IReadOnlyList<Ohlcv> h, WorkspaceState s) =>
                _bars++ == 0
                    ? new StrategySignal(Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 1.0,
                        LimitPrice: null, StopLoss: 1.0, TakeProfit: null, Rationale: "once", Confidence: 1)
                    : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        [Fact]
        public async Task A_round_trip_on_a_flat_market_costs_a_tenth_of_a_percent_each_way()
        {
            var bars = Enumerable.Range(0, 10)
                .Select(i => new Ohlcv(new DateTime(2026, 1, 1, i, 0, 0, DateTimeKind.Utc), 100, 100, 100, 100, 1000))
                .ToList();
            var cfg = new BacktestConfig(StartingCapital: 10_000, SlippagePercent: 0.0, WarmupBars: 0, ReplayProfiles: false);

            var result = await new StrategyBacktester().RunAsync(new BuysOnce(), bars, cfg);

            Assert.Single(result.Trades);
            Assert.Equal(10_000 - 0.10 - 0.10, result.EquityCurve[^1].EquityValue, 6);
        }
    }
}
