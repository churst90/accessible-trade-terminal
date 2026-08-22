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
    /// <summary>
    /// A successful read. Explicit because the point of <see cref="ProviderResult{T}"/>
    /// is that a bare list no longer says whether the fetch worked — these tests
    /// have to state which they mean, and most of them mean "it worked".
    /// </summary>
    private static ProviderResult<List<T>> Ok<T>(List<T> items) => ProviderResult<List<T>>.Ok(items);

    private static Position Pos(string sym = "BTC/USDT") => new(sym, 1.0, 100.0, 100.0, 0.0);
    private static OpenOrder Ord(string id = "o1") =>
        new(id, "BTC/USDT", OrderSide.Buy, OrderType.Limit, 1.0, 100.0, "open");

    private sealed record Harness(
        TradingReconciliationCoordinator Coordinator,
        SpyEventBus Bus,
        IOrderExecutionService Orders,
        IPaperTradingProvider Paper);

    private static Harness Build(bool paperMode, string? dataDir = null)
    {
        var bus = new SpyEventBus();
        var orders = Substitute.For<IOrderExecutionService>();
        var paper = Substitute.For<IPaperTradingProvider>();
        paper.GetPositionsAsync().Returns(new List<Position>());
        paper.GetOpenOrdersAsync().Returns(new List<OpenOrder>());
        var settings = Substitute.For<ISettingsManager>();
        if (paperMode)
            settings.GetSetting("trading.paperTradingMode").Returns(JToken.FromObject(true));

        var paths = Substitute.For<IPlatformPathService>();
        paths.AppDataDirectory.Returns(dataDir ?? System.IO.Directory.CreateTempSubdirectory("att-recon-").FullName);
        var coordinator = new TradingReconciliationCoordinator(
            bus, orders, paper, settings,
            new DemoPolicy(isDemo: false), paths,
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
        h.Orders.GetPositionsAsync("Binance").Returns(Ok(new List<Position> { Pos() }));
        h.Orders.GetOpenOrdersAsync("Binance").Returns(Ok(new List<OpenOrder>()));

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
        h.Orders.GetPositionsAsync("Binance").Returns(Ok(new List<Position> { Pos() }));
        h.Orders.GetOpenOrdersAsync("Binance").Returns(Ok(new List<OpenOrder>()));

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
        h.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position>()));
        h.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));

        h.Bus.Publish(new ConnectionStatusEvent("Kraken", ConnectionState.Connected, "up"));
        await Task.Delay(50);

        Assert.Empty(Announcements(h.Bus));
    }

    // ── While you were away (2026-07-22) ─────────────────────────────────────

    private static async Task ConnectAsync(Harness h, string provider)
    {
        h.Bus.Publish(new ConnectionStatusEvent(provider, ConnectionState.Connected, "up"));
        await SettleAsync(() => Announcements(h.Bus).Count > 0 || true);
        await Task.Delay(80); // let the fire-and-forget reconcile finish
    }

    [Fact]
    public async Task Position_closed_while_away_is_reported_with_realized_pnl()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("att-away-").FullName;

        // Session 1: long 0.5 BTC on Kraken — snapshot persisted on reconcile.
        var s1 = Build(paperMode: false, dataDir: dir);
        s1.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s1.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position>
        {
            new("BTC/USD", 0.5, 90000, 45000, 0.0),
        }));
        s1.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s1, "Kraken");
        s1.Coordinator.Dispose();

        // Session 2: the stop fired overnight — flat account, closing fill on record.
        var s2 = Build(paperMode: false, dataDir: dir);
        s2.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s2.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position>()));
        s2.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        s2.Orders.GetFillsAsync("Kraken", "BTC/USD", Arg.Any<int>()).Returns(Ok(new List<TradeFill>
        {
            new("f1", "BTC/USD", OrderSide.Sell, 0.5, 92300, DateTime.UtcNow, OrderId: "o9", RealizedPnL: 1150),
        }));
        await ConnectAsync(s2, "Kraken");

        var away = Announcements(s2.Bus).Find(a => a.Message?.Contains("While you were away") == true);
        Assert.NotNull(away);
        Assert.Contains("BTC/USD position closed", away!.Message);
        Assert.Contains("Sold at", away.Message);
        Assert.Contains("Profit", away.Message);
        s2.Coordinator.Dispose();
    }

    [Fact]
    public async Task Pnl_is_approximated_from_entry_when_the_broker_reports_none()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("att-away-").FullName;

        var s1 = Build(paperMode: false, dataDir: dir);
        s1.Orders.SupportsTradingAsync("Tradier").Returns(true);
        s1.Orders.GetPositionsAsync("Tradier").Returns(Ok(new List<Position>
        {
            new("AAPL", 10, 200, 2000, 0.0), // long 10 @ 200
        }));
        s1.Orders.GetOpenOrdersAsync("Tradier").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s1, "Tradier");
        s1.Coordinator.Dispose();

        var s2 = Build(paperMode: false, dataDir: dir);
        s2.Orders.SupportsTradingAsync("Tradier").Returns(true);
        s2.Orders.GetPositionsAsync("Tradier").Returns(Ok(new List<Position>()));
        s2.Orders.GetOpenOrdersAsync("Tradier").Returns(Ok(new List<OpenOrder>()));
        s2.Orders.GetFillsAsync("Tradier", "AAPL", Arg.Any<int>()).Returns(Ok(new List<TradeFill>
        {
            new("f1", "AAPL", OrderSide.Sell, 10, 190, DateTime.UtcNow), // RealizedPnL 0 → approximate
        }));
        await ConnectAsync(s2, "Tradier");

        var away = Announcements(s2.Bus).Find(a => a.Message?.Contains("While you were away") == true);
        Assert.NotNull(away);
        Assert.Contains("Loss", away!.Message); // (190 − 200) × 10 = −100
        s2.Coordinator.Dispose();
    }

    [Fact]
    public async Task Still_open_positions_produce_no_away_report()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("att-away-").FullName;
        var positions = new List<Position> { new("BTC/USD", 0.5, 90000, 45000, 0.0) };

        var s1 = Build(paperMode: false, dataDir: dir);
        s1.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s1.Orders.GetPositionsAsync("Kraken").Returns(Ok(positions));
        s1.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s1, "Kraken");
        s1.Coordinator.Dispose();

        var s2 = Build(paperMode: false, dataDir: dir);
        s2.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s2.Orders.GetPositionsAsync("Kraken").Returns(Ok(positions)); // unchanged
        s2.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s2, "Kraken");

        Assert.DoesNotContain(Announcements(s2.Bus), a => a.Message?.Contains("While you were away") == true);
        s2.Coordinator.Dispose();
    }

    // ── Margin / liquidation-proximity warnings ──────────────────────────────

    private static List<MarginWarningEvent> MarginWarnings(SpyEventBus bus) =>
        bus.Log.OfType<MarginWarningEvent>().ToList();

    [Fact]
    public async Task Position_near_liquidation_raises_a_margin_warning()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Binance").Returns(true);
        // Long 1 BTC, mark 100 (MarketValue/Quantity), liquidation at 92 → 8% away (< 15%).
        h.Orders.GetPositionsAsync("Binance").Returns(Ok(new List<Position>
        {
            new("BTC/USD", 1.0, 100.0, 100.0, 0.0, Leverage: 10.0, LiquidationPrice: 92.0),
        }));
        h.Orders.GetOpenOrdersAsync("Binance").Returns(Ok(new List<OpenOrder>()));

        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));
        await SettleAsync(() => MarginWarnings(h.Bus).Count > 0);

        var warn = Assert.Single(MarginWarnings(h.Bus));
        Assert.Equal("BTC/USD", warn.Symbol);
        Assert.Contains("Margin warning", warn.Message);
        Assert.Contains("liquidation price", warn.Message);
        h.Coordinator.Dispose();
    }

    [Fact]
    public async Task Position_comfortably_clear_of_liquidation_stays_silent()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Binance").Returns(true);
        // Mark 100, liquidation at 50 → 50% away, well outside the 15% band.
        h.Orders.GetPositionsAsync("Binance").Returns(Ok(new List<Position>
        {
            new("BTC/USD", 1.0, 100.0, 100.0, 0.0, Leverage: 2.0, LiquidationPrice: 50.0),
        }));
        h.Orders.GetOpenOrdersAsync("Binance").Returns(Ok(new List<OpenOrder>()));

        h.Bus.Publish(new ConnectionStatusEvent("Binance", ConnectionState.Connected, "up"));
        await SettleAsync(() => Announcements(h.Bus).Count > 0);

        Assert.Empty(MarginWarnings(h.Bus));
        h.Coordinator.Dispose();
    }

    [Fact]
    public async Task Spot_position_without_a_liquidation_price_never_warns()
    {
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Coinbase").Returns(true);
        // Spot holding: LiquidationPrice defaults to 0 → not a margin position.
        h.Orders.GetPositionsAsync("Coinbase").Returns(Ok(new List<Position>
        {
            new("BTC/USD", 1.0, 100.0, 100.0, 0.0),
        }));
        h.Orders.GetOpenOrdersAsync("Coinbase").Returns(Ok(new List<OpenOrder>()));

        h.Bus.Publish(new ConnectionStatusEvent("Coinbase", ConnectionState.Connected, "up"));
        await SettleAsync(() => Announcements(h.Bus).Count > 0);

        Assert.Empty(MarginWarnings(h.Bus));
        h.Coordinator.Dispose();
    }

    [Fact]
    public async Task Reduced_position_is_reported_as_reduced()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("att-away-").FullName;

        var s1 = Build(paperMode: false, dataDir: dir);
        s1.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s1.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position> { new("BTC/USD", 1.0, 90000, 90000, 0.0) }));
        s1.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s1, "Kraken");
        s1.Coordinator.Dispose();

        var s2 = Build(paperMode: false, dataDir: dir);
        s2.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s2.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position> { new("BTC/USD", 0.4, 90000, 36000, 0.0) }));
        s2.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        s2.Orders.GetFillsAsync("Kraken", "BTC/USD", Arg.Any<int>()).Returns(Ok(new List<TradeFill>()));
        await ConnectAsync(s2, "Kraken");

        var away = Announcements(s2.Bus).Find(a => a.Message?.Contains("While you were away") == true);
        Assert.NotNull(away);
        Assert.Contains("reduced to 0.4", away!.Message);
        s2.Coordinator.Dispose();
    }

    // ── A failed read is not a flat account ──────────────────────────────────
    //
    // The reason ProviderResult exists. Reconciliation received an empty list for
    // a FAILED positions fetch and could not tell it from a flat account: it
    // announced every position as closed while you were away, then overwrote the
    // snapshot with the empty result — so one network hiccup reported the account
    // flat AND destroyed the record that would have corrected it next time.

    [Fact]
    public async Task A_failed_positions_read_is_never_reported_as_positions_closing()
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("att-failread-").FullName;

        // Session 1: a real position, snapshotted.
        var s1 = Build(paperMode: false, dataDir: dir);
        s1.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s1.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position>
        {
            new("BTC/USD", 0.5, 90000, 45000, 0.0),
        }));
        s1.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s1, "Kraken");
        s1.Coordinator.Dispose();

        // Session 2: the venue is down. Not flat — unknown.
        var s2 = Build(paperMode: false, dataDir: dir);
        s2.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s2.Orders.GetPositionsAsync("Kraken")
            .Returns(ProviderResult<List<Position>>.Failed("Reading positions failed: connection reset"));
        s2.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        await ConnectAsync(s2, "Kraken");

        Assert.DoesNotContain(Announcements(s2.Bus), a => a.Message?.Contains("While you were away") == true);
        Assert.DoesNotContain(Announcements(s2.Bus), a => a.Message?.Contains("closed") == true);
        s2.Coordinator.Dispose();

        // …and the snapshot must be INTACT, so a later good read still sees the
        // position it had. If the failure had been persisted, session 3 would now
        // believe the account was always flat.
        var s3 = Build(paperMode: false, dataDir: dir);
        s3.Orders.SupportsTradingAsync("Kraken").Returns(true);
        s3.Orders.GetPositionsAsync("Kraken").Returns(Ok(new List<Position>()));
        s3.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));
        s3.Orders.GetFillsAsync("Kraken", "BTC/USD", Arg.Any<int>()).Returns(Ok(new List<TradeFill>()));
        await ConnectAsync(s3, "Kraken");

        Assert.Contains(Announcements(s3.Bus), a => a.Message?.Contains("While you were away") == true);
        s3.Coordinator.Dispose();
    }

    [Fact]
    public async Task A_permission_failure_is_spoken_because_the_user_can_fix_it()
    {
        // A key missing a scope, or unfinished verification, is actionable at the
        // venue — so it is said out loud, unlike a transient timeout.
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Kraken").Returns(true);
        h.Orders.GetPositionsAsync("Kraken").Returns(
            ProviderResult<List<Position>>.NotPermitted("check the API key's permissions on the venue"));
        h.Orders.GetOpenOrdersAsync("Kraken").Returns(Ok(new List<OpenOrder>()));

        await ConnectAsync(h, "Kraken");

        var msg = Assert.Single(Announcements(h.Bus), a => a.Message?.Contains("permissions") == true);
        Assert.Equal(FeedbackType.Error, msg.Type);
    }

    [Fact]
    public async Task A_spot_only_provider_reports_no_positions_without_announcing_anything()
    {
        // NotSupported is not an error and not a change — a spot venue simply has
        // no positions concept. Nothing to interrupt the user with.
        var h = Build(paperMode: false);
        h.Orders.SupportsTradingAsync("Coinbase").Returns(true);
        h.Orders.GetPositionsAsync("Coinbase").Returns(
            ProviderResult<List<Position>>.NotSupported("Coinbase is spot only"));
        h.Orders.GetOpenOrdersAsync("Coinbase").Returns(Ok(new List<OpenOrder>()));

        await ConnectAsync(h, "Coinbase");

        Assert.Empty(Announcements(h.Bus));
    }
}
