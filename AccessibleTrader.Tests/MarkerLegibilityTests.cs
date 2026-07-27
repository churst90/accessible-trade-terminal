using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;
using SkiaSharp;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Can a reader tell two indicators' marks apart when both are on one chart?
    ///
    /// <para>
    /// Market Structure and Value Deviation shipped a week apart and were each checked alone. On a
    /// chart carrying both, Swing High was a red down-triangle at 7px and Resistance tier 1-2 was
    /// a red down-triangle at 7px — a shade apart, same glyph, same size. Same story for the green
    /// up-triangles. Nothing in the code was wrong; the two were simply never looked at together.
    /// </para>
    ///
    /// <para>
    /// Market Structure now owns the angular family (square, cross) and Value Deviation keeps the
    /// graded triangle → dot → diamond ladder, where the shape carries the tier and so cannot move.
    /// </para>
    /// </summary>
    public class MarkerLegibilityTests
    {
        private static IEnumerable<IndicatorComponentMetadata> Components(IIndicatorProvider p) =>
            p.GetIndicators().SelectMany(i => i.Components);

        private static bool IsMarker(ComponentDisplayType t) =>
            AccessibleTrader.Core.Services.ChartRenderer.IsMarker(t);

        /// <summary>
        /// Squared Euclidean distance in RGB. Crude, but enough to separate "different hue" from
        /// "a shade apart" — #EF5350 vs #EF9A9A, the pair that caused this, is ~13k.
        /// </summary>
        private static int ColorDistanceSq(string a, string b)
        {
            SKColor.TryParse(a, out var ca);
            SKColor.TryParse(b, out var cb);
            int dr = ca.Red - cb.Red, dg = ca.Green - cb.Green, db = ca.Blue - cb.Blue;
            return dr * dr + dg * dg + db * db;
        }

        [Fact]
        public void MarketStructure_and_ValueDeviation_share_no_marker_shape_at_a_similar_colour()
        {
            // Below this the two swatches read as the same colour at glyph size. #EF5350 vs
            // #EF9A9A scored 13,034 and was plainly indistinguishable on the chart.
            const int TooClose = 30_000;

            var structure = Components(new SwingStructureProvider()).Where(c => IsMarker(c.DisplayType)).ToList();
            var value     = Components(new ValueDeviationProvider()).Where(c => IsMarker(c.DisplayType)).ToList();

            Assert.NotEmpty(structure);
            Assert.NotEmpty(value);

            var clashes = new List<string>();
            foreach (var s in structure)
                foreach (var v in value)
                {
                    if (s.DisplayType != v.DisplayType) continue;
                    int d = ColorDistanceSq(s.DefaultColorHex ?? "#000000", v.DefaultColorHex ?? "#000000");
                    if (d < TooClose)
                        clashes.Add($"{s.DisplayName} ({s.DefaultColorHex}) vs {v.DisplayName} ({v.DefaultColorHex}) " +
                                    $"— both {s.DisplayType}, colour distance {d}");
                }

            Assert.True(clashes.Count == 0,
                "Indistinguishable marks across the two structure indicators:\n  " + string.Join("\n  ", clashes));
        }

        [Fact]
        public void ValueDeviation_keeps_a_distinct_shape_per_tier_because_the_shape_IS_the_tier()
        {
            // Shallow → triangle, mid → dot, deep → diamond, on each side. Collapsing any two
            // would erase the one thing the marker carries beyond "a zone formed here".
            var comps = Components(new ValueDeviationProvider()).ToDictionary(c => c.Name);

            var support = new[]
            {
                comps[ValueDeviationProvider.CompSupportShallow].DisplayType,
                comps[ValueDeviationProvider.CompSupportMid].DisplayType,
                comps[ValueDeviationProvider.CompSupportDeep].DisplayType,
            };
            var resistance = new[]
            {
                comps[ValueDeviationProvider.CompResistShallow].DisplayType,
                comps[ValueDeviationProvider.CompResistMid].DisplayType,
                comps[ValueDeviationProvider.CompResistDeep].DisplayType,
            };

            Assert.Equal(3, support.Distinct().Count());
            Assert.Equal(3, resistance.Distinct().Count());

            // The two sides are also told apart by triangle DIRECTION at the shallow tier, so a
            // support and a resistance mark never share a glyph.
            Assert.Equal(ComponentDisplayType.TriangleUp, support[0]);
            Assert.Equal(ComponentDisplayType.TriangleDown, resistance[0]);
        }

        [Fact]
        public void MarketStructure_swings_are_angular_so_they_cannot_be_read_as_a_zone_tier()
        {
            var comps = Components(new SwingStructureProvider()).ToDictionary(c => c.Name);

            Assert.Equal(ComponentDisplayType.Square, comps[SwingStructureProvider.CompSwingHigh].DisplayType);
            Assert.Equal(ComponentDisplayType.Square, comps[SwingStructureProvider.CompSwingLow].DisplayType);
            // Both structure EVENTS are crosses, told apart by colour (amber continuation vs
            // purple turn) and size — a pairing Value Deviation's ladder never uses.
            Assert.Equal(ComponentDisplayType.Cross, comps[SwingStructureProvider.CompBos].DisplayType);
            Assert.Equal(ComponentDisplayType.Cross, comps[SwingStructureProvider.CompChoch].DisplayType);
            Assert.True(ColorDistanceSq(comps[SwingStructureProvider.CompBos].DefaultColorHex!,
                                        comps[SwingStructureProvider.CompChoch].DefaultColorHex!) > 30_000,
                "Break of Structure and Change of Character share a shape, so their colours must not be close.");
        }

        [Fact]
        public void Swing_marks_sit_clear_of_the_wick_rather_than_on_its_tip()
        {
            // The swing's VALUE is the pivot price — the bar's own high or low — so with the
            // default anchor the glyph lands exactly on the wick tip and hides the price it marks.
            var comps = Components(new SwingStructureProvider()).ToDictionary(c => c.Name);

            Assert.Equal(MarkerAnchor.AboveBar, comps[SwingStructureProvider.CompSwingHigh].DefaultMarkerAnchor);
            Assert.Equal(MarkerAnchor.BelowBar, comps[SwingStructureProvider.CompSwingLow].DefaultMarkerAnchor);
        }

        [Fact]
        public void Support_marks_hang_below_the_bar_and_resistance_marks_above_it()
        {
            // Reversed anchors would put a support mark over the candle body, where it reads as
            // resistance — the side is the whole meaning of the mark.
            var comps = Components(new ValueDeviationProvider()).ToDictionary(c => c.Name);

            foreach (var name in new[] { ValueDeviationProvider.CompSupportShallow,
                                         ValueDeviationProvider.CompSupportMid,
                                         ValueDeviationProvider.CompSupportDeep })
                Assert.Equal(MarkerAnchor.BelowBar, comps[name].DefaultMarkerAnchor);

            foreach (var name in new[] { ValueDeviationProvider.CompResistShallow,
                                         ValueDeviationProvider.CompResistMid,
                                         ValueDeviationProvider.CompResistDeep })
                Assert.Equal(MarkerAnchor.AboveBar, comps[name].DefaultMarkerAnchor);
        }
    }
}
