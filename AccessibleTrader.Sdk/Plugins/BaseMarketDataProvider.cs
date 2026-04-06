using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Sdk.Plugins
{
    public abstract class BaseMarketDataProvider : IMarketDataProvider, IProviderPlugin
    {
        protected readonly Subject<Ohlcv> _liveStream = new();
        public IObservable<Ohlcv> LiveStream => _liveStream;

        protected readonly Subject<string> _errorStream = new();
        public IObservable<string> ErrorStream => _errorStream;

        protected readonly BehaviorSubject<ConnectionState> _connectionStateStream = new(ConnectionState.Disconnected);
        public IObservable<ConnectionState> ConnectionStateStream => _connectionStateStream;

        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract List<Enums.MarketType> SupportedMarkets { get; }
        public abstract bool SupportsSymbolSearch { get; }
        public abstract bool RequiresApiKey { get; }
        public abstract bool IsConfigured { get; }
        public abstract bool SupportsLiveUpdates { get; }
        public abstract ProviderEnvironment Environment { get; }
        public abstract int MaxBarsPerRequest { get; }
        public abstract List<string> NativelySupportedTimeframes { get; }
        public virtual Enums.ProviderCapabilities Capabilities => Enums.ProviderCapabilities.None;

        // ── Capability discovery ──────────────────────────────────────────────────

        public virtual T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            return null;
        }

        // ── Optional trading capability flags ─────────────────────────────────────
        // Data-only providers return false/0 by default. Providers that also implement
        // ITradingProvider should override these to reflect their actual capabilities.

        /// <summary>True if this provider supports margin / cross-collateral accounts.</summary>
        public virtual bool SupportsMarginTrading  => false;

        /// <summary>True if this provider offers futures/perpetuals contracts.</summary>
        public virtual bool SupportsFuturesTrading => false;

        /// <summary>True if stop-loss orders can be attached to positions.</summary>
        public virtual bool SupportsStopLoss       => false;

        /// <summary>True if take-profit orders can be attached to positions.</summary>
        public virtual bool SupportsTakeProfit     => false;

        /// <summary>Maximum leverage available (1.0 = spot only).</summary>
        public virtual double MaxLeverage          => 1.0;

        public abstract void Configure(Dictionary<string, string> config);
        public abstract Task EnsureConnectedAsync();
        public abstract Task SetSubscriptionAsync(string market, string symbol, string timeframe);
        public abstract Task DisconnectAsync();
        public abstract Task<List<string>> GetAvailableSymbolsAsync(Enums.MarketType market, string subType = "Spot");
        public abstract Task<List<string>> GetSupportedSubTypesAsync(Enums.MarketType market);
        public abstract Task<List<string>> GetSupportedTimeframesAsync();
        public abstract Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request);
        public abstract Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10);

        protected string CleanSymbol(string symbol)
        {
            return symbol?.Replace("/", "").Replace("-", "").ToUpper() ?? string.Empty;
        }
    }
}
