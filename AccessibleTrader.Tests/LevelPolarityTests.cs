using AccessibleTrader.Core.Services.Analysis;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The chokepoint itself. Four lines of production code, and the reason they are worth a test
    /// file is that the two bugs they replace were each one line as well — the risk here is never
    /// complexity, it is that the rule quietly stops being the rule.
    /// </summary>
    public class LevelPolarityTests
    {
        [Theory]
        [InlineData(105.2, 105.0, true)]    // ceiling
        [InlineData(104.8, 105.0, false)]   // floor
        [InlineData(0.0364, 0.0363, true)]  // magnitude is irrelevant — sub-dollar assets included
        [InlineData(0.0362, 0.0363, false)]
        [InlineData(-1.0, -2.0, true)]      // and so is sign; some series live below zero
        public void ALevelIsResistanceIfItSitsAtOrAboveTheReferencePrice(
            double level, double price, bool expected)
            => Assert.Equal(expected, LevelPolarity.IsResistance(level, price));

        [Fact]
        public void ALevelExactlyOnThePriceIsCalledResistance()
        {
            // The tie has to go somewhere. Both sites that predate the chokepoint resolved it this
            // way — the drawn-level provider emitted Resistance for `!(price < currentPrice)` and
            // the zone announcement used `zoneVal >= bar.Close` — so keeping it means the
            // chokepoint changed no case that was previously right.
            Assert.True(LevelPolarity.IsResistance(105.0, 105.0));
        }

        [Fact]
        public void ANaNOnEitherSideIsNotAClaimWorthMaking()
        {
            // Every comparison against NaN is false, so this falls out of the implementation rather
            // than being handled. It is pinned because the alternative — a caller reading "false"
            // as a confident "support" — is the failure this whole chokepoint exists to prevent,
            // and because callers are expected to guard for NaN before they get here.
            Assert.False(LevelPolarity.IsResistance(double.NaN, 105.0));
            Assert.False(LevelPolarity.IsResistance(105.0, double.NaN));
        }

        [Theory]
        [InlineData(105.2, 105.0, "resistance")]
        [InlineData(104.8, 105.0, "support")]
        public void TheSpokenWordMatchesTheClassification(double level, double price, string expected)
            => Assert.Equal(expected, LevelPolarity.Word(level, price));
    }
}
