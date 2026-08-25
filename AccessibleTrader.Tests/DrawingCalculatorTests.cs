using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// RiskRewardCalculator and MeasureToolCalculator — geometry a user reads a position
    /// size off. The R:R ratio drives how much risk a trade is described as carrying, and
    /// the measure string is what a screen-reader user hears as "the distance", so the
    /// sub-dollar formatting fence matters here as much as on the chart.
    /// </summary>
    public class DrawingCalculatorTests
    {
        private static List<Ohlcv> Bars(int count, double price = 100)
            => Enumerable.Range(0, count).Select(i => new Ohlcv(
                new DateTime(2026, 1, 1, 0, i, 0, DateTimeKind.Utc),
                price, price + 1, price - 1, price, 1000)).ToList();

        // ── RiskRewardCalculator ────────────────────────────────────────────────

        private static DrawingData RiskRewardDrawing(double? entry, double? stop, double? target) => new()
        {
            Type = DrawingType.RiskReward,
            AnchorPrice1 = entry,
            AnchorPrice2 = stop,
            AnchorPrice3 = target,
        };

        [Theory]
        [InlineData(null, 90.0, 130.0)]
        [InlineData(100.0, null, 130.0)]
        [InlineData(100.0, 90.0, null)]
        public void RiskReward_MissingAnchor_YieldsNothing(double? entry, double? stop, double? target)
        {
            var drawing = RiskRewardDrawing(entry, stop, target);
            var results = new RiskRewardCalculator().Calculate(drawing, Bars(10));

            Assert.Empty(results);
            Assert.Equal(0, drawing.RiskRewardRatio);
        }

        [Fact]
        public void RiskReward_LongSetup_ComputesRatio_AndThreeConstantLines()
        {
            var drawing = RiskRewardDrawing(entry: 100, stop: 90, target: 130);
            var bars = Bars(10);

            var results = new RiskRewardCalculator().Calculate(drawing, bars);

            Assert.Equal(3.0, drawing.RiskRewardRatio); // risk 10, reward 30
            Assert.Equal(new[] { "Entry", "Stop Loss", "Take Profit" }, results.Keys);
            Assert.All(results.Values, arr => Assert.Equal(bars.Count, arr.Length));
            Assert.All(results["Entry"], v => Assert.Equal(100.0, v));
            Assert.All(results["Stop Loss"], v => Assert.Equal(90.0, v));
            Assert.All(results["Take Profit"], v => Assert.Equal(130.0, v));
        }

        [Fact]
        public void RiskReward_ShortSetup_UsesAbsoluteDistances()
        {
            // Short: stop above entry, target below. Ratio must not come out negative.
            var drawing = RiskRewardDrawing(entry: 100, stop: 110, target: 80);

            new RiskRewardCalculator().Calculate(drawing, Bars(5));

            Assert.Equal(2.0, drawing.RiskRewardRatio); // risk 10, reward 20
        }

        [Fact]
        public void RiskReward_ZeroRisk_YieldsZeroRatio_NotInfinity()
        {
            var drawing = RiskRewardDrawing(entry: 100, stop: 100, target: 130);

            new RiskRewardCalculator().Calculate(drawing, Bars(5));

            Assert.Equal(0, drawing.RiskRewardRatio);
        }

        // ── MeasureToolCalculator ───────────────────────────────────────────────

        private static DrawingData MeasureDrawing(List<Ohlcv> bars, int i1, double p1, int i2, double p2) => new()
        {
            Type = DrawingType.MeasureTool,
            AnchorDate1 = bars[i1].Date,
            AnchorPrice1 = p1,
            AnchorDate2 = bars[i2].Date,
            AnchorPrice2 = p2,
        };

        [Fact]
        public void Measure_MissingAnchor_YieldsNothing()
        {
            var bars = Bars(10);
            var drawing = MeasureDrawing(bars, 2, 100, 7, 110);
            drawing.AnchorDate2 = null;

            var results = new MeasureToolCalculator().Calculate(drawing, bars);

            Assert.Empty(results);
            Assert.Equal(string.Empty, drawing.MeasureResult);
        }

        [Fact]
        public void Measure_ReportsPriceDistance_Percent_AndBarCount()
        {
            var bars = Bars(10);
            var drawing = MeasureDrawing(bars, 2, 100, 7, 110);

            var results = new MeasureToolCalculator().Calculate(drawing, bars);

            Assert.Equal("10.00 (10.00%), 5 bars", drawing.MeasureResult);
            // The measure line passes exactly through both anchors.
            var line = results["Measure"];
            Assert.Equal(bars.Count, line.Length);
            Assert.Equal(100.0, line[2], 10);
            Assert.Equal(110.0, line[7], 10);
        }

        [Fact]
        public void Measure_DownwardDistance_IsSigned()
        {
            var bars = Bars(10);
            var drawing = MeasureDrawing(bars, 2, 100, 7, 90);

            new MeasureToolCalculator().Calculate(drawing, bars);

            Assert.Equal("-10.00 (-10.00%), 5 bars", drawing.MeasureResult);
        }

        [Fact]
        public void Measure_SubDollarDistance_DoesNotCollapseToZero()
        {
            // The magnitude-aware formatting fence: a SHIB/KAS measure distance used to
            // read "0.00 (…)" — useless to a screen-reader user sizing a position.
            var bars = Bars(10, price: 0.01);
            var drawing = MeasureDrawing(bars, 0, 0.0100, 5, 0.0125);

            new MeasureToolCalculator().Calculate(drawing, bars);

            Assert.StartsWith("0.00250 (25.00%), 5 bars", drawing.MeasureResult);
        }

        [Fact]
        public void Measure_AnchorDateBeyondTheChart_YieldsNothing()
        {
            var bars = Bars(10);
            var drawing = MeasureDrawing(bars, 2, 100, 7, 110);
            drawing.AnchorDate2 = bars[^1].Date.AddDays(1);

            var results = new MeasureToolCalculator().Calculate(drawing, bars);

            Assert.Empty(results);
        }
    }
}
