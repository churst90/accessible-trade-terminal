using System.Globalization;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// <b>The narration ladder with no store, no bus and no browser</b> — the per-series scan
    /// that <c>AutoNarrationService</c> runs on every redraw, lifted out so it can be run
    /// against a state somebody else composed.
    ///
    /// <para>
    /// ── Why it was lifted ──────────────────────────────────────────────────────
    /// Cody, 2026-09-11: <i>"I also want the narration ladder to also be spoken when the
    /// browser is closed too."</i> Until this class the whole scan — marker signals, zone-line
    /// breaks, touches and approaches, cloud transitions, overlay crosses, level crosses, the
    /// volume reading, oscillator zones and crossovers — was a set of private instance methods
    /// on a service bound to <c>IWorkspaceStore</c> and <c>RedrawEvent</c>, and its tracking
    /// dictionaries (what has already been announced, which side of each line the close was on)
    /// were fields of that service. Nothing headless has a store to bind to: the background
    /// monitor fetches bars off the provider once a minute and holds them as method arguments.
    /// Copying the scan would have been a second place every rule has to be added; this is the
    /// one place, and both the in-session service and <see cref="HeadlessChart"/> call
    /// it.
    /// </para>
    ///
    /// <para>
    /// ── What it owns ───────────────────────────────────────────────────────────
    /// The per-series tracking state, keyed <c>"{seriesId}:{componentName}"</c>, exactly as the
    /// service kept it. One instance per CHART: the in-session service has one for the focused
    /// chart; the headless narrator has one per watched tab. It does not decide WHEN to scan —
    /// the caller answers the master switches, works out which bar closed and hands over the
    /// state — and it does not speak; it returns the composed sentence or null.
    /// </para>
    ///
    /// <para>
    /// Signal detection rules, unchanged from the service:
    ///   Marker components (Dot/Arrow/Diamond/TriangleUp/Down/Square/Cross/ZeroDot): a non-NaN
    ///   value at a bar index that appeared AFTER narration was enabled is announced. A 20-bar
    ///   look-back window catches signals from pivot-based indicators whose confirmation is
    ///   delayed by several bars. Components with UsesGradientSpeech are excluded — their zone
    ///   transitions are handled by the oscillator context path.
    ///   Oscillator components: zone transitions and crossovers are detected on bar close via
    ///   <see cref="IIndicatorContextAnalyzer"/> and announced on the first occurrence.
    ///   Seeding: <see cref="Seed"/> records the current bar count; only bars at or after the
    ///   forming bar are eligible, so nothing historical is replayed when N is pressed.
    /// </para>
    /// </summary>
    public sealed class NarrationScanner
    {
        private readonly IIndicatorContextAnalyzer _contextAnalyzer;

        /// <summary>
        /// Bar count when narration was enabled for each series.
        /// Bars with index &lt; seedCount are historical and never announced. Key = series ID.
        /// </summary>
        private readonly Dictionary<string, int> _seedBarCounts = new();

        /// <summary>Which bar indices have already been announced per series+component.</summary>
        private readonly Dictionary<string, HashSet<int>> _announcedMarkers = new();

        /// <summary>Last zone and crossover state for oscillator transition detection.</summary>
        private readonly Dictionary<string, (ZoneStatus Zone, CrossoverStatus Crossover)> _lastOscState = new();

        /// <summary>Last known price position relative to cloud boundaries: "inside", "above" or "below".</summary>
        private readonly Dictionary<string, string> _lastCloudPosition = new();

        /// <summary>Last confirmed pivot bar index seen per series+component, for pivot-based indicators.</summary>
        private readonly Dictionary<string, int> _lastSeenPivotIndex = new();

        /// <summary>Last zone-line value seen per series+component, for break detection.</summary>
        private readonly Dictionary<string, double> _lastZoneLineValue = new();

        /// <summary>
        /// The bar close at the last bar on which each zone line still had a value.
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

        /// <summary>Last touch count seen per series+component.</summary>
        private readonly Dictionary<string, int> _lastTouchCount = new();

        /// <summary>Whether "approaching" has already been said for a level we are still near.</summary>
        private readonly Dictionary<string, bool> _inProximity = new();

        /// <summary>Whether price was above each zone-line component on the previous bar.</summary>
        private readonly Dictionary<string, bool> _lastPriceAboveZone = new();

        /// <summary>
        /// Which side of a plain price-space OVERLAY line (an EMA, a VWAP, a MA Cloud band) the
        /// close was on at the last bar close. Separate from <see cref="_lastPriceAboveZone"/>
        /// because that one belongs to declared zone lines, which also carry break, touch and
        /// approach vocabulary; an overlay gets crosses and nothing else.
        /// </summary>
        private readonly Dictionary<string, bool> _lastPriceAboveOverlay = new();

        /// <summary>
        /// Which side of one of the series' declared reference levels its reading was on at the
        /// last bar close. Key = "{seriesId}:{componentName}:{levelName}".
        /// </summary>
        private readonly Dictionary<string, bool> _lastAboveLevel = new();

        /// <summary>
        /// Which side of a PROFILE's point of control the close was on at the last bar close,
        /// whether it sat below, inside or above the value area, and where the point of control
        /// was — the three things a narrated profile has to say. Key = "{seriesId}:profile".
        /// </summary>
        private readonly Dictionary<string, bool> _lastPriceAbovePoc = new();
        private readonly Dictionary<string, int> _lastValueAreaSide = new();
        private readonly Dictionary<string, double> _lastPoc = new();

        /// <summary>
        /// Window of bars to scan behind the just-closed bar to catch delayed-confirmation signals
        /// (e.g. SR pivots that need pivotBars future bars before they appear in the data).
        /// 20 is generous enough for the maximum AutoScale pivotBars = 15.
        /// </summary>
        public const int PivotConfirmWindow = 20;

        public NarrationScanner(IIndicatorContextAnalyzer contextAnalyzer)
        {
            _contextAnalyzer = contextAnalyzer;
        }

        // ── Seeding and forgetting ───────────────────────────────────────────────

        /// <summary>Whether <see cref="Seed"/> has been called for this series and not forgotten since.</summary>
        public bool IsSeeded(string seriesId) => _seedBarCounts.ContainsKey(seriesId);

        /// <summary>
        /// Narration was just switched on for <paramref name="series"/>: record where history
        /// ends and which side of everything the close is on, so the first scan announces only
        /// what happens NEXT.
        ///
        /// <para>── THE LIVE BAR IS NOT HISTORICAL ──────────────────────────────────────
        /// The seed was <c>barCount</c>, and the effect was that THE FIRST BAR TO CLOSE AFTER
        /// YOU SWITCH NARRATION ON COULD NEVER SPEAK — the one bar the user is listening for
        /// when they press N. The scan requires <c>max(seedCount, …) &lt;= closedBound</c>, and
        /// the first bar to close is index <c>barCount - 1</c>, one below a seed of
        /// <c>barCount</c>. The last bar is the FORMING one. Everything strictly before it has
        /// closed already and is history nobody asked to have replayed; the forming bar has not
        /// happened yet, and what it prints when it closes is exactly the news the flag was set
        /// to hear. Measured, not reasoned: the diagnostic in NewBarNarrationCompositionTests
        /// spoke only the candle on the first close and the signal on the second.</para>
        /// </summary>
        public void Seed(ChartSeries series, WorkspaceState state)
        {
            int barCount = state.Data?.Count ?? 0;
            _seedBarCounts[series.Id] = Math.Max(0, barCount - 1);

            SeedOscillatorState(series, state);
            SeedZoneLineState(series, state);
            SeedCloudState(series, state);
            SeedProfileState(series, state);
        }

        /// <summary>Narration was switched off for this series — drop its tracking to keep the dictionaries lean.</summary>
        public void Forget(string seriesId)
        {
            _seedBarCounts.Remove(seriesId);
            string prefix = seriesId + ":";
            RemoveByPrefix(_announcedMarkers, prefix);
            RemoveByPrefix(_lastOscState, prefix);
            RemoveByPrefix(_lastSeenPivotIndex, prefix);
            RemoveByPrefix(_lastZoneLineValue, prefix);
            RemoveByPrefix(_lastZoneClose, prefix);
            RemoveByPrefix(_lastTouchCount, prefix);
            RemoveByPrefix(_inProximity, prefix);
            RemoveByPrefix(_lastPriceAboveZone, prefix);
            RemoveByPrefix(_lastPriceAboveOverlay, prefix);
            RemoveByPrefix(_lastAboveLevel, prefix);
            RemoveByPrefix(_lastCloudPosition, prefix);
            RemoveByPrefix(_lastPriceAbovePoc, prefix);
            RemoveByPrefix(_lastValueAreaSide, prefix);
            RemoveByPrefix(_lastPoc, prefix);
        }

        private static void RemoveByPrefix<T>(Dictionary<string, T> map, string prefix)
        {
            foreach (var k in map.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                map.Remove(k);
        }

        /// <summary>
        /// The caller dropped <paramref name="by"/> bars off the FRONT of its data, so every bar
        /// this scanner remembers by index has moved left by that much.
        ///
        /// <para>The in-session store only ever grows, so the service never needed this. A
        /// headless buffer cannot grow forever — one poll a minute on a 1-minute chart is 1,440
        /// bars a day — and trimming it without telling the scanner would re-announce every
        /// marker in the 20-bar window (the announced set is a set of INDICES) and stop honouring
        /// the seed. Shifting is the alternative to re-seeding, which would lose an in-flight
        /// pivot confirmation for no reason.</para>
        /// </summary>
        public void ShiftIndices(int by)
        {
            if (by <= 0) return;
            foreach (var k in _seedBarCounts.Keys.ToList())
                _seedBarCounts[k] = Math.Max(0, _seedBarCounts[k] - by);
            foreach (var k in _announcedMarkers.Keys.ToList())
                _announcedMarkers[k] = _announcedMarkers[k].Select(i => i - by).Where(i => i >= 0).ToHashSet();
            foreach (var k in _lastSeenPivotIndex.Keys.ToList())
                _lastSeenPivotIndex[k] -= by;   // may go below zero: "none seen in the window"
        }

        // ── The scan ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything one scan finds across every narrated, seeded series in
        /// <paramref name="series"/>, composed into ONE phrase — or null when there is nothing.
        /// </summary>
        /// <param name="closedBound">The newest CONFIRMED bar index to look at: the bar that just
        /// closed on a bar close, the penultimate bar on an intra-bar tick.</param>
        /// <param name="isBarClose">True when <paramref name="closedBound"/> closed since the last
        /// scan. Everything that reads a confirmed value — overlay and level crosses, the volume
        /// reading, oscillator zones — runs only then.</param>
        internal string? ScanAll(IEnumerable<ChartSeries> series, WorkspaceState state, int closedBound, bool isBarClose)
        {
            if (closedBound < 0) return null;

            // Everything this scan finds, across every narrated series, goes into ONE phrase —
            // see ScanUtterance. Nine Speak calls in one handler is eight discarded on the web
            // head and an unstoppable queue of nine on the desktop one.
            var utterance = new ScanUtterance();

            foreach (var s in series)
            {
                if (!s.IsAutoNarrated) continue;
                if (!_seedBarCounts.TryGetValue(s.Id, out int seedCount)) continue;

                // Scan window: covers recently-confirmed delayed signals (pivot indicators).
                // seedCount is the exclusive lower bound — bars before it are historical.
                int scanFrom = Math.Max(seedCount, closedBound - PivotConfirmWindow);
                if (scanFrom > closedBound) continue;

                ScanSeriesForChanges(s, state, scanFrom, closedBound, isBarClose, utterance);
            }

            string composed = utterance.Compose();
            return string.IsNullOrEmpty(composed) ? null : composed;
        }

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

            // 1d′. A PROFILE: price against its point of control and value area, and the point
            // of control moving. A profile has no per-bar value and no marker; what it has is
            // three levels, and the news about a level is what crossed it.
            ScanProfile(series, state, toIndex, utterance);

            // 1e. The indicator crossing one of its OWN declared levels (RSI 70, MACD zero).
            ScanLevelCrosses(series, toIndex, utterance);

            // 1f. The reading itself, for a series with nothing signal-shaped to say. A volume
            // pane under N used to set a flag nothing acted on; now the closed bar's value is the
            // last clause of the ladder. Bar close only — this branch is below the isBarClose
            // return above on purpose, and playback never reaches this scan at all.
            ReadValueAtClose(series, state, toIndex, utterance);

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
                // Hidden or muted is silent here exactly as it is at the four other scan sites
                // (Cody, 2026-09-05: "if a series or component is hidden, it should be excluded
                // from the narration"). Two gaps closed: a hidden oscillator COMPONENT used to
                // narrate because this path applied only the N selection; and a context naming
                // a component the series does not have skipped the scope check altogether, so a
                // hidden SERIES spoke through it. HiddenSeriesNarrationTests pins both.
                bool oscNarrates = SeriesNarrationScope.SeriesNarrates(series)
                    && (oscComp == null
                        || (oscComp.IsVisible && !oscComp.IsMuted
                            && SeriesNarrationScope.ComponentNarrates(series, oscComp)));

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

        // ── The reading at the close ─────────────────────────────────────────────

        /// <summary>
        /// "Volume: 12,345." on the bar close, for a series whose narratable content is a
        /// bar-type component and nothing else. Cody, 2026-09-11: <i>"I may be doing dishes but
        /// still want to keep an ear on the volume, so hearing everything if narrated in the
        /// ladder is valuable."</i> The component is chosen by
        /// <see cref="SeriesNarrationScope.ReadingComponent"/>, which is also what the N-key
        /// confirmation consults, so the toggle and the ladder cannot disagree about whether a
        /// series reads.
        /// </summary>
        private static void ReadValueAtClose(ChartSeries series, WorkspaceState state, int barIndex, ScanUtterance utterance)
        {
            var comp = SeriesNarrationScope.ReadingComponent(series);
            if (comp == null) return;

            var data = series.GetComponentData(comp.Name);
            if (data == null || barIndex < 0 || barIndex >= data.Length) return;
            double val = data[barIndex];
            if (double.IsNaN(val)) return;

            // A VOLUME bar is coloured by the candle it belongs to, and the arrow-key reading
            // already says so ("12,345.68, up" — SpeechFormatter.VolumeBarStrategy). The close
            // reading said the number alone. Cody, 2026-09-11: "in addition to the value of the
            // volume bar, it may be nice to hear the direction as well up or down." Same words,
            // same source (the bar's own open and close), same order — the value, then the
            // direction — so a reading heard headless matches one heard while arrowing. Only for
            // volume: a MACD histogram's sign is already in its number.
            string direction = "";
            if (comp.Role == ComponentRole.Volume
                && state.Data != null && barIndex < state.Data.Count)
            {
                var bar = state.Data[barIndex];
                direction = bar.Close >= bar.Open ? ", up" : ", down";
            }

            // Spoken form: "1.2 million", never "1.2M" — a screen reader reads the letter.
            //
            // No colon after the name on purpose. ScanUtterance.Compose drops a "{series}: "
            // prefix from a clause that follows another clause about the same series, so behind
            // "Volume: crossed above level 1, 100500." a reading built the same way would arrive
            // as a bare "101,000." — a number with nothing to say what it is. A reading is the
            // one clause whose whole content is its name and a value; it keeps both.
            utterance.Add(ScanUtterance.TierReading, series.FriendlyName, $"{series.Id}:reading",
                          $"{series.FriendlyName} {QuantityFormatter.FormatSpoken(val)}{direction}");
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

        // ── A profile: price against the point of control and the value area ────

        /// <summary>
        /// The narration route for a volume or market profile. Cody, 2026-09-11: <i>"I don't
        /// hear any narration events for profiles, volume or market."</i> Until then N on a
        /// profile promised "Value read at each bar close" and could not keep it — the
        /// profile's one component is a Bar with no per-bar data (the bins live in
        /// <c>ProfileBins</c>), so the reading found an empty array and said nothing.
        ///
        /// <para>What a profile can say at a bar close, in the order a trader wants it: price
        /// crossed the point of control (the price the market accepted most — a cross of it is
        /// a change of hands); price entered or left the value area (the 70% the market agreed
        /// on — leaving it is a breakout attempt, re-entering is a failed one); and the point of
        /// control itself moved to a new price (acceptance shifting). The first two are
        /// crossings and rank with the other crossings; the third is the lowest tier, so a busy
        /// close drops it first.</para>
        ///
        /// <para>Bar close only, seeded on first sighting like the overlay cross, and read off
        /// the SAME bins the level service hands a POC alert — so the ladder and the alert
        /// name one price.</para>
        /// </summary>
        private void ScanProfile(ChartSeries series, WorkspaceState state, int barIndex, ScanUtterance utterance)
        {
            if (!SeriesNarrationScope.SeriesNarrates(series)) return;
            if (ProfileSides(series, state, barIndex) is not var (key, levels, poc, nowAbove, side)) return;
            string name = series.FriendlyName;

            // Seeded when N was pressed (SeedProfileState) — or, for a series that arrived
            // without a seed, on this first sighting, silently: there is no previous side to
            // have crossed from, and announcing one would fire a cross the moment N was pressed.
            if (_lastPriceAbovePoc.TryGetValue(key, out bool wasAbove))
            {
                if (wasAbove != nowAbove)
                    utterance.Add(ScanUtterance.TierCross, name, key + ":poc",
                        $"{name}: Price crossed {(nowAbove ? "above" : "below")} the point of control at {SpeechPriceFormatter.FormatPrice(poc)}.");

                if (levels.HasValueArea && _lastValueAreaSide.TryGetValue(key, out int wasSide) && wasSide != side)
                {
                    string va = side == 0
                        ? $"Price entered the value area, {SpeechPriceFormatter.FormatPrice(levels.ValueAreaLow!.Value)} to {SpeechPriceFormatter.FormatPrice(levels.ValueAreaHigh!.Value)}."
                        : side > 0
                            ? $"Price left the value area above {SpeechPriceFormatter.FormatPrice(levels.ValueAreaHigh!.Value)}."
                            : $"Price left the value area below {SpeechPriceFormatter.FormatPrice(levels.ValueAreaLow!.Value)}.";
                    utterance.Add(ScanUtterance.TierTouch, name, key + ":va", $"{name}: {va}");
                }

                // Moved by at least a bin: the profile is re-binned over a slightly different
                // range every bar, and a midpoint drifting by less than a bin is the binning,
                // not the market.
                if (_lastPoc.TryGetValue(key, out double prevPoc) && levels.BinWidth > 0
                    && Math.Abs(poc - prevPoc) >= levels.BinWidth * 0.999)
                    utterance.Add(ScanUtterance.TierReading, name, key + ":pocmove",
                        $"{name}: Point of control moved to {SpeechPriceFormatter.FormatPrice(poc)}.");
            }

            _lastPriceAbovePoc[key] = nowAbove;
            _lastValueAreaSide[key] = side;
            _lastPoc[key] = poc;
        }

        /// <summary>
        /// Where the close at <paramref name="barIndex"/> sits against the profile: above or
        /// below the point of control, and below (-1), inside (0) or above (+1) the value area.
        /// Null when the series is not a profile, has no bins, or the bar is out of range.
        /// </summary>
        private static (string Key, ProfileLevels.Levels Levels, double Poc, bool AbovePoc, int ValueAreaSide)?
            ProfileSides(ChartSeries series, WorkspaceState state, int barIndex)
        {
            if (!SeriesNarrationScope.IsProfileSeries(series)) return null;
            if (state.Data == null || barIndex < 0 || barIndex >= state.Data.Count) return null;

            var levels = ProfileLevels.Of(series.ProfileBins);
            if (levels.Poc is not double poc) return null;
            double close = (double)state.Data[barIndex].Close;
            if (close <= 0) return null;

            int side = levels.HasValueArea
                ? (close < levels.ValueAreaLow!.Value ? -1 : close > levels.ValueAreaHigh!.Value ? 1 : 0)
                : 0;
            return ($"{series.Id}:profile", levels, poc, close > poc, side);
        }

        /// <summary>
        /// N was just pressed on a profile: record which side of its levels the newest bar is
        /// on, so the FIRST close after the flag can speak. Without this the scan's own
        /// first-sighting seed swallowed that close — the one bar the user is listening for
        /// when they press N, as the Seed doc above already says of the marker window.
        /// </summary>
        private void SeedProfileState(ChartSeries series, WorkspaceState state)
        {
            if (ProfileSides(series, state, state.CurrentDataIndex) is not var (key, _, poc, nowAbove, side)) return;
            _lastPriceAbovePoc[key] = nowAbove;
            _lastValueAreaSide[key] = side;
            _lastPoc[key] = poc;
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
                // Line, histogram OR bar. The real Volume component is a Bar (CoreIndicatorProvider),
                // and until 2026-09-11 this accepted only Line and Histogram — so a level placed on a
                // volume pane could never be crossed by anything, while the N-key message was
                // advising exactly that. The test that "proved" the advice modelled volume as a
                // histogram. Match production, not the fixture.
                if (fallback == null &&
                    (comp.DisplayType == ComponentDisplayType.Line || SeriesNarrationScope.IsReadingDisplay(comp.DisplayType)))
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

            // Three phrasings, because three things can be true of a line you just crossed.
            //
            // A BAND EDGE says where you now ARE — "strong trend" — because that is what the
            // declaration is for and it is the whole message of an indicator like ADX. Cody, on
            // hearing the band words land in the cursor readout: "when narration is on, should it
            // speak the zones as price crosses into them?" Yes, and this is where.
            //
            // Anything else keeps the older wording, which names the LINE and its value: on RSI,
            // "crossed above overbought, 70" is the useful sentence, because the number is the
            // thing the reader has calibrated against.
            var entered = new List<string>();
            var above = new List<string>();
            var below = new List<string>();

            foreach (var level in series.Levels)
            {
                if (!level.IsVisible) continue;
                if (!SubscribesToLevel(comp, level.Name)) continue;
                string key = $"{series.Id}:{comp.Name}:{level.Name}";
                bool nowAbove = val > level.Value;

                if (_lastAboveLevel.TryGetValue(key, out bool wasAbove) && wasAbove != nowAbove)
                {
                    string? band = nowAbove ? level.AboveLabel : level.BelowLabel;
                    if (!string.IsNullOrWhiteSpace(band)) entered.Add(band!);
                    else (nowAbove ? above : below).Add(LevelPhrase(level));
                }

                _lastAboveLevel[key] = nowAbove;
            }

            if (entered.Count > 0)
                utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, $"{series.Id}:levels",
                              $"{series.FriendlyName}: {string.Join(", ", entered)}.");
            if (above.Count > 0)
                utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, $"{series.Id}:levels",
                              $"{series.FriendlyName}: crossed above {string.Join(", ", above)}.");
            if (below.Count > 0)
                utterance.Add(ScanUtterance.TierOscillator, series.FriendlyName, $"{series.Id}:levels",
                              $"{series.FriendlyName}: crossed below {string.Join(", ", below)}.");
        }

        /// <summary>
        /// Whether this component answers to that level, using the subscription list the audio
        /// layer and the spoken zone word both honour. On a pane like Aroon's — Up and Down about
        /// 50, the Oscillator about zero — narrating a line that belongs to a different scale
        /// would be a crossing that did not happen.
        /// </summary>
        private static bool SubscribesToLevel(ComponentConfig comp, string levelName)
        {
            if (comp.SubscribedLevelNames is not { } subs) return true;
            if (subs.Count == 0) return false;
            return subs.Contains(levelName, StringComparer.OrdinalIgnoreCase);
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
    }
}
