using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The 2026-07 seeds: the trend-following benchmark (and the COT-gated Cipher
    /// reversal, until it was retired on 2026-10-31). Pins the condition wiring — especially the signal-descriptor
    /// strings, which fail SILENTLY at evaluation time if they drift from the
    /// indicator component names.
    /// </summary>
    public class TrendAndCotSeedTests
    {
        private static StrategySpec Seed(string id) =>
            StrategyCatalogue.AllSpecs().Single(s => s.Id == id);

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
            var spec = Seed(StrategyCatalogue.LongTrendBaselineId);

            var leaf = Assert.Single(Leaves(spec.Conditions));
            Assert.Equal("REGIME.AboveSma200", leaf.SignalDescriptorId);
            Assert.Equal(LeafOperator.CrossesAbove, leaf.Operator);
            Assert.Equal(0.0, leaf.Value);

            // The benchmark rides trends: trail after TP1, no premature ladder.
            Assert.Equal(StopAdjustOnTp1.TrailByAtr, spec.Risk.StopAdjust);
            Assert.Single(spec.Risk.TpLadder);
            Assert.False(spec.IsAutoActivate);
        }

        // The COT-gated v23c seed's test went with it: retired 2026-10-31 with Cipher B's Anchor
        // Wave, its required regime gate. Its verdict is in CatalogueProvenance's retired list.

        // The v24 Cycle Low Reversal spec's two tests went with it: retired 2026-10-22 with Loukas
        // Cycle Detection, its entry event. Its verdict is in CatalogueProvenance's retired list.
    }
}
