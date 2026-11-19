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
    /// </summary>
    public static class GoBarMatcher
    {
        public const int MaxMatches = 20;

        public static IReadOnlyList<GoBarMatch> Match(string input, IReadOnlyList<GoBarEntry> entries)
            => throw new NotImplementedException();
    }
}
