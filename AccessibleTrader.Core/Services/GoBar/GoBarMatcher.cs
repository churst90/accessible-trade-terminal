namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>How an entry matched, in the fixed order the Go bar uses.</summary>
    public enum GoBarMatchKind
    {
        ExactCode,
        ExactName,
        ExactAlias,
        Prefix,
        WordStart,
        NearMiss,
    }

    /// <summary>One match and the text that produced it.</summary>
    public sealed record GoBarMatch(GoBarEntry Entry, GoBarMatchKind How, string MatchedText);

    /// <summary>
    /// The fixed matching order: exact code, exact name or alias, prefix, start of a word,
    /// then a near-miss spelling. Case- and culture-insensitive (invariant upper case, so
    /// Turkish's dotted i cannot break it).
    ///
    /// <para><b>Money is never fuzzy.</b> A <see cref="GoBarKind.Money"/> entry survives only
    /// as an exact code or an exact full name; aliases, prefixes, word starts and typos are
    /// discarded for every money entry in one guard, not entry by entry.</para>
    /// </summary>
    public static class GoBarMatcher
    {
        public const int MaxMatches = 20;

        public static IReadOnlyList<GoBarMatch> Match(string input, IReadOnlyList<GoBarEntry> entries)
        {
            string wanted = Normalize(input);
            if (wanted.Length == 0 || entries == null || entries.Count == 0)
                return Array.Empty<GoBarMatch>();

            var found = new List<(int Index, GoBarMatch Match)>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var match = BestFor(wanted, entry);
                if (match == null) continue;

                // One guard, not a per-entry rule: money never matches a prefix, an alias, a
                // word start or a near miss, only its exact code or its exact full name.
                if (entry.Kind == GoBarKind.Money
                    && match.How != GoBarMatchKind.ExactCode
                    && match.How != GoBarMatchKind.ExactName)
                {
                    continue;
                }

                found.Add((i, match));
            }

            return found
                .OrderBy(x => Tier(x.Match.How))
                .ThenBy(x => x.Index)
                .Take(MaxMatches)
                .Select(x => x.Match)
                .ToList();
        }

        private static int Tier(GoBarMatchKind how) => how switch
        {
            GoBarMatchKind.ExactCode => 0,
            GoBarMatchKind.ExactName => 1,
            GoBarMatchKind.ExactAlias => 1,
            GoBarMatchKind.Prefix => 2,
            GoBarMatchKind.WordStart => 3,
            _ => 4,
        };

        private static GoBarMatch? BestFor(string wanted, GoBarEntry entry)
        {
            foreach (var code in entry.Codes)
            {
                if (Normalize(code) == wanted)
                    return new GoBarMatch(entry, GoBarMatchKind.ExactCode, code);
            }

            if (Normalize(entry.Name) == wanted)
                return new GoBarMatch(entry, GoBarMatchKind.ExactName, entry.Name);

            foreach (var alias in entry.Aliases)
            {
                if (Normalize(alias) == wanted)
                    return new GoBarMatch(entry, GoBarMatchKind.ExactAlias, alias);
            }

            foreach (var code in entry.Codes)
            {
                if (Normalize(code).StartsWith(wanted, StringComparison.Ordinal))
                    return new GoBarMatch(entry, GoBarMatchKind.Prefix, code);
            }

            foreach (var alias in entry.Aliases)
            {
                if (Normalize(alias).StartsWith(wanted, StringComparison.Ordinal))
                    return new GoBarMatch(entry, GoBarMatchKind.Prefix, alias);
            }

            if (Normalize(entry.Name).StartsWith(wanted, StringComparison.Ordinal))
                return new GoBarMatch(entry, GoBarMatchKind.Prefix, entry.Name);

            foreach (var word in Words(entry.Name))
            {
                if (Normalize(word).StartsWith(wanted, StringComparison.Ordinal))
                    return new GoBarMatch(entry, GoBarMatchKind.WordStart, word);
            }

            foreach (var alias in entry.Aliases)
            {
                foreach (var word in Words(alias))
                {
                    if (Normalize(word).StartsWith(wanted, StringComparison.Ordinal))
                        return new GoBarMatch(entry, GoBarMatchKind.WordStart, word);
                }
            }

            if (wanted.Length < 4) return null;
            int allowed = wanted.Length >= 8 ? 2 : 1;

            foreach (var code in entry.Codes)
            {
                string candidate = Normalize(code);
                if (WithinDistance(candidate, wanted, allowed))
                    return new GoBarMatch(entry, GoBarMatchKind.NearMiss, code);
            }

            foreach (var alias in entry.Aliases)
            {
                string candidate = Normalize(alias);
                if (WithinDistance(candidate, wanted, allowed))
                    return new GoBarMatch(entry, GoBarMatchKind.NearMiss, alias);
            }

            string name = Normalize(entry.Name);
            if (WithinDistance(name, wanted, allowed))
                return new GoBarMatch(entry, GoBarMatchKind.NearMiss, entry.Name);

            return null;
        }

        private static IEnumerable<string> Words(string text)
            => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Trim, collapse every run of whitespace to one space, invariant upper case.</summary>
        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            var parts = new List<string>();
            foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                parts.Add(part);
            return string.Join(' ', parts).ToUpperInvariant();
        }

        /// <summary>
        /// Damerau–Levenshtein distance (optimal string alignment) is at most
        /// <paramref name="limit"/>. A cheap length check and a banded walk keep the
        /// near-miss tier over 140 entries cheap.
        /// </summary>
        private static bool WithinDistance(string candidate, string wanted, int limit)
        {
            if (Math.Abs(candidate.Length - wanted.Length) > limit) return false;

            int rows = wanted.Length + 1;
            int cols = candidate.Length + 1;
            var previous = new int[cols];
            var current = new int[cols];
            var beforePrevious = new int[cols];

            for (int j = 0; j < cols; j++) previous[j] = j;

            for (int i = 1; i < rows; i++)
            {
                current[0] = i;
                for (int j = 1; j < cols; j++)
                {
                    int cost = wanted[i - 1] == candidate[j - 1] ? 0 : 1;
                    int value = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);

                    if (i > 1 && j > 1
                        && wanted[i - 1] == candidate[j - 2]
                        && wanted[i - 2] == candidate[j - 1])
                    {
                        value = Math.Min(value, beforePrevious[j - 2] + 1);
                    }

                    current[j] = value;
                }

                var swap = beforePrevious;
                beforePrevious = previous;
                previous = current;
                current = swap;
            }

            return previous[cols - 1] <= limit;
        }
    }
}
