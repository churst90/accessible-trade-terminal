using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Strategies;
using AccessibleTrader.StrategyLab.Catalogue;

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

        [Fact]
        public void CotGatedSeed_ReferencesTheRealZScoreComponent()
        {
            var spec = Seed(StrategyCatalogue.LongV23cCipherBCotId);
            var leaves = Leaves(spec.Conditions).ToList();

            // The gate must reference the component name exactly as the indicator
            // registers it — a rename on either side silently kills the strategy.
            var cotLeaf = Assert.Single(leaves, l => l.SignalDescriptorId.StartsWith("COT_POSITIONING."));
            Assert.Equal($"COT_POSITIONING.{CotPositioningProvider.CompZScore}", cotLeaf.SignalDescriptorId);
            Assert.Equal(LeafOperator.LessThan, cotLeaf.Operator);
            Assert.Equal(1.5, cotLeaf.Value); // matches the indicator's extreme threshold

            // Still a v23-family reversal: trigger trio + anchor gate present, plus
            // the Faber bull-regime gate the 2026-07 battery validated for this combo.
            Assert.Contains(leaves, l => l.SignalDescriptorId == "CIPHER_B.Oversold Crossover");
            Assert.Contains(leaves, l => l.SignalDescriptorId == "CIPHER_B.Anchor Wave");
            Assert.Contains(leaves, l => l.SignalDescriptorId == "REGIME.AboveSma200"
                                         && l.Operator == LeafOperator.GreaterThan);
        }

        [Fact]
        public void V24CycleLowReversal_WiresTheValidatedDesign()
        {
            // Pins the 2026-07-17 lab-validated configuration. Every one of these
            // was iterated in the lab: the DCL-within-2 entry event, the widened
            // 8-bar trigger windows (confirmation lag), the ABSENCE of the anchor
            // gate (it deleted the good half), and the ATR stop (swing-low stops
            // died to cycle-low retests).
            var spec = Seed(StrategyCatalogue.LongV24CycleLowReversalId);
            var leaves = Leaves(spec.Conditions).ToList();

            var dcl = Assert.Single(leaves, l => l.SignalDescriptorId.StartsWith("LOUKAS_CYCLES."));
            Assert.Equal($"LOUKAS_CYCLES.{LoukasCyclesProvider.CompDclConfirmed}", dcl.SignalDescriptorId);
            Assert.Equal(LeafOperator.FiredWithin, dcl.Operator);
            Assert.Equal(2, dcl.WithinNBars);

            // The three v23 triggers, all widened to 8 bars for the confirmation lag.
            foreach (var trig in leaves.Where(l => l.SignalDescriptorId.StartsWith("CIPHER_B.")))
            {
                Assert.Equal(LeafOperator.FiredWithin, trig.Operator);
                Assert.Equal(8, trig.WithinNBars);
            }
            Assert.Equal(3, leaves.Count(l => l.SignalDescriptorId.StartsWith("CIPHER_B.")));

            // The anchor-depth gate must NOT be present — it deleted H1 in the lab.
            Assert.DoesNotContain(leaves, l => l.SignalDescriptorId.Contains("Anchor Wave"));

            // ATR stop (not swing-low) + trail after TP1: the exit design the lab kept.
            Assert.Equal(StopSourceKind.AtrMultiple, spec.Risk.Stop.Kind);
            Assert.Equal(StopAdjustOnTp1.TrailByAtr, spec.Risk.StopAdjust);
            Assert.Equal(AccessibleTrader.Sdk.Plugins.OrderSide.Buy, spec.Side); // never short cycle signals
            Assert.False(spec.IsAutoActivate);
        }

        [Fact]
        public void V24DclDescriptor_ResolvesAgainstTheIndicatorMetadata()
        {
            var provider = new LoukasCyclesProvider();
            var components = provider.GetIndicators().SelectMany(m => m.Components).Select(c => c.Name).ToList();
            Assert.Contains(LoukasCyclesProvider.CompDclConfirmed, components);
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
