using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// What the pane legend chooses to name, and in what order.
    ///
    /// <para>
    /// These exist because of one weekly BTC chart. The legend took the first nine components in
    /// series order with a fixed nine-row cap, so a single marker-heavy indicator spent the entire
    /// budget: the box grew to about 152px inside a ~215px price pane, covered a third of the
    /// plot, and listed nine near-identical tier labels while the candles, the moving average and
    /// the levels — the things a reader actually needs named — never appeared at all.
    /// </para>
    ///
    /// <para>
    /// So: rank before truncating, size against the pane, collapse marker families, and say so
    /// when rows are dropped. A legend showing a silent subset reads as a complete list of what is
    /// on the chart, which is precisely the wrong thing for it to imply.
    /// </para>
    /// </summary>
    public class PaneLegendTests
    {
        // Line height and padding in device pixels, matching RenderPaneLegend at density 1.
        private const float Line = 16f;
        private const float Pad  = 4f;

        // Tall enough that the row budget is the hard ceiling of 9 rather than the pane fraction.
        private const float TallPane = 400f;

        private static ChartSeries Series(string name, params (string Label, ComponentDisplayType Type)[] comps)
        {
            var s = new ChartSeries();
            s.Config.Name = name;
            foreach (var (label, type) in comps)
            {
                s.Components.Add(new ComponentConfig
                {
                    Name = label,
                    DisplayName = label,
                    DisplayType = type,
                    ColorHex = "#FFFFFF",
                    IsVisible = true,
                });
            }
            return s;
        }

        private static List<string> Labels(List<ChartRenderer.LegendRow> rows) =>
            rows.Select(r => r.Label).ToList();

        // ── Ranking ──────────────────────────────────────────────────────

        [Fact]
        public void Price_and_lines_outrank_markers_regardless_of_series_order()
        {
            // The real ordering: Market Structure and Value Deviation are added AFTER candles,
            // but under the old first-N rule their markers came first and pushed price out.
            var panes = new List<ChartSeries>
            {
                Series("Value Deviation",
                    ("Support tier 1-2", ComponentDisplayType.TriangleUp),
                    ("Support tier 3", ComponentDisplayType.Dot),
                    ("Support tier 4-5", ComponentDisplayType.Diamond),
                    ("Value POC", ComponentDisplayType.StepLine)),
                Series("Candles", ("Candles", ComponentDisplayType.Candle)),
                Series("EMA 50", ("EMA 50", ComponentDisplayType.Line)),
            };

            var labels = Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad));

            Assert.Equal("Candles", labels[0]);
            // Both continuous lines precede the collapsed marker row.
            Assert.True(labels.IndexOf("Value POC") < labels.IndexOf("Value Deviation — 3 marks"));
            Assert.True(labels.IndexOf("EMA 50") < labels.IndexOf("Value Deviation — 3 marks"));
        }

        [Fact]
        public void Within_a_rank_the_users_own_series_order_is_preserved()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", ("EMA 10", ComponentDisplayType.Line)),
                Series("B", ("EMA 50", ComponentDisplayType.Line)),
                Series("C", ("EMA 200", ComponentDisplayType.Line)),
            };

            Assert.Equal(new[] { "EMA 10", "EMA 50", "EMA 200" },
                Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad)));
        }

        [Fact]
        public void Volume_bars_count_as_base_data_and_lead_their_pane()
        {
            var panes = new List<ChartSeries>
            {
                Series("Market Structure",
                    ("Swing High", ComponentDisplayType.Square),
                    ("Swing Low", ComponentDisplayType.Square)),
                Series("Volume", ("Volume", ComponentDisplayType.Bar)),
            };

            Assert.Equal("Volume", Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad))[0]);
        }

        // ── Marker collapse ──────────────────────────────────────────────

        [Fact]
        public void A_marker_heavy_series_collapses_to_one_row_naming_the_series_and_the_count()
        {
            var panes = new List<ChartSeries>
            {
                Series("Value Deviation",
                    ("Support tier 1-2", ComponentDisplayType.TriangleUp),
                    ("Support tier 3", ComponentDisplayType.Dot),
                    ("Support tier 4-5", ComponentDisplayType.Diamond),
                    ("Resistance tier 1-2", ComponentDisplayType.TriangleDown),
                    ("Resistance tier 3", ComponentDisplayType.Dot),
                    ("Resistance tier 4-5", ComponentDisplayType.Diamond)),
            };

            var labels = Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad));

            Assert.Equal(new[] { "Value Deviation — 6 marks" }, labels);
        }

        [Fact]
        public void One_or_two_markers_keep_their_real_names()
        {
            // Collapsing "Swing High / Swing Low" into "Market Structure — 2 marks" would lose
            // information rather than save space.
            var panes = new List<ChartSeries>
            {
                Series("Market Structure",
                    ("Swing High", ComponentDisplayType.Square),
                    ("Swing Low", ComponentDisplayType.Square)),
            };

            Assert.Equal(new[] { "Swing High", "Swing Low" },
                Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad)));
        }

        [Fact]
        public void Collapsing_one_series_never_removes_another_series_marker_rows()
        {
            // The bug this guards: matching the rows to remove by COLOUR meant that when two
            // indicators happened to share a colour, collapsing the second deleted the first's
            // row too. Every component here is #FFFFFF, which is the worst case.
            var panes = new List<ChartSeries>
            {
                Series("Market Structure", ("Swing High", ComponentDisplayType.Square)),
                Series("Value Deviation",
                    ("Support tier 1-2", ComponentDisplayType.TriangleUp),
                    ("Support tier 3", ComponentDisplayType.Dot),
                    ("Support tier 4-5", ComponentDisplayType.Diamond)),
            };

            var labels = Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad));

            Assert.Contains("Swing High", labels);
            Assert.Contains("Value Deviation — 3 marks", labels);
        }

        // ── Sizing ───────────────────────────────────────────────────────

        [Fact]
        public void The_row_budget_comes_from_the_pane_height_not_a_constant()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", Enumerable.Range(0, 12)
                    .Select(i => ($"Line {i}", ComponentDisplayType.Line)).ToArray()),
            };

            // A 215px price pane sharing the window with a volume pane — the screenshot's case.
            var shortPane = ChartRenderer.BuildLegendRows(panes, 215f, Line, Pad);
            var tallPane  = ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad);

            Assert.True(shortPane.Count < tallPane.Count,
                "A short pane must yield fewer legend rows than a tall one.");
            // Whatever it chose has to fit inside the box it is allowed to draw.
            Assert.True(Pad * 2 + shortPane.Count * Line <= 215f * 0.45f + Line,
                "Legend box overflows its share of the pane.");
        }

        [Fact]
        public void The_legend_never_exceeds_nine_rows_however_tall_the_pane()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", Enumerable.Range(0, 40)
                    .Select(i => ($"Line {i}", ComponentDisplayType.Line)).ToArray()),
            };

            Assert.Equal(9, ChartRenderer.BuildLegendRows(panes, 5000f, Line, Pad).Count);
        }

        [Fact]
        public void A_pane_too_short_for_the_computed_budget_still_gets_a_minimum_of_three_rows()
        {
            // Better a cramped legend than none: with zero rows the reader has no key at all.
            var panes = new List<ChartSeries>
            {
                Series("A",
                    ("Candles", ComponentDisplayType.Candle),
                    ("EMA", ComponentDisplayType.Line),
                    ("VWAP", ComponentDisplayType.Line),
                    ("BB Upper", ComponentDisplayType.Line)),
            };

            Assert.Equal(3, ChartRenderer.BuildLegendRows(panes, 20f, Line, Pad).Count);
        }

        // ── Honesty about truncation ─────────────────────────────────────

        [Fact]
        public void Dropped_rows_are_announced_rather_than_silently_omitted()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", Enumerable.Range(0, 20)
                    .Select(i => ($"Line {i}", ComponentDisplayType.Line)).ToArray()),
            };

            var rows = ChartRenderer.BuildLegendRows(panes, 5000f, Line, Pad);

            // 20 components, 9 rows: 8 named + one row accounting for the other 12.
            Assert.Equal(9, rows.Count);
            Assert.Equal("+12 more (see the object tree)", rows[^1].Label);
        }

        [Fact]
        public void Nothing_is_dropped_when_everything_fits_so_no_more_row_appears()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", ("Candles", ComponentDisplayType.Candle), ("EMA", ComponentDisplayType.Line)),
            };

            var rows = ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad);

            Assert.Equal(2, rows.Count);
            Assert.DoesNotContain(rows, r => r.Label.Contains("more"));
        }

        [Fact]
        public void The_dropped_count_accounts_for_every_component_that_is_not_shown()
        {
            var panes = new List<ChartSeries>
            {
                Series("A", Enumerable.Range(0, 15)
                    .Select(i => ($"Line {i}", ComponentDisplayType.Line)).ToArray()),
            };

            var rows = ChartRenderer.BuildLegendRows(panes, 5000f, Line, Pad);
            int named = rows.Count - 1;

            Assert.Equal($"+{15 - named} more (see the object tree)", rows[^1].Label);
        }

        // ── Exclusions ───────────────────────────────────────────────────

        [Fact]
        public void Hidden_components_and_reference_levels_are_not_listed()
        {
            var s = Series("A",
                ("Visible line", ComponentDisplayType.Line),
                ("Hidden line", ComponentDisplayType.Line),
                ("A level", ComponentDisplayType.Level));
            s.Components[1].IsVisible = false;

            Assert.Equal(new[] { "Visible line" },
                Labels(ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, TallPane, Line, Pad)));
        }

        [Fact]
        public void An_empty_or_null_pane_yields_no_rows_rather_than_an_empty_box()
        {
            Assert.Empty(ChartRenderer.BuildLegendRows(new List<ChartSeries>(), TallPane, Line, Pad));
            Assert.Empty(ChartRenderer.BuildLegendRows(null!, TallPane, Line, Pad));
        }

        // ── Classification ───────────────────────────────────────────────

        [Fact]
        public void Every_glyph_shape_the_marker_renderers_handle_is_classified_as_a_marker()
        {
            // If a shape is added to the renderers but not to IsMarker, it stops collapsing and
            // silently re-inflates the legend the way this whole change exists to prevent.
            foreach (var t in new[]
            {
                ComponentDisplayType.Dot, ComponentDisplayType.ZeroDot, ComponentDisplayType.GradientDot,
                ComponentDisplayType.Diamond, ComponentDisplayType.Square, ComponentDisplayType.Cross,
                ComponentDisplayType.TriangleUp, ComponentDisplayType.TriangleDown, ComponentDisplayType.Arrow,
            })
                Assert.True(ChartRenderer.IsMarker(t), $"{t} is drawn as a glyph but is not classified as a marker.");
        }

        [Fact]
        public void Continuous_shapes_are_not_markers()
        {
            foreach (var t in new[]
            {
                ComponentDisplayType.Line, ComponentDisplayType.StepLine, ComponentDisplayType.Cloud,
                ComponentDisplayType.Area, ComponentDisplayType.Gradient,
            })
                Assert.False(ChartRenderer.IsMarker(t), $"{t} is continuous and must not collapse as a marker.");
        }
        // ── Key consistency ──────────────────────────────────────────────

        [Fact]
        public void A_lines_key_is_a_line_a_markers_key_is_that_marker_and_candles_are_a_chip()
        {
            // Every row used to get an identical coloured square, so a dashed resistance line and
            // a diamond marker had the same key and the legend told you the colour and nothing
            // else. The key now has to look like the thing it stands for.
            var panes = new List<ChartSeries>
            {
                Series("A",
                    ("Candles", ComponentDisplayType.Candle),
                    ("Value POC", ComponentDisplayType.StepLine),
                    ("Swing High", ComponentDisplayType.Square)),
            };

            var rows = ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad)
                                    .ToDictionary(r => r.Label);

            Assert.Equal(ChartRenderer.LegendGlyph.Fill,   rows["Candles"].Glyph);
            Assert.Equal(ChartRenderer.LegendGlyph.Line,   rows["Value POC"].Glyph);
            Assert.Equal(ChartRenderer.LegendGlyph.Marker, rows["Swing High"].Glyph);
            Assert.Equal(ComponentDisplayType.Square, rows["Swing High"].MarkerShape);
        }

        [Fact]
        public void A_dashed_series_carries_its_dash_style_into_the_key()
        {
            var s = Series("A", ("Resistance", ComponentDisplayType.StepLine));
            s.Components[0].DashStyle = DashStyle.Dash;
            s.Components[0].Thickness = 3f;

            var row = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, TallPane, Line, Pad).Single();

            Assert.Equal(DashStyle.Dash, row.Dash);
            Assert.Equal(3f, row.StrokeWidth);
        }

        [Fact]
        public void A_collapsed_marker_row_carries_every_distinct_colour_in_the_family()
        {
            // Showing one swatch for six marks in six colours claims the indicator is one colour.
            var s = new ChartSeries();
            s.Config.Name = "Value Deviation";
            var spec = new[]
            {
                ("Support tier 1-2",    ComponentDisplayType.TriangleUp,   "#66BB6A"),
                ("Support tier 3",      ComponentDisplayType.Dot,          "#2E9E4F"),
                ("Support tier 4-5",    ComponentDisplayType.Diamond,      "#00E676"),
                ("Resistance tier 1-2", ComponentDisplayType.TriangleDown, "#EF9A9A"),
                ("Resistance tier 3",   ComponentDisplayType.Dot,          "#E53935"),
                ("Resistance tier 4-5", ComponentDisplayType.Diamond,      "#FF1744"),
            };
            foreach (var (name, type, hex) in spec)
                s.Components.Add(new ComponentConfig
                {
                    Name = name, DisplayName = name, DisplayType = type,
                    ColorHex = hex, IsVisible = true,
                });

            var row = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, TallPane, Line, Pad).Single();

            Assert.Equal("Value Deviation — 6 marks", row.Label);
            Assert.Equal(6, row.Colors.Count);
        }

        [Fact]
        public void Repeated_colours_in_a_family_are_not_listed_twice()
        {
            var s = Series("A",
                ("One", ComponentDisplayType.Dot),
                ("Two", ComponentDisplayType.Dot),
                ("Three", ComponentDisplayType.Dot));   // all #FFFFFF from the helper

            var row = ChartRenderer.BuildLegendRows(new List<ChartSeries> { s }, TallPane, Line, Pad).Single();

            Assert.Single(row.Colors);
        }

        // ── Label collisions ─────────────────────────────────────────────

        [Fact]
        public void Two_indicators_naming_a_component_the_same_get_their_owner_prefixed()
        {
            // Real case: a level provider and Value Deviation both produce a "Resistance". Side by
            // side those two rows read as a duplicated entry rather than as two different lines.
            var panes = new List<ChartSeries>
            {
                Series("Cipher SR 5 1", ("Resistance", ComponentDisplayType.StepLine)),
                Series("Value Deviation (zones) 240", ("Resistance", ComponentDisplayType.StepLine)),
            };

            var labels = Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad));

            Assert.Contains("Cipher SR Resistance", labels);
            Assert.Contains("Value Deviation Resistance", labels);
            Assert.DoesNotContain("Resistance", labels);
        }

        [Fact]
        public void A_label_that_is_already_unique_is_left_alone()
        {
            // Prefixing everything would make every row longer for no gain, and the legend is
            // width-constrained.
            var panes = new List<ChartSeries>
            {
                Series("Cipher SR 5 1", ("Resistance", ComponentDisplayType.StepLine)),
                Series("Value Deviation 240", ("Value POC", ComponentDisplayType.StepLine)),
            };

            var labels = Labels(ChartRenderer.BuildLegendRows(panes, TallPane, Line, Pad));

            Assert.Contains("Resistance", labels);
            Assert.Contains("Value POC", labels);
        }
    }
}