using System;
using System.IO;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.WebHost.Account;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// Path service that routes <see cref="IPlatformPathService.AppDataDirectory"/> per
    /// user when hosted accounts are enabled, while keeping <see cref="CacheDirectory"/>
    /// shared (it holds public OHLCV/HTTP data — one cache for everyone is the point).
    ///
    /// When accounts are DISABLED it is byte-for-byte equivalent to the legacy
    /// <see cref="WebHostPathService"/> (one fixed app-data dir), so the local single-user
    /// and --demo modes are completely unaffected.
    ///
    /// <see cref="AppDataDirectory"/> is computed on access (not in the ctor), so it reads
    /// the per-circuit <see cref="ICurrentUser"/> AFTER the circuit handler has set it.
    /// </summary>
    public sealed class UserScopedPathService : IPlatformPathService
    {
        private const string AppFolderName = "AccessibleTrader";

        private readonly ICurrentUser _user;
        private readonly bool _accountsEnabled;
        private readonly string _legacyAppData;   // accounts off → exactly WebHostPathService
        private readonly string _dataRoot;        // accounts on  → {dataRoot}/users/{key}
        private readonly string _cacheDir;        // shared either way

        public UserScopedPathService(ICurrentUser user, bool accountsEnabled, string? dataRoot)
        {
            _user = user;
            _accountsEnabled = accountsEnabled;

            // PlatformPaths guarantees an absolute root — see WebHostPathService for why that
            // is not something GetFolderPath gives you on Unix.
            _legacyAppData = Path.Combine(
                AccessibleTrader.Core.Services.PlatformPaths.LocalAppDataRoot(), AppFolderName);

            _dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? _legacyAppData : dataRoot!;

            // Shared cache: under the data root when accounts are on, else the legacy
            // InternetCache location (unchanged for local/demo).
            _cacheDir = EnsureDir(_accountsEnabled
                ? Path.Combine(_dataRoot, "cache")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.InternetCache), AppFolderName));
        }

        public string AppDataDirectory
        {
            get
            {
                if (!_accountsEnabled)
                    return EnsureDir(_legacyAppData);

                // Per-user: derive the folder from the immutable id (sanitised), never a
                // user-supplied name — no path traversal, no collisions.
                return EnsureDir(Path.Combine(_dataRoot, "users", Sanitize(_user.DataKey)));
            }
        }

        public string CacheDirectory => _cacheDir;

        private static string Sanitize(string s)
        {
            var clean = new string((s ?? "anon").Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            return string.IsNullOrEmpty(clean) ? "anon" : clean;
        }

        private static string EnsureDir(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
