using System;
using System.IO;
using System.Threading;

namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Always-on file logger for the Dot Pad code path. Writes to
    /// %LocalAppData%\AccessibleTrader\dotpad.log so a user without a debugger
    /// attached can still see why the tactile display did or did not come up.
    /// Best-effort: swallows any I/O exception silently rather than letting a
    /// log-write failure crash the SDK thread.
    /// </summary>
    public static class DotPadDiagnostics
    {
        private static readonly object _lock = new();
        private static string? _path;
        private static bool _initialized;

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
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [tid {Thread.CurrentThread.ManagedThreadId,3}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    File.AppendAllText(_path, line);
                }
            }
            catch { /* best-effort */ }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            try
            {
                string dir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_DOTPAD_LOG_DIR")
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AccessibleTrader");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "dotpad.log");
                File.AppendAllText(_path, $"{Environment.NewLine}=== Dot Pad log opened {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
            }
            catch { _path = null; }
            finally { _initialized = true; }
        }
    }
}
