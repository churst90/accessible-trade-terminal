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
    /// An entry carrying a stop or target is refused BEFORE it is sent when the venue cannot
    /// attach one (2026-11-14). Bitstamp, Coinbase, Gemini, Kraken Futures and MEXC spot sent the
    /// entry and dropped the stop without a word; the "may be unprotected" check two seconds
    /// later was the first anyone heard of it, with the position already open.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class BracketAttachRefusalTests
    {
        private static readonly TradeSignal Entry = new("BTC/USD", OrderSide.Buy, 0.01, OrderType.Market);

        private static (GeneralOrderService svc, ITradingProvider tp) Build(bool attaches)
        {
            var data = Substitute.For<IDataService>();
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var trading = (ITradingProvider)tp;
            trading.IsConnected.Returns(true);
            trading.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            trading.HasPracticeEnvironment.Returns(true);
            trading.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(attaches);
            trading.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("EX-1"));
            trading.GetOpenOrdersAsync(Arg.Any<string?>()).Returns(_ => Task.FromResult(new List<OpenOrder>()));
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tp));
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            paper.HasPracticeEnvironment.Returns(true);
            paper.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(true);
            var svc = new GeneralOrderService(data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, new EventBus(), paper, Substitute.For<ISettingsManager>(),
                new DemoPolicy(isDemo: false), new QuickTradeEquity());
            svc.ProtectionVerifyDelay = TimeSpan.FromHours(1);   // the after-the-fact check is not what is tested here
            return (svc, trading);
        }

        [Theory]
        [InlineData(44_000.0, null)]
        [InlineData(null, 50_000.0)]
        public async Task A_stop_or_target_on_an_entry_is_refused_unsent_where_the_venue_cannot_attach_it(double? sl, double? tpx)
        {
            var (svc, tp) = Build(attaches: false);

            var r = await svc.PlaceOrderAsync("Coinbase", Entry with { StopLoss = sl, TakeProfit = tpx });

            Assert.False(r.Succeeded);
            Assert.Contains("cannot attach a stop loss or take profit", r.FailureMessage);
            await tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        // Vacuity: the same venue still takes a plain entry, and a stop ORDER whose stop is its own
        // trigger is not a bracket — the Positions-tab stop editor sends exactly that.
        [Fact]
        public async Task The_same_venue_still_sends_a_plain_entry_and_a_stop_order()
        {
            var (svc, tp) = Build(attaches: false);

            var plain = await svc.PlaceOrderAsync("Coinbase", Entry);
            Assert.True(plain.Succeeded, plain.Raw + " / " + plain.FailureMessage);
            Assert.True((await svc.PlaceOrderAsync("Coinbase", Entry with
            {
                Side = OrderSide.Sell, Type = OrderType.StopMarket, StopLoss = 44_000.0, TriggerPrice = 44_000.0,
            })).Succeeded);
            await tp.Received(2).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task A_venue_that_attaches_gets_the_bracket()
        {
            var (svc, tp) = Build(attaches: true);

            Assert.True((await svc.PlaceOrderAsync("Binance", Entry with { StopLoss = 44_000.0 })).Succeeded);
            await tp.Received(1).PlaceOrderAsync(Arg.Is<TradeSignal>(s => s.StopLoss == 44_000.0));
        }

        /// <summary>
        /// Every trading venue's answer for a spot entry, by name, so a new venue has to decide.
        /// A plugin that attaches nothing must not say it does: that is the silent drop again.
        /// </summary>
        [Fact]
        public void Each_venue_says_whether_it_attaches_a_stop_to_a_spot_entry()
        {
            var noAttach = ProviderRoster.Trading()
                .Where(p => !((ITradingProvider)p).AttachesProtectiveLegs(Entry with { StopLoss = 44_000.0 }))
                .Select(p => p.GetType().Name).OrderBy(n => n).ToArray();

            Assert.Equal(new[] { "BitstampProvider", "CoinbaseProvider", "GeminiProvider", "KalshiProvider", "KrakenFuturesProvider", "MexcProvider" },
                noAttach);
        }

        [Fact]
        public void Mexc_attaches_on_futures_only_and_Oanda_attaches()
        {
            ITradingProvider mexc = new AccessibleTrader.Plugins.Mexc.MexcProvider();
            Assert.True(mexc.AttachesProtectiveLegs(Entry with { StopLoss = 44_000.0, SubType = "Futures" }));
            Assert.False(mexc.AttachesProtectiveLegs(Entry with { StopLoss = 44_000.0 }));

            ITradingProvider oanda = new AccessibleTrader.Plugins.Oanda.OandaProvider();
            Assert.True(oanda.AttachesProtectiveLegs(Entry with { StopLoss = 1.05 }));
        }
    }
}
