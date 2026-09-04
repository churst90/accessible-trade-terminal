using System.Collections.Concurrent;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The fetch chokepoint's symbol check, and the one provider it must not apply to.
    ///
    /// <para>
    /// <c>SymbolValidator</c>'s charset — <c>[A-Za-z0-9_./\-:]</c> — is a security boundary: a
    /// symbol becomes a path segment or a query value in a signed request, so anything that could
    /// introduce one is rejected before a URL is built. That is correct, and these tests pin it.
    /// </para>
    ///
    /// <para>
    /// It was also rejecting the "My Data" provider, whose symbols are the user's own dataset
    /// names and whose data is a CSV read out of the app-data directory by dataset id — no URL
    /// anywhere. Measured 2026-09-04 in the browser harness, where a seeded dataset called
    /// "Harness Candles" produced <c>"Invalid symbol 'Harness Candles' for My Data. No data for
    /// Harness Candles from My Data. The chart is empty."</c> A space was enough. Every
    /// Values-shaped dataset was unreachable outright, because its symbol is
    /// <c>"{dataset} — {column}"</c> and carries an em dash too.
    /// </para>
    /// </summary>
    public class SymbolChokepointExemptionTests
    {
        // ── Harness ──────────────────────────────────────────────────────────────

        /// <summary>Records every fetch that reached it — which is how "the chokepoint let this
        /// through" is told apart from "the chokepoint rejected it and returned empty", since
        /// both hand the caller back an empty list.</summary>
        private sealed class RecordingFetcher : HistoricalDataFetcher
        {
            public readonly ConcurrentQueue<string> Symbols = new();
            public RecordingFetcher() : base(null!, null!, null!, null!) { }

            public override Task<List<Ohlcv>> FetchOhlcvAsync(
                string market, string provider, string symbol, string timeframe,
                long? since = null, int? limit = null, long? until = null, CancellationToken ct = default)
            {
                Symbols.Enqueue(symbol);
                return Task.FromResult(new List<Ohlcv>());
            }
        }

        /// <summary>
        /// A real class rather than a substitute, deliberately: <c>SymbolsAreUrlBound</c> is a
        /// default interface member anchored as a virtual on <see cref="BaseMarketDataProvider"/>,
        /// and a mocking framework resolving the DIM instead of the override would report the
        /// default and hide exactly the bug the anchor exists to prevent.
        /// </summary>
        private sealed class StubProvider : BaseMarketDataProvider
        {
            private readonly bool _urlBound;
            public StubProvider(string name, bool urlBound) { Name = name; _urlBound = urlBound; }

            public override bool SymbolsAreUrlBound => _urlBound;

            public override string Name { get; }
            public override string Description => "stub";
            public override List<MarketType> SupportedMarkets => new() { MarketType.Crypto };
            public override bool SupportsSymbolSearch => false;
            public override bool RequiresApiKey => false;
            public override bool IsConfigured => true;
            public override bool SupportsLiveUpdates => false;
            public override ProviderEnvironment Environment => ProviderEnvironment.HistoricalOnly;
            public override int MaxBarsPerRequest => 1000;
            public override List<string> NativelySupportedTimeframes => new() { "1d" };
            public override void Configure(Dictionary<string, string> config) { }
            public override Task EnsureConnectedAsync() => Task.CompletedTask;
            public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
            public override Task DisconnectAsync() => Task.CompletedTask;
            public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot") => Task.FromResult(new List<string>());
            public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market) => Task.FromResult(new List<string> { "Spot" });
            public override Task<List<string>> GetSupportedTimeframesAsync() => Task.FromResult(new List<string> { "1d" });
            public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
        }

        private const string SpacedSymbol = "Harness Candles";
        private const string ColumnSymbol = "Budget — Spending";

        /// <summary>
        /// Hand-written, and it has to be. <c>Substitute.For&lt;IDataService&gt;()</c> was the first
        /// version of this harness, and it made <see cref="An_unknown_provider_is_not_exempt"/>
        /// FAIL for a reason that had nothing to do with production: NSubstitute auto-substitutes
        /// for an unconfigured member returning a pure-virtual interface, so
        /// <c>GetProviderAsync("Some Other Venue")</c> handed back a live substitute rather than
        /// the null the real service returns — and on that substitute
        /// <c>SymbolsAreUrlBound</c>, a DEFAULT INTERFACE MEMBER, read as <c>false</c>, which is
        /// "exempt". The double is what decided the answer. Nothing configured here returns
        /// anything but what a real lookup would.
        /// </summary>
        private sealed class StubDataService : IDataService
        {
            private readonly Dictionary<string, IMarketDataProvider> _byName =
                new(StringComparer.OrdinalIgnoreCase);
            public StubDataService(IEnumerable<StubProvider> providers)
            {
                foreach (var p in providers) _byName[p.Name] = p;
            }

            public Task<IMarketDataProvider?> GetProviderAsync(string name) =>
                Task.FromResult(_byName.TryGetValue(name, out var p) ? p : null);

            public Task InitializeAsync(IPluginLoaderService pluginLoader) => Task.CompletedTask;
            public void RegisterProvider(IMarketDataProvider provider) => _byName[provider.Name] = provider;
            public Task ConfigureStoredKeyProvidersAsync() => Task.CompletedTask;
            public Task<List<string>> LoadAvailableMarketsAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersAsync() => Task.FromResult(new List<string>());
            public Task<List<string>> LoadProvidersByMarketTypeAsync(string marketType) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedSubTypesAsync(string provider, string marketType) => Task.FromResult(new List<string>());
            public Task<List<string>> LoadSymbolsAsync(string marketInfo, string provider) => Task.FromResult(new List<string>());
            public Task<List<string>> GetSupportedTimeframesAsync(string provider) => Task.FromResult(new List<string>());
            public Task<bool> IsProviderConfiguredAsync(string provider) => Task.FromResult(true);
            public bool IsProviderConfigured(string provider) => true;
            public Task<bool> ProviderRequiresApiKeyAsync(string provider) => Task.FromResult(false);
            public Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(
                string provider, MarketDataRequest request, CancellationToken ct = default)
                => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
            public Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(
                string provider, string symbol, int limit = 10)
                => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
            public Task<List<MarketType>> GetSupportedMarketsForProviderAsync(string provider) => Task.FromResult(new List<MarketType>());
            public Task<IProviderPlugin?> GetPluginAsync(string name) => Task.FromResult<IProviderPlugin?>(null);
        }

        private static DataOrchestrator Build(
            RecordingFetcher fetcher, out SpyEventBus bus, params StubProvider[] providers)
        {
            bus = new SpyEventBus();
            IDataService? svc = providers.Length > 0 ? new StubDataService(providers) : null;
            return new DataOrchestrator(fetcher, new MockLiveStreamManager(), bus,
                NullLogger<DataOrchestrator>.Instance, new DemoPolicy(isDemo: false), svc);
        }

        private static bool SaidInvalidSymbol(SpyEventBus bus) =>
            bus.Log.OfType<FeedbackRequestEvent>()
               .Any(f => f.Message.Contains("Invalid symbol", StringComparison.Ordinal));

        // ── The defect ───────────────────────────────────────────────────────────

        /// <summary>
        /// The measured browser failure, reproduced at the unit level. Both of these are symbols
        /// a real user creates: a dataset called "My Budget", and any column of a Values-shaped
        /// dataset, whose symbol is composed with an em dash by the provider itself.
        /// </summary>
        [Theory]
        [InlineData(SpacedSymbol)]
        [InlineData(ColumnSymbol)]
        public async Task A_local_file_providers_symbol_reaches_the_fetcher(string symbol)
        {
            var fetcher = new RecordingFetcher();
            var o = Build(fetcher, out var bus, new StubProvider("My Data", urlBound: false));

            await o.FetchOhlcvAsync("MyData", "My Data", symbol, "1d");

            Assert.Contains(symbol, fetcher.Symbols);
            Assert.False(SaidInvalidSymbol(bus),
                "The terminal told the user their own dataset name was an invalid symbol.");
        }

        /// <summary>
        /// The vacuity partner, and the security property. If this ever goes green-by-passing an
        /// arbitrary symbol to a venue, the exemption has stopped being an exemption.
        /// </summary>
        [Theory]
        [InlineData(SpacedSymbol)]
        [InlineData("BTC/USD?x=1")]
        [InlineData("../../etc/passwd")]
        public async Task A_url_bound_providers_symbol_is_still_rejected(string symbol)
        {
            var fetcher = new RecordingFetcher();
            var o = Build(fetcher, out var bus, new StubProvider("Kraken", urlBound: true));

            await o.FetchOhlcvAsync("Crypto", "Kraken", symbol, "1d");

            Assert.Empty(fetcher.Symbols);
            Assert.True(SaidInvalidSymbol(bus));
        }

        /// <summary>
        /// The exemption must be DECLARED, never assumed. With no data service the orchestrator
        /// cannot ask, and "cannot ask" has to mean "reject" — the alternative is a construction
        /// site that silently opts every provider out of the chokepoint.
        /// </summary>
        [Fact]
        public async Task An_orchestrator_that_cannot_resolve_the_provider_still_rejects()
        {
            var fetcher = new RecordingFetcher();
            var o = Build(fetcher, out var bus);   // no IDataService

            await o.FetchOhlcvAsync("MyData", "My Data", SpacedSymbol, "1d");

            Assert.Empty(fetcher.Symbols);
            Assert.True(SaidInvalidSymbol(bus));
        }

        /// <summary>
        /// A provider the data service does not know about is not exempt either — the null
        /// branch of the lookup is a distinct path from the "no data service at all" one above,
        /// and only one of them is covered by that test.
        /// </summary>
        [Fact]
        public async Task An_unknown_provider_is_not_exempt()
        {
            var fetcher = new RecordingFetcher();
            var o = Build(fetcher, out var bus, new StubProvider("My Data", urlBound: false));

            await o.FetchOhlcvAsync("Crypto", "Some Other Venue", SpacedSymbol, "1d");

            Assert.Empty(fetcher.Symbols);
            Assert.True(SaidInvalidSymbol(bus));
        }

        // ── The declaration itself ───────────────────────────────────────────────

        /// <summary>
        /// The default is the safe one, and the one provider that opts out is the one that reads
        /// local files. A new provider inherits the chokepoint by saying nothing.
        /// </summary>
        [Fact]
        public void Only_the_local_file_provider_opts_out_of_the_chokepoint()
        {
            Assert.False(new StubProvider("x", urlBound: false).SymbolsAreUrlBound);
            Assert.True(new StubProvider("x", urlBound: true).SymbolsAreUrlBound);

            var myData = new Core.Services.MyData.MyDataProvider(
                Substitute.For<Core.Services.MyData.IMyDataStore>());
            Assert.False(myData.SymbolsAreUrlBound,
                "My Data charts a CSV by dataset id and builds no URL from the symbol.");

            // Read through the INTERFACE, which is what DataOrchestrator holds. A derived
            // override that does not resolve through the base's virtual anchor is a shadow
            // property: correct when read off the concrete type and wrong here.
            IMarketDataProvider asInterface = myData;
            Assert.False(asInterface.SymbolsAreUrlBound);
        }

        /// <summary>The charset itself is unchanged — the fix is an exemption, not a widening.</summary>
        [Fact]
        public void The_validator_charset_is_unchanged()
        {
            Assert.False(SymbolValidator.IsValid(SpacedSymbol));
            Assert.False(SymbolValidator.IsValid(ColumnSymbol));
            Assert.True(SymbolValidator.IsValid("BTC/USD"));
            Assert.True(SymbolValidator.IsValid("AAPL:NASDAQ"));
        }
    }
}
