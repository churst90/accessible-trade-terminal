using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Screening;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Screening
{
    /// <summary>
    /// One row of the quick screen builder: a signal, an operator, and whatever operands that
    /// operator needs. Mutable because it is bound directly to form controls.
    /// </summary>
    public sealed class QuickFilter
    {
        /// <summary>Indicator the signal belongs to — kept separately so the UI can offer a
        /// two-step indicator-then-component picker instead of one flat list of hundreds.</summary>
        public string IndicatorCode { get; set; } = "";

        /// <summary>Foreign key into <see cref="SignalDescriptor.Id"/>.</summary>
        public string SignalId { get; set; } = "";

        public LeafOperator Operator { get; set; } = LeafOperator.Fired;
        public double Value { get; set; }
        public double Value2 { get; set; }
        public int WithinNBars { get; set; } = 1;
        public double Score { get; set; } = 1.0;
    }

    /// <summary>
    /// What <see cref="QuickScreenBuilder.FromRoot"/> recovered from a saved screen.
    /// </summary>
    /// <param name="Logic">How the filters combine.</param>
    /// <param name="ScoreThreshold">Threshold when <paramref name="Logic"/> is Score; 1.0 otherwise.</param>
    /// <param name="Filters">The flat filter rows.</param>
    /// <param name="HasNestedGroups">
    /// True when the tree contained a group inside a group. The quick builder is deliberately flat,
    /// so this is surfaced as a warning rather than silently discarded — re-saving a flattened copy
    /// over a hand-built nested screen would destroy work the user can't get back.
    /// </param>
    public record QuickScreenShape(
        LogicOperator Logic,
        double ScoreThreshold,
        List<QuickFilter> Filters,
        bool HasNestedGroups);

    /// <summary>
    /// Translation between the flat filter rows the screen builder shows and the
    /// <see cref="ConditionNode"/> tree the screener actually evaluates.
    ///
    /// <para>
    /// This lives in Core rather than in the razor because it is the part that can be WRONG in a
    /// way the user cannot see: an operator that a signal kind never satisfies, or an operand
    /// dropped on the way to disk, produces a screen that runs cleanly and matches nothing. That
    /// failure looks exactly like "no setups today", which is why it needs tests rather than eyes.
    /// </para>
    /// </summary>
    public static class QuickScreenBuilder
    {
        /// <summary>
        /// Operators a given signal kind can meaningfully take.
        ///
        /// <para>
        /// Offering the whole enum would let a user build a screen that is silently always false —
        /// asking a marker component whether it is "greater than 30" when it is NaN on every bar it
        /// did not fire, for instance. The first entry is used as the default when an operator has
        /// to be snapped to a valid one after the signal changes.
        /// </para>
        /// </summary>
        public static IReadOnlyList<LeafOperator> OperatorsFor(SignalKind kind) => kind switch
        {
            SignalKind.MarkerFire => new[] { LeafOperator.Fired, LeafOperator.FiredWithin },
            SignalKind.Pattern    => new[] { LeafOperator.Fired, LeafOperator.FiredWithin },
            SignalKind.Cloud      => new[] { LeafOperator.InsideCloud, LeafOperator.AboveCloud, LeafOperator.BelowCloud },
            SignalKind.Level      => new[] { LeafOperator.PriceRejectsLevel, LeafOperator.PriceBreaksLevel },
            SignalKind.Line       => new[]
            {
                LeafOperator.GreaterThan, LeafOperator.LessThan,
                LeafOperator.CrossesAbove, LeafOperator.CrossesBelow,
                LeafOperator.ChangesDirection
            },
            _ => new[]
            {
                LeafOperator.GreaterThan, LeafOperator.LessThan, LeafOperator.Between,
                LeafOperator.CrossesAbove, LeafOperator.CrossesBelow, LeafOperator.ChangesDirection,
                LeafOperator.GreaterThanWithin, LeafOperator.LessThanWithin, LeafOperator.BetweenWithin,
                LeafOperator.PercentileAbove, LeafOperator.PercentileBelow
            },
        };

        /// <summary>True when the operator compares against a primary number.</summary>
        public static bool NeedsValue(LeafOperator op) => op is
            LeafOperator.GreaterThan or LeafOperator.LessThan or LeafOperator.Between or
            LeafOperator.CrossesAbove or LeafOperator.CrossesBelow or
            LeafOperator.GreaterThanWithin or LeafOperator.LessThanWithin or LeafOperator.BetweenWithin or
            LeafOperator.PercentileAbove or LeafOperator.PercentileBelow or
            LeafOperator.PriceRejectsLevel or LeafOperator.PriceBreaksLevel;

        /// <summary>True when the operator needs an upper bound as well.</summary>
        public static bool NeedsValue2(LeafOperator op) => op is
            LeafOperator.Between or LeafOperator.BetweenWithin;

        /// <summary>True when the operator looks back over a window rather than at the last bar.</summary>
        public static bool NeedsWithin(LeafOperator op) => op is
            LeafOperator.FiredWithin or LeafOperator.GreaterThanWithin or LeafOperator.LessThanWithin or
            LeafOperator.BetweenWithin or LeafOperator.PercentileAbove or LeafOperator.PercentileBelow or
            LeafOperator.PriceRejectsLevel;

        /// <summary>Spoken/printed name of an operator, phrased to read as a sentence after the signal name.</summary>
        public static string OperatorLabel(LeafOperator op) => op switch
        {
            LeafOperator.Fired              => "fired on the last bar",
            LeafOperator.FiredWithin        => "fired within N bars",
            LeafOperator.GreaterThan        => "is above",
            LeafOperator.LessThan           => "is below",
            LeafOperator.Between            => "is between",
            LeafOperator.CrossesAbove       => "crossed above",
            LeafOperator.CrossesBelow       => "crossed below",
            LeafOperator.CrossesAboveLine   => "crossed above another component",
            LeafOperator.CrossesBelowLine   => "crossed below another component",
            LeafOperator.ChangesDirection   => "changed direction",
            LeafOperator.InsideCloud        => "price inside the cloud",
            LeafOperator.AboveCloud         => "price above the cloud",
            LeafOperator.BelowCloud         => "price below the cloud",
            LeafOperator.PriceRejectsLevel  => "price rejected the level",
            LeafOperator.PriceBreaksLevel   => "price broke the level",
            LeafOperator.BarClosesAbovePoc  => "bar closed above the point of control",
            LeafOperator.BarClosesBelowPoc  => "bar closed below the point of control",
            LeafOperator.PriceInsideValueArea  => "price inside the value area",
            LeafOperator.PriceOutsideValueArea => "price outside every value area",
            LeafOperator.WickIntoLvn        => "wick reached a low-volume node",
            LeafOperator.GreaterThanWithin  => "was above, within N bars",
            LeafOperator.LessThanWithin     => "was below, within N bars",
            LeafOperator.BetweenWithin      => "was between, within N bars",
            LeafOperator.PercentileAbove    => "is above the Nth percentile",
            LeafOperator.PercentileBelow    => "is below the Nth percentile",
            _                               => op.ToString(),
        };

        /// <summary>Label for the primary operand box, which is not always "value".</summary>
        public static string ValueLabel(LeafOperator op) => op switch
        {
            LeafOperator.Between or LeafOperator.BetweenWithin => "Lower",
            LeafOperator.PercentileAbove or LeafOperator.PercentileBelow => "Percentile",
            LeafOperator.PriceRejectsLevel or LeafOperator.PriceBreaksLevel => "Tolerance (fraction)",
            _ => "Value",
        };

        /// <summary>
        /// Puts a threshold somewhere useful inside the signal's documented range, so a freshly
        /// added filter means something instead of comparing an oscillator against zero.
        /// Percentile operators get conventional tails rather than a range quarter.
        /// </summary>
        public static void SeedDefaultValue(QuickFilter filter, SignalDescriptor signal)
        {
            if (filter.Operator == LeafOperator.PercentileAbove) { filter.Value = 80; return; }
            if (filter.Operator == LeafOperator.PercentileBelow) { filter.Value = 20; return; }

            if (!double.IsNaN(signal.MinValue) && !double.IsNaN(signal.MaxValue)
                && signal.MaxValue > signal.MinValue)
            {
                double span = signal.MaxValue - signal.MinValue;
                filter.Value  = signal.MinValue + span * 0.25;
                filter.Value2 = signal.MinValue + span * 0.75;
            }
            else
            {
                // Price-space lines are unbounded; there is no honest default, so leave it at zero
                // and let the user type a level rather than inventing one that looks authoritative.
                filter.Value = 0;
                filter.Value2 = 0;
            }
        }

        /// <summary>
        /// Plain-language echo of a filter, so a screen-reader user can verify a row in one read
        /// instead of tabbing back through five separate controls.
        /// </summary>
        public static string Describe(QuickFilter filter, SignalDescriptor? signal, LogicOperator logic)
        {
            string name = signal?.DisplayLabel ?? filter.SignalId;
            string s = $"{name} {OperatorLabel(filter.Operator)}";
            if (NeedsValue(filter.Operator))  s += $" {filter.Value:0.####}";
            if (NeedsValue2(filter.Operator)) s += $" and {filter.Value2:0.####}";
            if (NeedsWithin(filter.Operator))
                s += $", within {filter.WithinNBars} bar{(filter.WithinNBars == 1 ? "" : "s")}";
            if (logic == LogicOperator.Score) s += $", weight {filter.Score:0.##}";
            return s + ".";
        }

        /// <summary>
        /// Builds the condition tree. Operands the chosen operator does not use are written as
        /// null/1 rather than carried through, so a value left over from a previously selected
        /// operator cannot change the meaning of the saved screen.
        ///
        /// <para>
        /// A single filter under And/Or becomes a bare leaf — no pointless one-child group — but
        /// Score always produces a group, because that is where the threshold lives.
        /// </para>
        /// </summary>
        /// <param name="idFactory">
        /// Supplies leaf/group ids. Injected so tests can produce deterministic trees; production
        /// passes null and gets GUIDs.
        /// </param>
        public static ConditionNode? BuildRoot(
            IReadOnlyList<QuickFilter> filters,
            LogicOperator logic,
            double scoreThreshold,
            Func<string>? idFactory = null)
        {
            if (filters == null || filters.Count == 0) return null;
            idFactory ??= () => Guid.NewGuid().ToString("N");

            var leaves = filters.Select(f => (ConditionNode)new ConditionLeaf(
                Id: idFactory(),
                SignalDescriptorId: f.SignalId,
                Operator: f.Operator,
                Value: NeedsValue(f.Operator) ? f.Value : 0,
                Value2: NeedsValue2(f.Operator) ? f.Value2 : null,
                WithinNBars: NeedsWithin(f.Operator) ? f.WithinNBars : 1,
                Score: f.Score)).ToList();

            if (leaves.Count == 1 && logic != LogicOperator.Score) return leaves[0];

            return new ConditionGroup(
                idFactory(),
                logic,
                leaves,
                logic == LogicOperator.Score ? scoreThreshold : null);
        }

        /// <summary>
        /// Recovers editable filter rows from a saved tree — the inverse of
        /// <see cref="BuildRoot"/> for everything the flat builder can express.
        /// </summary>
        public static QuickScreenShape FromRoot(ConditionNode? root)
        {
            var filters = new List<QuickFilter>();

            switch (root)
            {
                case ConditionGroup group:
                    foreach (var leaf in group.Children.OfType<ConditionLeaf>())
                        filters.Add(ToFilter(leaf));
                    return new QuickScreenShape(
                        group.Logic,
                        group.ScoreThreshold ?? 1.0,
                        filters,
                        group.Children.Any(c => c is not ConditionLeaf));

                case ConditionLeaf leaf:
                    filters.Add(ToFilter(leaf));
                    return new QuickScreenShape(LogicOperator.And, 1.0, filters, false);

                default:
                    return new QuickScreenShape(LogicOperator.And, 1.0, filters, false);
            }
        }

        private static QuickFilter ToFilter(ConditionLeaf leaf) => new()
        {
            IndicatorCode = IndicatorCodeOf(leaf.SignalDescriptorId),
            SignalId = leaf.SignalDescriptorId,
            Operator = leaf.Operator,
            Value = leaf.Value,
            Value2 = leaf.Value2 ?? 0,
            WithinNBars = leaf.WithinNBars,
            Score = leaf.Score,
        };

        /// <summary>
        /// Signal ids are <c>"{INDICATOR_CODE}.{component}"</c>. Component names can themselves
        /// contain dots, so this splits at the FIRST dot, not the last.
        /// </summary>
        public static string IndicatorCodeOf(string signalId)
        {
            if (string.IsNullOrEmpty(signalId)) return "";
            int dot = signalId.IndexOf('.');
            return dot > 0 ? signalId[..dot] : signalId;
        }

        /// <summary>
        /// Applies the picker's substring filter and the display cap in one place.
        ///
        /// <para>
        /// The cap exists because some providers list several thousand symbols and rendering all
        /// of them into a select makes the dialog unusable — but it must never be silent, which is
        /// why the caller reports both counts. Matching is case-insensitive because symbol casing
        /// varies by provider and a user typing "usdt" means BTC/USDT.
        /// </para>
        /// </summary>
        public static List<string> FilterSymbols(IReadOnlyList<string> all, string? filter, int max)
        {
            if (all == null || all.Count == 0) return new List<string>();

            IEnumerable<string> q = all;
            if (!string.IsNullOrWhiteSpace(filter))
                q = q.Where(s => s.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));

            return max > 0 ? q.Take(max).ToList() : q.ToList();
        }
    }
}
