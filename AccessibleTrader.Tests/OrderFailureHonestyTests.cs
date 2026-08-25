using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

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
            string? said = OrderResult.DescribeFailure("ORDER_UNCERTAIN:8891");

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
            string? said = OrderResult.DescribeFailure("ORDER_UNCERTAIN");

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
            Assert.Null(OrderResult.DescribeFailure("8891"));
            Assert.Null(OrderResult.DescribeFailure("paper-1a2b3c4d5e6f"));
        }
    }
}
