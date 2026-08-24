using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Evaluates a <see cref="ConditionNode"/> tree against the current workspace state.
    /// Returns per-leaf results so callers (notably <c>ConfigurableStrategy</c>) can detect
    /// dropouts — leaves that flipped from true to false since the previous evaluation.
    ///
    /// The evaluator does not own state; it is a pure function of (tree, history, workspace).
    /// State (per-leaf last result, debounce counters) lives on the calling strategy.
    /// </summary>
    public interface IConditionEvaluator
    {
        /// <summary>
        /// Walk the tree, look up each leaf's signal source in the active workspace,
        /// apply the leaf operator, and combine via AND/OR/NOT at each group.
        /// </summary>
        ConditionEvaluation Evaluate(
            ConditionNode root,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state);

        /// <summary>
        /// Why the most recent <see cref="Evaluate"/> could not honestly answer a leaf —
        /// an HTF leaf with no pre-warmed data, or a component the causality contract
        /// refuses — or null when it answered every leaf it was asked about.
        ///
        /// On the interface rather than only on the concrete class because a false tree
        /// and an *unanswerable* tree are the same silence to the user, and the layer that
        /// has to tell them apart (the alerts path) holds this type, not the concrete one.
        /// Cleared at the start of every Evaluate, so it describes the last call only.
        /// </summary>
        string? LastDegradation { get; }
    }
}
