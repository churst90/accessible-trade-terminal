using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Which key signs, which host receives, and the one refusal that stands between a
    /// mislabelled key and real money.</b> See <c>docs/ORDER_ROUTING_SAFETY_SCOPE.md</c>.
    ///
    /// <para>
    /// The defect these pin is not exotic. Six venues have no practice environment at all, so a
    /// stored key labelled "Paper" there signs a REAL order — and the dashboard's spoken live
    /// review keys off the same label, so it was skipped for exactly those orders. The dangerous
    /// case was also the quiet one.
    /// </para>
    ///
    /// <para>
    /// Each refusal below asserts the provider was never called, not merely that the result was a
    /// failure: a refusal that arrives after the order went is not a refusal.
    /// </para>
    /// </summary>
    public class OrderRoutingSafetyTests
    {
        private const string Provider = "Kraken";

        private static readonly TradeSignal Signal = new(
            Symbol: "BTC/USD", Side: OrderSide.Buy, Quantity: 0.01, Type: OrderType.Market);

        private static ApiKeyConfig Key(string environment, string nickname = "kraken-main") =>
            new(Provider, nickname, "ak", "sk", "", "Spot", environment);

        private sealed record Rig(
            GeneralOrderService Svc, ITradingProvider Tp, IDataService Data, IGlobalErrorCoordinator Err);

        /// <param name="hasPracticeEnvironment">The venue's own answer. NSubstitute intercepts the
        /// default interface member and would say false, so every case states it explicitly —
        /// which is the point: this flag is the whole gate.</param>
        private static Rig Build(bool hasPracticeEnvironment, bool paperMode = false)
        {
            var data = Substitute.For<IDataService>();
            var mp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var tp = (ITradingProvider)mp;
            tp.IsConnected.Returns(true);
            tp.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            tp.SupportsOrderEventStreaming.Returns(true);
            tp.HasPracticeEnvironment.Returns(hasPracticeEnvironment);
            tp.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("VENUE-1"));
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(mp));

            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            paper.HasPracticeEnvironment.Returns(true);
            paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            paper.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("PAPER-1"));

            var settings = Substitute.For<ISettingsManager>();
            if (paperMode)
                settings.GetSetting(SettingsKeys.PaperTradingMode)
                    .Returns(Newtonsoft.Json.Linq.JToken.FromObject(true));

            var err = Substitute.For<IGlobalErrorCoordinator>();
            var svc = new GeneralOrderService(
                data, err, NullLogger<GeneralOrderService>.Instance, new EventBus(), paper, settings,
                new DemoPolicy(isDemo: false), new QuickTradeEquity());
            return new Rig(svc, tp, data, err);
        }

        // ── The refusal ───────────────────────────────────────────────────────

        [Theory]
        [InlineData("Paper")]
        [InlineData("")]          // a legacy profile, stored before the field existed
        [InlineData("sandbox")]   // anything unrecognised fails toward the safe side
        public async Task Paper_key_on_a_venue_with_no_practice_environment_is_refused(string environment)
        {
            var r = Build(hasPracticeEnvironment: false);
            r.Data.CredentialInUse(Provider).Returns(Key(environment));

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.False(placement.Succeeded);
            await r.Tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
            // The sentence has to carry the venue, the key and the way out, because it is all the
            // user gets: this order never reached a venue that could explain itself.
            Assert.Contains(Provider, placement.FailureMessage!);
            Assert.Contains("kraken-main", placement.FailureMessage!);
            Assert.Contains("no practice venue", placement.FailureMessage!);
            Assert.Contains("F12", placement.FailureMessage!);
        }

        [Fact]
        public async Task Live_key_on_a_venue_with_no_practice_environment_is_placed()
        {
            var r = Build(hasPracticeEnvironment: false);
            r.Data.CredentialInUse(Provider).Returns(Key("Live"));

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.True(placement.Succeeded);
            Assert.Equal("VENUE-1", placement.OrderId);
        }

        [Fact]
        public async Task Paper_key_on_a_venue_WITH_a_practice_environment_is_placed()
        {
            // Alpaca, Tradier, Gemini, OANDA, Binance: the label picks the host and the two agree.
            var r = Build(hasPracticeEnvironment: true);
            r.Data.CredentialInUse(Provider).Returns(Key("Paper"));

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.True(placement.Succeeded);
        }

        [Fact]
        public async Task No_recorded_credential_on_a_venue_with_no_practice_environment_is_refused()
        {
            // Uncertainty resolves to REFUSE. An order signed with a credential nobody can name
            // is the thing the record exists to prevent.
            var r = Build(hasPracticeEnvironment: false);
            r.Data.CredentialInUse(Provider).Returns((ApiKeyConfig?)null);

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.False(placement.Succeeded);
            await r.Tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }

        [Fact]
        public async Task A_venue_with_a_practice_environment_is_never_asked_for_a_credential()
        {
            // The gate is the VENUE's capability, not the key's look. A venue with a sandbox must
            // not start refusing orders because nothing has recorded a credential for it.
            var r = Build(hasPracticeEnvironment: true);
            r.Data.CredentialInUse(Provider).Returns((ApiKeyConfig?)null);

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.True(placement.Succeeded);
        }

        [Fact]
        public async Task Paper_trading_mode_still_routes_to_the_simulator_and_is_never_refused()
        {
            // F12 is the OTHER way to trade without real money, and it does not touch a venue at
            // all. A refusal here would break the one path that is unambiguously safe.
            var r = Build(hasPracticeEnvironment: false, paperMode: true);
            r.Data.CredentialInUse(Provider).Returns(Key("Paper"));

            var placement = await r.Svc.PlaceOrderAsync(Provider, Signal);

            Assert.True(placement.Succeeded);
            Assert.Equal("PAPER-1", placement.OrderId);
            await r.Tp.DidNotReceive().PlaceOrderAsync(Arg.Any<TradeSignal>());
        }
    }
}
