using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Cross versus isolated margin in the paper broker.
    ///
    /// <para>
    /// The broker was isolated-only and did not say so, which had two costs. The
    /// dashboard hid the cross/isolated selector behind
    /// <c>ProviderCapabilities.IsolatedMargin</c>, so the one account every hosted user
    /// has could not reach the choice at all; and a position gave no clue which mode it
    /// was held under, which is the fact that decides whether one bad trade can take the
    /// rest of the account with it.
    /// </para>
    ///
    /// <para>
    /// The two modes have to actually differ or the selector is decoration. Isolated is
    /// ring-fenced: the position dies against its own collateral and touches nothing
    /// else. Cross draws on the pooled collateral of every cross short plus the free
    /// cash, so it survives further — and then every cross position goes at once. Both
    /// halves of that are asserted below, and the arithmetic is worked by hand rather
    /// than read back off the implementation.
    /// </para>
    ///
    /// <para>
    /// Starting cash is 100,000; margin is 1× (a short of notional N locks N of proceeds
    /// plus N of margin, and costs N of free cash); the taker fee is 4 basis points.
    /// </para>
    /// </summary>
    public sealed class PaperCrossMarginTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private const string Eth = "ETH/USDT";
        private readonly string _tempDir;

        public PaperCrossMarginTests()
        {
            _tempDir = TestTemp.NewPath("atc-cross-");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose() { try { Directory.Delete(_tempDir, recursive: true); } catch { } }

        private PaperTradingProvider Make(out MockWorkspaceStore store, string? dir = null)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(dir ?? _tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        private static WorkspaceState At(string symbol, double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", symbol, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        private static TradeSignal Short(string symbol, double qty, string mode) =>
            new(symbol, OrderSide.Sell, qty, MarginType: mode);

        // ── The mode is recorded and reported ────────────────────────────────

        [Theory]
        [InlineData("Isolated", MarginMode.Isolated)]
        [InlineData("Cross",    MarginMode.Cross)]
        [InlineData("cross",    MarginMode.Cross)]      // exchanges spell it however they like
        public async Task A_short_reports_the_mode_it_was_opened_under(string sent, MarginMode expected)
        {
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(Short(Btc, 1.0, sent));

            Assert.Equal(expected, (await paper.GetPositionsAsync()).Single().MarginMode);
        }

        [Fact]
        public async Task An_order_that_asks_for_nothing_stays_isolated()
        {
            // Isolated is what this broker always did. A signal with no MarginType —
            // every strategy, every quick trade, every caller written before the field
            // was honoured — must land exactly where it always landed, not on cross.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            Assert.Equal(MarginMode.Isolated, (await paper.GetPositionsAsync()).Single().MarginMode);
        }

        [Fact]
        public async Task A_long_reports_no_margin_mode_whatever_the_ticket_asked_for()
        {
            // A long here is bought outright: no borrow, no collateral, nothing held
            // either way. Printing "cross 1x" over it would describe a liquidation the
            // position cannot have.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));

            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, MarginType: "Cross"));

            var pos = (await paper.GetPositionsAsync()).Single();
            Assert.Equal(MarginMode.None, pos.MarginMode);
            Assert.Equal(0.0, pos.LiquidationPrice);
        }

        [Fact]
        public async Task Adding_to_a_position_does_not_re_margin_it()
        {
            // A venue does not move an open position's liquidation price because the
            // second lot asked for something else. Whichever way this went silently, it
            // would be the wrong one for somebody.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Isolated"));

            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));

            Assert.Equal(MarginMode.Isolated, (await paper.GetPositionsAsync()).Single().MarginMode);
        }

        // ── The modes liquidate by different maths ───────────────────────────

        [Fact]
        public async Task An_isolated_short_dies_at_twice_its_entry_and_a_cross_short_does_not()
        {
            // The whole reason the selector is a real choice. Same entry, same size,
            // same bar: isolated is bought in at 200 because that is where ITS OWN
            // collateral runs out; cross is still open, because the account behind it
            // has 99,899 of cash it has not touched.
            var isolatedDir = Path.Combine(_tempDir, "iso");
            var crossDir    = Path.Combine(_tempDir, "cross");
            Directory.CreateDirectory(isolatedDir);
            Directory.CreateDirectory(crossDir);

            var isolated = Make(out var isoStore, isolatedDir);
            var cross    = Make(out var crossStore, crossDir);
            isoStore.EmitState(At(Btc, 99, 101, 98, 100));
            crossStore.EmitState(At(Btc, 99, 101, 98, 100));
            await isolated.PlaceOrderAsync(Short(Btc, 1.0, "Isolated"));
            await cross.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));

            isoStore.EmitState(At(Btc, 150, 205, 150, 200));
            crossStore.EmitState(At(Btc, 150, 205, 150, 200));

            Assert.Empty(await isolated.GetPositionsAsync());
            Assert.Single(await cross.GetPositionsAsync());
        }

        [Fact]
        public async Task A_cross_short_is_liquidated_once_the_pooled_cash_is_gone()
        {
            // And it IS liquidated — cross is later, not never. One short of 1 at 100
            // leaves 200 of collateral and 100,000 - 100 - 0.04 = 99,899.96 of cash, so
            // the pool runs out at 100,099.96. Below it the position lives; above it,
            // it does not.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));

            Assert.Equal(100_099.96, (await paper.GetPositionsAsync()).Single().LiquidationPrice, 2);

            store.EmitState(At(Btc, 90_000, 100_000, 90_000, 99_000));
            Assert.Single(await paper.GetPositionsAsync());

            store.EmitState(At(Btc, 90_000, 100_200, 90_000, 99_000));
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task Cross_liquidation_takes_every_cross_position_with_it()
        {
            // The cost of the extra room. Two cross shorts share one pool, so when it
            // is exhausted by one of them the other is closed too — including the one
            // whose own price never moved.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));
            store.EmitState(At(Eth, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Eth, 1.0, "Cross"));

            Assert.Equal(2, (await paper.GetPositionsAsync()).Count);

            // Two shorts of 1 at 100: cash 100,000 - 200 - 0.08 = 99,799.92, collateral
            // 400, so the pool is 100,199.92 and ETH owes 100 of it. BTC touching
            // 100,200 puts the pair over. ETH has not moved at all.
            store.EmitState(At(Btc, 90_000, 100_200, 90_000, 99_000));

            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task An_isolated_position_survives_a_cross_liquidation()
        {
            // The property a trader picks isolated FOR. If this ever went red, cross
            // and isolated would be the same mode with two names.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));
            store.EmitState(At(Eth, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Eth, 1.0, "Isolated"));

            store.EmitState(At(Btc, 90_000, 200_000, 90_000, 199_000));

            var left = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(Eth, left.Symbol);
            Assert.Equal(MarginMode.Isolated, left.MarginMode);
        }

        [Fact]
        public async Task A_losing_cross_short_moves_the_other_ones_liquidation_price()
        {
            // Pooled collateral means somebody else's bad trade is your problem — the
            // fact that makes cross worth announcing on the row rather than burying in
            // a setting. ETH's reported liquidation price must fall once BTC is
            // underwater, because that is when the pool would actually run out.
            var paper = Make(out var store);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));
            store.EmitState(At(Eth, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(Short(Eth, 1.0, "Cross"));

            double before = (await paper.GetPositionsAsync()).Single(p => p.Symbol == Eth).LiquidationPrice;

            // BTC to 50,000: still inside the pool, but it has eaten most of it.
            store.EmitState(At(Btc, 49_000, 50_000, 49_000, 50_000));

            var after = (await paper.GetPositionsAsync()).Single(p => p.Symbol == Eth).LiquidationPrice;
            Assert.True(after < before,
                $"ETH's liquidation price did not move when the other cross short lost {50_000 - 100:N0}. "
                + $"Before {before:N2}, after {after:N2} — the collateral is not actually pooled.");
        }

        // ── Persistence ──────────────────────────────────────────────────────

        [Fact]
        public async Task The_mode_survives_a_restart()
        {
            var dir = Path.Combine(_tempDir, "restart");
            Directory.CreateDirectory(dir);
            var first = Make(out var store, dir);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await first.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));

            var reloaded = Make(out _, dir);

            Assert.Equal(MarginMode.Cross, (await reloaded.GetPositionsAsync()).Single().MarginMode);
        }

        [Fact]
        public async Task An_account_saved_before_cross_existed_reloads_as_isolated()
        {
            // The migration that decides money. Every short in a file written before
            // this feature was collateralised per symbol, so reading those as anything
            // but isolated would move a real position's liquidation price on restart —
            // silently, and in the direction that keeps a losing trade open longer.
            var dir = Path.Combine(_tempDir, "legacy");
            Directory.CreateDirectory(dir);
            var first = Make(out var store, dir);
            store.EmitState(At(Btc, 99, 101, 98, 100));
            await first.PlaceOrderAsync(Short(Btc, 1.0, "Cross"));

            string path = Path.Combine(dir, "paper_account.json");
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.NotNull(json["Margins"]);              // vacuity check: there IS something to remove
            json.Remove("Margins");
            File.WriteAllText(path, json.ToString());

            var reloaded = Make(out _, dir);

            var pos = (await reloaded.GetPositionsAsync()).Single();
            Assert.Equal(MarginMode.Isolated, pos.MarginMode);
            Assert.Equal(200.0, pos.LiquidationPrice, 6);
        }
    }
}
