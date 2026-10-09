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

        /// <summary>
        /// Starts — and, once their data has aged, restarts — the higher-timeframe loads this
        /// tree's leaves need for <paramref name="identity"/>, and says whether every one of them
        /// has finished at least once. The evaluator reads HTF data synchronously from a cache
        /// that only these loads fill; a strategy fills it from <c>Initialize</c>, and until
        /// 2026-10 nothing in the alert path filled it at all, so every HTF leaf in an advanced
        /// alert was false forever. False means "still loading — do not evaluate yet": a NOT
        /// over an unloaded leaf would otherwise be TRUE and fire.
        /// </summary>
        /// <remarks>Defaulted to "nothing to load" for evaluators that have no timeframe
        /// service.</remarks>
        bool PrepareTimeframes(ConditionNode root, ChartIdentity identity, int barCount) => true;
    }
}
