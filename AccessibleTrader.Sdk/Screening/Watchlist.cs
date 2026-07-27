using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Sdk.Screening;

/// <summary>
/// One symbol on a watchlist, fully qualified so it can be loaded without guessing:
/// a symbol alone is ambiguous across providers ("BTC/USDT" exists on Binance, MEXC,
/// Kraken and Bitstamp with different tick sizes and different history depth).
/// </summary>
/// <param name="Provider">Provider plugin name, e.g. "Binance". Must match <c>IMarketDataProvider.Name</c>.</param>
/// <param name="Symbol">Provider-native symbol string, e.g. "BTC/USDT".</param>
/// <param name="Market">Market type the symbol belongs to.</param>
/// <param name="SubType">Provider sub-type ("Spot", "Futures", …). Defaults to Spot.</param>
/// <param name="Note">Optional free-text note, spoken after the symbol when navigating the list.</param>
public record WatchlistEntry(
    string Provider,
    string Symbol,
    MarketType Market = MarketType.Crypto,
    string SubType = "Spot",
    string? Note = null)
{
    /// <summary>
    /// Stable identity used for de-duplication within a list. Deliberately excludes
    /// <see cref="Note"/> so editing a note doesn't create a second entry.
    /// </summary>
    public string Key => $"{Provider}|{Market}|{SubType}|{Symbol}";
}

/// <summary>
/// A named, ordered collection of symbols. Persisted to <c>watchlists.json</c> by
/// <c>JsonWatchlistLibrary</c>. Ordering is user-controlled (move up / move down) because
/// a screen-reader user navigates the list sequentially — alphabetical-only ordering would
/// make it impossible to keep related symbols adjacent.
/// </summary>
public record Watchlist(
    string Id,
    string Name,
    IReadOnlyList<WatchlistEntry> Entries)
{
    public static Watchlist Create(string name) =>
        new(Guid.NewGuid().ToString("N"), name, new List<WatchlistEntry>());
}
