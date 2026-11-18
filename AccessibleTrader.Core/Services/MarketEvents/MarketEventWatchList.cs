using System.Globalization;

namespace AccessibleTrader.Core.Services.MarketEvents
{
    /// <summary>
    /// The watched-event ids: "alert me before this" from the calendar. The ids are stable
    /// ("cpi-2026-03-11"), so this list is a string list in settings rather than a copy of the
    /// events themselves.
    ///
    /// <para>
    /// <b>Pruned on every write.</b> A watch is only useful while its event is still around; an
    /// id whose event is more than two days past is dropped whenever the list is written. An id
    /// that cannot be resolved is kept — a missing feed is not evidence that the event is old.
    /// </para>
    /// </summary>
    public static class MarketEventWatchList
    {
        /// <summary>How far past its event a watch survives, so a just-passed event can be re-read.</summary>
        public static readonly TimeSpan KeepPast = TimeSpan.FromDays(2);

        /// <summary>
        /// The ids to store, in order, without duplicates, after dropping ids whose event is more
        /// than <see cref="KeepPast"/> old. <paramref name="knownEvents"/> is whatever the caller
        /// has resolved; an id not in it is kept unless its own <c>yyyy-MM-dd</c> suffix says it
        /// is old too.
        /// </summary>
        public static IReadOnlyList<string> Prune(
            IEnumerable<string> ids,
            IReadOnlyList<MarketEvent> knownEvents,
            DateTime nowUtc)
        {
            var known = new Dictionary<string, MarketEvent>(StringComparer.Ordinal);
            foreach (var e in knownEvents) known[e.Id] = e;

            var now = AsUtc(nowUtc);
            var cutoff = now - KeepPast;
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                if (known.TryGetValue(id, out var e))
                {
                    if (AsUtc(e.MomentUtc) >= cutoff) result.Add(id);
                    continue;
                }

                if (TrailingDate(id) is { } date
                    && date < DateOnly.FromDateTime(cutoff.Date))
                    continue;

                result.Add(id);
            }
            return result;
        }

        /// <summary>The <c>yyyy-MM-dd</c> an id ends in, when it ends in one.</summary>
        private static DateOnly? TrailingDate(string id)
        {
            if (id.Length < 10) return null;
            string tail = id[^10..];
            return DateOnly.TryParseExact(
                tail, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date)
                ? date
                : null;
        }

        private static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
