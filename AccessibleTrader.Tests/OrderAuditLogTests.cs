using System.Reactive.Linq;
using System.Reactive.Subjects;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Every order the terminal sends, refuses or cancels, and every fill or cancel a venue reports
    /// back, is one JSON line in OrderAudit/orders-yyyy-MM.jsonl (code review, 2026-11-20).
    /// </summary>
    public class OrderAuditLogTests
    {
        private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private sealed record Rig(GeneralOrderService Svc, ITradingProvider Live, Subject<OrderUpdate> PaperStream, OrderAuditLog Audit);

        private static Rig Build()
        {
            var data = Substitute.For<IDataService>();
            var tp = Substitute.For<IMarketDataProvider, ITradingProvider>();
            var live = (ITradingProvider)tp;
            live.IsConnected.Returns(true);
            live.OrderUpdateStream.Returns(Observable.Empty<OrderUpdate>());
            live.SupportsOrderEventStreaming.Returns(true);
            live.HasPracticeEnvironment.Returns(true);
            live.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(true);
            data.GetProviderAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<IMarketDataProvider?>(tp));

            var paperStream = new Subject<OrderUpdate>();
            var paper = Substitute.For<IPaperTradingProvider>();
            paper.IsConnected.Returns(true);
            paper.HasPracticeEnvironment.Returns(true);
            paper.AttachesProtectiveLegs(Arg.Any<TradeSignal>()).Returns(true);
            paper.OrderUpdateStream.Returns(paperStream);

            var audit = new OrderAuditLog(new TempWorkspacePaths(), null, () => Now);
            var svc = new GeneralOrderService(
                data, Substitute.For<IGlobalErrorCoordinator>(), NullLogger<GeneralOrderService>.Instance,
                new EventBus(), paper, Substitute.For<ISettingsManager>(), new DemoPolicy(isDemo: false),
                new QuickTradeEquity(), audit);
            return new Rig(svc, live, paperStream, audit);
        }

        private static List<JObject> Lines(OrderAuditLog audit) =>
            File.Exists(audit.CurrentPath)
                ? File.ReadAllLines(audit.CurrentPath).Select(JObject.Parse).ToList()
                : new List<JObject>();

        [Fact]
        public async Task A_sent_order_and_its_fill_are_each_one_line()
        {
            var rig = Build();
            rig.Live.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns("EX-1");

            await rig.Svc.PlaceOrderAsync("Kraken",
                new TradeSignal("BTC/USD", OrderSide.Buy, 0.5, OrderType.Limit, Price: 60000, StopLoss: 58000));
            rig.PaperStream.OnNext(new OrderUpdate("P-9", "BTC/USDT", OrderSide.Sell, 1, 61000, 0,
                OrderStatus.Filled, StopTriggered: true, TakeProfitTriggered: false, Now, RealizedPnL: -120));

            Assert.EndsWith(Path.Combine("OrderAudit", "orders-2026-11.jsonl"), rig.Audit.CurrentPath);
            var lines = Lines(rig.Audit);
            Assert.Equal(2, lines.Count);

            var sent = lines[0];
            Assert.Equal("sent", (string?)sent["event"]);
            Assert.Equal("Kraken", (string?)sent["provider"]);
            Assert.Equal("BTC/USD", (string?)sent["symbol"]);
            Assert.Equal("Buy", (string?)sent["side"]);
            Assert.Equal("Limit", (string?)sent["type"]);
            Assert.Equal(0.5, (double)sent["quantity"]!);
            Assert.Equal(60000, (double)sent["price"]!);
            Assert.Equal(58000, (double)sent["stopLoss"]!);
            Assert.Equal("EX-1", (string?)sent["orderId"]);
            Assert.Equal(Now, (DateTime)sent["utc"]!);

            var fill = lines[1];
            Assert.Equal("update", (string?)fill["event"]);
            Assert.Equal("Paper", (string?)fill["provider"]);
            Assert.Equal("Filled", (string?)fill["outcome"]);
            Assert.Equal("stop", (string?)fill["leg"]);
            Assert.Equal(61000, (double)fill["filledPrice"]!);
            Assert.Equal(-120, (double)fill["realizedPnL"]!);
        }

        [Fact]
        public async Task A_refused_order_is_recorded_with_the_reason_the_user_heard()
        {
            var rig = Build();

            var placement = await rig.Svc.PlaceOrderAsync("Kraken", new TradeSignal("BTC/USD", OrderSide.Buy, 0));

            Assert.False(placement.Succeeded);
            var line = Assert.Single(Lines(rig.Audit));
            Assert.Equal("refused", (string?)line["event"]);
            Assert.Equal(placement.FailureMessage, (string?)line["reason"]);
            await rig.Live.DidNotReceiveWithAnyArgs().PlaceOrderAsync(default!);
        }

        [Fact]
        public async Task A_cancel_is_recorded_whether_or_not_the_venue_took_it()
        {
            var rig = Build();
            rig.Live.CancelOrderAsync("A1", "BTC/USD").Returns(true);
            rig.Live.CancelOrderAsync("A2", "BTC/USD").Returns(false);

            await rig.Svc.CancelOrderAsync("Kraken", "A1", "BTC/USD");
            await rig.Svc.CancelOrderAsync("Kraken", "A2", "BTC/USD");

            var lines = Lines(rig.Audit);
            Assert.Equal(new[] { "cancel-sent", "cancel-failed" }, lines.Select(l => (string?)l["event"]));
            Assert.Equal(new[] { "A1", "A2" }, lines.Select(l => (string?)l["orderId"]));
        }

        [Fact]
        public async Task A_log_that_cannot_be_written_never_stops_the_order()
        {
            var rig = Build();
            rig.Live.PlaceOrderAsync(Arg.Any<TradeSignal>()).Returns("EX-2");
            string folder = Path.GetDirectoryName(rig.Audit.CurrentPath)!;
            File.WriteAllText(folder, "a file where the folder would go");

            var placement = await rig.Svc.PlaceOrderAsync("Kraken", new TradeSignal("BTC/USD", OrderSide.Buy, 1));

            Assert.True(placement.Succeeded);
            Assert.Equal("EX-2", placement.OrderId);
        }

        [Fact]
        public void A_full_month_stops_growing()
        {
            var audit = new OrderAuditLog(new TempWorkspacePaths(), null, () => Now);
            Directory.CreateDirectory(Path.GetDirectoryName(audit.CurrentPath)!);
            using (var f = File.Create(audit.CurrentPath)) f.SetLength(OrderAuditLog.MaxFileBytes);

            audit.Cancel("Kraken", "A1", "BTC/USD", ok: true);

            Assert.Equal(OrderAuditLog.MaxFileBytes, new FileInfo(audit.CurrentPath).Length);
        }

        [Fact]
        public void Two_logs_on_one_folder_lose_no_line()
        {
            // The hosted demo: one log per visitor's circuit, one shared folder.
            var paths = new TempWorkspacePaths();
            var logs = Enumerable.Range(0, 8).Select(_ => new OrderAuditLog(paths, null, () => Now)).ToArray();

            Parallel.For(0, 800, i => logs[i % logs.Length].Cancel("Paper", $"P-{i}", "BTC/USDT", ok: true));

            Assert.Equal(800, File.ReadAllLines(logs[0].CurrentPath).Length);
        }

        [Fact]
        public void Both_heads_register_the_log_beside_the_order_service()
        {
            string root = RepoPaths.RepoRoot();
            foreach (var rel in new[] { "AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs",
                                        "AccessibleTrader.WebHost/ServiceCollectionExtensions.cs" })
            {
                string text = File.ReadAllText(Path.Combine(root, rel));
                Assert.Matches(@"Add(Singleton|Scoped)<AccessibleTrader\.Core\.Services\.Trading\.OrderAuditLog>\(\)", text);
            }
        }
    }
}
