using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Core.Services.Accessibility
{
    public interface INavigationFeedbackManager
    {
        bool IsSpeechEnabled { get; set; }
        void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false);
    }

    public class NavigationFeedbackManager : INavigationFeedbackManager
    {
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly ISpeechFormatter _formatter;
        private readonly IEventBus _eventBus;
        private readonly INavigationSonifier _navigationSonifier;
        private readonly IIndicatorEngine _indicatorEngine;

        // Proximity tolerance: zone is "active" if the bar's H/L range comes within 0.5% of the zone value.
        private const double ZoneProximityPct = 0.005;

        private string _lastSpokenSeriesId = "";
        private WorkspaceState? _previousState;

        public bool IsSpeechEnabled { get; set; } = true;

        public NavigationFeedbackManager(
            ISpeechFeedbackRouter speechRouter,
            ISpeechFormatter formatter,
            IEventBus eventBus,
            INavigationSonifier navigationSonifier,
            IIndicatorEngine indicatorEngine)
        {
            _speechRouter = speechRouter;
            _formatter = formatter;
            _eventBus = eventBus;
            _navigationSonifier = navigationSonifier;
            _indicatorEngine = indicatorEngine;
        }

        /// <summary>
        /// Speech system — 3 paths evaluated in order for each component:
        ///   Path 1: Provider GetComponentSpeech() — imperative, full context access. Return non-null to consume.
        ///   Path 2: Metadata SpeechTemplate / SignalSpeechTemplate — declarative token expansion.
        ///           SpeechTemplate: continuous oscillator/line components. Tokens: {name} {type} {value} {value:F1} {value:F2} {zone} {gradient_speech}.
        ///           SignalSpeechTemplate: marker signals present on a bar. Tokens: {price} {name}.
        ///   Path 3: SpeechFormatter generic fallback — display type + raw value.
        /// </summary>
        public void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false)
        {
            if (state.Data == null || !state.Data.Any() || state.CurrentDataIndex < 0 || state.CurrentDataIndex >= state.Data.Count)
            {
                _previousState = state;
                return;
            }

            // When Heikin-Ashi is active, transform the current raw bar to its HA equivalent
            // so that spoken OHLC values match what the user sees on screen.
            Ohlcv pt = state.Data[state.CurrentDataIndex];
            if (state.IsHeikinAshi && state.Data.Count > 1)
            {
                var rawSlice = new List<Ohlcv>(state.CurrentDataIndex + 1);
                for (int i = 0; i <= state.CurrentDataIndex; i++) rawSlice.Add(state.Data[i]);
                var haData = ChartMath.CalculateHeikinAshi(rawSlice);
                if (haData.Count > 0) pt = haData[^1];
            }

            var seriesId = state.FocusedSeriesId ?? "candles";
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null)
            {
                _previousState = state;
                return;
            }

            bool isHeatmap = s.Components.Any(c => c.DisplayType == ComponentDisplayType.Heatmap);
            bool isProfile = s.IsProfile || s.Components.Any(c => c.DisplayType == ComponentDisplayType.Profile || c.DisplayType == ComponentDisplayType.Distribution);

            // 1. Sonification is handled exclusively by SonificationManager.SyncNavigationSlots,
            // which observes StateStream directly. This method handles SPEECH ONLY.
            // Removing the duplicate SonifyCurrentContext call here eliminates the dual-path
            // race where two different callers both write to voice slot 0 with different durations
            // (0.4s vs 0.2s), causing the second to override the first with a Silence() + restart.

            // ── Coordinate Entry mode: always speak price + timestamp regardless of other settings ──
            if (state.IsCoordinateEntryMode)
            {
                string ts = pt.Date.ToString("t");
                string ceMsg = $"{SpeechPriceFormatter.FormatPrice(pt.Close)}, {ts}";

                // When anchor 1 is already confirmed, also speak the change from that anchor.
                if (state.CoordinateEntryAnchorCount == 1 &&
                    state.CoordinateEntryAnchor1Index >= 0 &&
                    state.CoordinateEntryAnchor1Index < state.Data.Count)
                {
                    double anchor1Close = (double)state.Data[state.CoordinateEntryAnchor1Index].Close;
                    double delta = (double)pt.Close - anchor1Close;
                    string sign = delta >= 0 ? "+" : "";
                    ceMsg += $". Change from anchor: {sign}{delta:F0}";
                }

                _speechRouter.Speak(ceMsg, interrupt: isUserInitiated);
                _previousState = state;
                return;
            }

            if (!IsSpeechEnabled)
            {
                _previousState = state;
                return;
            }

            // 2. Detect Series Switch (Silence the prefixes like 'Home', 'End', 'Live', etc.)
            bool seriesIdChanged = _lastSpokenSeriesId != seriesId;
            _lastSpokenSeriesId = seriesId;
            
            // Treat null and internal navigation sentinels as no prefix.
            string speechPrefix = (!string.IsNullOrEmpty(prefixMessage)
                && prefixMessage != "NAV_MOVE"
                && prefixMessage != "NAV_SERIES_NEXT"
                && prefixMessage != "NAV_SERIES_PREV") ? prefixMessage : "";

            if (seriesIdChanged)
            {
                // Count ALL components regardless of visibility — hidden data companions
                // (like Cipher S z-score / deviation buffers) are still part of the indicator.
                int count     = s.Components.Count(c => !c.IsMuted);
                string compWord = count == 1 ? "component" : "components";

                // State suffix: callers navigating to a hidden or muted series must hear
                // that status immediately so they know why there is no sound.
                string stateSuffix = !s.IsVisible     ? ", hidden"
                                   : s.IsMuted        ? ", muted"
                                   : s.IsAutoNarrated ? ", narrating"
                                   : "";

                string countMsg;
                if (isHeatmap)
                {
                    // FirstOrDefault() always hits historical bars with empty inner lists.
                    // Use the last non-empty bar so the count reflects actual accumulated live data.
                    int levelCount = s.HeatmapData?.LastOrDefault(l => l != null && l.Count > 0)?.Count ?? 0;
                    countMsg = levelCount > 0 ? $"Liquidity heatmap, {levelCount} levels" : "Liquidity heatmap, no live data yet";
                }
                else if (isProfile)
                {
                    int binCount = s.Data.ProfileBins?.Count ?? 0;
                    countMsg = $"{binCount} {(binCount == 1 ? "bin" : "bins")}";
                }
                else
                {
                    // Count distinct sub-panes (components with a non-null/non-empty SubPaneName).
                    int distinctSubPanes = s.Components
                        .Select(c => c.SubPaneName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    // Total pane count = 1 (main) + sub-pane count.
                    int totalPanes = 1 + distinctSubPanes;

                    countMsg = $"{count} {compWord}";
                    if (totalPanes > 1)
                        countMsg += $", {totalPanes} panes";
                }
                // Use Name (base indicator name without parameter values) for speech.
                // FriendlyName includes baked-in parameter values (e.g. "RSI 14") which are
                // noise in navigation context — the user knows what they added.
                speechPrefix = $"{s.Name}{stateSuffix}. {countMsg}. " + speechPrefix;
            }

            // 2b. Sub-pane boundary announcement during Up/Down component navigation.
            // When the newly focused component is in a different sub-pane than the previous one,
            // prepend the pane display name so the user hears "[Pane]. [Component]..." on transition only.
            if (isYMove && !seriesIdChanged && _previousState != null)
            {
                int prevCompIdx = Math.Clamp(_previousState.FocusedComponentIndex, 0, s.Components.Count - 1);
                int currCompIdx = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
                if (prevCompIdx != currCompIdx)
                {
                    string? prevPane = s.Components[prevCompIdx].SubPaneName;
                    string? currPane = s.Components[currCompIdx].SubPaneName;
                    bool paneChanged = !string.Equals(prevPane, currPane, StringComparison.OrdinalIgnoreCase);
                    if (paneChanged)
                    {
                        string paneLabel = GetPaneDisplayName(currPane, s);
                        speechPrefix = paneLabel + ". " + speechPrefix;
                    }
                }
            }

            // 3. Formatted Speech
            string finalSpeech = "";

            // Zone proximity and additional signal speech are only meaningful when price is the navigation context.
            // Computed here so all code paths below share the same value without re-evaluation.
            bool focusedOnCandleSeries = string.IsNullOrEmpty(state.FocusedSeriesId)
                || state.FocusedSeriesId == state.PrimarySeriesId
                || state.FocusedSeriesId == CoreSeriesIds.Candles
                || state.FocusedSeriesId == CoreSeriesIds.Price;

            if (isHeatmap)
            {
                int heatmapIdx = FindNearestHeatmapIndex(s, state.CurrentDataIndex);
                if (heatmapIdx >= 0)
                    finalSpeech = _formatter.FormatHeatmapFeedback(state, isXMove, isYMove, s, heatmapIdx, state.FocusedBinIndex, speechPrefix);
                else
                    finalSpeech = string.IsNullOrEmpty(speechPrefix) ? "No live data yet" : $"{speechPrefix} No live data yet";
            }
            else if (isProfile)
            {
                if ((s.Data.ProfileBins?.Count ?? 0) > 0)
                    finalSpeech = _formatter.FormatProfileFeedback(state, isXMove, isYMove, s, state.FocusedBinIndex, speechPrefix);
                else
                    finalSpeech = string.IsNullOrEmpty(speechPrefix) ? "No data" : $"{speechPrefix} No data";
            }
            else
            {
                // Try provider-level contextual speech first (Component context only — not Series summary).
                if (state.LastInteractionContext == InteractionContext.Component &&
                    !string.IsNullOrEmpty(s.IndicatorCode))
                {
                    var provider = _indicatorEngine.GetProvider(s.IndicatorCode);
                    if (provider != null)
                    {
                        int compIdx = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
                        var focusedComp = s.Components[compIdx];

                        // Build allComponentData dict keyed by DisplayName (or Name when DisplayName is null).
                        // Also include companion arrays that providers write to the buffer but do not
                        // register as navigable components:
                        //   _color  — gradient source (e.g. "WT Momentum_color" → raw WT1 value)
                        //   _touches — wick touch count (e.g. "Resistance_touches")
                        // These are accessed by providers in GetComponentSpeech via raw key lookups.
                        var compDataDict = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in s.Components)
                        {
                            var cd = s.GetComponentData(c.Name);
                            if (cd != null) compDataDict[c.DisplayName ?? c.Name] = cd;

                            // Gradient companion (_color array for GradientDot speech and audio)
                            if (c.UsesGradientSpeech)
                            {
                                var colorKey = c.Name + "_color";
                                var colorData = s.GetComponentData(colorKey);
                                if (colorData.Length > 0 && !compDataDict.ContainsKey(colorKey))
                                    compDataDict[colorKey] = colorData;
                            }

                            // Touch count companion (_touches array for S/R pivot speech)
                            var touchKey = c.Name + "_touches";
                            var touchData = s.GetComponentData(touchKey);
                            if (touchData.Length > 0 && !compDataDict.ContainsKey(touchKey))
                                compDataDict[touchKey] = touchData;
                        }

                        // Inject the current live bar's close so providers (e.g. SR) can compute
                        // distance relative to the present price rather than the navigated bar's
                        // historical close, regardless of how far back the cursor has scrolled.
                        if (state.Data != null && state.Data.Count > 0)
                            compDataDict["__live_close"] = new double[] { (double)state.Data[^1].Close };

                        double compValue = double.NaN;
                        var cdata = s.GetComponentData(focusedComp.Name);
                        if (cdata != null && state.CurrentDataIndex >= 0 && state.CurrentDataIndex < cdata.Length)
                            compValue = cdata[state.CurrentDataIndex];

                        // Pass Name (the provider's internal switch key), not DisplayName.
                        // DisplayName is the spoken label; Name is the component identity providers match on.
                        string? providerSpeech = provider.GetComponentSpeech(
                            focusedComp.Name, compValue, pt, compDataDict, state.CurrentDataIndex);

                        if (providerSpeech != null)
                        {
                            // On UP/DOWN (component switch): prepend "[Name]. [TypeLabel]. " so the user
                            // always hears what component they just arrived on before the value.
                            // On LEFT/RIGHT (same component, bar scanning): value only — no repeat of name.
                            string valuePrefix = "";
                            if (isYMove)
                            {
                                string typeLabel = GetComponentTypeLabel(focusedComp);
                                // Append "Hidden." or "Muted." so the user knows the visual/audio state of this component.
                                string stateLabel = !focusedComp.IsVisible ? "Hidden. "
                                                  : focusedComp.IsMuted    ? "Muted. "
                                                  : "";
                                valuePrefix = string.IsNullOrEmpty(typeLabel)
                                    ? $"{focusedComp.DisplayName ?? focusedComp.Name}. {stateLabel}"
                                    : $"{focusedComp.DisplayName ?? focusedComp.Name}. {typeLabel}. {stateLabel}";
                            }

                            // Timestamp first — consistent with SpeechFormatter ordering.
                            string tsPrefix = "";
                            if (isXMove && state.SpeakTimestamps && state.TimestampReadLocation != "None" && state.TimestampReadLocation != "Along Y Axis")
                            {
                                string tsFormat = state.SpeechOrder.Contains("TimeOnly") ? "HH:mm"
                                                : state.SpeechOrder.Contains("DateOnly") ? "MMMM dd"
                                                : "MMMM dd, yyyy, HH:mm";
                                tsPrefix = pt.Date.ToLocalTime().ToString(tsFormat) + ". ";
                            }
                            finalSpeech = tsPrefix + (string.IsNullOrEmpty(speechPrefix) ? "" : speechPrefix) + valuePrefix + providerSpeech;

                            if (!string.IsNullOrEmpty(finalSpeech))
                                _speechRouter.Speak(finalSpeech, interrupt: isUserInitiated);
                            if (isXMove && focusedOnCandleSeries)
                                CheckAndPlayZoneProximity(state, state.CurrentDataIndex);
                            _previousState = state;
                            return;
                        }
                    }
                }

                finalSpeech = _formatter.FormatPointFeedback(state, isXMove, isYMove, s, pt, speechPrefix);

                // Cipher S / CandleColor overlay: prepend the sentiment phase name so it is
                // the very first thing the user hears on every bar.  Only injected when
                // navigating the candle/price series — not when inside an indicator sub-series.
                if (focusedOnCandleSeries)
                {
                    string? phasePrefix = GetActiveCandlePhase(state, state.CurrentDataIndex);
                    if (phasePrefix != null)
                        finalSpeech = phasePrefix + ". " + finalSpeech;
                }
            }

            if (!string.IsNullOrEmpty(finalSpeech))
            {
                _speechRouter.Speak(finalSpeech, interrupt: isUserInitiated);
            }

            // Additional signal speech: after primary component speech, announce other active marker signals
            // on the same bar in the same tier order used by the cluster audio tick system (Phase F).
            // Only in Component context (Series context already speaks all components in summary).
            // Only on X-axis moves (bar changes) to avoid repetition during Y navigation.
            // Only when focused on the candle/price series — when navigating inside an indicator (Cipher A/B, SR, etc.)
            // the user is already in that indicator's context; cross-indicator signal announcements are noise.
            // Additional signal speech and zone proximity are suppressed on jump navigation (Home/End/Live)
            // because the user is repositioning, not reading bar-by-bar — the extra context is noise.
            if (!isJump)
            {
                if (isXMove && !isHeatmap && !isProfile && focusedOnCandleSeries && state.LastInteractionContext == InteractionContext.Component)
                {
                    int focusedComp = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
                    string additionalSignals = GetAdditionalSignalSpeech(state, state.CurrentDataIndex, s.Id, focusedComp);
                    if (!string.IsNullOrEmpty(additionalSignals))
                        _speechRouter.Speak(additionalSignals, interrupt: false);
                }

                // Zone proximity earcon: fires only when navigating the candle/price series.
                // When the user has focus inside a different indicator (Cipher A/B, SR, etc.),
                // zone speech is noise — only that indicator's own content should speak.
                if (isXMove && focusedOnCandleSeries)
                    CheckAndPlayZoneProximity(state, state.CurrentDataIndex);
            }

            _previousState = state;
        }

        /// <summary>
        /// Returns a human-readable display name for a sub-pane.
        /// null/empty → "Main pane".
        /// Otherwise, looks for a component in the series whose DisplayName contains the SubPaneName
        /// to derive a friendlier label (e.g. "MF" components named "Money Flow..." → "Money Flow pane").
        /// Fallback: SubPaneName + " pane".
        /// </summary>
        private static string GetPaneDisplayName(string? subPaneName, ChartSeries series)
        {
            if (string.IsNullOrEmpty(subPaneName)) return "Main pane";

            // Look for a component in this pane whose DisplayName contains more descriptive text.
            foreach (var comp in series.Components)
            {
                if (!string.Equals(comp.SubPaneName, subPaneName, StringComparison.OrdinalIgnoreCase)) continue;
                string dn = comp.DisplayName ?? comp.Name;
                // Use the display name if it's meaningfully longer/different than the raw pane key.
                if (!string.IsNullOrEmpty(dn) && !dn.Equals(subPaneName, StringComparison.OrdinalIgnoreCase))
                    return dn.Trim() + " pane";
            }

            return subPaneName + " pane";
        }

        /// <summary>
        /// Returns a spoken type qualifier for the component (e.g., "Oscillator", "Signal", "Level").
        /// Empty string when the type is already implied by the component name, avoiding redundancy.
        /// Used by the UP/DOWN navigation prefix so the user hears "[Name]. [Type]. [Value]."
        /// </summary>
        private static string GetComponentTypeLabel(ComponentConfig comp)
        {
            var dt = comp.DisplayType;

            if (dt is ComponentDisplayType.Oscillator or ComponentDisplayType.ZeroArea)
                return "Oscillator";

            if (dt == ComponentDisplayType.CandleColor)
                return "Sentiment Phase";

            if (dt == ComponentDisplayType.Line)
                return comp.IsZoneLine ? "Level" : "Line";

            if (dt == ComponentDisplayType.Histogram)
                return "Histogram";

            // ZeroDot: discrete zero-line dots — no type qualifier, name carries the context.
            if (dt == ComponentDisplayType.ZeroDot)
                return "";

            // Level role wins over display shape.
            if (comp.Role == ComponentRole.Level)
                return "Level";

            // Remaining marker shapes (Dot, Diamond, Arrow, Cross, Triangle, Square) are signals.
            // Skip "Signal" qualifier when the component name already contains the word "Signal".
            if (dt is ComponentDisplayType.Dot or ComponentDisplayType.Diamond or
                       ComponentDisplayType.Arrow or ComponentDisplayType.Cross or
                       ComponentDisplayType.TriangleUp or ComponentDisplayType.TriangleDown or
                       ComponentDisplayType.Square)
            {
                string name = comp.DisplayName ?? comp.Name;
                return name.Contains("Signal", StringComparison.OrdinalIgnoreCase) ? "" : "Signal";
            }

            return "";
        }


        /// <summary>
        /// Returns the sentiment phase name (e.g. "Max Fear", "Neutral", "Extreme Greed") for the
        /// current bar when a CandleColor overlay (e.g. Cipher S) is active. Returns null when no
        /// such overlay is loaded or when the phase data is not yet available for this bar.
        /// </summary>
        private static string? GetActiveCandlePhase(WorkspaceState state, int dataIndex)
        {
            foreach (var os in state.ActiveSeries)
            {
                if (!os.IsVisible) continue;
                var phaseComp = os.Components.FirstOrDefault(c =>
                    c.DisplayType == ComponentDisplayType.CandleColor && c.IsVisible && c.IsEnabled != false);
                if (phaseComp == null) continue;
                var phaseData = os.GetComponentData(phaseComp.Name);
                if (phaseData == null || dataIndex < 0 || dataIndex >= phaseData.Length) continue;
                double raw = phaseData[dataIndex];
                if (double.IsNaN(raw)) continue;
                int phase = Math.Clamp((int)Math.Round(raw), 0, AudioConstants.PhaseNames.Length - 1);
                return AudioConstants.PhaseNames[phase];
            }
            return null;
        }

        /// <summary>
        /// Returns a "Also: {signal1}. {signal2}." speech string summarising active marker signals
        /// on the current bar from series other than the primary focused one (or from non-focused
        /// components of the focused series in Series-scope context).
        /// Signals are sorted in the same tier order as the cluster audio tick system (Phase F).
        /// Zone line components are excluded (they use zone proximity speech instead).
        /// Returns empty string when there are no additional signals.
        /// </summary>
        private string GetAdditionalSignalSpeech(WorkspaceState state, int dataIndex, string excludeSeriesId, int excludeCompIdx)
        {
            if (dataIndex < 0 || dataIndex >= state.Data.Count) return string.Empty;

            // Collect active marker signals, excluding the focused component.
            var signals = new List<(int tier, bool positive, string speech)>();

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsVisible || series.IsMuted) continue;
                if (series.IsProfile || series.Components.Any(c =>
                    c.DisplayType == ComponentDisplayType.Heatmap ||
                    c.DisplayType == ComponentDisplayType.Profile)) continue;

                // Only report cross-series signals from main-pane overlays.
                // Oscillator-pane signals (Cipher B MF dots, etc.) are irrelevant context
                // when the user is navigating the candle series.
                bool isMainPane = string.IsNullOrEmpty(series.Pane) ||
                                  series.Pane.Equals("Main", StringComparison.OrdinalIgnoreCase) ||
                                  series.Pane.Equals("Price", StringComparison.OrdinalIgnoreCase);
                if (!isMainPane) continue;

                for (int ci = 0; ci < series.Components.Count; ci++)
                {
                    var comp = series.Components[ci];
                    if (!comp.IsVisible || comp.IsMuted) continue;
                    if (!AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType)) continue;
                    if (comp.IsZoneLine) continue;
                    if (comp.SignalSpeechTemplate == null) continue;

                    // Skip the focused component.
                    if (series.Id == excludeSeriesId && ci == excludeCompIdx) continue;

                    var data = series.GetComponentData(comp.Name);
                    if (data == null || dataIndex >= data.Length) continue;
                    double val = data[dataIndex];
                    if (double.IsNaN(val)) continue;

                    // Expand the signal speech template. Magnitude-aware formatting so
                    // sub-cent assets don't collapse {price} to "0".
                    string speech = comp.SignalSpeechTemplate
                        .Replace("{price}", SpeechPriceFormatter.FormatPrice(val))
                        .Replace("{name}", comp.DisplayName);
                    if (string.IsNullOrWhiteSpace(speech)) continue;

                    int tier = SignalTierClassifier.GetTier(comp, series);
                    string dn = comp.DisplayName ?? comp.Name;
                    bool positive = dn.Contains("Bull", StringComparison.OrdinalIgnoreCase) ||
                                    dn.Contains("Buy", StringComparison.OrdinalIgnoreCase) ||
                                    dn.Contains("Up", StringComparison.OrdinalIgnoreCase);

                    signals.Add((tier, positive, speech));
                }
            }

            if (signals.Count == 0) return string.Empty;

            // Sort: tier ascending, positive before negative within each tier.
            signals.Sort((a, b) =>
            {
                int tc = a.tier.CompareTo(b.tier);
                if (tc != 0) return tc;
                return b.positive.CompareTo(a.positive);
            });

            // Cap at 5 to match cluster audio tick limit.
            int maxSignals = Math.Min(signals.Count, 5);
            var parts = signals.Take(maxSignals).Select(s => s.speech);
            return "Also: " + string.Join(". ", parts) + ".";
        }

        /// <summary>
        /// Checks all active series for zone lines (IsZoneLine == true) and plays a quiet
        /// proximity tone on audio slot 2 when the current bar's price range overlaps a zone value.
        /// Resistance zone (BaseFrequency ~650 Hz) → ceiling cue.
        /// Support zone (BaseFrequency ~300 Hz) → floor cue.
        /// Fires at most one cue per type per navigation step (highest-priority zone wins).
        /// </summary>
        private void CheckAndPlayZoneProximity(WorkspaceState state, int dataIndex)
        {
            if (dataIndex < 0 || dataIndex >= state.Data.Count) return;
            var bar = state.Data[dataIndex];

            float? resistanceFreq = null;
            float? supportFreq = null;
            double resistanceVal = double.NaN;
            double supportVal = double.NaN;

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsVisible || series.IsMuted) continue;

                foreach (var comp in series.Components)
                {
                    if (!comp.IsZoneLine || !comp.IsVisible || comp.IsMuted) continue;

                    var compData = series.GetComponentData(comp.Name);
                    if (compData == null || dataIndex >= compData.Length) continue;

                    double zoneVal = compData[dataIndex];
                    if (double.IsNaN(zoneVal) || zoneVal <= 0) continue;

                    // Tolerance: zone is "near" if within ZoneProximityPct of the bar's price range.
                    double tolerance = Math.Abs(zoneVal) * ZoneProximityPct;
                    bool inRange = bar.High + tolerance >= zoneVal && bar.Low - tolerance <= zoneVal;
                    if (!inRange) continue;

                    // Classify by base frequency: higher frequency = resistance (ceiling), lower = support (floor).
                    float freq = (float)comp.BaseFrequency;
                    if (freq >= 500f)
                    {
                        // Resistance zone — keep highest freq found (most recent/active level wins).
                        if (resistanceFreq == null || freq > resistanceFreq.Value)
                        {
                            resistanceFreq = freq;
                            resistanceVal = zoneVal;
                        }
                    }
                    else
                    {
                        // Support zone — keep lowest freq found.
                        if (supportFreq == null || freq < supportFreq.Value)
                        {
                            supportFreq = freq;
                            supportVal = zoneVal;
                        }
                    }
                }
            }

            // Zone proximity is communicated via speech only (no audio ping).
            if (resistanceFreq.HasValue && !double.IsNaN(resistanceVal))
                _speechRouter.Speak($"Near resistance at {resistanceVal:F0}", interrupt: false);
            if (supportFreq.HasValue && !double.IsNaN(supportVal))
                _speechRouter.Speak($"Near support at {supportVal:F0}", interrupt: false);
        }

        /// <summary>
        /// Finds the nearest bar index with non-empty heatmap bins.
        /// Searches backwards from <paramref name="dataIndex"/> first; if nothing found (cursor is in
        /// historical area before the live session), falls back to the last non-empty bar in the list.
        /// Returns -1 only if the entire list is empty.
        /// </summary>
        private static int FindNearestHeatmapIndex(ChartSeries series, int dataIndex)
        {
            var hd = series.Data.HeatmapData;
            if (hd == null || hd.Count == 0) return -1;
            // Backwards search first (cursor is at or past some live data).
            int start = Math.Min(dataIndex, hd.Count - 1);
            for (int i = start; i >= 0; i--)
            {
                if (hd[i] != null && hd[i].Count > 0) return i;
            }
            // Cursor is in historical area — use the most recent live snapshot.
            for (int i = hd.Count - 1; i > start; i--)
            {
                if (hd[i] != null && hd[i].Count > 0) return i;
            }
            return -1;
        }

    }
}
