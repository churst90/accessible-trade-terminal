using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The builder's read-back: the sentence a blind user hears to verify a strategy BY EAR before
    /// saving it (Summary and Export tab). It had no test at all until mutation campaign A2l
    /// (2026-09-24), where "Long" spoken for a short setup survived 8,088 tests. The side and the
    /// risk per trade are the two facts a trader must not hear wrong.
    /// </summary>
    public class StrategySpecNarratorTests
    {
        private static readonly StrategySpecNarrator Narrator = new(id => id == "RSI.RSI" ? "RSI — RSI" : "(unset)");

        private static EditableStrategySpec Spec(OrderSide side) => new()
        {
            Name = "Oversold bounce",
            Side = side,
            Root = new EditableConditionNode { SignalDescriptorId = "RSI.RSI", Operator = LeafOperator.LessThan, Value = 30 },
            SizingMode = SizingMode.FixedRiskPercent,
            RiskPercent = 0.005,
        };

        [Fact]
        public void A_short_setup_is_narrated_as_short_and_a_long_one_as_long()
        {
            Assert.StartsWith("Short setup. Oversold bounce.", Narrator.Narrate(Spec(OrderSide.Sell)));
            Assert.StartsWith("Long setup. Oversold bounce.", Narrator.Narrate(Spec(OrderSide.Buy)));
        }

        [Fact]
        public void The_risk_per_trade_is_spoken_as_a_percent_of_equity()
        {
            // RiskPercent is a fraction (0.005 = half a percent); the sentence says percent.
            string text = Narrator.Narrate(Spec(OrderSide.Buy));
            Assert.Contains("Risking 0.50 percent of equity per trade.", text);
        }

        [Fact]
        public void The_condition_reads_its_label_operator_and_threshold()
        {
            string text = Narrator.Narrate(Spec(OrderSide.Buy));
            Assert.Contains("Conditions: RSI — RSI LessThan 30.00", text);
        }
    }
}
