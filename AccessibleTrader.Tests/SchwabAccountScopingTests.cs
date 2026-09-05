using System.Reflection;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The Schwab account you can see is the Schwab account you can trade.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Schwab addresses orders by an opaque <c>hashValue</c>, and the provider resolves exactly
    /// one: <c>_primaryAccountHash = _accountHashes.FirstOrDefault()?.HashValue</c>.
    /// <c>PlaceOrderAsync</c>, <c>CancelOrderAsync</c>, <c>GetOpenOrdersAsync</c>,
    /// <c>GetFillsAsync</c> and <c>GetOrderStatusAsync</c> all address that single hash.
    /// </para>
    ///
    /// <para>
    /// But <c>GetPositionsAsync</c> and <c>GetBalancesAsync</c> called
    /// <c>GET /accounts?fields=positions</c>, which returns <b>every</b> account the grant
    /// covers, and flattened all of them into one list. A user with a brokerage account and an
    /// IRA saw the IRA's positions in the dashboard, pressed sell, and the order went to
    /// whichever account Schwab happened to list first. Balances compounded it: <c>"Cash"</c>,
    /// <c>"Equity"</c> and <c>"Buying Power"</c> were emitted once per account under identical
    /// asset names, so the numbers on screen belonged to no particular account.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// The fixture deliberately puts the traded account <b>second</b> in the payload. A fixture
    /// where the traded account happens to be first cannot tell "scoped correctly" from "took
    /// the first one", which is the whole defect. There is no account selector in the UI yet;
    /// until there is, showing only the tradable account is the honest behaviour, and this
    /// pins it.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class SchwabAccountScopingTests
    {
        private const string TradedNumber = "22222222";
        private const string TradedHash   = "HASH-TRADED";
        private const string OtherNumber  = "11111111";

        /// <summary>
        /// Two accounts, and the one orders route to is NOT the one listed first in the
        /// positions payload.
        /// </summary>
        private const string TwoAccountPositionsPayload = """
            [
              {"securitiesAccount":{
                 "accountNumber":"11111111",
                 "currentBalances":{"equity":111.0,"cashBalance":11.0,"buyingPower":1111.0},
                 "positions":[{"instrument":{"symbol":"IRA_ONLY"},
                               "longQuantity":7.0,"shortQuantity":0.0,
                               "averagePrice":10.0,"marketValue":70.0,
                               "longOpenProfitLoss":0.0}]
              }},
              {"securitiesAccount":{
                 "accountNumber":"22222222",
                 "currentBalances":{"equity":222.0,"cashBalance":22.0,"buyingPower":2222.0},
                 "positions":[{"instrument":{"symbol":"BROKERAGE_ONLY"},
                               "longQuantity":3.0,"shortQuantity":0.0,
                               "averagePrice":20.0,"marketValue":60.0,
                               "longOpenProfitLoss":0.0}]
              }}
            ]
            """;

        private static void Swap(object provider, FakeHttpMessageHandler handler)
        {
            HttpClientSwap.ReplaceAll(provider, handler);
        }

        /// <summary>
        /// A Schwab provider whose account hashes have been resolved through the real
        /// <c>RefreshAccountHashesAsync</c> path — so the join this fix relies on (hash →
        /// plain account number) is the one under test, not one the fixture faked.
        /// </summary>
        private static async Task<AccessibleTrader.Plugins.Schwab.SchwabProvider> ConnectedSchwabAsync(
            FakeHttpMessageHandler h)
        {
            h.Get(@"/accounts/accountNumbers", $$"""
                [
                  {"accountNumber":"{{TradedNumber}}","hashValue":"{{TradedHash}}"},
                  {"accountNumber":"{{OtherNumber}}","hashValue":"HASH-OTHER"}
                ]
                """);

            var p = new AccessibleTrader.Plugins.Schwab.SchwabProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "client-id",
                ["ApiSecret"] = "client-secret",
                ["Passphrase"] = "seeded-refresh-token",
            });
            Swap(p, h);

            var oauthField = typeof(AccessibleTrader.Plugins.Schwab.SchwabProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType.Name == "SchwabOAuthService");
            var oauth = oauthField.GetValue(p)!;
            oauth.GetType().GetProperty("AccessToken")!.SetValue(oauth, "seeded-access-token");
            oauth.GetType().GetProperty("AccessTokenExpiresAtUtc")!
                 .SetValue(oauth, DateTime.UtcNow.AddHours(1));

            var refresh = typeof(AccessibleTrader.Plugins.Schwab.SchwabProvider)
                .GetMethod("RefreshAccountHashesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)refresh.Invoke(p, Array.Empty<object>())!;

            return p;
        }

        [Fact]
        public async Task Positions_come_only_from_the_account_orders_are_routed_to()
        {
            var h = new FakeHttpMessageHandler().Get(@"/accounts\?fields=positions", TwoAccountPositionsPayload);
            var p = await ConnectedSchwabAsync(h);

            var positions = await p.GetPositionsAsync();

            var pos = Assert.Single(positions);
            Assert.Equal("BROKERAGE_ONLY", pos.Symbol);
            // The IRA's holding must not appear next to a Sell button that cannot reach it.
            Assert.DoesNotContain(positions, x => x.Symbol == "IRA_ONLY");
        }

        [Fact]
        public async Task Balances_are_one_set_of_rows_belonging_to_one_named_account()
        {
            var h = new FakeHttpMessageHandler().Get(@"/accounts\?fields=positions", TwoAccountPositionsPayload);
            var p = await ConnectedSchwabAsync(h);

            var balances = await p.GetBalancesAsync();

            // Exactly one Cash / Equity / Buying Power row, and they are the traded account's.
            Assert.Equal(3, balances.Count);
            Assert.Equal(22.0,   Assert.Single(balances, b => b.Asset == "Cash").Free);
            Assert.Equal(222.0,  Assert.Single(balances, b => b.Asset == "Equity").Free);
            Assert.Equal(2222.0, Assert.Single(balances, b => b.Asset == "Buying Power").Free);
        }

        [Fact]
        public async Task An_unresolvable_account_reports_nothing_rather_than_everything()
        {
            // If the hash cannot be joined back to an account number, reporting the union of
            // every account is the one answer that is actively dangerous. An empty list is
            // recoverable; a list mixing two accounts is not.
            var h = new FakeHttpMessageHandler().Get(@"/accounts\?fields=positions", TwoAccountPositionsPayload);
            var p = await ConnectedSchwabAsync(h);

            typeof(AccessibleTrader.Plugins.Schwab.SchwabProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.Name.Contains("primaryAccountHash"))
                .SetValue(p, "HASH-THAT-IS-NOT-IN-THE-LIST");

            Assert.Empty(await p.GetPositionsAsync());
            Assert.Empty(await p.GetBalancesAsync());
        }

        [Fact]
        public async Task The_fixture_lists_the_traded_account_second()
        {
            // Vacuity check for the two tests above: if the traded account were first in the
            // payload, "scoped to the traded account" and "took whichever came first" would
            // produce identical results and neither test would guard anything.
            var h = new FakeHttpMessageHandler().Get(@"/accounts\?fields=positions", TwoAccountPositionsPayload);
            await ConnectedSchwabAsync(h);

            int tradedAt = TwoAccountPositionsPayload.IndexOf(TradedNumber, StringComparison.Ordinal);
            int otherAt  = TwoAccountPositionsPayload.IndexOf(OtherNumber, StringComparison.Ordinal);
            Assert.True(otherAt < tradedAt,
                "The traded account must NOT be first in the fixture, or these tests are vacuous.");
        }
    }
}
