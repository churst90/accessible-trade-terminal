using System;
using System.Collections.Generic;
using System.Linq;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Which of several overlapping formations the user has chosen to hear about.
    ///
    /// <para>
    /// A region can satisfy four definitions at once, and the terminal ranks them by size because
    /// that is the only tie-break available which is not a directional opinion. But size is not
    /// always what a trader cares about: the twelve-bar flag inside the eighty-bar triangle may be
    /// exactly the thing their setup is built on, and having the terminal insist on naming the
    /// triangle every time is the software substituting its ordering for their thesis.
    /// </para>
    ///
    /// <para>
    /// Pinning solves that without the application acquiring an opinion. The user says "this one",
    /// and it leads until they say otherwise. Nothing is scored, nothing is hidden — the others are
    /// still counted and still readable with the detail key.
    /// </para>
    ///
    /// <para>
    /// The pin is held as a <see cref="ChartPattern.Key"/> rather than a record, because the record
    /// is re-derived on every bar (the narrator projects each formation to the state it held at the
    /// bar being described) while the Key is stable across those projections and across a
    /// re-detection triggered by a new live bar.
    /// </para>
    /// </summary>
    public interface IChartPatternFocus
    {
        /// <summary>Reorder so the pinned formation leads, if one is pinned and present.</summary>
        IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked);

        /// <summary>
        /// Pin the next formation in the ranked order, wrapping around. Returns the newly pinned
        /// pattern, or null when there is nothing at this bar to pin.
        /// </summary>
        ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked);

        /// <summary>Clear the pin for this chart. Returns true if one was actually cleared.</summary>
        bool Clear(string chartKey);

        /// <summary>Whether this chart currently has a pinned formation.</summary>
        bool IsPinned(string chartKey);

        /// <summary>
        /// The pinned formation as it appears among <paramref name="candidates"/>, or null when
        /// nothing is pinned or the pinned shape is not in that set.
        ///
        /// <para>
        /// Exists so the JUMP keys can respect the pin. Reordering a readout was only half of what
        /// pinning has to mean: a user who pins a formation and then presses the next-formation key
        /// is asking to travel to <i>that</i> formation's edges, and the keys used to compute their
        /// stops from every pattern on the chart. The result was a pin that changed which shape was
        /// described but not which shape you landed on — so "leading with ascending triangle" was
        /// followed, one keypress later, by "double bottom confirmed here". Reported from live use.
        /// </para>
        /// </summary>
        ChartPattern? PinnedIn(string chartKey, IEnumerable<ChartPattern> candidates);
    }

    public sealed class ChartPatternFocus : IChartPatternFocus
    {
        // Per chart, for the same reason the detection cache is per chart: a pin is a statement
        // about one instrument's structure and means nothing on another.
        private readonly Dictionary<string, (ChartPatternKind, int, int, int)> _pinned = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked)
        {
            if (ranked.Count <= 1) return ranked;

            lock (_gate)
            {
                if (!_pinned.TryGetValue(chartKey, out var key)) return ranked;

                int i = -1;
                for (int n = 0; n < ranked.Count; n++)
                    if (ranked[n].Key.Equals(key)) { i = n; break; }

                // A pinned formation that is not at this bar is not an error and must not clear the
                // pin: the user is simply somewhere else on the chart, and walking back should find
                // their choice still in force.
                if (i <= 0) return ranked;

                var reordered = new List<ChartPattern>(ranked.Count) { ranked[i] };
                for (int n = 0; n < ranked.Count; n++)
                    if (n != i) reordered.Add(ranked[n]);
                return reordered;
            }
        }

        public ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked)
        {
            if (ranked.Count == 0) return null;

            lock (_gate)
            {
                int current = -1;
                if (_pinned.TryGetValue(chartKey, out var key))
                    for (int n = 0; n < ranked.Count; n++)
                        if (ranked[n].Key.Equals(key)) { current = n; break; }

                int next = (current + 1) % ranked.Count;
                _pinned[chartKey] = ranked[next].Key;
                return ranked[next];
            }
        }

        public bool Clear(string chartKey)
        {
            lock (_gate) return _pinned.Remove(chartKey);
        }

        public bool IsPinned(string chartKey)
        {
            lock (_gate) return _pinned.ContainsKey(chartKey);
        }

        public ChartPattern? PinnedIn(string chartKey, IEnumerable<ChartPattern> candidates)
        {
            lock (_gate)
            {
                if (!_pinned.TryGetValue(chartKey, out var key)) return null;
                foreach (var c in candidates)
                    if (c.Key.Equals(key)) return c;
                return null;
            }
        }
    }
}
