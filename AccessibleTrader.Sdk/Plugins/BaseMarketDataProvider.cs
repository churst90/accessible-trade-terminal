using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Sdk.Plugins
{
    public abstract class BaseMarketDataProvider : IMarketDataProvider, IProviderPlugin, IDisposable
    {
        private bool _disposed;
        protected readonly Subject<Ohlcv> _liveStream = new();
        public IObservable<Ohlcv> LiveStream => _liveStream;

        protected readonly Subject<string> _errorStream = new();
        public IObservable<string> ErrorStream => _errorStream;

        protected readonly Subject<ProviderError> _providerErrors = new();
        /// <summary>Structured errors (severity/category/symbol). Emitted alongside the
        /// legacy <see cref="ErrorStream"/> string by <see cref="SurfaceError"/>.</summary>
        public IObservable<ProviderError> ProviderErrors => _providerErrors;

        /// <summary>Surface a provider error to BOTH the typed and legacy streams.
        /// Prefer this over raw <c>_errorStream.OnNext</c> so consumers can react on
        /// severity/category and a blind trader always hears read-path failures.</summary>
        protected void SurfaceError(string message,
            Enums.ErrorSeverity severity = Enums.ErrorSeverity.Medium,
            Enums.ErrorCategory category = Enums.ErrorCategory.Provider,
            string? symbol = null)
        {
            _errorStream.OnNext(message);
            _providerErrors.OnNext(new ProviderError(Name, message, severity, category, symbol));
        }

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

        // ── Data shape + symbol label (declared here so derived plugins can use ──
        //    `public override` and have the override actually resolve through the
        //    interface vtable). These members exist as default interface methods on
        //    IMarketDataProvider, but a derived class shadow-property does NOT
        //    override a default interface method in C# — the interface default
        //    still fires when the provider is accessed through an IMarketDataProvider
        //    reference (which is what MarketOrchestrator does via GetProviderAsync).
        //    Declaring them as `virtual` here anchors them in the concrete class
        //    hierarchy so overrides propagate correctly.

        /// <summary>
        /// Natural rendering shape for this provider's data. Defaults to
        /// <see cref="ProviderDataShape.Ohlcv"/>. Analytics providers that emit a
        /// single-value time series should override to
        /// <see cref="ProviderDataShape.SingleValueLine"/>.
        /// </summary>
        public virtual ProviderDataShape DataShape => ProviderDataShape.Ohlcv;

        /// <summary>
        /// Per-symbol shape override (see <see cref="IMarketDataProvider.GetDataShapeForSymbol"/>).
        /// Anchored virtual here for the same vtable reason as <see cref="DataShape"/>.
        /// </summary>
        public virtual ProviderDataShape GetDataShapeForSymbol(string symbol) => DataShape;

        /// <summary>
        /// Human-readable label for the given symbol, used as the Price series
        /// FriendlyName on analytics charts. Defaults to returning the raw symbol;
        /// analytics providers should override with per-symbol mappings like
        /// "FNG_INDEX" → "Fear &amp; Greed Index".
        /// </summary>
        public virtual string GetSymbolDisplayName(string symbol) => symbol;

        /// <summary>
        /// Optional per-symbol render hints (range, reference zones, display type,
        /// speech template, color). Defaults to null (no hints). See
        /// <see cref="SymbolRenderHints"/> for full documentation. Anchored as
        /// virtual here so derived-class overrides resolve through the
        /// IMarketDataProvider interface vtable — declaring it directly on the
        /// plugin class (without this virtual anchor) would be a shadow property
        /// that the default interface method hides.
        /// </summary>
        public virtual SymbolRenderHints? GetSymbolRenderHints(string symbol) => null;

        /// <summary>
        /// Live tick semantics (see <see cref="IMarketDataProvider.LiveTickStyle"/>).
        /// Anchored virtual here for the same vtable reason as <see cref="DataShape"/>:
        /// kline-style providers override to CumulativeBars so consolidation stops
        /// double-counting their running volume totals.
        /// </summary>
        public virtual LiveTickStyle LiveTickStyle => LiveTickStyle.TradeDeltas;

        /// <summary>
        /// Keyed-feeds capability (see
        /// <see cref="IMarketDataProvider.SupportsMultipleLiveSubscriptions"/>).
        /// Anchored virtual for the vtable reason above.
        /// </summary>
        public virtual bool SupportsMultipleLiveSubscriptions => false;

        /// <summary>
        /// Independent live subscription (see
        /// <see cref="IMarketDataProvider.SubscribeLiveAsync"/>). Anchored virtual
        /// for the vtable reason above; only providers that override
        /// <see cref="SupportsMultipleLiveSubscriptions"/> implement it.
        /// </summary>
        public virtual Task<IAsyncDisposable> SubscribeLiveAsync(string market, string symbol, string timeframe, Action<Ohlcv> onBar)
            => throw new NotSupportedException($"{Name} does not support concurrent live subscriptions.");

        // ── Capability discovery ──────────────────────────────────────────────────

        public virtual T? GetCapability<T>() where T : class
        {
            if (typeof(T) == typeof(IMarketDataProvider)) return this as T;
            return null;
        }

        // ── Optional trading capability flags ─────────────────────────────────────
        // Data-only providers return false/0 by default. Providers that also implement
        // ITradingProvider should override these to reflect their actual capabilities.

        /// <summary>
        /// True if this provider supports margin / cross-collateral accounts.
        ///
        /// <para>
        /// **Derived, and deliberately NOT virtual.** This used to be an independent
        /// overridable bool, which meant a provider could declare
        /// <c>ProviderCapabilities.Leverage</c> and answer false here — two fields
        /// stating one fact, free to disagree, with an invariant test standing
        /// between them. Computing it from the flag removes the possibility rather
        /// than policing it: there is now one place to state this, so there is no
        /// second place to state it wrongly.
        /// </para>
        /// </summary>
        public bool SupportsMarginTrading  => Capabilities.HasFlag(ProviderCapabilities.MarginTrading);

        /// <summary>
        /// True if this provider offers futures/perpetuals contracts. Derived from
        /// <see cref="ProviderCapabilities.FuturesTrading"/>; see the note above.
        /// </summary>
        public bool SupportsFuturesTrading => Capabilities.HasFlag(ProviderCapabilities.FuturesTrading);

        /// <summary>True if stop-loss orders can be attached to positions.</summary>
        public virtual bool SupportsStopLoss       => false;

        /// <summary>True if take-profit orders can be attached to positions.</summary>
        public virtual bool SupportsTakeProfit     => false;

        /// <summary>Maximum leverage available (1.0 = spot only).</summary>
        public virtual double MaxLeverage          => 1.0;

        public abstract void Configure(Dictionary<string, string> config);

        /// <summary>
        /// Default key validation returns success. Providers that require keys should override.
        /// </summary>
        public virtual Task<(bool IsValid, string Message)> ValidateApiKeyAsync()
            => Task.FromResult((true, "No API key required"));

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

        /// <summary>
        /// Drops references to any supplied credential strings so the GC can
        /// reclaim their backing storage on the next collection. Because .NET
        /// strings are immutable and interned, we cannot reliably zero the
        /// underlying bytes in place — nulling the reference is the pragmatic
        /// minimum so a crash dump taken long after last-use does not still
        /// contain live API secrets.
        ///
        /// Providers should call this from <c>DisconnectAsync</c> or
        /// <c>Dispose</c> on every credential field they hold
        /// (<c>_apiKey</c>, <c>_apiSecret</c>, <c>_passphrase</c>, cached
        /// session tokens, etc). For true in-memory scrubbing the provider
        /// needs to switch to a fetch-on-demand pattern that reads from
        /// SecureStorage per signed request — tracked in docs/TODO.md phase 3+.
        /// </summary>
        protected static void ScrubCredentials(params Action[] nullSetters)
        {
            foreach (var setter in nullSetters)
            {
                try { setter(); } catch { /* best-effort; never throw from teardown */ }
            }
            // Hint the GC to reclaim the now-unrooted credential strings sooner
            // than it otherwise would. Gen-0 only; don't pay for a full collect.
            GC.Collect(0, GCCollectionMode.Optimized, blocking: false, compacting: false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                _liveStream.OnCompleted();
                _liveStream.Dispose();
                _errorStream.OnCompleted();
                _errorStream.Dispose();
                _providerErrors.OnCompleted();
                _providerErrors.Dispose();
                _connectionStateStream.OnCompleted();
                _connectionStateStream.Dispose();
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
