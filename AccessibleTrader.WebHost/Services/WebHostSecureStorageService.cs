using System.Text;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost implementation of <see cref="ISecureStorageService"/> +
    /// <see cref="IPluginSecureStorage"/>. Backed by
    /// <c>Microsoft.AspNetCore.DataProtection</c>, which has a usable
    /// cross-platform default key ring (DPAPI on Windows, XML key files
    /// at <c>~/.aspnet/DataProtection-Keys</c> on Linux and macOS).
    ///
    /// Encrypted blobs are written one file per key under
    /// <c>{AppDataDirectory}/secrets/</c>. Single file per key keeps
    /// concurrent writes from different SDK callers from clobbering each
    /// other; the directory creation in <see cref="WebHostPathService"/>
    /// guarantees the parent exists.
    ///
    /// NOT for high-stakes credentials. This is "encrypt at rest with a
    /// machine-bound key", not "hardware-isolated keystore". Good enough
    /// for v1; upgrade path is libsecret on Linux desktop and CredMan on
    /// Windows if a user explicitly opts in.
    /// </summary>
    public sealed class WebHostSecureStorageService : ISecureStorageService, IPluginSecureStorage
    {
        private const string Purpose = "AccessibleTrader.WebHost.SecureStorage.v1";

        private readonly IDataProtector _protector;
        private readonly string _secretsDir;
        private readonly ILogger<WebHostSecureStorageService>? _logger;

        public WebHostSecureStorageService(IDataProtectionProvider provider, IPlatformPathService paths,
                                           ILogger<WebHostSecureStorageService>? logger = null)
        {
            _protector = provider.CreateProtector(Purpose);
            _secretsDir = Path.Combine(paths.AppDataDirectory, "secrets");
            _logger = logger;
            Directory.CreateDirectory(_secretsDir);
        }

        public Task SetAsync(string key, string value)
        {
            var cipher = _protector.Protect(Encoding.UTF8.GetBytes(value));
            File.WriteAllBytes(PathFor(key), cipher);
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string key)
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return Task.FromResult<string?>(null);
            try
            {
                var cipher = File.ReadAllBytes(path);
                var plain = _protector.Unprotect(cipher);
                return Task.FromResult<string?>(Encoding.UTF8.GetString(plain));
            }
            catch (Exception ex)
            {
                // Corrupt blob or key-ring change. Treat as "no value" so
                // callers can decide whether to re-prompt for the secret —
                // that part is right for the caller.
                //
                // But it was ALSO silent, and that part was not. A lost or
                // replaced DataProtection key ring makes every secret on the
                // box undecryptable, and the app presented that as "no value
                // configured": the operator sees an instance that has quietly
                // forgotten its market-data key, its Schwab refresh token and
                // its VAPID keypair, with nothing anywhere saying why. A file
                // that EXISTS and will not decrypt is an incident, so it is
                // logged at Error. (A missing file returns above and is not an
                // error — it is the ordinary "never set" case.)
                _logger?.LogError(ex,
                    "Secure-storage entry {Path} exists but could not be decrypted. This normally means the "
                    + "DataProtection key ring was lost or replaced, in which case EVERY stored secret on this "
                    + "instance is unreadable and will present as 'not configured'. Restore the key ring, or "
                    + "re-enter the affected secrets.", path);
                return Task.FromResult<string?>(null);
            }
        }

        public void Remove(string key)
        {
            var path = PathFor(key);
            if (File.Exists(path)) File.Delete(path);
        }

        private string PathFor(string key)
        {
            // Hash to a stable hex filename so user-supplied key names with
            // path separators or unusual chars don't escape _secretsDir.
            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return Path.Combine(_secretsDir, Convert.ToHexString(hash) + ".bin");
        }
    }
}
