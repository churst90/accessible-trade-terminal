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

        private static string EnsureDir(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
