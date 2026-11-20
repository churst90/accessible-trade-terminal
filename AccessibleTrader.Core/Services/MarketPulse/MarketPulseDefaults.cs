using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Screening;

namespace AccessibleTrader.Core.Services.MarketPulse
{
    /// <summary>One default item: what it is called and the symbols that can stand for it.</summary>
    public sealed record MarketPulseDefaultItem(
        string Name,
        IReadOnlyList<MarketType> Markets,
        IReadOnlyList<string> Candidates,
        string Short = "");

    /// <summary>
    /// A provider as the resolver sees it: whether it is usable now, which markets it supports,
    /// and a pure function from a market to that provider's own symbol list. The service builds
    /// these after its async reads; the resolver never touches the network.
    /// </summary>
    public sealed record MarketPulseProvider(
        string Name,
        bool Usable,
        IReadOnlyCollection<MarketType> Markets,
        Func<MarketType, IReadOnlyList<string>?> Symbols,
        Func<MarketType, string>? SubTypeOf = null)
    {
        public string SubType(MarketType market) => SubTypeOf?.Invoke(market) ?? "Spot";
    }

    public sealed record MarketPulseResolveResult(
        IReadOnlyList<WatchlistEntry> Entries,
        IReadOnlyList<string> LeftOut);

    /// <summary>
    /// The nine default items, in the fixed order Cody decided, and the rules that pick a
    /// provider for each. A candidate is matched after normalising both sides, and the entry
    /// stores the provider's own spelling of the symbol — exactly what the watchlist screen
    /// would have saved.
    /// </summary>
    public static class MarketPulseDefaults
    {
        private static readonly string[] CryptoPreference =
            { "Coinbase", "Kraken", "Gemini", "Bitstamp" };

        private static readonly string[] OtherPreference =
        {
            "Fmp", "TwelveData", "Oanda", "Finnhub", "Polygon", "InteractiveBrokers",
        };

        private static readonly string[] CryptoQuoteSuffixes =
        {
            "USDT", "USDC", "FDUSD", "BUSD", "TUSD", "DAI", "USD", "EUR", "GBP", "JPY", "BTC", "ETH",
        };

        public static IReadOnlyList<MarketPulseDefaultItem> Items { get; } = new[]
        {
            new MarketPulseDefaultItem(
                "S&P",
                new[] { MarketType.Index, MarketType.Stock },
                new[] { "SPX", "GSPC", "US500", "SPX500USD" }, "SPX"),
            new MarketPulseDefaultItem(
                "Nasdaq",
                new[] { MarketType.Index },
                new[] { "IXIC", "NDX", "COMP", "NAS100USD", "US100" }, "NDX"),
            new MarketPulseDefaultItem(
                "Bitcoin",
                new[] { MarketType.Crypto },
                new[] { "BTCUSD", "BTCUSDT" }, "BTC"),
            new MarketPulseDefaultItem(
                "Ethereum",
                new[] { MarketType.Crypto },
                new[] { "ETHUSD", "ETHUSDT" }, "ETH"),
            new MarketPulseDefaultItem(
                "ten-year",
                new[] { MarketType.Index, MarketType.Bonds },
                new[] { "TNX", "US10Y" }, "10Y"),
            new MarketPulseDefaultItem(
                "dollar index",
                new[] { MarketType.Index, MarketType.Forex },
                new[] { "DXY", "DXYNYB", "USDX" }, "DXY"),
            new MarketPulseDefaultItem(
                "gold",
                new[] { MarketType.Commodity, MarketType.Forex },
                new[] { "XAUUSD", "GCUSD" }, "Gold"),
            new MarketPulseDefaultItem(
                "oil",
                new[] { MarketType.Commodity },
                new[] { "WTICOUSD", "CLUSD", "USOIL", "WTIUSD" }, "Oil"),
            new MarketPulseDefaultItem(
                "VIX",
                new[] { MarketType.Index },
                new[] { "VIX" }, "VIX"),
        };

