using System.Globalization;
using AccessibleTrader.Core.Services.Audio;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// The words a user hears about playback — the text equivalent of a run of tones.
    ///
    /// <para>
    /// Until 2026-09-02 there were none. The coordinator's playback gate returned before any
    /// announcement "because the PlaybackOrchestrator handles its own sonification/speech", and
    /// the orchestrator has no speech router; nothing else spoke either. Space started a stream
    /// of tones with no word about what was playing or from where, Ctrl+Space parked it in
    /// silence, Shift+= changed the speed without saying so, and when the last bar sounded the
    /// tones simply stopped — the same sound as a crash, a dropped feed or a muted chart. Up to
    /// eight minutes of the terminal's richest output, and not one sentence (WCAG 1.1.1, 1.2.1).
    /// </para>
    ///
    /// <para>
    /// Everything here is a pure function of state, so the coordinator's tests can assert the
    /// exact sentence and the rules can be read in one place:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Start</b> names the scope, the first bar and how many bars will play.</item>
    ///   <item><b>Pause</b> names where the cursor parked; <b>resume</b> is one word.</item>
    ///   <item><b>Finished</b> and <b>stopped</b> are different sentences. The sequencer reached
    ///         the last bar in one case and was interrupted in the other, and a user who hears
    ///         "finished" knows the whole range sounded.</item>
    ///   <item><b>Landmarks</b> while it runs: the tones carry price, so speech carries time. Each
    ///         time the bar's date crosses a calendar boundary one step coarser than the bar
    ///         spacing — hour, day, month or year, chosen so that at the current speed the
    ///         announcements are at least <see cref="MinSecondsBetweenLandmarks"/> apart — the new
    ///         period is spoken without interrupting. Nothing else is said per bar: a bar readout
    ///         every 100 ms would be noise, not an equivalent.</item>
    /// </list>
    /// </summary>
    public static class PlaybackNarration
    {
        /// <summary>Playback speaks ~10 bars a second at 1x; a landmark every two seconds is a
        /// cadence a person can follow without the words piling up.</summary>
        public const double MinSecondsBetweenLandmarks = 2.0;

        /// <summary>Bars per second at speed 1.0 — the sequencer's 100 ms base delay.</summary>
        private const double BarsPerSecondAtUnitSpeed = 10.0;

        public enum LandmarkUnit { Hour, Day, Month, Year }

        // ── Start / pause / resume / stop ──────────────────────────────────────────

        public static string StartText(WorkspaceState state, PlaybackPlan plan)
        {
            string what = state.PlaybackScope switch
            {
                PlaybackScope.Chart => "chart",
                PlaybackScope.Component => ComponentName(plan),
                _ => plan.Series.Count > 0 ? SeriesName(plan.Series[0]) : "series",
            };

            int barSeconds = BarSeconds(state);
            int count = state.Data!.Count - plan.StartIndex;
            string from = DateText(state.Data[plan.StartIndex].Date, barSeconds);
            return $"Playing {what} from {from}, {count} bar{(count == 1 ? "" : "s")}."
                 + SilentSignalsCaveat(state, plan);
        }

        /// <summary>
        /// The sentence that turns a silent playback into an answerable one, or "" when there is
        /// nothing to disclose.
        ///
        /// <para>
        /// Reported by Cody, 2026-09-04: <i>"when I added cipher sr or b to the chart I don't hear
        /// signals being spoken during playback"</i>. Both indicators exist to print signals, and
        /// both were silent — because <see cref="SeriesConfig.IsAutoNarrated"/> defaults to FALSE
        /// and nothing sets it when an indicator is added, so <see cref="SignalsForStep"/> skipped
        /// every series on the chart.
        /// </para>
        ///
        /// <para>
        /// The default is deliberate and is not changed here: the standing convention is that
        /// continuous verbal output is opted into rather than imposed, and flipping it on for
        /// every added indicator would make a chart with four of them unlistenable. What was NOT
        /// deliberate is the silence being indistinguishable from a broken feature. A user who has
        /// switched narration on in Settings, added a signal indicator and pressed play has done
        /// everything the feature asks of them; the one thing left is a per-series flag they have
        /// no way to know exists. So playback says it, once, at the moment they asked — the same
        /// remedy the detail key got for an empty chart.
        /// </para>
        ///
        /// <para>
        /// Only when there is something to be silent ABOUT. A chart of plain moving averages has
        /// no signals to narrate whether or not a series is flagged, and telling that user about a
        /// shortcut for a feature they are not missing is the noise this whole convention exists
        /// to avoid.
        /// </para>
        /// </summary>
        /// <param name="plan">Scoped exactly like <see cref="SignalsForStep"/>, and it has to be:
        /// with a plan pinning one un-narrated series, "no series is set to narrate" was FALSE
        /// whenever some other series on the chart was flagged — so the one disclosure that
        /// explains the silence went missing in precisely the case it was written for.</param>
        internal static string SilentSignalsCaveat(WorkspaceState state, PlaybackPlan? plan = null)
        {
            if (!state.NarrateDuringPlayback) return "";

            var inScope = plan?.Series is { Count: > 0 } scoped
                ? state.ActiveSeries.Where(s => scoped.Any(p => p.Id == s.Id)).ToList()
                : state.ActiveSeries.ToList();

            if (inScope.Any(s => s.IsAutoNarrated && s.IsVisible && !s.IsMuted)) return "";

            int componentFilter = plan?.ComponentFilter ?? -1;
            bool anySignalsToMiss = inScope.Any(s =>
                s.IsVisible && !s.IsMuted && s.Components.Where((c, i) => componentFilter < 0 || i == componentFilter).Any(c =>
                    c.IsVisible && !c.IsMuted && !c.IsZoneLine && !c.UsesGradientSpeech
                    && AudioConstants.MarkerDisplayTypes.Contains(c.DisplayType)
                    && !string.IsNullOrEmpty(c.SignalSpeechTemplate)));

            return anySignalsToMiss
                ? " No series is set to narrate, so signals will not be spoken."
                  + " Press N on a series to turn its narration on."
                : "";
        }

        /// <summary>The name the arrow keys use for a series, with the config name behind it for
        /// a series that was never given a friendly one — a blank would read "Playing  from".</summary>
        private static string SeriesName(ChartSeries series)
            => string.IsNullOrWhiteSpace(series.FriendlyName) ? series.Name : series.FriendlyName;

        private static string ComponentName(PlaybackPlan plan)
        {
            if (plan.Series.Count == 0) return "component";
            var series = plan.Series[0];
            if (plan.ComponentFilter < 0 || plan.ComponentFilter >= series.Components.Count)
                return SeriesName(series);
            // A one-component indicator would stutter — "RSI RSI", "VWAP VWAP" — and the
            // series name already says everything the component name would.
            if (series.Components.Count == 1) return SeriesName(series);
            var comp = series.Components[plan.ComponentFilter];
            string name = string.IsNullOrWhiteSpace(comp.DisplayName) ? comp.Name : comp.DisplayName;
            return $"{SeriesName(series)} {name}";
        }

        public static string PauseText(WorkspaceState state)
            => $"Paused at {CursorDateText(state)}.";

        public const string ResumeText = "Resumed.";

        /// <summary>
        /// "Finished" when the cursor is on the last bar, "stopped" otherwise. The sequencer
        /// walks the cursor to <c>Count - 1</c> before it ends, so that is what "the whole range
        /// sounded" looks like from the store; a user stop lands anywhere before it.
        /// </summary>
        public static string EndText(WorkspaceState state)
            => $"Playback {(ReachedEnd(state) ? "finished" : "stopped")} at {CursorDateText(state)}.";

        /// <summary>
        /// The sequencer iterates a snapshot of the data, so a bar appended by the live feed
        /// mid-playback makes a full run land one short of the new last bar and read as
        /// "stopped". Rare, and the honest reading of the store; noted rather than special-cased.
        /// </summary>
        public static bool ReachedEnd(WorkspaceState state)
            => state.Data != null && state.Data.Count > 0
            && state.CurrentDataIndex >= state.Data.Count - 1;

        public static string SpeedText(float speed)
            => $"Playback speed: {speed.ToString("F1", CultureInfo.InvariantCulture)}x";

        // ── Landmarks while playing ─────────────────────────────────────────────────

        /// <summary>
        /// The coarsest-necessary calendar unit for landmark speech: the smallest of hour, day,
        /// month, year that holds at least <see cref="MinSecondsBetweenLandmarks"/> worth of bars
        /// at <paramref name="speed"/>. Year is the ceiling — monthly bars at any speed land here.
        /// </summary>
        public static LandmarkUnit UnitFor(int barSeconds, float speed)
        {
            double barsPerSecond = BarsPerSecondAtUnitSpeed * Math.Max(0.1, speed);
            double minBars = MinSecondsBetweenLandmarks * barsPerSecond;
            int bar = Math.Max(1, barSeconds);

            if (3600.0 / bar >= minBars) return LandmarkUnit.Hour;
            if (86400.0 / bar >= minBars) return LandmarkUnit.Day;
            if (2592000.0 / bar >= minBars) return LandmarkUnit.Month;
            return LandmarkUnit.Year;
        }

        /// <summary>
        /// The landmark to speak when playback steps from <paramref name="previous"/> to
        /// <paramref name="current"/>, or null when no boundary of <paramref name="unit"/> was
        /// crossed. Compared in the user's zone, the same instant the arrow keys read.
        /// </summary>
        public static string? Landmark(DateTime previous, DateTime current, LandmarkUnit unit)
        {
            var p = SpeechTimeFormatter.ToDisplay(previous);
            var c = SpeechTimeFormatter.ToDisplay(current);

            switch (unit)
            {
                case LandmarkUnit.Hour:
                    if (p.Date == c.Date && p.Hour == c.Hour) return null;
                    return c.ToString("HH:mm", CultureInfo.InvariantCulture);
                case LandmarkUnit.Day:
                    if (p.Date == c.Date) return null;
                    return c.ToString("MMMM d", CultureInfo.InvariantCulture);
                case LandmarkUnit.Month:
                    if (p.Year == c.Year && p.Month == c.Month) return null;
                    return c.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                default:
                    if (p.Year == c.Year) return null;
                    return c.ToString("yyyy", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The landmark for the step the store just took, if playback is running and the step
        /// crossed a boundary. Null while paused and whenever the cursor did not move, and null
        /// when <paramref name="isFirstStep"/>: the sequencer's first NavigateAction jumps the
        /// cursor from wherever the user left it to the plan's start bar, which is not a step
        /// through time, and the start sentence has already named that bar.
        /// </summary>
        public static string? LandmarkForStep(WorkspaceState previous, WorkspaceState current, bool isFirstStep)
        {
            if (!current.IsPlaying || current.IsPaused || !previous.IsPlaying || isFirstStep) return null;
            // The landmark's own switch, subordinate to NarrateDuringPlayback (checked by the
            // caller). Split out on 2026-09-04: the landmark answers WHERE IN TIME the tones are
            // and the signals answer WHAT the indicators printed, and wanting one is not wanting
            // the other — a user scanning for signals does not necessarily want the calendar read
            // to them every few seconds for the length of a run.
            if (!current.SpeakPlaybackLandmarks) return null;
            var data = current.Data;
            if (data == null || data.Count == 0) return null;
            int from = previous.CurrentDataIndex, to = current.CurrentDataIndex;
            if (from == to || from < 0 || to < 0 || from >= data.Count || to >= data.Count) return null;

            var unit = UnitFor(BarSeconds(current), current.PlaybackSpeed);
            return Landmark(data[from].Date, data[to].Date, unit);
        }

        // ── Signals while playing ───────────────────────────────────────────────────

        /// <summary>
        /// Ceiling on signal clauses in one playback utterance.
        ///
        /// <para>
        /// Two, where the bar-close narrator's <c>ScanUtterance</c> allows five, and the
        /// difference is the clock. A bar-close utterance has a whole bar interval to land in; a
        /// playback utterance has 100 ms at 1x before the next bar sounds, and speech that runs
        /// for four seconds is heard over sixty bars of tones it is not about.
        /// </para>
        ///
        /// <para>
        /// <b>What gets dropped is the COMMONEST marker, not the last one scanned</b> — see
        /// <see cref="FireCount"/>. Dropping by scan order is what silenced Cipher B's gold dot
        /// in every playback there has ever been.
        /// </para>
        /// </summary>
        private const int MaxSignalClauses = 2;

        /// <summary>
        /// How many bars of a marker's data carry a value at all — how often it fires across the
        /// whole loaded range. LOWER IS MORE IMPORTANT, and that is the entire ranking rule.
        ///
        /// <para>
        /// <b>The defect this exists for.</b> Reported by Cody, 2026-09-05: <i>"everything reads
        /// fine for cipher b like the wavetrend and bull/bear crosses just not the tripple"</i>.
        /// Cipher B's Triple Confluence Buy is computed INSIDE the Oversold Crossover branch,
        /// which is itself inside the WaveTrend cross branch — so on every gold bar all three
        /// markers are non-NaN, and the scan reaches them in declaration order: cross, then blue,
        /// then gold. With a two-clause ceiling the two routine ones filled it and the gold dot
        /// was dropped on the floor. Not intermittently: on every gold dot, in every playback,
        /// since the feature shipped. The rarest signal on the chart was the one signal playback
        /// could never say.
        /// </para>
        ///
        /// <para>
        /// <b>Why frequency and not a table.</b> A per-indicator importance list would have to be
        /// maintained for every provider including plugin ones, and would be wrong the moment a
        /// user retunes a parameter — Cipher B's gold gate is a <c>GoldMinConfluence</c> setting
        /// away from firing five times as often. How often a marker actually fired on the data in
        /// front of the user is a fact, it is already in the array, and it says what an
        /// importance column would be trying to approximate: an indicator's consequential calls
        /// are its rare ones, and the ones it prints every other cycle are its commentary.
        /// </para>
        ///
        /// <para>
        /// Counted over the whole array rather than the played range so that the ranking does not
        /// change as playback advances — the same bar must rank the same way whether it was
        /// reached from bar 0 or from a Shift+Space restart mid-chart.
        /// </para>
        /// </summary>
        internal static int FireCount(IReadOnlyList<double> data)
        {
            int fires = 0;
            for (int i = 0; i < data.Count; i++)
                if (!double.IsNaN(data[i])) fires++;
            return fires;
        }

        /// <summary>
        /// What one playback step has to say about the markers that fired on it, and how rare the
        /// rarest of them is.
        ///
        /// <para>
        /// The count travels with the text because the rate limit needs it: a marker that fires
        /// eight times in four thousand bars must not be swallowed by the window a marker that
        /// fires four hundred times opened two bars earlier. See
        /// <c>AccessibilityFeedbackCoordinator.PlaybackSpeechForStep</c>.
        /// </para>
        /// </summary>
        /// <param name="Text">The utterance, or null when nothing fired.</param>
        /// <param name="RarestFireCount">Fires of the rarest marker SPOKEN, or
        /// <see cref="int.MaxValue"/> when there is no text — an utterance carrying no signal at
        /// all claims nothing, so it cannot silence one.</param>
        public readonly record struct SignalStep(string? Text, int RarestFireCount)
        {
            public static readonly SignalStep None = new(null, int.MaxValue);
        }

        /// <summary>
        /// The minimum number of bars between two spoken signal utterances at
        /// <paramref name="speed"/> — <see cref="MinSecondsBetweenLandmarks"/> converted into
        /// bars, so signals arrive at the same cadence landmarks do.
        ///
        /// <para>
        /// A signal inside the window is DROPPED, not queued. At ten bars a second a queue is a
        /// backlog: the user would hear a signal about a bar the tones passed eight seconds ago,
        /// with no way to tell which bar it belonged to. Silence is the honest answer, and the
        /// chart can be re-read bar by bar afterwards.
        /// </para>
        /// </summary>
        public static int MinBarsBetweenSignals(float speed)
            // Rounded before the ceiling because the speed arrives as a float: 0.1f widens to
            // 0.100000001490116 as a double, and 2 * 10 * that ceilings to 3 rather than 2. A
            // bar either way is immaterial to the user and very much not immaterial to a test
            // that states the rule, so the arithmetic is made to mean what it says.
            => Math.Max(1, (int)Math.Ceiling(Math.Round(
                MinSecondsBetweenLandmarks * BarsPerSecondAtUnitSpeed * Math.Max(0.1, speed), 6)));

        /// <summary>
        /// What the marker components of the user's narrated series say about the bar playback
        /// has just stepped onto, or null when they say nothing.
        ///
        /// <para>
        /// <b>Which series.</b> Those flagged <see cref="ChartSeries.IsAutoNarrated"/> — N, or
        /// Ctrl+Alt+Shift+N — AND inside the plan that is currently sounding. One mental model
        /// holds everywhere then: <b>N picks WHAT may speak, the Narration tab picks WHEN, and
        /// the scope you played picks WHICH OF THEM.</b> The earlier design scanned every active
        /// visible series, which would have made playback the one place in the terminal where a
        /// series the user never asked to hear from starts talking.
        /// </para>
        ///
        /// <para>
        /// <b>Which components.</b> Marker components carrying a <c>SignalSpeechTemplate</c> —
        /// the discrete calls an indicator was added to make (entry triggers, divergences, breaks
        /// of structure), which is <c>ScanUtterance.TierSignal</c> and nothing below it. No
        /// crosses, no oscillator commentary, no zone lines: Cody's words were "not RSI crossings
        /// or anything like that… just important events", and an oscillator changing zone is the
        /// most frequent thing this terminal can say.
        /// </para>
        ///
        /// <para>
        /// Hidden or muted is skipped, at both levels, the same rule
        /// <c>NavigationFeedbackManager</c>'s cross-series signal scan applies: a component
        /// producing no tone during playback must not be the only thing that speaks.
        /// </para>
        /// </summary>
        /// <param name="state">The state after the step — its <c>ActiveSeries</c> is scanned.</param>
        /// <param name="barIndex">The bar the cursor has just landed on.</param>
        /// <param name="plan">What is actually SOUNDING. Null scans the whole chart, which is
        /// what chart scope means anyway; see the scope note above.</param>
        public static string? SignalsForStep(WorkspaceState state, int barIndex, PlaybackPlan? plan = null)
            => SignalStepFor(state, barIndex, plan).Text;

        /// <inheritdoc cref="SignalsForStep"/>
        /// <summary>
        /// <see cref="SignalsForStep"/> with the rarity of what it chose to say, for the caller
        /// that has to decide whether this utterance may interrupt the rate-limit window.
        /// </summary>
        public static SignalStep SignalStepFor(WorkspaceState state, int barIndex, PlaybackPlan? plan = null)
        {
            if (barIndex < 0) return SignalStep.None;

            // ── SPEECH IS SCOPED THE WAY THE TONES ARE ──────────────────────────────────────
            //
            // Cody, 2026-09-04: "just like sonification per chart/series/component, speech
            // should do the same — if I play back only a series I should hear that narrated".
            //
            // He is describing a bug as a feature request. Space, Shift+Space and the component
            // play all three narrated the WHOLE CHART, because this scan walked ActiveSeries and
            // nothing told it what was playing. Playing one series and hearing another series'
            // signals is not a narrower feature missing — it is the narration describing
            // something the user is not listening to, and on a component play it is the loudest
            // possible contradiction of what the key was for.
            //
            // The plan is the authority, not PlaybackScope, for the same reason the start
            // sentence reads from it: it is what the sequencer was handed. Series scope pins one
            // series; component scope pins one component of it (ComponentFilter, -1 = all).
            var scopedSeries = plan?.Series;
            int componentFilter = plan?.ComponentFilter ?? -1;

            // EVERY candidate is collected before any is dropped. The scan used to stop at the
            // ceiling, which made "which two get spoken" a fact about declaration order in a
            // provider file — and the provider files declare the routine markers first.
            var candidates = new List<(int Fires, int Order, string Component, string Clause)>();
            int order = 0;

            foreach (var series in state.ActiveSeries)
            {
                if (!series.IsAutoNarrated || !series.IsVisible || series.IsMuted) continue;

                // Matched by ID: the plan holds the ChartSeries objects from the state that
                // RESOLVED it, and a later reduction replaces those instances.
                if (scopedSeries != null && !scopedSeries.Any(p => p.Id == series.Id)) continue;

                for (int ci = 0; ci < series.Components.Count; ci++)
                {
                    var comp = series.Components[ci];
                    if (componentFilter >= 0 && ci != componentFilter) continue;
                    if (!comp.IsVisible || comp.IsMuted) continue;
                    if (!SeriesNarrationScope.ComponentNarrates(series, comp)) continue;
                    if (comp.IsZoneLine || comp.UsesGradientSpeech) continue;
                    if (!AudioConstants.MarkerDisplayTypes.Contains(comp.DisplayType)) continue;
                    if (string.IsNullOrEmpty(comp.SignalSpeechTemplate)) continue;

                    var data = series.GetComponentData(comp.Name);
                    if (data == null || barIndex >= data.Length) continue;
                    double val = data[barIndex];
                    if (double.IsNaN(val)) continue;

                    string clause = ExpandSignalTemplate(series, comp, val, state, barIndex);
                    if (!string.IsNullOrWhiteSpace(clause))
                        candidates.Add((FireCount(data), order++, SignalClauseSpeech.ComponentName(comp), clause));
                }
            }

            if (candidates.Count == 0) return SignalStep.None;

            // ── THE RAREST MARKER LEADS, AND THE COMMONEST IS WHAT GETS DROPPED ─────────────
            //
            // Rarity first, then the order the scan found them in so that two markers of equal
            // frequency keep a stable, explainable order. Sorting rather than merely filtering
            // matters for the same reason the ceiling does: on a bar where a gold dot and a wave
            // cross both fire, the first clause is the one that arrives before the next bar's
            // tones start, and it should be the one the user was waiting for.
            var kept = candidates
                .OrderBy(c => c.Fires)
                .ThenBy(c => c.Order)
                .Take(MaxSignalClauses)
                .ToList();

            // ── A SIGNAL IS INTRODUCED BY ITS COMPONENT, NEVER BY ITS SERIES ────────────────
            //
            // Cody, 2026-09-04: "during playback only the signal itself should be read, not
            // prefixed with everything" — and then, 2026-09-05, once the series name had been
            // cut back to the cases where two series spoke in one breath: "hearing only the
            // component name before the signal is all that is needed, not the series name, as
            // the user probably knows what they enabled for narration".
            //
            // Both are the same instruction. Narration is opt-in per series, so WHICH series is
            // speaking is a fact the listener chose; WHICH of its markers fired is the fact they
            // are waiting for. So every clause is introduced by its component — and only when the
            // template has not already said it, because most templates are the component's own
            // name in a sentence and "Bullish Divergence: Bullish divergence" is a stutter. The
            // series name is never spoken here at all.
            return new SignalStep(
                string.Join(" ", kept.Select(c => SignalClauseSpeech.WithComponentName(c.Clause, c.Component))),
                kept[0].Fires);
        }

        /// <summary>
        /// One signal clause: the component's own template, expanded. The series NAME is no longer
        /// added here — <see cref="SignalsForStep"/> decides that, because whether the name is
        /// needed is a fact about the whole utterance and not about one clause. A template that
        /// names the series itself via <c>{series}</c> still does, exactly as
        /// <c>ScanUtterance.Compose</c> treats it.
        /// </summary>
        private static string ExpandSignalTemplate(
            ChartSeries series, ComponentConfig comp, double value, WorkspaceState state, int barIndex)
        {
            string seriesName = SeriesName(series);
            string price = (state.Data != null && barIndex < state.Data.Count)
                ? SpeechPriceFormatter.FormatPrice(state.Data[barIndex].Close)
                : SpeechPriceFormatter.FormatPrice(value);

            string text = comp.SignalSpeechTemplate!
                .Replace("{price}", price)
                .Replace("{value}", value.ToString("F1", CultureInfo.InvariantCulture))
                .Replace("{name}", string.IsNullOrEmpty(comp.DisplayName) ? comp.Name : comp.DisplayName)
                .Replace("{series}", seriesName)
                .Trim();

            if (text.Length == 0) return "";
            // 47 of the 61 shipped templates end without a full stop, which read fine alone and
            // run into the next clause once joined — the same repair ScanUtterance makes.
            if (!".!?".Contains(text[^1])) text += ".";
            return text;
        }

        // ── Dates ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds between bars: the chart's timeframe when it parses, the spacing of the first
        /// two bars when it does not, and a day when there is only one bar to look at.
        /// </summary>
        public static int BarSeconds(WorkspaceState state)
        {
            int fromTimeframe = TimeframeUtility.ToSeconds(state.Identity.Timeframe ?? "");
            if (fromTimeframe > 0) return fromTimeframe;

            var data = state.Data;
            if (data != null && data.Count >= 2)
            {
                double gap = (data[1].Date - data[0].Date).TotalSeconds;
                if (gap > 0) return (int)gap;
            }
            return 86400;
        }

        /// <summary>A bar's date the way the viewport description reads it, with the time of
        /// day added only when bars are closer together than a day.</summary>
        public static string DateText(DateTime stamp, int barSeconds)
            => barSeconds < 86400
                ? SpeechTimeFormatter.Format(stamp, SpeechTimeFormatter.DateTimeFormat)
                : SpeechTimeFormatter.FormatLongDate(stamp);

        private static string CursorDateText(WorkspaceState state)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return "no bar";
            int idx = Math.Clamp(state.CurrentDataIndex, 0, data.Count - 1);
            return DateText(data[idx].Date, BarSeconds(state));
        }
    }
}
