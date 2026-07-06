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
        string SelectedAnalyticsType { get; set; }
        string SelectedProvider { get; set; }
        string SelectedSubType { get; set; }
        string SelectedSymbol { get; set; }
        string SelectedTimeframe { get; set; }

        IReadOnlyList<string> AvailableMarkets { get; }
        IReadOnlyList<string> AvailableAnalyticsTypes { get; }
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
        private string _selectedAnalyticsType = "";
        private List<string> _availableAnalyticsTypes = new();

        /// <summary>
        /// Umbrella entry shown in the Market dropdown that groups every non-tradeable
        /// data category. Selecting it reveals the Analytics-type dropdown (Economic,
        /// OnChain, …). Replaces the old Trading/Analytics mode toggle: the market
        /// choice is now the single source of truth for trading-vs-analytics.
        /// </summary>
        public const string AnalyticsMarket = "Analytics";

        // The non-tradeable data categories. Each hosts analytics providers whose data is
        // SingleValueLine rather than OHLCV. New categories join here so they surface under
        // the Analytics umbrella instead of leaking into the tradeable market list.
        private static readonly string[] AnalyticsCategories =
            { "Economic", "OnChain", "Derivatives", "Sentiment" };

        private static bool IsAnalyticsCategory(string market) =>
            AnalyticsCategories.Contains(market, StringComparer.OrdinalIgnoreCase);

        // The real category key that drives provider/symbol loading and the chart identity:
        // the chosen analytics type when the umbrella is selected, otherwise the market itself.
        private string EffectiveMarket =>
            string.Equals(_selectedMarket, AnalyticsMarket, StringComparison.OrdinalIgnoreCase)
                ? _selectedAnalyticsType
                : _selectedMarket;

        public MarketState CurrentState => _stateMachine.CurrentState;
        public IObservable<MarketState> StateChanged => _stateMachine.StateChanged;

        // Simple properties — callers use the explicit Refresh* methods to trigger
        // the async cascade and surface exceptions rather than fire-and-forget setters.
        public string SelectedMarket
        {
            get => _selectedMarket;
            set { _selectedMarket = value; }
        }

        public string SelectedAnalyticsType
        {
            get => _selectedAnalyticsType;
            set { _selectedAnalyticsType = value; }
        }

        public string SelectedProvider
        {
            get => _selectedProvider;
            // Curated-data guard (demo + hosted): reject a non-whitelisted provider so a
            // crafted request can't escape the server-keyed set even if the UI is bypassed.
            set { if (_demo.RestrictsData && !_demo.IsProviderAllowed(value)) return; _selectedProvider = value; }
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
        public IReadOnlyList<string> AvailableAnalyticsTypes => _availableAnalyticsTypes;
        public IReadOnlyList<string> AvailableProviders => _availableProviders;
        public IReadOnlyList<string> AvailableSubTypes => _availableSubTypes;
        public IReadOnlyList<string> AvailableSymbols => _availableSymbols;
        public IReadOnlyList<string> AvailableTimeframes => _availableTimeframes;

        public IObservable<Unit> PipelineUpdated => _pipelineUpdated.AsObservable();

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

                // Split the raw category list into tradeable markets and non-tradeable
                // analytics categories. Both used to be gated behind the Trading/Analytics
                // mode toggle; now they coexist in one unified dropdown.
                var tradeable      = allMarkets.Where(m => !IsAnalyticsCategory(m)).ToList();
                var analyticsTypes = allMarkets.Where(IsAnalyticsCategory).ToList();

                // Fallbacks: ensure a minimal set if the data service returned nothing.
                if (tradeable.Count == 0)
                    tradeable.AddRange(new[] { "Crypto", "Forex", "Stock" });
                if (analyticsTypes.Count == 0)
                    analyticsTypes.AddRange(AnalyticsCategories);

                // Demo/hosted whitelist: drop any category that has no whitelisted provider,
                // so a selection can never land on an empty provider list.
                if (_demo.RestrictsData)
                {
                    tradeable      = _demo.FilterMarkets(tradeable).ToList();
                    analyticsTypes = _demo.FilterMarkets(analyticsTypes).ToList();
                }

                _availableAnalyticsTypes = analyticsTypes;

                // Unified market list: every tradeable market, plus a single "Analytics"
                // umbrella entry when at least one analytics category survives. Picking it
                // exposes the Analytics-type dropdown that the mode toggle used to gate.
                _availableMarkets = new List<string>(tradeable);
                if (analyticsTypes.Count > 0)
                    _availableMarkets.Add(AnalyticsMarket);
                if (_availableMarkets.Count == 0)
                    _availableMarkets.Add("Crypto");   // absolute fallback

                if (string.IsNullOrEmpty(_selectedMarket) || !_availableMarkets.Contains(_selectedMarket))
                    _selectedMarket = _availableMarkets[0];

                // Keep the analytics-type selection valid so EffectiveMarket resolves the
                // moment the umbrella is (or becomes) the active market.
                if (_availableAnalyticsTypes.Count > 0
                    && (string.IsNullOrEmpty(_selectedAnalyticsType) || !_availableAnalyticsTypes.Contains(_selectedAnalyticsType)))
                    _selectedAnalyticsType = _availableAnalyticsTypes[0];

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

            // Resolve the umbrella "Analytics" selection to its concrete category before
            // loading providers, and keep the persisted TerminalMode in step so saved
            // workspaces round-trip. The market choice is now the single source of truth
            // for trading-vs-analytics (the manual mode toggle is gone).
            string market = EffectiveMarket;
            var desiredMode = IsAnalyticsCategory(market) ? TerminalMode.Analytics : TerminalMode.Trading;
            if (_store.State.Mode != desiredMode)
                _store.Dispatch(new ChangeModeAction(desiredMode));

            _availableProviders = await _dataService.LoadProvidersByMarketTypeAsync(market).ConfigureAwait(false);

            // Ensure well-known providers appear in their canonical market even if the
            // data service returned an empty list (e.g., providers not yet configured).
            switch (market)
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
                    EnsureContains(_availableProviders, "Twelve Data", "Alpaca", "Polygon", "FMP");
                    break;
                case "Forex":
                    EnsureContains(_availableProviders, "Twelve Data", "Alpaca", "Polygon", "FMP");
                    break;
                case "Commodity":
                case "Index":
                    EnsureContains(_availableProviders, "FMP");
                    break;
            }

            // Curated data (demo + hosted): pin each market to its single server-keyed provider.
            // (A flat provider whitelist isn't enough — Twelve Data also advertises Crypto, so it
            // would otherwise be selectable for BTC/USD where its free stream fails. And in hosted
            // there are no user keys, so the dozen key-required providers must not be offered.)
            if (_demo.RestrictsData)
            {
                var only = _demo.ProviderForMarket(market);
                _availableProviders = string.IsNullOrEmpty(only)
                    ? new List<string>()
                    : new List<string> { only };
            }

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

        /// <summary>
        /// Curated starter symbols for Twelve Data, whose free-tier symbol-list endpoints return
        /// empty even though its time_series charts these fine. Keyed by market. Used only when the
        /// provider returns no list; symbol search still lets the user chart any other ticker.
        /// </summary>
        private static List<string> TwelveDataStarterSymbols(string market) => (market ?? "").ToLowerInvariant() switch
        {
            "stock" => new List<string>
            {
                "AAPL", "MSFT", "GOOGL", "AMZN", "NVDA", "META", "TSLA", "NFLX", "AMD", "INTC",
                "JPM", "BAC", "V", "MA", "DIS", "KO", "PEP", "WMT", "XOM", "CVX", "BA", "PFE",
                "SPY", "QQQ", "DIA", "IWM"
            },
            "forex" => new List<string>
            {
                "EUR/USD", "GBP/USD", "USD/JPY", "USD/CHF", "AUD/USD", "USD/CAD", "NZD/USD",
                "EUR/GBP", "EUR/JPY", "GBP/JPY", "AUD/JPY", "EUR/CHF"
            },
            _ => new List<string>()
        };

        public async Task RefreshSymbolsAsync()
        {
            if (string.IsNullOrEmpty(_selectedProvider))
            {
                _availableSymbols = new List<string>();
                // In demo, never expose the full timeframe set on a fallback path.
                _availableTimeframes = _demo.IsDemo
                    ? _demo.AllowedTimeframes.ToList()
                    : new List<string> { "1m", "5m", "15m", "1h", "4h", "1d" };
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
                _availableTimeframes = _demo.IsDemo
                    ? _demo.AllowedTimeframes.ToList()
                    : new List<string> { "1h" };
                _pipelineUpdated.OnNext(Unit.Default);
                return;
            }

            // Resolve the umbrella "Analytics" selection to its concrete category for all
            // data-service keys below (sub-types, symbols, curated fallbacks).
            string market = EffectiveMarket;

            // Load sub-types (Spot/Futures/etc.) — only shown in the toolbar when count > 1.
            _availableSubTypes = await _dataService.GetSupportedSubTypesAsync(_selectedProvider, market).ConfigureAwait(false);
            if (_availableSubTypes.Count == 0) _availableSubTypes = new List<string> { "Spot" };
            if (!_availableSubTypes.Contains(_selectedSubType)) _selectedSubType = _availableSubTypes[0];

            // Pass the sub-type as "Market|SubType" so DataService routes symbol fetch correctly.
            string marketKey = _availableSubTypes.Count > 1
                ? $"{market}|{_selectedSubType}"
                : market;

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

            // Curated-data modes (demo + hosted, no user keys): Twelve Data's free-tier symbol
            // lists are unusable — /stocks comes back empty and /forex_pairs returns 1000+ obscure
            // pairs — so substitute a curated major-symbol list per market. These chart fine via
            // time_series, and symbol search still lets the user pull up any other valid ticker.
            if (_demo.RestrictsData
                && string.Equals(_selectedProvider, "Twelve Data", StringComparison.OrdinalIgnoreCase))
            {
                var curatedTd = TwelveDataStarterSymbols(market);
                if (curatedTd.Count > 0) _availableSymbols = curatedTd;
            }

            // Demo whitelist: restrict symbols to the curated set and force the two
            // demo timeframes, so the fixups below land on allowed values and no
            // other symbol/timeframe is reachable from the UI.
            if (_demo.IsDemo)
            {
                var curated = _demo.FilterSymbols(_selectedProvider, _availableSymbols).ToList();
                // Fall back to the curated symbol list when the provider's symbol-list
                // endpoint is empty (e.g. Twelve Data /stocks on the free tier). These
                // chart fine via the data endpoint even though the listing is empty.
                if (curated.Count == 0)
                    curated = _demo.DemoSymbolsForMarket(market).ToList();
                _availableSymbols = curated;
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
                ? $"{EffectiveMarket}|{_selectedSubType}"
                : EffectiveMarket;

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
            var targetMarket        = _selectedMarket;
            var targetAnalyticsType = _selectedAnalyticsType;
            var targetSubType       = _selectedSubType;
            var targetProvider      = _selectedProvider;
            var targetSymbol        = _selectedSymbol;
            var targetTimeframe     = _selectedTimeframe;

            // AddTabAction creates a blank tab and makes it active. The previous tab's
            // state is saved into a TabSnapshot and will be restored if the user switches
            // back — so the trading tab's indicators and drawings are never lost.
            _store.Dispatch(new AddTabAction());

            // The new tab starts empty; re-apply the toolbar selections (the orchestrator's
            // fields are global, not per-tab, so they already match what the user picked).
            _selectedMarket        = targetMarket;
            _selectedAnalyticsType = targetAnalyticsType;
            _selectedSubType       = targetSubType;
            _selectedProvider      = targetProvider;
            _selectedSymbol        = targetSymbol;
            _selectedTimeframe     = targetTimeframe;

            // Normal load — InitializeDefaultSeries will seed the correct core stack for
            // the new tab based on the target provider's shape.
            await LoadChartAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
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
