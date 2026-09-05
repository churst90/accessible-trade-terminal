using System.Globalization;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Analysis;
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
    ///
    /// ONE SCAN, ONE UTTERANCE: everything a single scan finds — across every narrated series —
    /// is composed into one phrase and spoken once, most consequential first, capped. See
    /// <see cref="ScanUtterance"/> for why, for the order, and for what a cap can safely drop.
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

        /// <summary>
        /// The bar close at the last bar on which each zone line still had a value.
        /// Key = "{seriesId}:{componentName}". Value = that bar's close.
        ///
        /// <para>
        /// It exists solely so a BREAK can be announced with the polarity the level actually had.
        /// A break is the moment price crossed the level, so the current close is on the wrong
        /// side of it by definition: judging a break against the current close would rename every
        /// broken resistance "support" and every broken support "resistance" — the exact
        /// inversion this narrator was fixed for on 2026-08-27, reintroduced from the other end.
        /// </para>
        /// </summary>
        private readonly Dictionary<string, double> _lastZoneClose = new();

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

        /// <summary>
        /// Which side of a plain price-space OVERLAY line (an EMA, a VWAP, a MA Cloud band) the
        /// close was on at the last bar close. Separate from <see cref="_lastPriceAboveZone"/>
        /// because that one belongs to declared zone lines, which also carry break, touch and
        /// approach vocabulary; an overlay gets crosses and nothing else.
        /// Key = "{seriesId}:{componentName}".
        /// </summary>
        private readonly Dictionary<string, bool> _lastPriceAboveOverlay = new();

        /// <summary>
        /// The new-bar sentence the coordinator handed over, waiting to be spoken as the first
        /// clause of this bar's narration. Null when there is none. See
        /// <see cref="DeferBarCloseSentence"/>.
        /// </summary>
        private string? _pendingBarCloseSentence;

        /// <summary>
        /// Which side of one of the series' declared reference levels its reading was on at the
        /// last bar close. Key = "{seriesId}:{componentName}:{levelName}".
        /// </summary>
        private readonly Dictionary<string, bool> _lastAboveLevel = new();

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

        // ── One scan, one utterance ──────────────────────────────────────────────

        /// <summary>
        /// Collects everything a single scan found and composes it into ONE phrase.
        ///
        /// <para>
        /// <b>Why.</b> This narrator used to make up to nine separate <c>Speak</c> calls inside a
        /// single <c>RedrawEvent</c> handler — a marker signal, a broken level, a level tested
        /// again, an approach, a cross, a cloud entry or exit, an oscillator zone change and an
        /// oscillator crossover. On the web head speech is delivered by assigning
        /// <c>MainLayout</c>'s <c>_latestSpeech</c> field, and Blazor batches an entire handler
        /// into one render, so the field was assigned nine times and only the last value ever
        /// reached the DOM. The other eight were never muted or filtered — they were overwritten
        /// before a screen reader could read any of them, and the one that survived was whichever
        /// happened to be last, not whichever mattered. On the desktop head the failure inverts:
        /// all nine queue and the listener cannot get out from under them.
        /// </para>
        ///
        /// <para>
        /// This is the same defect <c>NavigationFeedbackManager</c> was fixed for — its
        /// "ONE UTTERANCE PER BAR" comment describes it exactly — and this narrator was not fixed
        /// with it. Composing first and speaking once is the only arrangement correct on every
        /// head, and a single utterance cannot cut itself off half way through.
        /// </para>
        ///
        /// <para>
        /// <b>The order.</b> Ordering is not cosmetic in an audio interface. The narrator runs in
        /// the background while the user is doing something else, so whatever is most consequential
        /// has to arrive in the opening syllables or it is heard after attention has moved on. So:
        /// a level that has ceased to exist, then price changing side of one, then the indicator's
        /// own discrete signal, then a level tested again, then an approach to something that has
        /// not happened yet, and last the oscillator commentary — which is the most frequent thing
        /// this narrator says and therefore the least worth leading with.
        /// </para>
        ///
        /// <para>
        /// <b>The series name.</b> Every clause is built already carrying "<c>{series}: </c>",
        /// which read correctly when each was its own utterance and reads as a stutter once they
        /// are joined. The prefix is dropped from a clause whose series is the same as the
        /// previous clause's, so a name is spoken once per run of clauses about that series and
        /// again whenever the utterance moves to another one. Clauses from a user-authored
        /// <c>SignalSpeechTemplate</c> simply do not match the prefix and are left alone.
        /// </para>
        /// </summary>
        private sealed class ScanUtterance
        {
            /// <summary>A level ceased to exist. The most consequential thing this narrator says.</summary>
            public const int TierBreak = 1;
            /// <summary>
            /// The indicator printed one of its own discrete signals — an entry trigger, a
            /// divergence, a break of structure. Ahead of a cross because this is the call the
            /// indicator was added to the chart to make, while any plotted line gets crossed
            /// routinely.
            /// </summary>
            public const int TierSignal = 2;
            /// <summary>Price changed side of a level or a cloud.</summary>
            public const int TierCross = 3;
            /// <summary>A level was tested again and held.</summary>
            public const int TierTouch = 4;
            /// <summary>Price came within the proximity band of a level. Has not happened yet.</summary>
            public const int TierApproach = 5;
            /// <summary>Oscillator zone changes and crossovers — the most repetitive commentary.</summary>
            public const int TierOscillator = 6;

            /// <summary>
            /// Ceiling on clauses in one utterance, matching the cap
            /// <c>NavigationFeedbackManager.GetAdditionalSignalSpeech</c> puts on the same kind of
            /// list. The clause count is not bounded by anything else — the scan walks a 20-bar
            /// window across every component of every narrated series — and an utterance that runs
            /// for twenty seconds is not a text equivalent of anything, it is an obstruction: the
            /// speech router protects an in-flight utterance from a lower-priority interrupt
            /// (<c>SpeechFeedbackRouter.MayInterrupt</c>), so an arrow key pressed underneath one
            /// is queued behind the rest of it.
            ///
            /// <para>
            /// Dropping is safe here only BECAUSE the tiers above exist: what goes is always the
            /// least consequential thing the scan found, deterministically, rather than whatever
            /// the live region happened to overwrite last. That is the whole difference between
            /// this and the defect being fixed.
            /// </para>
            /// </summary>
            private const int MaxClauses = 5;

            private readonly List<(int Tier, int Order, string Series, string Key, string Text, string? Component)> _clauses = new();
            private readonly HashSet<string> _approachSuppressed = new();
            private int _next;

            /// <param name="key">
            /// "{seriesId}:{componentName}" — the key convention used by this service's tracking
            /// dictionaries. Identifies which component a clause is about, so two clauses about
            /// the same one can be reconciled.
            /// </param>
            /// <param name="componentName">
            /// The spoken name of the component a SIGNAL clause is about. Given only for clauses
            /// built from a component's own template, which is the kind that may name nothing;
            /// see <see cref="Compose"/> for what it is used for.
            /// </param>
            public void Add(int tier, string seriesName, string key, string? text, string? componentName = null)
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                _clauses.Add((tier, _next++, seriesName, key, text.Trim(), componentName));
            }

            /// <summary>
            /// Price has just crossed this component, so anything said about approaching it is
            /// stale. Separate utterances could get away with the pair — they were minutes apart
            /// in the ordinary case and, on the web head, seven of eight never arrived at all.
            /// In one breath "Price crossed above R1 at 103.50. Approaching support at 103.50."
            /// is a contradiction: you are not approaching a level you are already past.
            /// </summary>
            public void SuppressApproachFor(string key) => _approachSuppressed.Add(key);

            /// <summary>
            /// Tier first, then the order the scan found them in — a stable sort, so within one
            /// tier the series and components keep the order they were scanned in.
            /// </summary>
            public string Compose()
            {
                var kept = _clauses
                    .Where(c => c.Tier != TierApproach || !_approachSuppressed.Contains(c.Key))
                    .OrderBy(c => c.Tier).ThenBy(c => c.Order)
                    .Take(MaxClauses)
                    .ToList();

                var parts = new List<string>(kept.Count);
                string? prevSeries = null;

                foreach (var clause in kept)
                {
                    string text = clause.Text;
                    string prefix = clause.Series + ": ";
                    bool carriesName = text.StartsWith(prefix, StringComparison.Ordinal);

                    if (carriesName)
                    {
                        // A level, cross or zone clause is built "{series}: …" because the
                        // series IS the thing it is about ("EMA 50: price crossed above"). The
                        // name is spoken once per run of clauses about that series.
                        if (prevSeries == clause.Series) text = text[prefix.Length..];
                    }
                    else
                    {
                        // ── A SIGNAL IS INTRODUCED BY ITS COMPONENT, NEVER BY ITS SERIES ─────
                        //
                        // Until 2026-09-05 a template clause joined behind another series' clause
                        // was prefixed with the SERIES name, so it would not be heard as the
                        // other series' signal. Cody: "hearing only the component name before the
                        // signal is all that is needed, not the series name, as the user probably
                        // knows what they enabled for narration". He is right about what the
                        // listener already knows — narration is opt-in per series, the person
                        // chose the two or three series that speak — and the component name is
                        // the fact they do NOT have: WHICH of Cipher B's eleven markers fired.
                        //
                        // The component leads only when the template has not already said it.
                        // Most of the shipped templates are the component's own name in a
                        // sentence ("Bullish divergence", "Triple confluence buy, strong
                        // confirmation"), and "Bullish Divergence: Bullish divergence" is the
                        // stutter this replaces.
                        text = SignalClauseSpeech.WithComponentName(text, clause.Component);
                    }

                    // 47 of those 61 templates also end without a full stop, which read fine as
                    // whole utterances and run into the next clause once they are joined.
                    if (!".!?".Contains(text[^1])) text += ".";

                    parts.Add(text);
                    prevSeries = clause.Series;
                }

                return string.Join(" ", parts);
            }
        }

        // ── StateStream: detect narration enable/disable ─────────────────────────

        private void OnStateChanged(WorkspaceState state)
        {
            var currentIds = new HashSet<string>(
                state.ActiveSeries.Where(s => s.IsAutoNarrated).Select(s => s.Id));

            // Newly enabled — record seed bar count so no historical signals fire
            foreach (var id in currentIds.Except(_prevNarratedIds))
            {
                // ── THE LIVE BAR IS NOT HISTORICAL ──────────────────────────────────────
                //
                // This was `barCount`, and the effect was that THE FIRST BAR TO CLOSE AFTER
                // YOU SWITCH NARRATION ON COULD NEVER SPEAK — the one bar the user is
                // listening for when they press N. The scan requires
                // `max(seedCount, …) <= closedBound`, and the first bar to close is index
                // `barCount - 1`, one below a seed of `barCount`.
                //
                // The last bar is the FORMING one. Everything strictly before it has closed
                // already and is history nobody asked to have replayed; the forming bar has
                // not happened yet, and what it prints when it closes is exactly the news the
                // flag was set to hear. Measured, not reasoned: the diagnostic in
                // NewBarNarrationCompositionTests spoke only the candle on the first close and
                // the signal on the second.
                int barCount = state.Data?.Count ?? 0;
                _seedBarCounts[id] = Math.Max(0, barCount - 1);

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
                foreach (var k in _lastZoneClose.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastZoneClose.Remove(k);
                foreach (var k in _lastTouchCount.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastTouchCount.Remove(k);
                foreach (var k in _inProximity.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _inProximity.Remove(k);
                foreach (var k in _lastPriceAboveZone.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastPriceAboveZone.Remove(k);
                foreach (var k in _lastPriceAboveOverlay.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastPriceAboveOverlay.Remove(k);
                foreach (var k in _lastAboveLevel.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastAboveLevel.Remove(k);
                foreach (var k in _lastCloudPosition.Keys.Where(k => k.StartsWith(id + ":")).ToList())
                    _lastCloudPosition.Remove(k);
            }

            _prevNarratedIds = currentIds;
        }

        // ── RedrawEvent: scan for new signals ────────────────────────────────────

        /// <inheritdoc />
        public bool WillNarrateBarClose()
        {
            var state = _store.State;
            return state.Data is { Count: > 0 }
                && state.DataStatus != DataStatus.LoadingHistorical
                && state.InitStatus == InitializationStatus.Ready
                && state.IsSpeechEnabled
                && state.NarrateSignalsOnBarClose
                && _prevNarratedIds.Any();
        }

        /// <inheritdoc />
        public void DeferBarCloseSentence(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence)) return;
            // Replacing rather than appending: if two bars closed without a scan in between,
            // the older sentence describes a bar that is no longer the one that just closed,
            // and speaking it late is worse than not speaking it.
            _pendingBarCloseSentence = sentence.Trim();
        }

        /// <summary>
        /// ONE utterance per bar close: the new-bar sentence the coordinator deferred, then
        /// whatever this scan found, spoken together or not at all.
        ///
        /// <para>
        /// The pending sentence is taken BEFORE the scan and spoken even when the scan itself
        /// bails out — a narration gate must not be able to swallow the new-bar announcement,
        /// which answers to a different switch (<c>AnnounceNewBars</c>).
        /// </para>
        /// </summary>
        private void OnIndicatorsUpdated()
        {
            string? pending = _pendingBarCloseSentence;
            _pendingBarCloseSentence = null;

            string? scanned = ScanForNarration();

            string whole = string.Join(" ", new[] { pending, scanned }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (whole.Length == 0) return;

            _speechRouter.Speak(whole, interrupt: false, channel: SpeechChannel.Event);
        }

        /// <summary>What this scan found, or null. Speaks nothing itself — see the caller.</summary>
        private string? ScanForNarration()
        {
            var state = _store.State;
            if (state.Data == null || state.Data.Count == 0) return null;
            if (state.DataStatus == DataStatus.LoadingHistorical) return null;
            if (state.InitStatus != InitializationStatus.Ready) return null;
            if (!state.IsSpeechEnabled) return null;

            // The Narration tab's master switch. It sits ABOVE the per-series flag, not instead
            // of it: Ctrl+Alt+Shift+N picks WHICH series speak, this says whether any of them
            // do. Default ON, so on a chart with nothing flagged it gates nothing — what it buys
            // is one place to silence the whole channel without un-flagging six series and
            // having to remember which six they were.
            //
            // Placed exactly where the F2 speech gate is, and it inherits that gate's one known
            // edge: the per-series seeding in OnStateChanged is a DIFFERENT subscription and
            // keeps running while this is off (so re-enabling never replays a whole session),
            // but the 20-bar PivotConfirmWindow means the first scan after re-enabling can still
            // speak a signal from up to 20 bars back. That is pre-existing behaviour for F2, it
            // is bounded, and a signal 20 bars old is arguably still worth hearing — noted here
            // rather than special-cased.
            if (!state.NarrateSignalsOnBarClose) return null;

            if (!_prevNarratedIds.Any()) return null;

            int currentCount = state.Data.Count;
            bool isNewBar = currentCount > _lastDataCount && _lastDataCount > 0;
            int scanIndex = isNewBar ? _lastDataCount - 1 : currentCount - 1;
            _lastDataCount = currentCount;

            // Only scan confirmed (closed) bars to avoid announcing unstable live-bar values.
            // On bar close: just-closed bar is scanIndex. On intra-bar tick: penultimate bar.
            int closedBound = isNewBar ? scanIndex : currentCount - 2;
            if (closedBound < 0) return null;

            // Everything this scan finds, across every narrated series, goes into ONE phrase —
            // see ScanUtterance. Nine Speak calls in one handler is eight discarded on the web
            // head and an unstoppable queue of nine on the desktop one.
            var utterance = new ScanUtterance();

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsAutoNarrated) continue;
                if (!_seedBarCounts.TryGetValue(series.Id, out int seedCount)) continue;

                // Scan window: covers recently-confirmed delayed signals (pivot indicators).
                // seedCount is the exclusive lower bound — bars before it are historical.
                int scanFrom = Math.Max(seedCount, closedBound - PivotConfirmWindow);
                if (scanFrom > closedBound) continue;

                ScanSeriesForChanges(series, state, scanFrom, closedBound, isNewBar, utterance);
            }

            string composed = utterance.Compose();
            return string.IsNullOrEmpty(composed) ? null : composed;
        }

        // ── Core scanning logic ──────────────────────────────────────────────────

        private void ScanSeriesForChanges(ChartSeries series, WorkspaceState state, int fromIndex, int toIndex, bool isBarClose, ScanUtterance utterance)
        {
            // 1. Marker signals (discrete signal dots/arrows/shapes)
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                // The per-component narration selection (N with the cursor on a component).
                // Applied at all four scan sites rather than once at the top, because "this
                // component narrates" is a fact about the component and each site reaches its
                // components differently — the oscillator path does not even iterate them.
                if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
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
                        utterance.Add(ScanUtterance.TierSignal, series.FriendlyName, markerKey, msg,
                            componentName: SignalClauseSpeech.ComponentName(comp));
                        announced.Add(barIndex);
                        if (!_lastSeenPivotIndex.TryGetValue(pivotKey, out int cur) || barIndex > cur)
                            _lastSeenPivotIndex[pivotKey] = barIndex;
                    }
                }
            }

            // 1b. SR zone line scanning — runs on every update
            ScanZoneLines(series, state, toIndex, utterance);

            // 1c. Cloud entry/exit detection — runs on every update
            ScanCloudTransitions(series, state, toIndex, utterance);

            // Everything below this line is BAR CLOSE ONLY — a confirmed candle.
            if (!isBarClose) return;

            // 1d. Price crossing a plain price-space overlay (an EMA, a VWAP).
            ScanOverlayCrosses(series, state, toIndex, utterance);

            // 1e. The indicator crossing one of its OWN declared levels (RSI 70, MACD zero).
            ScanLevelCrosses(series, toIndex, utterance);

            // 2. Oscillator zone transitions.

            var indexedState = state with { CurrentDataIndex = toIndex };
            foreach (var oscContext in _contextAnalyzer.AnalyzeAll(series, indexedState))
            {
                string oscKey = $"{series.Id}:{oscContext.ComponentName}";

                // The analyser names its component; find it back so the selection applies here
                // too. A context whose component no longer exists still updates _lastOscState
                // below — dropping the tracking as well would make the NEXT bar look like a
                // fresh transition.
                var oscComp = series.Components.FirstOrDefault(c => c.Name == oscContext.ComponentName);
                bool oscNarrates = oscComp == null || SeriesNarrationScope.ComponentNarrates(series, oscComp);

                if (oscNarrates && _lastOscState.TryGetValue(oscKey, out var prev))
                {
                    // Zone entered
                    if (prev.Zone != oscContext.Zone)
                    {
                        string? zoneMsg = BuildZoneTransitionMessage(series.FriendlyName, oscContext.ComponentName, oscContext.Zone, prev.Zone);
                        utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, oscKey, zoneMsg);
                    }

                    // Crossover appeared
                    if (oscContext.Crossover != CrossoverStatus.None && prev.Crossover == CrossoverStatus.None)
                    {
                        string? crossMsg = BuildCrossoverMessage(series.FriendlyName, oscContext);
                        utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, oscKey, crossMsg);
                    }
                }

                _lastOscState[oscKey] = (oscContext.Zone, oscContext.Crossover);
            }
        }

        // ── Price crossing a plain overlay line ──────────────────────────────────

        /// <summary>
        /// Whether <paramref name="comp"/> is a price-space overlay line — the kind of component
        /// price can be on one side of. An EMA, a VWAP, either band of a MA Cloud.
        ///
        /// <para>
        /// Everything excluded here is excluded because "price crossed it" would be a category
        /// error or a duplicate: a declared zone line already has its own scan with break, touch
        /// and approach vocabulary; a marker is a discrete event rather than a line; anything in
        /// a sub-pane or a non-Main pane is drawn against a different Y axis, so comparing it to
        /// the close compares two different units; the candles' own components ARE the price; and
        /// a drawing's components are the user's own lines, which the drawing speech contract
        /// owns.
        /// </para>
        /// </summary>
        private static bool IsPriceSpaceOverlayLine(ChartSeries series, ComponentConfig comp)
            => !series.IsDrawing
               && string.Equals(series.Pane, "Main", StringComparison.OrdinalIgnoreCase)
               && string.IsNullOrEmpty(comp.SubPaneName)
               && comp.DisplayType == ComponentDisplayType.Line
               && !comp.IsZoneLine
               && comp.Role != ComponentRole.PriceAction
               && comp.Role != ComponentRole.Body
               && comp.Role != ComponentRole.Wick;

        /// <summary>
        /// "Price crossed above EMA 9 at 64,900." on the bar close where it happened.
        ///
        /// <para>
        /// <b>Why this exists.</b> Cody, 2026-09-05: <i>"when you add things like ema's, these
        /// aren't included in playback narration even if you enable it, ema crosses should be
        /// announced though on new bar announcements"</i>. The first half was already true and is
        /// deliberate — a line has a value on every bar, and playback speaks discrete signals
        /// only. The second half was NOT true: cross detection lived in <see cref="ScanZoneLines"/>
        /// behind <c>comp.IsZoneLine</c>, which only Cipher SR's pivots and Spider Lines'
        /// fibonacci EMAs set. A user who flagged a plain EMA with N got silence from it forever —
        /// no marker to fire, no zone definition to transition, no zone-line flag to cross.
        /// </para>
        ///
        /// <para>
        /// Bar close only, and crosses only. An overlay is not support or resistance — it has no
        /// polarity to break and nothing to test — so it gets the one sentence that is true about
        /// every line on the price axis, on the confirmed candle, which is where the user asked
        /// for it.
        /// </para>
        /// </summary>
        private void ScanOverlayCrosses(ChartSeries series, WorkspaceState state, int barIndex, ScanUtterance utterance)
        {
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return;
            double close = (double)state.Data[barIndex].Close;
            if (close <= 0) return;

            var above = new List<string>();
            var below = new List<string>();
            int overlayCount = series.Components.Count(c => IsPriceSpaceOverlayLine(series, c));

            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
                if (!IsPriceSpaceOverlayLine(series, comp)) continue;

                var data = series.GetComponentData(comp.Name);
                if (data == null || barIndex >= data.Length) continue;
                double val = data[barIndex];
                if (double.IsNaN(val)) continue;

                string key = $"{series.Id}:{comp.Name}";
                bool nowAbove = close > val;

                // A first sighting SEEDS and says nothing — there is no previous side to have
                // crossed from, and announcing one would fire a cross the moment N was pressed.
                if (_lastPriceAboveOverlay.TryGetValue(key, out bool wasAbove) && wasAbove != nowAbove)
                {
                    // A one-line overlay IS its series — "the 50 EMA" — so the series name is the
                    // line's name and adding the component's would read "EMA 50 Ema". A multi-line
                    // one (a MA Cloud's fast and slow) has to say which of them was crossed.
                    string label = overlayCount > 1
                        ? $"{series.FriendlyName} {(string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)}"
                        : series.FriendlyName;
                    (nowAbove ? above : below).Add($"{label} at {SpeechPriceFormatter.FormatPrice(val)}");
                }

                _lastPriceAboveOverlay[key] = nowAbove;
            }

            if (above.Count > 0)
                utterance.Add(ScanUtterance.TierCross, series.FriendlyName, $"{series.Id}:overlay",
                              $"Price crossed above {string.Join(", ", above)}.");
            if (below.Count > 0)
                utterance.Add(ScanUtterance.TierCross, series.FriendlyName, $"{series.Id}:overlay",
                              $"Price crossed below {string.Join(", ", below)}.");
        }

        // ── The indicator crossing its own reference levels ──────────────────────

        /// <summary>
        /// The component a series' declared levels are ABOUT: its primary reading.
        ///
        /// <para>
        /// One component, not all of them, and the reason is Stochastic: %K and %D cross 80
        /// within a bar or two of each other, and saying it twice is the wall of speech this
        /// narrator's tiers exist to prevent. An oscillator LINE is preferred over a histogram
        /// or a plain line because that is what the pane's levels are drawn against — on Cipher
        /// B the first visible component is the WT Histogram, and its ±53 bands belong to the
        /// Wave Trend. A user who wants a different component picks it with N; the selection is
        /// honoured through <see cref="SeriesNarrationScope.ComponentNarrates"/> below.
        /// </para>
        /// </summary>
        private static ComponentConfig? PrimaryReading(ChartSeries series)
        {
            ComponentConfig? fallback = null;
            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
                if (comp.IsZoneLine || comp.UsesGradientSpeech) continue;
                if (IsMarkerDisplayType(comp.DisplayType)) continue;

                if (comp.DisplayType == ComponentDisplayType.Oscillator) return comp;
                if (fallback == null &&
                    (comp.DisplayType == ComponentDisplayType.Line || comp.DisplayType == ComponentDisplayType.Histogram))
                    fallback = comp;
            }
            return fallback;
        }

        /// <summary>
        /// "RSI 14: crossed above overbought, 70." on the bar close.
        ///
        /// <para>
        /// <b>Why this exists.</b> A census of every shipped provider on 2026-09-05 found that
        /// roughly thirty-five indicators — nearly every oscillator in the terminal: Stochastic,
        /// CCI, MFI, ADX, ROC, Williams %R, TRIX, CMO, Chop, PPO, StochRSI and the rest — had NO
        /// route by which they could ever narrate anything. They print no markers, declare no
        /// zone lines, own no cloud, and are not one of the three indicators
        /// (<c>RSI</c>, <c>MACD</c>, <c>ATR</c>) with a hand-registered
        /// <c>IndicatorContextDefinition</c>. Pressing N on any of them was a dead switch that
        /// confirmed "narrating" and then said nothing for the rest of the session.
        /// </para>
        ///
        /// <para>
        /// The fix uses what the providers ALREADY declare: <c>GetDefaultLevels</c>, which
        /// <c>SeriesManagementService.InjectDefaultLevels</c> turns into <c>series.Levels</c>.
        /// Those constants are the thresholds the indicator was designed around, so crossing one
        /// is the event. Registering a definition per indicator was the alternative and is what
        /// produced the gap: three of ninety-nine got one.
        /// </para>
        ///
        /// <para>
        /// Levels the user hid are skipped, and so are Main-pane series: a fixed constant cannot
        /// be a price (<c>MainPaneLevelUnitsTests</c> enforces that no Main-pane indicator
        /// declares one), and price-space crossings are <see cref="ScanOverlayCrosses"/>'s job.
        /// </para>
        /// </summary>
        private void ScanLevelCrosses(ChartSeries series, int barIndex, ScanUtterance utterance)
        {
            if (series.Levels.Count == 0) return;
            if (string.Equals(series.Pane, "Main", StringComparison.OrdinalIgnoreCase)) return;

            var comp = PrimaryReading(series);
            if (comp == null) return;

            // One voice per threshold. A component with a registered definition already has
            // hand-written overbought/oversold wording, and RSI declares 70/30 in both places.
            if (_contextAnalyzer.HasZoneThresholds(series.IndicatorCode, comp.Name)) return;

            var data = series.GetComponentData(comp.Name);
            if (data == null || barIndex < 0 || barIndex >= data.Length) return;
            double val = data[barIndex];
            if (double.IsNaN(val)) return;

            var above = new List<string>();
            var below = new List<string>();

            foreach (var level in series.Levels)
            {
                if (!level.IsVisible) continue;
                string key = $"{series.Id}:{comp.Name}:{level.Name}";
                bool nowAbove = val > level.Value;

                if (_lastAboveLevel.TryGetValue(key, out bool wasAbove) && wasAbove != nowAbove)
                    (nowAbove ? above : below).Add(LevelPhrase(level));

                _lastAboveLevel[key] = nowAbove;
            }

            if (above.Count > 0)
                utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, $"{series.Id}:levels",
                              $"{series.FriendlyName}: crossed above {string.Join(", ", above)}.");
            if (below.Count > 0)
                utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, $"{series.Id}:levels",
                              $"{series.FriendlyName}: crossed below {string.Join(", ", below)}.");
        }

        /// <summary>"overbought, 70" — or just "zero", where the name already IS the number.</summary>
        private static string LevelPhrase(LevelConfig level)
        {
            string name = level.Name.ToLowerInvariant();
            return Math.Abs(level.Value) < 1e-9 && name.Contains("zero")
                ? name
                : $"{name}, {level.Value.ToString("0.####", CultureInfo.InvariantCulture)}";
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
                if (IsPriceSpaceOverlayLine(series, comp))
                {
                    // Same reason the zone-line seed below exists: the side price is on when
                    // narration is switched on is not a cross.
                    var odata = series.GetComponentData(comp.Name);
                    if (odata != null && idx < odata.Length && !double.IsNaN(odata[idx])
                        && state.Data != null && idx < state.Data.Count)
                    {
                        double oClose = (double)state.Data[idx].Close;
                        if (oClose > 0) _lastPriceAboveOverlay[$"{series.Id}:{comp.Name}"] = oClose > odata[idx];
                    }
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
                        {
                            _lastPriceAboveZone[zoneKey] = seedClose > data[idx];
                            _lastZoneClose[zoneKey] = seedClose;
                        }
                    }
                }
            }
            // The side the reading is on when narration is switched on is not a cross.
            var primary = PrimaryReading(series);
            if (primary != null)
            {
                var pdata = series.GetComponentData(primary.Name);
                if (pdata != null && idx < pdata.Length && !double.IsNaN(pdata[idx]))
                    foreach (var level in series.Levels)
                        _lastAboveLevel[$"{series.Id}:{primary.Name}:{level.Name}"] = pdata[idx] > level.Value;
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

        private void ScanZoneLines(ChartSeries series, WorkspaceState state, int barIndex, ScanUtterance utterance)
        {
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return;
            double currentClose = (double)state.Data[barIndex].Close;
            if (currentClose <= 0) return;

            var bullishCrosses = new List<string>();
            var bearishCrosses = new List<string>();

            foreach (var comp in series.Components)
            {
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
                if (!comp.IsZoneLine) continue;

                var data = series.GetComponentData(comp.Name);
                if (data == null || barIndex >= data.Length) continue;

                double currentVal = data[barIndex];
                string zoneKey = $"{series.Id}:{comp.Name}";
                string lineName = comp.DisplayName ?? comp.Name;

                // ── Break detection ──────────────────────────────────────────────────
                if (_lastZoneLineValue.TryGetValue(zoneKey, out double lastVal))
                {
                    if (double.IsNaN(currentVal) && !double.IsNaN(lastVal))
                    {
                        // Polarity comes from LevelPolarity, and the reference price is the close
                        // at the bar the level last existed on — NOT this bar's close. A break is
                        // precisely the moment price crossed the level, so the current close sits
                        // on the far side of it and would invert every announcement.
                        //
                        // Before this it was `comp.Name.Contains("resistance", …)`, which is a
                        // property of the provider's naming, not of the market: "res_upper" fell
                        // through and had its break announced as "Support at 61,200 broken." —
                        // the opposite structural claim, with no visual to catch it.
                        //
                        // The touch, approach and cross messages below all route through
                        // SpeechPriceFormatter; the BREAK message — arguably the most
                        // consequential thing this narrator says — was still on F0, so a
                        // sub-dollar asset heard "Support at 0 broken."
                        double breakRefClose = _lastZoneClose.TryGetValue(zoneKey, out double lastClose)
                            ? lastClose
                            : currentClose;
                        string breakMsg = LevelPolarity.IsResistance(lastVal, breakRefClose)
                            ? $"{series.FriendlyName}: Resistance at {SpeechPriceFormatter.FormatPrice(lastVal)} broken."
                            : $"{series.FriendlyName}: Support at {SpeechPriceFormatter.FormatPrice(lastVal)} broken.";
                        utterance.Add(ScanUtterance.TierBreak, series.FriendlyName, zoneKey, breakMsg);
                        _lastZoneLineValue.Remove(zoneKey);
                        _lastZoneClose.Remove(zoneKey);
                        _inProximity.Remove(zoneKey);
                        _lastPriceAboveZone.Remove(zoneKey);
                        string tcKey2 = $"{series.Id}:{comp.Name}:touches";
                        _lastTouchCount.Remove(tcKey2);
                        continue;
                    }
                }

                if (double.IsNaN(currentVal)) continue;

                // Everything below describes THIS bar, so this bar's close is the reference.
                bool isResistance = LevelPolarity.IsResistance(currentVal, currentClose);

                _lastZoneLineValue[zoneKey] = currentVal;
                _lastZoneClose[zoneKey] = currentClose;

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
                        utterance.Add(ScanUtterance.TierTouch, series.FriendlyName, zoneKey, touchMsg);
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
                    utterance.Add(ScanUtterance.TierApproach, series.FriendlyName, zoneKey, approachMsg);
                }
                _inProximity[zoneKey] = nowNear;

                // ── Cross detection ──────────────────────────────────────────────────
                bool priceAboveNow = currentClose > currentVal;
                if (_lastPriceAboveZone.TryGetValue(zoneKey, out bool wasAbove))
                {
                    if (!wasAbove && priceAboveNow)
                    {
                        bullishCrosses.Add($"{lineName} at {SpeechPriceFormatter.FormatPrice(currentVal)}");
                        utterance.SuppressApproachFor(zoneKey);
                    }
                    else if (wasAbove && !priceAboveNow)
                    {
                        bearishCrosses.Add($"{lineName} at {SpeechPriceFormatter.FormatPrice(currentVal)}");
                        utterance.SuppressApproachFor(zoneKey);
                    }
                }
                _lastPriceAboveZone[zoneKey] = priceAboveNow;
            }

            // Announce grouped cross messages (avoids flood of individual announcements).
            if (bullishCrosses.Count > 0)
            {
                string crossed = string.Join(", ", bullishCrosses);
                utterance.Add(ScanUtterance.TierCross, series.FriendlyName, $"{series.Id}:crosses",
                              $"{series.FriendlyName}: Price crossed above {crossed}.");
            }
            if (bearishCrosses.Count > 0)
            {
                string crossed = string.Join(", ", bearishCrosses);
                utterance.Add(ScanUtterance.TierCross, series.FriendlyName, $"{series.Id}:crosses",
                              $"{series.FriendlyName}: Price crossed below {crossed}.");
            }
        }

        // ── Cloud entry/exit detection ───────────────────────────────────────────

        private void ScanCloudTransitions(ChartSeries series, WorkspaceState state, int barIndex, ScanUtterance utterance)
        {
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return;
            double close = (double)state.Data[barIndex].Close;
            if (close <= 0 || double.IsNaN(close)) return;

            foreach (var comp in series.Components)
            {
                if (comp.DisplayType != ComponentDisplayType.Cloud) continue;
                if (!comp.IsVisible || comp.IsMuted) continue;
                if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
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

                    utterance.Add(ScanUtterance.TierCross, series.FriendlyName, cloudKey, msg);
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
            string valueStr = val.ToString("F1", CultureInfo.InvariantCulture);

            if (!string.IsNullOrEmpty(comp.SignalSpeechTemplate))
            {
                return comp.SignalSpeechTemplate
                    .Replace("{price}", price)
                    .Replace("{value}", valueStr)
                    .Replace("{name}", !string.IsNullOrEmpty(comp.DisplayName) ? comp.DisplayName : comp.Name)
                    .Replace("{series}", series.FriendlyName);
            }

            // The component, not the series, introduces a signal — see ScanUtterance.Compose.
            return $"{SignalClauseSpeech.ComponentName(comp)} at {price}.";
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
