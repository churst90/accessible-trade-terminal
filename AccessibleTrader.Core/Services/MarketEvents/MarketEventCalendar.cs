using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The market's event calendar: the shipped seed plus the rule-worked expiries, holidays and
    /// halvings, for a moment range and a market.
    ///
    /// <para>
    /// It never returns null. A range with nothing in it is an empty list, and the caller says
    /// nothing rather than an empty section.
    /// </para>
    /// </summary>
    public sealed class MarketEventCalendar
    {
        private readonly IReadOnlyList<MarketEvent> _seed;
        private readonly IMarketEventFigureFeed? _feed;

        /// <summary>A calendar over the embedded seed and the live feed, or over a supplied set for a test.</summary>
        public MarketEventCalendar(IReadOnlyList<MarketEvent>? seed = null, IMarketEventFigureFeed? feed = null)
        {
            _seed = seed ?? MarketEventSeed.Load().Events;
            _feed = feed;
        }

        /// <summary>Every event that touches <paramref name="market"/> and starts in the half-open range.</summary>
        public IReadOnlyList<MarketEvent> Between(
            DateTime fromUtc,
            DateTime toUtc,
            MarketType market)
            => Between(fromUtc, toUtc, new[] { market });

        /// <summary>
        /// Every event that touches ANY of <paramref name="markets"/> and starts in the half-open
        /// range. One pass over the seed and the rules, not one pass per market: the live service
        /// asks this every second and must not walk the calendar fourteen times.
        /// </summary>
        public IReadOnlyList<MarketEvent> Between(
            DateTime fromUtc,
            DateTime toUtc,
            IReadOnlyCollection<MarketType> markets)
        {
            if (toUtc <= fromUtc || markets.Count == 0) return Array.Empty<MarketEvent>();

            bool TouchesAny(MarketEvent e) => e.Scope.Any(markets.Contains);

            var events = new List<MarketEvent>();
            var fetched = _feed?.Events ?? Array.Empty<MarketEvent>();

            // A fetched date or figure WINS over the seed for the same id; seed dates are
            // never removed, and a source with no fetched history leaves the seed alone.
            if (fetched.Count == 0)
            {
                foreach (var e in _seed)
                    if (TouchesAny(e) && InRange(e.MomentUtc, fromUtc, toUtc))
                        events.Add(e);
            }
            else
            {
                var fetchedIds = new HashSet<string>(fetched.Select(e => e.Id), StringComparer.Ordinal);
                foreach (var e in _seed)
                    if (!fetchedIds.Contains(e.Id)
                        && TouchesAny(e) && InRange(e.MomentUtc, fromUtc, toUtc))
                        events.Add(e);
                foreach (var e in fetched)
                    if (TouchesAny(e) && InRange(e.MomentUtc, fromUtc, toUtc))
                        events.Add(e);
            }

            // A rule event's own date can sit a few hours outside the UTC window used to find it
            // (a New York midnight is 04:00/05:00Z), so a margin year on either side keeps a
            // New Year's Eve or New Year's Day event from being missed at the edge of the data.
            for (int year = fromUtc.Year - 1; year <= toUtc.Year + 1; year++)
                foreach (var e in MarketEventRules.ForYear(year))
                    if (TouchesAny(e) && InRange(e.MomentUtc, fromUtc, toUtc)
                        && !events.Any(x => x.Id == e.Id))
                        events.Add(e);

            events.Sort(Compare);
            return events;
        }

        /// <summary>The same, filtered by the level and kind settings (Part 3).</summary>
        public IReadOnlyList<MarketEvent> Between(
            DateTime fromUtc,
            DateTime toUtc,
            MarketType market,
            MarketEventLevel level,
            MarketEventKindFilter kinds)
        {
            if (level == MarketEventLevel.Off) return Array.Empty<MarketEvent>();
            var all = Between(fromUtc, toUtc, market);
            var filtered = new List<MarketEvent>(all.Count);
            foreach (var e in all)
                if (MarketEventLevels.Includes(level, e.Importance) && kinds.Includes(e.Kind))
                    filtered.Add(e);
            return filtered;
        }

        /// <summary>
        /// Moment, then importance (high first), then spoken name — a stable order the speech
        /// layer and the jump keys can both rely on.
        /// </summary>
        internal static int Compare(MarketEvent a, MarketEvent b)
        {
            int byMoment = a.MomentUtc.CompareTo(b.MomentUtc);
            if (byMoment != 0) return byMoment;
            int byImportance = a.Importance.CompareTo(b.Importance);
            if (byImportance != 0) return byImportance;
            return string.CompareOrdinal(a.SpokenName, b.SpokenName);
        }

        private static bool InRange(DateTime moment, DateTime fromUtc, DateTime toUtc)
            => moment >= AsUtc(fromUtc) && moment < AsUtc(toUtc);

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
