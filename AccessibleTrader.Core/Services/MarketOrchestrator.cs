using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    public interface IMarketOrchestrator
    {
        string SelectedMarket { get; set; }
        string SelectedProvider { get; set; }
        string SelectedSubType { get; set; }
        string SelectedSymbol { get; set; }
        string SelectedTimeframe { get; set; }

        IReadOnlyList<string> AvailableMarkets { get; }
        IReadOnlyList<string> AvailableProviders { get; }
        IReadOnlyList<string> AvailableSubTypes { get; }
        IReadOnlyList<string> AvailableSymbols { get; }
        IReadOnlyList<string> AvailableTimeframes { get; }

        /// <summary>Fires after any list (markets, providers, symbols, timeframes) changes.</summary>
        IObservable<MarketOrchestrator.Unit> PipelineUpdated { get; }

        /// <summary>Full refresh: reloads markets, then cascades through providers → symbols → timeframes.</summary>
        Task RefreshPipelineAsync();

        /// <summary>Reload the provider list for the current market selection.</summary>
        Task RefreshProvidersAsync();

        /// <summary>Reload the symbol and timeframe lists for the current provider selection.</summary>
        Task RefreshSymbolsAsync();

        /// <summary>Load chart data for the currently selected symbol/timeframe/provider.</summary>
        Task LoadChartAsync();

        /// <summary>
        /// Peeks at the currently-selected provider's declared <see cref="Sdk.Plugins.ProviderDataShape"/>
        /// without triggering a data fetch. Used by the toolbar to pre-flight whether
        /// clicking Load Chart would cause a shape-change strip, so the user can be
        /// warned and offered the "Open in New Tab" alternative before losing work.
        /// Returns <see cref="Sdk.Plugins.ProviderDataShape.Ohlcv"/> if the provider lookup fails.
        /// </summary>
        Task<Sdk.Plugins.ProviderDataShape> GetSelectedProviderDataShapeAsync();

        /// <summary>
        /// Opens a new tab, sets the currently-selected market/provider/symbol/timeframe
        /// on it, and loads the chart — without touching the previously active tab. This
        /// is the "Open in New Tab" path from the shape-change warning dialog: the user
        /// can preview an analytics metric without losing their trading tab's indicators
        /// or drawings. Preserves all toolbar selections before returning.
        /// </summary>
        Task LoadChartInNewTabAsync();
    }

    public class MarketOrchestrator : IMarketOrchestrator, IDisposable
    {
        private readonly IDataService _dataService;
        private readonly IDataManager _dataManager;
        private readonly IWorkspaceStore _store;
        private readonly IWorkspaceInitializer _workspaceInitializer;
        private readonly IEventBus _eventBus;
        private readonly Subject<Unit> _pipelineUpdated = new();
        private readonly MarketStateMachine _stateMachine;
        private IDisposable? _tabSwitchedSub;

        private string _selectedMarket = "";
        private string _selectedProvider = "";
        private string _selectedSubType = "Spot";
        private string _selectedSymbol = "";
        private string _selectedTimeframe = "1h";

        private List<string> _availableMarkets = new();
        private List<string> _availableProviders = new();
        private List<string> _availableSubTypes = new() { "Spot" };
        private List<string> _availableSymbols = new();
        private List<string> _availableTimeframes = new();

        public MarketState CurrentState => _stateMachine.CurrentState;
        public IObservable<MarketState> StateChanged => _stateMachine.StateChanged;

        // Simple properties — callers use the explicit Refresh* methods to trigger
        // the async cascade and surface exceptions rather than fire-and-forget setters.
        public string SelectedMarket
        {
            get => _selectedMarket;
            set { _selectedMarket = value; }
        }

        public string SelectedProvider
        {
            get => _selectedProvider;
            // Demo guard: reject a non-whitelisted provider so a crafted request
            // can't escape the curated set even if the UI is bypassed.
            set { if (_demo.IsDemo && !_demo.IsProviderAllowed(value)) return; _selectedProvider = value; }
        }

        public string SelectedSubType
        {
            get => _selectedSubType;
            set { _selectedSubType = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public string SelectedSymbol
        {
            get => _selectedSymbol;
            // Demo guard: reject a symbol outside the active provider's whitelist.
            set { if (_demo.IsDemo && !_demo.IsSymbolAllowed(_selectedProvider, value)) return; _selectedSymbol = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public string SelectedTimeframe
        {
            get => _selectedTimeframe;
            // Demo guard: reject a timeframe outside the curated set.
            set { if (_demo.IsDemo && !_demo.IsTimeframeAllowed(value)) return; _selectedTimeframe = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public IReadOnlyList<string> AvailableMarkets => _availableMarkets;
        public IReadOnlyList<string> AvailableProviders => _availableProviders;
        public IReadOnlyList<string> AvailableSubTypes => _availableSubTypes;
        public IReadOnlyList<string> AvailableSymbols => _availableSymbols;
        public IReadOnlyList<string> AvailableTimeframes => _availableTimeframes;

        public IObservable<Unit> PipelineUpdated => _pipelineUpdated.AsObservable();

        private readonly IDisposable _modeSub;
        private CancellationTokenSource _tabSwitchCts = new();

        // Public-demo whitelist. A no-op (everything allowed) outside demo mode.
        private readonly DemoPolicy _demo;

        public MarketOrchestrator(
            IDataService dataService,
            IDataManager dataManager,
            IWorkspaceStore store,
            IWorkspaceInitializer workspaceInitializer,
            IEventBus eventBus,
            DemoPolicy demo)
        {
            _dataService           = dataService;
            _dataManager           = dataManager;
            _store                 = store;
            _workspaceInitializer  = workspaceInitializer;
            _eventBus              = eventBus;
            _demo                  = demo;
            _stateMachine          = new MarketStateMachine();

            // When the user switches to a tab that already has a chart loaded, refresh its data
            // so it catches up with any bars that arrived while it was inactive.
            // Tabs with no identity (blank new tabs) are skipped.
            //
            // Race-condition guard: each new tab switch cancels any in-flight refresh from the
            // previous switch. RefreshDataAsync checks the token after the network fetch so
            // stale dispatches never land on the wrong tab.
            _tabSwitchedSub = _eventBus.Subscribe<TabSwitchedEvent>(e =>
            {
                var state = _store.State;
                int switchedToIndex = state.ActiveTabIndex;
                if (!string.IsNullOrEmpty(state.Identity.Symbol))
                {
                    // Cancel any in-flight refresh from a previous switch.
                    _tabSwitchCts.Cancel();
                    _tabSwitchCts = new CancellationTokenSource();
                    var cts = _tabSwitchCts;
                    var capturedIdentity = state.Identity;
                    // Capture the snapshot data now — after SwitchTab ran, WorkspaceState.Data
                    // already holds the restored snapshot data. We pass it to CatchUpFromSnapshotAsync
                    // so the full scrollback history is preserved and only the gap is fetched.
                    var capturedSnapshotData = state.Data;

                    Task.Run(async () =>
                    {
                        try
                        {
                            if (_store.State.ActiveTabIndex != switchedToIndex) return;

                            // Sync local selection state with the restored tab's identity.
                            _selectedProvider  = capturedIdentity.Provider;
                            _selectedSymbol    = capturedIdentity.Symbol;
                            _selectedTimeframe = capturedIdentity.Timeframe;
                            _dataManager.Identity = capturedIdentity;

                            // Use gap-fill instead of a full 200-bar re-fetch.
                            // CatchUpFromSnapshotAsync restores the snapshot then appends
                            // only the bars that arrived while the tab was inactive.
                            await _dataManager.CatchUpFromSnapshotAsync(capturedSnapshotData, cts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tab catch-up failed: {ex.Message}"); }
                    });
                }
            });

            // When the terminal mode changes (Trading ↔ Analytics), refresh the market list
            // because each mode exposes a different subset of markets.
            _modeSub = _store.StateStream
                .Select(s => s.Mode)
                .DistinctUntilChanged()
                .Subscribe(_ => Task.Run(async () =>
                {
                    try { await RefreshPipelineAsync().ConfigureAwait(false); }
                    catch { /* Swallow — mode switch failures are non-fatal */ }
                }));
        }

        /// <summary>
        /// Full cascade: loads all available markets, filters by terminal mode, then
        /// loads providers → symbols → timeframes for the current selection.
        /// </summary>
        public async Task RefreshPipelineAsync()
        {
            _stateMachine.Fire(MarketTrigger.RefreshRequested);
            try 
            {
                var allMarkets = await _dataService.LoadAvailableMarketsAsync().ConfigureAwait(false);
                var mode = _store.State.Mode;

                // Analytics mode hosts every non-tradeable data category. New
                // categories (Derivatives = funding/OI, Sentiment = fear-greed, etc.)
                // join Economic + OnChain here so the Analytics radio button surfaces
                // the full data-source taxonomy in the market dropdown.
                bool IsAnalyticsCategory(string m) =>
                    m == "Economic" || m == "OnChain" || m == "Derivatives" || m == "Sentiment";

                _availableMarkets = mode == TerminalMode.Analytics
                    ? allMarkets.Where(IsAnalyticsCategory).ToList()
                    : allMarkets.Where(m => !IsAnalyticsCategory(m)).ToList();

                // Fallback: ensure at least a minimal market list is present.
                if (_availableMarkets.Count == 0)
                    _availableMarkets.AddRange(mode == TerminalMode.Analytics
                        ? new[] { "Economic", "OnChain", "Derivatives", "Sentiment" }
                        : new[] { "Crypto", "Forex", "Stock" });

                if (string.IsNullOrEmpty(_selectedMarket) || !_availableMarkets.Contains(_selectedMarket))
                    _selectedMarket = _availableMarkets[0];

                await RefreshProvidersAsync().ConfigureAwait(false);
                _stateMachine.Fire(MarketTrigger.RefreshCompleted);
            }
            catch 
            {
                _stateMachine.Fire(MarketTrigger.ErrorOccurred);
                throw;
            }
        }

        /// <summary>
        /// Reload providers for the currently selected market, then cascade to symbols.
        /// Throws on network failure so callers can surface the error to the user.
        /// </summary>
        public async Task RefreshProvidersAsync()
        {
            if (string.IsNullOrEmpty(_selectedMarket))
            {
                _availableProviders = new List<string>();
                _pipelineUpdated.OnNext(Unit.Default);
                return;
            }

            _availableProviders = await _dataService.LoadProvidersByMarketTypeAsync(_selectedMarket).ConfigureAwait(false);

            // Ensure well-known providers appear in their canonical market even if the
            // data service returned an empty list (e.g., providers not yet configured).
            switch (_selectedMarket)
            {
                case "Crypto":
                    EnsureContains(_availableProviders, "Binance", "Bitstamp", "Coinbase", "FMP");
                    break;
                case "Economic":
                    EnsureContains(_availableProviders, "Fred", "FMP Analytics");
                    break;
                case "OnChain":
                    EnsureContains(_availableProviders, "Glassnode", "CoinGecko", "BGeometrics", "DefiLlama", "CoinMetrics", "Mempool", "Etherscan");
                    break;
                case "Derivatives":
                    EnsureContains(_availableProviders, "OkxDerivatives", "BinanceDerivatives");
                    break;
                case "Sentiment":
                    EnsureContains(_availableProviders, "AlternativeMe");
                    break;
                case "Stock":
                    EnsureContains(_availableProviders, "Alpaca", "Polygon", "FMP");
                    break;
                case "Forex":
                    EnsureContains(_availableProviders, "Alpaca", "Polygon", "FMP");
                    break;
                case "Commodity":
                case "Index":
                    EnsureContains(_availableProviders, "FMP");
                    break;
            }

            // Demo whitelist: keep only the curated providers so every Available*-driven
            // UI is restricted automatically and the fixup below lands on an allowed one.
            if (_demo.IsDemo)
                _availableProviders = _demo.FilterProviders(_availableProviders).ToList();

            if (string.IsNullOrEmpty(_selectedProvider) || !_availableProviders.Contains(_selectedProvider))
                _selectedProvider = _availableProviders.FirstOrDefault() ?? "";

            await RefreshSymbolsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reload symbols and timeframes for the currently selected provider.
        /// Throws on network failure so callers can surface the error to the user.
        /// </summary>
        // Sentinel value placed in the symbol list when a provider requires an API key
        // that has not been configured. The Toolbar disables the Load button for this value.
        public const string ApiKeyRequiredSentinel = "⚠ API key required — open API Keys (Alt+K)";

        public async Task RefreshSymbolsAsync()
        {
            if (string.IsNullOrEmpty(_selectedProvider))
            {
                _availableSymbols = new List<string>();
                _availableTimeframes = new List<string> { "1m", "5m", "15m", "1h", "4h", "1d" };
                _pipelineUpdated.OnNext(Unit.Default);
                return;
            }

            // Pre-flight: if the provider requires an API key and none is configured,
            // show a descriptive sentinel in the symbol dropdown instead of silently
            // falling back to a hardcoded list that would mislead the user.
            bool requiresKey = await _dataService.ProviderRequiresApiKeyAsync(_selectedProvider).ConfigureAwait(false);
            bool isConfigured = await _dataService.IsProviderConfiguredAsync(_selectedProvider).ConfigureAwait(false);

            if (requiresKey && !isConfigured)
            {
                _availableSubTypes   = new List<string> { "Spot" };
                _availableSymbols    = new List<string> { ApiKeyRequiredSentinel };
                _selectedSymbol      = ApiKeyRequiredSentinel;
                _availableTimeframes = new List<string> { "1h" };
                _pipelineUpdated.OnNext(Unit.Default);
                return;
            }

            // Load sub-types (Spot/Futures/etc.) — only shown in the toolbar when count > 1.
            _availableSubTypes = await _dataService.GetSupportedSubTypesAsync(_selectedProvider, _selectedMarket).ConfigureAwait(false);
            if (_availableSubTypes.Count == 0) _availableSubTypes = new List<string> { "Spot" };
            if (!_availableSubTypes.Contains(_selectedSubType)) _selectedSubType = _availableSubTypes[0];

            // Pass the sub-type as "Market|SubType" so DataService routes symbol fetch correctly.
            string marketKey = _availableSubTypes.Count > 1
                ? $"{_selectedMarket}|{_selectedSubType}"
                : _selectedMarket;

            _availableSymbols = await _dataService.LoadSymbolsAsync(marketKey, _selectedProvider).ConfigureAwait(false);
            _availableTimeframes = await _dataService.GetSupportedTimeframesAsync(_selectedProvider).ConfigureAwait(false);

            // Only fall back to a minimal hardcoded set for providers that genuinely have
            // no server-side symbol list (e.g. FRED uses well-known series codes).
            if (_availableSymbols.Count == 0)
            {
                _availableSymbols = _selectedProvider.ToLowerInvariant() switch
                {
                    "fred" => new List<string> { "UNRATE", "CPIAUCSL", "GDP", "FEDFUNDS", "DEXUSEU", "DGS10" },
                    _      => new List<string>()
                };
            }

            if (_availableTimeframes.Count == 0)
                _availableTimeframes = new List<string> { "1m", "5m", "15m", "1h", "4h", "1d" };

            // Demo whitelist: restrict symbols to the curated set and force the two
            // demo timeframes, so the fixups below land on allowed values and no
            // other symbol/timeframe is reachable from the UI.
            if (_demo.IsDemo)
            {
                _availableSymbols = _demo.FilterSymbols(_selectedProvider, _availableSymbols).ToList();
                _availableTimeframes = _demo.AllowedTimeframes.ToList();
                if (!_demo.IsTimeframeAllowed(_selectedTimeframe))
                    _selectedTimeframe = _demo.DefaultTimeframe;
            }

            if (string.IsNullOrEmpty(_selectedSymbol)
                || _selectedSymbol == ApiKeyRequiredSentinel
                || !_availableSymbols.Contains(_selectedSymbol))
            {
                _selectedSymbol = _availableSymbols.FirstOrDefault() ?? "";
            }

            if (!_availableTimeframes.Contains(_selectedTimeframe))
                _selectedTimeframe = "1h";

            _pipelineUpdated.OnNext(Unit.Default);
        }

        public async Task LoadChartAsync()
        {
            if (string.IsNullOrEmpty(_selectedSymbol))
                throw new InvalidOperationException("No symbol selected.");

            // Resolve the selected provider so we can pass its declared data shape AND
            // its symbol display name to the reconciler. Shape drives which core series
            // get seeded (Candles+Volume+Price vs Price-only); the display name labels
            // the Price series on analytics tabs so speech reads "Fear and Greed Index"
            // instead of "Price". Falls back to OHLCV / raw symbol if the provider
            // lookup fails (e.g. plugin DLL missing); a stale lookup never crashes the
            // load path.
            var providerForShape = await _dataService.GetProviderAsync(_selectedProvider).ConfigureAwait(false);
            var dataShape = providerForShape?.DataShape ?? Sdk.Plugins.ProviderDataShape.Ohlcv;
            var symbolDisplayName = providerForShape?.GetSymbolDisplayName(_selectedSymbol) ?? _selectedSymbol;
            // Q3: per-symbol render hints for analytics metrics (range/zones/display/speech).
            // Null = provider declared no hints → Price series renders as plain line. See
            // SymbolRenderHints for the full field documentation.
            var renderHints = providerForShape?.GetSymbolRenderHints(_selectedSymbol);

            // Reconcile the core series stack against the new shape, relabel the Price
            // series on analytics tabs, and apply any render hints. See WorkspaceInitializer
            // for the full shape-change / same-shape / first-load matrix.
            _workspaceInitializer.InitializeDefaultSeries(dataShape, symbolDisplayName, renderHints);

            _store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Loading));
            _stateMachine.Fire(MarketTrigger.ConnectionStarted);

            string marketForIdentity = _availableSubTypes.Count > 1
                ? $"{_selectedMarket}|{_selectedSubType}"
                : _selectedMarket;

            var identity = new ChartIdentity
            {
                Provider = _selectedProvider,
                Symbol = _selectedSymbol,
                Timeframe = _selectedTimeframe,
                Market = marketForIdentity
            };

            _store.Dispatch(new SetIdentityAction(identity));
            _dataManager.Identity = identity;
            try
            {
                await _dataManager.RefreshDataAsync().ConfigureAwait(false);
                _store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Ready));
                _stateMachine.Fire(MarketTrigger.ConnectionEstablished);
            }
            catch
            {
                _store.Dispatch(new RequestInitializationStatusAction(InitializationStatus.Error));
                _stateMachine.Fire(MarketTrigger.ErrorOccurred);
                throw;
            }
        }

        public async Task<Sdk.Plugins.ProviderDataShape> GetSelectedProviderDataShapeAsync()
        {
            if (string.IsNullOrEmpty(_selectedProvider)) return Sdk.Plugins.ProviderDataShape.Ohlcv;
            try
            {
                var provider = await _dataService.GetProviderAsync(_selectedProvider).ConfigureAwait(false);
                return provider?.DataShape ?? Sdk.Plugins.ProviderDataShape.Ohlcv;
            }
            catch
            {
                return Sdk.Plugins.ProviderDataShape.Ohlcv;
            }
        }

        public async Task LoadChartInNewTabAsync()
        {
            // Capture the currently-selected market/provider/symbol/timeframe before we
            // add the new tab. AddTabAction is synchronous but the snapshot/restore dance
            // happens inside the reducer and may race with MarketOrchestrator's own state
            // if we're not careful. Capturing first keeps all selections stable.
            var targetMarket    = _selectedMarket;
            var targetSubType   = _selectedSubType;
            var targetProvider  = _selectedProvider;
            var targetSymbol    = _selectedSymbol;
            var targetTimeframe = _selectedTimeframe;

            // AddTabAction creates a blank tab and makes it active. The previous tab's
            // state is saved into a TabSnapshot and will be restored if the user switches
            // back — so the trading tab's indicators and drawings are never lost.
            _store.Dispatch(new AddTabAction());

            // The new tab starts empty; re-apply the toolbar selections (the orchestrator's
            // fields are global, not per-tab, so they already match what the user picked).
            _selectedMarket    = targetMarket;
            _selectedSubType   = targetSubType;
            _selectedProvider  = targetProvider;
            _selectedSymbol    = targetSymbol;
            _selectedTimeframe = targetTimeframe;

            // Normal load — InitializeDefaultSeries will seed the correct core stack for
            // the new tab based on the target provider's shape.
            await LoadChartAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            _modeSub?.Dispose();
            _tabSwitchedSub?.Dispose();
            _tabSwitchCts.Cancel();
            _tabSwitchCts.Dispose();
            _pipelineUpdated.Dispose();
            _stateMachine.Dispose();
        }

        private static void EnsureContains(List<string> list, params string[] items)
        {
            foreach (var item in items)
                if (!list.Contains(item))
                    list.Add(item);
        }

        public enum MarketState
        {
            Idle,
            RefreshingMetadata,
            Connecting,
            Connected,
            Disconnected,
            Reconnecting,
            Faulted
        }

        public enum MarketTrigger
        {
            RefreshRequested,
            RefreshCompleted,
            ConnectionStarted,
            ConnectionEstablished,
            ConnectionLost,
            ReconnectAttempt,
            ErrorOccurred,
            Reset
        }

        private class MarketStateMachine : AccessibleTrader.Sdk.Services.StateMachine<MarketState, MarketTrigger>
        {
            public MarketStateMachine() : base(MarketState.Idle) { }

            protected override MarketState Transition(MarketState currentState, MarketTrigger trigger)
            {
                return (currentState, trigger) switch
                {
                    (MarketState.Idle, MarketTrigger.RefreshRequested) => MarketState.RefreshingMetadata,
                    (MarketState.RefreshingMetadata, MarketTrigger.RefreshCompleted) => MarketState.Idle,
                    
                    (MarketState.Idle, MarketTrigger.ConnectionStarted) => MarketState.Connecting,
                    (MarketState.Connecting, MarketTrigger.ConnectionEstablished) => MarketState.Connected,
                    
                    (MarketState.Connected, MarketTrigger.ConnectionLost) => MarketState.Reconnecting,
                    (MarketState.Reconnecting, MarketTrigger.ConnectionEstablished) => MarketState.Connected,
                    (MarketState.Reconnecting, MarketTrigger.ReconnectAttempt) => MarketState.Reconnecting,
                    
                    (_, MarketTrigger.ErrorOccurred) => MarketState.Faulted,
                    (_, MarketTrigger.Reset) => MarketState.Idle,
                    
                    _ => currentState
                };
            }
        }

        /// <summary>Unit type used in the PipelineUpdated observable.</summary>
        public struct Unit
        {
            public static Unit Default => default;
        }
    }
}
