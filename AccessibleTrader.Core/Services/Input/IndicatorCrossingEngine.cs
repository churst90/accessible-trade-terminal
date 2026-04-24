using System;
using System.Linq;
using System.Collections.Generic;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.Input
{
    /// <summary>
    /// Handles context-aware Ctrl+Left/Right crossing and sparse-signal navigation.
    /// Extracted from CommandDispatcher so the scan algorithms are independently testable
    /// and reusable by the strategy platform without coupling to the input-routing layer.
    ///
    /// Crossing taxonomy:
    ///   Trendline        — price crosses a drawn trendline (default for price/candle focus).
    ///   ZeroLine         — oscillator value crosses zero (MACD, Momentum, ZeroArea series).
    ///   ThresholdLevel   — value enters/exits an OB/OS zone (RSI, MFI, Stoch, CCI).
    ///   MovingAverageCross — close price crosses the focused MA line.
    ///   BandLine         — %B value crosses Upper (1.0), Midband (0.5), or Lower (0.0).
    ///   SparseSignal     — component array transitions from NaN to non-NaN (signal fires).
    /// </summary>
    public class IndicatorCrossingEngine
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;

        public IndicatorCrossingEngine(IWorkspaceStore store, IEventBus eventBus)
        {
            _store    = store;
            _eventBus = eventBus;
        }

        // ── Public entry point ────────────────────────────────────────────────

        public void HandleCrossJump(SystemCommand command)
        {
            var state    = _store.State;
            int current  = state.CurrentDataIndex;
            var data     = state.Data;
            int count    = data.Count;
            bool jumpRight = command == SystemCommand.NavRightJump;

            var focusedSeries = state.ActiveSeries
                .FirstOrDefault(s => s.Id == state.FocusedSeriesId);

            // Focused-trendline shortcut: when the user has a specific drawn trendline selected,
            // Ctrl+L/R should only walk price-vs-THAT trendline crossings — not sweep every
            // trendline on the chart. The former is the natural interpretation of "my focus is
            // on this line, find where price crosses it"; the latter is what the price-action
            // path below is for (no specific drawing in hand).
            if (focusedSeries != null && focusedSeries.IsDrawing
                && focusedSeries.Drawing?.Type == DrawingType.TrendLine)
            {
                DoFocusedTrendlineCrossJump(state, focusedSeries, data, count, current, jumpRight);
                return;
            }

            if (focusedSeries != null && state.FocusedComponentIndex >= 0
                && state.FocusedComponentIndex < focusedSeries.Components.Count)
            {
                var focusedComp = focusedSeries.Components[state.FocusedComponentIndex];

                // Case 1: price-action / candle → trendlines
                bool isPriceAction = focusedComp.Role == ComponentRole.PriceAction
                                  || focusedComp.Role == ComponentRole.Body
                                  || focusedComp.DisplayType == ComponentDisplayType.Candle
                                  || focusedComp.DisplayType == ComponentDisplayType.Wick;
                if (isPriceAction)
                {
                    DoTrendlineCrossJump(state, data, count, current, jumpRight);
                    return;
                }

                // Case 2: sparse marker → jump to nearest non-NaN signal
                bool isSparseMarker = focusedComp.DisplayType == ComponentDisplayType.Dot
                                   || focusedComp.DisplayType == ComponentDisplayType.Diamond
                                   || focusedComp.DisplayType == ComponentDisplayType.Cross
                                   || focusedComp.DisplayType == ComponentDisplayType.Arrow
                                   || focusedComp.DisplayType == ComponentDisplayType.TriangleUp
                                   || focusedComp.DisplayType == ComponentDisplayType.TriangleDown
                                   || focusedComp.DisplayType == ComponentDisplayType.Square
                                   || focusedComp.DisplayType == ComponentDisplayType.ZeroDot
                                   || focusedComp.DisplayType == ComponentDisplayType.GradientDot;
                if (isSparseMarker)
                {
                    DoSparseSignalJump(focusedSeries, focusedComp, current, jumpRight);
                    return;
                }

                // Case 3: indicator component → context-aware crossing
                var crossingType = GetCrossingStrategy(state, focusedSeries);
                switch (crossingType)
                {
                    case CrossingType.ZeroLine:
                        DoZeroLineCrossJump(state, focusedSeries, current, count, jumpRight);
                        return;
                    case CrossingType.ThresholdLevel:
                        DoThresholdCrossJump(state, focusedSeries, current, count, jumpRight);
                        return;
                    case CrossingType.MovingAverageCross:
                        DoMACrossJump(state, focusedSeries, current, count, jumpRight);
                        return;
                    case CrossingType.BandLine:
                        DoBandLineCrossJump(state, focusedSeries, current, count, jumpRight);
                        return;
                    case CrossingType.Trendline:
                    default:
                        // No dedicated crossing rule matched this component. For a continuous-
                        // points line (every bar has a numeric value) there's nothing sparse to
                        // jump to — silently falling through to "all trendlines" surprised users
                        // who had a specific oscillator component in focus. Instead, announce
                        // the absence explicitly so the user knows why Ctrl+L/R is a no-op here.
                        var compData = focusedSeries.GetComponentData(focusedComp.Name);
                        bool hasNaN = compData != null && compData.Any(double.IsNaN);
                        if (compData != null && compData.Length > 0 && hasNaN && compData.Any(v => !double.IsNaN(v)))
                        {
                            // Mixed NaN / non-NaN values — treat as sparse marker.
                            DoSparseSignalJump(focusedSeries, focusedComp, current, jumpRight);
                        }
                        else
                        {
                            string compName = focusedComp.DisplayName ?? focusedComp.Name;
                            _eventBus.Publish(new FeedbackRequestEvent(
                                FeedbackType.Boundary,
                                $"No points of interest on {compName}"));
                        }
                        return;
                }
            }

            DoTrendlineCrossJump(state, data, count, current, jumpRight);
        }

        // ── Crossing type resolution ──────────────────────────────────────────

        private enum CrossingType { Trendline, ZeroLine, ThresholdLevel, BandLine, MovingAverageCross }

        private static CrossingType GetCrossingStrategy(WorkspaceState state, ChartSeries? focusedSeries)
        {
            if (focusedSeries == null) return CrossingType.Trendline;

            string id = focusedSeries.Id;
            if (id == CoreSeriesIds.Candles || id == CoreSeriesIds.Price ||
                id == CoreSeriesIds.Volume  || focusedSeries.IsDrawing)
                return CrossingType.Trendline;

            string code = (focusedSeries.IndicatorCode ?? string.Empty).ToUpperInvariant();

            if (code is "PERCENTB" or "BOLLINGERPERCENTB" or "BBP")
                return CrossingType.BandLine;

            if (code.StartsWith("EMA") || code.StartsWith("SMA") || code.StartsWith("WMA") ||
                code.StartsWith("DEMA") || code.StartsWith("TEMA") || code.StartsWith("HULL") ||
                code.StartsWith("ALMA") || code.StartsWith("VWMA") || code.Contains("SPIDER") ||
                code.Contains("MOVING AVG") || code.Contains("MOVING AVERAGE"))
                return CrossingType.MovingAverageCross;

            var seriesLevels = focusedSeries.Levels;
            bool hasOB = seriesLevels.Any(l => l.Name.Contains("Overbought", StringComparison.OrdinalIgnoreCase) || l.Name.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase));
            bool hasOS = seriesLevels.Any(l => l.Name.Contains("Oversold",   StringComparison.OrdinalIgnoreCase) || l.Name.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase));
            if (hasOB && hasOS) return CrossingType.ThresholdLevel;

            bool hasZero = seriesLevels.Any(l => l.Name.Equals("Zero", StringComparison.OrdinalIgnoreCase));
            if (hasZero) return CrossingType.ZeroLine;

            var primaryComp = focusedSeries.Components.FirstOrDefault(c =>
                c.DisplayType == ComponentDisplayType.ZeroArea ||
                c.DisplayType == ComponentDisplayType.Oscillator);
            if (primaryComp != null) return CrossingType.ZeroLine;

            return CrossingType.Trendline;
        }

        // ── Per-type jump handlers ─────────────────────────────────────────────

        private void DoSparseSignalJump(ChartSeries focusedSeries, ComponentConfig focusedComp, int current, bool jumpRight)
        {
            var compData = focusedSeries.GetComponentData(focusedComp.Name);
            if (compData == null || compData.Length == 0)
            {
                string compName = focusedComp.DisplayName ?? focusedComp.Name;
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, $"No {compName} data"));
                return;
            }

            int found = -1;
            if (jumpRight) { for (int i = current + 1; i < compData.Length; i++) if (!double.IsNaN(compData[i])) { found = i; break; } }
            else           { for (int i = current - 1; i >= 0; i--)              if (!double.IsNaN(compData[i])) { found = i; break; } }

            if (found >= 0)
            {
                _store.Dispatch(new NavigateAction(found));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "", true, IsXMove: true));
            }
            else
            {
                string compName = focusedComp.DisplayName ?? focusedComp.Name;
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, $"No more {compName} signals in this direction"));
            }
        }

        private void DoZeroLineCrossJump(WorkspaceState state, ChartSeries focusedSeries, int current, int count, bool jumpRight)
        {
            var primaryComp = focusedSeries.Components.FirstOrDefault(c =>
                c.Role != ComponentRole.Level && c.DisplayType != ComponentDisplayType.Level &&
                c.IsVisible && !double.IsNaN(GetFirstValidValue(focusedSeries.GetComponentData(c.Name))))
                ?? focusedSeries.Components.FirstOrDefault(c =>
                    c.Role != ComponentRole.Level && c.DisplayType != ComponentDisplayType.Level);

            if (primaryComp == null) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var seriesData = focusedSeries.GetComponentData(primaryComp.Name);
            if (seriesData == null || seriesData.Length < 2) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            int found = ScanSignCrossing(seriesData, current, jumpRight, 0.0);
            if (found >= 0)
            {
                _store.Dispatch(new NavigateAction(found));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, $"Zero line cross at {FormatTimestamp(state, found)}", true));
            }
            else _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view"));
        }

        private void DoThresholdCrossJump(WorkspaceState state, ChartSeries focusedSeries, int current, int count, bool jumpRight)
        {
            double obLevel = GetNamedLevelValue(focusedSeries, "Overbought", "Extreme OB");
            double osLevel = GetNamedLevelValue(focusedSeries, "Oversold",   "Extreme OS");

            if (double.IsNaN(obLevel) || double.IsNaN(osLevel))
            {
                var seriesLevels2 = focusedSeries.Levels;
                var obEntry = seriesLevels2.FirstOrDefault(l => l.Name.Contains("Overbought", StringComparison.OrdinalIgnoreCase) || l.Name.Contains("Extreme OB", StringComparison.OrdinalIgnoreCase));
                var osEntry = seriesLevels2.FirstOrDefault(l => l.Name.Contains("Oversold",   StringComparison.OrdinalIgnoreCase) || l.Name.Contains("Extreme OS", StringComparison.OrdinalIgnoreCase));
                if (obEntry != null) obLevel = obEntry.Value;
                if (osEntry != null) osLevel = osEntry.Value;
            }

            if (double.IsNaN(obLevel) || double.IsNaN(osLevel)) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var primaryComp = focusedSeries.Components.FirstOrDefault(c =>
                c.Role != ComponentRole.Level && c.DisplayType != ComponentDisplayType.Level && c.IsVisible);
            if (primaryComp == null) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var seriesData = focusedSeries.GetComponentData(primaryComp.Name);
            if (seriesData == null || seriesData.Length < 2) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            int obFound = ScanThresholdCrossing(seriesData, current, jumpRight, obLevel, aboveIsZone: true,  out string obMsg);
            int osFound = ScanThresholdCrossing(seriesData, current, jumpRight, osLevel, aboveIsZone: false, out string osMsg);

            int found = -1; string crossMsg = string.Empty;
            if (obFound >= 0 && osFound >= 0)
            {
                bool pickOb = jumpRight ? obFound <= osFound : obFound >= osFound;
                (found, crossMsg) = pickOb ? (obFound, obMsg) : (osFound, osMsg);
            }
            else if (obFound >= 0) { found = obFound; crossMsg = obMsg; }
            else if (osFound >= 0) { found = osFound; crossMsg = osMsg; }

            if (found >= 0)
            {
                _store.Dispatch(new NavigateAction(found));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, $"{crossMsg} at {FormatTimestamp(state, found)}", true));
            }
            else _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view"));
        }

        private void DoMACrossJump(WorkspaceState state, ChartSeries focusedSeries, int current, int count, bool jumpRight)
        {
            var primaryComp = focusedSeries.Components.FirstOrDefault(c =>
                c.DisplayType == ComponentDisplayType.Line && c.Role != ComponentRole.Level && c.IsVisible)
                ?? focusedSeries.Components.FirstOrDefault(c =>
                    c.Role != ComponentRole.Level && c.DisplayType != ComponentDisplayType.Level);

            if (primaryComp == null) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var maData = focusedSeries.GetComponentData(primaryComp.Name);
            if (maData == null || maData.Length < 2) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var rawData = state.Data;
            int dataLen = Math.Min(maData.Length, rawData.Count);
            int found   = -1;
            string direction = string.Empty;

            if (jumpRight)
            {
                for (int i = current + 1; i < dataLen; i++)
                {
                    if (double.IsNaN(maData[i]) || double.IsNaN(maData[i - 1])) continue;
                    bool wasAbove = (double)rawData[i - 1].Close >= maData[i - 1];
                    bool isAbove  = (double)rawData[i].Close     >= maData[i];
                    if (wasAbove != isAbove) { found = i; direction = isAbove ? "Price crosses above MA" : "Price crosses below MA"; break; }
                }
            }
            else
            {
                for (int i = current - 1; i > 0; i--)
                {
                    if (double.IsNaN(maData[i]) || double.IsNaN(maData[i - 1])) continue;
                    bool wasAbove = (double)rawData[i - 1].Close >= maData[i - 1];
                    bool isAbove  = (double)rawData[i].Close     >= maData[i];
                    if (wasAbove != isAbove) { found = i; direction = isAbove ? "Price crosses above MA" : "Price crosses below MA"; break; }
                }
            }

            if (found >= 0)
            {
                _store.Dispatch(new NavigateAction(found));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, $"{direction} at {FormatTimestamp(state, found)}", true));
            }
            else _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view"));
        }

        private void DoBandLineCrossJump(WorkspaceState state, ChartSeries focusedSeries, int current, int count, bool jumpRight)
        {
            var primaryComp = focusedSeries.Components.FirstOrDefault(c =>
                c.Role != ComponentRole.Level && c.DisplayType != ComponentDisplayType.Level && c.IsVisible);
            if (primaryComp == null) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            var seriesData = focusedSeries.GetComponentData(primaryComp.Name);
            if (seriesData == null || seriesData.Length < 2) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view")); return; }

            double[] boundaries = { 1.0, 0.5, 0.0 };
            int found = -1; string crossMsg = "Band crossing"; int bestDistance = int.MaxValue;

            foreach (double boundary in boundaries)
            {
                int bFound = ScanSignCrossing(seriesData, current, jumpRight, boundary);
                if (bFound >= 0)
                {
                    int dist = Math.Abs(bFound - current);
                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        found = bFound;
                        crossMsg = boundary switch { 1.0 => "Upper band crossing", 0.0 => "Lower band crossing", _ => "Midband crossing" };
                    }
                }
            }

            if (found >= 0)
            {
                _store.Dispatch(new NavigateAction(found));
                _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, $"{crossMsg} at {FormatTimestamp(state, found)}", true));
            }
            else _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing in view"));
        }

        /// <summary>
        /// Walk price-vs-focused-trendline crossings. Used when the user has a specific drawn
        /// trendline in focus — the natural interpretation is "find where close crosses THIS
        /// line" rather than sweeping every trendline on the chart (which is what
        /// <see cref="DoTrendlineCrossJump"/> does when no drawing is focused).
        /// </summary>
        private void DoFocusedTrendlineCrossJump(WorkspaceState state, ChartSeries focusedSeries,
            System.Collections.Generic.IReadOnlyList<Ohlcv> data, int count, int current, bool jumpRight)
        {
            var drawing = focusedSeries.Drawing;
            if (drawing == null || !drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue
                || !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, "Focused trendline has no anchors"));
                return;
            }

            int i1 = -1, i2 = -1;
            for (int k = 0; k < count; k++) { if (data[k].Date >= drawing.AnchorDate1.Value) { i1 = k; break; } }
            for (int k = 0; k < count; k++) { if (data[k].Date >= drawing.AnchorDate2.Value) { i2 = k; break; } }
            if (i1 < 0 || i2 < 0 || i1 == i2)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary, "Focused trendline anchors are off-chart"));
                return;
            }

            double p1 = drawing.AnchorPrice1.Value, p2 = drawing.AnchorPrice2.Value;
            double m  = (p2 - p1) / (i2 - i1);
            double b  = p1 - (m * i1);

            int foundIndex = -1;
            for (int i = 1; i < count; i++)
            {
                bool above     = data[i].Close     >= (m * i)       + b;
                bool abovePrev = data[i - 1].Close >= (m * (i - 1)) + b;
                if (above == abovePrev) continue;
                if (!jumpRight && i < current  && (foundIndex < 0 || i > foundIndex)) foundIndex = i;
                else if (jumpRight && i > current && (foundIndex < 0 || i < foundIndex)) foundIndex = i;
            }

            if (foundIndex >= 0)
            {
                _store.Dispatch(new NavigateAction(foundIndex));
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Info,
                    $"Focused trendline crossing at {FormatTimestamp(state, foundIndex)}",
                    true));
            }
            else
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    "No crossing against focused trendline"));
            }
        }

        private void DoTrendlineCrossJump(WorkspaceState state, System.Collections.Generic.IReadOnlyList<Ohlcv> data, int count, int current, bool jumpRight)
        {
            var trendlines = state.ActiveSeries
                .Where(s => s.IsDrawing && s.Drawing?.Type == DrawingType.TrendLine)
                .ToList();

            if (!trendlines.Any()) { _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No trendlines found")); return; }

            int foundIndex = -1;
            foreach (var series in trendlines)
            {
                var drawing = series.Drawing!;
                if (!drawing.AnchorDate1.HasValue || !drawing.AnchorPrice1.HasValue ||
                    !drawing.AnchorDate2.HasValue || !drawing.AnchorPrice2.HasValue)
                    continue;

                int i1 = -1, i2 = -1;
                for (int k = 0; k < count; k++) { if (data[k].Date >= drawing.AnchorDate1.Value) { i1 = k; break; } }
                for (int k = 0; k < count; k++) { if (data[k].Date >= drawing.AnchorDate2.Value) { i2 = k; break; } }
                if (i1 < 0 || i2 < 0 || i1 == i2) continue;

                double p1 = drawing.AnchorPrice1.Value, p2 = drawing.AnchorPrice2.Value;
                double m  = (p2 - p1) / (i2 - i1);
                double b  = p1 - (m * i1);

                for (int i = 1; i < count; i++)
                {
                    bool above     = data[i].Close     >= (m * i)       + b;
                    bool abovePrev = data[i - 1].Close >= (m * (i - 1)) + b;
                    if (above == abovePrev) continue;
                    if (!jumpRight && i < current  && (foundIndex < 0 || i > foundIndex)) foundIndex = i;
                    else if (jumpRight && i > current && (foundIndex < 0 || i < foundIndex)) foundIndex = i;
                }
            }

            if (foundIndex >= 0)
            {
                _store.Dispatch(new NavigateAction(foundIndex));
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info, "Trendline crossing"));
            }
            else _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Navigation, "No crossing found"));
        }

        // ── Static scan primitives (also used by CrossingNavigationTests via reflection) ─

        internal static int ScanSignCrossing(double[] data, int current, bool scanRight, double threshold)
        {
            if (scanRight)
            {
                for (int i = current + 1; i < data.Length; i++)
                {
                    if (double.IsNaN(data[i]) || double.IsNaN(data[i - 1])) continue;
                    if ((data[i] >= threshold) != (data[i - 1] >= threshold)) return i;
                }
            }
            else
            {
                for (int i = current - 1; i > 0; i--)
                {
                    if (double.IsNaN(data[i]) || double.IsNaN(data[i - 1])) continue;
                    if ((data[i] >= threshold) != (data[i - 1] >= threshold)) return i;
                }
            }
            return -1;
        }

        internal static int ScanThresholdCrossing(double[] data, int current, bool scanRight,
            double level, bool aboveIsZone, out string message)
        {
            string entering = aboveIsZone ? "Entering overbought" : "Entering oversold";
            string leaving  = aboveIsZone ? "Leaving overbought"  : "Leaving oversold";

            if (scanRight)
            {
                for (int i = current + 1; i < data.Length; i++)
                {
                    if (double.IsNaN(data[i]) || double.IsNaN(data[i - 1])) continue;
                    bool inZone     = aboveIsZone ? data[i]     >= level : data[i]     <= level;
                    bool inZonePrev = aboveIsZone ? data[i - 1] >= level : data[i - 1] <= level;
                    if (inZone != inZonePrev) { message = inZone ? entering : leaving; return i; }
                }
            }
            else
            {
                for (int i = current - 1; i > 0; i--)
                {
                    if (double.IsNaN(data[i]) || double.IsNaN(data[i - 1])) continue;
                    bool inZone     = aboveIsZone ? data[i]     >= level : data[i]     <= level;
                    bool inZonePrev = aboveIsZone ? data[i - 1] >= level : data[i - 1] <= level;
                    if (inZone != inZonePrev) { message = inZone ? entering : leaving; return i; }
                }
            }
            message = string.Empty;
            return -1;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static double GetNamedLevelValue(ChartSeries series, params string[] nameFragments)
        {
            foreach (var level in series.Levels)
                foreach (var frag in nameFragments)
                    if (level.Name.Contains(frag, StringComparison.OrdinalIgnoreCase)) return level.Value;
            return double.NaN;
        }

        private static double GetFirstValidValue(double[]? data)
        {
            if (data == null) return double.NaN;
            foreach (var v in data) if (!double.IsNaN(v)) return v;
            return double.NaN;
        }

        private static string FormatTimestamp(WorkspaceState state, int dataIndex)
        {
            if (state.Data == null || dataIndex < 0 || dataIndex >= state.Data.Count) return string.Empty;
            return state.Data[dataIndex].Date.ToString("t");
        }
    }
}
