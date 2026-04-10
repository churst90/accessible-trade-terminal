using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Sdk.Plugins
{
    public enum ConnectionState { Disconnected, Connecting, Connected, Error }
    public enum ProviderEnvironment { Live, Paper, Sandbox, HistoricalOnly }

    public interface IMarketDataProvider : IProviderPlugin
    {
        new string Name { get; }
        new string Description { get; }
        List<MarketType> SupportedMarkets { get; }
        bool SupportsSymbolSearch { get; }
        bool RequiresApiKey { get; }
        bool IsConfigured { get; }
        bool SupportsLiveUpdates { get; }
        ProviderEnvironment Environment { get; }
        int MaxBarsPerRequest { get; }

        /// <summary>
        /// Natural rendering shape for this provider's data. Defaults to <see cref="ProviderDataShape.Ohlcv"/>
        /// for backward compatibility — every existing market-data provider returns true OHLCV.
        /// Analytics providers (FRED, BinanceDerivatives, AlternativeMe, Glassnode, CoinGecko)
        /// override this to <see cref="ProviderDataShape.SingleValueLine"/> so the chart loader
        /// seeds a Line series instead of the default Candles + Volume + Price stack.
        /// </summary>
        ProviderDataShape DataShape => ProviderDataShape.Ohlcv;

        /// <summary>
        /// Returns a human-readable label for the given symbol, used as the series
        /// FriendlyName and the primary component's DisplayName when the chart loads.
        /// OHLCV providers generally don't need to override — the symbol itself
        /// ("BTC/USDT") is already readable. Analytics providers should override to
        /// translate opaque codes into meaningful labels:
        ///   • "FNG"             → "Fear &amp; Greed Index"
        ///   • "GLOBAL_BTC_DOM"  → "BTC Dominance"
        ///   • "BTCUSDT_FUNDING" → "BTC/USDT Funding Rate"
        /// so the speech output says "Fear and Greed Index, 47" instead of "Price, 47".
        /// Default implementation returns the raw symbol, preserving today's behavior.
        /// </summary>
        string GetSymbolDisplayName(string symbol) => symbol;

        /// <summary>
        /// Returns optional per-symbol render + sonification hints for analytics metrics.
        /// See <see cref="SymbolRenderHints"/> for the full field documentation. Bounded
        /// analytics providers should override this to declare value range, reference
        /// zones (fear/greed/neutral), speech templates, and audio profiles — so the
        /// chart renders the metric as a proper oscillator with context instead of a
        /// flat auto-scaled line. Returning null (default) preserves today's plain-line
        /// rendering for providers that don't need customization.
        /// </summary>
        SymbolRenderHints? GetSymbolRenderHints(string symbol) => null;

        IObservable<ConnectionState> ConnectionStateStream { get; }
        IObservable<Ohlcv> LiveStream { get; }
        IObservable<string> ErrorStream { get; }
        List<string> NativelySupportedTimeframes { get; }

        void Configure(Dictionary<string, string> config);

        /// <summary>
        /// Validates that the configured API key(s) are functional by making a
        /// lightweight test request. Returns (true, "") on success, or (false, reason) on failure.
        /// </summary>
        Task<(bool IsValid, string Message)> ValidateApiKeyAsync();

        // --- Connection & Subscription Management ---
        Task EnsureConnectedAsync();
        Task SetSubscriptionAsync(string market, string symbol, string timeframe);
        Task DisconnectAsync();

        // --- Data Discovery & Fetching ---
        Task<List<string>> GetSupportedSubTypesAsync(MarketType market);
        Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot");
        Task<List<string>> GetSupportedTimeframesAsync();
        Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request);
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10);
    }
}
