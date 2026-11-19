namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// The pure half of the change log: append, cap, age out, plan a press and move the
    /// caught-up mark. No file, no clock, no bus — every rule here has a test without a store.
    /// </summary>
    public static class ChangeLogRules
    {
        public const int MaxEntries = 500;
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

        /// <summary>
        /// Adds <paramref name="entry"/> unless its key is already present, then drops entries
        /// older than seven days and finally the oldest by time until at most 500 remain. The
        /// surviving entries keep their original order; this never sorts.
        /// </summary>
        public static ChangeLogDocument Append(
            ChangeLogDocument doc, ChangeLogEntry entry, DateTime nowUtc)
        {
            var entries = doc.Entries?.ToList() ?? new List<ChangeLogEntry>();
            if (entries.Any(e => string.Equals(e.Key, entry.Key, StringComparison.Ordinal)))
                return doc;

            entries.Add(entry);

            DateTime cutoff = nowUtc - MaxAge;
            entries.RemoveAll(e => e.TimeUtc < cutoff);

            while (entries.Count > MaxEntries)
            {
                int oldest = 0;
                for (int i = 1; i < entries.Count; i++)
                    if (entries[i].TimeUtc < entries[oldest].TimeUtc) oldest = i;
                entries.RemoveAt(oldest);
            }

            return doc with { Entries = entries };
        }

        public static IReadOnlyList<ChangeLogEntry> Unheard(ChangeLogDocument doc)
            => (doc.Entries ?? new List<ChangeLogEntry>()).Where(e => !e.Heard).ToList();

        /// <summary>
        /// The entries this press reads: category order first, then oldest first inside a
        /// category, at most <paramref name="maxItems"/> of them. The rest stay unheard.
        /// </summary>
        public static ChangeLogPlan Plan(ChangeLogDocument doc, DateTime nowUtc, int maxItems = 5)
        {
            var allUnheard = Unheard(doc);
            var items = allUnheard
                .Select((entry, index) => (Entry: entry, Index: index))
                .OrderBy(x => x.Entry.Category)
                .ThenBy(x => x.Entry.TimeUtc)
                .ThenBy(x => x.Index)
                .Take(Math.Max(0, maxItems))
                .Select(x => x.Entry)
                .ToList();

            return new ChangeLogPlan(Items: items, TotalUnheard: allUnheard.Count);
        }

        /// <summary>
        /// Marks exactly the keys named, and moves the caught-up mark to <paramref name="nowUtc"/>.
        /// An item not named stays unheard, which is what lets an interrupted catch-up keep the
        /// rest.
        /// </summary>
        public static ChangeLogDocument MarkHeard(
            ChangeLogDocument doc, IEnumerable<string> keys, DateTime nowUtc)
        {
            var heardKeys = new HashSet<string>(keys ?? Array.Empty<string>(), StringComparer.Ordinal);
            var entries = (doc.Entries ?? new List<ChangeLogEntry>())
                .Select(e => heardKeys.Contains(e.Key) ? e with { Heard = true } : e)
                .ToList();

            return doc with { Entries = entries, LastCatchUpUtc = nowUtc };
        }
    }
}
