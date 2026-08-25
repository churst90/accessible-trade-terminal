using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Collateralised shorting in the paper broker, at 1× — fully collateralised.
    ///
    /// <para>
    /// There is no shorting without a borrow or a contract, and both need
    /// collateral: selling an asset you do not hold means somebody lent it to you,
    /// and you owe it back whatever it comes to cost. The model here is that the
    /// sale proceeds are locked (you owe the asset) and an equal margin is locked on
    /// top, so shorting N costs exactly the same free cash as buying N and the
    /// position is liquidated at twice its entry.
    /// </para>
    ///
    /// <para>
    /// **Liquidation is the point, not a detail.** A long can only go to zero; a
    /// short can go to infinity. A paper account that lets you short and never buys
    /// you in does not teach shorting — it teaches that shorting is free money with
    /// no ruin risk, which is the opposite of the truth and worse than not offering
    /// it. Every number below decides money, so they are worked by hand rather than
    /// asserted against whatever the code happens to produce.
    /// </para>
    /// </summary>
    public sealed class PaperShortingTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private readonly string _tempDir;

        public PaperShortingTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "atc-short-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store, string? dir = null)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir ?? _tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        private static WorkspaceState At(double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", Btc, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        private static Balance Cash(List<Balance> b) => Assert.Single(b, x => x.Asset == "USDT");
        private static List<OrderUpdate> Collect(PaperTradingProvider p)
        {
            var u = new List<OrderUpdate>();
            p.OrderUpdateStream.Subscribe(u.Add);
            return u;
        }

        // ── Opening ──────────────────────────────────────────────────────────

        [Fact]
        public async Task Shorting_costs_the_same_free_cash_as_buying_and_locks_twice_that()
        {
            // Short 1 BTC at 100. Proceeds of 100 are locked — the asset is owed back —
            // and 100 of margin is locked on top. Free cash falls by 100, exactly as
            // buying 1 BTC would cost 100. Locked is 200.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));

            var id = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            Assert.StartsWith("paper-", id);
            var cash = Cash(await paper.GetBalancesAsync());
            Assert.Equal(100_000.0 - 100.0 - 0.04, cash.Free, 6);   // 0.04 = the taker fee
            Assert.Equal(200.0, cash.Locked, 6);

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(-1.0, pos.Quantity, 6);                     // negative = short
            Assert.Equal(100.0, pos.AveragePrice, 6);
        }

        [Fact]
        public async Task A_short_reports_its_liquidation_price_at_twice_the_entry()
        {
            // At 1x the collateral is exhausted when the buy-back costs what is held:
            // 200 held against 1 BTC owed means 200 a coin.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            var pos = Assert.Single(await paper.GetPositionsAsync());

            Assert.Equal(200.0, pos.LiquidationPrice, 6);
        }

        [Fact]
        public async Task A_long_has_no_liquidation_price()
        {
            // A spot long cannot be liquidated — it just goes to zero. Reporting 0
            // here means "not applicable" rather than "liquidated at zero".
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            Assert.Equal(0.0, (await paper.GetPositionsAsync()).Single().LiquidationPrice);
        }

        [Fact]
        public async Task A_short_bigger_than_the_account_can_collateralise_is_refused_with_the_numbers()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));

            // 2,000 BTC at 100 needs 200,000 of collateral against a 100,000 account,
            // plus the 0.04% taker fee on the 200,000 notional — 80 — because the fee
            // is settled in the same number the affordability check tests. The quoted
            // figure is what the fill actually costs, not just its collateral leg.
            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 2000.0));

            Assert.StartsWith("ORDER_FAILED", result);
            Assert.Contains("collateral", result);
            Assert.Contains("200,080.00", result);
            Assert.Empty(await paper.GetPositionsAsync());
        }

        // ── Profit and loss ──────────────────────────────────────────────────

        [Fact]
        public async Task A_short_closed_lower_returns_the_collateral_and_the_profit()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            store.EmitState(At(91, 91, 89, 90));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            // Shorted at 100, bought back at 90 → +10, less two 0.04% fees
            // (0.04 on the way in, 0.036 on the way out).
            var cash = Cash(await paper.GetBalancesAsync());
            Assert.Equal(100_000.0 + 10.0 - 0.04 - 0.036, cash.Free, 6);
            Assert.Equal(0.0, cash.Locked, 6);            // nothing owed, nothing held
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task A_short_closed_higher_takes_the_loss_out_of_the_collateral()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            store.EmitState(At(119, 121, 119, 120));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));

            var cash = Cash(await paper.GetBalancesAsync());
            Assert.Equal(100_000.0 - 20.0 - 0.04 - 0.048, cash.Free, 6);
            Assert.Equal(0.0, cash.Locked, 6);
        }

        [Fact]
        public async Task Unrealized_pnl_on_a_short_is_positive_when_price_falls()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 2.0));

            store.EmitState(At(91, 91, 89, 90));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(20.0, pos.UnrealizedPnL, 6);      // 2 × (100 − 90)
        }

        // ── Liquidation ──────────────────────────────────────────────────────

        [Fact]
        public async Task A_short_is_bought_in_when_its_collateral_is_exhausted()
        {
            // The reason collateral accounting exists. Price doubles; the position is
            // closed whether the trader likes it or not.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));
            updates.Clear();

            store.EmitState(At(150, 205, 150, 200));       // high of 205 crosses 200

            Assert.Empty(await paper.GetPositionsAsync());
            var fill = Assert.Single(updates, u => u.Status == OrderStatus.Filled);
            Assert.Equal(OrderSide.Buy, fill.Side);
            Assert.Equal(200.0, fill.FilledPrice, 6);
            Assert.Contains("LIQUIDATED", fill.Reason);
        }

        [Fact]
        public async Task Liquidation_costs_the_margin_and_no_more()
        {
            // The lesson the model is built to teach: a short loses what you posted,
            // and being bought in is what stops it going further.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            store.EmitState(At(150, 205, 150, 200));

            var cash = Cash(await paper.GetBalancesAsync());
            Assert.Equal(0.0, cash.Locked, 6);
            // 100 of margin gone, plus the two fees. Never worse than that.
            Assert.Equal(100_000.0 - 100.0 - 0.04 - 0.08, cash.Free, 6);
            Assert.True(cash.Free > 0, "an account must never be driven negative by a liquidation");
        }

        [Fact]
        public async Task Liquidation_fires_on_the_bars_high_not_its_close()
        {
            // A spike through the level liquidates you on a real venue even if price
            // recovers by the close. Judging on the close would teach a forgiveness
            // that does not exist.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            store.EmitState(At(150, 210, 150, 160));       // spiked to 210, closed at 160

            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task A_short_just_short_of_the_level_survives()
        {
            // Guards the guard: if liquidation fired unconditionally the test above
            // would pass while the model was nonsense.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            store.EmitState(At(150, 199.9, 150, 199));

            Assert.Single(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task Liquidation_withdraws_the_protective_orders_too()
        {
            // They protect nothing now, and leaving them resting would reopen the
            // position on the next touch — the same defect the bracket fix removed.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0, StopLoss: 130, TakeProfit: 70));
            updates.Clear();

            store.EmitState(At(150, 205, 150, 200));

            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
            Assert.Contains(updates, u => u.Status == OrderStatus.Cancelled);
        }

        // ── Crossing from long to short in one fill ──────────────────────────

        [Fact]
        public async Task Selling_more_than_is_held_closes_the_long_and_opens_a_short()
        {
            // Hold 1, sell 2.5 → the long closes at spot and a 1.5 short opens on
            // collateral. Two settlement rules in one fill, which is exactly where
            // arithmetic like this goes wrong.
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 2.5));

            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(-1.5, pos.Quantity, 6);
            Assert.Equal(100.0, pos.AveragePrice, 6);

            // Collateral covers only the 1.5 short: 1.5 × 100 × 2 = 300.
            Assert.Equal(300.0, Cash(await paper.GetBalancesAsync()).Locked, 6);
        }

        [Fact]
        public async Task Partially_closing_a_short_releases_collateral_in_proportion()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 2.0));   // locked 400

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));    // half back

            Assert.Equal(200.0, Cash(await paper.GetBalancesAsync()).Locked, 6);
            Assert.Equal(-1.0, (await paper.GetPositionsAsync()).Single().Quantity, 6);
        }

        // ── Persistence ──────────────────────────────────────────────────────

        [Fact]
        public async Task Collateral_survives_a_restart()
        {
            // Restoring the position without its collateral would hand back money the
            // account still owes — free money on every restart.
            string dir = Path.Combine(_tempDir, "restart");
            Directory.CreateDirectory(dir);

            var a = Make(out var storeA, dir);
            storeA.EmitState(At(99, 101, 98, 100));
            await a.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));
            double lockedBefore = Cash(await a.GetBalancesAsync()).Locked;
            a.Dispose();

            var b = Make(out _, dir);

            Assert.Equal(lockedBefore, Cash(await b.GetBalancesAsync()).Locked, 6);
            Assert.Equal(-1.0, (await b.GetPositionsAsync()).Single().Quantity, 6);
            Assert.Equal(200.0, (await b.GetPositionsAsync()).Single().LiquidationPrice, 6);
        }

        [Fact]
        public async Task Resetting_the_account_clears_the_collateral()
        {
            var paper = Make(out var store);
            store.EmitState(At(99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            paper.ResetAccount();

            var cash = Cash(await paper.GetBalancesAsync());
            Assert.Equal(100_000.0, cash.Free, 6);
            Assert.Equal(0.0, cash.Locked, 6);
        }

        // ── The capability claim ─────────────────────────────────────────────

        [Fact]
        public void Shorting_is_declared_now_that_it_is_real()
        {
            var paper = Make(out _);

            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.Shorting));
            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.MarginTrading));
            // Leverage above 1x is still NOT implemented and still not claimed.
            Assert.False(paper.Capabilities.HasFlag(ProviderCapabilities.Leverage));
            Assert.Equal(1.0, paper.MaxLeverage);
        }
    }
}
