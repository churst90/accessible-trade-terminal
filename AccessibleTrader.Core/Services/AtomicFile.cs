using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Crash-safe text writer. Writes the payload to a sibling temp file, fsyncs
    /// it, then atomically renames it over the destination via
    /// <see cref="File.Move(string, string, bool)"/>. A power loss or process
    /// kill mid-write leaves either the previous valid file (rename never
    /// happened) or the new valid file (rename completed) — never a half-written
    /// JSON document that bricks a workspace / config / shortcut profile.
    ///
    /// <para>
    /// Audit (2026-04-27 e19) flagged 6+ <c>File.WriteAllText</c> sites that
    /// could corrupt user state on crash: <c>WorkspaceLibraryService</c>, <c>StrategyLibraryFacade</c>,
    /// <c>SoundPatchLibrary</c>, <c>SettingsManager</c>, <c>ShortcutManager</c>,
    /// <c>JsonStrategyLibrary</c>, <c>IndicatorPreferencesService</c>,
    /// <c>ApiKeyService</c>. Each of those sites was migrated to call into here.
    /// (<c>SpeechTemplateService</c> was on that list and was deleted 2026-08-24 —
    /// it was never registered in any container and had no callers.)
    /// </para>
    /// </summary>
    public static class AtomicFile
    {
        private static readonly object _renameLock = new();

        /// <summary>
        /// Synchronously writes <paramref name="contents"/> to <paramref name="path"/>
        /// via temp-file + fsync + rename. Creates the parent directory if needed.
        /// </summary>
        public static void WriteAllText(string path, string contents)
        {
            EnsureParentDirectoryExists(path);
            string tempPath = MakeTempPath(path);
            try
            {
                using (var fs = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, useAsync: false))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write(contents);
                    sw.Flush();
                    // Best-effort fsync. On platforms / filesystems where Flush(true)
                    // is unsupported, the underlying call is a no-op — the rename
                    // still gives atomic visibility, just without a hard storage
                    // commit guarantee.
                    try { fs.Flush(true); } catch { /* fall through */ }
                }
                AtomicReplace(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        /// <summary>
        /// Async version of <see cref="WriteAllText"/>. Useful for hot paths that
        /// don't want to block on disk I/O.
        /// </summary>
        public static async Task WriteAllTextAsync(string path, string contents, CancellationToken ct = default)
        {
            EnsureParentDirectoryExists(path);
            string tempPath = MakeTempPath(path);
            try
            {
                await using (var fs = new FileStream(
                    tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, useAsync: true))
                await using (var sw = new StreamWriter(fs))
                {
                    await sw.WriteAsync(contents.AsMemory(), ct).ConfigureAwait(false);
                    await sw.FlushAsync(ct).ConfigureAwait(false);
                    try { fs.Flush(true); } catch { /* fall through */ }
                }
                AtomicReplace(tempPath, path);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static string MakeTempPath(string finalPath) =>
            finalPath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static void EnsureParentDirectoryExists(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static void AtomicReplace(string tempPath, string finalPath)
        {
            // File.Move(src, dst, overwrite: true) is atomic on the same volume on
            // every platform where AccessibleTrader runs (NTFS / APFS / ext4 /
            // Android sqfs over the app's internal storage). Wrap in a lock so a
            // process that hits the same path from two threads doesn't observe
            // a partial state mid-rename. Cheap — contention is negligible.
            lock (_renameLock)
            {
                File.Move(tempPath, finalPath, overwrite: true);
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup */ }
        }
    }
}
