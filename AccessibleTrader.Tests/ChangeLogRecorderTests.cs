using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.ChangeLog;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

public class ChangeLogRecorderTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

    private sealed class TempPaths : IPlatformPathService
    {
        public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }

    private sealed class ThrowingStore : ChangeLogStore
    {
        public ThrowingStore(string dir)
            : base(new TempPaths(dir), NullLogger<ChangeLogStore>.Instance) { }

        public override Task AppendAsync(ChangeLogEntry entry)
            => throw new InvalidOperationException("disk on fire");
    }

    private static ChangeLogStore NewStore(string dir)
        => new(new TempPaths(dir), NullLogger<ChangeLogStore>.Instance);

    private static async Task<ChangeLogDocument> WaitForAsync(
        ChangeLogStore store, Func<ChangeLogDocument, bool> done)
    {
        for (int i = 0; i < 400; i++)
        {
            var doc = await store.SnapshotAsync();
            if (done(doc)) return doc;
            await Task.Delay(10);
        }
        var last = await store.SnapshotAsync();
        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for the change log; it held {last.Entries.Count} entries: "
            + string.Join(", ", last.Entries.Select(e => e.Key)));
    }

    private static AlertFired Alert(string id = "alert-1", string text = "BTC/USDT crossed above 95,000.")
        => new(
            new AlertDefinition
            {
                Id = id,
                Name = "BTC 95k",
                Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove,
                Delivery = AlertDelivery.Speech,
                Symbol = "BTC/USDT",
            },
            TriggeringValue: 95_100,
            PreviousValue: 94_900,
            SpeechText: text,
            Symbol: "BTC/USDT");

    private static OrderUpdate Order(
        OrderStatus status = OrderStatus.Filled,
        double filled = 0.1,
        bool stop = false,
        bool takeProfit = false,
        string id = "order-1",
        string symbol = "BTC/USDT")
        => new(id, symbol, OrderSide.Buy, filled, 94_210, 0, status, stop, takeProfit, Now);

    [Fact]
    public async Task AnAlertMapsToAnAlertEntryWithItsSpokenSentence()
    {
        var store = NewStore(TestTemp.NewDir("att-changelog-alert-"));
        var bus = new SpyEventBus();
        using var recorder = new ChangeLogRecorder(bus, store);

        bus.Publish(new AlertFiredEvent(Alert()));

        var doc = await WaitForAsync(store, d => d.Entries.Count == 1);
        var entry = Assert.Single(doc.Entries);
        Assert.Equal(ChangeLogCategory.Alert, entry.Category);
        Assert.StartsWith("alert|alert-1|BTC/USDT|", entry.Key);
        Assert.Equal("BTC/USDT crossed above 95,000.", entry.Text);
        Assert.Equal("BTC/USDT", entry.Symbol);
    }

    [Fact]
    public async Task EveryMoneyEventMapsToMoneyWithTheWordingTheTerminalAlreadySpeaks()
    {
        var bus = new SpyEventBus();
        var store = NewStore(TestTemp.NewDir("att-changelog-money-"));
        using var recorder = new ChangeLogRecorder(bus, store);

        bus.Publish(new OrderFilledEvent(Order(), "MEXC"));
        bus.Publish(new OrderPartialFillEvent(Order(OrderStatus.PartialFill, 0.05, id: "order-2"), "MEXC"));
        bus.Publish(new StopHitEvent(Order(stop: true, id: "order-3"), "MEXC"));
        bus.Publish(new TakeProfitHitEvent(Order(takeProfit: true, id: "order-4"), "MEXC"));

        var doc = await WaitForAsync(store, d => d.Entries.Count == 4);
        Assert.All(doc.Entries, e => Assert.Equal(ChangeLogCategory.Money, e.Category));
        Assert.All(doc.Entries, e => Assert.StartsWith("order|MEXC|", e.Key));

        var texts = doc.Entries.Select(e => e.Text).ToList();
        Assert.Contains(texts, t => t.StartsWith("Order filled. Bought 0.1 BTC/USDT at"));
        Assert.Contains(texts, t => t.StartsWith("Partial fill. Bought 0.05 BTC/USDT at"));
        Assert.Contains(texts, t => t.StartsWith("Stop loss hit. Bought 0.1 BTC/USDT at"));
        Assert.Contains(texts, t => t.StartsWith("Take profit hit. Bought 0.1 BTC/USDT at"));
    }

    [Fact]
    public async Task ACountdownIsNotRecordedButAReadOutIs()
    {
        var bus = new SpyEventBus();
        var store = NewStore(TestTemp.NewDir("att-changelog-event-"));
        using var recorder = new ChangeLogRecorder(bus, store);

        bus.Publish(new MarketEventNoticeEvent(
            "CPI in 15 minutes.", MarketEventImportance.High, "cpi-1",
            MarketEventNoticeKind.Countdown));
        bus.Publish(new MarketEventNoticeEvent(
            "CPI out. BTC up 0.4%.", MarketEventImportance.High, "cpi-1",
            MarketEventNoticeKind.ReadOut));

        var doc = await WaitForAsync(store, d => d.Entries.Count == 1);
        var entry = Assert.Single(doc.Entries);
        Assert.Equal(ChangeLogCategory.Event, entry.Category);
        Assert.Equal("event|cpi-1|ReadOut", entry.Key);
        Assert.Equal("CPI out. BTC up 0.4%.", entry.Text);
    }

    [Fact]
    public async Task AFirstConnectIsNotRecordedButAReconnectAfterAnErrorIs()
    {
        var bus = new SpyEventBus();
        var store = NewStore(TestTemp.NewDir("att-changelog-conn-"));
        using var recorder = new ChangeLogRecorder(bus, store);

        bus.Publish(new ConnectionStatusEvent("MEXC", ConnectionState.Connected, "MEXC connected."));
        bus.Publish(new ConnectionStatusEvent("MEXC", ConnectionState.Connected, "MEXC connected again."));
        var afterFirst = await store.SnapshotAsync();
        Assert.Empty(afterFirst.Entries);

        bus.Publish(new ConnectionStatusEvent("MEXC", ConnectionState.Error, "MEXC network issue. Circuit tripped."));
        bus.Publish(new ConnectionStatusEvent("MEXC", ConnectionState.Connected, "MEXC connection restored."));

        var doc = await WaitForAsync(store, d => d.Entries.Count == 2);
        Assert.Equal(new[] { ConnectionState.Error, ConnectionState.Connected },
            doc.Entries.Select(e => e.Key.Contains("|Error|") ? ConnectionState.Error : ConnectionState.Connected));
        Assert.All(doc.Entries, e => Assert.Equal(ChangeLogCategory.Connection, e.Category));
        Assert.Contains("network issue", doc.Entries[0].Text);
        Assert.Contains("connection restored", doc.Entries[1].Text);
    }

    [Fact]
    public async Task TheSameFillSeenByTwoBusesIsOneEntry()
    {
        var dir = TestTemp.NewDir("att-changelog-buses-");
        var store = NewStore(dir);
        var busA = new SpyEventBus();
        var busB = new SpyEventBus();
        using var recorderA = new ChangeLogRecorder(busA, store);
        using var recorderB = new ChangeLogRecorder(busB, store);

        var fill = new OrderFilledEvent(Order(filled: 0.25, id: "same-order"), "MEXC");
        busA.Publish(fill);
        busB.Publish(fill);

        var doc = await WaitForAsync(store, d => d.Entries.Count == 1);
        Assert.Single(doc.Entries);
        Assert.Contains("same-order", doc.Entries[0].Key);
    }

    [Fact]
    public async Task AStoreThatThrowsDoesNotThrowOutOfTheSubscriber()
    {
        var bus = new SpyEventBus();
        var store = new ThrowingStore(TestTemp.NewDir("att-changelog-throw-"));
        using var recorder = new ChangeLogRecorder(bus, store);

        var exception = Record.Exception(() => bus.Publish(new OrderFilledEvent(Order(), "MEXC")));

        Assert.Null(exception);
        await Task.Delay(100);
        // Nothing was written, and the recorder is still alive to try the next one.
        Assert.Empty((await store.SnapshotAsync()).Entries);
    }
}
