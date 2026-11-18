using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The bar-to-event map, built once per chart data/market/setting and reused on every key
    /// press, the way <see cref="Analysis.ChartPatternCache"/> caches formations.
    ///
    /// <para>
    /// The expensive half — reading the seed, working out a year's expiries and holidays and
    /// matching every event to a bar — does not run again while the chart's bars, timeframe and
    /// market are unchanged. "Scheduled" is the one word that depends on the clock, so it is
    /// overlaid on each read instead of forcing a rebuild when a minute passes.
    /// </para>
    /// </summary>
    public interface IMarketEventBarMapCache
    {
        IReadOnlyList<MarketEventBarPlacement> For(WorkspaceState state, DateTime nowUtc);
    }

    public sealed class MarketEventBarMapCache : IMarketEventBarMapCache
    {
        internal const int MaxEntries = 16;

        private sealed record Entry(int Count, DateTime Last, IReadOnlyList<MarketEventBarPlacement> Placements);

        private readonly MarketEventCalendar _calendar;
        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();

        /// <summary>How many times the map was actually rebuilt. A test reads it to prove reuse.</summary>
        public int BuildCount { get; private set; }

        public MarketEventBarMapCache(MarketEventCalendar? calendar = null)
            => _calendar = calendar ?? new MarketEventCalendar();

        public IReadOnlyList<MarketEventBarPlacement> For(WorkspaceState state, DateTime nowUtc)
        {
            var data = state.Data;
            if (data == null || data.Count == 0) return Array.Empty<MarketEventBarPlacement>();

            int barSeconds = Math.Max(1, Accessibility.PlaybackNarration.BarSeconds(state));
            var market = state.SelectedMarketType;
            var zone = MarketEventChartZone.For(market);
            string key = $"{ChartPatternKey(state)}|{(int)market}|{barSeconds}|{zone.Id}";
            var stamp = (data.Count, data[^1].Date);

            IReadOnlyList<MarketEventBarPlacement> placements;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var hit)
                    && hit.Count == stamp.Count && hit.Last == stamp.Date)
                {
                    placements = hit.Placements;
                }
                else
                {
                    placements = Build(data, barSeconds, market, zone);
                    BuildCount++;
                    if (!_entries.ContainsKey(key)) _order.Enqueue(key);
                    _entries[key] = new Entry(stamp.Count, stamp.Date, placements);
                    while (_order.Count > MaxEntries)
                    {
                        string evict = _order.Dequeue();
                        if (evict != key) _entries.Remove(evict);
                    }
                }
            }

            return ApplyScheduled(placements, data.Count, nowUtc);
        }

        private IReadOnlyList<MarketEventBarPlacement> Build(
            IReadOnlyList<Ohlcv> data,
            int barSeconds,
            Sdk.Enums.MarketType market,
            TimeZoneInfo zone)
        {
            var first = data[0].Date;
            var last = data[^1].Date;
            var events = _calendar.Between(first.AddDays(-2), last.AddDays(100), market);

            // DateTime.MaxValue as "now" keeps the base map free of the one clock-dependent word.
            return MarketEventPlacement.Place(events, data, barSeconds, zone, DateTime.MaxValue);
        }

        /// <summary>Rule 7's second sentence, applied at read time so the map does not go stale.</summary>
        private static IReadOnlyList<MarketEventBarPlacement> ApplyScheduled(
            IReadOnlyList<MarketEventBarPlacement> placements,
            int barCount,
            DateTime nowUtc)
        {
            if (placements.Count == 0) return placements;
            bool any = false;
            var result = new List<MarketEventBarPlacement>(placements.Count);
            foreach (var p in placements)
            {
                if (p.IsPlaced && p.Placement == null && p.BarIndex == barCount - 1
                    && p.Event.MomentUtc > nowUtc)
                {
                    result.Add(p with { Placement = MarketEventPlacement.Scheduled });
                    any = true;
                }
                else
                {
                    result.Add(p);
                }
            }
            return any ? result : placements;
        }

        private static string ChartPatternKey(WorkspaceState state)
            => Analysis.ChartPatternCache.KeyFor(state.Identity);
    }
}
