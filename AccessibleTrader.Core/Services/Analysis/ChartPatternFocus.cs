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
    /// Choosing solves that without the application acquiring an opinion. The user says "this one",
    /// and it leads while they are on its bars. Nothing is scored, nothing is hidden — the others are
    /// still counted and still readable with the detail key.
    /// </para>
    ///
    /// <para>
    /// <b>The choice belongs to the bars, not the chart (Cody, 2026-11-13).</b> It used to be a
    /// pin that lasted until Shift+semicolon released it, and while it lasted comma and period
    /// visited only the pinned formation's two edges — so after choosing a double top on TAO/USDT
    /// 1h the jump keys could reach nothing else, and his words were "I'm still not understanding
    /// why i need to pin a chart pattern ... I thought ; just cycled between the possible chart
    /// patterns." Now semicolon and Shift+semicolon only step forward and back through the
    /// formations at the bar, the jump keys always reach every formation, and the readout drops
    /// the choice by itself (<see cref="Clear"/>) on the first bar the chosen formation is not on.
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
        /// Choose the next formation in the ranked order (the previous one when
        /// <paramref name="forward"/> is false), wrapping around. Counted from the one currently
        /// leading: the chosen formation if it is in <paramref name="ranked"/>, otherwise the
        /// first. Returns the newly chosen pattern, or null when there is nothing at this bar.
        /// </summary>
        ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked, bool forward = true);

        /// <summary>Clear the pin for this chart. Returns true if one was actually cleared.</summary>
        bool Clear(string chartKey);

        /// <summary>Whether this chart currently has a pinned formation.</summary>
        bool IsPinned(string chartKey);

        /// <summary>
        /// The pinned formation as it appears among <paramref name="candidates"/>, or null when
        /// nothing is pinned or the pinned shape is not in that set.
        ///
        /// <para>
        /// The readout asks this with the formations at the bar it is describing, and drops the
        /// choice when the answer is null: the user has moved off the formation they chose.
        /// (It once scoped the jump keys to the pinned formation's two edges instead; see the
        /// type's remarks for why that went.)
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

        public ChartPattern? CycleAt(string chartKey, IReadOnlyList<ChartPattern> ranked, bool forward = true)
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

                int next = ((forward ? current + 1 : current - 1) % ranked.Count + ranked.Count) % ranked.Count;
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
