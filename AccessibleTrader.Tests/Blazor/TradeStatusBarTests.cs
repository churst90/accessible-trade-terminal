using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using Bunit;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// <b>The trade status bar</b> (2026-11-04). Cody: "if I have an open position I could see at a glance
/// at the bottom of the screen ... my trades listed something like 'btcusdt Kraken Futures isolated spot
/// long, up 14.3%' and have the text green and red to indicate the direction of the p and l ... something
/// that might could be customized somewhere maybe in the f12 settings".
/// </summary>
public sealed class TradeStatusBarTests
{
    private static BlazorTestHarness Harness(string style = "blocks", string figure = "percent", params Position[] positions)
    {
        var h = new BlazorTestHarness();
        h.SettingsManager.GetSetting(SettingsKeys.PaperTradingMode, Arg.Any<JToken?>()).Returns(new JValue(true));
        h.SettingsManager.GetSetting(SettingsKeys.TradeStatusBar, Arg.Any<JToken?>()).Returns(new JValue(style));
        h.SettingsManager.GetSetting(SettingsKeys.TradeStatusFigure, Arg.Any<JToken?>()).Returns(new JValue(figure));
        h.OrderService.GetPositionsAsync("Paper")
            .Returns(ProviderResult<List<Position>>.Ok(positions.ToList()));
        return h;
    }

    [Fact]
    public void An_open_position_is_listed_by_its_venue_direction_and_move_in_a_named_region()
    {
        // 1 ETH long from 3,000, 300 up = 10% on 1x.
        using var h = Harness(positions: new Position("ETHUSDT", 1.0, 3_000, 3_300, 300));
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("section.trade-status-bar")));
        var section = cut.Find("section.trade-status-bar");
        Assert.Equal("Open trades", section.GetAttribute("aria-label"));
        Assert.Null(section.GetAttribute("aria-live"));
        Assert.Null(section.GetAttribute("role"));
        var chip = cut.Find("li.trade-chip");
        Assert.Contains("up", chip.ClassList);
        Assert.Equal("ETHUSDT Paper long, up 10.0%", chip.TextContent.Replace("▲", "").Trim());
        Assert.Equal("blocks", section.GetAttribute("data-style"));
    }

    [Fact]
    public void A_losing_short_is_red_and_says_down()
    {
        using var h = Harness(positions: new Position("BTCUSDT", -0.5, 60_000, 31_500, -1_500, 5, 0, MarginMode.Isolated));
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("li.trade-chip")));
        var chip = cut.Find("li.trade-chip");
        Assert.Contains("down", chip.ClassList);
        // 1,500 lost on 6,000 of margin (0.5 x 60,000 / 5x) is 25%.
        Assert.Equal("BTCUSDT Paper isolated 5x short, down 25.0%", chip.TextContent.Replace("▼", "").Trim());
    }

    [Fact]
    public void No_positions_draws_nothing()
    {
        using var h = Harness();
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();
        cut.WaitForAssertion(() => h.OrderService.Received().GetPositionsAsync("Paper"));
        Assert.Empty(cut.FindAll("section.trade-status-bar"));
    }

    [Fact]
    public void Off_in_settings_draws_nothing_even_with_a_position()
    {
        using var h = Harness("off", positions: new Position("ETHUSDT", 1.0, 3_000, 3_300, 300));
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();
        Thread.Sleep(200);
        Assert.Empty(cut.FindAll("section.trade-status-bar"));
        // Off is off: no venue is asked for its positions every ten seconds either.
        h.OrderService.DidNotReceiveWithAnyArgs().GetPositionsAsync(default!);
    }

    [Fact]
    public void A_settings_change_is_picked_up_without_a_restart()
    {
        using var h = Harness(positions: new Position("ETHUSDT", 1.0, 3_000, 3_300, 300));
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("section.trade-status-bar")));

        h.SettingsManager.GetSetting(SettingsKeys.TradeStatusBar, Arg.Any<JToken?>()).Returns(new JValue("outline"));
        h.SettingsManager.GetSetting(SettingsKeys.TradeStatusFigure, Arg.Any<JToken?>()).Returns(new JValue("both"));
        h.EventBus.Publish(new TradeStatusBarChangedEvent());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("outline", cut.Find("section.trade-status-bar").GetAttribute("data-style"));
            Assert.Contains("up 10.0%, 300.00", cut.Find("li.trade-chip").TextContent);
        }, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_fill_reads_the_positions_again()
    {
        using var h = Harness();
        var cut = h.Ctx.RenderComponent<TradeStatusBar>();
        cut.WaitForAssertion(() => h.OrderService.Received(1).GetPositionsAsync("Paper"));
        Assert.Empty(cut.FindAll("section.trade-status-bar"));

        h.OrderService.GetPositionsAsync("Paper")
            .Returns(ProviderResult<List<Position>>.Ok(new List<Position> { new("ETHUSDT", 1.0, 3_000, 3_300, 300) }));
        h.EventBus.Publish(new OrderFilledEvent(new AccessibleTrader.Sdk.Trading.OrderUpdate("o1", "ETHUSDT", OrderSide.Buy, 1.0, 3_000, 0, AccessibleTrader.Sdk.Trading.OrderStatus.Filled, false, false, DateTime.UtcNow)));

        // Well inside the ten-second poll, so only the fill can have done it.
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("li.trade-chip")), TimeSpan.FromSeconds(3));
    }
}
