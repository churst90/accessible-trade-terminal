using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Monitors series flagged with <see cref="SeriesConfig.IsAutoNarrated"/> and announces
    /// new indicator signals and zone transitions via TTS as they appear on live bar closes.
    ///
    /// Signal detection rules:
    ///   Marker components (Dot/Arrow/Diamond/TriangleUp/Down/Square/Cross/ZeroDot):
    ///     A non-NaN value at a bar index that appeared AFTER narration was enabled is announced.
    ///     A 20-bar look-back window catches signals from pivot-based indicators whose confirmation
    ///     is delayed by several bars (e.g. Cipher SR pivots that need pivotBars future bars).
    ///     Components with UsesGradientSpeech (continuous ribbon like WT Momentum) are excluded —
    ///     their zone transitions are handled by the oscillator context path.
    ///
    ///   Oscillator components:
    ///     Zone transitions (Normal ↔ Overbought/Oversold) and crossovers are detected
    ///     on bar close via IIndicatorContextAnalyzer and announced on the first occurrence.
    ///
    /// Seeding: when narration is enabled for a series, the current bar count is recorded.
    /// Only bars with index >= that count are eligible for marker announcements, preventing
    /// retroactive announcement of pre-existing signals.
    /// </summary>
    public sealed class AutoNarrationService : IAutoNarrationService, IDisposable
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly IIndicatorContextAnalyzer _contextAnalyzer;
        private readonly List<IDisposable> _subs = new();

        // ── Per-series tracking ──────────────────────────────────────────────────

        /// <summary>
        /// Bar count when narration was enabled for each series.
        /// Bars with index &lt; seedCount are historical and never announced.
        /// Key = series ID.
        /// </summary>
        private readonly Dictionary<string, int> _seedBarCounts = new();

        /// <summary>
        /// Tracks which bar indices have already been announced per series+component.
        /// Key = "{seriesId}:{componentName}".
        /// </summary>
        private readonly Dictionary<string, HashSet<int>> _announcedMarkers = new();

        /// <summary>
        /// Last zone and crossover state for oscillator transition detection.
        /// Key = "{seriesId}:{componentName}".
        /// </summary>
        private readonly Dictionary<string, (ZoneStatus Zone, CrossoverStatus Crossover)> _lastOscState = new();

        /// <summary>
        /// Last known price position relative to cloud boundaries.
        /// Key = "{seriesId}:{componentName}". Value = "inside", "above", or "below".
        /// </summary>
        private readonly Dictionary<string, string> _lastCloudPosition = new();

        // Tracks the last confirmed pivot bar index seen per series+component, for pivot-based indicators.
        // Key = "{seriesId}:{componentName}". Value = last bar index where a non-NaN dot was seen.
        private readonly Dictionary<string, int> _lastSeenPivotIndex = new();

        // Tracks the last zone-line value seen per series+component, for break detection.
        // Key = "{seriesId}:{componentName}". Value = last non-NaN zone value.
        private readonly Dictionary<string, double> _lastZoneLineValue = new();

        // Tracks the last touch count seen per series+component.
        // Key = "{seriesId}:{componentName}". Value = last touch count integer.
        private readonly Dictionary<string, int> _lastTouchCount = new();

        // Tracks whether we are currently "in proximity" to a zone level, to prevent repeat announcements.
        // Key = "{seriesId}:{componentName}". Value = true if we already announced "approaching" for this level.
        private readonly Dictionary<string, bool> _inProximity = new();

        /// <summary>
        /// Tracks whether price was above each zone-line component on the previous bar.
        /// Key = "{seriesId}:{componentName}". Value = true if price was above the line.
        /// Used to detect price crossing above or below a zone line.
        /// </summary>
        private readonly Dictionary<string, bool> _lastPriceAboveZone = new();

        /// <summary>Set of series IDs that were narrated in the previous StateStream emission.</summary>
        private HashSet<string> _prevNarratedIds = new();

        /// <summary>Data count seen on the last RedrawEvent. Zero = uninitialized.</summary>
        private int _lastDataCount = 0;

        /// <summary>
        /// Window of bars to scan behind the just-closed bar to catch delayed-confirmation signals
        /// (e.g. SR pivots that need pivotBars future bars before they appear in the data).
        /// 20 is generous enough for the maximum AutoScale pivotBars = 15.
        /// </summary>
        private const int PivotConfirmWindow = 20;

        public AutoNarrationService(
            IWorkspaceStore store,
            IEventBus eventBus,
            ISpeechFeedbackRouter speechRouter,
            IIndicatorContextAnalyzer contextAnalyzer)
        {
            _store = store;
            _eventBus = eventBus;
            _speechRouter = speechRouter;
            _contextAnalyzer = contextAnalyzer;

            // Detect narration toggled on/off for specific series so we can seed or clear state.
            _subs.Add(store.StateStream.Subscribe(OnStateChanged));

            // Scan for new signals after every indicator recalculation pass.
            // RedrawEvent fires at the end of both RecalculateAllAsync and RecalculateLastAsync,
            // covering every bar close and every live intra-bar tick.
            _subs.Add(_eventBus.AsObservable<RedrawEvent>()
                .Subscribe(_ => OnIndicatorsUpdated()));
        }

        // ── StateStream: detect narration enable/disable ─────────────────────────

        private void OnStateChanged(WorkspaceState state)
        {
            var currentIds = new HashSet<string>(
                state.ActiveSeries.Where(s => s.IsAutoNarrated).Select(s => s.Id));

            // Newly enabled — record seed bar count so no historical signals fire
            foreach (var id in currentIds.Except(_prevNarratedIds))
            {
                int barCount = state.Data?.Count ?? 0;
                _seedBarCounts[id] = barCount;

                var series = state.ActiveSeries.FirstOrDefault(s => s.Id == id);
                if (series != null)
                {
                    SeedOscillatorState(series, state);
                    SeedZoneLineState(series, state);
                    SeedCloudState(series, state);
                }
            }

            // Disabled — clean up tracking to keep dictionaries lean
            foreach (var id in _prevNarratedIds.Except(currentIds))
            {
                _seedBarCounts.Remove(id);
                foreach (var k in _announcedMarkers.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _announcedMarkers.Remove(k);
                foreach (var k in _lastOscState.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastOscState.Remove(k);
                foreach (var k in _lastSeenPivotIndex.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastSeenPivotIndex.Remove(k);
                foreach (var k in _lastZoneLineValue.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastZoneLineValue.Remove(k);
                foreach (var k in _lastTouchCount.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastTouchCount.Remove(k);
                foreach (var k in _inProximity.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _inProximity.Remove(k);
                foreach (var k in _lastPriceAboveZone.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastPriceAboveZone.Remove(k);
                foreach (var k in _lastCloudPosition.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastCloudPosition.Remove(k);
            }

            _prevNarratedIds = currentIds;
        }

        // ── RedrawEvent: scan for new signals ────────────────────────────────────

        private void OnIndicatorsUpdated()
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return;
            if (state.DataStatus == DataStatus.LoadingHistorical) return;
            if (state.InitStatus != InitializationStatus.Ready) return;
            if (!state.IsSpeechEnabled) return;
            if (!_prevNarratedIds.Any()) return;

            int currentCount = state.Data.Count;
            bool isNewBar = currentCount > _lastDataCount && _lastDataCount > 0;
            int scanIndex = isNewBar ? _lastDataCount - 1 : currentCount - 1;
            _lastDataCount = currentCount;

            // Only scan confirmed (closed) bars to avoid announcing unstable live-bar values.
            // On bar close: just-closed bar is scanIndex. On intra-bar tick: penultimate bar.
            int closedBound = isNewBar ? scanIndex : currentCount - 2;
            if (closedBound < 0) return;

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsAutoNarrated) continue;
                if (!_seedBarCounts.TryGetValue(series.Id, out int seedCount)) continue;

                // Scan window: covers recently-confirmed delayed signals (pivot indicators).
                // seedCount is the exclusive lower bound — bars before it are historical.
                int scanFrom = Math.Max(seedCount, closedBound - PivotConfirmWindow);
                if (scanFrom > closedBound) continue;

                ScanSeriesForChanges(series, state, scanFrom, closedBound, isNewBar);
            }
        }

        // ── Core scanning logic ──────────────────────────────────────────────────

        private void ScanSeriesForChanges(ChartSeries series, WorkspaceState state, int fromIndex, int toIndex, bool isBarClose)
        {
            // 1. Marker signals (discrete signal dots/arrows/shapes)
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!IsMarkerDisplayType(comp.DisplayType)) continue;
                if (comp.UsesGradientSpeech) continue;

                string markerKey = $"{series.Id}:{comp.Name}";
                if (!_announcedMarkers.TryGetValue(markerKey, out var announced))
                {
                    announced = new HashSet<int>();
                    _announcedMarkers[markerKey] = announced;
                }

                string pivotKey = $"{series.Id}:{comp.Name}:pivot";
                int pivotLowerBound = _lastSeenPivotIndex.TryGetValue(pivotKey, out int lsp)
                    ? lsp + 1
                    : fromIndex;
                int effectiveScanFrom = Math.Max(pivotLowerBound, toIndex - PivotConfirmWindow);
                if (effectiveScanFrom > toIndex) continue;

                var data = series.GetComponentData(comp.Name);
                if (data == null) continue;
                for (int barIndex = effectiveScanFrom; barIndex <= toIndex; barIndex++)
                {
                    if (announced.Contains(barIndex)) continue;
                    if (barIndex < 0 || barIndex >= data.Length) continue;
                    double val = data[barIndex];
                    if (double.IsNaN(val)) continue;

                    string msg = BuildMarkerMessage(series, comp, val, state, barIndex);
                    if (!string.IsNullOrEmpty(msg))
                    {
                        _speechRouter.Speak(msg, interrupt: false);
                        announced.Add(barIndex);
                        if (!_lastSeenPivotIndex.TryGetValue(pivotKey, out int cur) || barIndex > cur)
                            _lastSeenPivotIndex[pivotKey] = barIndex;
                    }
                }
            }

            // 1b. SR zone line scanning — runs on every update
            ScanZoneLines(series, state, toIndex);

            // 1c. Cloud entry/exit detection — runs on every update
            ScanCloudTransitions(series, state, toIndex);

            // 2. Oscillator zone transitions — only on bar close (confirmed candle)
            if (!isBarClose) return;

            var indexedState = state with { CurrentDataIndex = toIndex };
            foreach (var oscContext in _contextAnalyzer.AnalyzeAll(series, indexedState))
            {
                string oscKey = $"{series.Id}:{oscContext.ComponentName}";

                if (_lastOscState.TryGetValue(oscKey, out var prev))
                {
                    // Zone entered
                    if (prev.Zone != oscContext.Zone)
                    {
                        string? zoneMsg = BuildZoneTransitionMessage(series.FriendlyName, oscContext.ComponentName, oscContext.Zone, prev.Zone);
                        if (zoneMsg != null)
                            _speechRouter.Speak(zoneMsg, interrupt: false);
                    }

                    // Crossover appeared
                    if (oscContext.Crossover != CrossoverStatus.None && prev.Crossover == CrossoverStatus.None)
                    {
                        string? crossMsg = BuildCrossoverMessage(series.FriendlyName, oscContext);
                        if (crossMsg != null)
                            _speechRouter.Speak(crossMsg, interrupt: false);
                    }
                }

                _lastOscState[oscKey] = (oscContext.Zone, oscContext.Crossover);
            }
        }

        // ── State seeding (prevents false alarms when narration is enabled) ──────

        private void SeedOscillatorState(ChartSeries series, WorkspaceState state)
        {
            foreach (var ctx in _contextAnalyzer.AnalyzeAll(series, state))
            {
                string oscKey = $"{series.Id}:{ctx.ComponentName}";
                _lastOscState[oscKey] = (ctx.Zone, ctx.Crossover);
            }
        }

        private void SeedZoneLineState(ChartSeries series, WorkspaceState state)
        {
            int idx = (state.Data?.Count ?? 1) - 1;
            if (idx < 0) return;
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible) continue;
                if (IsMarkerDisplayType(comp.DisplayType) && !comp.UsesGradientSpeech)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data == null) continue;
                    string pivotKey = $"{series.Id}:{comp.Name}:pivot";
                    int lastPivot = -1;
                    for (int i = Math.Min(idx, data.Length - 1); i >= 0; i--)
                    {
                        if (!double.IsNaN(data[i])) { lastPivot = i; break; }
                    }
                    _lastSeenPivotIndex[pivotKey] = lastPivot;
                }
                if (comp.IsZoneLine)
                {
                    var data = series.GetComponentData(comp.Name);
                    if (data != null && idx < data.Length && !double.IsNaN(data[idx]))
                    {
                        string zoneKey = $"{series.Id}:{comp.Name}";
                        _lastZoneLineValue[zoneKey] = data[idx];
                        _inProximity[zoneKey] = false;
                        // Seed the cross direction so the first scan doesn't fire a false cross.
                        double seedClose = state.Data != null && idx < state.Data.Count
                            ? (double)state.Data[idx].Close
                            : double.NaN;
                        if (!double.IsNaN(seedClose) && seedClose > 0)
                            _lastPriceAboveZone[zoneKey] = seedClose > data[idx];
                    }
                }
            }
            foreach (var comp in series.Components)
            {
                if (!comp.IsZoneLine) continue;
                string dotName = comp.Name.Replace(" Zone", "");
                string touchKey = dotName + "_touches";
                var touchData = series.GetComponentData(touchKey);
                if (touchData != null && idx < touchData.Length && !double.IsNaN(touchData[idx]))
                {
                    string tcKey = $"{series.Id}:{comp.Name}:touches";
                    _lastTouchCount[tcKey] = (int)touchData[idx];
                }
            }
        }

        private void SeedCloudState(ChartSeries series, WorkspaceState state)
        {
            int idx = state.CurrentDataIndex;
            if (state.Data == null || idx < 0 || idx >= state.Data.Count) return;
            double close = (double)state.Data[idx].Close;

            foreach (var comp in series.Components)
            {
                if (comp.DisplayType != ComponentDisplayType.Cloud) continue;
                if (string.IsNullOrEmpty(comp.UpperComponentName) || string.IsNullOrEmpty(comp.LowerComponentName)) continue;

                var upperData = series.GetComponentData(comp.UpperComponentName);
                var lowerData = series.GetComponentData(comp.LowerComponentName);
                if (upperData.Length <= idx || lowerData.Length <= idx) continue;

                double u = upperData[idx], l = lowerData[idx];
                if (double.IsNaN(u) || double.IsNaN(l)) continue;

                double hi = Math.Max(u, l), lo = Math.Min(u, l);
                string position = (close >= lo && close <= hi) ? "inside"
                    : close > hi ? "above" : "below";

                _lastCloudPosition[$"{series.Id}:{comp.Name}"] = position;
            }
        }

        private void ScanZoneLines(ChartSeries series, WorkspaceState state, int barIndex)
        {
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return;
            double currentClose = (double)state.Data[barIndex].Close;
            if (currentClose <= 0) return;

            var bullishCrosses = new List<string>();
            var bearishCrosses = new List<string>();

            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!comp.IsZoneLine) continue;

                var data = series.GetComponentData(comp.Name);
                if (data == null || barIndex >= data.Length) continue;

                double currentVal = data[barIndex];
                string zoneKey = $"{series.Id}:{comp.Name}";
                bool isResistance = comp.Name.Contains("Resistance") || comp.Name.Contains("resistance");
                string lineName = comp.DisplayName ?? comp.Name;

                // ── Break detection ──────────────────────────────────────────────────
                if (_lastZoneLineValue.TryGetValue(zoneKey, out double lastVal))
                {
                    if (double.IsNaN(currentVal) && !double.IsNaN(lastVal))
                    {
                        string breakMsg = isResistance
                            ? $"{series.FriendlyName}: Resistance at {lastVal:F0} broken."
                            : $"{series.FriendlyName}: Support at {lastVal:F0} broken.";
                        _speechRouter.Speak(breakMsg, interrupt: false);
                        _lastZoneLineValue.Remove(zoneKey);
                        _inProximity.Remove(zoneKey);
                        _lastPriceAboveZone.Remove(zoneKey);
                        string tcKey2 = $"{series.Id}:{comp.Name}:touches";
                        _lastTouchCount.Remove(tcKey2);
                        continue;
                    }
                }

                if (double.IsNaN(currentVal)) continue;

                _lastZoneLineValue[zoneKey] = currentVal;

                // ── Touch detection ──────────────────────────────────────────────────
                string dotName = comp.Name.Replace(" Zone", "");
                string touchCompKey = dotName + "_touches";
                var touchData = series.GetComponentData(touchCompKey);
                if (touchData != null && barIndex < touchData.Length && !double.IsNaN(touchData[barIndex]))
                {
                    int currentTouches = (int)touchData[barIndex];
                    string tcKey = $"{series.Id}:{comp.Name}:touches";
                    if (_lastTouchCount.TryGetValue(tcKey, out int lastTouches) && currentTouches > lastTouches)
                    {
                        string touchMsg = isResistance
                            ? $"{series.FriendlyName}: Price tested resistance at {SpeechPriceFormatter.FormatPrice(currentVal)}. Tested {currentTouches} {(currentTouches == 1 ? "time" : "times")}."
                            : $"{series.FriendlyName}: Price tested support at {SpeechPriceFormatter.FormatPrice(currentVal)}. Tested {currentTouches} {(currentTouches == 1 ? "time" : "times")}.";
                        _speechRouter.Speak(touchMsg, interrupt: false);
                    }
                    _lastTouchCount[tcKey] = currentTouches;
                }

                // ── Proximity detection ──────────────────────────────────────────────
                double distPct = Math.Abs(currentClose - currentVal) / currentClose * 100.0;
                bool nowNear = distPct <= 0.5;
                bool wasNear = _inProximity.TryGetValue(zoneKey, out bool prevNear) && prevNear;
                if (nowNear && !wasNear)
                {
                    string approachMsg = isResistance
                        ? $"{series.FriendlyName}: Approaching resistance at {SpeechPriceFormatter.FormatPrice(currentVal)}."
                        : $"{series.FriendlyName}: Approaching support at {SpeechPriceFormatter.FormatPrice(currentVal)}.";
                    _speechRouter.Speak(approachMsg, interrupt: false);
                }
                _inProximity[zoneKey] = nowNear;

                // ── Cross detection ──────────────────────────────────────────────────
                bool priceAboveNow = currentClose > currentVal;
                if (_lastPriceAboveZone.TryGetValue(zoneKey, out bool wasAbove))
                {
                    if (!wasAbove && priceAboveNow)
                        bullishCrosses.Add($"{lineName} at {SpeechPriceFormatter.FormatPrice(currentVal)}");
                    else if (wasAbove && !priceAboveNow)
                        bearishCrosses.Add($"{lineName} at {SpeechPriceFormatter.FormatPrice(currentVal)}");
                }
                _lastPriceAboveZone[zoneKey] = priceAboveNow;
            }

            // Announce grouped cross messages (avoids flood of individual announcements).
            if (bullishCrosses.Count > 0)
            {
                string crossed = string.Join(", ", bullishCrosses);
                _speechRouter.Speak($"{series.FriendlyName}: Price crossed above {crossed}.", interrupt: false);
            }
            if (bearishCrosses.Count > 0)
            {
                string crossed = string.Join(", ", bearishCrosses);
                _speechRouter.Speak($"{series.FriendlyName}: Price crossed below {crossed}.", interrupt: false);
            }
        }

        // ── Cloud entry/exit detection ───────────────────────────────────────────

        private void ScanCloudTransitions(ChartSeries series, WorkspaceState state, int barIndex)
        {
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return;
            double close = (double)state.Data[barIndex].Close;
            if (close <= 0 || double.IsNaN(close)) return;

            foreach (var comp in series.Components)
            {
                if (comp.DisplayType != ComponentDisplayType.Cloud) continue;
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (string.IsNullOrEmpty(comp.UpperComponentName) || string.IsNullOrEmpty(comp.LowerComponentName)) continue;

                var upperData = series.GetComponentData(comp.UpperComponentName);
                var lowerData = series.GetComponentData(comp.LowerComponentName);
                if (upperData.Length <= barIndex || lowerData.Length <= barIndex) continue;

                double u = upperData[barIndex];
                double l = lowerData[barIndex];
                if (double.IsNaN(u) || double.IsNaN(l)) continue;

                double hi = Math.Max(u, l);
                double lo = Math.Min(u, l);

                string position;
                if (close >= lo && close <= hi)
                    position = "inside";
                else if (close > hi)
                    position = "above";
                else
                    position = "below";

                string cloudKey = $"{series.Id}:{comp.Name}";

                if (_lastCloudPosition.TryGetValue(cloudKey, out var prev) && prev != position)
                {
                    string displayName = !string.IsNullOrEmpty(comp.DisplayName) ? comp.DisplayName : comp.Name;
                    string? msg = (prev, position) switch
                    {
                        (_, "inside") => $"{series.FriendlyName}: Price entered {displayName}.",
                        ("inside", _) => $"{series.FriendlyName}: Price exited {displayName}.",
                        ("below", "above") => $"{series.FriendlyName}: Price crossed above {displayName}.",
                        ("above", "below") => $"{series.FriendlyName}: Price crossed below {displayName}.",
                        _ => null
                    };

                    if (msg != null)
                        _speechRouter.Speak(msg, interrupt: false);
                }

                _lastCloudPosition[cloudKey] = position;
            }
        }

        // ── Message builders ─────────────────────────────────────────────────────

        private static string BuildMarkerMessage(
            ChartSeries series, ComponentConfig comp, double val,
            WorkspaceState state, int barIndex)
        {
            string price = (state.Data != null && barIndex < state.Data.Count)
                ? SpeechPriceFormatter.FormatPrice(state.Data[barIndex].Close)
                : SpeechPriceFormatter.FormatPrice(val);
            string valueStr = val.ToString("F1");

            if (!string.IsNullOrEmpty(comp.SignalSpeechTemplate))
            {
                return comp.SignalSpeechTemplate
                    .Replace("{price}", price)
                    .Replace("{value}", valueStr)
                    .Replace("{name}", !string.IsNullOrEmpty(comp.DisplayName) ? comp.DisplayName : comp.Name)
                    .Replace("{series}", series.FriendlyName);
            }

            string compName = !string.IsNullOrEmpty(comp.DisplayName) ? comp.DisplayName : comp.Name;
            return $"{series.FriendlyName}: {compName} at {price}.";
        }

        private static string? BuildZoneTransitionMessage(
            string seriesName, string componentName, ZoneStatus current, ZoneStatus previous)
        {
            // Component-specific overrides — avoids generic "overbought/oversold" labels
            // where those terms don't reflect the underlying meaning.
            if (componentName.Equals("Anchor Wave", StringComparison.OrdinalIgnoreCase))
                return current switch
                {
                    ZoneStatus.Overbought => $"{seriesName}: Anchor wave overbought.",
                    ZoneStatus.Oversold   => $"{seriesName}: Anchor wave oversold.",
                    ZoneStatus.Normal when previous is ZoneStatus.Overbought or ZoneStatus.Oversold
                                          => $"{seriesName}: Anchor wave returning to neutral.",
                    _ => null
                };

            if (componentName.Equals("Trigger Wave", StringComparison.OrdinalIgnoreCase))
                return current switch
                {
                    ZoneStatus.Overbought => $"{seriesName}: Trigger positive.",
                    ZoneStatus.Oversold   => $"{seriesName}: Trigger negative.",
                    // Suppress neutral return for trigger — it oscillates too frequently.
                    _ => null
                };

            if (componentName.Equals("Money Flow Wave", StringComparison.OrdinalIgnoreCase))
                return current switch
                {
                    ZoneStatus.Overbought => $"{seriesName}: Money flow bullish.",
                    ZoneStatus.Oversold   => $"{seriesName}: Money flow bearish.",
                    ZoneStatus.Normal when previous is ZoneStatus.Overbought or ZoneStatus.Oversold
                                          => $"{seriesName}: Money flow neutral.",
                    _ => null
                };

            return current switch
            {
                ZoneStatus.Overbought => $"{seriesName}: {componentName} entered overbought.",
                ZoneStatus.Oversold   => $"{seriesName}: {componentName} entered oversold.",
                ZoneStatus.Normal when previous is ZoneStatus.Overbought or ZoneStatus.Oversold
                                      => $"{seriesName}: {componentName} left extreme zone.",
                _ => null
            };
        }

        private static string? BuildCrossoverMessage(string seriesName, IndicatorContext ctx)
        {
            return ctx.Crossover switch
            {
                CrossoverStatus.BullishCrossover => $"{seriesName}: {ctx.ComponentName} bullish crossover.",
                CrossoverStatus.BearishCrossover => $"{seriesName}: {ctx.ComponentName} bearish crossover.",
                _ => null
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsMarkerDisplayType(ComponentDisplayType dt) => dt switch
        {
            ComponentDisplayType.Dot or ComponentDisplayType.Arrow
            or ComponentDisplayType.Diamond or ComponentDisplayType.TriangleUp
            or ComponentDisplayType.TriangleDown or ComponentDisplayType.Square
            or ComponentDisplayType.Cross or ComponentDisplayType.ZeroDot => true,
            _ => false
        };

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
        }
    }
}
