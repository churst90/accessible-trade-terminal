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

    /// <summary>
    /// What a provider's live ticks MEAN, so consolidation can merge them
    /// correctly. TradeDeltas: each tick is an individual trade (or delta) whose
    /// volume ADDS to the current bar — Bitstamp's live_trades channel.
    /// CumulativeBars: each tick is the current source bar re-sent with
    /// CUMULATIVE volume-so-far — Binance's kline stream. Accumulating
    /// cumulative volumes double-counts (each ~1s kline update re-adds the
    /// running total), which inflated live-bar volume on kline providers until
    /// the next REST refresh corrected it.
    /// </summary>
    public enum LiveTickStyle { TradeDeltas, CumulativeBars }

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
        /// Per-symbol shape override for providers whose datasets vary — the My Data
        /// provider charts an OHLCV file as candles and a value column as a line.
        /// Defaults to the provider-level <see cref="DataShape"/>.
        /// </summary>
        ProviderDataShape GetDataShapeForSymbol(string symbol) => DataShape;

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
        /// The single identity of the market a UI symbol resolves to on this venue, for ledgers and
        /// any other store that must hold ONE record per market however the user spelled it.
        /// Default strips separators and uppercases: BTC/USD, btc-usd and BTCUSD are one market,
        /// while BTCUSDT stays a different one. Venues that remap internally override it — see
        /// BaseMarketDataProvider.GetCanonicalSymbol and the Bitstamp usdt-to-usd case.
        /// </summary>
        string GetCanonicalSymbol(string symbol) =>
            symbol?.Replace("/", "").Replace("-", "").ToUpperInvariant() ?? string.Empty;

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

        /// <summary>
        /// Semantics of the ticks this provider emits on <see cref="LiveStream"/>
        /// (and delivers to <see cref="SubscribeLiveAsync"/> callbacks before
        /// consolidation). Defaults to TradeDeltas — the historical assumption.
        /// Kline-style providers MUST override to CumulativeBars or their live-bar
        /// volume is double-counted during consolidation.
        /// </summary>
        LiveTickStyle LiveTickStyle => LiveTickStyle.TradeDeltas;

        /// <summary>
        /// True when the provider can hold MULTIPLE concurrent live subscriptions
        /// via <see cref="SubscribeLiveAsync"/> — the keyed-feeds capability
        /// (docs/KEYED_FEEDS_DESIGN.md). <see cref="SetSubscriptionAsync"/> remains
        /// the single REPLACE-style subscription used by the focused chart.
        /// </summary>
        bool SupportsMultipleLiveSubscriptions => false;

        /// <summary>
        /// Opens an independent live subscription that delivers consolidated bars
        /// for the REQUESTED timeframe to <paramref name="onBar"/>, without
        /// disturbing the provider's focused-chart subscription or any other
        /// concurrent subscription. Dispose the returned handle to unsubscribe.
        /// Implementations own reconnection for the subscription's lifetime.
        /// Only valid when <see cref="SupportsMultipleLiveSubscriptions"/> is true.
        /// </summary>
        Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            => throw new NotSupportedException($"{Name} does not support concurrent live subscriptions.");

        // --- Data Discovery & Fetching ---
        Task<List<string>> GetSupportedSubTypesAsync(MarketType market);
        Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot");
        Task<List<string>> GetSupportedTimeframesAsync();
        Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request);
        Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10);
    }
}
