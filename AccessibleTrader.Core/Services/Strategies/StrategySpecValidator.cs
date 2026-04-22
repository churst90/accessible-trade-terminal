using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Spec-shape validation for the Build-Setup flow. Mirrors the runtime gate in
    /// <see cref="AccessibleTrader.Core.Strategies.ConfigurableStrategy"/> so the UI refuses
    /// saves up-front rather than letting the engine silently auto-promote a trigger after
    /// the fact — which was the silent-fail that Day 4 of the 2026-04-22 audit flagged.
    /// </summary>
    public static class StrategySpecValidator
    {
        /// <summary>
        /// Returns true when every leaf in the tree uses a one-bar transient operator
        /// (Fired, CrossesAbove/Below, etc.). Such trees are structurally a single-bar pulse:
        /// they cannot satisfy any non-Immediate entry trigger and the engine auto-promotes
        /// them to Immediate execution. Mirrors ConfigurableStrategy.IsPurePulseTree.
        /// </summary>
        public static bool IsPurePulseTree(EditableConditionNode? n)
        {
            if (n == null) return false;
            if (n.IsGroup)
            {
                if (n.Children.Count == 0) return false;
                foreach (var c in n.Children)
                    if (!IsPurePulseTree(c)) return false;
                return true;
            }
            return n.Operator switch
            {
                LeafOperator.Fired             => true,
                LeafOperator.CrossesAbove      => true,
                LeafOperator.CrossesBelow      => true,
                LeafOperator.CrossesAboveLine  => true,
                LeafOperator.CrossesBelowLine  => true,
                LeafOperator.ChangesDirection  => true,
                LeafOperator.PriceBreaksLevel  => true,
                LeafOperator.PriceRejectsLevel => true,
                LeafOperator.WickIntoLvn       => true,
                _ => false
            };
        }

        /// <summary>
        /// Pre-save / pre-add validation. Returns a non-null error message when the spec cannot be
        /// saved as-configured; returns null when the spec is safe to persist.
        ///
        /// The rule enforced here is: a pure-pulse condition tree cannot carry a non-Immediate
        /// entry trigger. Refusing the save up-front keeps the saved spec honest — what the user
        /// hears narrated is what the engine will actually run.
        /// </summary>
        public static string? ValidateForSave(EditableStrategySpec spec)
        {
            if (IsPurePulseTree(spec.Root) && spec.EntryKind != EntryTriggerKind.Immediate)
            {
                return $"Cannot save: every condition is a one-bar pulse, so the {spec.EntryKind} entry " +
                       "trigger can never fire. Either switch the trigger to Immediate, or AND a " +
                       "persistent condition (e.g. an oscillator threshold) into the tree so the " +
                       "setup stays armed long enough for the trigger to resolve.";
            }
            return null;
        }

        /// <summary>
        /// Builds an advisory string explaining the pulse-only situation, or null when there's
        /// nothing to flag. Appended to the save-confirmation message so the user knows when
        /// engine auto-promotion will take effect.
        /// </summary>
        public static string? BuildPulseOnlyAdvisory(EditableStrategySpec spec)
        {
            if (!IsPurePulseTree(spec.Root)) return null;
            if (spec.EntryKind == EntryTriggerKind.Immediate)
            {
                return "Note: every condition is a one-bar pulse. The strategy will fire on the bar " +
                       "the pulse appears (Immediate). Consider AND-ing a persistent gate (e.g. WT < -53) " +
                       "if you want fewer, higher-quality entries.";
            }
            return "Warning: every condition is a one-bar pulse, so the configured entry trigger " +
                   $"({spec.EntryKind}) cannot be satisfied — by the next bar the conditions are already " +
                   "false again. The engine will auto-promote this strategy to Immediate execution. " +
                   "To use a deferred trigger, AND a persistent condition (e.g. an oscillator threshold) " +
                   "into the tree so the setup stays armed long enough to wait for the trigger.";
        }
    }
}
