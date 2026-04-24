using AccessibleTrader.Core.PineScript;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pin the warning surface for Tier-3 Pine features that aren't yet wired
    /// to AccessibleTrader's DrawingService / TradeSignal / ColorRule pipeline.
    /// Goal: when a user pastes a TradingView strategy that uses these features,
    /// the transpiler succeeds at the indicator-shaped subset and reports each
    /// dropped call site as a warning — never a silent drop. This is the safety
    /// gate until the ICustomStrategy host contract ships in Phase 10-D.2.
    /// </summary>
    public class PineTranspilerWarningsTests
    {
        private static TranspileResult Transpile(string pine) =>
            new PineTranspiler().Transpile(pine);

        [Fact]
        public void LineNew_EmitsWarning_PerCallSite()
        {
            const string pine = @"
//@version=5
indicator('LineTest', overlay=true)
plot(close)
if barstate.islast
    line.new(bar_index, low, bar_index + 5, high)
    line.new(bar_index, open, bar_index + 3, close)
";
            var result = Transpile(pine);

            Assert.True(result.Success);
            int hits = 0;
            foreach (var w in result.Warnings)
                if (w.Contains("line.new()")) hits++;
            Assert.Equal(2, hits);
        }

        [Fact]
        public void LabelNew_EmitsWarning_PerCallSite()
        {
            const string pine = @"
//@version=5
indicator('LabelTest', overlay=true)
plot(close)
label.new(bar_index, high, 'Top')
";
            var result = Transpile(pine);

            Assert.True(result.Success);
            Assert.Contains(result.Warnings, w => w.Contains("label.new()"));
        }

        [Fact]
        public void StrategyEntry_EmitsWarning_AndPointsToComposer()
        {
            const string pine = @"
//@version=5
strategy('LongOnly', overlay=true)
plot(close)
if close > open
    strategy.entry('Long', strategy.long)
";
            var result = Transpile(pine);

            Assert.Contains(result.Warnings, w => w.Contains("strategy.entry()"));
            Assert.Contains(result.Warnings, w => w.Contains("StrategyComposer"));
        }

        [Fact]
        public void StrategyExit_EmitsWarning()
        {
            const string pine = @"
//@version=5
strategy('ExitTest', overlay=true)
plot(close)
strategy.exit('Stop', from_entry='Long', stop=low)
";
            var result = Transpile(pine);

            Assert.Contains(result.Warnings, w => w.Contains("strategy.exit()"));
        }

        [Fact]
        public void StrategyClose_EmitsWarning()
        {
            const string pine = @"
//@version=5
strategy('CloseTest')
plot(close)
strategy.close('Long')
";
            var result = Transpile(pine);

            Assert.Contains(result.Warnings, w => w.Contains("strategy.close()"));
        }

        [Fact]
        public void ColorNew_EmitsWarning()
        {
            const string pine = @"
//@version=5
indicator('ColorTest', overlay=true)
c = close > open ? color.new(color.green, 60) : color.new(color.red, 60)
plot(close, color=c)
";
            var result = Transpile(pine);

            Assert.Contains(result.Warnings, w => w.Contains("color.new()"));
            Assert.Contains(result.Warnings, w => w.Contains("ColorRule"));
        }

        [Fact]
        public void NoSurprise_OrdinaryPineHasNoTier3Warnings()
        {
            // A plain SMA indicator should transpile cleanly with zero Tier-3
            // warnings — the gate only triggers when the user opted into the
            // drawing/strategy/color features.
            const string pine = @"
//@version=5
indicator('Plain SMA', overlay=true)
length = input.int(20, 'Length')
sma = ta.sma(close, length)
plot(sma, 'SMA')
";
            var result = Transpile(pine);

            Assert.True(result.Success);
            foreach (var w in result.Warnings)
            {
                Assert.DoesNotContain("line.new()", w);
                Assert.DoesNotContain("label.new()", w);
                Assert.DoesNotContain("strategy.", w);
                Assert.DoesNotContain("color.new()", w);
            }
        }

        [Fact]
        public void Combined_StrategyAndDrawing_AccumulatesAllWarnings()
        {
            // Realistic TradingView paste: strategy with stops + visual labels.
            // Every dropped call site must surface at least one warning so the
            // user's UI shows them what didn't transpile.
            const string pine = @"
//@version=5
strategy('Mixed', overlay=true)
plot(close)
if close > open
    strategy.entry('L', strategy.long)
    label.new(bar_index, high, 'Long')
strategy.exit('S', stop=low)
";
            var result = Transpile(pine);

            Assert.Contains(result.Warnings, w => w.Contains("strategy.entry()"));
            Assert.Contains(result.Warnings, w => w.Contains("strategy.exit()"));
            Assert.Contains(result.Warnings, w => w.Contains("label.new()"));
        }
    }
}
