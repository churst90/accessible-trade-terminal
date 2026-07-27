using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services.Screening;
using AccessibleTrader.Sdk.Strategies;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The screen builder's translation layer — flat filter rows in, condition tree out.
    ///
    /// <para>
    /// Everything here fails SILENTLY in production if it is wrong. A screen with an operator its
    /// signal can never satisfy, or one that lost an operand on the way to disk, still runs, still
    /// reports "0 matched of 40 evaluated", and reads exactly like a quiet market. There is no
    /// visual tell and no exception, so these are the tests that stand in for looking at it.
    /// </para>
    /// </summary>
    public class QuickScreenBuilderTests
    {
        // Deterministic ids so trees can be compared without GUID noise.
        private static Func<string> Ids()
        {
            int n = 0;
            return () => $"id{n++}";
        }

        private static QuickFilter Filter(LeafOperator op, string signal = "RSI.Value") =>
            new() { IndicatorCode = QuickScreenBuilder.IndicatorCodeOf(signal), SignalId = signal, Operator = op };

        // ── Operator gating ──────────────────────────────────────────────

        [Fact]
        public void MarkerFire_only_offers_fire_operators()
        {
            var ops = QuickScreenBuilder.OperatorsFor(SignalKind.MarkerFire);

            Assert.Equal(new[] { LeafOperator.Fired, LeafOperator.FiredWithin }, ops);
            // The specific trap: a marker is NaN on every bar it didn't fire, so a threshold
            // comparison against it is false forever and looks like an absent setup.
            Assert.DoesNotContain(LeafOperator.GreaterThan, ops);
        }

        [Fact]
        public void Cloud_and_level_kinds_offer_only_their_own_semantics()
        {
            var cloud = QuickScreenBuilder.OperatorsFor(SignalKind.Cloud);
            var level = QuickScreenBuilder.OperatorsFor(SignalKind.Level);

            Assert.All(cloud, op => Assert.Contains("Cloud", op.ToString()));
            Assert.All(level, op => Assert.StartsWith("Price", op.ToString()));
            Assert.Empty(cloud.Intersect(level));
        }

        [Fact]
        public void Line_kind_excludes_range_and_percentile_operators()
        {
            // Price-space lines are unbounded, so "between 25 and 75" and "above the 80th
            // percentile" are meaningless framings for them.
            var ops = QuickScreenBuilder.OperatorsFor(SignalKind.Line);

            Assert.DoesNotContain(LeafOperator.Between, ops);
            Assert.DoesNotContain(LeafOperator.PercentileAbove, ops);
            Assert.Contains(LeafOperator.CrossesAbove, ops);
        }

        [Fact]
        public void Every_signal_kind_offers_at_least_one_operator()
        {
            // The UI snaps to OperatorsFor(...)[0] whenever the signal changes, so an empty
            // list would be an index-out-of-range on selection, not a graceful degradation.
            foreach (SignalKind kind in Enum.GetValues<SignalKind>())
                Assert.NotEmpty(QuickScreenBuilder.OperatorsFor(kind));
        }

        // ── Operand requirements ─────────────────────────────────────────

        [Fact]
        public void Between_needs_both_bounds_and_plain_comparisons_need_only_one()
        {
            Assert.True(QuickScreenBuilder.NeedsValue(LeafOperator.Between));
            Assert.True(QuickScreenBuilder.NeedsValue2(LeafOperator.Between));

            Assert.True(QuickScreenBuilder.NeedsValue(LeafOperator.GreaterThan));
            Assert.False(QuickScreenBuilder.NeedsValue2(LeafOperator.GreaterThan));
        }

        [Fact]
        public void Fired_needs_no_operands_but_FiredWithin_needs_a_window()
        {
            Assert.False(QuickScreenBuilder.NeedsValue(LeafOperator.Fired));
            Assert.False(QuickScreenBuilder.NeedsWithin(LeafOperator.Fired));

            Assert.True(QuickScreenBuilder.NeedsWithin(LeafOperator.FiredWithin));
        }

        [Fact]
        public void Every_within_operator_is_declared_as_needing_a_window()
        {
            // Guards against a new "...Within" operator being added to LeafOperator without the
            // builder learning to show its bars box — the resulting filter would quietly use 1.
            foreach (LeafOperator op in Enum.GetValues<LeafOperator>())
            {
                if (op.ToString().EndsWith("Within", StringComparison.Ordinal))
                    Assert.True(QuickScreenBuilder.NeedsWithin(op), $"{op} ends in Within but NeedsWithin is false.");
            }
        }

        // ── Tree construction ────────────────────────────────────────────

        [Fact]
        public void No_filters_builds_a_null_root_which_matches_every_symbol()
        {
            Assert.Null(QuickScreenBuilder.BuildRoot(new List<QuickFilter>(), LogicOperator.And, 1.0, Ids()));
        }

        [Fact]
        public void Single_filter_under_And_builds_a_bare_leaf_not_a_one_child_group()
        {
            var root = QuickScreenBuilder.BuildRoot(
                new[] { Filter(LeafOperator.GreaterThan) }, LogicOperator.And, 1.0, Ids());

            var leaf = Assert.IsType<ConditionLeaf>(root);
            Assert.Equal("RSI.Value", leaf.SignalDescriptorId);
        }

        [Fact]
        public void Single_filter_under_Score_still_builds_a_group_because_the_threshold_lives_there()
        {
            var root = QuickScreenBuilder.BuildRoot(
                new[] { Filter(LeafOperator.GreaterThan) }, LogicOperator.Score, 2.5, Ids());

            var group = Assert.IsType<ConditionGroup>(root);
            Assert.Equal(LogicOperator.Score, group.Logic);
            Assert.Equal(2.5, group.ScoreThreshold);
        }

        [Fact]
        public void And_or_groups_carry_no_score_threshold()
        {
            // ConditionEvaluator only consults ScoreThreshold for Score logic, but persisting a
            // stray threshold on an And group would resurface in the editor as a value the user
            // never set.
            var filters = new[] { Filter(LeafOperator.Fired), Filter(LeafOperator.Fired, "MACD.Hist") };

            var and = (ConditionGroup)QuickScreenBuilder.BuildRoot(filters, LogicOperator.And, 3.0, Ids())!;
            var or  = (ConditionGroup)QuickScreenBuilder.BuildRoot(filters, LogicOperator.Or,  3.0, Ids())!;

            Assert.Null(and.ScoreThreshold);
            Assert.Null(or.ScoreThreshold);
        }

        [Fact]
        public void Operands_the_operator_does_not_use_are_dropped_rather_than_carried_through()
        {
            // The real scenario: the user picks "is between 20 and 80", then changes the operator
            // to "fired". Value2 and WithinNBars are still sitting in the row from before. If they
            // were written to the leaf, the saved screen would mean something different from what
            // the dialog was showing.
            var stale = new QuickFilter
            {
                SignalId = "CIPHER_B.Buy",
                Operator = LeafOperator.Fired,
                Value = 20,
                Value2 = 80,
                WithinNBars = 14,
            };

            var leaf = (ConditionLeaf)QuickScreenBuilder.BuildRoot(new[] { stale }, LogicOperator.And, 1.0, Ids())!;

            Assert.Equal(0, leaf.Value);
            Assert.Null(leaf.Value2);
            Assert.Equal(1, leaf.WithinNBars);
        }

        [Fact]
        public void Operands_the_operator_does_use_survive_intact()
        {
            var f = new QuickFilter
            {
                SignalId = "RSI.Value",
                Operator = LeafOperator.BetweenWithin,
                Value = 20,
                Value2 = 35,
                WithinNBars = 8,
                Score = 0.4,
            };

            var leaf = (ConditionLeaf)QuickScreenBuilder.BuildRoot(new[] { f }, LogicOperator.And, 1.0, Ids())!;

            Assert.Equal(20, leaf.Value);
            Assert.Equal(35, leaf.Value2);
            Assert.Equal(8, leaf.WithinNBars);
            Assert.Equal(0.4, leaf.Score);
        }

        [Fact]
        public void Leaf_ids_are_unique_across_the_tree()
        {
            // ConditionEvaluator keys per-leaf state by Id; duplicates would make two filters
            // share one slot and produce wrong dropout detection.
            var filters = Enumerable.Range(0, 5).Select(i => Filter(LeafOperator.Fired, $"IND{i}.C")).ToList();

            var group = (ConditionGroup)QuickScreenBuilder.BuildRoot(filters, LogicOperator.And, 1.0)!;
            var ids = group.Children.Select(c => c.Id).ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.DoesNotContain(group.Id, ids);
        }

        // ── Round-trip ───────────────────────────────────────────────────

        [Fact]
        public void Build_then_FromRoot_round_trips_every_field()
        {
            var original = new List<QuickFilter>
            {
                new() { SignalId = "RSI.Value",   Operator = LeafOperator.LessThan,     Value = 30, Score = 0.5 },
                new() { SignalId = "MACD.Hist",   Operator = LeafOperator.Between,      Value = -1, Value2 = 1, Score = 1.5 },
                new() { SignalId = "CIPHER_B.Buy", Operator = LeafOperator.FiredWithin, WithinNBars = 5, Score = 2 },
            };

            var shape = QuickScreenBuilder.FromRoot(
                QuickScreenBuilder.BuildRoot(original, LogicOperator.Score, 3.0, Ids()));

            Assert.Equal(LogicOperator.Score, shape.Logic);
            Assert.Equal(3.0, shape.ScoreThreshold);
            Assert.False(shape.HasNestedGroups);
            Assert.Equal(3, shape.Filters.Count);

            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].SignalId, shape.Filters[i].SignalId);
                Assert.Equal(original[i].Operator, shape.Filters[i].Operator);
                Assert.Equal(original[i].Score, shape.Filters[i].Score);
                if (QuickScreenBuilder.NeedsValue(original[i].Operator))
                    Assert.Equal(original[i].Value, shape.Filters[i].Value);
                if (QuickScreenBuilder.NeedsValue2(original[i].Operator))
                    Assert.Equal(original[i].Value2, shape.Filters[i].Value2);
                if (QuickScreenBuilder.NeedsWithin(original[i].Operator))
                    Assert.Equal(original[i].WithinNBars, shape.Filters[i].WithinNBars);
            }
        }

        [Fact]
        public void FromRoot_recovers_the_indicator_code_for_the_two_step_picker()
        {
            var shape = QuickScreenBuilder.FromRoot(
                new ConditionLeaf("x", "VALUE_DEVIATION.Support tier 3", LeafOperator.Fired));

            Assert.Equal("VALUE_DEVIATION", shape.Filters[0].IndicatorCode);
        }

        [Fact]
        public void FromRoot_flags_nested_groups_instead_of_silently_dropping_them()
        {
            // A screen built elsewhere (or by hand in screeners.json) can nest. The flat builder
            // cannot show that, and saving over it would destroy structure the user can't recover,
            // so the caller has to be told rather than shown a plausible-looking subset.
            var nested = new ConditionGroup("root", LogicOperator.And, new ConditionNode[]
            {
                new ConditionLeaf("a", "RSI.Value", LeafOperator.LessThan, 30),
                new ConditionGroup("inner", LogicOperator.Or, new ConditionNode[]
                {
                    new ConditionLeaf("b", "MACD.Hist", LeafOperator.GreaterThan, 0),
                }),
            });

            var shape = QuickScreenBuilder.FromRoot(nested);

            Assert.True(shape.HasNestedGroups);
            Assert.Single(shape.Filters);   // the top-level leaf is still shown
        }

        [Fact]
        public void FromRoot_on_a_null_root_yields_an_empty_editable_screen()
        {
            var shape = QuickScreenBuilder.FromRoot(null);

            Assert.Empty(shape.Filters);
            Assert.Equal(LogicOperator.And, shape.Logic);
            Assert.False(shape.HasNestedGroups);
        }

        // ── Defaults ─────────────────────────────────────────────────────

        [Fact]
        public void Bounded_signals_seed_a_threshold_inside_their_own_range()
        {
            var rsi = new SignalDescriptor("RSI.Value", "RSI", "Value", SignalKind.Oscillator, "RSI — Value", 0, 100);
            var f = new QuickFilter { SignalId = rsi.Id, Operator = LeafOperator.Between };

            QuickScreenBuilder.SeedDefaultValue(f, rsi);

            Assert.Equal(25, f.Value);
            Assert.Equal(75, f.Value2);
        }

        [Fact]
        public void Unbounded_signals_seed_zero_rather_than_an_invented_level()
        {
            // A price-space line has no honest default. Making one up would look authoritative.
            var ema = new SignalDescriptor("EMA.Line", "EMA", "Line", SignalKind.Line, "EMA — Line");
            var f = new QuickFilter { SignalId = ema.Id, Operator = LeafOperator.GreaterThan };

            QuickScreenBuilder.SeedDefaultValue(f, ema);

            Assert.Equal(0, f.Value);
        }

        [Fact]
        public void Percentile_operators_seed_conventional_tails_not_a_range_quarter()
        {
            var rsi = new SignalDescriptor("RSI.Value", "RSI", "Value", SignalKind.Oscillator, "RSI — Value", 0, 100);

            var above = new QuickFilter { SignalId = rsi.Id, Operator = LeafOperator.PercentileAbove };
            var below = new QuickFilter { SignalId = rsi.Id, Operator = LeafOperator.PercentileBelow };
            QuickScreenBuilder.SeedDefaultValue(above, rsi);
            QuickScreenBuilder.SeedDefaultValue(below, rsi);

            Assert.Equal(80, above.Value);
            Assert.Equal(20, below.Value);
        }

        // ── Description ──────────────────────────────────────────────────

        [Fact]
        public void Describe_mentions_every_operand_the_operator_actually_uses()
        {
            var sig = new SignalDescriptor("RSI.Value", "RSI", "Value", SignalKind.Oscillator, "RSI — Value", 0, 100);
            var f = new QuickFilter
            {
                SignalId = sig.Id, Operator = LeafOperator.BetweenWithin,
                Value = 20, Value2 = 35, WithinNBars = 5, Score = 0.5,
            };

            string plain = QuickScreenBuilder.Describe(f, sig, LogicOperator.And);
            string scored = QuickScreenBuilder.Describe(f, sig, LogicOperator.Score);

            Assert.Contains("RSI — Value", plain);
            Assert.Contains("20", plain);
            Assert.Contains("35", plain);
            Assert.Contains("within 5 bars", plain);
            // Weight is only meaningful under Score logic, so it only appears there.
            Assert.DoesNotContain("weight", plain);
            Assert.Contains("weight", scored);
        }

        [Fact]
        public void Describe_omits_operands_the_operator_ignores()
        {
            var sig = new SignalDescriptor("CIPHER_B.Buy", "CIPHER_B", "Buy", SignalKind.MarkerFire, "Cipher B — Buy");
            var f = new QuickFilter { SignalId = sig.Id, Operator = LeafOperator.Fired, Value = 42 };

            Assert.DoesNotContain("42", QuickScreenBuilder.Describe(f, sig, LogicOperator.And));
        }

        [Fact]
        public void Describe_falls_back_to_the_signal_id_when_the_descriptor_is_gone()
        {
            // Happens when a screen references an indicator from a plugin that is no longer
            // loaded. Showing the raw id beats showing a blank row.
            var f = new QuickFilter { SignalId = "GHOST.Component", Operator = LeafOperator.Fired };

            Assert.Contains("GHOST.Component", QuickScreenBuilder.Describe(f, null, LogicOperator.And));
        }

        [Fact]
        public void Every_operator_has_a_human_label()
        {
            foreach (LeafOperator op in Enum.GetValues<LeafOperator>())
            {
                string label = QuickScreenBuilder.OperatorLabel(op);
                Assert.False(string.IsNullOrWhiteSpace(label));
                // A label identical to the enum name means the switch fell through to ToString()
                // and the user is reading "PriceOutsideValueArea" instead of English.
                Assert.NotEqual(op.ToString(), label);
            }
        }

        // ── Signal id parsing ────────────────────────────────────────────

        [Fact]
        public void IndicatorCodeOf_splits_at_the_first_dot_because_components_can_contain_dots()
        {
            Assert.Equal("CIPHER_A", QuickScreenBuilder.IndicatorCodeOf("CIPHER_A.WT 1.0 cross"));
            Assert.Equal("RSI", QuickScreenBuilder.IndicatorCodeOf("RSI.Value"));
            Assert.Equal("BARE", QuickScreenBuilder.IndicatorCodeOf("BARE"));
            Assert.Equal("", QuickScreenBuilder.IndicatorCodeOf(""));
        }

        // ── Symbol picker filter ─────────────────────────────────────────

        [Fact]
        public void FilterSymbols_matches_case_insensitively_anywhere_in_the_symbol()
        {
            var all = new[] { "BTC/USDT", "ETH/USDT", "BTC/EUR", "SOLUSDT" };

            var hits = QuickScreenBuilder.FilterSymbols(all, "usdt", 100);

            Assert.Equal(new[] { "BTC/USDT", "ETH/USDT", "SOLUSDT" }, hits);
        }

        [Fact]
        public void FilterSymbols_caps_the_list_so_a_three_thousand_symbol_provider_cannot_wedge_the_dialog()
        {
            var all = Enumerable.Range(0, 3000).Select(i => $"SYM{i}").ToList();

            Assert.Equal(500, QuickScreenBuilder.FilterSymbols(all, null, 500).Count);
            // A non-positive cap means "no cap" — used by tests and any caller that wants it all.
            Assert.Equal(3000, QuickScreenBuilder.FilterSymbols(all, null, 0).Count);
        }

        [Fact]
        public void FilterSymbols_trims_the_query_and_survives_an_empty_universe()
        {
            Assert.Equal(new[] { "BTC/USDT" },
                QuickScreenBuilder.FilterSymbols(new[] { "BTC/USDT", "ETH/EUR" }, "  usdt  ", 100));

            Assert.Empty(QuickScreenBuilder.FilterSymbols(Array.Empty<string>(), "x", 100));
        }
    }
}
