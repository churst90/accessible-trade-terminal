using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
            double mainMin = visibleBars.Min(b => (double)b.Low);
            double mainMax = visibleBars.Max(b => (double)b.High);

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
    }
}
