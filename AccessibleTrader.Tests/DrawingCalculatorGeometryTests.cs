using AccessibleTrader.Core.Services.Drawing.Calculators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Tier 3 geometry coverage for the main drawing calculators. These classes
    /// produce the numeric arrays that <c>StandardRenderers</c> paints on top of
    /// price — a subtle off-by-one or wrong-level-constant here ships a chart
    /// that silently misleads the user. Coverage: TrendLine linear fit (slope +
    /// intercept), Channel parallel lines (baseline + offset upper + mid median),
    /// FibRetracement level constants (0 / 23.6 / 38.2 / 50 / 61.8 / 78.6 / 100),
    /// FibExtension level constants (adds 161.8 / 261.8), Rectangle bounds (top =
    /// max anchor price, bottom = min anchor price, NaN outside date range),
    /// HorizontalLine constant fill, missing-anchor early returns.
    /// </summary>
    public class DrawingCalculatorGeometryTests
    {
        // ── TrendLineCalculator ───────────────────────────────────────────────

        [Fact]
        public void TrendLine_LinearFit_ProducesExpectedValueAtEveryIndex()
        {
            // Two anchors: index 0 price=100, index 4 price=200. Slope = 25/bar,
            // intercept = 100. So results[0..4] = 100, 125, 150, 175, 200.
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1  = bars[0].Date, AnchorPrice1 = 100,
                AnchorDate2  = bars[4].Date, AnchorPrice2 = 200,
            };
            var calc = new TrendLineCalculator();
            var line = calc.Calculate(drawing, bars)["Line"];

            Assert.Equal(5, line.Length);
            Assert.Equal(100, line[0], precision: 6);
            Assert.Equal(125, line[1], precision: 6);
            Assert.Equal(150, line[2], precision: 6);
            Assert.Equal(175, line[3], precision: 6);
            Assert.Equal(200, line[4], precision: 6);
        }

        /// <summary>
        /// Extrapolation past the anchors is what the EXTEND FLAGS buy, and the slope it
        /// extrapolates on is the anchors'.
        ///
        /// <para>This test asserted the same four values with both flags left false, back when
        /// <c>CalculateLinearPoints</c> accepted <c>extL</c>/<c>extR</c> and read neither — so
        /// it was green on a line drawn across bars it had never been anchored near. It now
        /// asks for the extension explicitly, which is the only condition under which those
        /// numbers are the right answer.</para>
        /// </summary>
        [Fact]
        public void TrendLine_ExtrapolatesBeyondAnchorRange_WhenBothEndsAreExtended()
        {
            // Anchors at index 1 (price 100) and index 3 (price 200). Slope=50.
            // Indices 0 and 4 must be extrapolated: line[0]=50, line[4]=250.
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = bars[1].Date, AnchorPrice1 = 100,
                AnchorDate2 = bars[3].Date, AnchorPrice2 = 200,
                ExtendLeft = true, ExtendRight = true,
            };
            var line = new TrendLineCalculator().Calculate(drawing, bars)["Line"];
            Assert.Equal( 50, line[0], precision: 6);
            Assert.Equal(100, line[1], precision: 6);
            Assert.Equal(200, line[3], precision: 6);
            Assert.Equal(250, line[4], precision: 6);
        }

        /// <summary>The same drawing with the flags off: the bars outside the span carry no
        /// value at all. Paired with the test above deliberately — one alone cannot tell
        /// "reads the flags" from "extends by default" or from "never extends".</summary>
        [Fact]
        public void TrendLine_DoesNotExtrapolate_WhenNeitherEndIsExtended()
        {
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = bars[1].Date, AnchorPrice1 = 100,
                AnchorDate2 = bars[3].Date, AnchorPrice2 = 200,
            };
            var line = new TrendLineCalculator().Calculate(drawing, bars)["Line"];
            Assert.True(double.IsNaN(line[0]));
            Assert.Equal(100, line[1], precision: 6);
            Assert.Equal(150, line[2], precision: 6);
            Assert.Equal(200, line[3], precision: 6);
            Assert.True(double.IsNaN(line[4]));
        }

        [Fact]
        public void TrendLine_MissingAnchor_ReturnsEmpty()
        {
            // Missing anchor2 → early return with empty dictionary. No crash, no
            // partial line leaked into the chart.
            var bars = MakeBars(3);
            var drawing = new DrawingData
            {
                Type = DrawingType.TrendLine,
                AnchorDate1 = bars[0].Date, AnchorPrice1 = 100,
                // AnchorDate2/AnchorPrice2 deliberately unset.
            };
            var result = new TrendLineCalculator().Calculate(drawing, bars);
            Assert.Empty(result);
        }

        // ── ChannelCalculator ─────────────────────────────────────────────────

        [Fact]
        public void Channel_ProducesBaseUpperMedian_AtConfiguredWidth()
        {
            // Baseline slope 25/bar from (idx0, 100) to (idx4, 200). Width = 40.
            // Upper = base + 40; Median = base + 20.
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.Channel,
                AnchorDate1 = bars[0].Date, AnchorPrice1 = 100,
                AnchorDate2 = bars[4].Date, AnchorPrice2 = 200,
                ChannelWidth = 40,
            };
            var result = new ChannelCalculator().Calculate(drawing, bars);

            Assert.Equal(3, result.Count);
            var lower  = result["Lower Bound"];
            var upper  = result["Upper Bound"];
            var median = result["Median"];

            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(lower[i] + 40, upper[i],  precision: 6);
                Assert.Equal(lower[i] + 20, median[i], precision: 6);
            }
        }

        [Fact]
        public void Channel_ZeroWidth_DefaultsTo5PercentOfFirstAnchor()
        {
            // Width omitted (defaults to 0) → calculator falls back to
            // AnchorPrice1 * 0.05. Protects against a zero-width channel rendering
            // as a visible single line when the user forgot to set the third anchor.
            var bars = MakeBars(3);
            var drawing = new DrawingData
            {
                Type = DrawingType.Channel,
                AnchorDate1 = bars[0].Date, AnchorPrice1 = 100,
                AnchorDate2 = bars[2].Date, AnchorPrice2 = 120,
                ChannelWidth = 0,
            };
            var result = new ChannelCalculator().Calculate(drawing, bars);
            var lower  = result["Lower Bound"];
            var upper  = result["Upper Bound"];
            // Expected width = 100 * 0.05 = 5.
            Assert.Equal(lower[0] + 5, upper[0], precision: 6);
        }

        // ── FibRetracementCalculator ──────────────────────────────────────────

        [Fact]
        public void FibRetracement_EmitsStandardLevels_FromP1Downward()
        {
            // p1=200, p2=100 → diff=100. Levels: 0%=200, 23.6%=176.4, 38.2%=161.8,
            // 50%=150, 61.8%=138.2, 78.6%=121.4, 100%=100.
            var bars = MakeBars(3);
            var drawing = new DrawingData
            {
                Type = DrawingType.FibRetracement,
                AnchorPrice1 = 200,
                AnchorPrice2 = 100,
            };
            var result = new FibRetracementCalculator().Calculate(drawing, bars);

            Assert.Equal(200.0, result["0.0% Level"][0],   precision: 6);
            Assert.Equal(176.4, result["23.6% Level"][0],  precision: 3);
            Assert.Equal(161.8, result["38.2% Level"][0],  precision: 3);
            Assert.Equal(150.0, result["50.0% Level"][0],  precision: 6);
            Assert.Equal(138.2, result["61.8% Level"][0],  precision: 3);
            Assert.Equal(121.4, result["78.6% Level"][0],  precision: 3);
            Assert.Equal(100.0, result["100.0% Level"][0], precision: 6);
        }

        [Fact]
        public void FibRetracement_InvertedAnchors_LevelsFlipDirection()
        {
            // p1 < p2 → diff is negative → 50% level sits above p1, i.e. levels
            // climb upward from p1. Makes the drawing orientation-agnostic.
            var bars = MakeBars(3);
            var drawing = new DrawingData
            {
                Type = DrawingType.FibRetracement,
                AnchorPrice1 = 100,
                AnchorPrice2 = 200,  // upswing
            };
            var result = new FibRetracementCalculator().Calculate(drawing, bars);
            Assert.Equal(100.0, result["0.0% Level"][0],   precision: 6);
            Assert.Equal(150.0, result["50.0% Level"][0],  precision: 6);
            Assert.Equal(200.0, result["100.0% Level"][0], precision: 6);
        }

        // ── FibExtensionCalculator ────────────────────────────────────────────

        [Fact]
        public void FibExtension_AddsMoveScaledFromP3_IncludingExtendedLevels()
        {
            // move = p2 - p1 = 100. p3 = 150. Levels include 161.8% and 261.8%.
            // 0%  = 150 + 100*0.000 = 150
            // 50% = 150 + 100*0.500 = 200
            // 100%= 150 + 100*1.000 = 250
            // 161.8% = 150 + 100*1.618 = 311.8
            // 261.8% = 150 + 100*2.618 = 411.8
            var bars = MakeBars(3);
            var drawing = new DrawingData
            {
                Type = DrawingType.FibExtension,
                AnchorPrice1 = 100,
                AnchorPrice2 = 200,
                AnchorPrice3 = 150,
            };
            var result = new FibExtensionCalculator().Calculate(drawing, bars);

            Assert.Equal(150.0, result["0.0% Ext"][0],   precision: 6);
            Assert.Equal(200.0, result["50.0% Ext"][0],  precision: 6);
            Assert.Equal(250.0, result["100.0% Ext"][0], precision: 6);
            Assert.Equal(311.8, result["161.8% Ext"][0], precision: 3);
            Assert.Equal(411.8, result["261.8% Ext"][0], precision: 3);
        }

        // ── RectangleCalculator ───────────────────────────────────────────────

        [Fact]
        public void Rectangle_NormalisesCorners_AndFillsOnlyWithinDateRange()
        {
            // Corner anchors: (idx 1, price 150) and (idx 3, price 100). Rectangle
            // must span idx 1..3 with top=150 and bottom=100 regardless of which
            // anchor is higher. Bars outside the range stay NaN.
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.Rectangle,
                AnchorDate1 = bars[1].Date, AnchorPrice1 = 150,
                AnchorDate2 = bars[3].Date, AnchorPrice2 = 100,
            };
            var result = new RectangleCalculator().Calculate(drawing, bars);
            var top    = result["Top"];
            var bottom = result["Bottom"];

            Assert.True(double.IsNaN(top[0]));
            Assert.True(double.IsNaN(bottom[0]));
            for (int i = 1; i <= 3; i++)
            {
                Assert.Equal(150, top[i],    precision: 6);
                Assert.Equal(100, bottom[i], precision: 6);
            }
            Assert.True(double.IsNaN(top[4]));
            Assert.True(double.IsNaN(bottom[4]));
        }

        [Fact]
        public void Rectangle_ReversedDates_SwapsStartAndEnd()
        {
            // Date2 earlier than Date1 → calculator swaps so start <= end. No
            // empty fill; no crash on reversed user draws.
            var bars = MakeBars(5);
            var drawing = new DrawingData
            {
                Type = DrawingType.Rectangle,
                AnchorDate1 = bars[3].Date, AnchorPrice1 = 100,
                AnchorDate2 = bars[1].Date, AnchorPrice2 = 200,
            };
            var result = new RectangleCalculator().Calculate(drawing, bars);
            var top = result["Top"];
            Assert.True(double.IsNaN(top[0]));
            for (int i = 1; i <= 3; i++)
                Assert.Equal(200, top[i], precision: 6);
            Assert.True(double.IsNaN(top[4]));
        }

        // ── HorizontalLineCalculator ──────────────────────────────────────────

        [Fact]
        public void HorizontalLine_FillsEveryBarAtConstantPrice()
        {
            var bars = MakeBars(4);
            var drawing = new DrawingData
            {
                Type = DrawingType.HorizontalLine,
                AnchorPrice1 = 123.45,
            };
            var result = new HorizontalLineCalculator().Calculate(drawing, bars);
            Assert.All(result["Line"], v => Assert.Equal(123.45, v, precision: 6));
        }

        [Fact]
        public void HorizontalLine_MissingAnchor_ReturnsEmpty()
        {
            var bars = MakeBars(3);
            var drawing = new DrawingData { Type = DrawingType.HorizontalLine };
            var result = new HorizontalLineCalculator().Calculate(drawing, bars);
            Assert.Empty(result);
        }

        // ── Fixtures ──────────────────────────────────────────────────────────

        private static List<Ohlcv> MakeBars(int count)
        {
            var t0 = new DateTime(2026, 4, 23, 9, 30, 0, DateTimeKind.Utc);
            var list = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                list.Add(new Ohlcv(t0.AddMinutes(i), 100, 100, 100, 100, 0));
            return list;
        }
    }
}
