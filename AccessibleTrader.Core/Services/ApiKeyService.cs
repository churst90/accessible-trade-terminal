using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    // Local struct to save metadata only to disk (credentials stored separately in SecureStorage)
    internal record ApiKeyMetadata(string Provider, string Nickname, string MarketType,
        string Environment = "Paper", bool IsActive = false);

    public class ApiKeyService : IApiKeyService
    {
        private readonly string _filePath;
        private readonly ILogger<ApiKeyService> _logger;
        private readonly ISecureStorageService _secureStorage;
        private List<ApiKeyMetadata> _cache = new();
        private readonly SemaphoreSlim _loadLock = new(1, 1);
        private bool _isLoaded;

        public ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage)
        {
            _logger = logger;
            _secureStorage = secureStorage;
            _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "apikeys_meta.json");
        }

        private async Task EnsureLoadedAsync()
        {
            if (_isLoaded) return;
            await _loadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isLoaded) return;
                if (File.Exists(_filePath))
                {
                    var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                    _cache = JsonSerializer.Deserialize<List<ApiKeyMetadata>>(json) ?? new List<ApiKeyMetadata>();
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading API keys metadata from {Path}.", _filePath);
            }
            finally
            {
                _loadLock.Release();
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                await AtomicFile.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving API keys metadata.");
            }
        }

        public async Task<List<ApiKeyConfig>> GetAllKeysAsync()
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var results = new List<ApiKeyConfig>();
            foreach (var meta in _cache)
            {
                string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key").ConfigureAwait(false) ?? "";
                string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret").ConfigureAwait(false) ?? "";
                string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase").ConfigureAwait(false) ?? "";
                results.Add(new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive));
            }
            return results;
        }

        public async Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var metas = _cache.Where(k => k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToList();
            var results = new List<ApiKeyConfig>();
            foreach (var meta in metas)
            {
                string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key").ConfigureAwait(false) ?? "";
                string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret").ConfigureAwait(false) ?? "";
                string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase").ConfigureAwait(false) ?? "";
                results.Add(new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive));
            }
            return results;
        }

        public async Task<ApiKeyConfig?> GetKeyForProviderAsync(string provider, string marketType = "Spot")
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = _cache.FirstOrDefault(k =>
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.MarketType.Equals(marketType, StringComparison.OrdinalIgnoreCase));

            // Fallback: if no profile matches this market sub-type, accept any profile
            // for the provider (preferring an active one). MarketType is informational
            // — the data path looks keys up by sub-type ("Spot"/"Futures"), yet the
            // API Keys modal also offers market names ("Crypto"/"Stocks") that never
            // equal a sub-type, so an exact-match-only lookup would strand a good key.
            if (meta == null)
                meta = _cache.FirstOrDefault(k =>
                            k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && k.IsActive)
                    ?? _cache.FirstOrDefault(k =>
                            k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

            if (meta == null) return null;

            string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key").ConfigureAwait(false) ?? "";
            string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret").ConfigureAwait(false) ?? "";
            string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase").ConfigureAwait(false) ?? "";

            return new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive);
        }

        public async Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper")
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = _cache.FirstOrDefault(k =>
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) &&
                k.IsActive);

            if (meta == null) return null;

            string key    = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key").ConfigureAwait(false) ?? "";
            string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret").ConfigureAwait(false) ?? "";
            string pass   = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase").ConfigureAwait(false) ?? "";

            return new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive);
        }

        public async Task SetActiveKeyAsync(string nickname)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var target = _cache.FirstOrDefault(k => k.Nickname == nickname);
            if (target == null) return;

            // Deactivate other profiles for same provider+environment, activate this one.
            for (int i = 0; i < _cache.Count; i++)
            {
                var m = _cache[i];
                if (m.Provider.Equals(target.Provider, StringComparison.OrdinalIgnoreCase) &&
                    m.Environment.Equals(target.Environment, StringComparison.OrdinalIgnoreCase))
                {
                    _cache[i] = m with { IsActive = m.Nickname == nickname };
                }
            }
            await SaveAsync().ConfigureAwait(false);
        }

        public async Task SaveKeyAsync(ApiKeyConfig config)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var existing = _cache.FirstOrDefault(k => k.Nickname == config.Nickname);
            if (existing != null) _cache.Remove(existing);

            _cache.Add(new ApiKeyMetadata(config.Provider, config.Nickname, config.MarketType, config.Environment, config.IsActive));

            try
            {
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_key", config.ApiKey ?? "").ConfigureAwait(false);
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_secret", config.ApiSecret ?? "").ConfigureAwait(false);
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_passphrase", config.Passphrase ?? "").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SecureStorage write failed for {Nickname}.", config.Nickname);
                throw;
            }

            await SaveAsync().ConfigureAwait(false);
        }

        public async Task RemoveKeyAsync(string nickname)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var existing = _cache.FirstOrDefault(k => k.Nickname == nickname);
            if (existing != null)
            {
                _cache.Remove(existing);
                _secureStorage.Remove($"apikey_{nickname}_key");
                _secureStorage.Remove($"apikey_{nickname}_secret");
                _secureStorage.Remove($"apikey_{nickname}_passphrase");
                await SaveAsync().ConfigureAwait(false);
            }
        }
    }
}
