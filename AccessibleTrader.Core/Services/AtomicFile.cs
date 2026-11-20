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

        /// <summary>The folder, beside the file, its daily copies are kept in.</summary>
        public const string BackupFolderName = "Backups";

        /// <summary>
        /// <see cref="WriteAllText"/>, first copying the file as it stands into
        /// <c>Backups/&lt;name&gt;.&lt;yyyy-MM-dd&gt;&lt;ext&gt;</c> beside it on the first save of each UTC day,
        /// and keeping the newest <paramref name="keepDays"/> of those copies.
        ///
        /// <para>Code review, 2026-11-20: the rename makes a crash safe, but a save that is WRONG
        /// (a bug that empties the positions, a bad restore) replaces the only copy. The file as it
        /// stood before today's first save is yesterday's last state, which is the copy worth
        /// having; one per day rather than one per save, because the paper account is written on
        /// every fill and seven per-save copies would all be from the last minute.</para>
        ///
        /// <para>A failed copy never stops the save: a full disk that cannot hold a backup must not
        /// also lose the trade being recorded.</para>
        /// </summary>
        public static void WriteAllTextKeepingDailyBackup(string path, string contents, int keepDays = 7, DateTime? nowUtc = null)
        {
            try { KeepDailyBackup(path, keepDays, nowUtc ?? DateTime.UtcNow); }
            catch { /* best-effort: the save below matters more than the copy */ }
            WriteAllText(path, contents);
        }

        internal static void KeepDailyBackup(string path, int keepDays, DateTime nowUtc)
        {
            if (!File.Exists(path)) return;
            string dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, BackupFolderName);
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            string today = Path.Combine(dir, $"{stem}.{nowUtc:yyyy-MM-dd}{ext}");
            if (File.Exists(today)) return;
            Directory.CreateDirectory(dir);
            File.Copy(path, today, overwrite: false);

            // Only this file's own dated copies: "Main.2026-11-20.json", never "Main.Other.json".
            var dated = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"\.\d{4}-\d{2}-\d{2}"
                + System.Text.RegularExpressions.Regex.Escape(ext) + "$");
            var old = Directory.GetFiles(dir)
                .Where(f => dated.IsMatch(Path.GetFileName(f)))
                .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                .Skip(Math.Max(1, keepDays));
            foreach (var f in old) TryDelete(f);
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
