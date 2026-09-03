using System.Reactive.Linq;
using System.Text.RegularExpressions;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Trading;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The path a real order takes from the button to the wire, guarded end to end.
    ///
    /// <para>
    /// Each test here corresponds to a way that path could cost the user money that they did
    /// not choose to spend: a Close that opens a position, a bracket that closes the trade it
    /// was meant to protect, a button that places two orders because Enter was pressed twice.
    /// They are grouped in one file deliberately — this is the tier where a regression is
    /// measured in currency rather than in inconvenience, and it should be readable in one sitting.
    /// </para>
    /// </summary>
    public sealed class MoneyPathSafetyTests : IDisposable
    {
        private readonly string _dir = TestTemp.NewDir("att-moneypath-");
        public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* temp */ } }

        // ── Close position must be reduce-only ───────────────────────────────

        // Shared with DashboardRefusalScanTests — see DashboardSourceReader for why
        // the brace matcher lives in one place rather than two.
        private static string DashboardMethod(string signature) => DashboardSourceReader.Method(signature);

        /// <summary>
        /// A button labelled "Close" must never be able to OPEN a position.
        ///
        /// <para>
        /// <c>p</c> is a snapshot from a timer-refreshed list. Hold 1.0 BTC with a stop, let the
        /// stop take it to 0.4, press Close on the stale row: a plain sell of 1.0 closes 0.4 and
        /// opens a 0.6 SHORT on collateral, while the user hears "Closing BTC/USDT. Sell 1.0."
        /// </para>
        ///
        /// <para>
        /// And it must be unconditional — NOT gated on the ReduceOnly capability flag the way the
        /// ticket's checkbox is. A venue that ignores the flag behaves exactly as before; a venue
        /// that honours it clips the overshoot, which is the whole point.
        /// </para>
        /// </summary>
        [Fact]
        public void ClosePosition_SendsReduceOnly_Unconditionally()
        {
            // Both close paths build their order in ONE method, so there is one line to
            // guard rather than two that could drift. That matters more since the limit
            // close shipped: a resting exit can sit on the book for days while a stop
            // takes the position out from under it, and without the flag the survivor
            // opens a fresh position the other way at a price picked for a different trade.
            string body = DashboardMethod("private async Task CloseAsync(AccountPosition row, double? limitPrice)");

            Assert.Contains("ReduceOnly: true", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Can(ProviderCapabilities.ReduceOnly)", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// Neither close button's order type may depend on hidden state. `Close position`
        /// is market and always market; `Close at limit` is a limit and always a limit.
        ///
        /// <para>
        /// One control that switched behaviour on whether a price field elsewhere happened
        /// to be filled would do two different things to two presses that felt identical —
        /// and on a screen where the deciding state cannot be glanced at, that is the whole
        /// class of defect this dialog was rebuilt to remove.
        /// </para>
        /// </summary>
        [Fact]
        public void EachCloseButtonsOrderTypeIsFixedByWhichButtonItIs()
        {
            // Member, not Method: both are expression-bodied, and brace matching alone
            // would silently read whatever is declared after them.
            Assert.Contains("CloseAsync(row, limitPrice: null)",
                DashboardSourceReader.Member("private Task ClosePosition(AccountPosition row)"),
                StringComparison.Ordinal);
            Assert.Contains("CloseAsync(row, limitPrice)",
                DashboardSourceReader.Member("private Task ClosePositionAtLimit(AccountPosition row, double limitPrice)"),
                StringComparison.Ordinal);

            // And the shared builder derives the type from the argument alone — not from
            // _closeLimitText, not from a nullable field read at the last moment.
            string body = DashboardMethod("private async Task CloseAsync(AccountPosition row, double? limitPrice)");
            Assert.Contains("limitPrice is null ? OrderType.Market : OrderType.Limit", body, StringComparison.Ordinal);
            Assert.DoesNotContain("_closeLimitText", body, StringComparison.Ordinal);
        }

        /// <summary>
        /// The complement, and the reason the test above is not vacuous: the TICKET's reduce-only
        /// checkbox stays capability-gated. A field sent to a provider that ignores it is the
        /// silent half of the same defect as a control drawn over nothing.
        /// </summary>
        [Fact]
        public void TheTicketsReduceOnlyStaysCapabilityGated()
        {
            // BuildSignal, not SubmitOrder. The ticket's signal construction moved into its
            // own method when the live review needed to build the SAME order twice — once to
            // read aloud, once to send — and this guard went red on the move, which is the
            // guard working: it is pointed at the line that decides what reaches the venue,
            // and that line changed method. Re-point it, never widen it to the whole file.
            string body = DashboardMethod("private TradeSignal BuildSignal()");
            Assert.Contains("ReduceOnly:     Can(ProviderCapabilities.ReduceOnly) && _reduceOnly", body,
                StringComparison.Ordinal);
        }

        // ── The in-flight latch ──────────────────────────────────────────────

        public static TheoryData<string> OrderPlacingHandlers() => new()
        {
            "private async Task SubmitOrder()",
            "private async Task CloseAsync(AccountPosition row, double? limitPrice)",
            "private async Task CancelOrder(AccountOrder row)",
            "private async Task SubmitOcoPair()",
        };

        /// <summary>
        /// Every handler that places or cancels an order claims the in-flight latch and releases
        /// it in a <c>finally</c>.
        ///
        /// <para>
        /// The submit button was only <c>disabled="@(!CanSubmit)"</c>, and <c>CanSubmit</c> checked
        /// quantity, symbol and provider — none of which change across the await. So the button
        /// stayed enabled for the whole round trip and a second Enter re-entered the handler.
        /// Pressing Enter twice on a button is not an exotic slip for a screen-reader user.
        /// </para>
        /// </summary>
        [Theory]
        [MemberData(nameof(OrderPlacingHandlers))]
        public void EveryOrderPlacingHandlerTakesTheInFlightLatch(string signature)
        {
            string body = DashboardMethod(signature);

            Assert.True(body.Contains("BeginOrderAction(", StringComparison.Ordinal),
                $"`{signature}` does not claim the in-flight latch, so a second activation re-enters it "
                + "while the first is still awaiting the broker.");
            Assert.True(body.Contains("EndOrderAction()", StringComparison.Ordinal),
                $"`{signature}` claims the latch but never releases it — the controls would stay dead "
                + "for the rest of the session.");
            Assert.True(Regex.IsMatch(body, @"finally\s*\{[^}]*EndOrderAction\(\)"),
                $"`{signature}` releases the latch outside a finally, so a throw leaves it stuck on. "
                + "A permanently disabled Submit button is a different bug, not a fix.");
        }

        /// <summary>
        /// And the latch reaches the CONTROLS rather than only refusing inside the handler: a
        /// button that looks live and does nothing is indistinguishable from a broken one.
        ///
        /// <para>
        /// Re-pointed 2026-09-02. This used to count <c>disabled="@…_orderInFlight"</c>
        /// bindings, and it counted four. Every one of those has become a
        /// <c>GatedButton Gate="…"</c>, because `disabled` did not make those buttons look
        /// inert to a blind trader — it made them ABSENT, out of the tab order with nothing
        /// said. The property being guarded is unchanged and the guard is now stronger: the
        /// latch has to be inside the gate EXPRESSION, which is what both draws the state
        /// and refuses the click, so a gate that stopped reading it would let a second
        /// Enter through as well as looking live.
        /// </para>
        /// </summary>
        [Fact]
        public void TheLatchAlsoRefusesAtTheControls()
        {
            string src = DashboardSourceReader.Source();

            // Each of the three gates that a money control hangs off must read the latch.
            foreach (var gate in new[] { "private string? SubmitGate()",
                                         "private string? OcoGate()",
                                         "private string? InFlightGate()" })
            {
                // Member(), not Method(): InFlightGate is expression-bodied, and brace
                // matching on an `=>` member silently reads whatever is declared NEXT.
                string body = DashboardSourceReader.Member(gate);
                Assert.True(body.Contains("_orderInFlight", StringComparison.Ordinal),
                    $"`{gate}` no longer reads the in-flight latch, so the control it draws stays "
                    + "live across the await and a second Enter reaches the handler.");
            }

            // And they are actually WIRED to controls — a gate nothing calls guards nothing.
            // Four money controls, matching the four this guard was written against:
            // live-confirm and plain Submit (SubmitGate), the OCO pair (OcoGate), and the
            // per-row close/cancel actions (InFlightGate).
            int wired = Regex.Matches(src, @"Gate=""(SubmitGate|OcoGate|InFlightGate)""").Count;
            Assert.True(wired >= 4,
                $"Only {wired} controls hang off a latch-reading gate; 4 did when this guard was "
                + "written (live-confirm, plain submit, OCO, and the per-row close/cancel).");

            // CanSubmit is defined AS the gate being open, so the latch cannot be in one and
            // not the other. Before 2026-09-02 they were separate expressions and CanSubmit
            // checked quantity, symbol and provider — none of which change across the await.
            int at = src.IndexOf("private bool CanSubmit =>", StringComparison.Ordinal);
            Assert.True(at >= 0, "CanSubmit is gone — re-point this guard.");
            int end = src.IndexOf(';', at);
            Assert.Contains("SubmitGate() is null", src[at..end], StringComparison.Ordinal);
        }

        /// <summary>
        /// A refusal must SPEAK. A handler that returns silently because the latch was held leaves
        /// the user with a button that did nothing and no way to tell why.
        /// </summary>
        [Fact]
        public void TheLatchRefusalSpeaks()
        {
            string body = DashboardMethod("private bool BeginOrderAction(string what)");
            Assert.Contains("FeedbackRequestEvent", body, StringComparison.Ordinal);
        }

        // ── A bracket may not close the trade it is protecting ───────────────

        private (PaperTradingProvider Paper, MockWorkspaceStore Store, IDataService Data) MakePaper()
        {
            var store = new MockWorkspaceStore();
            var paths = Substitute.For<IPlatformPathService>();
            paths.AppDataDirectory.Returns(_dir);
            var data = Substitute.For<IDataService>();
            data.GetProviderAsync(Arg.Any<string>()).Returns(Task.FromResult<IMarketDataProvider?>(null));
            data.FetchOhlcvAsync(Arg.Any<string>(), Arg.Any<MarketDataRequest>())
                .Returns(Task.FromResult((new List<Ohlcv>(), new List<(long, double)>())));
            var paper = new PaperTradingProvider(store, paths,
                NullLogger<PaperTradingProvider>.Instance, null, data);
            store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Spot", "Venue", "BTC/USD", "1h"),
                Data = new TimeSeriesBuffer<Ohlcv>(new Ohlcv(DateTime.UtcNow, 100, 100, 100, 100, 1)),
            });
            return (paper, store, data);
        }

        /// <summary>
        /// Market buy at 100 with the stop typed as 110. Several venues accept such a stop and
        /// trigger it immediately; the paper broker's own <c>Crossed</c> for a sell stop is
        /// <c>bar.Low &lt;= trigger</c>, true on the very next bar. The position closes itself
        /// having paid two fees — and on a simulator that teaches a habit that loses real money.
        /// </summary>
        [Theory]
        // (side, stop, takeProfit) — each pair is on the wrong side of a fill at 100.
        [InlineData(true,  110.0, null)]   // long, stop ABOVE the entry
        [InlineData(true,  null,  90.0)]   // long, target BELOW the entry
        [InlineData(false, 90.0,  null)]   // short, stop BELOW the entry
        [InlineData(false, null,  110.0)]  // short, target ABOVE the entry
        public async Task ABracketLegOnTheWrongSideOfTheEntryIsRefusedAndAnnounced(
            bool isLong, double? stop, double? target)
        {
            var (paper, _, _) = MakePaper();
            var rejects = new List<OrderUpdate>();
            using var sub = paper.OrderUpdateStream
                .Where(u => u.Status == OrderStatus.Rejected)
                .Subscribe(rejects.Add);

            string id = await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: "BTC/USD",
                Side: isLong ? OrderSide.Buy : OrderSide.Sell,
                Quantity: 1.0,
                Type: OrderType.Market,
                StopLoss: stop,
                TakeProfit: target));

            Assert.DoesNotContain("ORDER_FAILED", id);          // the ENTRY still goes
            var refusal = Assert.Single(rejects);
            Assert.False(string.IsNullOrWhiteSpace(refusal.Reason));
            Assert.Contains("not attached", refusal.Reason!, StringComparison.OrdinalIgnoreCase);

            // And nothing protective is resting: the leg was refused, not silently accepted.
            var open = await paper.GetOpenOrdersAsync("BTC/USD");
            Assert.Empty(open);
        }

        /// <summary>
        /// The vacuity half. A CORRECT bracket must attach silently — a guard that refused every
        /// leg would pass the test above and break the feature.
        /// </summary>
        [Fact]
        public async Task ACorrectBracketAttachesWithNoComplaint()
        {
            var (paper, _, _) = MakePaper();
            var rejects = new List<OrderUpdate>();
            using var sub = paper.OrderUpdateStream
                .Where(u => u.Status == OrderStatus.Rejected)
                .Subscribe(rejects.Add);

            await paper.PlaceOrderAsync(new TradeSignal(
                Symbol: "BTC/USD", Side: OrderSide.Buy, Quantity: 1.0, Type: OrderType.Market,
                StopLoss: 90, TakeProfit: 110));

            Assert.Empty(rejects);
            var open = await paper.GetOpenOrdersAsync("BTC/USD");
            Assert.Equal(2, open.Count);
        }

        /// <summary>
        /// The ticket checks the same rule BEFORE anything is sent, so on a live venue the order
        /// never leaves. <c>ProtectiveLevelValidator</c> is the single statement of the rule and
        /// had exactly one caller — the position table's inline editor — so the one place a
        /// bracket is typed from scratch was the one place it was not checked.
        /// </summary>
        [Fact]
        public void TheTicketValidatesItsProtectiveLevelsBeforeSubmitting()
        {
            string body = DashboardMethod("private async Task SubmitOrder()");
            int validate = body.IndexOf("ValidateTicketProtectiveLevels()", StringComparison.Ordinal);
            int place = body.IndexOf("OrderService.PlaceOrderAsync", StringComparison.Ordinal);

            Assert.True(validate >= 0, "SubmitOrder does not check its stop/target direction at all.");
            Assert.True(place >= 0, "SubmitOrder no longer places an order — re-point this guard.");
            Assert.True(validate < place,
                "SubmitOrder validates the bracket AFTER placing the order, which is not validation.");

            string check = DashboardMethod("private bool ValidateTicketProtectiveLevels()");
            Assert.Contains("ProtectiveLevelValidator.Validate", check, StringComparison.Ordinal);
        }

        // ── Equity is one currency, never a sum of several ───────────────────

        /// <summary>
        /// ¥1,000,000 plus $2,000 is not 1,002,000 of anything. Summed, a 1% quick trade sized a
        /// ~$10,020 position against a ~$8,700 account — the same category error the comment two
        /// lines above it made the case against, one currency class down.
        /// </summary>
        [Fact]
        public void EquityNeverSumsAcrossCurrencies()
        {
            var eq = new QuickTradeEquity();
            eq.ReportCashLines(new[] { ("JPY", 1_000_000.0), ("USD", 2_000.0) });

            Assert.Equal(1_000_000, eq.Latest);       // one currency, the largest line
            Assert.Equal("JPY", eq.LatestAsset);
            Assert.Equal(2_000, eq.LatestFor("USD")); // and each is still readable on its own
            Assert.Equal(1_000_000, eq.LatestFor("jpy"));
        }

        [Fact]
        public void EquityStillSumsMultipleLinesOfTheSameCurrency()
        {
            // Two USD lines (spot + margin wallet, say) ARE one balance.
            var eq = new QuickTradeEquity();
            eq.ReportCashLines(new[] { ("USD", 500.0), ("USD", 250.0), ("BTC", 3.0) });

            Assert.Equal(750, eq.Latest);
            Assert.Equal("USD", eq.LatestAsset);
            Assert.Equal(0, eq.LatestFor("BTC"));     // never cash, never sizing input
        }

        [Fact]
        public void EquityIgnoresNonCashAndNonsenseLines()
        {
            var eq = new QuickTradeEquity();
            eq.ReportCashLines(new[] { ("BTC", 10.0), ("USD", double.NaN), ("EUR", -5.0) });

            Assert.Equal(0, eq.Latest);
            Assert.Equal("", eq.LatestAsset);
        }

        /// <summary>
        /// The dashboard's own sizer had the same bug one step further out: it took the largest
        /// balance of ANY asset, so an account holding 50,000 DOGE and 300 USDT sized every trade
        /// from 50,000 — roughly 166× too large, from a button labelled "Size from risk".
        /// </summary>
        [Fact]
        public void TheDashboardRiskSizerReadsCashOnly()
        {
            string body = DashboardMethod("private void SizeFromRisk()");

            Assert.DoesNotContain("_balances.Select(b => b.Free).DefaultIfEmpty(0).Max()", body,
                StringComparison.Ordinal);
            Assert.Contains("QuickTradeEquity.IsCashAsset", body, StringComparison.Ordinal);
            // Rounding half-away-from-zero could put the sized quantity ABOVE the risk budget
            // the user just asked to stay inside.
            Assert.Contains("Math.Floor", body, StringComparison.Ordinal);
        }
    }
}
