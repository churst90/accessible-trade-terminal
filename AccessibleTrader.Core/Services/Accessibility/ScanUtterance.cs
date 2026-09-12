namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Collects everything a single narration scan found and composes it into ONE phrase.
    ///
    /// <para>
    /// <b>Why.</b> The bar-close narrator used to make up to nine separate <c>Speak</c> calls
    /// inside a single <c>RedrawEvent</c> handler — a marker signal, a broken level, a level
    /// tested again, an approach, a cross, a cloud entry or exit, an oscillator zone change and
    /// an oscillator crossover. On the web head speech is delivered by assigning
    /// <c>MainLayout</c>'s <c>_latestSpeech</c> field, and Blazor batches an entire handler into
    /// one render, so the field was assigned nine times and only the last value ever reached the
    /// DOM. The other eight were never muted or filtered — they were overwritten before a screen
    /// reader could read any of them, and the one that survived was whichever happened to be
    /// last, not whichever mattered. On the desktop head the failure inverts: all nine queue and
    /// the listener cannot get out from under them.
    /// </para>
    ///
    /// <para>
    /// This is the same defect <c>NavigationFeedbackManager</c> was fixed for — its "ONE
    /// UTTERANCE PER BAR" comment describes it exactly — and the narrator was not fixed with it.
    /// Composing first and speaking once is the only arrangement correct on every head, and a
    /// single utterance cannot cut itself off half way through.
    /// </para>
    ///
    /// <para>
    /// <b>The order.</b> Ordering is not cosmetic in an audio interface. The narrator runs in the
    /// background while the user is doing something else, so whatever is most consequential has
    /// to arrive in the opening syllables or it is heard after attention has moved on. So: a level
    /// that has ceased to exist, then price changing side of one, then the indicator's own
    /// discrete signal, then a level tested again, then an approach to something that has not
    /// happened yet, and last the oscillator commentary — which is the most frequent thing this
    /// narrator says and therefore the least worth leading with.
    /// </para>
    ///
    /// <para>
    /// <b>The series name.</b> Every clause is built already carrying "<c>{series}: </c>", which
    /// read correctly when each was its own utterance and reads as a stutter once they are
    /// joined. The prefix is dropped from a clause whose series is the same as the previous
    /// clause's, so a name is spoken once per run of clauses about that series and again whenever
    /// the utterance moves to another one. Clauses from a user-authored
    /// <c>SignalSpeechTemplate</c> simply do not match the prefix and are left alone.
    /// </para>
    ///
    /// <para>
    /// Lifted out of <c>AutoNarrationService</c> on 2026-09-11 so the same ladder can be composed
    /// with no browser attached (<see cref="HeadlessChart"/>): one composer, one order,
    /// one cap, whichever process is speaking.
    /// </para>
    /// </summary>
    internal sealed class ScanUtterance
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
        /// The plain value of a series that narrates by reading rather than by signal — a
        /// volume bar at the close. Last, because it is said on EVERY bar: it is the thing
        /// the cap should drop first when a bar has real news, and the thing that fills the
        /// silence when it does not. See <see cref="SeriesNarrationScope.ReadingComponent"/>.
        /// </summary>
        public const int TierReading = 7;

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
        /// "{seriesId}:{componentName}" — the key convention used by the scanner's tracking
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
        /// In one breath — <i>price crossed above R1 at 103.50, approaching R1 at 103.50</i> —
        /// it is a contradiction: you are not approaching a level you are already past.
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
}
