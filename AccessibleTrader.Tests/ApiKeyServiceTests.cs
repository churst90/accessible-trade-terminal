using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Direct tests for <see cref="ApiKeyService"/> — the component guarding
/// exchange credentials. Pins the Phase A hardening: metadata lives in
/// ISecureStorageService (encrypted at rest), never in plaintext JSON, with a
/// one-time migration from the legacy file; plus CRUD round-trips, active-flag
/// exclusivity, missing-secret null safety, and concurrent-mutation safety.
/// </summary>
public class ApiKeyServiceTests
{
    private sealed class InMemorySecureStorage : ISecureStorageService
    {
        public readonly ConcurrentDictionary<string, string> Store = new();
        public Task<string?> GetAsync(string key)
            => Task.FromResult(Store.TryGetValue(key, out var v) ? v : (string?)null);
        public Task SetAsync(string key, string value)
        {
            Store[key] = value;
            return Task.CompletedTask;
        }
        public void Remove(string key) => Store.TryRemove(key, out _);
    }

    private static string TempLegacyPath()
        => Path.Combine(Path.GetTempPath(), $"at-apikeys-test-{Guid.NewGuid():N}.json");

    private static ApiKeyService NewService(InMemorySecureStorage storage, string? legacyPath = null)
        => new(NullLogger<ApiKeyService>.Instance, storage, legacyPath ?? TempLegacyPath());

    private static ApiKeyConfig Config(string nickname, string provider = "Kraken",
        string market = "Spot", string env = "Live", bool active = false,
        string key = "k", string secret = "s", string pass = "p", bool withdrawal = false)
        => new(provider, nickname, key, secret, pass, market, env, active, withdrawal);

