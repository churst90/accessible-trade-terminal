using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// COT Positioning indicator (promoted from StrategyLab after the 2026-07
    /// cross-asset validation): chart-symbol → CFTC contract mapping, weekly
    /// forward-fill, unique-value z-score (repeated daily fills must not deflate
    /// the variance), and crowded-extreme markers at ±1.5 sigma.
    /// </summary>
    public class CotPositioningIndicatorTests
    {
        private const string Code = "COT_POSITIONING";

        // ── Symbol mapping ────────────────────────────────────────────────────

        [Theory]
        [InlineData("XAU/USD",  "GOLD_COT")]
        [InlineData("GOLD",     "GOLD_COT")]
        [InlineData("BTC/USDT", "BITCOIN_COT")]
        [InlineData("btc-usd",  "BITCOIN_COT")]
        [InlineData("ETH/USD",  "ETHER_COT")]
        [InlineData("SPY",      "SP500_COT")]
        [InlineData("QQQ",      "NASDAQ_COT")]
        [InlineData("EUR/USD",  "EURO_FX_COT")]
        [InlineData("DXY",      "USD_INDEX_COT")]
        [InlineData("CL",       "WTI_CRUDE_COT")]
        [InlineData("XAG/USD",  "SILVER_COT")]
        public void MapSymbol_RoutesChartSymbols_ToCftcContracts(string chart, string expected)
            => Assert.Equal(expected, CotPositioningProvider.MapSymbol(chart));

        [Theory]
        [InlineData("AAPL")]      // single stocks have no COT contract
        [InlineData("USD/JPY")]   // base token is USD, not a mapped contract
        [InlineData("")]
        [InlineData(null)]
        public void MapSymbol_UnmappedSymbols_ReturnNull(string? chart)
            => Assert.Null(CotPositioningProvider.MapSymbol(chart));

        // ── Calculation ───────────────────────────────────────────────────────

        private static Ohlcv[] DailyBars(DateTime start, int n)
            => Enumerable.Range(0, n)
                .Select(i => new Ohlcv(start.AddDays(i), 100, 101, 99, 100, 1000))
                .ToArray();

        private static (CotPositioningProvider Provider, ICrossSeriesCache Cache) Build(
            IReadOnlyList<(long Ts, double Value)> ticks)
        {
            var xs = Substitute.For<ICrossSeriesCache>();
            xs.GetOrFetch(Arg.Any<CrossSeriesRequest>()).Returns(ticks);
            return (new CotPositioningProvider(xs), xs);
        }

        private static Dictionary<string, double[]> Run(
            CotPositioningProvider provider, Ohlcv[] bars, Dictionary<string, object> parameters)
        {
            var results = new Dictionary<string, double[]>();
            var buffer = new IndicatorResultBuffer(results, bars.Length);
            provider.Calculate(Code, bars, parameters, buffer);
            return results;
        }

        private static long Ms(DateTime d) => new DateTimeOffset(d, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Fact]
        public void Calculate_ForwardFills_AndComputesZ_OverUniqueWeeklyValues()
        {
            // 30 weekly ticks alternating around a mean, then a final outlier that
            // must register as an extreme against the 26-week window.
            var start = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            var ticks = new List<(long, double)>();
            for (int w = 0; w < 30; w++)
                ticks.Add((Ms(start.AddDays(w * 7)), w % 2 == 0 ? 10.0 : 12.0));
            ticks.Add((Ms(start.AddDays(30 * 7)), 40.0)); // crowded-long outlier

            var (provider, _) = Build(ticks);
            var bars = DailyBars(start, 31 * 7 + 3);
            var results = Run(provider, bars, new() { ["__symbol"] = "XAU/USD" });

            var net = results[CotPositioningProvider.CompNetPctOi];
            var z = results[CotPositioningProvider.CompZScore];
            var crowdedLong = results[CotPositioningProvider.CompCrowdedLong];

            // Forward-fill: bars between releases carry the prior week's value.
            Assert.Equal(10.0, net[0]);
            Assert.Equal(10.0, net[6]);
            Assert.Equal(12.0, net[7]);

            int last = bars.Length - 1;
            Assert.Equal(40.0, net[last]);
            Assert.True(z[last] > 1.5, $"outlier z should be extreme, was {z[last]}");
            Assert.False(double.IsNaN(crowdedLong[last]), "crowded-long marker should fire on the outlier");

            // Repeated daily fills must not have shrunk the variance: with unique-value
            // tracking the alternating 10/12 weeks give sd≈1, so |z| for a normal week
            // stays around ±1 — never extreme.
            Assert.True(Math.Abs(z[20 * 7]) < 1.5, $"normal week must not be extreme, z was {z[20 * 7]}");
        }

        [Fact]
        public void Calculate_CrowdedShortMarker_FiresOnNegativeExtreme()
        {
            var start = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            var ticks = new List<(long, double)>();
            for (int w = 0; w < 30; w++)
                ticks.Add((Ms(start.AddDays(w * 7)), w % 2 == 0 ? 10.0 : 12.0));
            ticks.Add((Ms(start.AddDays(30 * 7)), -20.0));

            var (provider, _) = Build(ticks);
            var bars = DailyBars(start, 31 * 7 + 3);
            var results = Run(provider, bars, new() { ["__symbol"] = "XAU/USD" });

            int last = bars.Length - 1;
            Assert.False(double.IsNaN(results[CotPositioningProvider.CompCrowdedShort][last]));
            Assert.True(double.IsNaN(results[CotPositioningProvider.CompCrowdedLong][last]));
        }

        [Fact]
        public void Calculate_UnmappedSymbol_LeavesAllComponentsNaN_AndSkipsFetch()
        {
            var (provider, cache) = Build(new List<(long, double)>());
            var bars = DailyBars(new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc), 50);
            var results = Run(provider, bars, new() { ["__symbol"] = "AAPL" });

            Assert.All(results[CotPositioningProvider.CompZScore], v => Assert.True(double.IsNaN(v)));
            cache.DidNotReceive().GetOrFetch(Arg.Any<CrossSeriesRequest>());
        }

        [Fact]
        public void Calculate_RequestsWeeklyCftcSeries_ForMappedSymbol()
        {
            var start = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc);
            var (provider, cache) = Build(new List<(long, double)> { (Ms(start), 5.0) });
            var bars = DailyBars(start, 10);
            Run(provider, bars, new() { ["__symbol"] = "EUR/USD" });

            cache.Received(1).GetOrFetch(Arg.Is<CrossSeriesRequest>(r =>
                r.Provider == "CFTC" && r.Symbol == "EURO_FX_COT" && r.Timeframe == "1w"));
        }
    }
}
