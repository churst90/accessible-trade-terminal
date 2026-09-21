using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// <b>A log the desktop head actually writes somewhere a human can read.</b>
    ///
    /// <para>
    /// Until 2026-09-21 <c>MauiProgram</c> registered logging providers inside <c>#if DEBUG</c>
    /// and nothing else, so a RELEASE build of the desktop head had <b>zero</b> providers: every
    /// <c>LogError</c> and <c>LogWarning</c> in the application went nowhere at all. That is not
    /// a diagnostics gap, it is the reason this head stayed unmeasured — the 2026-08-24 audit
    /// filed "MAUI Release has no logging providers" as a finding and the consequence was not
    /// followed through. <c>AppStartupService.InitializeAsync</c> is launched through
    /// <see cref="SafeFireAndForget"/>, which catches every exception and reports it by calling
    /// <c>logger.LogError</c>; with no provider behind that logger, a startup that dies halfway
    /// leaves an application that is half-built, silent about it, and impossible to investigate
    /// from the outside. Reported from a Windows VM: the market dropdown held only the providers
    /// registered before the failure point and the terminal never spoke.
    /// </para>
    ///
    /// <para>
    /// Deliberately hand-rolled and small rather than a logging package: this has to work in the
    /// one configuration nobody can attach a debugger to, so it takes no dependency, needs no
    /// configuration file, and cannot itself throw into the caller. A logger that can break the
    /// thing it is watching is worse than none.
    /// </para>
    /// </summary>
    public sealed class RollingFileLoggerProvider : ILoggerProvider
    {
        private readonly string _path;
        private readonly LogLevel _minimum;
        private readonly object _gate = new();
        private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);
        private bool _disabled;

        /// <summary>Roughly a megabyte, then the file is rotated once to <c>.1</c>. One generation
        /// is enough to survive a restart-after-a-crash, which is the case this exists for.</summary>
        private const long MaxBytes = 1024 * 1024;

        public RollingFileLoggerProvider(string path, LogLevel minimum = LogLevel.Information)
        {
            _path = path;
            _minimum = minimum;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); }
            catch { _disabled = true; }
        }

        /// <summary>
        /// The conventional location: <c>&lt;LocalAppData&gt;/AccessibleTrader/logs/terminal.log</c>.
        /// Beside the workspace and plugin directories rather than beside the binary, because an
        /// installed app may sit somewhere unwritable and a log that cannot be written is the one
        /// failure this class must not have.
        /// </summary>
        public static string DefaultLogPath() =>
            Path.Combine(PlatformPaths.AppDataRoot(), "logs", "terminal.log");

        public ILogger CreateLogger(string categoryName) =>
            _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(this, name));

        internal bool IsEnabled(LogLevel level) => !_disabled && level >= _minimum && level != LogLevel.None;

        internal void Write(string line)
        {
            if (_disabled) return;
            lock (_gate)
            {
                try
                {
                    Roll();
                    File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // One failure disables the provider for the session rather than throwing on
                    // every subsequent call. A disk that is full or a directory that is read-only
                    // must not turn every log statement in the application into an exception.
                    _disabled = true;
                }
            }
        }

        private void Roll()
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < MaxBytes) return;
            var previous = _path + ".1";
            if (File.Exists(previous)) File.Delete(previous);
            File.Move(_path, previous);
        }

        public void Dispose() => _loggers.Clear();

        private sealed class RollingFileLogger : ILogger
        {
            private readonly RollingFileLoggerProvider _owner;
            private readonly string _category;

            public RollingFileLogger(RollingFileLoggerProvider owner, string category)
            {
                _owner = owner;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => _owner.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                    Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                string message;
                try { message = formatter(state, exception); }
                catch { return; }

                var sb = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(Short(logLevel)).Append("] ")
                    .Append(_category).Append(": ").Append(message);

                // The exception, in full, including the inner chain. A startup failure's TYPE and
                // SITE are the whole content of the report — "Background task 'AppStartup' failed"
                // on its own names no cause.
                if (exception != null) sb.Append(Environment.NewLine).Append(exception);

                _owner.Write(sb.ToString());
            }

            private static string Short(LogLevel level) => level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???",
            };
        }
    }
}
