using System.Text.Json;
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
        private readonly DemoPolicy? _demo;
        private List<ApiKeyMetadata> _cache = new();
        private readonly SemaphoreSlim _lock = new(1, 1);
        private bool _isLoaded;

        public ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage,
            DemoPolicy? demo = null)
            // PlatformPaths, not GetFolderPath: an empty return on Unix would make this RELATIVE,
            // so the legacy-plaintext migration would look in the process's working directory and
            // silently find nothing to migrate.
            : this(logger, secureStorage,
                   Path.Combine(PlatformPaths.AppDataRoot(), "apikeys_meta.json"), demo)
        {
        }

        /// <summary>Test seam: inject the legacy plaintext path so migration is verifiable.</summary>
        internal ApiKeyService(ILogger<ApiKeyService> logger, ISecureStorageService secureStorage,
            string legacyFilePath, DemoPolicy? demo = null)
        {
            _logger = logger;
            _secureStorage = secureStorage;
            _legacyFilePath = legacyFilePath;
            _demo = demo;
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

        /// <summary>
        /// Every profile stored for <paramref name="provider"/>, matched by provider IDENTITY
        /// rather than by string equality — see <see cref="ProviderNames"/>. A profile saved
        /// as "TwelveData" belongs to the provider that calls itself "Twelve Data"; refusing
        /// to see that is what left a correctly-entered key looking unconfigured.
        /// </summary>
        private List<ApiKeyMetadata> MetaForProvider(string provider)
        {
            // Exact first. Only when nothing matches exactly does the spelling-tolerant
            // comparison run, so two providers whose names merely resemble each other can
            // never take one another's credentials.
            var exact = _cache.Where(k => k.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 0) return exact;
            return _cache.Where(k => ProviderNames.Match(k.Provider, provider)).ToList();
        }

        public async Task<List<ApiKeyConfig>> GetKeysForProviderAsync(string provider)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var metas = MetaForProvider(provider);
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
            var candidates = MetaForProvider(provider);
            var meta = candidates.FirstOrDefault(k =>
                !k.AllowsWithdrawal &&
                k.MarketType.Equals(marketType, StringComparison.OrdinalIgnoreCase));

            // Fallback: if no profile matches this market sub-type, accept any profile
            // for the provider (preferring an active one). MarketType is informational
            // — the data path looks keys up by sub-type ("Spot"/"Futures"), yet the
            // API Keys modal also offers market names ("Crypto"/"Stocks") that never
            // equal a sub-type, so an exact-match-only lookup would strand a good key.
            if (meta == null)
                meta = candidates.FirstOrDefault(k => !k.AllowsWithdrawal && k.IsActive)
                    ?? candidates.FirstOrDefault(k => !k.AllowsWithdrawal);

            if (meta == null) return null;
            return await ToConfigAsync(meta).ConfigureAwait(false);
        }

        public async Task<ApiKeyConfig?> GetActiveKeyForProviderAsync(string provider, string environment = "Paper")
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = MetaForProvider(provider).FirstOrDefault(k =>
                !k.AllowsWithdrawal &&
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
        ///
        /// <para>
        /// The spelling-tolerant provider match added for the API-keys dropdown does not
        /// loosen that: <see cref="MetaForProvider"/> stops at the exact-name profiles when
        /// any exist, so a withdrawal profile stored under a different spelling than the
        /// caller asked for yields null and the withdrawal is refused. Refusing is the only
        /// direction this method is allowed to be wrong in.
        /// </para>
        /// </summary>
        public async Task<ApiKeyConfig?> GetWithdrawalKeyAsync(string provider)
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            var meta = MetaForProvider(provider).FirstOrDefault(k => k.AllowsWithdrawal);

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
                    // ProviderNames.Match, not string equality: a store can hold the same
                    // provider under two spellings (an older profile saved as "TwelveData"
                    // beside a new "Twelve Data" one). Both are that provider, so exactly
                    // one of them may be active — otherwise the lookup picks whichever it
                    // reaches first and the user cannot tell which key is in use.
                    if (ProviderNames.Match(m.Provider, target.Provider) &&
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

        public Task SaveKeyAsync(ApiKeyConfig config)
        {
            ThrowIfUserKeyMutationDisabled();
            return SaveKeyCoreAsync(config);
        }

        /// <summary>
        /// The host's own seeding path — Program.cs storing the server-side shared
        /// market-data keys (Twelve Data, FRED) at startup on the demo/hosted heads.
        /// Bypasses the <see cref="DemoPolicy.AllowApiKeysModal"/> wall on purpose:
        /// that wall exists so a TENANT cannot store broker credentials server-side,
        /// not so the operator cannot configure the shared read-only data keys.
        /// </summary>
        public Task SaveServerManagedKeyAsync(ApiKeyConfig config) => SaveKeyCoreAsync(config);

        /// <summary>
        /// Service-layer enforcement of <see cref="DemoPolicy.AllowApiKeysModal"/>. The
        /// Razor <c>@if</c> that hides the API-keys modal is presentation; a Blazor
        /// refactor (or any new caller) that reaches this service from a hosted
        /// circuit must hit this wall instead of quietly persisting credentials on
        /// a server that promises not to hold them.
        /// </summary>
        private void ThrowIfUserKeyMutationDisabled()
        {
            if (_demo != null && !_demo.AllowApiKeysModal)
                throw new InvalidOperationException(
                    "API-key management is disabled on this host: broker credentials are " +
                    "never held server-side outside the desktop (Full mode) build.");
        }

        private async Task SaveKeyCoreAsync(ApiKeyConfig config)
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
            ThrowIfUserKeyMutationDisabled();
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
