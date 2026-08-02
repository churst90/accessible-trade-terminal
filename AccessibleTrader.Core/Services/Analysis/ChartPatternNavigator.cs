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

        public ChartPatternNavigator(IWorkspaceStore store, IEventBus eventBus, IChartPatternCache patterns)
        {
            _store = store;
            _eventBus = eventBus;
            _patterns = patterns;
        }

        public void Jump(SystemCommand command)
        {
            bool forward = command == SystemCommand.NavPatternNext;
            var state = _store.State;
            var data = state.Data;
            if (data == null || data.Count == 0) return;

            var all = _patterns.For(data);
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
