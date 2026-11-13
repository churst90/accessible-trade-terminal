using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Comma / period: step between chart formations.
    ///
    /// <para>
    /// Reading a chart by ear is slow, and a formation is one of the few things on it worth
    /// travelling to directly. Arrowing bar by bar through four hundred bars to find out whether a
    /// triangle ever broke is the kind of task that makes an audio interface feel like a punishment;
    /// two keys that land on exactly the bars where something happened is the difference.
    /// </para>
    ///
    /// <para>
    /// <b>The stops are EDGES, not patterns.</b> Each formation contributes two: the bar it first
    /// became knowable, and the bar its story ended — the break, or the point it aged out. Landing
    /// on those two bars walks the user through the same narrative the navigation announcements
    /// give them ("start of…", then "…confirmed here"), so the jump key and the arrow keys agree
    /// about what is worth saying. Stopping only at starts would make it impossible to answer the
    /// question people actually have, which is how the thing turned out.
    /// </para>
    /// </summary>
    public sealed class ChartPatternNavigator
    {
        private readonly IWorkspaceStore _store;
        private readonly IEventBus _eventBus;
        private readonly IChartPatternCache _patterns;
        private readonly IChartPatternFocus _focus;

        // The focus is REQUIRED, not optional-with-a-fallback. It used to default to a private
        // `new ChartPatternFocus()`, which meant that if the DI registration were ever dropped this
        // class would pin into an instance nobody else reads: semicolon would announce "leading
        // with …" and the readout would never change, with nothing failing anywhere. A missing
        // registration should be a startup error, not a feature that half works.
        public ChartPatternNavigator(IWorkspaceStore store, IEventBus eventBus,
            IChartPatternCache patterns, IChartPatternFocus focus)
        {
            _store = store;
            _eventBus = eventBus;
            _patterns = patterns;
            _focus = focus;
        }

        /// <summary>
        /// Semicolon — choose the next overlapping formation at this bar, so it leads the readout;
        /// Shift+semicolon the previous one (with two, both keys swap them). The choice lasts while
        /// the cursor is on that formation's bars and never limits comma and period.
        ///
        /// <para>
        /// Cycles rather than opening a picker. A picker would be a modal, and the whole point of
        /// this is to change what the next arrow key says without leaving the chart. Pressing it
        /// repeatedly walks the overlapping set, which is short by construction — the ranking exists
        /// precisely because a bar rarely carries more than three or four.
        /// </para>
        /// </summary>
        public void CycleFocus(bool forward = true)
        {
            var state = _store.State;
            var data = state.Data;
            if (data == null || data.Count == 0) return;

            if (!state.DescribeChartPatterns)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary,
                    "Chart formation description is off. Turn it on in Settings to use these keys."));
                return;
            }

            int idx = state.CurrentDataIndex;
            var all = _patterns.For(state.Identity, data);
            var here = ChartPatternNarrator.ByDominance(ChartPatternNarrator.AtBar(all, idx)).ToList();

            if (here.Count == 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary,
                    "No chart formation at this bar to choose from."));
                return;
            }
            string chartKey = ChartPatternCache.KeyFor(state.Identity);

            // One formation and nothing else chosen: it is already the one read. With a choice
            // elsewhere on the chart, the lone formation here is a real change of choice.
            if (here.Count == 1 && (!_focus.IsPinned(chartKey) || _focus.PinnedIn(chartKey, here) != null))
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                    $"Only one formation here: {ChartPatternNarrator.Name(here[0].Kind)}."));
                return;
            }

            // Counted from what a landing here reads, so the first press always moves off it.
            var reading = ChartPatternNarrator.Landing(here, idx, null);
            var picked = _focus.CycleAt(chartKey, here, forward, reading == null ? 0 : here.IndexOf(reading));
            if (picked == null) return;
            // The choice is drawn too (ChartFrameRenderer leads the formation layer with it), and
            // nothing in the store changed, so the chart has to be told to repaint.
            _eventBus.Publish(new RedrawEvent());

            // The containment clause is what tells nested formations apart by ear: "2 of 3" alone
            // does not say whether the shape now leading is the parent or a piece of it. Reported
            // from live use 2026-10-09 ("I can't tell which formation is being changed").
            int position = here.FindIndex(p => p.Key.Equals(picked.Key)) + 1;
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                $"Leading with {ChartPatternNarrator.Name(picked.Kind)}, {position} of {here.Count}. "
              + ChartPatternNarrator.Describe(picked, SpeechPriceFormatter.FormatPrice)
              + ChartPatternNarrator.DescribeContainment(picked, all, idx)));
        }

        public void Jump(SystemCommand command)
        {
            bool forward = command == SystemCommand.NavPatternNext;
            var state = _store.State;
            var data = state.Data;
            if (data == null || data.Count == 0) return;

            // ── Refuse rather than move silently ────────────────────────────────
            //
            // These keys used to work with formation description switched off: the cursor jumped to
            // the right bar and then said nothing about why, because the announcement that would
            // have explained the jump is the thing the setting disables. Being teleported across a
            // chart with no explanation is worse than the key doing nothing — the user cannot tell
            // whether it worked, and has lost their place either way.
            if (!state.DescribeChartPatterns)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    "Chart formation description is off. Turn it on in Settings to use these keys."));
                return;
            }

            var all = _patterns.For(state.Identity, data);
            if (all.Count == 0)
            {
                // Never silent. "There are none" and "the key did nothing" are indistinguishable by
                // ear, and the second is the one users report as a bug.
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary, "No chart formations on this chart."));
                return;
            }

            // Every formation's edges, whatever semicolon chose. A chosen formation used to confine
            // these keys to its own two edges until Shift+semicolon released it, which left Cody
            // unable to reach any other formation on TAO/USDT 1h (2026-11-13). Semicolon now only
            // decides which formation at the bar is described; travel is never limited by it.
            int target = NextEdge(all, state.CurrentDataIndex, forward, data.Count);
            if (target < 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(
                    FeedbackType.Boundary,
                    forward ? "No further chart formations." : "No earlier chart formations."));
                return;
            }

            _store.Dispatch(new NavigateAction(target));
            _store.Dispatch(new SetInteractionContextAction(InteractionContext.Component));

            // Routed as ordinary navigation so the bar is read exactly as it would be if the user
            // had arrowed onto it — including the formation clause, which the coordinator composes
            // for any X move. A bespoke announcement here would drift out of step with that wording.
            _eventBus.Publish(new FeedbackRequestEvent(
                FeedbackType.Navigation, "", true, IsXMove: true));
        }

        /// <summary>
        /// The nearest formation edge strictly beyond <paramref name="from"/>, or -1.
        /// </summary>
        internal static int NextEdge(IReadOnlyList<ChartPattern> all, int from, bool forward, int barCount)
        {
            var edges = Edges(all, barCount);
            return forward
                ? edges.Where(e => e > from).DefaultIfEmpty(-1).Min()
                : edges.Where(e => e < from).DefaultIfEmpty(-1).Max();
        }

        /// <summary>
        /// Every bar worth stopping on, deduplicated and clamped to the loaded series.
        ///
        /// <para>
        /// A resolution bar past the end of the data is dropped rather than clamped to the last
        /// bar: a formation still open at the right-hand edge has not resolved, and offering its
        /// notional expiry bar as a stop would invent an event that has not happened.
        /// </para>
        /// </summary>
        internal static IReadOnlyList<int> Edges(IReadOnlyList<ChartPattern> all, int barCount)
        {
            var set = new SortedSet<int>();
            foreach (var p in all)
            {
                if (p.KnownAtIndex >= 0 && p.KnownAtIndex < barCount) set.Add(p.KnownAtIndex);
                int end = p.ResolvesAt;
                if (end > p.KnownAtIndex && end < barCount) set.Add(end);
            }
            return set.ToList();
        }
    }
}
