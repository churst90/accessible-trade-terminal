using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Collections.Concurrent;

namespace AccessibleTrader.Tests;

/// <summary>
/// The API-keys modal writes a PROVIDER NAME into the credential store, and every later
/// lookup resolves a provider object by that name. When the two spellings differ the key
/// is stored, the modal reports success — and the symbol dropdown goes on saying
/// "API key required" forever, because nothing on the path says a word about the mismatch.
///
/// <para>
/// This is the reported defect: the modal offered <c>"TwelveData"</c> while the provider
/// calls itself <c>"Twelve Data"</c>. A space. <see cref="ProviderNameLiteralTests"/>
/// already guards the same class of bug for MarketOrchestrator and DemoPolicy — the
/// credential dropdown was simply never enrolled, which is why this one survived.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public sealed class ApiKeysProviderNameTests
{
    // ── Harness ───────────────────────────────────────────────────────────────

    private sealed class InMemorySecureStorage : ISecureStorageService
    {
        private readonly ConcurrentDictionary<string, string> _store = new();
        public Task<string?> GetAsync(string key)
            => Task.FromResult(_store.TryGetValue(key, out var v) ? v : (string?)null);
        public Task SetAsync(string key, string value) { _store[key] = value; return Task.CompletedTask; }
        public void Remove(string key) => _store.TryRemove(key, out _);
    }

    /// <summary>
    /// Stands in for the real Twelve Data plugin: same Name, same RequiresApiKey, same
    /// "configured means a key arrived" rule — but no network, so the test can run the real
    /// DataService and the real orchestrator cascade end to end.
    /// </summary>
    private sealed class KeyedStockProvider : BaseMarketDataProvider
    {
        private string _apiKey = "";
        public override string Name => "Twelve Data";
        public override string Description => "test double for the real Twelve Data plugin";
        public override List<MarketType> SupportedMarkets => new() { MarketType.Stock };
        public override bool SupportsSymbolSearch => false;
        public override bool RequiresApiKey => true;
        public override bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
        public override bool SupportsLiveUpdates => false;
        public override ProviderEnvironment Environment => ProviderEnvironment.Live;
        public override int MaxBarsPerRequest => 100;
        public override List<string> NativelySupportedTimeframes => new() { "1h", "1d" };
        public override void Configure(Dictionary<string, string> config)
        {
            if (config.TryGetValue("ApiKey", out var k)) _apiKey = k;
        }
        public override Task EnsureConnectedAsync() => Task.CompletedTask;
        public override Task SetSubscriptionAsync(string market, string symbol, string timeframe) => Task.CompletedTask;
        public override Task DisconnectAsync() => Task.CompletedTask;
        public override Task<List<string>> GetAvailableSymbolsAsync(MarketType market, string subType = "Spot")
            => Task.FromResult(new List<string> { "AAPL", "MSFT" });
        public override Task<List<string>> GetSupportedSubTypesAsync(MarketType market)
            => Task.FromResult(new List<string> { "Spot" });
        public override Task<List<string>> GetSupportedTimeframesAsync()
            => Task.FromResult(new List<string> { "1h", "1d" });
        public override Task<(List<Ohlcv> Ohlcv, List<(long Timestamp, double Volume)> Volume)> FetchOhlcvAsync(MarketDataRequest request)
            => Task.FromResult((new List<Ohlcv>(), new List<(long, double)>()));
        public override Task<(List<OrderBookEntry> Bids, List<OrderBookEntry> Asks)> GetOrderBookAsync(string symbol, int limit = 10)
            => Task.FromResult((new List<OrderBookEntry>(), new List<OrderBookEntry>()));
    }

    /// <summary>The exact spelling the shipped API-keys dropdown wrote into the store.</summary>
    private const string ModalSpelling = "TwelveData";

    /// <summary>What the provider actually calls itself.</summary>
    private const string ProviderSpelling = "Twelve Data";

    private static async Task<(DataService Data, ApiKeyService Keys, KeyedStockProvider Provider)> BuildAsync()
    {
        var loader = Substitute.For<IPluginLoaderService>();
        loader.LoadPlugins<IMarketDataProvider>(Arg.Any<string>()).Returns(_ => new List<IMarketDataProvider>());

        var keys = new ApiKeyService(NullLogger<ApiKeyService>.Instance, new InMemorySecureStorage(),
                                     TestTemp.NewPath("at-apikeys-name-") + ".json");

        var data = new DataService(loader, NullLogger<DataService>.Instance,
                                   Substitute.For<ICacheService>(), keys);
        await data.InitializeAsync(loader);

        var provider = new KeyedStockProvider();
        data.RegisterProvider(provider);
        return (data, keys, provider);
    }

    private static ApiKeyConfig Profile(string provider) =>
        new(provider, "my twelve data", "REAL-KEY", "", "", "Spot", "Live", IsActive: true);

    // ── The defect ────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole bug in one assertion: a key saved through the modal must configure the
    /// provider it was saved FOR. Before the fix the metadata said "TwelveData", the
    /// provider answered to "Twelve Data", and the lookup returned null — silently, with a
    /// <c>continue</c> that logged nothing.
    /// </summary>
    [Fact]
    public async Task KeySavedUnderTheModalsSpelling_ConfiguresTheRealProvider()
    {
        var (data, keys, provider) = await BuildAsync();

        await keys.SaveKeyAsync(Profile(ModalSpelling));
        await data.ConfigureStoredKeyProvidersAsync();

        Assert.True(provider.IsConfigured,
            $"A key stored under '{ModalSpelling}' never reached the provider named "
          + $"'{ProviderSpelling}', so the provider stayed unconfigured and the symbol "
          + "dropdown keeps demanding a key the user already supplied.");
        Assert.True(data.IsProviderConfigured(ProviderSpelling));
    }

    /// <summary>
    /// The symptom the user actually reported, driven through the real cascade: add the
    /// key, and the "API key required" sentinel has to go away.
    /// </summary>
    [Fact]
    public async Task AddingTheKey_ClearsTheApiKeyRequiredSentinel()
    {
        var (data, keys, _) = await BuildAsync();
        var orch = NewOrchestrator(data);

        await orch.RefreshPipelineAsync();
        orch.SelectedMarket = "Stock";
        await orch.RefreshProvidersAsync();

        // Baseline: with no key at all the sentinel is correct and expected.
        Assert.Equal(ProviderSpelling, orch.SelectedProvider);
        Assert.Contains(MarketOrchestrator.ApiKeyRequiredSentinel, orch.AvailableSymbols);

        // Exactly what the modal does on Save Profile.
        await keys.SaveKeyAsync(Profile(ModalSpelling));
        await data.ConfigureStoredKeyProvidersAsync();
        await orch.RefreshSymbolsAsync();

        Assert.DoesNotContain(MarketOrchestrator.ApiKeyRequiredSentinel, orch.AvailableSymbols);
        Assert.Contains("AAPL", orch.AvailableSymbols);
    }

    /// <summary>
    /// The store's own lookups have to answer across the same spelling difference, or the
    /// data path re-configures the provider from nothing on every fetch and the trading
    /// path finds no credential at all.
    /// </summary>
    [Fact]
    public async Task KeyLookups_ResolveAcrossTheSpellingDifference()
    {
        var keys = new ApiKeyService(NullLogger<ApiKeyService>.Instance, new InMemorySecureStorage(),
                                     TestTemp.NewPath("at-apikeys-name-") + ".json");
        await keys.SaveKeyAsync(Profile(ModalSpelling));

        Assert.NotNull(await keys.GetKeyForProviderAsync(ProviderSpelling));
        Assert.NotNull(await keys.GetActiveKeyForProviderAsync(ProviderSpelling, "Live"));
        Assert.NotEmpty(await keys.GetKeysForProviderAsync(ProviderSpelling));
    }

    /// <summary>
    /// Loose matching must stay a FALLBACK, never a merge: FMP and FMP Analytics are two
    /// providers with two keys, and a match that collapsed them would hand one provider the
    /// other's credential.
    /// </summary>
    [Fact]
    public async Task DistinctProviders_AreStillDistinct()
    {
        var keys = new ApiKeyService(NullLogger<ApiKeyService>.Instance, new InMemorySecureStorage(),
                                     TestTemp.NewPath("at-apikeys-name-") + ".json");
        await keys.SaveKeyAsync(new ApiKeyConfig("FMP", "fmp", "FMP-KEY", "", "", "Spot", "Live", true));
        await keys.SaveKeyAsync(new ApiKeyConfig("FMP Analytics", "fmpa", "ANALYTICS-KEY", "", "", "Spot", "Live", true));

        Assert.Equal("FMP-KEY", (await keys.GetKeyForProviderAsync("FMP"))?.ApiKey);
        Assert.Equal("ANALYTICS-KEY", (await keys.GetKeyForProviderAsync("FMP Analytics"))?.ApiKey);
        Assert.Equal("ANALYTICS-KEY", (await keys.GetKeyForProviderAsync("FMPAnalytics"))?.ApiKey);
    }

    // ── The guard that was missing ────────────────────────────────────────────

    /// <summary>
    /// The literal guard, stated against the providers themselves rather than against
    /// another list: every provider that declares it needs a key must be offerable in the
    /// API-keys dropdown, spelled EXACTLY as the provider spells itself. Exact, not
    /// tolerant — the runtime tolerance added with this fix is a repair for stored data,
    /// and letting it excuse a new typo here would just move the bug somewhere quieter.
    ///
    /// <para>
    /// This is what would have caught "TwelveData" before it shipped, and it also catches
    /// the second, quieter hole the same audit found: FMP Analytics takes a key and was
    /// missing from the list entirely, so its key could only ever be saved under "FMP".
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "ProviderRoster")]
    public void EveryKeyRequiringProvider_IsOfferedByTheApiKeysDropdown()
    {
        var offered = ApiKeysModal.FallbackProviders.ToHashSet(StringComparer.Ordinal);

        var missing = ProviderRoster.All()
            .Where(p => p.RequiresApiKey)
            .Select(p => p.Name)
            .Where(name => !offered.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "These providers require an API key but the API-keys dropdown does not offer their "
          + "exact Name, so a key saved through it is stored against a provider nothing answers "
          + $"to: [{string.Join(", ", missing)}]. Add the name exactly as the provider spells it.");
    }

    /// <summary>
    /// The anti-vacuity half: a roster that found no key-requiring providers would make the
    /// test above pass by iterating nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "ProviderRoster")]
    public void TheRosterActuallyContainsKeyRequiringProviders()
    {
        var keyed = ProviderRoster.All().Where(p => p.RequiresApiKey).Select(p => p.Name).ToList();

        Assert.True(keyed.Count >= 8,
            $"Only {keyed.Count} key-requiring providers found — the sweep above is close to "
          + $"vacuous. Found: {string.Join(", ", keyed)}");
        Assert.Contains("Twelve Data", keyed);
    }

    /// <summary>
    /// The list must not be the only source of truth ever again. The live dropdown is built
    /// from the providers the host actually loaded, so a provider that ships tomorrow needs
    /// nobody to remember to type its name in.
    /// </summary>
    [Fact]
    public void TheDropdownIsBuiltFromTheLoadedProviders_NotOnlyFromTheList()
    {
        string src = File.ReadAllText(Path.Combine(
            RepoPaths.RepoRoot(), "AccessibleTrader.BlazorClient.Components", "ApiKeysModal.razor"));

        Assert.Contains("BuildProviderChoicesAsync", src);
        Assert.Contains("DataService.LoadProvidersAsync()", src);
        Assert.Contains("_knownProviders = await BuildProviderChoicesAsync()", src);
    }

    private static MarketOrchestrator NewOrchestrator(IDataService data)
    {
        var store = new WorkspaceStore(
            new EventBus(), new ViewportRangeCalculator(),
            new ViewportNavigationService(), new VolumeStateService());

        return new MarketOrchestrator(
            data, Substitute.For<IDataManager>(), store,
            Substitute.For<IWorkspaceInitializer>(), new EventBus(), new DemoPolicy(HostMode.Full));
    }
}
