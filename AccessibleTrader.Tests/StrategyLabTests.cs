using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// First unit coverage for the StrategyLab research harness — the pure math
    /// that every battery/rolling-window verdict rests on. If TradeR or the
    /// bootstrap CI drift, every historical "SURVIVOR" comparison silently breaks.
    /// </summary>
    public class StrategyLabTests
    {
        private static BacktestTrade Trade(
            double entry, double? exit, double? stop, OrderSide side = OrderSide.Buy) =>
            new(DateTime.UtcNow, entry, side, 1.0, DateTime.UtcNow, exit, null, "tp", stop);

        // ── BootstrapCi.TradeR ───────────────────────────────────────────────

        [Fact]
        public void TradeR_Long_WinAndLoss()
        {
            // entry 100, stop 90 → risk 10. Exit 120 = +2R; exit 95 = -0.5R.
            Assert.Equal(2.0, BootstrapCi.TradeR(Trade(100, 120, 90)), 6);
            Assert.Equal(-0.5, BootstrapCi.TradeR(Trade(100, 95, 90)), 6);
        }

        [Fact]
        public void TradeR_Short_SignFlipped_SoWinningShortIsPositive()
        {
            // Short entry 100, stop 110 → risk 10. Exit 80 = +2R for the short.
            Assert.Equal(2.0, BootstrapCi.TradeR(Trade(100, 80, 110, OrderSide.Sell)), 6);
            Assert.Equal(-1.0, BootstrapCi.TradeR(Trade(100, 110, 110, OrderSide.Sell)), 6);
        }

        [Theory]
        [InlineData(false, true)]   // no exit
        [InlineData(true, false)]   // no stop
        public void TradeR_MissingData_ReturnsNaN(bool hasExit, bool hasStop)
        {
            var t = Trade(100, hasExit ? 120 : null, hasStop ? 90.0 : null);
            Assert.True(double.IsNaN(BootstrapCi.TradeR(t)));
        }

        [Fact]
        public void TradeR_ZeroRisk_ReturnsNaN()
            => Assert.True(double.IsNaN(BootstrapCi.TradeR(Trade(100, 120, 100))));

        // ── BootstrapCi.Compute ──────────────────────────────────────────────

        [Fact]
        public void Compute_IsDeterministic_AndBracketsTheMean()
        {
            var rs = new List<double> { 1.0, -0.5, 2.0, -1.0, 0.5, 1.5, -0.5, 1.0 };

            var a = BootstrapCi.Compute(rs);
            var b = BootstrapCi.Compute(rs);

            Assert.Equal(a, b); // fixed seed → identical CI across runs
            Assert.True(a.Lo <= a.Mean && a.Mean <= a.Hi, $"CI must bracket mean: {a}");
            Assert.Equal(0.5, a.Mean, 6); // point estimate is the sample mean
        }

        [Fact]
        public void Compute_AllPositiveRs_HasPositiveLowerBound()
        {
            var rs = new List<double> { 0.5, 1.0, 0.8, 1.2, 0.6, 0.9, 1.1, 0.7 };
            var ci = BootstrapCi.Compute(rs);
            Assert.True(ci.Lo > 0, $"uniformly winning sample must have CIlo>0, was {ci.Lo}");
        }

        [Fact]
        public void ExtractRs_FiltersUncomputableTrades()
        {
            var trades = new List<BacktestTrade>
            {
                Trade(100, 120, 90),      // valid: +2R
                Trade(100, null, 90),     // open — dropped
                Trade(100, 120, null),    // no stop — dropped
            };
            var r = Assert.Single(BootstrapCi.ExtractRs(trades));
            Assert.Equal(2.0, r, 6);
        }

        // ── MarkerSideHelper ─────────────────────────────────────────────────

        [Theory]
        [InlineData("Bullish Divergence", "Buy")]   // "bull" must win before "div"
        [InlineData("Oversold Crossover", "Buy")]
        [InlineData("Manipulation", "Buy")]
        [InlineData("Bearish Divergence", "Sell")]
        [InlineData("Overbought Crossover", "Sell")]
        [InlineData("Blood Diamond", "Sell")]
        [InlineData("DC Day Count", null)]          // neutral component → no side
        public void MarkerSide_ClassifiesByKeyword(string component, string? expected)
        {
            var side = MarkerSideHelper.Classify(component);
            Assert.Equal(expected, side?.ToString());
        }

        // ── CrossSeriesSnapshot ──────────────────────────────────────────────

        [Fact]
        public void Snapshot_CacheKey_MatchesRequestKeyConvention_AndRoundTrips()
        {
            var snap = new CrossSeriesSnapshot
            {
                Provider = "CFTC",
                Symbol = "GOLD_COT",
                Timeframe = "1w",
                Points = new List<TimedValue>
                {
                    new() { Ts = 1000, Value = 12.5 },
                    new() { Ts = 2000, Value = -3.0 },
                },
                PointCount = 2,
            };

            // The lowercase "{provider}:{symbol}:{timeframe}" key is the contract the
            // SnapshottingCrossSeriesCache matches CotPositioningProvider requests on.
            Assert.Equal("cftc:gold_cot:1w", snap.CacheKey);
            Assert.EndsWith(Path.Combine("dir", "xs_cftc_gold_cot_1w.json"), snap.FilePath("dir"));

            string dir = Path.Combine(Path.GetTempPath(), "at-lab-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(snap.FilePath(dir), Newtonsoft.Json.JsonConvert.SerializeObject(snap));
                var cache = new SnapshottingCrossSeriesCache(dir);
                var got = cache.GetOrFetch(new AccessibleTrader.Core.Services.Indicators.CrossSeriesRequest(
                    "Derivatives", "CFTC", "GOLD_COT", "1w", 1));
                Assert.Equal(2, got.Count);
                Assert.Equal(12.5, got[0].Value, 6);
                // Unknown key → empty, never throws.
                Assert.Empty(cache.GetOrFetch(new AccessibleTrader.Core.Services.Indicators.CrossSeriesRequest(
                    "Derivatives", "CFTC", "NOPE_COT", "1w", 1)));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
