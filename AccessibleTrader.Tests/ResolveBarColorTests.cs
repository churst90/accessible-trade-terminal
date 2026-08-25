using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Direct tests for the per-bar color-rule resolver (previously only
    /// indirectly covered through renderer smoke tests). The rules decide what a
    /// sighted user SEES for directional bars/histograms, so each condition's
    /// boundary is pinned: AboveZero includes zero, BelowLevel includes the level,
    /// Rising/Falling fall back to "unchanged" when the previous bar is NaN, and
    /// rule order is first-match-wins.
    /// </summary>
    public class ResolveBarColorTests
    {
        private static ComponentConfig Comp(params ColorRule[] rules) => new()
        {
            Name = "hist",
            ColorRules = new List<ColorRule>(rules),
        };

        private static ColorRule Rule(ColorCondition c, string hex = "#00FF00", double level = 0)
            => new() { Condition = c, ColorHex = hex, Level = level };

        private static readonly SKColor Green = SKColor.Parse("#00FF00");

        [Fact]
        public void No_rules_returns_null_so_the_static_paint_is_used()
        {
            var comp = new ComponentConfig { Name = "line" };
            Assert.Null(StandardRenderers.ResolveBarColor(comp, new[] { 1.0 }, 0));
        }

        [Fact]
        public void NaN_value_returns_null()
        {
            var comp = Comp(Rule(ColorCondition.AboveZero));
            Assert.Null(StandardRenderers.ResolveBarColor(comp, new[] { double.NaN }, 0));
        }

        [Theory]
        [InlineData(0.0, true)]   // zero counts as above — matches the histogram convention
        [InlineData(0.5, true)]
        [InlineData(-0.5, false)]
        public void AboveZero_includes_zero(double val, bool matches)
        {
            var comp = Comp(Rule(ColorCondition.AboveZero));
            var col = StandardRenderers.ResolveBarColor(comp, new[] { val }, 0);
            Assert.Equal(matches ? Green : (SKColor?)null, col);
        }

        [Fact]
        public void Rising_and_falling_compare_to_the_previous_bar()
        {
            var comp = Comp(Rule(ColorCondition.Rising));
            var data = new[] { 1.0, 2.0, 1.5 };
            Assert.Equal(Green, StandardRenderers.ResolveBarColor(comp, data, 1)); // 2 > 1
            Assert.Null(StandardRenderers.ResolveBarColor(comp, data, 2));         // 1.5 < 2

            var falling = Comp(Rule(ColorCondition.Falling));
            Assert.Equal(Green, StandardRenderers.ResolveBarColor(falling, data, 2));
        }

        [Fact]
        public void NaN_previous_bar_reads_as_unchanged_not_rising()
        {
            // First bar / gap after NaN: prev falls back to the current value, so
            // neither Rising nor Falling matches — no phantom direction color.
            var data = new[] { double.NaN, 5.0 };
            Assert.Null(StandardRenderers.ResolveBarColor(Comp(Rule(ColorCondition.Rising)), data, 1));
            Assert.Null(StandardRenderers.ResolveBarColor(Comp(Rule(ColorCondition.Falling)), data, 1));
        }

        [Theory]
        [InlineData(70.0, false)]  // exactly at level: AboveLevel is strict…
        [InlineData(70.1, true)]
        public void AboveLevel_is_strict(double val, bool matches)
        {
            var comp = Comp(Rule(ColorCondition.AboveLevel, level: 70));
            var col = StandardRenderers.ResolveBarColor(comp, new[] { val }, 0);
            Assert.Equal(matches ? Green : (SKColor?)null, col);
        }

        [Theory]
        [InlineData(30.0, true)]   // …and BelowLevel is inclusive, so the pair tiles the axis
        [InlineData(29.9, true)]
        [InlineData(30.1, false)]
        public void BelowLevel_is_inclusive(double val, bool matches)
        {
            var comp = Comp(Rule(ColorCondition.BelowLevel, level: 30));
            var col = StandardRenderers.ResolveBarColor(comp, new[] { val }, 0);
            Assert.Equal(matches ? Green : (SKColor?)null, col);
        }

        [Fact]
        public void First_matching_rule_wins()
        {
            var comp = Comp(
                Rule(ColorCondition.AboveZero, "#FF0000"),
                Rule(ColorCondition.Rising, "#0000FF"));
            var data = new[] { 1.0, 2.0 }; // index 1 matches BOTH rules
            Assert.Equal(SKColor.Parse("#FF0000"), StandardRenderers.ResolveBarColor(comp, data, 1));
        }

        [Fact]
        public void Unparseable_hex_is_skipped_and_later_rules_still_apply()
        {
            var comp = Comp(
                Rule(ColorCondition.AboveZero, "not-a-color"),
                Rule(ColorCondition.AboveZero, "#00FF00"));
            Assert.Equal(Green, StandardRenderers.ResolveBarColor(comp, new[] { 1.0 }, 0));
        }

        [Fact]
        public void Index_past_the_data_returns_null()
        {
            var comp = Comp(Rule(ColorCondition.AboveZero));
            Assert.Null(StandardRenderers.ResolveBarColor(comp, new[] { 1.0 }, 5));
        }
    }
}
