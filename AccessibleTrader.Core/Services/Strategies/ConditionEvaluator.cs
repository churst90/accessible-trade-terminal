using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Strategies;

namespace AccessibleTrader.Core.Services.Strategies
{
    /// <summary>
    /// Default <see cref="IConditionEvaluator"/>. Resolves each leaf's signal value via
    /// <see cref="ISignalCatalog"/> + the workspace's active series, applies the leaf operator,
    /// then folds results up the tree using AND/OR/NOT semantics.
    ///
    /// Multi-timeframe leaves (those with <see cref="ConditionLeaf.Timeframe"/> set) currently
    /// fall through to the active-TF series — Session B will plug the multi-timeframe data
    /// service in here and route HTF leaves to a different series source.
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
        /// Records the most recent HTF-data gap encountered during evaluation, for UI surfacing.
        /// Cleared at the start of each Evaluate call. When non-empty, at least one HTF leaf
        /// could not resolve its data and returned false — meaning the strategy's signal is
        /// safer than the old "fall through to active-TF" behaviour but incomplete. UI layers
        /// read this to warn users their strategy is not operating at full fidelity.
        /// </summary>
        public string? LastHtfDegradation { get; private set; }

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
            LastHtfDegradation = null;

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

        private bool EvaluateLeaf(ConditionLeaf leaf, IReadOnlyList<Ohlcv> history, WorkspaceState state)
        {
            var desc = _catalog.GetById(leaf.SignalDescriptorId);
            if (desc == null) return false;

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

                // First: try the pre-warmed HTF indicator cache. ConfigurableStrategy.Initialize
                // is expected to have called PrewarmIndicatorAsync for every unique (Timeframe,
                // IndicatorCode) pair in the spec, so by the time evaluation runs the indicator
                // result arrays are sitting in MultiTimeframeDataService's cache. This is what
                // makes indicator-based HTF leaves work — sync read on the hot path.
                // Determine the last CLOSED HTF bar at the strategy's current main-TF time.
                // Without this clip the HTF cache leaks future values — at main-TF bar 100 we'd
                // otherwise read htfData[^1] (the final HTF value in the entire cached series).
                var htfBars = _mtf.GetCachedBars(prov, sym, leaf.Timeframe);
                int htfEndExclusive = htfBars.Count > 0 && history.Count > 0
                    ? HtfLastClosedIndexExclusive(htfBars, history[^1].Date)
                    : htfBars.Count;

                var cachedInd = _mtf.GetCachedIndicator(prov, sym, leaf.Timeframe, desc.IndicatorCode);
                if (cachedInd != null && cachedInd.TryGetValue(desc.ComponentName, out var htfData) && htfData.Length > 0)
                {
                    int clip = htfBars.Count > 0
                        ? Math.Min(htfEndExclusive, htfData.Length)
                        : htfData.Length;
                    return EvaluateHtfIndicatorLeaf(leaf, htfData, clip);
                }

                // Second: fall through to the price-only HTF path. Works for any leaf whose
                // operator is one of the price-comparison primitives (GreaterThan, LessThan,
                // CrossesAbove, etc.) tested against the HTF close.
                if (htfBars.Count > 0)
                {
                    return EvaluateHtfPriceLeaf(leaf, htfBars, htfEndExclusive);
                }

                // Neither cached indicator nor cached bars are available for this HTF leaf.
                // The previous behaviour was to fall through to active-timeframe data here,
                // silently degrading the strategy — a backtest on a 1h/daily spec would run
                // the daily leaf against 1h bars and report wrong results with no warning.
                //
                // New behaviour: return FALSE and record the degradation. The leaf does not
                // evaluate true until HTF data arrives. Strategies will simply not fire until
                // pre-warm completes, which is conservative and correct.
                string msg = $"HTF leaf '{leaf.Id}' on timeframe '{leaf.Timeframe}' has no cached data — leaf returning false until pre-warm completes.";
                LastHtfDegradation = msg;
                string warningKey = $"{leaf.Id}|{leaf.Timeframe}";
                if (_htfWarningsEmitted.TryAdd(warningKey, 0))
                {
                    System.Diagnostics.Debug.WriteLine($"[ConditionEvaluator] {msg} Fire IMultiTimeframeDataService PrewarmIndicatorAsync from the strategy's Initialize to resolve.");
                }
                return false;
            }

