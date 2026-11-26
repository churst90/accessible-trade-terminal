using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// A TWAP is the venue's own slicer (2026-11-26, Hyperliquid first). It reaches only a
    /// provider declaring <see cref="ProviderCapabilities.Twap"/>; any other, the paper broker
    /// included, is refused in the terminal's words before it sees an order type it does not know.
    /// </summary>
    public class TwapRoutingTests
    {
        private static readonly TradeSignal Twap = new("BTC-PERP", OrderSide.Buy, 0.5, OrderType.Twap, TwapMinutes: 30);

        private static (GeneralOrderService Svc, ITradingProvider Venue, IPaperTradingProvider Paper) Build(
            ProviderCapabilities venueCaps, bool paperMode = false)
        {
            var data = Substitute.For<IDataService>();
            var mp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp = (ITradingProvider)mp;
            tp.IsConnected.Returns(true);
            tp.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            tp.SupportsOrderEventStreaming.Returns(true);
            tp.HasPracticeEnvironment.Returns(true);
            tp.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(true);
            tp.Capabilities.Returns(venueCaps);
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("twap:7"));
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(mp));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            paper.HasPracticeEnvironment.Returns(true);
            paper.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(true);
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            paper.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("PAPER-1"));

            var settings = Substitute.For<ISettingsManager>();
            if (paperMode)
                settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));

            var svc = new GeneralOrderService(data, Substitute.For<IGlobalErrorCoordinator>(),
                NullLogger<GeneralOrderService>.Instance, new EventBus(), paper, settings,
                new DemoPolicy(isDemo: false), new QuickTradeEquity());
            return (svc, tp, paper);
        }

        [Fact]
        public async Task A_venue_without_TWAP_is_refused_by_name_and_never_sees_it()
        {
            var (svc, venue, _) = Build(ProviderCapabilities.Shorting | ProviderCapabilities.MarginTrading);

            var r = await svc.PlaceOrderAsync("Kraken", Twap);

            Assert.False(r.Succeeded);
            Assert.Contains("Kraken does not run TWAP orders", r.FailureMessage);
            await venue.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task Paper_trading_says_it_does_not_simulate_TWAP_and_how_to_send_one_for_real()
        {
            var (svc, venue, paper) = Build(ProviderCapabilities.Twap, paperMode: true);

            var r = await svc.PlaceOrderAsync("Hyperliquid", Twap);

            Assert.False(r.Succeeded);
            Assert.Contains("paper trading does not simulate TWAP orders", r.FailureMessage);
            Assert.Contains("F12", r.FailureMessage);
            await paper.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
            await venue.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task A_venue_that_declares_TWAP_gets_it_with_its_minutes()
        {
            var (svc, venue, _) = Build(ProviderCapabilities.Twap);

            var r = await svc.PlaceOrderAsync("Hyperliquid", Twap with { TwapRandomize = true });

            Assert.True(r.Succeeded, r.Raw + " / " + r.FailureMessage);
            await venue.Received(1).PlaceOrderAsync(Arg.Is<TradeSignal>(s =>
                s.Type == OrderType.Twap && s.TwapMinutes == 30 && s.TwapRandomize));
        }

        [Fact]
        public async Task Other_order_types_are_not_touched_by_the_TWAP_check()
        {
            var (svc, venue, _) = Build(ProviderCapabilities.None);

            Assert.True((await svc.PlaceOrderAsync("Kraken", Twap with { Type = OrderType.Market, TwapMinutes = null })).Succeeded);
            await venue.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        }
    }
}
