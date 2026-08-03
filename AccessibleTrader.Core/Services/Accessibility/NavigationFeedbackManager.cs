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
        /// <param name="extraContext">
        /// An already-composed clause to append to this bar's utterance — currently the chart
        /// formation the cursor has moved into. Passed in rather than spoken separately by the
        /// caller: see the composition note in the implementation.
        /// </param>
        void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false, string? extraContext = null);
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
        public void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false, string? extraContext = null)
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
                // Provider contextual speech, series summaries, component templates —
                // the ENTIRE utterance precedence now lives in SpeechFormatter (see the
                // strategy list in its ctor; debt item 4). The old "path 1" provider
                // block that lived here is strategy #1 there.
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

            // ── ONE UTTERANCE PER BAR ──────────────────────────────────────────
            //
            // Everything true about this bar is joined into a single phrase and spoken once.
            //
            // This used to be three Speak calls — the bar reading, then any additional marker
            // signals, then the chart-formation clause — and on a bar where more than one had
            // something to say the user heard only one of them. The reason is structural, not a
            // race: on the web head speech is delivered by writing into an ARIA live region;
            // Blazor batches an entire event handler into one render; so the region is written
            // three times but only the final value ever reaches the DOM for the screen reader to
            // announce. The earlier phrases were never dropped by a mute or a filter — they were
            // overwritten before anything could read them.
            //
            // Composing first and speaking once is the only arrangement that is correct on every
            // head, and it also removes the interrupt ordering problem: a single utterance cannot
            // cut itself off half way through.
            // ── EVENTS FIRST, THE ROUTINE VALUE LAST ────────────────────────────
            //
            // Ordering is not cosmetic in an audio interface. Scanning a chart with the arrow keys
            // means most bars say the same unremarkable thing, and the listener's attention is
            // already moving on before the phrase ends. Anything genuinely notable about a bar —
            // a chart formation starting, a support zone, a break of structure — has to arrive in
            // the first syllables or it is heard after the decision to move on has been made.
            //
            // So the order is: what is SPECIAL about this bar, then what this bar IS. On an ordinary
            // bar nothing precedes the value and the reading is exactly as before; on a notable one
            // the notable part leads. That also means a fast scan can be driven entirely off the
            // opening words, which is what makes scanning by ear viable at all.
            var utterance = new List<string>();

            // 1. Chart formations (composed by the caller — see AccessibilityFeedbackCoordinator).
            if (!string.IsNullOrWhiteSpace(extraContext)) utterance.Add(extraContext.Trim());

            // 2. Marker signals from OTHER series on this same bar, in the tier order used by the
            //    cluster audio tick system (Phase F).
            //
            //    No longer gated on being focused on the candle series. The old rule assumed that
            //    once you were inside an indicator you only wanted that indicator's output, but the
            //    things this reports — support zones, structure breaks, divergences — are context a
            //    trader wants wherever they are standing, and suppressing them made the rest of the
            //    chart silent the moment focus moved off price. Per-series opt-out is
            //    SeriesConfig.AnnounceAcrossSeries, for indicators whose signals only mean something
            //    inside their own pane.
            //
            //    Still Component context only (Series context already summarises every component),
            //    still X moves only (no repetition while moving up and down), and still suppressed
            //    on a jump, where the user is repositioning rather than reading.
            if (!isJump && isXMove && !isHeatmap && !isProfile
                && state.LastInteractionContext == InteractionContext.Component)
            {
                int focusedComp = Math.Clamp(state.FocusedComponentIndex, 0, s.Components.Count - 1);
                string additionalSignals = GetAdditionalSignalSpeech(state, state.CurrentDataIndex, s.Id, focusedComp);
                if (!string.IsNullOrWhiteSpace(additionalSignals)) utterance.Add(additionalSignals.Trim());
            }

            // 3. The bar itself.
            if (!string.IsNullOrWhiteSpace(finalSpeech)) utterance.Add(finalSpeech.Trim());

            if (utterance.Count > 0)
                _speechRouter.Speak(string.Join(" ", utterance), interrupt: isUserInitiated);

            // Zone proximity earcon: fires only when navigating the candle/price series.
            // When the user has focus inside a different indicator (Cipher A/B, SR, etc.),
            // zone speech is noise — only that indicator's own content should speak.
            if (!isJump && isXMove && focusedOnCandleSeries)
                CheckAndPlayZoneProximity(state, state.CurrentDataIndex);

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
        // GetComponentTypeLabel moved to SpeechFormatter.ProviderSpeechStrategy
        // (debt item 4) — the label is part of the utterance, so it lives with it.


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
        /// Returns a "{signal1}. {signal2}." speech string summarising active marker signals
        /// on the current bar from series other than the primary focused one (or from non-focused
        /// components of the focused series in Series-scope context).
        ///
        /// <para>
        /// There is no "Also:" lead-in. It was a filler word on a phrase the user hears on most
        /// bars, and by the time it has been spoken often enough to be recognised it is carrying no
        /// information — the signals themselves already read as a list. Words that cost time and
        /// say nothing are the thing an audio-first interface can least afford.
        /// </para>
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

                // Per-series opt-out, for indicators whose signals only mean something inside their
                // own context. It applies ONLY when this is not the series being navigated — an
                // indicator always speaks its own signals when you are reading it.
                if (series.Id != excludeSeriesId && !series.AnnounceAcrossSeries) continue;

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
            return string.Join(". ", parts) + ".";
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
