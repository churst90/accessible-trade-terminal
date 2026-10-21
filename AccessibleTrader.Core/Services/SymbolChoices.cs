namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Which symbols the toolbar's Symbol list actually holds.
    ///
    /// <para>
    /// <b>Why it is not always the whole list.</b> Alpaca's US stock list is 14,402 symbols
    /// (counted from the app's own symbol cache, 2026-10-21). The toolbar rendered one
    /// <c>&lt;option&gt;</c> per symbol, so arriving on the first Alpaca stock tab after a crypto
    /// tab rebuilt the dropdown with 14,402 new options. Every one of them is an accessibility
    /// node; the screen reader was handed fourteen thousand change events at once and stopped
    /// talking until it had worked through them (Cody, the Camel cycles workspace: "when I switch
    /// to the DIA tab speech freezes"). Bitstamp's list is 256 and Alpaca's crypto list 73, which
    /// is why only the stock tabs did it.
    /// </para>
    ///
    /// <para>
    /// A list up to <see cref="FullListLimit"/> is shown whole, exactly as before. A longer one is
    /// searched instead: the toolbar shows a Find box, and the dropdown holds the current symbol
    /// plus at most <see cref="MatchLimit"/> matches for what was typed.
    /// </para>
    /// </summary>
    public static class SymbolChoices
    {
        /// <summary>The longest list the dropdown still holds in full.</summary>
        public const int FullListLimit = 1000;

        /// <summary>The most matches a search puts in the dropdown.</summary>
        public const int MatchLimit = 200;

        /// <summary>True when the list is too long to render whole, so the Find box is shown.</summary>
        public static bool NeedsFind(int count) => count > FullListLimit;

        /// <summary>
        /// The options the dropdown renders. The whole list when it is short. Otherwise the
        /// current symbol — a native select must hold its own value — followed by the matches
        /// for <paramref name="find"/>: an exact match first, then symbols that start with the
        /// text, then symbols that contain it, each group in list order. Case is ignored.
        /// </summary>
        public static IReadOnlyList<string> Shown(IReadOnlyList<string> all, string? selected, string? find)
        {
            if (!NeedsFind(all.Count)) return all;

            var shown = new List<string>();
            var text = find?.Trim() ?? "";
            if (text.Length > 0)
            {
                var exact    = new List<string>();
                var prefix   = new List<string>();
                var contains = new List<string>();
                foreach (var s in all)
                {
                    if (s.Equals(text, StringComparison.OrdinalIgnoreCase)) exact.Add(s);
                    else if (s.StartsWith(text, StringComparison.OrdinalIgnoreCase)) prefix.Add(s);
                    else if (s.Contains(text, StringComparison.OrdinalIgnoreCase)) contains.Add(s);
                }
                shown.AddRange(exact.Concat(prefix).Concat(contains).Take(MatchLimit));
            }

            if (!string.IsNullOrEmpty(selected) && !shown.Contains(selected))
                shown.Insert(0, selected);
            return shown;
        }

        /// <summary>How many symbols in the whole list match <paramref name="find"/>.</summary>
        public static int MatchCount(IReadOnlyList<string> all, string? find)
        {
            var text = find?.Trim() ?? "";
            return text.Length == 0 ? 0 : all.Count(s => s.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
    }
}
