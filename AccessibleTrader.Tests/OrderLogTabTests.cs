using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Blazor;
using Bunit;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The trading dashboard's Order log tab (Cody, 2026-11-20: "add the order log to that new
    /// tab as a table in the trading dashboard"): the terminal's own record of every order it
    /// sent, refused or cancelled and every fill a venue reported, newest first.
    /// </summary>
    public class OrderLogTabTests
    {
        private static readonly DateTime Oct = new(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Nov = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

        private static TradeSignal Signal(string symbol, OrderSide side = OrderSide.Buy) =>
            new(symbol, side, 0.5, OrderType.Limit, 100.0);

        private static OrderAuditLog LogAt(IPlatformPathService paths, DateTime when) =>
            new(paths, null, () => when);

        // ── Reading the file back ────────────────────────────────────────────

        [Fact]
        public void Recent_reads_newest_first_across_months()
        {
            var paths = new TempWorkspacePaths();
            LogAt(paths, Oct).Placement("Kraken", Signal("OLD"), new OrderPlacement(OrderOutcome.Placed, "o1", "o1", null));
            LogAt(paths, Nov).Placement("Kraken", Signal("MID"), new OrderPlacement(OrderOutcome.Placed, "o2", "o2", null));
            LogAt(paths, Nov.AddMinutes(1)).Placement("Kraken", Signal("NEW"), new OrderPlacement(OrderOutcome.Placed, "o3", "o3", null));

            var recent = LogAt(paths, Nov).Recent();

            Assert.Equal(new[] { "NEW", "MID", "OLD" }, recent.Select(r => r.Symbol));
        }

        [Fact]
        public void Recent_stops_at_the_limit_and_keeps_the_newest()
        {
            var paths = new TempWorkspacePaths();
            for (int i = 0; i < 5; i++)
                LogAt(paths, Nov.AddMinutes(i)).Placement("Kraken", Signal("S" + i), new OrderPlacement(OrderOutcome.Placed, "o" + i, "o" + i, null));

            var recent = LogAt(paths, Nov).Recent(2);

            Assert.Equal(new[] { "S4", "S3" }, recent.Select(r => r.Symbol));
        }

        [Fact]
        public void A_line_that_does_not_parse_is_skipped_not_fatal()
        {
            var paths = new TempWorkspacePaths();
            var log = LogAt(paths, Nov);
            log.Placement("Kraken", Signal("GOOD"), new OrderPlacement(OrderOutcome.Placed, "o1", "o1", null));
            // The half-written tail an append still in flight would leave.
            File.AppendAllText(log.CurrentPath, "{\"utc\":\"2026-11-20T12:00:00Z\",\"ev");

            var recent = log.Recent();

            Assert.Equal("GOOD", Assert.Single(recent).Symbol);
        }

        [Fact]
        public void No_folder_yet_reads_as_no_orders()
        {
            Assert.Empty(LogAt(new TempWorkspacePaths(), Nov).Recent());
        }

        // ── Rows in words ────────────────────────────────────────────────────

        [Fact]
        public void A_stop_firing_reads_as_filled_by_stop_at_the_fill_price()
        {
            var paths = new TempWorkspacePaths();
            var log = LogAt(paths, Nov);
            log.Update("Paper", new OrderUpdate("o9", "BTCUSD", OrderSide.Sell, 0.25, 91000, 0,
                OrderStatus.Filled, StopTriggered: true, TakeProfitTriggered: false, Nov, RealizedPnL: -120));

            var row = OrderLogRow.From(Assert.Single(log.Recent()));

            Assert.Equal("Filled by stop", row.What);
            Assert.Equal("Paper", row.Exchange);
            Assert.Equal("Sell", row.Side);
            Assert.Equal(PriceFormatter.FormatQuantity(0.25), row.Quantity);
            Assert.Equal(PriceFormatter.FormatPrice(91000), row.Price);
            Assert.Contains("P&L " + PriceFormatter.FormatPnL(-120), row.Details);
            Assert.Contains("order o9", row.Details);
        }

        [Fact]
        public void A_refusal_reads_as_refused_with_the_reason_first()
        {
            var paths = new TempWorkspacePaths();
            var log = LogAt(paths, Nov);
            log.Placement("Kraken", Signal("ETHUSD"), new OrderPlacement(OrderOutcome.Rejected, "", null, "Insufficient funds"));

            var row = OrderLogRow.From(Assert.Single(log.Recent()));

            Assert.Equal("Refused", row.What);
            Assert.StartsWith("Insufficient funds", row.Details);
        }

        [Fact]
        public void The_csv_quotes_commas_and_defuses_a_formula()
        {
            var row = new OrderLogRow(Nov, "Refused", "BTCUSD", "Kraken", "Buy", "Limit", "1", "2",
                "=HYPERLINK(\"x\"), then more");

            string csv = OrderLogRow.ToCsv(new[] { row });

            Assert.Contains("\"'=HYPERLINK(\"\"x\"\"), then more\"", csv);
            Assert.StartsWith("Time (UTC),What happened,", csv);
        }

        // ── The tab ──────────────────────────────────────────────────────────

        private static BlazorTestHarness Harness(OrderAuditLog? audit, bool demo = false)
        {
            var h = new BlazorTestHarness();
            ModalCatalog.SeedChartState(h);
            h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
            h.Ctx.JSInterop.SetupVoid("accessibleTrader.focusElement", _ => true).SetVoidResult();
            if (audit != null) h.With(audit);
            if (demo) h.With(new DemoPolicy(isDemo: true));
            var keys = new List<ApiKeyConfig> { new("kraken", "main", "key", "secret", Environment: "Live", IsActive: true) };
            h.ApiKeyService.GetAllKeysAsync().Returns(keys);
            h.ApiKeyService.GetKeysForProviderAsync(default!).ReturnsForAnyArgs(keys);
            h.OrderService.SupportsTradingAsync(default!).ReturnsForAnyArgs(true);
            h.OrderService.GetCapabilitiesAsync(default!).ReturnsForAnyArgs(ProviderCapabilities.None);
            h.OrderService.SupportsOcoPairsAsync(default!).ReturnsForAnyArgs(false);
            h.OrderService.GetMaxLeverageAsync(default!).ReturnsForAnyArgs(1.0);
            h.OrderService.GetBalancesAsync(default!)
                .ReturnsForAnyArgs(ProviderResult<List<Balance>>.Ok(new List<Balance>()));
            h.OrderService.GetPositionsAsync(default!)
                .ReturnsForAnyArgs(ProviderResult<List<Position>>.Ok(new List<Position>()));
            h.OrderService.GetOpenOrdersAsync(default!, default)
                .ReturnsForAnyArgs(ProviderResult<List<OpenOrder>>.Ok(new List<OpenOrder>()));
            h.OrderService.GetFillsAsync(default!, default, default)
                .ReturnsForAnyArgs(ProviderResult<List<TradeFill>>.Ok(new List<TradeFill>()));
            return h;
        }

        private static IRenderedComponent<TradingDashboardModal> OpenLogTab(BlazorTestHarness h)
        {
            var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
            cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#order-qty")), TimeSpan.FromSeconds(10));
            cut.Find("button#acct-tab-log").Click();
            return cut;
        }

        private static List<string> BodyRows(IRenderedComponent<TradingDashboardModal> cut) =>
            cut.FindAll(".order-log-scroll tbody tr").Select(tr => tr.TextContent).ToList();

        [Fact]
        public void The_tab_lists_the_terminals_own_record_newest_first()
        {
            var paths = new TempWorkspacePaths();
            LogAt(paths, Nov).Placement("Kraken", Signal("ADAUSD"), new OrderPlacement(OrderOutcome.Placed, "o1", "o1", null));
            LogAt(paths, Nov.AddMinutes(1)).Placement("Kraken", Signal("SOLUSD", OrderSide.Sell), new OrderPlacement(OrderOutcome.Rejected, "", null, "Price too far"));
            using var h = Harness(LogAt(paths, Nov));

            var cut = OpenLogTab(h);

            Assert.Equal("true", cut.Find("button#acct-tab-log").GetAttribute("aria-selected"));
            var rows = BodyRows(cut);
            Assert.Equal(2, rows.Count);
            Assert.Contains("SOLUSD", rows[0]);
            Assert.Contains("Refused", rows[0]);
            Assert.Contains("Price too far", rows[0]);
            Assert.Contains("ADAUSD", rows[1]);
            Assert.Contains("Sent", rows[1]);
            Assert.Equal("Order log, newest first", cut.Find(".order-log-scroll caption").TextContent);
        }

        [Fact]
        public void The_filter_narrows_by_symbol_and_says_when_nothing_matches()
        {
            var paths = new TempWorkspacePaths();
            LogAt(paths, Nov).Placement("Kraken", Signal("ADAUSD"), new OrderPlacement(OrderOutcome.Placed, "o1", "o1", null));
            LogAt(paths, Nov).Placement("Kraken", Signal("SOLUSD"), new OrderPlacement(OrderOutcome.Placed, "o2", "o2", null));
            using var h = Harness(LogAt(paths, Nov));
            var cut = OpenLogTab(h);

            cut.Find("#order-log-filter").Input("sol");
            Assert.Contains("SOLUSD", Assert.Single(BodyRows(cut)));

            cut.Find("#order-log-filter").Input("xrp");
            Assert.Equal("No orders match the filter.", Assert.Single(BodyRows(cut)).Trim());
        }

        [Fact]
        public void An_empty_log_says_so()
        {
            using var h = Harness(LogAt(new TempWorkspacePaths(), Nov));
            var cut = OpenLogTab(h);

            Assert.Equal("No orders recorded yet.", Assert.Single(BodyRows(cut)).Trim());
            Assert.True(cut.Find(".order-log-tools button").HasAttribute("disabled"));
        }

        [Fact]
        public void The_public_demo_does_not_show_the_shared_file()
        {
            var paths = new TempWorkspacePaths();
            LogAt(paths, Nov).Placement("Kraken", Signal("SOMEONE_ELSES"), new OrderPlacement(OrderOutcome.Placed, "o1", "o1", null));
            using var h = Harness(LogAt(paths, Nov), demo: true);

            var cut = h.OpenModal<TradingDashboardModal>(b => b.Publish(new OpenTradingDashboardEvent()));
            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("button#acct-tab-log")), TimeSpan.FromSeconds(10));
            cut.Find("button#acct-tab-log").Click();

            Assert.DoesNotContain("SOMEONE_ELSES", cut.Markup);
            Assert.Contains("public demo", cut.Find("#acct-panel").TextContent);
        }
    }
}
