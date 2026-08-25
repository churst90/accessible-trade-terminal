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

        public WebHostSecureStorageService(IDataProtectionProvider provider, IPlatformPathService paths)
        {
            _protector = provider.CreateProtector(Purpose);
            _secretsDir = Path.Combine(paths.AppDataDirectory, "secrets");
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
            catch
            {
                // Corrupt blob or key-ring change. Treat as "no value" so
                // callers can decide whether to re-prompt for the secret.
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
