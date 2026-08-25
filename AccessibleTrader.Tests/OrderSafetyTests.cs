using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
            // Paper broker + settings: GeneralOrderService subscribes to the paper
            // OrderUpdateStream at construction and reads trading.paperTradingMode to
            // decide routing. Default (no setting) routes to the live provider above.
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            var settings = Substitute.For<ISettingsManager>();
            // Full host mode → AllowLiveTrading true, so routing follows the (unset)
            // paperTradingMode setting and reaches the live provider substitute above.
            var demo = new DemoPolicy(isDemo: false);
            var svc = new GeneralOrderService(data, err, NullLogger<GeneralOrderService>.Instance, bus, paper, settings, demo,
                new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
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

        /// <summary>
        /// Pins the id that goes ON THE WIRE, which is a separate concern from dedup:
        /// several venues (Binance, Coinbase, Kraken, Alpaca, IBKR, MEXC, Gemini) reject a
        /// repeat of a client order id, and that exchange-side check is the only thing that
        /// can catch a duplicate this process never saw.
        ///
        /// <para>
        /// It is emphatically NOT evidence that the terminal-side dedup gate works — for two
        /// years it was read that way, and because a fresh GUID lands here on every submit,
        /// the gate keyed on it could never match itself. See
        /// <see cref="PlaceOrder_DedupSuppressesAccidentalDoubleSubmit_WithNoClientOid"/>
        /// for the property that actually protects the user.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// Two DIFFERENT explicit ids are two orders the caller deliberately asked for, and
        /// both must go. This is the escape hatch for anything that legitimately wants to
        /// place the same shape of order twice in a row.
        /// </summary>
        [Fact]
        public async Task PlaceOrder_DedupAllowsDifferentClientOids()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK"));

            await svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "id-a" });
            await svc.PlaceOrderAsync("Binance", SaneSignal with { ClientOid = "id-b" });

            await tp.Received(2).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        /// <summary>
        /// THE property this gate exists for, stated without reference to the mechanism:
        /// a user cannot accidentally submit the same order twice.
        ///
        /// <para>
        /// No production caller sets <c>ClientOid</c> — not the dashboard ticket, not
        /// Close position, not <c>QuickTradeExecutor</c>, not <c>StrategyEngine</c> — so
        /// this is the ONLY shape the gate ever meets in the field. A screen-reader user
        /// pressing Enter twice on a submit button is the routine case, not the exotic one.
        /// </para>
        /// </summary>
        [Fact]
        public async Task PlaceOrder_DedupSuppressesAccidentalDoubleSubmit_WithNoClientOid()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("ORDER_123"));

            var first  = await svc.PlaceOrderAsync("Binance", SaneSignal);
            var second = await svc.PlaceOrderAsync("Binance", SaneSignal);

            Assert.Equal("ORDER_123", first);
            Assert.Equal("ORDER_DUPLICATE_SUPPRESSED", second);
            await tp.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        /// <summary>
        /// The other half, and the reason the fix is a fingerprint rather than a blanket
        /// "one order per symbol per 30 seconds": scaling into a position, or changing your
        /// mind about the size, must still work.
        /// </summary>
        [Theory]
        [MemberData(nameof(GenuinelyDifferentOrders))]
        public async Task PlaceOrder_DedupAllowsAGenuinelyDifferentOrder_WithNoClientOid(TradeSignal other)
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK"));

            await svc.PlaceOrderAsync("Binance", SaneSignal);
            await svc.PlaceOrderAsync("Binance", other);

            await tp.Received(2).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        public static TheoryData<TradeSignal> GenuinelyDifferentOrders() => new()
        {
            SaneSignal with { Quantity = 0.02 },
            SaneSignal with { Side = OrderSide.Sell },
            SaneSignal with { Symbol = "ETH/USD" },
            SaneSignal with { Type = OrderType.Limit, Price = 50_000 },
            // A close and an entry of equal size are opposite intentions. If these
            // collapsed into one key, the second — whichever it was — would be eaten.
            SaneSignal with { ReduceOnly = true },
            // Spot and futures are different books, so the same numbers are two orders.
            SaneSignal with { SubType = "Futures" },
        };

        /// <summary>
        /// The window is a window, not a lock: after it expires the same order goes again.
        /// Uses the private field rather than sleeping 30 seconds — the point is that the
        /// gate is time-bounded, and a test that proved it by waiting would never be run.
        /// </summary>
        [Fact]
        public async Task PlaceOrder_DedupWindowExpires()
        {
            var (svc, tp, _, _) = BuildService();
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("OK"));

            await svc.PlaceOrderAsync("Binance", SaneSignal);

            var map = (System.Collections.Generic.Dictionary<string, DateTime>)typeof(GeneralOrderService)
                .GetField("_recentOrders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(svc)!;
            Assert.Single(map);
            foreach (var k in new List<string>(map.Keys)) map[k] = DateTime.UtcNow.AddMinutes(-5);

            await svc.PlaceOrderAsync("Binance", SaneSignal);

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

        // ── Protective-order verification net (2026-06-12 audit fix 2) ──────────
        // Providers attach TP/SL as separate orders after the entry; that attach
        // can fail silently, leaving a naked position. GeneralOrderService now
        // scans open orders after a bracket placement and alarms when nothing
        // protective is found.

        [Fact]
        public async Task VerifyProtection_Alarms_WhenNoProtectiveOrderExists()
        {
            var (svc, tp, _, err) = BuildService();
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            // Exchange shows only the entry order — no stop, no TP.
            tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>
            {
                new("E1", "BTC/USD", OrderSide.Buy, OrderType.Limit, 0.01, 45000, "open")
            }));
            var bracket = SaneSignal with { StopLoss = 44000.0 };

            await svc.VerifyProtectiveOrdersAsync(tp, "Binance", bracket);

            err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("unprotected") || m.Contains("no stop loss")),
                ErrorSeverity.High,
                Arg.Any<ErrorCategory>());
        }

        [Fact]
        public async Task VerifyProtection_Quiet_WhenOppositeSideStopExists()
        {
            var (svc, tp, _, err) = BuildService();
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>
            {
                new("SL1", "BTC/USD", OrderSide.Sell, OrderType.StopMarket, 0.01, 0.0, "open")
            }));
            var bracket = SaneSignal with { StopLoss = 44000.0 };

            await svc.VerifyProtectiveOrdersAsync(tp, "Binance", bracket);

            err.DidNotReceive().ReportError(
                Arg.Any<string>(), Arg.Any<ErrorSeverity>(), Arg.Any<ErrorCategory>());
        }

        [Fact]
        public async Task VerifyProtection_Quiet_WhenEntryCarriesEmbeddedStop()
        {
            var (svc, tp, _, err) = BuildService();
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            // Providers that embed SL/TP in the entry order surface them as fields.
            tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>
            {
                new("E1", "BTC/USD", OrderSide.Buy, OrderType.Limit, 0.01, 45000, "open",
                    StopLoss: 44000.0)
            }));
            var bracket = SaneSignal with { StopLoss = 44000.0 };

            await svc.VerifyProtectiveOrdersAsync(tp, "Binance", bracket);

            err.DidNotReceive().ReportError(
                Arg.Any<string>(), Arg.Any<ErrorSeverity>(), Arg.Any<ErrorCategory>());
        }

        [Fact]
        public async Task VerifyProtection_ReportsVerifyFailure_WhenScanThrows()
        {
            var (svc, tp, _, err) = BuildService();
            svc.ProtectionVerifyDelay = TimeSpan.Zero;
            tp.GetOpenOrdersAsync(Arg.Any<string?>())
                .Returns<Task<List<OpenOrder>>>(_ => throw new TimeoutException("exchange down"));
            var bracket = SaneSignal with { TakeProfit = 50000.0 };

            await svc.VerifyProtectiveOrdersAsync(tp, "Binance", bracket);

            err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("Could not verify")),
                ErrorSeverity.High,
                Arg.Any<ErrorCategory>());
        }
    }
}
