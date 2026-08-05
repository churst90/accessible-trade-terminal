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
    // Metadata for a key profile (credentials stored separately in SecureStorage).
    // Since 2026-07 the metadata list itself is ALSO stored via ISecureStorageService
    // (encrypted at rest) rather than as plaintext JSON on disk — the plaintext file
    // leaked which exchanges a user trades on and the profile nicknames/environments.
    internal record ApiKeyMetadata(string Provider, string Nickname, string MarketType,
        string Environment = "Paper", bool IsActive = false,
        // Appended with a default so existing stored metadata deserialises as
        // withdrawal-DISABLED. A migration that silently enabled it would be the
        // worst possible default on the one flag that moves money.
        bool AllowsWithdrawal = false);

    public class ApiKeyService : IApiKeyService
    {
        /// <summary>SecureStorage entry holding the serialized metadata list.</summary>
        internal const string MetaStorageKey = "apikeys_meta";

        private readonly string _legacyFilePath;
        private readonly ILogger<ApiKeyService> _logger;
        private readonly ISecureStorageService _secureStorage;
        private List<ApiKeyMetadata> _cache = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _isLoaded;

        public ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage)
            : this(logger, secureStorage,
                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "AccessibleTrader", "apikeys_meta.json"))
        {
        }

        /// <summary>Test seam: inject the legacy plaintext path so migration is verifiable.</summary>
        internal ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage, string legacyFilePath)
        {
            _logger = logger;
            _secureStorage = secureStorage;
            _legacyFilePath = legacyFilePath;
        }

        private async Task EnsureLoadedAsync()
        {
            if (_isLoaded) return;
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_isLoaded) return;

                var json = await _secureStorage.GetAsync(MetaStorageKey).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(json))
                {
                    _cache = JsonSerializer.Deserialize<List<ApiKeyMetadata>>(json) ?? new List<ApiKeyMetadata>();
                }
                else if (File.Exists(_legacyFilePath))
                {
                    // One-time migration from the pre-2026-07 plaintext metadata file.
                    // The plaintext copy is deleted only after the encrypted write
                    // succeeded, so a failed migration loses nothing.
                    var legacyJson = await File.ReadAllTextAsync(_legacyFilePath).ConfigureAwait(false);
                    _cache = JsonSerializer.Deserialize<List<ApiKeyMetadata>>(legacyJson) ?? new List<ApiKeyMetadata>();
                    await _secureStorage.SetAsync(MetaStorageKey, JsonSerializer.Serialize(_cache)).ConfigureAwait(false);
                    try
                    {
                        File.Delete(_legacyFilePath);
                        _logger.LogInformation("Migrated API key metadata from plaintext {Path} into secure storage.", _legacyFilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Migrated API key metadata into secure storage but could not delete the plaintext file {Path}. Delete it manually.", _legacyFilePath);
                    }
                }
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading API keys metadata.");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Persist the cache. Caller must hold <see cref="_lock"/>.</summary>
        private async Task SaveLockedAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache);
                await _secureStorage.SetAsync(MetaStorageKey, json).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving API keys metadata.");
            }
        }

        private async Task<ApiKeyConfig> ToConfigAsync(ApiKeyMetadata meta)
        {
            string key = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_key").ConfigureAwait(false) ?? "";
            string secret = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_secret").ConfigureAwait(false) ?? "";
            string pass = await _secureStorage.GetAsync($"apikey_{meta.Nickname}_passphrase").ConfigureAwait(false) ?? "";
            return new ApiKeyConfig(meta.Provider, meta.Nickname, key, secret, pass, meta.MarketType,
                                    meta.Environment, meta.IsActive, meta.AllowsWithdrawal);
        }

        public async Task<List<ApiKeyConfig>> GetAllKeysAsync()
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var results = new List<ApiKeyConfig>();
            foreach (var meta in _cache.ToList())
                results.Add(await ToConfigAsync(meta).ConfigureAwait(false));
            return results;
        }

        public async Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var metas = _cache.Where(k => k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToList();
            var results = new List<ApiKeyConfig>();
            foreach (var meta in metas)
                results.Add(await ToConfigAsync(meta).ConfigureAwait(false));
            return results;
        }

        public async Task<ApiKeyConfig?> GetKeyForProviderAsync(string provider, string marketType = "Spot")
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            // Withdrawal profiles are excluded from EVERY trading lookup, including
            // the fallbacks below. Separating the credentials is worth nothing if a
            // fallback quietly hands the withdrawal-enabled key to the order path —
            // and the fallbacks here are deliberately generous, which is exactly the
            // shape of mistake that would do it.
            var meta = _cache.FirstOrDefault(k =>
                !k.AllowsWithdrawal &&
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.MarketType.Equals(marketType, StringComparison.OrdinalIgnoreCase));

            // Fallback: if no profile matches this market sub-type, accept any profile
            // for the provider (preferring an active one). MarketType is informational
            // — the data path looks keys up by sub-type ("Spot"/"Futures"), yet the
            // API Keys modal also offers market names ("Crypto"/"Stocks") that never
            // equal a sub-type, so an exact-match-only lookup would strand a good key.
            if (meta == null)
                meta = _cache.FirstOrDefault(k =>
                            !k.AllowsWithdrawal &&
                            k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && k.IsActive)
                    ?? _cache.FirstOrDefault(k =>
                            !k.AllowsWithdrawal &&
                            k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

            if (meta == null) return null;
            return await ToConfigAsync(meta).ConfigureAwait(false);
        }

        public async Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper")
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = _cache.FirstOrDefault(k =>
                !k.AllowsWithdrawal &&
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase) &&
                k.IsActive);

            if (meta == null) return null;
            return await ToConfigAsync(meta).ConfigureAwait(false);
        }

        /// <summary>
        /// Deliberately has NO fallback to the trading key. If no profile is marked
        /// withdrawal-enabled the answer is null, and the caller must refuse — a
        /// convenience fallback here would silently rejoin the two powers this
        /// separation exists to keep apart, and it would do so on the one path where
        /// being wrong moves money.
        /// </summary>
        public async Task<ApiKeyConfig?> GetWithdrawalKeyAsync(string provider)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = _cache.FirstOrDefault(k =>
                k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
                k.AllowsWithdrawal);

            if (meta == null) return null;
            return await ToConfigAsync(meta).ConfigureAwait(false);
        }

        public async Task SetActiveKeyAsync(string nickname)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var target = _cache.FirstOrDefault(k => k.Nickname == nickname);
                if (target == null) return;

                // A withdrawal profile can never be the ACTIVE profile. Activation
                // only means "use this for trading sessions", which a withdrawal
                // profile must never be — and activating one would deactivate the
                // real trading profile for the same provider+environment, silently
                // breaking trading. The withdrawal path finds its credential by the
                // flag alone and never looks at IsActive.
                if (target.AllowsWithdrawal)
                    throw new InvalidOperationException(
                        $"{nickname} is a withdrawal profile. It is used automatically for withdrawals "
                      + "and cannot be made the active trading profile.");

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
                await SaveLockedAsync().ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveKeyAsync(ApiKeyConfig config)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);

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

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                _cache.RemoveAll(k => k.Nickname == config.Nickname);
                _cache.Add(new ApiKeyMetadata(config.Provider, config.Nickname, config.MarketType,
                                              config.Environment, config.IsActive, config.AllowsWithdrawal));
                await SaveLockedAsync().ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveKeyAsync(string nickname)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                int removed = _cache.RemoveAll(k => k.Nickname == nickname);
                if (removed == 0) return;

                _secureStorage.Remove($"apikey_{nickname}_key");
                _secureStorage.Remove($"apikey_{nickname}_secret");
                _secureStorage.Remove($"apikey_{nickname}_passphrase");
                await SaveLockedAsync().ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
