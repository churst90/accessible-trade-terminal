using System;
using System.IO;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// An <see cref="IPlatformPathService"/> rooted in a fresh temp directory, for tests that
    /// construct a <see cref="WorkspaceLibraryService"/> directly. Most of them immediately set
    /// <c>LibraryDirectoryOverride</c> anyway; this just keeps the constructor from touching the
    /// developer's real app-data directory on the way past.
    /// </summary>
    public sealed class TempWorkspacePaths : IPlatformPathService
    {
        public TempWorkspacePaths()
        {
            AppDataDirectory = Directory.CreateTempSubdirectory("att-paths-").FullName;
            CacheDirectory = Path.Combine(AppDataDirectory, "cache");
            Directory.CreateDirectory(CacheDirectory);
        }

        public string AppDataDirectory { get; }
        public string CacheDirectory { get; }
    }
}
