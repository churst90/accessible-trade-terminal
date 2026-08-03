using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Interfaces;
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

        public ChartPatternNavigator(IWorkspaceStore store, IEventBus eventBus,
            IChartPatternCache patterns, IChartPatternFocus? focus = null)
        {
            _store = store;
            _eventBus = eventBus;
            _patterns = patterns;
            _focus = focus ?? new ChartPatternFocus();
        }

        /// <summary>
        /// Semicolon — pin the next overlapping formation at this bar, so it leads the readout.
        ///
        /// <para>
        /// Cycles rather than opening a picker. A picker would be a modal, and the whole point of
        /// this is to change what the next arrow key says without leaving the chart. Pressing it
        /// repeatedly walks the overlapping set, which is short by construction — the ranking exists
        /// precisely because a bar rarely carries more than three or four.
        /// </para>
        /// </summary>
        public void CycleFocus()
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
            var here = ChartPatternNarrator.ByDominance(
                ChartPatternNarrator.AtBar(_patterns.For(state.Identity, data), idx)).ToList();

            if (here.Count == 0)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Boundary,
                    "No chart formation at this bar to choose from."));
                return;
            }
            if (here.Count == 1)
            {
                _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                    $"Only one formation here: {ChartPatternNarrator.Name(here[0].Kind)}."));
                return;
            }

            string chartKey = ChartPatternCache.KeyFor(state.Identity);
            var picked = _focus.CycleAt(chartKey, here);
            if (picked == null) return;

            int position = here.FindIndex(p => p.Key.Equals(picked.Key)) + 1;
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                $"Leading with {ChartPatternNarrator.Name(picked.Kind)}, {position} of {here.Count}. "
              + ChartPatternNarrator.Describe(picked, SpeechPriceFormatter.FormatPrice)));
        }

        /// <summary>Shift+semicolon — drop the pin and go back to the size ranking.</summary>
        public void ClearFocus()
        {
            string chartKey = ChartPatternCache.KeyFor(_store.State.Identity);
            bool had = _focus.Clear(chartKey);

            // Answers either way. "Nothing was pinned" is as useful as "cleared" when the user is
            // pressing the key precisely because they are unsure what state they are in.
            _eventBus.Publish(new FeedbackRequestEvent(FeedbackType.Info,
                had ? "Formation choice cleared; back to largest first."
                    : "No formation was pinned."));
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
