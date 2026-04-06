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
            set { _selectedProvider = value; }
        }

        public string SelectedSubType
        {
            get => _selectedSubType;
            set { _selectedSubType = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public string SelectedSymbol
        {
            get => _selectedSymbol;
            set { _selectedSymbol = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public string SelectedTimeframe
        {
            get => _selectedTimeframe;
            set { _selectedTimeframe = value; _pipelineUpdated.OnNext(Unit.Default); }
        }

        public IReadOnlyList<string> AvailableMarkets => _availableMarkets;
        public IReadOnlyList<string> AvailableProviders => _availableProviders;
        public IReadOnlyList<string> AvailableSubTypes => _availableSubTypes;
        public IReadOnlyList<string> AvailableSymbols => _availableSymbols;
        public IReadOnlyList<string> AvailableTimeframes => _availableTimeframes;

        public IObservable<Unit> PipelineUpdated => _pipelineUpdated.AsObservable();

        private readonly IDisposable _modeSub;
        private CancellationTokenSource _tabSwitchCts = new();

        public MarketOrchestrator(
            IDataService dataService,
            IDataManager dataManager,
            IWorkspaceStore store,
            IWorkspaceInitializer workspaceInitializer,
            IEventBus eventBus)
        {
            _dataService           = dataService;
            _dataManager           = dataManager;
            _store                 = store;
            _workspaceInitializer  = workspaceInitializer;
            _eventBus              = eventBus;
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
                            await _dataManager.CatchUpFromSnapshotAsync(capturedSnapshotData, cts.Token);
                        }
                        catch { /* Non-fatal: tab switch failure is silent */ }
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
                    try { await RefreshPipelineAsync(); }
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
                var allMarkets = await _dataService.LoadAvailableMarketsAsync();
                var mode = _store.State.Mode;

                _availableMarkets = mode == TerminalMode.Analytics
                    ? allMarkets.Where(m => m == "Economic" || m == "OnChain").ToList()
                    : allMarkets.Where(m => m != "Economic" && m != "OnChain").ToList();

                // Fallback: ensure at least a minimal market list is present.
                if (_availableMarkets.Count == 0)
                    _availableMarkets.AddRange(mode == TerminalMode.Analytics
                        ? new[] { "Economic" }
                        : new[] { "Crypto", "Forex", "Stock" });

                if (string.IsNullOrEmpty(_selectedMarket) || !_availableMarkets.Contains(_selectedMarket))
                    _selectedMarket = _availableMarkets[0];

                await RefreshProvidersAsync();
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

            _availableProviders = await _dataService.LoadProvidersByMarketTypeAsync(_selectedMarket);

            // Ensure well-known providers appear in their canonical market even if the
            // data service returned an empty list (e.g., providers not yet configured).
            switch (_selectedMarket)
            {
                case "Crypto":
                    EnsureContains(_availableProviders, "Binance", "Bitstamp", "Coinbase");
                    break;
                case "Economic":
                case "OnChain":
                    EnsureContains(_availableProviders, "Fred");
                    break;
                case "Stock":
                case "Forex":
                    EnsureContains(_availableProviders, "Alpaca", "Polygon");
                    break;
            }

            if (string.IsNullOrEmpty(_selectedProvider) || !_availableProviders.Contains(_selectedProvider))
                _selectedProvider = _availableProviders.FirstOrDefault() ?? "";

            await RefreshSymbolsAsync();
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
            bool requiresKey = await _dataService.ProviderRequiresApiKeyAsync(_selectedProvider);
            bool isConfigured = await _dataService.IsProviderConfiguredAsync(_selectedProvider);

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
            _availableSubTypes = await _dataService.GetSupportedSubTypesAsync(_selectedProvider, _selectedMarket);
            if (_availableSubTypes.Count == 0) _availableSubTypes = new List<string> { "Spot" };
            if (!_availableSubTypes.Contains(_selectedSubType)) _selectedSubType = _availableSubTypes[0];

            // Pass the sub-type as "Market|SubType" so DataService routes symbol fetch correctly.
            string marketKey = _availableSubTypes.Count > 1
                ? $"{_selectedMarket}|{_selectedSubType}"
                : _selectedMarket;

            _availableSymbols = await _dataService.LoadSymbolsAsync(marketKey, _selectedProvider);
            _availableTimeframes = await _dataService.GetSupportedTimeframesAsync(_selectedProvider);

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

            // On a fresh tab (no series yet), seed the default series (Candles / Volume / Price)
            // before loading data. WorkspaceInitializer's guard makes this a no-op on existing tabs.
            _workspaceInitializer.InitializeDefaultSeries();

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
                await _dataManager.RefreshDataAsync();
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
