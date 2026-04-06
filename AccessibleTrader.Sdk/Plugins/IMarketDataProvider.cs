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
        
        IObservable<ConnectionState> ConnectionStateStream { get; }
        IObservable<Ohlcv> LiveStream { get; }
        IObservable<string> ErrorStream { get; }
        List<string> NativelySupportedTimeframes { get; }

        void Configure(Dictionary<string, string> config);

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
