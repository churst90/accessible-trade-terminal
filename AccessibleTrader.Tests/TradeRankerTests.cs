using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using Xunit;

namespace AccessibleTrader.Tests
{
    public class TradeRankerTests
    {
        [Fact]
        public void Score_BareFire_ReturnsBaseline40()
        {
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy);
            Assert.Equal(40, TradeRanker.Score(ctx));
        }

        [Fact]
        public void Score_HurstMeanReverting_LiftsLongScore()
        {
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy, HurstValue: 0.30);
            int score = TradeRanker.Score(ctx);
            Assert.True(score > 40, $"Expected > 40, got {score}");
        }

        [Fact]
        public void Score_HurstTrending_PenalizesReversal()
        {
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy, HurstValue: 0.70);
            int score = TradeRanker.Score(ctx);
            Assert.True(score < 40, $"Expected < 40, got {score}");
        }

        [Fact]
        public void Score_LongAtSupport_BoostsScore()
        {
            // Pivot zone -1 = at support (longs want this).
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy, PivotZone: -1.0);
            int score = TradeRanker.Score(ctx);
            Assert.True(score > 50, $"Expected > 50 with at-support gate, got {score}");
        }

        [Fact]
        public void Score_LongAtResistance_PenalizesScore()
        {
            // Pivot zone +1 = at resistance (longs want NOT this).
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy, PivotZone: 1.0);
            int score = TradeRanker.Score(ctx);
            Assert.True(score < 40, $"Expected < 40 with at-resistance penalty, got {score}");
        }

        [Fact]
        public void Score_ShortAtResistance_BoostsScore()
        {
            var ctx = new TradeRanker.SignalContext(OrderSide.Sell, PivotZone: 1.0);
            int score = TradeRanker.Score(ctx);
            Assert.True(score > 50, $"Expected > 50, got {score}");
        }

        [Fact]
        public void Score_FullStackAlignedLong_ReachesVeryHigh()
        {
            // Maximum-confluence long: Hurst mean-reverting + at support + AVWAP bias up
            // + below SMA200 capitulation context + negative funding (contrarian).
            var ctx = new TradeRanker.SignalContext(
                OrderSide.Buy,
                HurstValue: 0.30,
                AvwapBias: 1.0,
                PivotZone: -1.0,
                AnchorWave: -60.0,
                AboveSma200: 1.0,
                Funding: -0.001,
                TimeframeMinutes: "1440");
            int score = TradeRanker.Score(ctx);
            Assert.True(score >= 85, $"Expected >= 85 (very high band), got {score}");
            Assert.Equal("very high", TradeRanker.ConfidenceBand(score));
        }

        [Fact]
        public void Score_FullStackOpposedLong_FloorsLow()
        {
            // Maximum-anti-confluence long: trending + at resistance + bearish bias.
            var ctx = new TradeRanker.SignalContext(
                OrderSide.Buy,
                HurstValue: 0.70,
                AvwapBias: -1.0,
                PivotZone: 1.0,
                AnchorWave: 60.0,
                AboveSma200: -1.0,
                Funding: 0.001,
                TimeframeMinutes: "60");
            int score = TradeRanker.Score(ctx);
            Assert.True(score < 25, $"Expected < 25, got {score}");
        }

        [Fact]
        public void Score_NaNInputs_DontPenalizeScore()
        {
            // All NaN = no information beyond the strategy fire = baseline 40.
            var ctx = new TradeRanker.SignalContext(OrderSide.Buy);
            Assert.Equal(40, TradeRanker.Score(ctx));
        }

        [Fact]
        public void Score_TimeframeBonus_WeeklyLiftsScore()
        {
            var weekly  = new TradeRanker.SignalContext(OrderSide.Buy, TimeframeMinutes: "10080");
            var fourh   = new TradeRanker.SignalContext(OrderSide.Buy, TimeframeMinutes: "240");
            Assert.True(TradeRanker.Score(weekly) > TradeRanker.Score(fourh));
        }

        [Theory]
        [InlineData(0, "weak")]
        [InlineData(40, "marginal")]
        [InlineData(55, "moderate")]
        [InlineData(70, "high")]
        [InlineData(85, "very high")]
        [InlineData(100, "very high")]
        public void ConfidenceBand_MapsScoreToLabel(int score, string expected)
        {
            Assert.Equal(expected, TradeRanker.ConfidenceBand(score));
        }
    }
}