    [Fact]
    public async Task SaveKey_RoundTrips_MetadataAndSecrets()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);

        await svc.SaveKeyAsync(Config("main", key: "AK", secret: "SK", pass: "PP", active: true));

        var all = await svc.GetAllKeysAsync();
        var got = Assert.Single(all);
        Assert.Equal("Kraken", got.Provider);
        Assert.Equal("AK", got.ApiKey);
        Assert.Equal("SK", got.ApiSecret);
        Assert.Equal("PP", got.Passphrase);
        Assert.True(got.IsActive);
    }

    [Fact]
    public async Task Metadata_IsStoredInSecureStorage_NotOnDiskInPlaintext()
    {
        var storage = new InMemorySecureStorage();
        var legacy = TempLegacyPath();
        var svc = NewService(storage, legacy);

        await svc.SaveKeyAsync(Config("main"));

        // Encrypted-at-rest entry exists; no plaintext file is ever written.
        Assert.True(storage.Store.ContainsKey(ApiKeyService.MetaStorageKey));
        Assert.Contains("Kraken", storage.Store[ApiKeyService.MetaStorageKey]);
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public async Task LegacyPlaintextFile_IsMigratedIntoSecureStorage_AndDeleted()
    {
        var storage = new InMemorySecureStorage();
        var legacy = TempLegacyPath();
        // Shape of the pre-2026-07 plaintext metadata file.
        await File.WriteAllTextAsync(legacy,
            "[{\"Provider\":\"Coinbase\",\"Nickname\":\"old\",\"MarketType\":\"Spot\",\"Environment\":\"Live\",\"IsActive\":true}]");
        storage.Store["apikey_old_key"] = "legacy-key";

        try
        {
            var svc = NewService(storage, legacy);
            var all = await svc.GetAllKeysAsync();

            var got = Assert.Single(all);
            Assert.Equal("Coinbase", got.Provider);
            Assert.Equal("legacy-key", got.ApiKey);
            Assert.True(storage.Store.ContainsKey(ApiKeyService.MetaStorageKey));
            Assert.False(File.Exists(legacy)); // plaintext deleted only after encrypted write succeeded
        }
        finally
        {
            if (File.Exists(legacy)) File.Delete(legacy);
        }
    }

    [Fact]
    public async Task MissingSecrets_ComeBackAsEmptyStrings_NotNull()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("main"));
        // Simulate a secure-storage entry lost out-of-band (OS keyring reset).
        storage.Remove("apikey_main_secret");

        var got = Assert.Single(await svc.GetAllKeysAsync());
        Assert.Equal("", got.ApiSecret);
        Assert.Equal("k", got.ApiKey);
    }

    [Fact]
    public async Task RemoveKey_DeletesMetadataAndAllThreeSecrets()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("main"));

        await svc.RemoveKeyAsync("main");

        Assert.Empty(await svc.GetAllKeysAsync());
        Assert.False(storage.Store.ContainsKey("apikey_main_key"));
        Assert.False(storage.Store.ContainsKey("apikey_main_secret"));
        Assert.False(storage.Store.ContainsKey("apikey_main_passphrase"));
    }

    [Fact]
    public async Task SetActiveKey_ActivatesTarget_AndDeactivatesSiblings_SameProviderAndEnvironment()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("a", provider: "Kraken", env: "Live", active: true));
        await svc.SaveKeyAsync(Config("b", provider: "Kraken", env: "Live"));
        await svc.SaveKeyAsync(Config("c", provider: "Kraken", env: "Paper", active: true)); // other env untouched
        await svc.SaveKeyAsync(Config("d", provider: "Binance", env: "Live", active: true)); // other provider untouched

        await svc.SetActiveKeyAsync("b");

        var all = await svc.GetAllKeysAsync();
        Assert.False(all.Single(k => k.Nickname == "a").IsActive);
        Assert.True(all.Single(k => k.Nickname == "b").IsActive);
        Assert.True(all.Single(k => k.Nickname == "c").IsActive);
        Assert.True(all.Single(k => k.Nickname == "d").IsActive);
    }

    [Fact]
    public async Task GetKeyForProvider_FallsBackToActiveProfile_WhenMarketTypeDoesNotMatch()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("spot", market: "Crypto", active: true));

        // Lookup by sub-type "Spot" never equals market name "Crypto" —
        // the fallback must still find the active profile.
        var got = await svc.GetKeyForProviderAsync("Kraken", "Spot");
        Assert.NotNull(got);
        Assert.Equal("spot", got!.Nickname);
    }

    [Fact]
    public async Task SaveKey_ReplacesExistingProfileWithSameNickname()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("main", key: "old"));
        await svc.SaveKeyAsync(Config("main", key: "new"));

        var got = Assert.Single(await svc.GetAllKeysAsync());
        Assert.Equal("new", got.ApiKey);
    }

    // ── Withdrawal profiles ──────────────────────────────────────────────
    // The flag's enforcement on the lookup paths is pinned here; the checkbox in
    // the API Keys modal is only a way to set it.

    [Fact]
    public async Task AllowsWithdrawal_RoundTrips_ThroughSaveAndLoad()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);

        await svc.SaveKeyAsync(Config("wd", withdrawal: true));

        var got = Assert.Single(await svc.GetAllKeysAsync());
        Assert.True(got.AllowsWithdrawal);
    }

    [Fact]
    public async Task TradingLookups_NeverReturnAWithdrawalProfile_EvenWhenItIsTheOnlyOne()
    {
        // The generous fallbacks in GetKeyForProviderAsync are exactly the shape of
        // code that would quietly hand the withdrawal key to the order path.
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("wd", withdrawal: true, active: true));

        Assert.Null(await svc.GetKeyForProviderAsync("Kraken", "Spot"));
        Assert.Null(await svc.GetActiveKeyForProviderAsync("Kraken", "Live"));
    }

    [Fact]
    public async Task GetWithdrawalKey_FindsTheFlaggedProfile_AndOnlyThat()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("trade", active: true));
        await svc.SaveKeyAsync(Config("wd", withdrawal: true));

        var got = await svc.GetWithdrawalKeyAsync("Kraken");
        Assert.Equal("wd", got!.Nickname);
        Assert.Null(await svc.GetWithdrawalKeyAsync("Binance"));
    }

    [Fact]
    public async Task SetActiveKey_RefusesAWithdrawalProfile_AndLeavesTheTradingProfileActive()
    {
        // Activation means "use for trading sessions" — which a withdrawal profile
        // never is. Activating one would also deactivate the real trading profile
        // for the provider+environment, silently breaking trading.
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);
        await svc.SaveKeyAsync(Config("trade", active: true));
        await svc.SaveKeyAsync(Config("wd", withdrawal: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SetActiveKeyAsync("wd"));

        var all = await svc.GetAllKeysAsync();
        Assert.True(all.Single(k => k.Nickname == "trade").IsActive);
        Assert.False(all.Single(k => k.Nickname == "wd").IsActive);
    }

    [Fact]
    public async Task ConcurrentSaves_DoNotCorruptTheMetadataList()
    {
        var storage = new InMemorySecureStorage();
        var svc = NewService(storage);

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => svc.SaveKeyAsync(Config($"nick{i}"))));

        var all = await svc.GetAllKeysAsync();
        Assert.Equal(20, all.Count);
        Assert.Equal(20, all.Select(k => k.Nickname).Distinct().Count());
    }

    // ── DemoPolicy wall ──────────────────────────────────────────────────────
    // AllowApiKeysModal was enforced only in Razor @if markup; these pin the
    // service-layer wall. "Hosted" is the interesting mode: open registration,
    // and the host promises broker credentials are never held server-side.

    [Fact]
    public async Task Hosted_RefusesUserKeyMutations_AtTheServiceLayer()
    {
        var storage = new InMemorySecureStorage();
        var svc = new ApiKeyService(NullLogger<ApiKeyService>.Instance, storage,
            TempLegacyPath(), new DemoPolicy(HostMode.Hosted));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveKeyAsync(Config("main")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RemoveKeyAsync("main"));
        Assert.Empty(storage.Store);   // nothing persisted before the wall
    }

    [Fact]
    public async Task Hosted_StillAllowsTheServersOwnSeededKeys_AndReads()
    {
        // Program.cs seeds shared read-only data keys (Twelve Data, FRED) on the
        // demo/hosted heads. That path must survive the wall — and so must reads,
        // which is how the data pipeline uses the seeded key.
        var storage = new InMemorySecureStorage();
        var svc = new ApiKeyService(NullLogger<ApiKeyService>.Instance, storage,
            TempLegacyPath(), new DemoPolicy(HostMode.Hosted));

        await svc.SaveServerManagedKeyAsync(Config("demo", provider: "Twelve Data"));

        var got = await svc.GetKeyForProviderAsync("Twelve Data");
        Assert.NotNull(got);
        Assert.Equal("k", got!.ApiKey);
    }

    [Fact]
    public async Task FullMode_UserKeyMutations_StayAllowed()
    {
        var storage = new InMemorySecureStorage();
        var svc = new ApiKeyService(NullLogger<ApiKeyService>.Instance, storage,
            TempLegacyPath(), new DemoPolicy(HostMode.Full));

        await svc.SaveKeyAsync(Config("main"));
        Assert.Single(await svc.GetAllKeysAsync());
        await svc.RemoveKeyAsync("main");
        Assert.Empty(await svc.GetAllKeysAsync());
    }
}
