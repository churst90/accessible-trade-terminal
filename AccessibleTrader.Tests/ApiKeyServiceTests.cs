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
        string key = "k", string secret = "s", string pass = "p")
        => new(provider, nickname, key, secret, pass, market, env, active);

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
}