        /// <summary>
        /// The first provider (in item preference order, then catalogue order) whose own symbol
        /// list contains one of the item's candidates, for one of the item's markets. Binance is
        /// never considered: Cody is in the US, where it is not available.
        /// </summary>
        public static WatchlistEntry? Resolve(
            MarketPulseDefaultItem item, IReadOnlyList<MarketPulseProvider> providers)
        {
            foreach (var provider in OrderedProviders(item, providers))
            {
                if (!provider.Usable) continue;
                foreach (var market in item.Markets)
                {
                    if (!provider.Markets.Contains(market)) continue;
                    IReadOnlyList<string>? symbols = provider.Symbols(market);
                    if (symbols == null || symbols.Count == 0) continue;

                    var byNormalised = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string symbol in symbols)
                    {
                        string key = Normalize(symbol);
                        if (key.Length == 0) continue;
                        byNormalised.TryAdd(key, symbol);
                    }

                    foreach (string candidate in item.Candidates)
                    {
                        if (byNormalised.TryGetValue(Normalize(candidate), out string? spelling))
                            return new WatchlistEntry(
                                provider.Name, spelling, market, provider.SubType(market));
                    }
                }
            }
            return null;
        }

        public static MarketPulseResolveResult ResolveAll(
            IReadOnlyList<MarketPulseProvider> providers)
        {
            var entries = new List<WatchlistEntry>();
            var leftOut = new List<string>();
            foreach (var item in Items)
            {
                if (Resolve(item, providers) is { } entry) entries.Add(entry);
                else leftOut.Add(item.Name);
            }
            return new MarketPulseResolveResult(entries, leftOut);
        }

        /// <summary>
        /// Upper case with the separator characters and spaces removed. The same normalisation
        /// is applied to the provider's symbols and the candidate table, so "^GSPC", "GSPC" and
        /// "gspc" are one spelling.
        /// </summary>
        public static string Normalize(string? symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return "";
            var builder = new System.Text.StringBuilder(symbol.Length);
            foreach (char raw in symbol)
            {
                if (char.IsWhiteSpace(raw)) continue;
                if (raw is '^' or '/' or '_' or '-' or '.') continue;
                builder.Append(char.ToUpperInvariant(raw));
            }
            return builder.ToString();
        }

        /// <summary>
        /// The table's name when the symbol is one of its candidates; otherwise the base asset
        /// for a crypto pair ("SOLUSDT" → "SOL"); otherwise the symbol as stored.
        /// </summary>
        public static string SpokenName(WatchlistEntry entry)
        {
            string normalised = Normalize(entry.Symbol);
            foreach (var item in Items)
                foreach (string candidate in item.Candidates)
                    if (normalised == Normalize(candidate))
                        return item.Name;

            if (entry.Market == MarketType.Crypto)
                return BaseAsset(normalised, entry.Symbol);

            return entry.Symbol;
        }

        /// <summary>
        /// The braille line's name: the table's short form ("SPX", "10Y"), the base asset for a
        /// crypto pair, otherwise the symbol as stored.
        /// </summary>
        public static string ShortName(WatchlistEntry entry)
        {
            string normalised = Normalize(entry.Symbol);
            foreach (var item in Items)
                foreach (string candidate in item.Candidates)
                    if (normalised == Normalize(candidate))
                        return item.Short.Length > 0 ? item.Short : item.Name;

            if (entry.Market == MarketType.Crypto)
                return BaseAsset(normalised, entry.Symbol);

            return entry.Symbol;
        }

        /// <summary>TNX or US10Y — the CBOE quotes TNX at ten times the yield.</summary>
        public static bool IsYield(WatchlistEntry entry)
        {
            string normalised = Normalize(entry.Symbol);
            return normalised is "TNX" or "US10Y";
        }

        private static IEnumerable<MarketPulseProvider> OrderedProviders(
            MarketPulseDefaultItem item, IReadOnlyList<MarketPulseProvider> providers)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var preference = item.Markets.Contains(MarketType.Crypto) &&
                             !item.Markets.Contains(MarketType.Index)
                ? CryptoPreference
                : OtherPreference;

            foreach (string preferred in preference)
            {
                foreach (var provider in providers)
                {
                    if (!ProviderNames.Match(provider.Name, preferred)) continue;
                    if (used.Add(ProviderNames.Normalize(provider.Name))) yield return provider;
                    break;
                }
            }

            foreach (var provider in providers)
            {
                if (ProviderNames.Match(provider.Name, "Binance")) continue;
                if (used.Add(ProviderNames.Normalize(provider.Name))) yield return provider;
            }
        }

        private static string BaseAsset(string normalised, string stored)
        {
            foreach (string suffix in CryptoQuoteSuffixes)
            {
                if (normalised.Length <= suffix.Length) continue;
                if (!normalised.EndsWith(suffix, StringComparison.Ordinal)) continue;
                return normalised[..^suffix.Length];
            }
            return normalised.Length > 0 ? normalised : stored;
        }
    }
}
