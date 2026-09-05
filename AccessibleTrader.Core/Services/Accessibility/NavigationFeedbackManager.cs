using System.Globalization;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Audio;

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
        // Two dependencies, and that is the whole point of the class: it turns a
        // WorkspaceState into a sentence and hands it to the router. It used to also take an
        // IEventBus, an INavigationSonifier and an IIndicatorEngine, none of which it had read
        // since sonification moved out to SonificationManager (see the note on
        // HandleNavigationFeedback) — a constructor that claimed a reach this class no longer has.
        private readonly ISpeechFeedbackRouter _speechRouter;
        private readonly ISpeechFormatter _formatter;

        // Proximity tolerance: zone is "active" if the bar's H/L range comes within 0.5% of the zone value.
        private const double ZoneProximityPct = 0.005;

        private string _lastSpokenSeriesId = "";
        private WorkspaceState? _previousState;

        public bool IsSpeechEnabled { get; set; } = true;

        public NavigationFeedbackManager(
            ISpeechFeedbackRouter speechRouter,
            ISpeechFormatter formatter)
        {
            _speechRouter = speechRouter;
            _formatter = formatter;
        }

        /// <summary>
        /// SPEECH ONLY. Sonification is <see cref="SonificationManager"/>'s job, off its own
        /// StateStream subscription — see the note at the top of the method body for the race
        /// that removing the duplicate call here fixed.
        ///
        /// <para>
        /// This used to document the utterance precedence as "3 paths evaluated in order" and
        /// listed them here. That chain no longer lives in this class: it is the strategy list in
        /// <c>SpeechFormatter</c>'s constructor, and the old "path 1" provider block that sat here
        /// is strategy #2 there. Read it there, not here — this note exists only so the next
        /// person to go looking does not conclude the precedence was deleted.
        /// </para>
        ///
        /// <para>
        /// The declarative templates are still real and still take the same tokens.
        /// <c>SpeechTemplate</c> (continuous oscillator/line components):
        /// <c>{name} {type} {value} {value:F1} {value:F2} {zone} {gradient_speech}</c>.
        /// <c>SignalSpeechTemplate</c> (marker signals present on a bar): <c>{price} {name}</c>.
        /// </para>
        /// </summary>
        public void HandleNavigationFeedback(WorkspaceState state, bool isXMove, bool isYMove, string prefixMessage, bool isUserInitiated = true, bool isJump = false, string? extraContext = null)
        {
            if (state.Data == null || !state.Data.Any() || state.CurrentDataIndex < 0 || state.CurrentDataIndex >= state.Data.Count)
            {
                // SAY SOMETHING. This was a bare return, so every arrow key before data loaded
                // — or with the cursor out of range — was total silence, and a user could not
                // tell an empty chart from a dead keyboard. Only when the user actually pressed
                // a key: a state change that arrives on its own has nothing to answer.
                // Critical channel: this is a refusal, and a refusal the user cannot hear is
                // the silent-failure class the feedback contract forbids.
                if (isUserInitiated)
                    _speechRouter.Speak("No chart data yet.", interrupt: true, SpeechChannel.Critical);
                _previousState = state;
                return;
            }

            // The bar AS DRAWN, so spoken OHLC values match the candle on screen. Shared with
            // the detail key and the sonifier rather than transformed here — see ChartMath.
            // SpeechFormatter re-reads the RAW bar for the close line, which is raw whatever
            // the candle style is; this point is the candles' answer.
            Ohlcv pt = ChartMath.BarAsDrawn(state.Data, state.CurrentDataIndex, state.IsHeikinAshi);

            var seriesId = state.FocusedSeriesId ?? "candles";
            var s = state.ActiveSeries.FirstOrDefault(x => x.Id == seriesId);
            if (s == null)
            {
                // Also a bare return: every navigation key was silent until focus was fixed,
                // with no way for the user to discover WHY. Naming the unresolved series is
                // what turns "the keyboard stopped working" into something actionable.
                if (isUserInitiated)
                    _speechRouter.Speak("No series in focus. Press Page Up or Page Down to pick one.",
                        interrupt: true, SpeechChannel.Critical);
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
                string ts = SpeechTimeFormatter.FormatTime(pt.Date);
                string ceMsg = $"{SpeechPriceFormatter.FormatPrice(pt.Close)}, {ts}";

                // When anchor 1 is already confirmed, also speak the change from that anchor.
                if (state.CoordinateEntryAnchorCount == 1 &&
                    state.CoordinateEntryAnchor1Index >= 0 &&
                    state.CoordinateEntryAnchor1Index < state.Data.Count)
                {
                    double anchor1Close = (double)state.Data[state.CoordinateEntryAnchor1Index].Close;
                    double delta = (double)pt.Close - anchor1Close;
                    string sign = delta >= 0 ? "+" : "";
                    // The delta is in quote currency, so it formats like a price. F0 said
                    // "+0" for every move a sub-dollar asset can make — the anchor exists
                    // precisely to measure that move.
                    ceMsg += $". Change from anchor: {sign}{SpeechPriceFormatter.FormatPrice(delta)}";
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

            // ── THE TRAILING ORIENTATION CLAUSES ──────────────────────────────
            //
            // Two facts that are ABOUT the move rather than about the bar: which pane the cursor
            // is now in, and whether this series is switched off. Both are spoken LAST, after the
            // reading.
            //
            // They used to be prefixes, and the reason they moved is listening cost measured
            // against listening ORDER. The first syllables of an utterance are the ones a user
            // scanning with the arrow keys actually hears before deciding to move on — they
            // belong to what is SPECIAL about this bar, then to what the bar IS. "Hidden" and
            // "Main pane" are neither: they do not change from bar to bar, and hearing them
            // ahead of the value pushes the value late in every single utterance. At the end
            // they cost nothing on the bars where the user has already moved on, and are still
            // there for the one where they wanted them.
            //
            // Both are ON CHANGE ONLY, which is what keeps a trailing clause from becoming the
            // ten-times-repeated tail the prepend rule was originally written to prevent.
            //
            // ── EXCEPT HIDDEN AND MUTED, WHICH LEAD ─────────────────────────────────────
            //
            // Cody, 2026-09-05: "put muted and hidden at the beginning, narrating at the end."
            // That splits what used to be one if/else chain — Hidden / Muted / Narrating, three
            // mutually exclusive words in a trailing clause — into the two facts they actually
            // are, and lines the series readout up with the component one, which has said them
            // that way round since 2026-09-04 (see SpeechFormatter and VisibilityStateSpeech):
            //
            //   • Hidden and muted explain a SILENCE. There is no sound coming from this series,
            //     and whatever interrupts an utterance cuts its END — so the half a user must
            //     not lose goes first.
            //   • Narrating explains nothing of the kind. It is an addition, everything else
            //     about the series is normal, and it is the least urgent thing in the sentence.
            //
            // They are also independent, which the if/else chain denied: a series can be hidden
            // AND muted (VisibilityStateSpeech.Qualifier says both), and a series that is hidden
            // can still carry the narration flag it was given before it was hidden — that is
            // worth hearing, because it is the answer to "why has this stopped talking".
            string visibilityPrefix = "";
            string stateClause = "";
            string orientationClause = "";

            if (seriesIdChanged)
            {
                // Count ALL components regardless of visibility — hidden data companions
                // (like Cipher S z-score / deviation buffers) are still part of the indicator.
                int count     = s.Components.Count(c => !c.IsMuted);
                string compWord = count == 1 ? "component" : "components";

                // Callers navigating to a hidden or muted series must hear that status so they
                // know why there is no sound. Both flags, in one clause, in front.
                visibilityPrefix = VisibilityStateSpeech.Prefix(s.IsVisible, s.IsMuted);
                stateClause = s.IsAutoNarrated ? "Narrating." : "";

                // A text label announces nothing on the switch itself. Its NAME is its wording,
                // and the component reading that follows in the same utterance is that wording
                // again — "Label: sold half here. 1 component. Label. Sold half here." was the
                // whole line. The count is noise too: a label has exactly one component and it
                // is the label. Only the state suffix survives, because a hidden or muted label
                // still has to explain its own silence.
                if (s.Drawing is { Type: DrawingType.TextLabel })
                {
                    // The label's NAME is its wording and the reading that follows is that
                    // wording again, so only its state survives — and "Text label." goes in
                    // front of whichever half of the state is actually being said, so the word
                    // introduces something rather than trailing off on its own.
                    if (visibilityPrefix.Length > 0) visibilityPrefix = "Text label. " + visibilityPrefix;
                    else if (stateClause.Length > 0) stateClause = "Text label. " + stateClause;
                }
                else if (s.IsDrawing)
                {
                    // A drawing is named the way the nudge readback names it — "Trend line 2",
                    // never "Trend line (2)" — so one object has one spoken name whichever key
                    // produced it. "1 component" is dropped: the count is only information when
                    // it tells the user Ctrl+Up/Down has somewhere to go. And a fourteen-component
                    // Gann box names the component it is about to READ, because "14 components"
                    // followed by a value from an unnamed one is a count of inaccessible objects.
                    // The per-bar readback that follows carries the position clause, so "where
                    // is my line" is answered by the sentence, not by a prefix.
                    string spokenName = DrawingSpeech.SpokenSeriesName(s);
                    int n = s.Components.Count;
                    if (n <= 1)
                    {
                        speechPrefix = $"{spokenName}. " + speechPrefix;
                    }
                    else
                    {
                        var reading = s.Components[s.ClampComponent(state.FocusedComponentIndex)];
                        // A hidden component's own sentence names it ("61.8% Level: hidden"), so
                        // naming it here as well would be the same object twice, two ways.
                        string compName = reading.IsVisible ? DrawingSpeech.SpokenComponentName(s.Components, reading) : "";
                        speechPrefix = compName.Length > 0
                            ? $"{spokenName}. {n} components, reading {compName}. " + speechPrefix
                            : $"{spokenName}. {n} components. " + speechPrefix;
                    }
                }
                else
                {
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
                        // The sub-pane count used to be spoken here as "N panes", and it was
                        // wrong twice over. It counted the STRIPS inside one series while calling
                        // them panes, so a chart with a volume pane and an oscillator pane under
                        // it said "1 pane"; and a count is only information when it tells the
                        // user a key has somewhere to go, which stopped being true when sub-pane
                        // navigation was retired. The pane the cursor is in is named in the
                        // trailing clause below, where it is a fact about the chart rather than a
                        // number about this series.
                        countMsg = $"{count} {compWord}";
                    }
                    // Use Name (base indicator name without parameter values) for speech.
                    // FriendlyName includes baked-in parameter values (e.g. "RSI 14") which are
                    // noise in navigation context — the user knows what they added.
                    speechPrefix = $"{s.Name}. {countMsg}. " + speechPrefix;
                }

                // In FRONT of everything the branches above built — the series name, the
                // component count, the drawing's name. All of them are things this series IS;
                // "hidden and muted" is the reason none of it will make a sound.
                speechPrefix = visibilityPrefix + speechPrefix;
            }

            // 2b. WHERE THE CURSOR NOW IS — the pane, and the strip inside it.
            //
            // Computed against the previous state and spoken ONLY when it changed. A pane is a Y
            // axis, so crossing into one means the numbers that follow are being read against a
            // different scale; that is worth a clause. Ten Page Downs inside the Main pane are
            // not, which is why nothing is said when nothing moved.
            //
            // Both halves are resolved through ChartPaneModel, so a strip is named from the
            // components of every series in the pane rather than of the focused one alone — the
            // renderer draws the strip that way, and the two disagreeing is the defect that made
            // Ctrl+Up/Down unable to reach Price from the candles.
            {
                string? prevPaneKey = null, prevStrip = null;
                if (_previousState != null)
                {
                    var prevSeries = _previousState.ActiveSeries
                        .FirstOrDefault(x => x.Id == (_previousState.FocusedSeriesId ?? seriesId));
                    if (prevSeries != null)
                    {
                        prevPaneKey = ChartPaneModel.KeyOf(prevSeries);
                        int pIdx = prevSeries.ClampComponent(_previousState.FocusedComponentIndex);
                        if (pIdx >= 0 && pIdx < prevSeries.Components.Count)
                            prevStrip = prevSeries.Components[pIdx].SubPaneName;
                    }
                }

                string currPaneKey = ChartPaneModel.KeyOf(s);
                int currCompIdx = s.ClampComponent(state.FocusedComponentIndex);
                string? currStrip = currCompIdx >= 0 && currCompIdx < s.Components.Count
                    ? s.Components[currCompIdx].SubPaneName
                    : null;

                bool paneChanged = !string.Equals(prevPaneKey, currPaneKey, StringComparison.Ordinal);
                bool stripChanged = !string.Equals(prevStrip, currStrip, StringComparison.OrdinalIgnoreCase);

                if (paneChanged || stripChanged)
                {
                    var pane = ChartPaneModel.Panes(state.ActiveSeries)
                        .FirstOrDefault(x => string.Equals(x.Key, currPaneKey, StringComparison.Ordinal));
                    var paneSeries = pane?.Series ?? new List<ChartSeries> { s };

                    string paneLabel = ChartPaneModel.DisplayName(currPaneKey, paneSeries) + " pane";
                    string? stripLabel = ChartPaneModel.SubPaneDisplayName(currStrip, paneSeries);

                    orientationClause = stripLabel == null
                        ? paneLabel + "."
                        : $"{paneLabel}, {stripLabel} strip.";
                }

                // A drawing's per-bar sentence carries no component name (it is constant across a
                // sweep), so the name is spoken where it CHANGES: on Ctrl+Up/Down between a fib's
                // levels or a rectangle's top and bottom. This one still LEADS — it names the
                // thing about to be read, not where it is.
                if (isYMove && !seriesIdChanged && _previousState != null
                    && s.IsDrawing && s.Components.Count > 1
                    && currCompIdx >= 0 && currCompIdx < s.Components.Count
                    && s.Components[currCompIdx].IsVisible
                    && s.ClampComponent(_previousState.FocusedComponentIndex) != currCompIdx)
                {
                    string compName = DrawingSpeech.SpokenComponentName(s.Components, s.Components[currCompIdx]);
                    if (compName.Length > 0) speechPrefix = compName + ". " + speechPrefix;
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
                    finalSpeech = _formatter.FormatHeatmapFeedback(state, isXMove, isYMove, s, heatmapIdx, state.FocusedBinIndex, speechPrefix, state.CurrentDataIndex);
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
                // block that lived here is strategy #2 there.
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

            // 2. Zone proximity — price has ARRIVED at a support or resistance level.
            //
            //    This used to be two Speak calls of its own, made AFTER the composed utterance
            //    below had already been spoken. On the web head that is the same overwrite this
            //    whole composition exists to prevent, running in the other direction: the phrase
            //    carrying the formation, the cross-series signals and the bar's own reading was
            //    written into the live region and then replaced by "Near resistance at …" before a
            //    screen reader could announce any of it. Measured on a bar carrying all four:
            //    three Speak calls, of which the user heard the last.
            //
            //    And the bar most likely to hit it is the bar a trader is navigating TOWARDS.
            //    Everything the terminal had worked out about the moment that mattered was the
            //    part that got discarded.
            //
            //    Placed ahead of the marker signals because a zone is about where price is right
            //    now, which is more immediate than an indicator's commentary on it. Zone lines are
            //    excluded from the cluster audio ticks precisely so that speech can carry them;
            //    this is that speech.
            if (!isJump && isXMove && focusedOnCandleSeries)
                utterance.AddRange(DescribeZoneProximity(state, state.CurrentDataIndex));

            // 2b. Text labels pinned to this bar.
            //
            //     A label is a note the user wrote to themselves about a BAR, so it has to be
            //     readable from wherever they are standing when they reach that bar — the same
            //     rule as the marker signals below, and for a stronger reason: a label is the
            //     only thing on the chart that the terminal did not compute and cannot restate.
            //     Its own series is excluded because the component strategy already reads it
            //     when that series is what is focused; without the exclusion the wording would
            //     be spoken twice in one utterance.
            //
            //     Jumps are NOT suppressed here, unlike the zone and marker clauses. Ctrl+Left /
            //     Ctrl+Right is how a label gets found again after it is off screen, and a jump
            //     that lands on a label and says nothing about it is the one case that matters.
            if (isXMove)
                utterance.AddRange(DescribeTextLabels(state, state.CurrentDataIndex, seriesId));

            // 3. Marker signals from OTHER series on this same bar, in the tier order used by the
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
                int focusedComp = s.ClampComponent(state.FocusedComponentIndex);
                if (focusedComp >= 0)
                {
                    string additionalSignals = GetAdditionalSignalSpeech(state, state.CurrentDataIndex, s.Id, focusedComp);
                    if (!string.IsNullOrWhiteSpace(additionalSignals)) utterance.Add(additionalSignals.Trim());
                }
            }

            // 4. The bar itself.
            if (!string.IsNullOrWhiteSpace(finalSpeech)) utterance.Add(finalSpeech.Trim());

            // 5. WHERE, and WHETHER IT IS ON — trailing, on change only.
            //
            //    Last because they are the least urgent things in the utterance and the least
            //    likely to have changed: orientation is what you want when the reading has
            //    already told you something surprising, and by then you are listening to the end
            //    of the phrase rather than skipping off the front of it.
            if (!string.IsNullOrWhiteSpace(orientationClause)) utterance.Add(orientationClause.Trim());
            if (!string.IsNullOrWhiteSpace(stateClause)) utterance.Add(stateClause.Trim());

            if (utterance.Count > 0)
                _speechRouter.Speak(string.Join(" ", utterance), interrupt: isUserInitiated);

            _previousState = state;
        }

        // GetComponentTypeLabel moved to SpeechFormatter.ProviderSpeechStrategy (debt item 4) —
        // the label is part of the utterance, so it lives with it. Its /// summary was left
        // behind here, documenting a method this file no longer has and attaching to whatever
        // came next.


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
        /// Checks all active series for zone lines (<c>IsZoneLine == true</c>) and SPEAKS when the
        /// current bar's price range overlaps a zone value — nearest support below, nearest
        /// resistance above, at most one of each per navigation step.
        ///
        /// <para>
        /// It plays no tone. This block used to describe "a quiet proximity tone on audio slot 2"
        /// with frequencies for the ceiling and floor cues, which the body has explicitly
        /// disclaimed ever since ("Zone proximity is communicated via speech only") — and the
        /// method is still called <c>CheckAndPlayZoneProximity</c>, which is the same leftover.
        /// Prices go through <c>SpeechPriceFormatter</c>, never a fixed precision: these two lines
        /// once used <c>:F0</c> and read every sub-dollar asset's level as "0".
        /// </para>
        /// </summary>
        /// <summary>
        /// The wording of every visible text label pinned to <paramref name="dataIndex"/>,
        /// excluding <paramref name="excludeSeriesId"/> (the focused series reads its own label
        /// through <c>TextLabelStrategy</c>, and saying it twice in one utterance is worse than
        /// not saying it).
        ///
        /// <para>
        /// Muted labels are skipped, matching every other cross-series clause: muting a drawing
        /// is how a user silences an annotation they have finished with.
        /// </para>
        /// </summary>
        private static List<string> DescribeTextLabels(WorkspaceState state, int dataIndex, string excludeSeriesId)
        {
            var clauses = new List<string>(1);
            if (dataIndex < 0 || dataIndex >= state.Data.Count) return clauses;

            foreach (var series in state.ActiveSeries)
            {
                if (series.Drawing is not { Type: DrawingType.TextLabel }) continue;
                if (series.Id == excludeSeriesId) continue;
                if (!series.IsVisible || series.IsMuted) continue;

                var data = series.GetComponentData("Label");
                if (data == null || dataIndex >= data.Length || double.IsNaN(data[dataIndex])) continue;

                clauses.Add(TextLabelStrategy.Describe(series));
            }

            return clauses;
        }

        /// <summary>
        /// The zone clauses for this bar, in the order they should be heard — nearest ceiling
        /// first, then nearest floor. Returns them rather than speaking them: everything true
        /// about a bar goes into one utterance, and this used to be the exception that quietly
        /// overwrote the rest of it.
        /// </summary>
        private static List<string> DescribeZoneProximity(WorkspaceState state, int dataIndex)
        {
            var clauses = new List<string>(2);
            if (dataIndex < 0 || dataIndex >= state.Data.Count) return clauses;
            var bar = state.Data[dataIndex];

            // Distance from price to the nearest level on each side. Nearest wins, because
            // the nearest level is the one price is about to interact with.
            double resistanceDist = double.PositiveInfinity;
            double supportDist = double.PositiveInfinity;
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

                    // Classify by WHERE THE LEVEL IS, not by how it sounds.
                    //
                    // This used to be `if ((float)comp.BaseFrequency >= 500f)` → resistance,
                    // else support. BaseFrequency is a SONIFICATION setting: a zone line whose
                    // tone was chosen for audibility rather than semantics was announced as
                    // the OPPOSITE STRUCTURAL LEVEL. "Near resistance at X" versus "near
                    // support at X" is a directional claim a trader acts on, and the magic 500
                    // was undocumented.
                    //
                    // The rule now lives in ONE place — LevelPolarity — because the same
                    // invariant was also got wrong by spelling over in AutoNarrationService,
                    // and each site was fixed alone. Frequency stays purely for the tone.
                    double distance = Math.Abs(zoneVal - bar.Close);
                    if (LevelPolarity.IsResistance(zoneVal, bar.Close))
                    {
                        if (distance < resistanceDist)
                        {
                            resistanceDist = distance;
                            resistanceVal = zoneVal;
                        }
                    }
                    else
                    {
                        if (distance < supportDist)
                        {
                            supportDist = distance;
                            supportVal = zoneVal;
                        }
                    }
                }
            }

            // Zone proximity is communicated via speech only (no audio ping).
            //
            // Through SpeechPriceFormatter, not a fixed precision. These two lines used ":F0" and so
            // read every sub-dollar asset's level as "0" — Kaspa near $0.083 announced "Near support
            // at 0", which is not a wrong price so much as no price at all. Any spoken price goes
            // through the formatter; it is the only thing that knows an instrument's magnitude.
            if (!double.IsNaN(resistanceVal))
                clauses.Add($"Near resistance at {SpeechPriceFormatter.FormatPrice(resistanceVal)}.");
            if (!double.IsNaN(supportVal))
                clauses.Add($"Near support at {SpeechPriceFormatter.FormatPrice(supportVal)}.");

            return clauses;
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
