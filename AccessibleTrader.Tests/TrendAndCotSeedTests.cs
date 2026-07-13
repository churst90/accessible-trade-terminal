using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Core.Services.Strategies;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07 seeds: the trend-following benchmark and the COT-gated Cipher
    /// reversal. Pins the condition wiring — especially the signal-descriptor
    /// strings, which fail SILENTLY at evaluation time if they drift from the
    /// indicator component names.
    /// </summary>
    public class TrendAndCotSeedTests
    {
        private static StrategySpec Seed(string id) =>
            BuiltInStrategySeeds.GetAllSeeds().Single(s => s.Id == id);

        private static System.Collections.Generic.IEnumerable<ConditionLeaf> Leaves(ConditionNode node)
        {
            if (node is ConditionLeaf leaf) { yield return leaf; yield break; }
            if (node is ConditionGroup group)
                foreach (var child in group.Children)
                    foreach (var l in Leaves(child))
                        yield return l;
        }

        [Fact]
        public void TrendBaseline_IsAFaberCross_WithTrailingExit()
        {
            var spec = Seed(BuiltInStrategySeeds.LongTrendBaselineId);

            var leaf = Assert.Single(Leaves(spec.Conditions));
            Assert.Equal("REGIME.AboveSma200", leaf.SignalDescriptorId);
            Assert.Equal(LeafOperator.CrossesAbove, leaf.Operator);
            Assert.Equal(0.0, leaf.Value);

            // The benchmark rides trends: trail after TP1, no premature ladder.
            Assert.Equal(StopAdjustOnTp1.TrailByAtr, spec.Risk.StopAdjust);
            Assert.Single(spec.Risk.TpLadder);
            Assert.False(spec.IsAutoActivate);
        }

        [Fact]
        public void CotGatedSeed_ReferencesTheRealZScoreComponent()
        {
            var spec = Seed(BuiltInStrategySeeds.LongV23cCipherBCotId);
            var leaves = Leaves(spec.Conditions).ToList();

            // The gate must reference the component name exactly as the indicator
            // registers it — a rename on either side silently kills the strategy.
            var cotLeaf = Assert.Single(leaves, l => l.SignalDescriptorId.StartsWith("COT_POSITIONING."));
            Assert.Equal($"COT_POSITIONING.{CotPositioningProvider.CompZScore}", cotLeaf.SignalDescriptorId);
            Assert.Equal(LeafOperator.LessThan, cotLeaf.Operator);
            Assert.Equal(1.5, cotLeaf.Value); // matches the indicator's extreme threshold

            // Still a v23-family reversal: trigger trio + anchor gate present.
            Assert.Contains(leaves, l => l.SignalDescriptorId == "CIPHER_B.Oversold Crossover");
            Assert.Contains(leaves, l => l.SignalDescriptorId == "CIPHER_B.Anchor Wave");
        }

        [Fact]
        public void CotZScoreDescriptor_ResolvesAgainstTheIndicatorMetadata()
        {
            // End-to-end name check: the component the seed references must exist
            // in the indicator's registered metadata (what SignalCatalog scans).
            var provider = new CotPositioningProvider(
                NSubstitute.Substitute.For<ICrossSeriesCache>());
            var components = provider.GetIndicators().Single().Components.Select(c => c.Name).ToList();

            Assert.Contains(CotPositioningProvider.CompZScore, components);
        }
    }
}
