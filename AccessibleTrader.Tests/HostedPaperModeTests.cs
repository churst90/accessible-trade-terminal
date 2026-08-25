using System.Reactive.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins the hosted/demo "paper trading is forced on and cannot be turned off" contract.
///
/// The bug: <see cref="GeneralOrderService"/> decided paper-vs-live purely from the
/// <c>trading.paperTradingMode</c> setting, which hosted web accounts never set. So on
/// the web a data-only provider (Twelve Data, Bitstamp) failed the ITradingProvider cast
/// and the dashboard reported "does not support trading". The fix makes IsPaperMode also
/// return true whenever <see cref="DemoPolicy.AllowLiveTrading"/> is false, so hosted/demo
/// always routes to the paper broker regardless of the setting.
/// </summary>
public sealed class HostedPaperModeTests
{
    // A data-only market provider that does NOT implement ITradingProvider — the shape of
    // every provider offered on the hosted web build.
    private static (GeneralOrderService svc, ISettingsManager settings) MakeService(HostMode mode)
    {
        var data = Substitute.For<IDataService>();
        var dataOnly = Substitute.For<IMarketDataProvider>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(dataOnly));

        var err = Substitute.For<IGlobalErrorCoordinator>();
        var bus = new EventBus();
        var paper = Substitute.For<IPaperTradingProvider>();
        paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
        var settings = Substitute.For<ISettingsManager>();

        var svc = new GeneralOrderService(
            data, err, NullLogger<GeneralOrderService>.Instance, bus, paper, settings,
            new DemoPolicy(mode), new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());
        return (svc, settings);
    }

    /// <summary>
    /// The harder case, and the one this file exists for: a provider that CAN trade for real.
    /// <c>SupportsTradingAsync</c> answers true for it either way, so only watching where the
    /// order actually goes can tell paper routing from live routing.
    /// </summary>
    private static (GeneralOrderService svc, ISettingsManager settings,
                    IPaperTradingProvider paper, ITradingProvider live) MakeServiceWithLiveBroker(HostMode mode)
    {
        var live = Substitute.For<IMarketDataProvider, ITradingProvider>();
        var liveTrading = (ITradingProvider)live;
        liveTrading.IsConnected.Returns(true);
        liveTrading.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("LIVE-1"));
        liveTrading.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());

        var data = Substitute.For<IDataService>();
        data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(live));

        var paper = Substitute.For<IPaperTradingProvider>();
        paper.IsConnected.Returns(true);
        paper.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
        paper.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns(_ => Task.FromResult("PAPER-1"));

        var settings = Substitute.For<ISettingsManager>();
        var svc = new GeneralOrderService(
            data, Substitute.For<IGlobalErrorCoordinator>(), NullLogger<GeneralOrderService>.Instance,
            new EventBus(), paper, settings, new DemoPolicy(mode),
            new AccessibleTrader.Core.Services.Trading.QuickTradeEquity());

        return (svc, settings, paper, liveTrading);
    }

    private static TradeSignal Buy() =>
        new("BTC/USD", OrderSide.Buy, 0.1, OrderType.Market);

    [Fact]
    public async Task Hosted_forces_paper_so_data_only_provider_still_supports_trading()
    {
        // Hosted (--accounts) → AllowLiveTrading false → paper forced → trading "supported"
        // via the paper broker even though the underlying provider is data-only.
        var (svc, _) = MakeService(HostMode.Hosted);
        Assert.True(await svc.SupportsTradingAsync("Twelve Data"));
    }

    [Fact]
    public async Task Demo_forces_paper_too()
    {
        var (svc, _) = MakeService(HostMode.Demo);
        Assert.True(await svc.SupportsTradingAsync("Bitstamp"));
    }

    [Fact]
    public async Task Full_desktop_leaves_trading_provider_dependent_when_setting_off()
    {
        // Desktop (HostMode.Full): with paper mode off, a data-only provider genuinely
        // does not support trading — the pre-existing, correct behavior.
        var (svc, settings) = MakeService(HostMode.Full);
        settings.GetSetting("trading.paperTradingMode").Returns((JToken?)null);
        Assert.False(await svc.SupportsTradingAsync("Twelve Data"));
    }

    [Fact]
    public async Task Full_desktop_honors_the_opt_in_paper_setting()
    {
        // Desktop with the user's paper toggle on → routes to paper → supported.
        var (svc, settings) = MakeService(HostMode.Full);
        settings.GetSetting("trading.paperTradingMode").Returns(JToken.FromObject(true));
        Assert.True(await svc.SupportsTradingAsync("Twelve Data"));
    }

    // ── The case this file was named for and never tested ────────────────────
    //
    // Every test above leaves trading.paperTradingMode at the substitute default (null), so
    // "hosted forces paper REGARDLESS of the setting" was never exercised: null and false take
    // the same branch either way. An explicit false is what a hosted user could actually end up
    // with — a settings file carried over from the desktop build, or a toggle that shipped
    // reachable — and it is the only value that can distinguish the two implementations.

    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public async Task Paper_is_forced_even_when_the_setting_is_explicitly_false(HostMode mode)
    {
        var (svc, settings) = MakeService(mode);
        settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(false));

        Assert.True(await svc.SupportsTradingAsync("Twelve Data"));
    }

    [Theory]
    [InlineData(HostMode.Hosted)]
    [InlineData(HostMode.Demo)]
    public async Task An_order_routes_to_the_paper_broker_and_never_to_the_live_provider(HostMode mode)
    {
        // SupportsTradingAsync is a claim about capability; this is the claim about money. All
        // four original tests asserted only the former, so a hosted build that said "yes you can
        // trade" and then sent the order to the real venue would have passed every one of them.
        var (svc, settings, paper, live) = MakeServiceWithLiveBroker(mode);
        settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(false));

        string result = await svc.PlaceOrderAsync("Kraken", Buy());

        Assert.Equal("PAPER-1", result);
        await paper.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        await live.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
    }

    [Fact]
    public async Task On_the_desktop_with_paper_off_the_order_goes_to_the_real_broker()
    {
        // The control. Without it, "never reaches the live provider" could be true because the
        // live path is broken for everyone, which would say nothing about the hosted gate.
        var (svc, settings, paper, live) = MakeServiceWithLiveBroker(HostMode.Full);
        settings.GetSetting(SettingsKeys.PaperTradingMode).Returns(JToken.FromObject(false));

        string result = await svc.PlaceOrderAsync("Kraken", Buy());

        Assert.Equal("LIVE-1", result);
        await live.Received(1).PlaceOrderAsync(Arg.Any<TradeSignal>());
        await paper.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
    }
}
