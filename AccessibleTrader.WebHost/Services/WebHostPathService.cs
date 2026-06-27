using System;
using System.IO;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost implementation of <see cref="IPlatformPathService"/>.
    /// Maps app-data and cache directories to XDG-compliant locations on
    /// Linux (<c>~/.local/share/AccessibleTrader</c> and
    /// <c>~/.cache/AccessibleTrader</c>) and to the OS equivalent
    /// (<c>%LOCALAPPDATA%\AccessibleTrader</c> on Windows,
    /// <c>~/Library/Application Support/AccessibleTrader</c> on macOS) via
    /// <see cref="Environment.SpecialFolder"/>.
    ///
    /// Both directories are created on first access so callers (SQLite
    /// cache, secure-event log, workspace library) can assume they exist.
    /// </summary>
    public sealed class WebHostPathService : IPlatformPathService
    {
        private const string AppFolderName = "AccessibleTrader";

        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }

        public WebHostPathService()
        {
            AppDataDirectory = EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName));

            // SpecialFolder.InternetCache maps to ~/.cache on Linux via
            // .NET's XDG handling; on Windows it lands at INetCache which
            // is fine for transient HTTP / OHLCV cache files.
            CacheDirectory = EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.InternetCache),
                AppFolderName));
        }

        /// <summary>
        /// App-data rooted at an explicit directory instead of the OS default. Used by the
        /// hosted (<c>--accounts</c>) build to pin the shared secret store under the instance's
        /// own data root, so a hosted instance is self-contained and can never collide with a
        /// co-located demo that would otherwise resolve to the same default
        /// <c>~/.local/share/AccessibleTrader</c>. Cache stays at the shared OS cache location.
        /// </summary>
        public WebHostPathService(string appDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(appDataDirectory))
                throw new ArgumentException("App-data directory must be provided.", nameof(appDataDirectory));

            AppDataDirectory = EnsureDir(appDataDirectory);
            CacheDirectory = EnsureDir(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.InternetCache),
                AppFolderName));
        }

        private static string EnsureDir(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
