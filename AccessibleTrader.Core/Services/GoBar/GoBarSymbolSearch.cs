using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>One symbol the Go bar found, fully qualified so loading it needs no guess.</summary>
    public sealed record GoBarSymbolMatch(
        string Provider,
        string Symbol,
        MarketType Market,
        string SubType,
        bool FromWatchlist);

    /// <summary>
    /// The symbol tiers, in the decision's order: the current venue first, then the user's
    /// watchlists, then any other venue whose symbol list is already in memory.
    ///
    /// <para><b>No network while the user types.</b> Stage 6b has no cross-venue symbol cache
    /// in memory: the orchestrator holds only the current provider's list. So the caller passes
    /// an empty <c>otherVenues</c> in production, and the no-match sentence names only where it
    /// looked. If a later stage keeps other venues' lists in memory, passing them here is all
    /// that is needed.</para>
    ///
    /// <para>Comparison removes <c>/</c>, <c>-</c>, <c>_</c> and spaces and ignores case, so
    /// BTC/USDT, BTC-USDT and btc_usdt are one symbol to the user.</para>
    /// </summary>
    public static class GoBarSymbolSearch
    {
        public const int MaxMatches = 20;

        public static IReadOnlyList<GoBarSymbolMatch> Find(
            string? argument,
            string currentProvider,
            IReadOnlyList<string> currentSymbols,
            IReadOnlyList<Watchlist> watchlists,
            IReadOnlyDictionary<string, IReadOnlyList<string>> otherVenues,
            MarketType currentMarket,
            string currentSubType)
        {
            string wanted = Normalize(argument);
            if (wanted.Length == 0) return Array.Empty<GoBarSymbolMatch>();

            var found = new List<(int Tier, int Rank, int Order, GoBarSymbolMatch Match)>();
            int order = 0;

            // Tier 0: the current venue.
            foreach (string symbol in currentSymbols ?? Array.Empty<string>())
            {
                int rank = Rank(symbol, wanted);
                if (rank < 0) continue;
                found.Add((0, rank, order++, new GoBarSymbolMatch(
                    currentProvider, symbol, currentMarket, currentSubType, FromWatchlist: false)));
            }

            // Tier 1: every watchlist entry, de-duplicated by (provider, symbol).
            foreach (var list in watchlists ?? Array.Empty<Watchlist>())
            {
                foreach (var entry in list.Entries)
                {
                    int rank = Rank(entry.Symbol, wanted);
                    if (rank < 0) continue;
                    if (found.Any(f => SameSymbol(f.Match.Provider, f.Match.Symbol,
                            entry.Provider, entry.Symbol))) continue;
                    found.Add((1, rank, order++, new GoBarSymbolMatch(
                        entry.Provider, entry.Symbol, entry.Market, entry.SubType, FromWatchlist: true)));
                }
            }

            // Tier 2: other venues, only from lists the caller already has in memory.
            if (otherVenues != null)
            {
                foreach (var (provider, symbols) in otherVenues)
                {
                    foreach (string symbol in symbols ?? Array.Empty<string>())
                    {
                        int rank = Rank(symbol, wanted);
                        if (rank < 0) continue;
                        if (found.Any(f => SameSymbol(f.Match.Provider, f.Match.Symbol,
                                provider, symbol))) continue;
                        found.Add((2, rank, order++, new GoBarSymbolMatch(
                            provider, symbol, currentMarket, currentSubType, FromWatchlist: false)));
                    }
                }
            }

            return found
                .OrderBy(f => f.Tier)
                .ThenBy(f => f.Rank)
                .ThenBy(f => f.Order)
                .Take(MaxMatches)
                .Select(f => f.Match)
                .ToList();
        }

        /// <summary>0 exact, 1 starts-with, 2 contains, -1 not a match.</summary>
        public static int Rank(string? symbol, string normalizedWanted)
        {
            string candidate = Normalize(symbol);
            if (candidate.Length == 0) return -1;
            if (candidate == normalizedWanted) return 0;
            if (candidate.StartsWith(normalizedWanted, StringComparison.Ordinal)) return 1;
            if (candidate.Contains(normalizedWanted, StringComparison.Ordinal)) return 2;
            return -1;
        }

        /// <summary>Strip <c>/ - _</c> and spaces, invariant upper case.</summary>
        public static string Normalize(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var chars = new List<char>(text.Length);
            foreach (char c in text)
            {
                if (c is '/' or '-' or '_' or ' ') continue;
                chars.Add(char.ToUpperInvariant(c));
            }
            return new string(chars.ToArray());
        }

        private static bool SameSymbol(string aProvider, string aSymbol, string bProvider, string bSymbol)
            => string.Equals(aProvider, bProvider, StringComparison.OrdinalIgnoreCase)
               && string.Equals(aSymbol, bSymbol, StringComparison.OrdinalIgnoreCase);
    }
}
