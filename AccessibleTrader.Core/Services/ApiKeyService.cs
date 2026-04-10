using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

        public ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage)
        {
            _logger = logger;
            _secureStorage = secureStorage;
            _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleTrader", "apikeys_meta.json");
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = await File.ReadAllTextAsync(_filePath);
                    _cache = JsonSerializer.Deserialize<List<ApiKeyMetadata>>(json) ?? new List<ApiKeyMetadata>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading API keys metadata: {ex.Message}");
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving API keys metadata: {ex.Message}");
            }
        }

        public async Task<List<ApiKeyConfig>> GetAllKeysAsync()
        {
            await LoadAsync();
            var results = new List<ApiKeyConfig>();
            foreach (var meta in _cache)
            {
                string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key") ?? "";
                string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret") ?? "";
                string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase") ?? "";
                results.Add(new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive));
            }
            return results;
        }

        public async Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider)
        {
            await LoadAsync();
            var metas = _cache.Where(k => k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToList();
            var results = new List<ApiKeyConfig>();
            foreach (var meta in metas)
            {
                string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key") ?? "";
                string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret") ?? "";
                string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase") ?? "";
                results.Add(new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive));
            }
            return results;
        }

        public async Task<ApiKeyConfig?> GetKeyForProviderAsync(string provider, string marketType = "Spot")
        {
            await LoadAsync();
            var meta = _cache.FirstOrDefault(k =>
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.MarketType.Equals(marketType, StringComparison.OrdinalIgnoreCase));

            if (meta == null) return null;

            string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key") ?? "";
            string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret") ?? "";
            string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase") ?? "";

            return new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive);
        }

        public async Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper")
        {
            await LoadAsync();
            var meta = _cache.FirstOrDefault(k =>
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) &&
                k.IsActive);

            if (meta == null) return null;

            string key    = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key") ?? "";
            string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret") ?? "";
            string pass   = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase") ?? "";

            return new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType, meta.Environment, meta.IsActive);
        }

        public async Task SetActiveKeyAsync(string nickname)
        {
            await LoadAsync();
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
            await SaveAsync();
        }

        public async Task SaveKeyAsync(ApiKeyConfig config)
        {
            await LoadAsync();
            var existing = _cache.FirstOrDefault(k => k.Nickname == config.Nickname);
            if (existing != null) _cache.Remove(existing);

            _cache.Add(new ApiKeyMetadata(config.Provider, config.Nickname, config.MarketType, config.Environment, config.IsActive));

            try
            {
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_key", config.ApiKey ?? "");
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_secret", config.ApiSecret ?? "");
                await _secureStorage.SetAsync($"apikey_{config.Nickname}_passphrase", config.Passphrase ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError($"SecureStorage write failed for '{config.Nickname}': {ex.Message}");
                throw;
            }

            await SaveAsync();
        }

        public async Task RemoveKeyAsync(string nickname)
        {
            await LoadAsync();
            var existing = _cache.FirstOrDefault(k => k.Nickname == nickname);
            if (existing != null)
            {
                _cache.Remove(existing);
                _secureStorage.Remove($"apikey_{nickname}_key");
                _secureStorage.Remove($"apikey_{nickname}_secret");
                _secureStorage.Remove($"apikey_{nickname}_passphrase");
                await SaveAsync();
            }
        }
    }
}
