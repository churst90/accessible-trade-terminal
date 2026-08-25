using System.Collections.Immutable;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public class ViewportRangeCalculator : IViewportRangeCalculator
    {
        public ViewportRangeResult Calculate(WorkspaceState state)
        {
            if (state.Data == null || state.Data.Count == 0 || state.ViewportLength <= 0)
            {
                return new ViewportRangeResult((0, 100), ImmutableDictionary<string, (double Min, double Max)>.Empty);
            }

            int start = Math.Clamp(state.ViewportStartIndex, 0, state.Data.Count - 1);
            int end = Math.Min(start + state.ViewportLength, state.Data.Count);
            var visibleBars = state.Data.Skip(start).Take(end - start).ToList();

            if (!visibleBars.Any()) return new ViewportRangeResult((0, 100), ImmutableDictionary<string, (double Min, double Max)>.Empty);

            // ── Main Range (Price Action) ────────────────────────────────────
            // Data-driven bounds from the OHLCV buffer. For analytics tabs where
            // O=H=L=C (SingleValueLine provider), Low/High collapse to the scalar
            // min/max — still correct.
            double mainMin = visibleBars.Min(b => (double)b.Low);
            double mainMax = visibleBars.Max(b => (double)b.High);

            // Expand main range to include any reference levels on main-pane series
            // (e.g. analytics hint-injected FNG 25/50/75 zones). Otherwise the pane
            // auto-scales too tight and the zones clip off-screen.
            //
            // ── But a level must be in the same UNITS as the pane ────────────────
            //
            // A series whose Pane is unset falls back to "Main" above, and some indicators declare
            // reference lines in their own units rather than in price. LoukasCyclesProvider is the
            // worked example: it declares "DC Floor" at 0.0, "DC Window Open" at 35.0 and
            // "DC Overdue" at 90.0 — those are BAR COUNTS, days into a cycle. Let one of those
            // expand a BTC chart and the price axis runs 0 to 70,000, compressing every candle into
            // the top tenth of the pane. Reported from a screenshot; the chart was unreadable and
            // nothing errored.
            //
            // The guard is a ratio rather than a pane-bookkeeping fix, because it encodes the actual
            // invariant — a reference level on a price pane has to be a price — and so it holds even
            // when a series' pane assignment is wrong or missing. A level within a few multiples of
            // the visible data range is plausibly the same quantity; one sixty times outside it is
            // measuring something else.
            double dataSpan = mainMax - mainMin;
            foreach (var s in state.ActiveSeries)
            {
                string paneName = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                if (paneName != "Main") continue;
                foreach (var lvl in s.Levels)
                {
                    if (!lvl.IsVisible) continue;
                    if (!IsPlausiblySamePane(lvl.Value, mainMin, mainMax, dataSpan)) continue;
                    if (lvl.Value < mainMin) mainMin = lvl.Value;
                    if (lvl.Value > mainMax) mainMax = lvl.Value;
                }
            }

            // Hint-driven range clamp: if any main-pane series declares RangeMin/RangeMax
            // (from SymbolRenderHints on an analytics load), use those as hard bounds.
            // This keeps bounded metrics like FNG fixed at 0–100 even when current data
            // is a subset of the range. Applied before the buffer expansion below.
            foreach (var s in state.ActiveSeries)
            {
                string paneName = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                if (paneName != "Main") continue;
                if (s.Config.RangeMin.HasValue) mainMin = s.Config.RangeMin.Value;
                if (s.Config.RangeMax.HasValue) mainMax = s.Config.RangeMax.Value;
            }

            double mainRange = mainMax - mainMin;
            if (mainRange < 0.000001) { mainMin -= 1.0; mainMax += 1.0; }
            else { mainMin -= mainRange * 0.05; mainMax += mainRange * 0.05; }

            // ── Indicator Panes ───────────────────────────────────────────────
            // Accumulate min/max per range key across ALL series (fixes early-exit bug where
            // only the first series per pane was computed).
            // Range key = pane name for main-area components; "PaneName/SubPaneName" for sub-pane components.
            var accum = new Dictionary<string, (double Min, double Max, bool HasData)>(StringComparer.Ordinal);

            foreach (var s in state.ActiveSeries)
            {
                string paneName = string.IsNullOrEmpty(s.Pane) ? "Main" : s.Pane;
                if (paneName == "Main") continue;

                // ── Component data ranges ─────────────────────────────────────
                foreach (var comp in s.Components)
                {
                    string rangeKey = string.IsNullOrEmpty(comp.SubPaneName)
                        ? paneName
                        : $"{paneName}/{comp.SubPaneName}";

                    var data = s.GetComponentData(comp.Name);
                    if (data == null || data.Length == 0) continue;

                    accum.TryGetValue(rangeKey, out var cur);
                    double rMin = cur.HasData ? cur.Min : double.MaxValue;
                    double rMax = cur.HasData ? cur.Max : double.MinValue;
                    bool hasData = cur.HasData;

                    int cEnd = Math.Min(start + state.ViewportLength, data.Length);
                    for (int i = start; i < cEnd; i++)
                    {
                        double val = data[i];
                        if (!double.IsNaN(val))
                        {
                            if (val < rMin) rMin = val;
                            if (val > rMax) rMax = val;
                            hasData = true;
                        }
                    }

                    if (hasData) accum[rangeKey] = (rMin, rMax, true);
                }

                // ── Reference levels expand the pane-level (main-area) range ─
                // Levels are always drawn in the main area of the pane, so they use the
                // plain pane key (not a sub-pane composite key).
                string paneKey = paneName;
                foreach (var lvl in s.Levels)
                {
                    if (!lvl.IsVisible) continue;
                    accum.TryGetValue(paneKey, out var cur);
                    double rMin = cur.HasData ? cur.Min : double.MaxValue;
                    double rMax = cur.HasData ? cur.Max : double.MinValue;
                    if (lvl.Value < rMin) rMin = lvl.Value;
                    if (lvl.Value > rMax) rMax = lvl.Value;
                    accum[paneKey] = (rMin, rMax, true);
                }
            }

            // ── Build final paneRanges dictionary ────────────────────────────
            var paneRanges = new Dictionary<string, (double Min, double Max)>(StringComparer.Ordinal);
            paneRanges["Main"] = (mainMin, mainMax);

            // Collect all unique pane names (base part before '/') that were seen
            var panesSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in accum.Keys)
            {
                int slash = key.IndexOf('/');
                panesSeen.Add(slash >= 0 ? key.Substring(0, slash) : key);
            }

            // Ensure every seen pane has at least a default range entry (handles panes with
            // only sub-pane components and no main-area data).
            foreach (var p in panesSeen)
                if (!accum.ContainsKey(p))
                    accum[p] = (double.MaxValue, double.MinValue, false);

            foreach (var kvp in accum)
            {
                string key = kvp.Key;
                var (rMin, rMax, hasData) = kvp.Value;

                bool isSubPane = key.IndexOf('/') >= 0;
                string basePaneName = isSubPane ? key.Substring(0, key.IndexOf('/')) : key;

                if (!hasData)
                {
                    paneRanges[key] = basePaneName == "Pane_CIPHER_B" && !isSubPane
                        ? (-100.0, 100.0)
                        : (0.0, 100.0);
                    continue;
                }

                if (basePaneName == "Pane_CIPHER_B" && !isSubPane)
                {
                    // Fixed ±100 floor keeps OB/OS levels (±53/±60) clearly visible.
                    rMin = Math.Min(rMin, -100.0);
                    rMax = Math.Max(rMax,  100.0);
                    paneRanges[key] = (rMin, rMax);
                }
                else
                {
                    double originalMin = rMin;
                    double pRange = rMax - rMin;
                    // Sub-panes get a slightly larger buffer so the fill doesn't touch the strip edge.
                    double bufferPct = isSubPane ? 0.15 : 0.10;
                    if (pRange < 0.000001) { rMin -= 1.0; rMax += 1.0; }
                    else { rMin -= pRange * bufferPct; rMax += pRange * bufferPct; }
                    // Don't let the buffer push an always-positive pane (e.g. Volume) negative.
                    if (rMin < 0 && originalMin >= 0) rMin = 0.0;
                    paneRanges[key] = (rMin, rMax);
                }
            }

            return new ViewportRangeResult((mainMin, mainMax), paneRanges.ToImmutableDictionary());
        }
    
        /// <summary>
        /// Whether a reference level is in the same units as the pane it would expand.
        ///
        /// <para>
        /// <b>How far outside the data is too far.</b> A level a little beyond the visible window is
        /// exactly what this expansion exists for — a Fear &amp; Greed chart showing 10–90 should
        /// still draw its 0 and 100 zone lines. A level sixty times the data span away is not a
        /// generous zone, it is a different quantity: a bar count, a percentage, an oscillator
        /// bound, on a pane measured in dollars.
        /// </para>
        ///
        /// <para>
        /// <see cref="MaxLevelSpanMultiple"/> is the dividing line. Three is deliberately loose —
        /// this guard exists to catch a unit mismatch, not to police legitimate zones, and being
        /// too strict would silently clip the very thing the expansion was added for.
        /// </para>
        /// </summary>
        internal static bool IsPlausiblySamePane(double level, double dataMin, double dataMax, double dataSpan)
        {
            if (!double.IsFinite(level)) return false;

            // A flat series has no span to judge against, so nothing can be ruled out.
            if (dataSpan <= 0) return true;

            double distance = level < dataMin ? dataMin - level
                            : level > dataMax ? level - dataMax
                            : 0;

            return distance <= dataSpan * MaxLevelSpanMultiple;
        }

        /// <summary>How many multiples of the visible data span a reference level may sit outside it.</summary>
        internal const double MaxLevelSpanMultiple = 3.0;
}
}
