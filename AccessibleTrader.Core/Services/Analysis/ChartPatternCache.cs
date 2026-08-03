using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Chart-pattern detection results for the loaded dataset, computed once and shared.
    ///
    /// <para>
    /// Detection is O(swings²) over the whole series. Three separate features want the same answer
    /// on the same bars — the navigation announcement on every arrow key, the detailed summary, and
    /// the comma/period jump keys — and each was about to keep its own cache. Three caches of one
    /// derived value is three chances for them to disagree about what is on the chart, which by ear
    /// is indistinguishable from a bug in the detector.
    /// </para>
    ///
    /// <para>
    /// ── THE KEY MUST IDENTIFY THE CHART, NOT JUST THE DATA ──────────────────────
    /// The first version keyed on <c>(bar count, last bar timestamp)</c> and shipped a cross-chart
    /// leak. Open TAO 4h in one tab and BTC 4h in another: both load the same default number of
    /// bars, and because crypto trades continuously the most recent 4-hour bar carries the
    /// <b>identical</b> timestamp on both. The key collided exactly, so the second chart was handed
    /// the first chart's formations and described a shape that was not on it.
    /// </para>
    ///
    /// <para>
    /// It is worth naming why that was so easy to miss: the key looked like it identified the data,
    /// and for a single chart it did. Two charts of the same asset class and timeframe are precisely
    /// the case a bar count and a close time cannot separate — and it is also the most ordinary
    /// thing a user does. The key now leads with the chart's <see cref="ChartIdentity"/>, so two
    /// different instruments cannot occupy the same entry however similar their data.
    /// </para>
    ///
    /// <para>
    /// ── AND IT HOLDS MORE THAN ONE ─────────────────────────────────────────────
    /// A single-entry cache would be correct after the fix but would recompute on every tab switch,
    /// turning an alt-tab between two charts into a repeated O(swings²) scan. Entries are kept per
    /// chart, bounded, so moving between open tabs is free.
    /// </para>
    /// </summary>
    public interface IChartPatternCache
    {
        /// <summary>
        /// Every pattern in <paramref name="bars"/> for the chart identified by
        /// <paramref name="identity"/>, recomputing only when that chart's data has changed.
        /// Returns an empty list for a series too short to hold a formation.
        /// </summary>
        IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars);
    }

    public sealed class ChartPatternCache : IChartPatternCache
    {
        /// <summary>
        /// Below this there is not enough history for a formation plus its confirmation lag, and
        /// the detector would spend the work to return nothing.
        /// </summary>
        internal const int MinimumBars = 30;

        /// <summary>
        /// How many charts to remember. Comfortably above the number of tabs anyone drives by
        /// keyboard, and small enough that a stale entry costs nothing.
        /// </summary>
        internal const int MaxEntries = 16;

        private readonly IChartPatternDetector _detector;
        private readonly object _gate = new();

        /// <summary>Insertion-ordered so the oldest entry can be evicted without a timestamp.</summary>
        private readonly Dictionary<string, (int Count, DateTime Last, IReadOnlyList<ChartPattern> Patterns)> _entries = new();
        private readonly Queue<string> _order = new();

        public ChartPatternCache(IChartPatternDetector detector) => _detector = detector;

        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars)
        {
            if (bars == null || bars.Count < MinimumBars) return Array.Empty<ChartPattern>();

            string id = KeyFor(identity);
            var stamp = (bars.Count, bars[^1].Date);

            // Live bars and keyboard navigation arrive on different threads on the desktop heads;
            // an unguarded read could hand out a list mid-replacement.
            lock (_gate)
            {
                if (_entries.TryGetValue(id, out var hit) && hit.Count == stamp.Count && hit.Last == stamp.Date)
                    return hit.Patterns;

                var patterns = _detector.Detect(bars);

                if (!_entries.ContainsKey(id)) _order.Enqueue(id);
                _entries[id] = (stamp.Count, stamp.Date, patterns);

                while (_order.Count > MaxEntries)
                {
                    string evict = _order.Dequeue();
                    if (evict != id) _entries.Remove(evict);
                }

                return patterns;
            }
        }

        /// <summary>
        /// The cache key for a chart.
        ///
        /// <para>
        /// All four identity fields participate. Symbol alone is not enough — the same ticker on two
        /// providers can carry different history, and the same symbol at two timeframes is two
        /// different charts entirely.
        /// </para>
        /// </summary>
        internal static string KeyFor(ChartIdentity id)
            => $"{id.Market}|{id.Provider}|{id.Symbol}|{id.Timeframe}";
    }
}
