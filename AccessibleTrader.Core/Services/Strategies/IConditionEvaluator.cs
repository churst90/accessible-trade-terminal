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
    }
}
