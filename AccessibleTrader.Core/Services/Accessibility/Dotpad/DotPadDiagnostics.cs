using System.Globalization;
using System.Text;

namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Always-on file logger for the Dot Pad code path. Writes to
    /// %LocalAppData%\AccessibleTrader\dotpad.log so a user without a debugger
    /// attached can still see why the tactile display did or did not come up.
    /// Best-effort: swallows any I/O exception silently rather than letting a
    /// log-write failure crash the SDK thread.
    ///
    /// Size-capped at <see cref="MaxBytes"/>: when the live log would exceed the
    /// cap it is rotated to <c>dotpad.log.1</c> (single backup, previous backup
    /// overwritten). Total on-disk footprint is therefore bounded to ~2× the cap
    /// even for a long-running host that never restarts — the append path used to
    /// grow without limit (a real deployment reached ~19 MB).
    /// </summary>
    public static class DotPadDiagnostics
    {
        private const long MaxBytes = 1_000_000; // ~1 MB live log, plus one rotated backup

        private static readonly object _lock = new();
        private static string? _path;
        private static bool _initialized;
        private static long _size;   // approximate live-log size in bytes, guarded by _lock

        public static string LogPath
        {
            get
            {
                EnsureInitialized();
                return _path ?? "(unavailable)";
            }
        }

        public static void Log(string message)
        {
            try
            {
                EnsureInitialized();
                if (_path is null) return;
                string line = $"[{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}] [tid {Thread.CurrentThread.ManagedThreadId,3}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    int bytes = Encoding.UTF8.GetByteCount(line);
                    RollIfTooLarge(bytes);
                    File.AppendAllText(_path, line);
                    _size += bytes;
                }
            }
            catch { /* best-effort */ }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            try
            {
                // PlatformPaths, not GetFolderPath: an empty return on Unix would drop dotpad.log
                // into the process's working directory rather than app data.
                string dir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_DOTPAD_LOG_DIR")
                    ?? AccessibleTrader.Core.Services.PlatformPaths.AppDataRoot();
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "dotpad.log");
                _size = File.Exists(_path) ? new FileInfo(_path).Length : 0;

                string header = $"{Environment.NewLine}=== Dot Pad log opened {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} ==={Environment.NewLine}";
                lock (_lock)
                {
                    RollIfTooLarge(Encoding.UTF8.GetByteCount(header));
                    File.AppendAllText(_path, header);
                    _size += Encoding.UTF8.GetByteCount(header);
                }
            }
            catch { _path = null; }
            finally { _initialized = true; }
        }

        /// <summary>
        /// Rotates the live log to <c>dotpad.log.1</c> when adding
        /// <paramref name="incomingBytes"/> would push it past <see cref="MaxBytes"/>,
        /// so the file cannot grow without bound. Caller must hold <see cref="_lock"/>.
        /// Best-effort: a failed rotate leaves the current file in place.
        /// </summary>
        private static void RollIfTooLarge(int incomingBytes)
        {
            if (_path is null || _size + incomingBytes <= MaxBytes) return;
            try
            {
                string backup = _path + ".1";
                File.Delete(backup);          // no-op if absent
                File.Move(_path, backup);      // live log becomes the single backup
            }
            catch { /* best-effort: keep appending to the current file */ }
            finally { _size = 0; }
        }
    }
}
