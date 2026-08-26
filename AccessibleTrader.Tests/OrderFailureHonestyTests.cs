using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What the user is told when an order action does not do what it says.
    ///
    /// <para>
    /// Every case here used to be silent or, worse, indistinguishable from success: a
    /// cancel that quietly did nothing while the order stayed on the book, a leverage
    /// request that returned 1.0 whether the venue applied 1× on purpose or the call
    /// failed, and the one status code that means "the order may be live, verify before
    /// retrying" being translated as a clean success by the single translator every
    /// order path routes through.
    /// </para>
    /// </summary>
    public sealed class OrderFailureHonestyTests
    {
        private sealed record Harness(GeneralOrderService Svc, ITradingProvider Tp, IGlobalErrorCoordinator Err);

        private static Harness Build(bool connected = true, bool anyProvider = true)
        {
            var data = Substitute.For<IDataService>();
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            ((ITradingProvider)tp).IsConnected.Returns(connected);
            ((ITradingProvider)tp).OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            data.GetProviderAsync(Arg.Any<string>()).Returns(
                _ => Task.FromResult(anyProvider ? (IMarketDataProvider?)tp : null));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            var err = Substitute.For<IGlobalErrorCoordinator>();
            var svc = new GeneralOrderService(data, err, NullLogger<GeneralOrderService>.Instance,
                new EventBus(), paper, Substitute.For<ISettingsManager>(),
                new DemoPolicy(isDemo: false), new QuickTradeEquity());
            return new Harness(svc, (ITradingProvider)tp, err);
        }

        // ── Cancel ───────────────────────────────────────────────────────────

        [Fact]
        public async Task ACancelOnADisconnectedProvider_SaysTheOrderIsStillResting()
        {
            var h = Build(connected: false);

            Assert.False(await h.Svc.CancelOrderAsync("Binance", "abc", "BTC/USDT"));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("not connected")
                                                          && m.Contains("still resting")),
                                         ErrorSeverity.High);
        }

        [Fact]
        public async Task ACancelWithNoTradingProvider_SaysTheOrderIsStillResting()
        {
            var h = Build(anyProvider: false);

            Assert.False(await h.Svc.CancelOrderAsync("Binance", "abc", "BTC/USDT"));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("still resting")),
                                         ErrorSeverity.High);
        }

        [Fact]
        public async Task ACancelTheVenueRefuses_IsReported()
        {
            var h = Build();
            h.Tp.CancelOrderAsync("abc", "BTC/USDT").Returns(Task.FromResult(false));

            Assert.False(await h.Svc.CancelOrderAsync("Binance", "abc", "BTC/USDT"));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("refused to cancel")),
                                         ErrorSeverity.High);
        }

        [Fact]
        public async Task ACancelThatThrows_IsReportedRatherThanSwallowed()
        {
            var h = Build();
            h.Tp.CancelOrderAsync("abc", "BTC/USDT")
                .Returns<Task<bool>>(_ => throw new InvalidOperationException("socket closed"));

            Assert.False(await h.Svc.CancelOrderAsync("Binance", "abc", "BTC/USDT"));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("socket closed")),
                                         ErrorSeverity.High);
        }

        /// <summary>
        /// The complement: a cancel that works stays quiet. Without this the tests above
        /// pass just as well against a service that shouts on every cancel.
        /// </summary>
        [Fact]
        public async Task ACancelThatWorks_SaysNothing()
        {
            var h = Build();
            h.Tp.CancelOrderAsync("abc", "BTC/USDT").Returns(Task.FromResult(true));

            Assert.True(await h.Svc.CancelOrderAsync("Binance", "abc", "BTC/USDT"));

            h.Err.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<ErrorSeverity>());
        }

        // ── Leverage ─────────────────────────────────────────────────────────

        /// <summary>
        /// 1.0 is a real answer AND the failure value, so the failure has to speak. A
        /// user who asked for 5× and silently got 1× holds a position a fifth the size
        /// they believe.
        /// </summary>
        [Fact]
        public async Task LeverageThatCouldNotBeSet_IsAnnouncedRatherThanReturnedAsOneTimes()
        {
            var h = Build(connected: false);

            Assert.Equal(1.0, await h.Svc.SetLeverageAsync("Binance", "BTC/USDT", 5));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("Leverage was not set")),
                                         ErrorSeverity.Medium);
        }

        [Fact]
        public async Task LeverageTheVenueTrimmed_IsAnnouncedWithBothNumbers()
        {
            var h = Build();
            h.Tp.SetLeverageAsync("BTC/USDT", 20).Returns(Task.FromResult(10.0));

            Assert.Equal(10.0, await h.Svc.SetLeverageAsync("Binance", "BTC/USDT", 20));

            h.Err.Received().ReportError(Arg.Is<string>(m => m.Contains("10") && m.Contains("20")),
                                         ErrorSeverity.Medium);
        }

        [Fact]
        public async Task LeverageAppliedAsAsked_SaysNothing()
        {
            var h = Build();
            h.Tp.SetLeverageAsync("BTC/USDT", 5).Returns(Task.FromResult(5.0));

            Assert.Equal(5.0, await h.Svc.SetLeverageAsync("Binance", "BTC/USDT", 5));

            h.Err.DidNotReceive().ReportError(Arg.Any<string>(), Arg.Any<ErrorSeverity>());
        }

        // ── ORDER_UNCERTAIN ──────────────────────────────────────────────────

        /// <summary>
        /// The worst case there is: the submit threw AND a matching order was found on
        /// the exchange afterwards. The switch's default arm read "an order id — it
        /// went", so <c>QuickTradeExecutor</c> and <c>StrategyEngine</c> both treated the
        /// one code meaning "verify before retrying" as a clean success and said nothing.
        /// </summary>
        [Fact]
        public void OrderUncertain_IsNotTranslatedAsSuccess()
        {
            string? said = OrderPlacement.Parse("ORDER_UNCERTAIN:8891").FailureMessage;

            Assert.NotNull(said);
            Assert.Contains("8891", said);
            Assert.Contains("Check your open orders", said);
            // NOT "not placed": the order most likely IS live, and telling the user it
            // failed is how the same position gets opened twice.
            Assert.DoesNotContain("Not placed", said, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void OrderUncertain_WithNoIdAttached_StillSpeaks()
        {
            string? said = OrderPlacement.Parse("ORDER_UNCERTAIN").FailureMessage;

            Assert.NotNull(said);
            Assert.Contains("Check your open orders", said);
        }

        /// <summary>
        /// The vacuity check for the pair above: a real order id must still translate to
        /// null, or "everything is a failure" would pass all three.
        /// </summary>
        [Fact]
        public void ARealOrderId_IsStillSilence()
        {
            Assert.Null(OrderPlacement.Parse("8891").FailureMessage);
            Assert.Null(OrderPlacement.Parse("paper-1a2b3c4d5e6f").FailureMessage);
        }

        // ── "Order placed" is said AFTER the answer is read, not before ───────

        /// <summary>
        /// The defect this section exists for, and it was one line long.
        /// <c>ReportSuccess("Order placed: …")</c> fired on the statement immediately after
        /// <c>tp.PlaceOrderAsync</c> returned — before anything had looked at what the provider
        /// actually said. A provider answering <c>ORDER_FAILED:…</c> produced a spoken
        /// confirmation followed by silence, which for a blind trader is indistinguishable from
        /// a filled order. There is no compensating channel: nothing else would ever have
        /// mentioned it.
        /// </summary>
        [Theory]
        [InlineData("ORDER_FAILED:insufficient balance")]
        [InlineData("ORDER_REJECTED_QUANTITY")]
        [InlineData("ORDER_DUPLICATE_SUPPRESSED")]
        [InlineData("PROVIDER_NOT_CONFIGURED")]      // A2's mutant M21, as a user-facing claim
        [InlineData("ORDER_THROTTLED")]              // reserved prefix, unrecognised code
        public async Task AProviderRefusal_IsNeverAnnouncedAsOrderPlaced(string code)
        {
            var h = Build();
            h.Tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult(code));

            var placement = await h.Svc.PlaceOrderAsync("Binance", Sane);

            Assert.False(placement.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(placement.FailureMessage));
            h.Err.DidNotReceive().ReportSuccess(Arg.Any<string>());
            // Nothing was placed, so nothing may be polled: an id-less code handed to the status
            // poller is how the poller came to be fed garbage.
            Assert.Equal(0, h.Svc.OrderWatchesStarted);
        }

        /// <summary>
        /// The vacuity half. A guard that refused everything would pass the theory above and
        /// break the feature: an order that goes MUST still be announced as placed, and must
        /// still start the watch that announces its fill.
        /// </summary>
        [Fact]
        public async Task AnOrderThatGoes_IsStillAnnouncedAsPlaced()
        {
            var h = Build();
            h.Tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("EX-77"));

            var placement = await h.Svc.PlaceOrderAsync("Binance", Sane);

            Assert.True(placement.Succeeded);
            Assert.Equal("EX-77", placement.OrderId);
            h.Err.Received().ReportSuccess(Arg.Is<string>(m => m.Contains("Order placed")));
            Assert.Equal(1, h.Svc.OrderWatchesStarted);
        }

        /// <summary>
        /// <c>ORDER_SUBMITTED</c> is the case both old recognisers disagreed about. It is a
        /// success — say "Order placed" — but there is no id, so no watch starts and the user is
        /// told, separately, that this order's fill cannot be announced. Announcing the success
        /// without that caveat lets "placed" imply an outcome that will never arrive.
        /// </summary>
        [Fact]
        public async Task AVenueAcceptanceWithNoId_IsPlacedButSaysItsFillCannotBeAnnounced()
        {
            var h = Build();
            h.Tp.SupportsOrderEventStreaming.Returns(false);
            h.Tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("ORDER_SUBMITTED"));

            var placement = await h.Svc.PlaceOrderAsync("Binance", Sane);

            Assert.True(placement.Succeeded);
            Assert.False(placement.HasOrderId);
            Assert.Equal(0, h.Svc.OrderWatchesStarted);
            h.Err.Received().ReportSuccess(Arg.Is<string>(m => m.Contains("Order placed")));
            h.Err.Received().ReportError(
                Arg.Is<string>(m => m.Contains("did not return an order id")),
                ErrorSeverity.Medium);
        }

        /// <summary>
        /// And the consequence of reading it as a success: a bracket on an id-less acceptance is
        /// now VERIFIED. The old prefix test skipped the protective-order scan for exactly these
        /// orders — the ones where nothing else can ever report a missing stop, because there is
        /// no id to poll and (here) no order-event stream either.
        /// </summary>
        [Fact]
        public async Task ABracketOnAnIdLessAcceptance_IsStillVerified()
        {
            var h = Build();
            h.Svc.ProtectionVerifyDelay = TimeSpan.Zero;
            h.Tp.SupportsOrderEventStreaming.Returns(false);
            h.Tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("ORDER_SUBMITTED"));
            h.Tp.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>()));

            await h.Svc.PlaceOrderAsync("Binance", Sane with { StopLoss = 44_000.0 });

            // The scan runs fire-and-forget so the order is not delayed by it. Wait for it
            // rather than sleeping a fixed amount: this fails only if it genuinely never runs.
            bool alarmed = await Eventually(() => h.Err.ReceivedCalls().Any(c =>
                c.GetMethodInfo().Name == nameof(IGlobalErrorCoordinator.ReportError)
                && c.GetArguments()[0] is string m
                && (m.Contains("unprotected") || m.Contains("no stop loss"))));

            Assert.True(alarmed, "A bracket on an id-less acceptance was never verified.");
        }

        private static TradeSignal Sane =>
            new("BTC/USD", OrderSide.Buy, 0.01, OrderType.Market);

        /// <summary>Polls a condition to a deadline. Fails slow, never fails fast on a busy box.</summary>
        private static async Task<bool> Eventually(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(25);
            }
            return condition();
        }
    }
}
