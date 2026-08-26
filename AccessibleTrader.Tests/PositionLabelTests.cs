using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Sdk.Plugins;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What a position row says about itself.
    ///
    /// <para>
    /// The Positions table used to lead with a bare symbol and put leverage in column
    /// eight, with the margin mode nowhere at all — so the three facts that decide how
    /// far a position can go wrong were one missing, one adjacent and one across the
    /// table. Direction was left to the sign of the quantity, and a leading minus is
    /// exactly the character screen readers drop at default punctuation settings, which
    /// turns a short into a long silently.
    /// </para>
    /// </summary>
    public class PositionLabelTests
    {
        private static Position P(double qty, MarginMode mode = MarginMode.None, double leverage = 1.0) =>
            new("BTCUSDT", qty, 100, 100 * qty, 0, leverage, 0, mode);

        [Theory]
        [InlineData(MarginMode.Isolated, 1.0, "BTCUSDT isolated 1x")]
        [InlineData(MarginMode.Cross,    1.0, "BTCUSDT cross 1x")]
        [InlineData(MarginMode.Cross,    3.0, "BTCUSDT cross 3x")]
        [InlineData(MarginMode.Isolated, 12.5, "BTCUSDT isolated 12.5x")]
        public void A_margin_position_names_its_mode_and_its_leverage(
            MarginMode mode, double leverage, string expected) =>
            Assert.Equal(expected, PositionLabel.Instrument(P(-1, mode, leverage)));

        [Fact]
        public void A_spot_holding_claims_neither()
        {
            // Nothing is borrowed and no collateral is held either way, so "cross 1x"
            // over a spot holding would describe a liquidation it cannot have.
            Assert.Equal("BTCUSDT", PositionLabel.Instrument(P(1)));
        }

        [Fact]
        public void Leverage_alone_is_still_worth_saying()
        {
            // A venue that reports leverage without a margin mode — several do — must
            // not lose the multiplier just because the mode is absent.
            Assert.Equal("BTCUSDT 5x", PositionLabel.Instrument(P(1, MarginMode.None, 5.0)));
        }

        [Fact]
        public void The_multiplier_is_a_plain_x_not_a_multiplication_sign()
        {
            // U+00D7 is announced as "times", as "ex", or skipped entirely depending on
            // the screen reader and its punctuation level — the same reason the Cancel
            // button stopped being ✕. "one x" is how a trader says it aloud anyway.
            Assert.DoesNotContain("×", PositionLabel.Instrument(P(-1, MarginMode.Cross, 3)));
        }

        [Theory]
        [InlineData(1.0, "Long")]
        [InlineData(-0.6875, "Short")]
        [InlineData(0.0, "Flat")]
        public void Direction_is_a_word_not_a_minus_sign(double qty, string expected) =>
            Assert.Equal(expected, PositionLabel.Direction(P(qty)));
    }
}
