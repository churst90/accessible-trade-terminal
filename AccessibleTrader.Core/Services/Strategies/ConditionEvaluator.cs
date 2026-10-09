using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="IConditionEvaluator"/>. Resolves each leaf's signal value via
    /// <see cref="ISignalCatalog"/> + the workspace's active series, applies the leaf operator,
    /// then folds results up the tree using AND/OR/NOT semantics.
    ///
    /// Multi-timeframe leaves (those with <see cref="ConditionLeaf.Timeframe"/> set) read the
    /// pre-warmed cache of <see cref="IMultiTimeframeDataService"/>, and only the line they name:
    /// missing data is false and recorded on <see cref="LastDegradation"/>, never another series.
    /// With no timeframe service at all (the StrategyLab passes none) they still read the
    /// active-TF series. That is the same wrong-series defect, left in place on purpose in 2026-10
    /// because fixing it changes StrategyLab results; it is Cody's call, not a quiet edit.
    /// </summary>
    public class ConditionEvaluator : IConditionEvaluator
    {
        private readonly ISignalCatalog _catalog;
        private readonly IMultiTimeframeDataService? _mtf;
        private readonly ILevelService? _levels;

        // Per-(leafId,timeframe) warning dedup. Using static shared state previously
        // meant the first HTF miss anywhere in the process silenced every subsequent
        // degradation across every strategy and every session — a long-running app
        // saw the warning once and never again. Per-key tracking lets each distinct
        // leaf surface its degradation at least once per session, while still
        // rate-limiting a single chatty leaf to one log line.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _htfWarningsEmitted = new();

        /// <summary>
        /// Records the most recent reason a leaf returned false without being evaluated, for UI
        /// surfacing. Cleared at the start of each Evaluate call. Two causes: an HTF leaf whose
        /// data has not been pre-warmed, and a leaf pointing at a component the causality contract
        /// refuses to publish. Both leave the strategy running but incomplete, and both are
        /// invisible from the outside — a leaf that never fires looks identical to a market that
        /// never met the condition. UI layers read this to say which it was.
        /// </summary>
        public string? LastDegradation { get; private set; }

        /// <inheritdoc/>
        public string? LastDegradationRemedy { get; private set; }

        // What the user can do about each kind of unanswerable leaf. The alerts path used to close
        // every degradation with "Check the indicator it references is on this chart" — wrong for
        // a weekly series the provider did not serve, which no chart change fixes.
        private const string EditRemedy = "Edit the condition in the Alerts dialog.";
        private static string LoadRemedy(string tf) =>
            $"Its {tf} data could not be loaded from the provider; it will keep trying.";

        public ConditionEvaluator(
            ISignalCatalog catalog,
            IMultiTimeframeDataService? mtf = null,
            ILevelService? levels = null)
        {
            _catalog = catalog;
            _mtf = mtf;
            _levels = levels;
        }

        public ConditionEvaluation Evaluate(
            ConditionNode root,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state)
        {
            var leafResults = new Dictionary<string, bool>();
            double score = 0.0;
            double maxScore = 0.0;
            LastDegradation = null;
            LastDegradationRemedy = null;

            bool overall = EvaluateNode(root, history, state, leafResults, ref score, ref maxScore);

            return new ConditionEvaluation(overall, leafResults, score, maxScore);
        }

        private bool EvaluateNode(
            ConditionNode node,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state,
            Dictionary<string, bool> leafResults,
            ref double score,
            ref double maxScore)
        {
            switch (node)
            {
                case ConditionLeaf leaf:
                {
                    bool result = EvaluateLeaf(leaf, history, state);
                    leafResults[leaf.Id] = result;
                    maxScore += Math.Max(0.0, leaf.Score);
                    if (result) score += Math.Max(0.0, leaf.Score);
                    return result;
                }

                case ConditionGroup group:
                {
                    if (group.Children.Count == 0) return false;

                    if (group.Logic == LogicOperator.Not)
                    {
                        // NOT must wrap a single child — defensive: if more, treat as NOT(AND(children)).
                        // ref params can't be captured by lambdas/All so we use a manual loop.
                        bool inner = true;
                        foreach (var c in group.Children)
                        {
                            bool r = EvaluateNode(c, history, state, leafResults, ref score, ref maxScore);
                            inner = inner && r;
                        }
                        return !inner;
                    }

                    // Sequence logic: enforce a chronological ordering. Children must be
                    // leaves; the evaluator walks backward from the current bar, finding
                    // the latest bar where the LAST child fired within its WithinNBars
                    // budget, then the latest bar where the PREVIOUS child fired in the
                    // window before that, and so on. If every child can be placed in order
                    // with each step strictly later than the previous, the sequence fires.
                    // Children do not contribute to score/maxScore (Sequence is a gate, not
                    // a sum). Leaf results are still recorded for dropout/debug visibility.
                    if (group.Logic == LogicOperator.Sequence)
                    {
                        return EvaluateSequence(group, history, state, leafResults);
                    }

                    // Score logic: evaluate every child, measure the subtree score delta, and
                    // fire when delta >= ScoreThreshold. This is the v7 confluence operator —
                    // a strategy can require e.g. 3.0 weighted points across 6 candidate leaves
                    // rather than insisting all 6 be true on the same bar. ScoreThreshold null
                    // gracefully degrades to OR so a misconfigured spec still evaluates.
                    if (group.Logic == LogicOperator.Score)
                    {
                        double scoreBefore = score;
                        foreach (var child in group.Children)
                        {
                            EvaluateNode(child, history, state, leafResults, ref score, ref maxScore);
                        }
                        double subtreeScore = score - scoreBefore;
                        if (!group.ScoreThreshold.HasValue)
                        {
                            // No threshold → OR fallback
                            return subtreeScore > 0;
                        }
                        return subtreeScore >= group.ScoreThreshold.Value;
                    }

                    bool combined = group.Logic == LogicOperator.And;
                    foreach (var child in group.Children)
                    {
                        // Always evaluate every child so leafResults is fully populated
                        // (needed for dropout detection even when AND short-circuits would skip).
                        bool r = EvaluateNode(child, history, state, leafResults, ref score, ref maxScore);
                        combined = group.Logic == LogicOperator.And ? (combined && r) : (combined || r);
                    }
                    return combined;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// True when this descriptor may not be evaluated because its component reads later bars.
        ///
        /// <para>
        /// Every path that resolves a descriptor and then reads its data goes through here — the
        /// plain leaf, the per-bar read the Sequence operator uses, and the second descriptor of a
        /// crosses-line comparison. Checking only the first would leave the other two able to reach
        /// a look-ahead component by a slightly different route, which is the shape most of this
        /// codebase's repeat defects have taken.
        /// </para>
        ///
        /// <para>
        /// A refused leaf returns false, as it must — but silently returning false is what a
        /// strategy saved before the causality contract existed would look like from the outside:
        /// still running, never firing, no reason given. So it is recorded on
        /// <see cref="LastDegradation"/> and logged once per ID.
        /// </para>
        /// </summary>
        private bool RefusedForCausality(SignalDescriptor desc)
        {
            if (desc.Causality == ComponentCausality.Causal) return false;

            string refusal = _catalog.RefusalReason(desc.Id)
                ?? $"{desc.Id} is not available as a strategy signal: it is declared {desc.Causality}.";
            LastDegradation = refusal;
            LastDegradationRemedy = null;
            if (_htfWarningsEmitted.TryAdd($"causality|{desc.Id}", 0))
                System.Diagnostics.Debug.WriteLine($"[ConditionEvaluator] {refusal} The leaf evaluates false at every bar.");
            return true;
        }

        private bool EvaluateLeaf(ConditionLeaf leaf, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            var desc = _catalog.GetById(leaf.SignalDescriptorId);
            if (desc == null) return false;

            if (RefusedForCausality(desc)) return false;

            // Multi-timeframe routing (Session B foundation):
            // If the leaf has a Timeframe set and the MTF service is available, use HTF cached
            // bars where applicable. Indicator-based HTF leaves require running the indicator
            // engine on raw HTF bars — that wiring lands in a follow-up session along with
            // pre-warm cache infrastructure (the evaluator runs sync per bar and cannot await).
            // For now, HTF leaves fall through to active-TF data with a one-time warning so
            // the strategy still produces meaningful results until full HTF indicator support
            // ships. Price-based HTF comparisons (close > value, etc.) on the cached HTF bars
            // are the supported subset for this session.
            if (!string.IsNullOrEmpty(leaf.Timeframe) && _mtf != null)
            {
                string prov = state.Identity.Provider ?? string.Empty;
                string sym  = state.Identity.Symbol   ?? string.Empty;

                // The pre-warmed HTF cache. ConfigurableStrategy.Initialize — and, for an alert,
                // PrepareTimeframes — loads every HTF line the tree reads, so by the time
                // evaluation runs the arrays are sitting in MultiTimeframeDataService's cache:
                // a sync read on the hot path.
                // Determine the last CLOSED HTF bar at the strategy's current main-TF time.
                // Without this clip the HTF cache leaks future values — at main-TF bar 100 we'd
                // otherwise read htfData[^1] (the final HTF value in the entire cached series).
                var htfBars = _mtf.GetCachedBars(prov, sym, leaf.Timeframe);
                int htfEndExclusive = htfBars.Count > 0 && history.Count > 0
                    ? HtfLastClosedIndexExclusive(htfBars, history[^1].Date)
                    : htfBars.Count;

                // The line the leaf names, on its timeframe — and ONLY that line. Until 2026-10 an
                // indicator leaf whose indicator had not been computed fell through to the raw
                // HTF bars and tested the weekly CLOSE: "1w SMA 50 > 100" came back true because
                // price was above 100. No data is false and said; a different series is never
                // substituted. Price itself (Candles / Price) is read from the HTF bars.
                var htfData = HtfLine(leaf, desc, leaf.Parameters, leaf.Timeframe, prov, sym, htfBars);
                if (htfData == null) return false;

                int clip = htfBars.Count > 0
                    ? Math.Min(htfEndExclusive, htfData.Length)
                    : htfData.Length;

                if (leaf.Operator is LeafOperator.CrossesAboveLine or LeafOperator.CrossesBelowLine)
                    return CrossesLineOnHtf(leaf, htfData, clip, htfBars, htfEndExclusive, prov, sym,
                        above: leaf.Operator == LeafOperator.CrossesAboveLine);

                return EvaluateHtfIndicatorLeaf(leaf, htfData, clip);
            }

            // Resolve component data from the workspace's active series — the instance the leaf
            // is bound to, or the first with its code for a leaf bound to none.
            var data = ChartLine(desc, leaf.Parameters, history, state);
            if (data == null || data.Length == 0) return false;

            // Future-leak fix: in backtest mode the strategy walks history bar-by-bar but the
            // workspace's component arrays carry the FULL pre-computed indicator series. Reading
            // data[^1] would surface the indicator's final value at every historical bar — the
            // strategy at backtest bar 100 would see Cipher A signals from bar 800. Clip the read
            // to the strategy's current bar index. In live mode history.Count == data.Length so
            // the clip is a no-op.
            int curIdx = System.Math.Min(history.Count, data.Length) - 1;
            if (curIdx < 0) return false;
            double cur  = data[curIdx];
            double prev = curIdx >= 1 ? data[curIdx - 1] : double.NaN;

            return leaf.Operator switch
            {
                LeafOperator.Fired         => !double.IsNaN(cur),
                LeafOperator.FiredWithin   => FiredWithin(data, leaf.WithinNBars, history.Count),
                LeafOperator.GreaterThan   => !double.IsNaN(cur) && cur >  leaf.Value,
                LeafOperator.LessThan      => !double.IsNaN(cur) && cur <  leaf.Value,
                LeafOperator.Between       => !double.IsNaN(cur) && cur >= leaf.Value && cur <= (leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.CrossesAbove  => !double.IsNaN(cur) && !double.IsNaN(prev) && prev <= leaf.Value && cur >  leaf.Value,
                LeafOperator.CrossesBelow  => !double.IsNaN(cur) && !double.IsNaN(prev) && prev >= leaf.Value && cur <  leaf.Value,
                LeafOperator.CrossesAboveLine => CrossesLine(history, state, leaf, curIdx, cur, prev, above: true),
                LeafOperator.CrossesBelowLine => CrossesLine(history, state, leaf, curIdx, cur, prev, above: false),
                LeafOperator.ChangesDirection => DirectionChanged(data, history.Count),
                LeafOperator.AboveCloud      => PriceVsCloud(history, state, desc, +1),
                LeafOperator.BelowCloud      => PriceVsCloud(history, state, desc, -1),
                LeafOperator.InsideCloud     => PriceVsCloud(history, state, desc,  0),
                LeafOperator.PriceRejectsLevel    => PriceRejectsLevel(history, state, leaf),
                LeafOperator.PriceBreaksLevel     => PriceBreaksLevel(history, state, leaf),
                LeafOperator.BarClosesAbovePoc    => BarClosesVsPoc(history, state, +1),
                LeafOperator.BarClosesBelowPoc    => BarClosesVsPoc(history, state, -1),
                LeafOperator.PriceInsideValueArea => PriceInsideValueArea(history, state),
                LeafOperator.PriceOutsideValueArea=> !PriceInsideValueArea(history, state),
                LeafOperator.WickIntoLvn          => WickIntoLvn(history, state),
                LeafOperator.GreaterThanWithin    => ValueWithin(data, curIdx + 1, leaf.WithinNBars, leaf.Value, above: true),
                LeafOperator.LessThanWithin       => ValueWithin(data, curIdx + 1, leaf.WithinNBars, leaf.Value, above: false),
                LeafOperator.BetweenWithin        => BetweenWithin(data, curIdx + 1, leaf.WithinNBars, leaf.Value, leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.PercentileBelow      => PercentileCompare(data, curIdx + 1, leaf.WithinNBars, leaf.Value, below: true),
                LeafOperator.PercentileAbove      => PercentileCompare(data, curIdx + 1, leaf.WithinNBars, leaf.Value, below: false),
                _ => false
            };
        }

        /// <summary>
        /// v10 Sequence operator. Children must all be ConditionLeaf (sub-groups inside a
        /// Sequence are unsupported and treated as failure). Walks the children list in
        /// reverse: finds the latest bar in the last <c>children[N-1].WithinNBars</c> bars
        /// where the last child fires; then finds the latest bar BEFORE that in the last
        /// <c>children[N-2].WithinNBars</c> bars where the previous child fires; and so on.
        /// All children must place in strictly chronological order to satisfy the sequence.
        ///
        /// This expresses the classic cipher confluence setup natively: "Anchor washed out, THEN Trigger
        /// crossed up, THEN buy signal fired" — instead of v9's parallel "all of these
        /// happened in their own windows independently" which loses causal ordering and
        /// fires on bars where the ingredients existed in the wrong order.
        ///
        /// Each child's <c>WithinNBars</c> is the budget for that step's window relative to
        /// the NEXT step (or for the last child, relative to the current bar). A child with
        /// WithinNBars=0 is treated as 1 (must fire on exactly its anchor bar).
        /// </summary>
        private bool EvaluateSequence(
            ConditionGroup group,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state,
            Dictionary<string, bool> leafResults)
        {
            if (history.Count == 0) return false;
            if (group.Children.Count == 0) return false;

            // Sub-groups inside Sequence are not supported. Validate up front.
            var leaves = new List<ConditionLeaf>(group.Children.Count);
            foreach (var c in group.Children)
            {
                if (c is ConditionLeaf l) leaves.Add(l);
                else return false;
            }

            // Walk backward through the leaves (most recent step first). Cursor starts at
            // the current bar; for each step, find the latest bar in [cursor - budget + 1,
            // cursor] where the leaf fires; then move cursor to (foundBar - 1) for the next
            // (earlier) step. If any step cannot be placed, the whole sequence fails.
            int cursor = history.Count - 1;
            // Track results for each leaf so the leafResults map gets populated even when
            // the sequence ultimately fails — useful for downstream dropout detection.
            var stepFireBars = new int[leaves.Count];
            for (int i = leaves.Count - 1; i >= 0; i--)
            {
                var leaf = leaves[i];
                int budget = leaf.WithinNBars > 0 ? leaf.WithinNBars : 1;
                int windowStart = Math.Max(0, cursor - budget + 1);
                int found = -1;
                for (int j = cursor; j >= windowStart; j--)
                {
                    if (EvaluateLeafAtBar(leaf, history, state, j))
                    {
                        found = j;
                        break;
                    }
                }
                if (found < 0)
                {
                    // Step missed. Record what we know so far for visibility, then fail.
                    leafResults[leaf.Id] = false;
                    for (int k = i - 1; k >= 0; k--)
                        leafResults[leaves[k].Id] = false;
                    return false;
                }
                stepFireBars[i] = found;
                leafResults[leaf.Id] = true;
                cursor = found - 1; // next (earlier) step must fire strictly before this one
                if (cursor < 0 && i > 0) return false;
            }
            return true;
        }

        /// <summary>
        /// Per-bar leaf evaluator used by the Sequence operator. Same operator switch as
        /// the main <see cref="EvaluateLeaf"/> path, but reads data at a specific bar index
        /// instead of the current end-of-history. Within-style operators are reduced to
        /// their per-bar base form (e.g. LessThanWithin → LessThan at this bar) because the
        /// sequence's own walking-window already provides the lookback semantics.
        /// </summary>
        private bool EvaluateLeafAtBar(
            ConditionLeaf leaf,
            IReadOnlyList<Ohlcv> history,
            WorkspaceState state,
            int barIndex)
        {
            if (barIndex < 0) return false;
            var desc = _catalog.GetById(leaf.SignalDescriptorId);
            if (desc == null) return false;
            if (RefusedForCausality(desc)) return false;

            // This walk reads the chart's own bars, one index at a time, and has no notion of a
            // second timeframe. It used to ignore Timeframe altogether, so a "1w" leaf inside a
            // Sequence quietly read the chart-timeframe series of the same indicator. Refused
            // and said instead: never a different series than the one the leaf names.
            if (!string.IsNullOrEmpty(leaf.Timeframe) || !string.IsNullOrEmpty(leaf.SecondTimeframe))
            {
                Degrade($"seq|{leaf.Id}",
                    $"a higher-timeframe condition ({desc.DisplayLabel}, {leaf.Timeframe ?? leaf.SecondTimeframe}) cannot be part of a Sequence group");
                return false;
            }

            var data = ChartLine(desc, leaf.Parameters, history, state);
            if (data == null || data.Length == 0) return false;
            int idx = Math.Min(barIndex, data.Length - 1);
            if (idx < 0) return false;

            double cur = data[idx];
            double prev = idx >= 1 ? data[idx - 1] : double.NaN;

            return leaf.Operator switch
            {
                LeafOperator.Fired             => !double.IsNaN(cur),
                LeafOperator.FiredWithin       => !double.IsNaN(cur), // per-bar reduction
                LeafOperator.GreaterThan       => !double.IsNaN(cur) && cur >  leaf.Value,
                LeafOperator.GreaterThanWithin => !double.IsNaN(cur) && cur >  leaf.Value,
                LeafOperator.LessThan          => !double.IsNaN(cur) && cur <  leaf.Value,
                LeafOperator.LessThanWithin    => !double.IsNaN(cur) && cur <  leaf.Value,
                LeafOperator.Between           => !double.IsNaN(cur) && cur >= leaf.Value && cur <= (leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.BetweenWithin     => !double.IsNaN(cur) && cur >= leaf.Value && cur <= (leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.CrossesAbove      => !double.IsNaN(cur) && !double.IsNaN(prev) && prev <= leaf.Value && cur >  leaf.Value,
                LeafOperator.CrossesBelow      => !double.IsNaN(cur) && !double.IsNaN(prev) && prev >= leaf.Value && cur <  leaf.Value,
                LeafOperator.ChangesDirection  => idx >= 2 && DirectionChangedAt(data, idx),
                _ => false // Cloud / Level / VPVR / Percentile operators not supported in Sequence
            };
        }

        /// <summary>Three-bar direction change check at a specific bar index.</summary>
        private static bool DirectionChangedAt(double[] data, int idx)
        {
            double a = data[idx - 2], b = data[idx - 1], c = data[idx];
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return false;
            int s1 = Math.Sign(b - a), s2 = Math.Sign(c - b);
            return s1 != 0 && s2 != 0 && s1 != s2;
        }

        /// <summary>
        /// True when the current close is between any VAL/VAH pair from any volume-profile
        /// series. Iterates the merged level set, pairing VAL and VAH levels by source label.
        /// Returns false when no profile is loaded or the close is outside every value area.
        /// </summary>
        private bool PriceInsideValueArea(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (_levels == null || history.Count == 0) return false;
            double close = history[^1].Close;
            var all = _levels.GetAllLevels(history, state);

            // Group by source so VAH and VAL from the same profile pair correctly.
            var bySource = new Dictionary<string, (double? vah, double? val)>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var lvl in all)
            {
                if (lvl.Kind != LevelKind.Vah && lvl.Kind != LevelKind.Val) continue;
                // Source labels are "VPVR VAH" / "VPVR VAL" — strip trailing token to pair them.
                string key = lvl.Source.Replace(" VAH", "").Replace(" VAL", "");
                bySource.TryGetValue(key, out var pair);
                if (lvl.Kind == LevelKind.Vah) pair.vah = lvl.Price;
                else                            pair.val = lvl.Price;
                bySource[key] = pair;
            }

            foreach (var (_, pair) in bySource)
            {
                if (!pair.vah.HasValue || !pair.val.HasValue) continue;
                double hi = System.Math.Max(pair.vah.Value, pair.val.Value);
                double lo = System.Math.Min(pair.vah.Value, pair.val.Value);
                if (close >= lo && close <= hi) return true;
            }
            return false;
        }

        /// <summary>
        /// True when this bar's wick crossed any LVN level — current bar low &lt; lvn &lt; current
        /// bar high. The bar may or may not have closed across the level. The breakout-pre-emptive
        /// "price tested an LVN intrabar" primitive that's hard to express with close-only operators.
        /// </summary>
        private bool WickIntoLvn(IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            if (_levels == null || history.Count == 0) return false;
            var bar = history[^1];
            foreach (var lvl in _levels.GetAllLevels(history, state))
            {
                if (lvl.Kind != LevelKind.Lvn) continue;
                if (bar.Low <= lvl.Price && lvl.Price <= bar.High) return true;
            }
            return false;
        }

        // ── Level-based leaf operators (Session C) ────────────────────────────

        /// <summary>
        /// True when, within the last <c>WithinNBars</c> bars, price came within
        /// <c>leaf.Value</c> (fractional tolerance, e.g. 0.001 = 0.1%) of any level
        /// from any registered <see cref="ILevelProvider"/> and then closed away from it.
        /// "Away" means: the most recent close is on the opposite side of the touched level
        /// relative to the touch direction (touched a support → close above; touched a
        /// resistance → close below).
        /// </summary>
        private bool PriceRejectsLevel(IReadOnlyList<Ohlcv> history, WorkspaceState state, ConditionLeaf leaf)
        {
            if (_levels == null || history.Count < 2) return false;
            var levels = FilterByStrength(_levels.GetAllLevels(history, state), leaf.MinLevelStrength);
            if (levels.Count == 0) return false;

            int n = Math.Max(1, leaf.WithinNBars);
            int from = Math.Max(0, history.Count - n);
            double tol = leaf.Value > 0 ? leaf.Value : 0.001;
            double curClose = history[^1].Close;

            for (int i = from; i < history.Count; i++)
            {
                var bar = history[i];
                foreach (var lvl in levels)
                {
                    double tolPx = lvl.Price * tol;
                    bool touched = bar.Low <= lvl.Price + tolPx && bar.High >= lvl.Price - tolPx;
                    if (!touched) continue;

                    // Reject from support: close above. Reject from resistance: close below.
                    bool isSupport = lvl.Kind == LevelKind.Support
                                  || lvl.Kind == LevelKind.Pivot
                                  || lvl.Kind == LevelKind.Kijun
                                  || lvl.Kind == LevelKind.KumoBottom
                                  || lvl.Kind == LevelKind.Val
                                  || lvl.Kind == LevelKind.Hvn;
                    if (isSupport && curClose > lvl.Price) return true;

                    bool isResistance = lvl.Kind == LevelKind.Resistance
                                     || lvl.Kind == LevelKind.Pivot
                                     || lvl.Kind == LevelKind.KumoTop
                                     || lvl.Kind == LevelKind.Vah;
                    if (isResistance && curClose < lvl.Price) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when this bar's open and close straddle any level — i.e. price closed on the
        /// opposite side of a level from where it opened. This is the breakout primitive.
        /// </summary>
        private bool PriceBreaksLevel(IReadOnlyList<Ohlcv> history, WorkspaceState state, ConditionLeaf leaf)
        {
            if (_levels == null || history.Count == 0) return false;
            var levels = FilterByStrength(_levels.GetAllLevels(history, state), leaf.MinLevelStrength);
            if (levels.Count == 0) return false;

            var bar = history[^1];
            double tol = leaf.Value > 0 ? leaf.Value : 0.0;
            foreach (var lvl in levels)
            {
                double tolPx = lvl.Price * tol;
                bool brokeUp   = bar.Open <  lvl.Price - tolPx && bar.Close > lvl.Price + tolPx;
                bool brokeDown = bar.Open >  lvl.Price + tolPx && bar.Close < lvl.Price - tolPx;
                if (brokeUp || brokeDown) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the current bar closes above (sign = +1) or below (sign = -1) the
        /// volume-profile POC level, when one is exposed via <see cref="ILevelProvider"/>.
        /// Currently no provider exposes POC; the operator returns false until VPVR/TPO
        /// integration ships in a follow-up session.
        /// </summary>
        private bool BarClosesVsPoc(IReadOnlyList<Ohlcv> history, WorkspaceState state, int sign)
        {
            if (_levels == null || history.Count == 0) return false;
            double close = history[^1].Close;
            foreach (var lvl in _levels.GetAllLevels(history, state))
            {
                if (lvl.Kind != LevelKind.Poc) continue;
                if (sign > 0 && close > lvl.Price) return true;
                if (sign < 0 && close < lvl.Price) return true;
            }
            return false;
        }

        /// <summary>
        /// Evaluate a leaf against a pre-warmed HTF indicator's component data array.
        /// The HTF cache holds the entire computed indicator series for that timeframe — we
        /// read its last value (and previous, for cross operators) and apply the leaf's
        /// operator. Marker / FiredWithin / DirectionChanged operators are honored just like
        /// the active-TF path, so any indicator-component leaf works on HTF as long as the
        /// pre-warm has populated the cache.
        /// </summary>
        private static bool EvaluateHtfIndicatorLeaf(ConditionLeaf leaf, double[] htfData, int endExclusive)
        {
            int upTo = Math.Min(endExclusive, htfData.Length);
            if (upTo <= 0) return false;
            double cur  = htfData[upTo - 1];
            double prev = upTo >= 2 ? htfData[upTo - 2] : double.NaN;

            return leaf.Operator switch
            {
                LeafOperator.Fired         => !double.IsNaN(cur),
                LeafOperator.FiredWithin   => FiredWithin(htfData, leaf.WithinNBars, upTo),
                LeafOperator.GreaterThan   => !double.IsNaN(cur) && cur >  leaf.Value,
                LeafOperator.LessThan      => !double.IsNaN(cur) && cur <  leaf.Value,
                LeafOperator.Between       => !double.IsNaN(cur) && cur >= leaf.Value && cur <= (leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.CrossesAbove  => !double.IsNaN(cur) && !double.IsNaN(prev) && prev <= leaf.Value && cur >  leaf.Value,
                LeafOperator.CrossesBelow  => !double.IsNaN(cur) && !double.IsNaN(prev) && prev >= leaf.Value && cur <  leaf.Value,
                LeafOperator.ChangesDirection => DirectionChanged(htfData, upTo),
                LeafOperator.GreaterThanWithin => ValueWithin(htfData, upTo, leaf.WithinNBars, leaf.Value, above: true),
                LeafOperator.LessThanWithin    => ValueWithin(htfData, upTo, leaf.WithinNBars, leaf.Value, above: false),
                LeafOperator.BetweenWithin     => BetweenWithin(htfData, upTo, leaf.WithinNBars, leaf.Value, leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.PercentileBelow   => PercentileCompare(htfData, upTo, leaf.WithinNBars, leaf.Value, below: true),
                LeafOperator.PercentileAbove   => PercentileCompare(htfData, upTo, leaf.WithinNBars, leaf.Value, below: false),
                _ => false
            };
        }

        /// <summary>
        /// Binary-search the HTF bar list for the most recent bar whose Date is strictly less than
        /// the current main-TF bar time, and return count-exclusive (i.e. "bars[0..result]" is the
        /// slice of closed HTF bars available at main-TF time <paramref name="mainBarDate"/>).
        /// Strictly less than — an HTF bar whose open-time equals the main-TF bar's time has not
        /// yet closed and must not be visible to the strategy.
        /// </summary>
        private static int HtfLastClosedIndexExclusive(IReadOnlyList<Ohlcv> htfBars, DateTime mainBarDate)
        {
            int lo = 0, hi = htfBars.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (htfBars[mid].Date < mainBarDate) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        // ── Which series a line is ──────────────────────────────────────────────

        /// <summary>
        /// The chart's pseudo-indicators whose components are columns of the bars themselves —
        /// the same projection <see cref="Indicators.OfflineWorkspaceBuilder"/> and the live
        /// orchestrator make. Kept in step with <c>CoreIndicatorProvider</c>'s component names.
        /// </summary>
        public static bool IsPriceCode(string? code) =>
            code != null && (code.Equals("CANDLES", StringComparison.OrdinalIgnoreCase)
                          || code.Equals("PRICE", StringComparison.OrdinalIgnoreCase)
                          || code.Equals("VOLUME", StringComparison.OrdinalIgnoreCase));

        /// <summary>The bar column a price descriptor names, projected over <paramref name="bars"/>;
        /// null for a component this projection does not know.</summary>
        private static double[]? ProjectPrice(SignalDescriptor desc, IReadOnlyList<Ohlcv> bars)
        {
            Func<Ohlcv, double>? column = (desc.IndicatorCode.ToUpperInvariant(), desc.ComponentName.ToLowerInvariant()) switch
            {
                ("CANDLES", "body")       => b => b.Close,
                ("CANDLES", "upper_wick") => b => b.High,
                ("CANDLES", "lower_wick") => b => b.Low,
                ("PRICE", "line")         => b => b.Close,
                ("VOLUME", "volume")      => b => b.Volume,
                _ => null
            };
            if (column == null) return null;
            var data = new double[bars.Count];
            for (int i = 0; i < data.Length; i++) data[i] = column(bars[i]);
            return data;
        }

        /// <summary>
        /// The series a leaf line reads on the chart: the one whose code matches and, when the
        /// leaf is bound to an instance, whose parameters match too. A leaf bound to none keeps the
        /// rule every leaf had before 2026-10 — the first series with the code — which is also what
        /// made "SMA 20 crosses SMA 50" impossible: both were "Sma", so both were the SMA 20.
        /// </summary>
        private static ChartSeries? FindSeries(WorkspaceState state, string code, IReadOnlyDictionary<string, double>? parameters)
        {
            // Case-insensitive match because indicator providers may register their codes in
            // different casing than the catalog or the chart series (e.g. "CipherB" vs "CIPHER_B").
            if (parameters == null || parameters.Count == 0)
                return state.ActiveSeries.FirstOrDefault(s =>
                    string.Equals(s.IndicatorCode, code, StringComparison.OrdinalIgnoreCase));
            return state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, code, StringComparison.OrdinalIgnoreCase)
                && ParametersMatch(s.Parameters, parameters));
        }

        /// <summary>Every parameter the leaf names is on the series with the same value.</summary>
        public static bool ParametersMatch(IReadOnlyDictionary<string, double> series, IReadOnlyDictionary<string, double> wanted)
        {
            foreach (var kv in wanted)
            {
                var hit = series.FirstOrDefault(s => string.Equals(s.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (hit.Key == null || Math.Abs(hit.Value - kv.Value) > 1e-9) return false;
            }
            return true;
        }

        /// <summary>"lookbackPeriods 50" — the parameters a leaf is bound to, for speech.</summary>
        public static string DescribeParameters(IReadOnlyDictionary<string, double>? parameters) =>
            parameters == null || parameters.Count == 0
                ? string.Empty
                : string.Join(", ", parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => $"{p.Key} {p.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)}"));

        /// <summary>
        /// One line's values on the chart's timeframe, or null. Price lines fall back to the bars
        /// themselves when no Candles / Price series carries them: the background tab monitor
        /// recomputes every series through the indicator engine, where the pseudo-indicators
        /// produce nothing, so "close crosses the SMA" was dead in a background tab. Where a
        /// series does carry them, it is read exactly as before.
        /// </summary>
        private double[]? ChartLine(SignalDescriptor desc, IReadOnlyDictionary<string, double>? parameters,
            IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            var series = FindSeries(state, desc.IndicatorCode, parameters);
            if (series != null)
            {
                var data = series.GetComponentData(desc.ComponentName);
                if (data != null && data.Length > 0) return data;
            }
            if (IsPriceCode(desc.IndicatorCode)) return ProjectPrice(desc, history);

            // A leaf bound to an instance that is no longer on the chart — the SMA 50 became an
            // SMA 60 — is not a quiet market. Say which instance is missing.
            if (series == null && parameters is { Count: > 0 })
                Degrade($"instance|{desc.Id}|{DescribeParameters(parameters)}",
                    $"{desc.DisplayLabel} with {DescribeParameters(parameters)} is not on this chart",
                    "Add that indicator back to the chart, or edit the condition in the Alerts dialog.");
            return null;
        }

        /// <summary>
        /// One line's values on a higher timeframe, or null with the reason recorded. Price lines
        /// come from the HTF bars; indicator lines only from the indicator computed on those bars
        /// with the leaf's parameters. Nothing else stands in for a missing line.
        /// </summary>
        private double[]? HtfLine(ConditionLeaf leaf, SignalDescriptor desc, IReadOnlyDictionary<string, double>? parameters,
            string timeframe, string prov, string sym, IReadOnlyList<Ohlcv> htfBars)
        {
            if (IsPriceCode(desc.IndicatorCode))
            {
                var projected = htfBars.Count > 0 ? ProjectPrice(desc, htfBars) : null;
                if (projected != null) return projected;
            }
            else
            {
                var cached = _mtf!.GetCachedIndicator(prov, sym, timeframe, desc.IndicatorCode, parameters);
                if (cached != null && cached.TryGetValue(desc.ComponentName, out var data) && data.Length > 0)
                    return data;
            }

            // Strategies simply do not fire until pre-warm completes, which is conservative and
            // correct; an alert does not evaluate until PrepareTimeframes says it has landed, so
            // reaching here there means the load itself failed.
            string what = string.IsNullOrEmpty(DescribeParameters(parameters))
                ? desc.DisplayLabel
                : $"{desc.DisplayLabel} ({DescribeParameters(parameters)})";
            string msg = htfBars.Count > 0 && !IsPriceCode(desc.IndicatorCode)
                ? $"{what} has not been computed on the {timeframe} timeframe"
                : $"the {timeframe} data for {what} has not loaded";
            LastDegradation = msg;
            LastDegradationRemedy = LoadRemedy(timeframe);
            if (_htfWarningsEmitted.TryAdd($"{leaf.Id}|{timeframe}|{desc.Id}", 0))
                System.Diagnostics.Debug.WriteLine(
                    $"[ConditionEvaluator] HTF leaf '{leaf.Id}' on timeframe '{timeframe}': {msg}. " +
                    "The leaf returns false until IMultiTimeframeDataService has the data (strategy Initialize or PrepareTimeframes).");
            return null;
        }

        /// <summary>Records a reason a leaf could not be answered, logging it once per key.</summary>
        private void Degrade(string key, string message, string? remedy = EditRemedy)
        {
            LastDegradation = message;
            LastDegradationRemedy = remedy;
            if (_htfWarningsEmitted.TryAdd(key, 0))
                System.Diagnostics.Debug.WriteLine($"[ConditionEvaluator] {message}. The leaf evaluates false.");
        }

        /// <summary>The value an HTF line had as of a chart bar that opened at <paramref name="asOf"/> —
        /// its last bar closed by then, by the same rule as <see cref="HtfLastClosedIndexExclusive"/>.</summary>
        private static double HtfValueAsOf(double[] data, IReadOnlyList<Ohlcv> htfBars, DateTime asOf)
        {
            int n = Math.Min(HtfLastClosedIndexExclusive(htfBars, asOf), data.Length);
            return n > 0 ? data[n - 1] : double.NaN;
        }

        /// <summary>
        /// Cross above: previous value was at-or-below the other line, current value is above.
        /// Cross below: previous at-or-above, current below. Standard MA-cross semantics — the one
        /// boundary every line cross in this class uses, whatever timeframe each line is on.
        /// </summary>
        private static bool Crossed(double prev, double cur, double otherPrev, double otherCur, bool above)
        {
            if (double.IsNaN(cur) || double.IsNaN(prev) ||
                double.IsNaN(otherCur) || double.IsNaN(otherPrev))
                return false;
            return above
                ? prev <= otherPrev && cur > otherCur
                : prev >= otherPrev && cur < otherCur;
        }

        /// <summary>Minutes in a timeframe code ("15m", "4h", "1w", "1M"); null when it is not one.</summary>
        public static double? TimeframeMinutes(string? tf)
        {
            if (string.IsNullOrWhiteSpace(tf) || tf.Length < 2) return null;
            char unit = tf[^1];
            if (!double.TryParse(tf[..^1], System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0)
                return null;
            double per = unit switch
            {
                'm' => 1, 'h' => 60, 'H' => 60, 'd' => 1440, 'D' => 1440,
                'w' => 10080, 'W' => 10080, 'M' => 43200, 'y' => 525600, 'Y' => 525600,
                _ => double.NaN
            };
            return double.IsNaN(per) ? null : n * per;
        }

        private static bool SameTimeframe(string? a, string? b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal)
            || (TimeframeMinutes(a) is { } x && TimeframeMinutes(b) is { } y && Math.Abs(x - y) < 1e-9);

        /// <summary>The line a crosses-line leaf crosses is on a LOWER timeframe than its first line,
        /// which no path here can align honestly. Unknown timeframes are not refused.</summary>
        public static bool IsLowerTimeframe(string? candidate, string? than) =>
            TimeframeMinutes(candidate) is { } c && TimeframeMinutes(than) is { } t && c < t - 1e-9;

        /// <summary>
        /// Temporal confluence helper: returns true when data[i] is on the requested side of
        /// <paramref name="threshold"/> for ANY bar in the last <paramref name="n"/> closed bars.
        /// Lets a strategy express "Cipher A VWAP slope &gt; 0 any time in the last 5 bars" as a
        /// persistent-condition analogue to <see cref="FiredWithin"/>. NaN bars are skipped.
        /// </summary>
        private static bool ValueWithin(double[] data, int upToExclusive, int n, double threshold, bool above)
        {
            int from = Math.Max(0, upToExclusive - Math.Max(1, n));
            for (int i = from; i < upToExclusive; i++)
            {
                double v = data[i];
                if (double.IsNaN(v)) continue;
                if (above ? v > threshold : v < threshold) return true;
            }
            return false;
        }

        /// <summary>
        /// v9 fix: regime-relative percentile gate. Returns true when the current value
        /// (data[upToExclusive-1]) is at-or-below (below=true) or at-or-above (below=false)
        /// the Pth percentile of the trailing <paramref name="n"/> closed bars, where P =
        /// <paramref name="percentile"/> in 0..100. Skips NaN bars from both the population
        /// and the comparison. With fewer than 2 valid samples returns false.
        /// </summary>
        private static bool PercentileCompare(double[] data, int upToExclusive, int n, double percentile, bool below)
        {
            int upTo = Math.Min(upToExclusive, data.Length);
            if (upTo <= 0) return false;
            double cur = data[upTo - 1];
            if (double.IsNaN(cur)) return false;
            int from = Math.Max(0, upTo - Math.Max(1, n));
            int len = upTo - from;
            if (len < 2) return false;

            // Copy non-NaN window into a working buffer and sort.
            var buf = new double[len];
            int k = 0;
            for (int i = from; i < upTo; i++)
            {
                double v = data[i];
                if (!double.IsNaN(v)) buf[k++] = v;
            }
            if (k < 2) return false;
            Array.Sort(buf, 0, k);

            double p = Math.Clamp(percentile, 0.0, 100.0) / 100.0;
            int idx = (int)Math.Floor(p * (k - 1));
            double threshold = buf[idx];
            return below ? cur <= threshold : cur >= threshold;
        }

        private static bool BetweenWithin(double[] data, int upToExclusive, int n, double lo, double hi)
        {
            int from = Math.Max(0, upToExclusive - Math.Max(1, n));
            for (int i = from; i < upToExclusive; i++)
            {
                double v = data[i];
                if (double.IsNaN(v)) continue;
                if (v >= lo && v <= hi) return true;
            }
            return false;
        }

        /// <summary>
        /// v7 pivot-strength gate: drop every level whose <see cref="PriceLevel.Strength"/> is
        /// below <paramref name="minStrength"/>. A zero minimum (the default) returns the list
        /// unchanged. Used by level operators so a strategy can demand "only retests of pivots
        /// with strength ≥ 0.7" without having to author a separate level provider.
        /// </summary>
        private static IReadOnlyList<PriceLevel> FilterByStrength(IReadOnlyList<PriceLevel> levels, double minStrength)
        {
            if (minStrength <= 0.0) return levels;
            var kept = new List<PriceLevel>(levels.Count);
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Strength >= minStrength) kept.Add(levels[i]);
            return kept;
        }

        private static bool FiredWithin(double[] data, int n, int historyCount)
        {
            int upTo = Math.Min(historyCount, data.Length);
            int from = Math.Max(0, upTo - Math.Max(1, n));
            for (int i = from; i < upTo; i++)
                if (!double.IsNaN(data[i])) return true;
            return false;
        }

        private static bool DirectionChanged(double[] data, int historyCount)
        {
            int upTo = Math.Min(historyCount, data.Length);
            if (upTo < 3) return false;
            double a = data[upTo - 3], b = data[upTo - 2], c = data[upTo - 1];
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsNaN(c)) return false;
            int s1 = Math.Sign(b - a), s2 = Math.Sign(c - b);
            return s1 != 0 && s2 != 0 && s1 != s2;
        }

        private bool CrossesLine(IReadOnlyList<Ohlcv> history, WorkspaceState state, ConditionLeaf leaf, int primaryIdx, double cur, double prev, bool above)
        {
            // Resolve the second descriptor (the line being crossed). If unset, the operator
            // degrades to false — same behavior as the original stub but with the path now wired.
            if (string.IsNullOrEmpty(leaf.SecondSignalDescriptorId)) return false;
            var secondDesc = _catalog.GetById(leaf.SecondSignalDescriptorId);
            if (secondDesc == null) return false;
            if (RefusedForCausality(secondDesc)) return false;

            // The crossed line on a higher timeframe than the chart: "daily close crosses above
            // the 50-week SMA". Its own path — a step line read as of each chart bar.
            if (!string.IsNullOrEmpty(leaf.SecondTimeframe))
                return CrossesHigherTimeframeLine(history, state, leaf, secondDesc, primaryIdx, cur, prev, above);

            var data = ChartLine(secondDesc, leaf.SecondParameters, history, state);
            if (data == null || data.Length == 0) return false;

            // Same future-leak protection as the main path, and it has to be the SAME clip —
            // against history.Count, not against this array's own length. The comment that used
            // to sit here reasoned that the two descriptors' arrays are the same length in a
            // consistent workspace, so reading the second at data.Length - 1 was safe. Length
            // matching was never the point: the primary had already been clipped to the
            // strategy's current bar, and the second had not been clipped at all. "SMA 50
            // crosses above SMA 200" therefore compared SMA 50 at backtest bar 100 against
            // SMA 200 at the LAST bar of the chart — on a rising series the leaf is false at
            // every bar, on a falling one true at every bar, and either way the cross it names
            // is not the cross it tests. In live evaluation history.Count == data.Length and
            // the clip is a no-op, which is why this survived so long.
            int curIdx = Math.Min(history.Count, data.Length) - 1;
            if (curIdx < 0) return false;
            double otherCur  = data[curIdx];
            double otherPrev = curIdx >= 1 ? data[curIdx - 1] : double.NaN;

            if (double.IsNaN(cur) || double.IsNaN(prev) ||
                double.IsNaN(otherCur) || double.IsNaN(otherPrev))
                return false;

            // Cross above: previous value was at-or-below the other line, current value is above.
            // Cross below: previous at-or-above, current below. Standard MA-cross semantics.
            if (above)
                return prev <= otherPrev && cur > otherCur;
            else
                return prev >= otherPrev && cur < otherCur;
        }

        /// <summary>
        /// A chart-timeframe line crossing a higher-timeframe one. The HTF line is a step: at each
        /// chart bar it holds the value of its last bar closed by that bar's open, the rule every
        /// HTF read here uses. The cross compares the two lines at the current and previous chart
        /// bars, so it fires on the chart bar where price actually crosses — not once a week.
        /// </summary>
        private bool CrossesHigherTimeframeLine(IReadOnlyList<Ohlcv> history, WorkspaceState state, ConditionLeaf leaf,
            SignalDescriptor secondDesc, int primaryIdx, double cur, double prev, bool above)
        {
            string tf = leaf.SecondTimeframe!;
            if (_mtf == null)
            {
                Degrade($"nomtf|{leaf.Id}", $"nothing here can load {tf} data for {secondDesc.DisplayLabel}",
                    "Higher-timeframe data is not available in this part of the app.");
                return false;
            }
            if (IsLowerTimeframe(tf, state.Identity.Timeframe))
            {
                Degrade($"lowertf|{leaf.Id}",
                    $"{secondDesc.DisplayLabel} is on {tf}, lower than this chart's {state.Identity.Timeframe}; the line crossed must be on the chart's timeframe or higher");
                return false;
            }
            if (primaryIdx < 1 || primaryIdx >= history.Count) return false;

            string prov = state.Identity.Provider ?? string.Empty;
            string sym  = state.Identity.Symbol   ?? string.Empty;
            var htfBars = _mtf.GetCachedBars(prov, sym, tf);
            var other = HtfLine(leaf, secondDesc, leaf.SecondParameters, tf, prov, sym, htfBars);
            if (other == null) return false;
            if (htfBars.Count == 0)
            {
                // Values with no bars to date them cannot be lined up with the chart's bars.
                Degrade($"{leaf.Id}|{tf}|bars", $"the {tf} bars for {secondDesc.DisplayLabel} have not loaded",
                    LoadRemedy(tf));
                return false;
            }

            double otherCur  = HtfValueAsOf(other, htfBars, history[primaryIdx].Date);
            double otherPrev = HtfValueAsOf(other, htfBars, history[primaryIdx - 1].Date);
            return Crossed(prev, cur, otherPrev, otherCur, above);
        }

        /// <summary>
        /// A crosses-line leaf on a higher timeframe: both lines on that timeframe, compared at
        /// its last two closed bars — the same reading every other HTF operator makes. Before
        /// 2026-10 this operator fell to the HTF switch's default and was false on every bar.
        /// </summary>
        private bool CrossesLineOnHtf(ConditionLeaf leaf, double[] htfData, int clip, IReadOnlyList<Ohlcv> htfBars,
            int htfEndExclusive, string prov, string sym, bool above)
        {
            if (string.IsNullOrEmpty(leaf.SecondSignalDescriptorId)) return false;
            var secondDesc = _catalog.GetById(leaf.SecondSignalDescriptorId);
            if (secondDesc == null) return false;
            if (RefusedForCausality(secondDesc)) return false;

            string tf = leaf.Timeframe!;
            if (!string.IsNullOrEmpty(leaf.SecondTimeframe) && !SameTimeframe(leaf.SecondTimeframe, tf))
            {
                // A weekly line against a daily one, both off the chart's own timeframe, has no
                // single grid to compare them on. Put the lower-timeframe line on the chart instead.
                Degrade($"mixedtf|{leaf.Id}",
                    $"a {tf} condition can only cross a line on {tf}; {secondDesc.DisplayLabel} is on {leaf.SecondTimeframe}");
                return false;
            }

            var other = HtfLine(leaf, secondDesc, leaf.SecondParameters, tf, prov, sym, htfBars);
            if (other == null) return false;
            int otherClip = htfBars.Count > 0 ? Math.Min(htfEndExclusive, other.Length) : other.Length;
            int upTo = Math.Min(clip, otherClip);
            if (upTo < 2) return false;
            return Crossed(htfData[upTo - 2], htfData[upTo - 1], other[upTo - 2], other[upTo - 1], above);
        }

        // ── Loading what HTF leaves read (the alert path) ─────────────────────

        /// <summary>One higher-timeframe line a tree reads.</summary>
        /// <param name="IsCrossedLine">True for the line a crosses-line leaf crosses — the half a
        /// strategy's own pre-warm did not cover before 2026-10.</param>
        public sealed record TimeframeNeed(
            string Timeframe,
            SignalDescriptor Descriptor,
            IReadOnlyDictionary<string, double>? Parameters,
            bool IsCrossedLine);

        /// <summary>Every higher-timeframe line the tree's leaves read, once each.</summary>
        public static IReadOnlyList<TimeframeNeed> TimeframeNeeds(ConditionNode root, ISignalCatalog catalog)
        {
            var needs = new List<TimeframeNeed>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? tf, string? descId, IReadOnlyDictionary<string, double>? p, bool crossed)
            {
                if (string.IsNullOrEmpty(tf) || string.IsNullOrEmpty(descId)) return;
                var desc = catalog.GetById(descId);
                if (desc == null) return;
                if (seen.Add($"{tf}|{desc.IndicatorCode}|{DescribeParameters(p)}|{(IsPriceCode(desc.IndicatorCode) ? "" : desc.ComponentName)}"))
                    needs.Add(new TimeframeNeed(tf, desc, p, crossed));
            }
            void Walk(ConditionNode n)
            {
                switch (n)
                {
                    case ConditionLeaf l:
                        Add(l.Timeframe, l.SignalDescriptorId, l.Parameters, false);
                        if (l.Operator is LeafOperator.CrossesAboveLine or LeafOperator.CrossesBelowLine)
                            Add(l.SecondTimeframe ?? l.Timeframe, l.SecondSignalDescriptorId, l.SecondParameters, true);
                        break;
                    case ConditionGroup g:
                        foreach (var c in g.Children) Walk(c);
                        break;
                }
            }
            Walk(root);
            return needs;
        }

        private sealed class HtfLoad
        {
            public Task Task = Task.CompletedTask;
            public DateTime StartedUtc;
            public bool Landed;
        }

        // One entry per (provider, symbol, timeframe, indicator, parameters): the load in flight
        // or last finished, and whether ANY load for it has finished — after the first, a refresh
        // runs behind the old data instead of blanking it.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HtfLoad> _htfLoads =
            new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public bool PrepareTimeframes(ConditionNode root, ChartIdentity identity, int barCount)
        {
            if (_mtf == null) return true;   // nothing can load; the leaves say so when evaluated
            bool ready = true;
            int count = Math.Max(200, barCount);
            foreach (var need in TimeframeNeeds(root, _catalog))
                if (!EnsureLoaded(need, identity, count)) ready = false;
            return ready;
        }

        private bool EnsureLoaded(TimeframeNeed need, ChartIdentity identity, int count)
        {
            bool price = IsPriceCode(need.Descriptor.IndicatorCode);
            string key = $"{identity.Provider}|{identity.Symbol}|{need.Timeframe}|" +
                         (price ? "bars" : $"{need.Descriptor.IndicatorCode}|{DescribeParameters(need.Parameters)}");
            var now = DateTime.UtcNow;
            var load = _htfLoads.GetOrAdd(key, _ => new HtfLoad { Task = StartLoad(need, identity, count), StartedUtc = now });
            if (load.Task.IsCompleted) load.Landed = true;

            // A live alert outlives a strategy's one pre-warm: refresh on the timeframe's own
            // cadence so the weekly SMA moves when a new week closes.
            if (load.Task.IsCompleted && now - load.StartedUtc >= RefreshInterval(need.Timeframe))
                _htfLoads.TryUpdate(key,
                    new HtfLoad { Task = StartLoad(need, identity, count), StartedUtc = now, Landed = true }, load);

            return load.Landed;
        }

        private Task StartLoad(TimeframeNeed need, ChartIdentity identity, int count)
        {
            string market = identity.Market   ?? string.Empty;
            string prov   = identity.Provider ?? string.Empty;
            string sym    = identity.Symbol   ?? string.Empty;
            try
            {
                if (IsPriceCode(need.Descriptor.IndicatorCode))
                    return _mtf!.GetBarsAsync(market, prov, sym, need.Timeframe, count);
                var parameters = new Dictionary<string, object>();
                if (need.Parameters != null)
                    foreach (var kv in need.Parameters) parameters[kv.Key] = kv.Value;
                return _mtf!.RefreshIndicatorAsync(market, prov, sym, need.Timeframe,
                    need.Descriptor.IndicatorCode, parameters, count);
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        /// <summary>How long loaded HTF data is used before it is reloaded — the same scale as
        /// the timeframe service's bar TTL.</summary>
        private static TimeSpan RefreshInterval(string timeframe)
        {
            double minutes = TimeframeMinutes(timeframe) ?? 0;
            if (minutes >= 10080) return TimeSpan.FromMinutes(15);
            if (minutes >= 1440)  return TimeSpan.FromMinutes(5);
            if (minutes >= 60)    return TimeSpan.FromSeconds(60);
            if (minutes > 0)      return TimeSpan.FromSeconds(15);
            return TimeSpan.FromSeconds(30);
        }

        private bool PriceVsCloud(IReadOnlyList<Ohlcv> history, WorkspaceState state, SignalDescriptor desc, int sign)
        {
            if (history.Count == 0) return false;
            double close = history[^1].Close;
            var series = state.ActiveSeries.FirstOrDefault(s => s.IndicatorCode == desc.IndicatorCode);
            if (series == null) return false;

            // Find a cloud fill that references the signal's component as either boundary.
            // This gives us both the upper and lower component names from the indicator's
            // DefaultCloudFills metadata (e.g., Ichimoku: Senkou Span A / Senkou Span B).
            var fill = series.CloudFills.FirstOrDefault(f =>
                f.UpperComponentName == desc.ComponentName ||
                f.LowerComponentName == desc.ComponentName);

            // Clip every component read to the strategy's current bar, exactly as the main leaf
            // path does. Reading [^1] here handed AboveCloud / BelowCloud / InsideCloud the
            // chart's FINAL cloud at every historical bar — and did it while the same Ichimoku
            // data reached the level path causally through IchimokuLevelProvider, so one
            // evaluation could see two different clouds. history.Count is the strategy's bar
            // count; in live evaluation it equals the array length and this is a no-op.
            if (fill != null)
            {
                var upperData = series.GetComponentData(fill.UpperComponentName);
                var lowerData = series.GetComponentData(fill.LowerComponentName);
                int upIdx = Math.Min(history.Count, upperData.Length) - 1;
                int loIdx = Math.Min(history.Count, lowerData.Length) - 1;
                if (upIdx >= 0 && loIdx >= 0)
                {
                    double u = upperData[upIdx];
                    double l = lowerData[loIdx];
                    if (!double.IsNaN(u) && !double.IsNaN(l))
                    {
                        // Normalise so hi >= lo regardless of which span is above.
                        double hi = Math.Max(u, l);
                        double lo = Math.Min(u, l);
                        return sign switch
                        {
                            +1 => close > hi,
                            -1 => close < lo,
                             0 => close >= lo && close <= hi,
                             _ => false
                        };
                    }
                }
            }

            // Fallback: no cloud fill found, or component data unavailable.
            // Use the named component as a single boundary (AboveCloud / BelowCloud only).
            var compData = series.GetComponentData(desc.ComponentName);
            int compIdx = Math.Min(history.Count, compData.Length) - 1;
            if (compIdx < 0) return false;
            double val = compData[compIdx];
            if (double.IsNaN(val)) return false;
            return sign switch
            {
                +1 => close > val,
                -1 => close < val,
                 _ => false  // InsideCloud requires both bounds — can't evaluate with one
            };
        }
    }
}
