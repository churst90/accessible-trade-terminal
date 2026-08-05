using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Conformance: what <see cref="PaperTradingProvider"/> DECLARES and what it
    /// DOES have to agree, in both directions.
    ///
    /// <para>
    /// The trading dashboard renders its controls from
    /// <see cref="ProviderCapabilities"/>, so a wrong flag is invisible to every
    /// other test and immediately visible to a user. Both directions had a live
    /// defect: <c>Leverage</c> and <c>Shorting</c> were declared and not
    /// implemented (a leverage selector that changed nothing, a sell side with
    /// no borrow that credited cash for assets never owned), while
    /// <c>TrailingStop</c> was implemented and not declared, which hid working
    /// trailing fields behind a flag that was never set.
    /// </para>
    ///
    /// <para>
    /// This is the paper-broker instance of the per-capability conformance suite
    /// the other providers need. Each test exercises the behaviour rather than
    /// re-asserting the flag, so it can only pass if the capability is real.
    /// </para>
    /// </summary>
    public sealed class PaperCapabilityConformanceTests : IDisposable
    {
        private const string Btc = "BTC/USDT";
        private readonly string _tempDir;

        public PaperCapabilityConformanceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "atc-paper-conf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private PaperTradingProvider Make(out MockWorkspaceStore store)
        {
            store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_tempDir);
            return new PaperTradingProvider(store, paths, NullLogger<PaperTradingProvider>.Instance);
        }

        private static WorkspaceState StateWith(string symbol, double open, double high, double low, double close) =>
            WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Test", symbol, "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, open, high, low, close, 1000)),
            };

        private static List<OrderUpdate> Collect(PaperTradingProvider paper)
        {
            var updates = new List<OrderUpdate>();
            paper.OrderUpdateStream.Subscribe(updates.Add);
            return updates;
        }

        // ── Declared, therefore it must work ─────────────────────────────────

        [Fact]
        public async Task Declares_TrailingStop_and_a_trailing_stop_actually_trails()
        {
            var paper = Make(out var store);
            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.TrailingStop));

            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            // Long with a trailing stop 5 below the high-water mark.
            await paper.PlaceOrderAsync(new TradeSignal(
                Btc, OrderSide.Buy, 1.0, TrailStopMode: TrailMode.Amount, TrailStopValue: 5));
            updates.Clear();

            // Bars advance the anchor before the trigger is tested, so a bar whose
            // low is below the newly-trailed stop fills within that same bar. That
            // is the pessimistic intrabar assumption and it is the right one for a
            // stop — these bars keep the low above the trail so the two effects
            // stay separable.
            store.EmitState(StateWith(Btc, 100, 120, 116, 119)); // anchor moves to 120 → trigger 115
            store.EmitState(StateWith(Btc, 119, 119, 118, 118)); // above 115 — must not fire
            Assert.DoesNotContain(updates, u => u.Status == OrderStatus.Filled);

            store.EmitState(StateWith(Btc, 118, 118, 114, 114)); // through the trailed trigger
            var fill = Assert.Single(updates.FindAll(u => u.Status == OrderStatus.Filled));
            Assert.True(fill.Trailing, "the fill must be marked as a trailing exit so speech says so");
            Assert.Equal(115, fill.FilledPrice); // trailed trigger, not the entry stop
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task Declares_Brackets_and_a_bracket_attaches_both_legs_and_self_cancels()
        {
            var paper = Make(out var store);
            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.Brackets));

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0, StopLoss: 95, TakeProfit: 110));

            Assert.Equal(2, (await paper.GetOpenOrdersAsync(Btc)).Count); // both legs attached

            store.EmitState(StateWith(Btc, 99, 99, 94, 94));             // stop closes the trade
            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));            // target withdrawn with it
            Assert.Empty(await paper.GetPositionsAsync());
        }

        [Fact]
        public async Task Declares_OCO_and_one_leg_filling_cancels_the_other()
        {
            var paper = Make(out var store);
            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.OCO));

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Buy, 1.0));
            string group = "g1";
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0, OrderType.Limit, Price: 110, OcoGroupId: group));
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0, OrderType.StopMarket, TriggerPrice: 95, OcoGroupId: group));

            store.EmitState(StateWith(Btc, 100, 111, 99, 110));

            Assert.Empty(await paper.GetOpenOrdersAsync(Btc));
        }

        // ── Not declared, therefore it must refuse ───────────────────────────

        [Fact]
        public async Task Declares_Shorting_and_a_short_is_collateralised_and_liquidatable()
        {
            // Declared only once it was real. The claim is not merely "a sell with no
            // position is accepted" — that was the money-minting bug — but that it
            // costs collateral and reports the price it will be bought in at.
            var paper = Make(out var store);
            Assert.True(paper.Capabilities.HasFlag(ProviderCapabilities.Shorting));

            store.EmitState(StateWith(Btc, 99, 101, 98, 100));
            var result = await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 1.0));

            Assert.StartsWith("paper-", result);
            var pos = Assert.Single(await paper.GetPositionsAsync());
            Assert.Equal(-1.0, pos.Quantity, 6);
            Assert.Equal(200.0, pos.LiquidationPrice, 6);          // twice the entry, at 1x

            var cash = Assert.Single(await paper.GetBalancesAsync(), b => b.Asset == "USDT");
            Assert.True(cash.Locked > 0, "a short must lock collateral, or it is the old bug again");
            Assert.True(cash.Free < 100_000.0, "a short must COST free cash, never pay it out");
        }

        [Fact]
        public async Task Does_not_declare_Leverage_above_one_even_though_margin_now_exists()
        {
            // Shorting is a borrow, so MarginTrading is now true. Leverage is a
            // different claim — position larger than collateral — and it is still
            // not implemented, so it is still not claimed.
            var paper = Make(out _);

            Assert.True(paper.SupportsMarginTrading);
            Assert.False(paper.Capabilities.HasFlag(ProviderCapabilities.Leverage));
            Assert.False(paper.SupportsFuturesTrading);
            Assert.Equal(1.0, paper.MaxLeverage);

            // And it cannot be talked into leverage through the back door.
            Assert.Equal(1.0, await paper.SetLeverageAsync(Btc, 25));
        }

        [Fact]
        public async Task A_refused_order_says_why_rather_than_failing_silently()
        {
            // A rejection with no reason is the silent-feedback defect: the user
            // is told an order did not happen and never told what to change.
            var paper = Make(out var store);
            var updates = Collect(paper);
            store.EmitState(StateWith(Btc, 99, 101, 98, 100));

            // Big enough that the account cannot post the collateral: 5,000 BTC at
            // 100 needs 500,000 against a 100,000 account.
            await paper.PlaceOrderAsync(new TradeSignal(Btc, OrderSide.Sell, 5000.0));

            var rejected = Assert.Single(updates.FindAll(u => u.Status == OrderStatus.Rejected));
            Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
            Assert.Contains("collateral", rejected.Reason);   // says WHY a sell can fail
        }
    }
}
