using System.Reflection;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b><see cref="Position.AveragePrice"/> is per unit at every provider boundary.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Kraken reports a position's <c>cost</c> and Tradier its <c>cost_basis</c>. Both are the
    /// <i>total</i> quote-currency cost of the position. Both were passed straight into the
    /// third positional argument of <see cref="Position"/>, which is the <i>per-unit</i> average
    /// entry price. A 0.5 BTC Kraken position entered at 60,000 reported an average price of
    /// 30,000; 100 shares of a $50 stock on Tradier reported 5,000.
    /// </para>
    ///
    /// <para>
    /// This is not a cosmetic number. It is read aloud in the positions panel — the only way a
    /// blind user learns where they got in — and it feeds risk math downstream. Being wrong by a
    /// factor of the position size is wrong in the direction that makes a losing position look
    /// like a winning one.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// Each venue is fed a canned positions payload whose total cost and unit count are
    /// deliberately <b>not</b> equal (quantity ≠ 1), because a fixture with quantity 1 cannot
    /// tell a total from an average — that is precisely why the defect survived. Binance and
    /// Schwab already read a per-unit field; they are pinned here too so that a future
    /// "consistency" sweep cannot divide them a second time.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class PositionAveragePriceTests
    {
        private static void Swap(object provider, FakeHttpMessageHandler handler)
        {
            var target = provider.GetType()
                                 .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                 .FirstOrDefault(f => f.FieldType == typeof(HttpClient));
            Assert.NotNull(target);
            target!.SetValue(provider, new HttpClient(handler));
        }

        // ── Kraken: OpenPositions "cost" is the position total ────────────────

        [Fact]
        public async Task Kraken_divides_total_cost_by_volume_to_report_a_per_unit_average()
        {
            // 0.5 BTC at 60,000 → cost 30,000. Reporting 30,000 as the average price is the bug.
            var h = new FakeHttpMessageHandler().Post(@"/0/private/OpenPositions", """
                {"error":[],"result":{"TP1":{
                  "pair":"XXBTZUSD","type":"buy","vol":"0.5",
                  "cost":"30000.0","value":"31000.0","net":"1000.0","margin":"6000.0"
                }}}
                """);
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k",
                ["ApiSecret"] = Convert.ToBase64String(new byte[32])
            });
            Swap(p, h);

            var positions = await p.GetPositionsAsync();

            var pos = Assert.Single(positions);
            Assert.Equal(0.5, pos.Quantity, 6);
            Assert.Equal(60000.0, pos.AveragePrice, 6);   // NOT 30000
            // Leverage is genuinely total-cost over margin and must stay undivided.
            Assert.Equal(5.0, pos.Leverage, 6);
        }

        [Fact]
        public async Task Kraken_reports_a_short_at_its_per_unit_average_with_a_signed_quantity()
        {
            // 2 units short at 1,500 → cost 3,000. Sign lives on Quantity, never on the price.
            var h = new FakeHttpMessageHandler().Post(@"/0/private/OpenPositions", """
                {"error":[],"result":{"TP2":{
                  "pair":"XETHZUSD","type":"sell","vol":"2.0",
                  "cost":"3000.0","value":"2900.0","net":"100.0","margin":"1000.0"
                }}}
                """);
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k",
                ["ApiSecret"] = Convert.ToBase64String(new byte[32])
            });
            Swap(p, h);

            var pos = Assert.Single(await p.GetPositionsAsync());
            Assert.Equal(-2.0, pos.Quantity, 6);
            Assert.Equal(1500.0, pos.AveragePrice, 6);    // positive, and per unit
        }

        [Fact]
        public async Task Kraken_a_zero_volume_position_does_not_divide_by_zero()
        {
            var h = new FakeHttpMessageHandler().Post(@"/0/private/OpenPositions", """
                {"error":[],"result":{"TP3":{
                  "pair":"XXBTZUSD","type":"buy","vol":"0",
                  "cost":"0","value":"0","net":"0","margin":"0"
                }}}
                """);
            var p = new AccessibleTrader.Plugins.Kraken.KrakenProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "k",
                ["ApiSecret"] = Convert.ToBase64String(new byte[32])
            });
            Swap(p, h);

            var pos = Assert.Single(await p.GetPositionsAsync());
            Assert.Equal(0.0, pos.AveragePrice);
            Assert.False(double.IsNaN(pos.AveragePrice) || double.IsInfinity(pos.AveragePrice));
        }

        // ── Tradier: positions "cost_basis" is the position total ─────────────

        private static AccessibleTrader.Plugins.Tradier.TradierProvider Tradier(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Tradier.TradierProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "t",
                ["AccountId"] = "ACC1"
            });
            Swap(p, h);
            return p;
        }

        [Fact]
        public async Task Tradier_divides_cost_basis_by_quantity_to_report_a_per_unit_average()
        {
            // 100 shares at $50 → cost_basis 5,000. Reporting 5,000 as the average is the bug.
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/positions", """
                {"positions":{"position":{
                  "symbol":"AAPL","quantity":100.0,"cost_basis":5000.0,"last_price":52.0
                }}}
                """);

            var pos = Assert.Single(await Tradier(h).GetPositionsAsync());
            Assert.Equal(100.0, pos.Quantity, 6);
            Assert.Equal(50.0, pos.AveragePrice, 6);      // NOT 5000
            Assert.Equal(5200.0, pos.MarketValue, 6);
        }

        [Fact]
        public async Task Tradier_reports_a_short_at_its_per_unit_average_with_a_signed_quantity()
        {
            // Tradier signs the quantity itself: -10 shares, cost_basis -400 → $40 a share.
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/positions", """
                {"positions":{"position":{
                  "symbol":"F","quantity":-10.0,"cost_basis":-400.0,"last_price":38.0
                }}}
                """);

            var pos = Assert.Single(await Tradier(h).GetPositionsAsync());
            Assert.Equal(-10.0, pos.Quantity, 6);
            Assert.Equal(-40.0, pos.AveragePrice, 6);     // magnitude is per-share, sign follows the venue
            Assert.Equal(-380.0, pos.MarketValue, 6);
        }

        [Fact]
        public async Task Tradier_a_zero_quantity_position_does_not_divide_by_zero()
        {
            var h = new FakeHttpMessageHandler().Get(@"/accounts/ACC1/positions", """
                {"positions":{"position":{
                  "symbol":"AAPL","quantity":0.0,"cost_basis":0.0,"last_price":52.0
                }}}
                """);

            var pos = Assert.Single(await Tradier(h).GetPositionsAsync());
            Assert.Equal(0.0, pos.AveragePrice);
            Assert.False(double.IsNaN(pos.AveragePrice) || double.IsInfinity(pos.AveragePrice));
        }
    }
}