            // Resolve component data from the workspace's active series.
            // Case-insensitive match because indicator providers may register their codes in
            // different casing than the catalog or the chart series (e.g. "CipherB" vs "CIPHER_B").
            // Without this comparison, a leaf can silently fail to find its data and the strategy
            // never fires — exactly the symptom users hit when they wire up an indicator that
            // *is* on the chart but with a casing variant.
            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, desc.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (series == null) return false;
            var data = series.GetComponentData(desc.ComponentName);
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
                LeafOperator.CrossesAboveLine => CrossesLine(state, leaf, cur, prev, above: true),
                LeafOperator.CrossesBelowLine => CrossesLine(state, leaf, cur, prev, above: false),
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
        /// This expresses the Crypto Face setup natively: "Anchor washed out, THEN Trigger
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

            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, desc.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (series == null) return false;
            var data = series.GetComponentData(desc.ComponentName);
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

        /// <summary>
        /// Evaluate the subset of leaf operators that work directly on raw HTF Ohlcv without
        /// requiring an indicator computation pass. Currently: GreaterThan / LessThan / Between /
        /// CrossesAbove / CrossesBelow against the close price. Returns false for any operator
        /// that needs indicator data — those use the EvaluateHtfIndicatorLeaf path above.
        /// </summary>
        private static bool EvaluateHtfPriceLeaf(ConditionLeaf leaf, IReadOnlyList<Ohlcv> htfBars, int endExclusive)
        {
            int upTo = Math.Min(endExclusive, htfBars.Count);
            if (upTo <= 0) return false;
            double cur = htfBars[upTo - 1].Close;
            double prev = upTo >= 2 ? htfBars[upTo - 2].Close : double.NaN;

            return leaf.Operator switch
            {
                LeafOperator.GreaterThan  => cur > leaf.Value,
                LeafOperator.LessThan     => cur < leaf.Value,
                LeafOperator.Between      => cur >= leaf.Value && cur <= (leaf.Value2 ?? double.PositiveInfinity),
                LeafOperator.CrossesAbove => !double.IsNaN(prev) && prev <= leaf.Value && cur >  leaf.Value,
                LeafOperator.CrossesBelow => !double.IsNaN(prev) && prev >= leaf.Value && cur <  leaf.Value,
                _ => false // indicator-dependent operators need Session-C HTF indicator runner
            };
        }

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

        private bool CrossesLine(WorkspaceState state, ConditionLeaf leaf, double cur, double prev, bool above)
        {
            // Resolve the second descriptor (the line being crossed). If unset, the operator
            // degrades to false — same behavior as the original stub but with the path now wired.
            if (string.IsNullOrEmpty(leaf.SecondSignalDescriptorId)) return false;
            var secondDesc = _catalog.GetById(leaf.SecondSignalDescriptorId);
            if (secondDesc == null) return false;

            var series = state.ActiveSeries.FirstOrDefault(s =>
                string.Equals(s.IndicatorCode, secondDesc.IndicatorCode, StringComparison.OrdinalIgnoreCase));
            if (series == null) return false;
            var data = series.GetComponentData(secondDesc.ComponentName);
            if (data == null || data.Length == 0) return false;

            // Same future-leak protection as the main path: clip the read to the strategy's
            // current bar index. The caller passed in `cur` and `prev` from the primary descriptor
            // (already clipped); we read the same index from the second descriptor.
            int upTo = data.Length;
            // We don't have history.Count here, but `cur` and `prev` were derived against
            // the primary descriptor at history.Count - 1 / -2 already, and the second
            // descriptor's array length should match the primary's in any consistent workspace.
            // Use the array's last/penultimate values and fall back gracefully on mismatch.
            int curIdx = upTo - 1;
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

            if (fill != null)
            {
                var upperData = series.GetComponentData(fill.UpperComponentName);
                var lowerData = series.GetComponentData(fill.LowerComponentName);
                if (upperData.Length > 0 && lowerData.Length > 0)
                {
                    double u = upperData[^1];
                    double l = lowerData[^1];
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
            if (compData.Length == 0) return false;
            double val = compData[^1];
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
