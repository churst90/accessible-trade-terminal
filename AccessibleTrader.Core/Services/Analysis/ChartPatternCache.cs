using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Analysis
{
    /// <summary>
    /// Chart-pattern detection results for the loaded dataset, computed once and shared.
    ///
    /// <para>
    /// Detection is O(swings²) over the whole series. Three separate features now want the same
    /// answer on the same bars — the navigation announcement on every arrow key, the detailed
    /// summary, and the comma/period jump keys — and each was about to keep its own cache. Three
    /// caches of one derived value is three chances for them to disagree about what is on the
    /// chart, which by ear is indistinguishable from a bug in the detector.
    /// </para>
    ///
    /// <para>
    /// The key is the bar count plus the last bar's timestamp: cheap to compute, and it changes on
    /// load, on timeframe switch, on symbol switch, and when a live bar closes — every occasion
    /// where the answer could differ.
    /// </para>
    /// </summary>
    public interface IChartPatternCache
    {
        /// <summary>
        /// Every pattern in <paramref name="bars"/>, recomputing only when the data has changed.
        /// Returns an empty list for a series too short to hold a formation.
        /// </summary>
        IReadOnlyList<ChartPattern> For(IReadOnlyList<Ohlcv>? bars);
    }

    public sealed class ChartPatternCache : IChartPatternCache
    {
        /// <summary>
        /// Below this there is not enough history for a formation plus its confirmation lag, and
        /// the detector would spend the work to return nothing.
        /// </summary>
        internal const int MinimumBars = 30;

        private readonly IChartPatternDetector _detector;
        private readonly object _gate = new();

        private IReadOnlyList<ChartPattern> _cached = Array.Empty<ChartPattern>();
        private (int Count, DateTime Last) _key;

        public ChartPatternCache(IChartPatternDetector detector) => _detector = detector;

        public IReadOnlyList<ChartPattern> For(IReadOnlyList<Ohlcv>? bars)
        {
            if (bars == null || bars.Count < MinimumBars) return Array.Empty<ChartPattern>();

            var key = (bars.Count, bars[^1].Date);

            // Live bars and keyboard navigation arrive on different threads on the desktop heads;
            // an unguarded read could hand out a list mid-replacement.
            lock (_gate)
            {
                if (_key != key)
                {
                    _cached = _detector.Detect(bars);
                    _key = key;
                }
                return _cached;
            }
        }
    }
}
