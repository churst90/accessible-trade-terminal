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
    /// The pin is held as a <see cref="ChartPattern.Identity"/> rather than a record, because the
    /// record is re-derived on every bar (the narrator projects each formation to the state it held
    /// at the bar being described) while the Identity is stable across those projections.
    /// </para>
    ///
    /// <para>
    /// It used to be the <see cref="ChartPattern.Key"/>, which is built from bar INDICES and so is
    /// not stable across a re-detection that moves them. A live feed at its growth cap sheds its
    /// oldest bar on every new one, and a history backfill prepends; either shifts every index on
    /// the chart, the pinned Key matched nothing afterwards, and the pin was silently gone — the
    /// next semicolon started over and said "2 of 2" again. Measured on the BTC/USDT daily
    /// snapshot at the 2,000-bar live cap. The Identity is the formation's kind and its two dates,
    /// which no index shift touches.
    /// </para>
    /// </summary>
    public interface IChartPatternFocus
    {
        /// <summary>Reorder so the pinned formation leads, if one is pinned and present.</summary>
        IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked);

        /// <summary>
        /// Pin the next formation in the ranked order, wrapping around. "Next" is counted from the
        /// one currently leading: the pinned formation if it is in <paramref name="ranked"/>,
        /// otherwise the first. Returns the newly pinned pattern, or null when there is nothing at
        /// this bar to pin.
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
        private readonly Dictionary<string, (ChartPatternKind, DateTime, DateTime)> _pinned = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public IReadOnlyList<ChartPattern> Apply(string chartKey, IReadOnlyList<ChartPattern> ranked)
        {
            if (ranked.Count <= 1) return ranked;

            lock (_gate)
            {
                if (!_pinned.TryGetValue(chartKey, out var key)) return ranked;

                int i = -1;
                for (int n = 0; n < ranked.Count; n++)
                    if (ranked[n].Identity.Equals(key)) { i = n; break; }

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
                // With nothing pinned here, the LEADER is current — it is the one the readout is
                // already naming. Starting from "none" made the first press pin the leader and say
                // "1 of 2": the formation the user had just heard, so the key appeared to do
                // nothing. The first press now moves to the second formation, as "next" promises.
                int current = 0;
                if (_pinned.TryGetValue(chartKey, out var key))
                    for (int n = 0; n < ranked.Count; n++)
                        if (ranked[n].Identity.Equals(key)) { current = n; break; }

                int next = (current + 1) % ranked.Count;
                _pinned[chartKey] = ranked[next].Identity;
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
                    if (c.Identity.Equals(key)) return c;
                return null;
            }
        }
    }
}
