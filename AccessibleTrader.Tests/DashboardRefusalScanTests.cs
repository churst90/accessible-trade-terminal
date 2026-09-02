using System.Text.RegularExpressions;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The trading dashboard must never refuse in silence, and must never resolve a
    /// venue from the focused chart on a control attached to a row that knows its own.
    ///
    /// <para>
    /// Both rules come out of one bug. Seven sites in this dialog opened with
    /// <c>var provider = Store.State.Identity.Provider; if (string.IsNullOrEmpty(provider))
    /// return;</c>. With no chart focused the user pressed Cancel and the terminal did
    /// nothing and said nothing — indistinguishable from a dead control, and on a
    /// terminal whose premise is that the user cannot see the screen, that is the worst
    /// available outcome. The venue was wrong as well as the silence: it came from
    /// whatever chart happened to be in front rather than from the order.
    /// </para>
    ///
    /// <para>
    /// These are source scans, so each one carries a vacuity check — a scan that finds
    /// nothing because its pattern stopped matching anything is a green test guarding
    /// nothing, and this repo has shipped that mistake before.
    /// </para>
    /// </summary>
    public class DashboardRefusalScanTests
    {
        /// <summary>The silent-refusal shape, exactly as it was written at all seven sites.</summary>
        private static readonly Regex SilentGuard =
            new(@"if\s*\(\s*string\.IsNullOrEmpty\s*\([^)]*\)\s*\)\s*return\s*;", RegexOptions.Compiled);

        [Fact]
        public void The_scan_pattern_still_matches_the_defect_it_is_looking_for()
        {
            // The vacuity check, and it is not decoration: an assertion that a pattern
            // finds nothing is satisfied just as well by a pattern that can no longer
            // find anything. Written the way the original was.
            Assert.Matches(SilentGuard, "var provider = Store.State.Identity.Provider;\n"
                                      + "if (string.IsNullOrEmpty(provider)) return;\n");
            Assert.DoesNotMatch(SilentGuard, "if (string.IsNullOrEmpty(provider)) { Speak(); return; }");
        }

        [Fact]
        public void No_dashboard_action_returns_silently_on_an_empty_identity()
        {
            string src = DashboardSourceReader.Stripped();

            var hits = SilentGuard.Matches(src).Select(m => m.Value).ToList();
            Assert.True(hits.Count == 0,
                $"{hits.Count} silent early return(s) are back in the trading dashboard: "
                + string.Join(" | ", hits)
                + ". A button that does nothing and says nothing is indistinguishable from a dead "
                + "control. Publish a FeedbackRequestEvent on every refusal path.");
        }

        /// <summary>
        /// The methods that act on an existing row. None of them may read the chart's
        /// provider — the row carries its own account, and that is the whole decoupling.
        /// </summary>
        public static TheoryData<string> RowScopedActions() => new()
        {
            "private async Task CancelOrder(AccountOrder row)",
            "private async Task CloseAsync(AccountPosition row, double? limitPrice)",
            "private async Task CommitProtectiveAsync(AccountPosition row, ProtectiveLevel level)",
            "private async Task<List<TradingAccount>> EnumerateAccountsAsync()",
            "private async Task<AccountData> LoadOneAccountAsync(TradingAccount account)",
        };

        [Theory]
        [MemberData(nameof(RowScopedActions))]
        public void A_row_scoped_action_never_asks_what_chart_is_focused(string signature)
        {
            // Stripped: this file's comments quote the defect they replaced, and that
            // is the most useful line in several of these methods.
            string body = DashboardSourceReader.MethodStripped(signature);

            Assert.DoesNotContain("Store.State.Identity.Provider", body, StringComparison.Ordinal);
        }

        [Fact]
        public void The_order_ticket_is_still_chart_bound_on_purpose()
        {
            // The complement, and the reason the theory above is not vacuous. You place
            // an order on the symbol you are LOOKING at; only the account views and the
            // actions on existing rows were ever wrong to ask. If this ever goes red the
            // ticket has been decoupled too, which needs an account selector of its own
            // rather than a silent guess.
            // Pointed at BuildSignal, which is where the chart's identity now enters the
            // order. SubmitOrder still mentions Store.State.Identity — for the provider it
            // passes to PlaceOrderAsync and for the in-flight latch's label — so this test
            // kept passing after the signal construction moved out from under it, and would
            // have kept passing with `Symbol:` deleted. A guard that cannot fail for its own
            // reason is not guarding anything.
            string body = DashboardSourceReader.MethodStripped("private TradeSignal BuildSignal()");

            Assert.Contains("Symbol:     Store.State.Identity.Symbol!", body, StringComparison.Ordinal);
        }

        [Fact]
        public void The_orders_read_is_not_scoped_to_the_charts_symbol()
        {
            // The other half of the reported bug: an exact string match on the chart's
            // symbol, so orders on BTC/USD vanished behind a BTCUSDT chart and the tab
            // read as "you have no orders".
            string body = DashboardSourceReader.MethodStripped("private async Task<AccountData> LoadOneAccountAsync(TradingAccount account)");

            Assert.Contains("GetOpenOrdersAsync(provider, null)", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Identity.Symbol", body, StringComparison.Ordinal);
        }
    }
}
