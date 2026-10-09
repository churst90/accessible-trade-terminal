using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Leaves that can never be true however the market moves, found before the tree is armed.
    ///
    /// <para>
    /// The condition evaluator answers such a leaf with false on every bar — correctly, and
    /// silently. For an advanced alert that is the worst shape a failure takes in this product:
    /// "Alert added" was spoken, and the user stops watching. The alerts dialog runs every new
    /// alert through <c>BackgroundWatchability.WhyUnfireable</c>, which asks here for trees, so
    /// these are refused at creation with the reason said, rather than accepted and dead.
    /// </para>
    ///
    /// <para>
    /// Structural only: no catalog, no chart. Whether the indicator is on the chart is a
    /// different question with a different answer later (it can be added), and the editor
    /// already warns about it.
    /// </para>
    /// </summary>
    public static class ConditionTreeChecks
    {
        /// <summary>Why the tree has a leaf that can never fire; null = none found.</summary>
        /// <param name="chartTimeframe">The timeframe the alert is evaluated on, when known — a
        /// crossed line below it has no honest reading.</param>
        public static string? WhyUnfireable(ConditionNode? root, string? chartTimeframe = null)
            => root == null ? null : Check(root, chartTimeframe, insideSequence: false);

        private static string? Check(ConditionNode node, string? chartTimeframe, bool insideSequence)
        {
            switch (node)
            {
                case ConditionGroup g:
                    foreach (var c in g.Children)
                        if (Check(c, chartTimeframe, insideSequence || g.Logic == LogicOperator.Sequence) is { } why)
                            return why;
                    return null;

                case ConditionLeaf l:
                    if (string.IsNullOrWhiteSpace(l.SignalDescriptorId))
                        return "a condition has no indicator chosen";

                    bool lineCross = l.Operator is LeafOperator.CrossesAboveLine or LeafOperator.CrossesBelowLine;

                    if (insideSequence && (lineCross || !string.IsNullOrEmpty(l.Timeframe)))
                        return lineCross
                            ? "a crosses-line condition cannot be part of a Sequence group"
                            : "a higher-timeframe condition cannot be part of a Sequence group";

                    if (!lineCross) return null;

                    if (string.IsNullOrWhiteSpace(l.SecondSignalDescriptorId))
                        return "a crosses-line condition has no line to cross chosen";

                    string? secondTf = l.SecondTimeframe ?? l.Timeframe;
                    if (string.Equals(l.SignalDescriptorId, l.SecondSignalDescriptorId, StringComparison.OrdinalIgnoreCase)
                        && SameParameters(l.Parameters, l.SecondParameters)
                        && string.Equals(l.Timeframe ?? "", secondTf ?? "", StringComparison.Ordinal))
                        return "a crosses-line condition crosses a line with itself, and a line never crosses itself: " +
                               "choose another line, or give it a different timeframe under Line's timeframe";

                    if (!string.IsNullOrEmpty(l.Timeframe) && !string.IsNullOrEmpty(l.SecondTimeframe)
                        && !string.Equals(l.Timeframe, l.SecondTimeframe, StringComparison.Ordinal))
                        return $"a {l.Timeframe} condition can only cross a line on {l.Timeframe}, not {l.SecondTimeframe}";

                    if (string.IsNullOrEmpty(l.Timeframe) && !string.IsNullOrEmpty(l.SecondTimeframe)
                        && ConditionEvaluator.IsLowerTimeframe(l.SecondTimeframe, chartTimeframe))
                        return $"the line crossed is on {l.SecondTimeframe}, lower than the chart's {chartTimeframe}";

                    return null;

                default:
                    return null;
            }
        }

        private static bool SameParameters(IReadOnlyDictionary<string, double>? a, IReadOnlyDictionary<string, double>? b)
        {
            if ((a == null || a.Count == 0) && (b == null || b.Count == 0)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            return ConditionEvaluator.ParametersMatch(a, b);
        }
    }
}
