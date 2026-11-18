using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The chart menu's limit items open this dialog with the side, type and price already chosen —
/// and stop there. Placing an order still requires the user's own review and confirm.
/// </summary>
public sealed class TradingDashboardModalPrefillTests
{
    private static BlazorTestHarness Harness()
    {
        var h = new BlazorTestHarness();
        ModalCatalog.SeedChartState(h);
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
        h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
        h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.None);
        h.OrderService.GetBalancesAsync(default!)
            .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
        return h;
    }

    [Fact]
    public void A_sell_limit_request_prefills_side_type_and_price_without_placing_anything()
    {
        using var h = Harness();
        var cut = h.OpenModal<AccessibleTrader.BlazorClient.Components.TradingDashboardModal>(
            bus => bus.Publish(new OpenTradingDashboardEvent("Sell", 123.45)));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("button.side-btn.sell").GetAttribute("aria-pressed"));
            Assert.Equal("Limit", cut.Find("select#order-type").GetAttribute("value"));
            Assert.Equal("123.45", cut.Find("input#limit-price").GetAttribute("value"));
        });

        h.OrderService.DidNotReceive().PlaceOrderAsync(Arg.Any<string>(), Arg.Any<TradeSignal>());
    }
}
