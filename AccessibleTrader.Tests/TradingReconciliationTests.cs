using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Restart safety: exposure that survives an app restart (persisted paper
/// positions, live positions resting at the broker) must be announced without
/// the user having to open the Trading Dashboard to discover it.
/// </summary>
public class TradingReconciliationTests
{
    private static Position Pos(string sym = "BTC/USDT") => new(sym, 1.0, 100.0, 100.0, 0.0);
    private static OpenOrder Ord(string id = "o1") =>
        new(id, "BTC/USDT", OrderSide.Buy, OrderType.Limit, 1.0, 100.0, "open");

    private sealed record Harness(
        TradingReconciliationCoordinator Coordinator,
        SpyEventBus Bus,
        IOrderExecutionService Orders,
        IPaperTradingProvider Paper);

    private static Harness Build(bool paperMode)
    {
        var bus = new SpyEventBus();
        var orders = Substitute.For<IOrderExecutionService>();
        var paper = Substitute.For<IPaperTradingProvider>();
        paper.GetPositionsAsync().Returns(new List<Position>());
        paper.GetOpenOrdersAsync().Returns(new List<OpenOrder>());
        var settings = Substitute.For<ISettingsManager>();
        if (paperMode)
            settings.GetSetting("trading.paperTradingMode").Returns(JToken.FromObject(true));

        var coordinator = new TradingReconciliationCoordinator(
            bus, orders, paper, settings,
            new DemoPolicy(isDemo: false),
            NullLogger<TradingReconciliationCoordinator>.Instance);
        return new Harness(coordinator, bus, orders, paper);
    }

    private static List<FeedbackRequestEvent> Announcements(SpyEventBus bus) =>
        bus.Log.OfType<FeedbackRequestEvent>().ToList();

    // The connection handler is fire-and-forget; with substitute-backed tasks it
    // completes synchronously, but poll briefly so a scheduler hiccup can't flake.
    private static async Task SettleAsync(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(10);
    }

    // ── Paper account at startup ─────────────────────────────────────────────

    [Fact]
    public async Task Paper_startup_announces_persisted_exposure()
    {
        var h = Build(paperMode: true);
        h.Paper.GetPositionsAsync().Returns(new List<Position> { Pos(), Pos("ETH/USDT") });
        h.Paper.GetOpenOrdersAsync().Returns(new List<OpenOrder> { Ord() });

        await h.Coordinator.AnnounceAtStartupAsync();

        var evt = Assert.Single(Announcements(h.Bus));
        Assert.Equal(FeedbackType.StateChange, evt.Type);
        Assert.Contains("Paper account: 2 open positions and 1 working order", evt.Message);
        Assert.False(evt.IsUserInitiated);
    }

    [Fact]
    public async Task Paper_startup_with_flat_account_stays_silent()
    {
        var h = Build(paperMode: true);

        await h.Coordinator.AnnounceAtStartupAsync();

        Assert.Empty(Announcements(h.Bus));
    }

    [Fact]
    public async Task Live_mode_startup_does_not_touch_the_paper_account()
    {
        var h = Build(paperMode: false);

        await h.Coordinator.AnnounceAtStartupAsync();

        Assert.Empty(Announcements(h.Bus));
        await h.Paper.DidNotReceive().GetPositionsAsync();
    }

    // ── Live broker on first connect ─────────────────────────────────────────

    [Fact]
    public async Task Live_first_connect_announces_broker_exposure()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Binance").Returns(true);
        h.Orders.GetPositionsAsync("Binance").Returns(new List<Position> { Pos() });
        h.Orders.GetOpenOrdersAsync("Binance").Returns(new List<OpenOrder>());

        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));
        await SettleAsync(() => Announcements(h.Bus).Count > 0);

        var evt = Assert.Single(Announcements(h.Bus));
        Assert.Contains("Binance: 1 open position", evt.Message);
        Assert.False(evt.IsUserInitiated);
    }

    [Fact]
    public async Task Live_reconnect_of_same_provider_announces_only_once()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Binance").Returns(true);
        h.Orders.GetPositionsAsync("Binance").Returns(new List<Position> { Pos() });
        h.Orders.GetOpenOrdersAsync("Binance").Returns(new List<OpenOrder>());

        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));
        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up again"));
        await SettleAsync(() => Announcements(h.Bus).Count > 0);

        Assert.Single(Announcements(h.Bus));
        await h.Orders.Received(1).GetPositionsAsync("Binance");
    }

    [Fact]
    public async Task Data_only_provider_connect_stays_silent()
    {
        // SupportsTradingAsync default (false) == a data-only provider like FRED.
        var h = Build(paperMode: false);

        h.Bus.Publish(new ConnectionStatusEvent("FRED", ConnectionState.Connected, "up"));
        await Task.Delay(50);

        Assert.Empty(Announcements(h.Bus));
        await h.Orders.DidNotReceive().GetPositionsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Live_connect_while_in_paper_mode_stays_silent()
    {
        // Paper mode routes every order to the paper broker, which was already
        // announced at startup — a provider connection adds nothing to say.
        var h = Build(paperMode: true);
        h.Orders.SupportsTradingAsync("Binance").Returns(true);

        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));
        await Task.Delay(50);

        Assert.Empty(Announcements(h.Bus));
        await h.Orders.DidNotReceive().GetPositionsAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Flat_live_account_on_connect_stays_silent()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Kraken").Returns(true);
        h.Orders.GetPositionsAsync("Kraken").Returns(new List<Position>());
        h.Orders.GetOpenOrdersAsync("Kraken").Returns(new List<OpenOrder>());

        h.Bus.Publish(new ConnectionStatusEvent("Kraken", ConnectionState.Connected, "up"));
        await Task.Delay(50);

        Assert.Empty(Announcements(h.Bus));
    }
}
