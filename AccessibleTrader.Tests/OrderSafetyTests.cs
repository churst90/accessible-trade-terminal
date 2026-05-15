using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Phase C (2026-04-27 e19) order-safety pins.
    ///
    /// <para>
    /// The audit flagged: no quantity/price sanity bounds on PlaceOrderAsync,
    /// no idempotency key auto-generation, and no de-dup gate to prevent
    /// double-submits on UI double-click or post-network-flap retry. All three
    /// were patched in <see cref="GeneralOrderService"/>; these tests pin the
    /// behaviour so a future refactor can't silently revert it.
    /// </para>
    /// </summary>
    public class OrderSafetyTests
    {
        private static readonly TradeSignal SaneSignal = new(
            Symbol: "BTC/USD",
            Side: OrderSide.Buy,
            Quantity: 0.01,
            Type: OrderType.Market);

        private static (GeneralOrderService svc, ITradingProvider tp, IDataService data, IGlobalErrorCoordinator err)
            BuildService()
        {
            var data = Substitute.For<IDataService>();
            // GeneralOrderService.GetTradingProviderAsync casts the returned
            // IMarketDataProvider to ITradingProvider — so the substitute must
            // implement BOTH interfaces.
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var trading = (ITradingProvider)tp;
            trading.IsConnected.Returns(true);
            trading.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tp));
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var bus = new EventBus();
            var svc = new GeneralOrderService(data, err, NullLogger<GeneralOrderService>.Instance, bus);
            return (svc, trading, data, err);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(1e10)]
        public async Task PlaceOrder_RejectsInsaneQuantities(double qty)
        {
            var (svc, tp, _, _) = BuildService();
            var signal = SaneSignal with { Quantity = qty };

            var result = await svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("ORDER_REJECTED_QUANTITY", result);
            await tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task PlaceOrder_RejectsLimitOrderWithMissingPrice()
        {
            var (svc, tp, _, _) = BuildService();
            var signal = SaneSignal with { Type = OrderType.Limit, Price = null };

            var result = await svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("ORDER_REJECTED_PRICE", result);
            await tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-100.0)]
        [InlineData(double.NaN)]
        public async Task PlaceOrder_RejectsLimitOrderWithInsanePrice(double price)
        {
            var (svc, tp, _, _) = BuildService();
            var signal = SaneSignal with { Type = OrderType.Limit, Price = price };

            var result = await svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("ORDER_REJECTED_PRICE", result);
            await tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task PlaceOrder_AutoGeneratesClientOidWhenAbsent()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK"));
            TradeSignal? captured = null;
            await tp.PlaceOrderAsync(Arg.Do<TradeSignal>(s => captured = s));

            await svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.NotNull(captured?.ClientOid);
            Assert.StartsWith("atc-", captured!.ClientOid);
        }

        [Fact]
        public async Task PlaceOrder_DedupSuppressesSecondCallWithSameClientOid()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("ORDER_123"));
            var signal = SaneSignal with { ClientOid = "fixed-id" };

            var first = await svc.PlaceOrderAsync("Binance", signal);
            var second = await svc.PlaceOrderAsync("Binance", signal);

            Assert.Equal("ORDER_123", first);
            Assert.Equal("ORDER_DUPLICATE_SUPPRESSED", second);
            await tp.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task PlaceOrder_DedupAllowsDifferentClientOids()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK"));

            await svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "id-a" });
            await svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "id-b" });

            await tp.Received(2).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task PlaceOrder_DedupSegregatesByProvider()
        {
            var (svc, tp1, data, _) = BuildService();
            var tp2 = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp2t = (ITradingProvider)tp2;
            tp2t.IsConnected.Returns(true);
            tp2t.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            tp2t.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK2"));
            data.GetProviderAsync("Coinbase").Returns(_ => Task.FromResult<IMarketDataProvider?>(tp2));
            tp1.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK1"));

            var signal = SaneSignal with { ClientOid = "shared" };
            await svc.PlaceOrderAsync("Binance", signal);
            // Same ClientOid on a DIFFERENT provider must succeed — dedup key is (provider|oid).
            var second = await svc.PlaceOrderAsync("Coinbase", signal);

            Assert.Equal("OK2", second);
        }

        [Fact]
        public async Task PlaceOrder_RecoveryScansOpenOrders_WhenSubmitThrows()
        {
            var (svc, tp, _, err) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>())
                .Returns<Task<string>>(_ => throw new InvalidOperationException("network drop"));
            tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>
            {
                new("ORDER_999", "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01, 0.0, "open")
            }));

            var result = await svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.StartsWith("ORDER_UNCERTAIN:", result);
            Assert.Contains("ORDER_999", result);
        }
    }
}
