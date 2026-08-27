using AccessibleTrader.Core.Strategies;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.Sdk.Trading;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The three costs <c>BacktestConfig</c> did not model until 2026-08-27: the bid-ask spread,
    /// perpetual funding, and the borrow on a short.
    ///
    /// <para>Their absence had the same shape as every other cost defect in this engine — it made
    /// the numbers FLATTERING — but with one difference that made it worse: spread and commission
    /// are paid once per round trip, while funding and borrow grow with HOLD TIME. So the error
    /// was largest exactly on the swing strategies this repo's catalogue favours, and on a held
    /// short it compounded with the borrow the same position was also not paying.</para>
    ///
    /// <para>Every fixture below is a FLAT market at price 100 with commission and slippage set to
    /// zero, so a position's entire P&amp;L is the cost being tested. A run that charges nothing
    /// reports exactly 0.00, which is what every one of these tests would have seen before the
    /// fix. The rates are deliberately absurd for the same reason the commission tests use 1% —
    /// a realistic 0.01%-per-8h would land inside the rounding tolerance of the zero-cost case,
    /// which is the same as not testing it.</para>
    /// </summary>
    public class BacktestCarryCostTests
    {
        private static readonly DateTime Midnight =
            new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A perfectly flat market: open = high = low = close, so nothing ever hits a stop or a
        /// target by accident and price contributes exactly zero to P&amp;L.
        /// </summary>
        private static List<Ohlcv> FlatBars(int count, TimeSpan interval, double price = 100.0)
        {
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Ohlcv(Midnight + i * interval, price, price, price, price, 1000));
            return list;
        }

        private sealed class EmitsOnce : ITradingStrategy
        {
            private readonly StrategySignal _signal;
            private int _bars;
            public EmitsOnce(StrategySignal signal) { _signal = signal; }

            public string Id => "CARRY";
            public string Name => "Carry";
            public string Description => "emits one signal on the first bar";
            public StrategyComplexityLevel Complexity => StrategyComplexityLevel.Simple;
            public IReadOnlyList<StrategyParameter> Parameters => Array.Empty<StrategyParameter>();
            public void Initialize(IReadOnlyList<Ohlcv> h, WorkspaceState s, IDictionary<string, object> p) { _bars = 0; }
            public StrategySignal? OnBar(Ohlcv b, IReadOnlyList<Ohlcv> h, WorkspaceState s)
                => _bars++ == 0 ? _signal : null;
            public void OnOrderFilled(OrderUpdate fill) { }
            public void OnStop() { }
            public StrategyMetrics GetMetrics() => new(0, 0, 0, 0, 0, 0);
        }

        private static StrategySignal Entry(OrderSide side, double qty = 1.0) =>
            new(Side: side, OrderType: OrderType.Market, Quantity: qty, LimitPrice: null,
                StopLoss: side == OrderSide.Buy ? 1.0 : 10_000.0,
                TakeProfit: null, Rationale: "carry", Confidence: 1);

        /// <summary>
        /// Zero commission and zero slippage, so the only thing that can move P&amp;L is the cost
        /// under test. Callers layer the one cost they are measuring on top.
        /// </summary>
        private static BacktestConfig FreeConfig(
            double spread = 0.0, double funding = 0.0, double borrow = 0.0,
            double fundingIntervalHours = 8.0) =>
            new(StartingCapital: 10_000, CommissionRate: 0.0, SlippagePercent: 0.0,
                WarmupBars: 0, ReplayProfiles: false,
                SpreadPercent: spread,
                FundingRatePerInterval: funding,
                FundingIntervalHours: fundingIntervalHours,
                BorrowRateAnnual: borrow);

        // ── Funding boundaries ───────────────────────────────────────────────
        //
        // The count is the whole cost model for funding, so it is worth pinning on its own:
        // everything else is a multiplication.

        [Fact]
        public void One_eight_hour_settlement_falls_between_midnight_and_eight()
        {
            Assert.Equal(1, StrategyBacktester.FundingEventsBetween(
                Midnight, Midnight.AddHours(8), 8.0));
        }

        [Fact]
        public void A_calendar_day_crosses_three_eight_hour_settlements()
        {
            // 08:00, 16:00 and the next midnight — the schedule the major perp venues run.
            Assert.Equal(3, StrategyBacktester.FundingEventsBetween(
                Midnight, Midnight.AddHours(24), 8.0));
        }

        [Fact]
        public void An_interval_that_touches_no_boundary_costs_nothing()
        {
            Assert.Equal(0, StrategyBacktester.FundingEventsBetween(
                Midnight.AddHours(3), Midnight.AddHours(5), 8.0));
        }

        [Fact]
        public void A_settlement_on_a_bar_boundary_is_charged_once_not_twice()
        {
            // The half-open convention is what makes this true: the bar ENDING at 08:00 pays for
            // that settlement and the bar STARTING there does not. Charging both would put a
            // timeframe-dependent multiplier on every funding bill.
            int endingAtEight   = StrategyBacktester.FundingEventsBetween(
                Midnight.AddHours(7), Midnight.AddHours(8), 8.0);
            int startingAtEight = StrategyBacktester.FundingEventsBetween(
                Midnight.AddHours(8), Midnight.AddHours(9), 8.0);

            Assert.Equal(1, endingAtEight);
            Assert.Equal(0, startingAtEight);
        }

        [Fact]
        public void A_nonpositive_funding_interval_settles_never()
        {
            // Guard against a divide-by-zero turning into an infinite bill on a misconfigured run.
            Assert.Equal(0, StrategyBacktester.FundingEventsBetween(Midnight, Midnight.AddDays(30), 0.0));
            Assert.Equal(0, StrategyBacktester.FundingEventsBetween(Midnight, Midnight.AddDays(30), -8.0));
        }

        // ── Spread ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Half_the_quoted_spread_is_paid_on_each_side_of_the_round_trip()
        {
            // 2% quoted → 1% per side. A long fills at 101 and closes at 99 on a market that
            // never moved: the full quoted spread, once, across the completed round trip.
            //
            // The number that matters here is the HALF. Charging the full 2% per side would give
            // 102 / 98 and a P&L of −4, which is the same direction of error and twice the size —
            // and OHLCV bars are one price series, so the trader crossing to the ask is already
            // half a spread away from it.
            var bt = new StrategyBacktester();
            var data = FlatBars(10, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Buy)), data, FreeConfig(spread: 0.02));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(101.0, trade.EntryPrice, 6);
            Assert.Equal(99.0, trade.ExitPrice!.Value, 6);
            Assert.Equal(-2.0, trade.PnL!.Value, 6);
        }

        [Fact]
        public async Task A_short_pays_the_spread_in_the_other_direction()
        {
            // Sells at the bid and buys back at the ask. A model that ADDED the spread instead of
            // charging it would make this short's entry 101 and its P&L positive.
            var bt = new StrategyBacktester();
            var data = FlatBars(10, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)), data, FreeConfig(spread: 0.02));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(99.0, trade.EntryPrice, 6);
            Assert.Equal(101.0, trade.ExitPrice!.Value, 6);
            Assert.Equal(-2.0, trade.PnL!.Value, 6);
        }

        [Fact]
        public async Task Spread_and_slippage_compose_rather_than_replacing_each_other()
        {
            // They model different things — the market moving while the order is in flight, and
            // the cost of crossing at all — so a run that sets both must pay both. 2% slippage
            // plus half of a 2% spread is 3% adverse per side.
            var bt = new StrategyBacktester();
            var data = FlatBars(10, TimeSpan.FromHours(1));
            var cfg = FreeConfig(spread: 0.02) with { SlippagePercent = 0.02 };

            var result = await bt.RunAsync(new EmitsOnce(Entry(OrderSide.Buy)), data, cfg);

            var trade = Assert.Single(result.Trades);
            Assert.Equal(103.0, trade.EntryPrice, 6);
            Assert.Equal(97.0, trade.ExitPrice!.Value, 6);
        }

        // ── Funding ──────────────────────────────────────────────────────────
        //
        // Fixture: 26 hourly bars from midnight. The signal is emitted on bar 0 and fills at
        // bar 1's open (01:00); the position closes on "End of data" at bar 25 (01:00 the next
        // day). That is a 24-hour hold spanning exactly three 8-hour settlements — 08:00, 16:00
        // and the following midnight.

        private const int HourlyBarsForOneDayHold = 26;

        [Fact]
        public async Task A_long_perp_pays_funding_for_every_settlement_it_is_open_across()
        {
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Buy)), data, FreeConfig(funding: 0.01));

            // Three settlements × 1% × 100 notional. The market did not move and there is no
            // commission, so every cent of this is funding.
            var trade = Assert.Single(result.Trades);
            Assert.Equal(-3.0, trade.PnL!.Value, 6);
            Assert.Equal(-3.0, result.Metrics.TotalPnL, 6);
        }

        [Fact]
        public async Task A_short_perp_is_CREDITED_when_the_funding_rate_is_positive()
        {
            // The exchange convention, and the reason funding is not simply "a cost": with a
            // positive rate the long pays the short. Getting this backwards would penalise the
            // side that is being paid, and this repo's corpus is full of held shorts.
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)), data, FreeConfig(funding: 0.01));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(3.0, trade.PnL!.Value, 6);
        }

        [Fact]
        public async Task Funding_scales_with_the_number_of_settlements_not_the_number_of_bars()
        {
            // The same 24-hour hold sampled at 15 minutes instead of an hour. 96 accrual steps
            // rather than 24, and the same three settlements — a funding model that charged per
            // BAR would bill this run four times over, which would make every cost in the
            // catalogue a function of the timeframe it happened to be backtested on.
            var bt = new StrategyBacktester();
            var data = FlatBars(98, TimeSpan.FromMinutes(15));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Buy)), data, FreeConfig(funding: 0.01));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(-3.0, trade.PnL!.Value, 6);
        }

        // ── Borrow ───────────────────────────────────────────────────────────

        [Fact]
        public async Task A_short_pays_borrow_for_the_calendar_time_it_is_held()
        {
            // 36.525%/yr is 10%/day, and the hold is exactly one day: 10.00 on a notional of 100.
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)), data, FreeConfig(borrow: 36.525));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(-10.0, trade.PnL!.Value, 6);
        }

        [Fact]
        public async Task A_long_borrows_nothing()
        {
            // The asymmetry is deliberate: a long is assumed cash-funded. Charging both sides
            // would be a symmetric haircut, and a symmetric haircut cannot change which of two
            // strategies looks better — which is precisely the mistake the StrategyLab
            // survivorship stress made.
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Buy)), data, FreeConfig(borrow: 36.525));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(0.0, trade.PnL!.Value, 6);
        }

        [Fact]
        public async Task Borrow_is_calendar_time_not_bar_count()
        {
            // Same day held, sampled four times as often. Borrow accrues on the clock, so a
            // position held over a weekend costs the same whether the data is hourly or daily.
            var bt = new StrategyBacktester();
            var hourly = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)),
                FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1)),
                FreeConfig(borrow: 36.525));
            var quarterly = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)),
                FlatBars(98, TimeSpan.FromMinutes(15)),
                FreeConfig(borrow: 36.525));

            Assert.Equal(
                Assert.Single(hourly.Trades).PnL!.Value,
                Assert.Single(quarterly.Trades).PnL!.Value, 6);
            Assert.Equal(-10.0, Assert.Single(hourly.Trades).PnL!.Value, 6);
        }

        [Fact]
        public async Task Funding_and_borrow_both_land_on_the_same_held_short()
        {
            // The compounding case the finding named. −10 borrow and +3 funding credit on a
            // short: they are independent terms and both have to be there, in their own
            // directions. A model that netted them into one "carry rate" would be indistinguishable
            // from one that had the funding sign wrong.
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));

            var result = await bt.RunAsync(
                new EmitsOnce(Entry(OrderSide.Sell)),
                data, FreeConfig(funding: 0.01, borrow: 36.525));

            var trade = Assert.Single(result.Trades);
            Assert.Equal(-7.0, trade.PnL!.Value, 6);
        }

        // ── Carry across a take-profit ladder ────────────────────────────────

        [Fact]
        public async Task A_ladder_rung_pays_its_share_of_the_carry_and_the_runner_pays_the_rest()
        {
            // 13 hourly bars. Entry fills at 01:00 with 2 units; the only 8-hour settlement in
            // the run lands at 08:00 and is charged on the FULL 2-unit notional (2.00). Bar 9
            // spikes to 110 and fires a rung that closes half the position, which takes half the
            // accrued carry with it; the runner closes at end of data owing the other half.
            //
            // If carry were charged in full at every exit row the position would pay 4.00 for one
            // settlement; if it were only ever charged on the final row the rung would look
            // costless. The split is what makes both wrong.
            var bt = new StrategyBacktester();
            var data = FlatBars(13, TimeSpan.FromHours(1));
            data[9] = data[9] with { High = 110.0 };

            var signal = new StrategySignal(
                Side: OrderSide.Buy, OrderType: OrderType.Market, Quantity: 2, LimitPrice: null,
                StopLoss: 1.0, TakeProfit: 105, Rationale: "ladder", Confidence: 1,
                TpLadder: new[] { 105.0 }, TpClosePortions: new[] { 0.5 });

            var result = await bt.RunAsync(
                new EmitsOnce(signal), data, FreeConfig(funding: 0.01));

            Assert.Equal(2, result.Trades.Count);

            // Rung: +5 a unit on 1 unit, less half of the 2.00 settlement.
            Assert.Equal(4.0, result.Trades[0].PnL!.Value, 6);
            // Runner: closes flat at 100, owing the other half.
            Assert.Equal(-1.0, result.Trades[1].PnL!.Value, 6);

            // One position, one settlement: gross 5.00 less 2.00 of funding.
            var positions = StrategyBacktester.PositionPnLs(result.Trades);
            Assert.Equal(3.0, Assert.Single(positions), 6);
        }

        // ── The defaults ─────────────────────────────────────────────────────

        [Fact]
        public async Task The_default_config_charges_no_carry_and_no_spread()
        {
            // Every historical number in this repo's findings docs was computed before these
            // fields existed. Defaulting them to anything but zero would silently re-score the
            // whole corpus, so turning them on has to be an explicit act — and this is the test
            // that says so. It is also the vacuity check for everything above: on this fixture a
            // run with no costs configured reports exactly 0.00, which is what a broken cost
            // model would report for all of them.
            var bt = new StrategyBacktester();
            var data = FlatBars(HourlyBarsForOneDayHold, TimeSpan.FromHours(1));
            var cfg = new BacktestConfig(
                StartingCapital: 10_000, CommissionRate: 0.0, SlippagePercent: 0.0,
                WarmupBars: 0, ReplayProfiles: false);

            var result = await bt.RunAsync(new EmitsOnce(Entry(OrderSide.Sell)), data, cfg);

            var trade = Assert.Single(result.Trades);
            Assert.Equal(100.0, trade.EntryPrice, 6);
            Assert.Equal(0.0, trade.PnL!.Value, 6);
        }
    }
}
