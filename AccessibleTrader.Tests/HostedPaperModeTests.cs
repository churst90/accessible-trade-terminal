using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

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
            new DemoPolicy(mode));
        return (svc, settings);
    }

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
}
