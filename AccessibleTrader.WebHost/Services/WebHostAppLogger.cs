using System;
using System.Collections.Concurrent;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost implementation of <see cref="IAppLogger"/>. Mirrors the
    /// MAUI head's adapter byte-for-byte (writes through ILogger, publishes
    /// AppErrorEvent for severity ≥ Medium, dedupes inside a 3-second
    /// window). Kept as a separate type to honour the
    /// "don't touch the MAUI head" rule — refactoring both into a shared
    /// Core helper is on the TODO list for after the WebHost is proven.
    /// </summary>
    public sealed class WebHostAppLogger : IAppLogger
    {
        private readonly ILogger<WebHostAppLogger> _logger;
        private readonly IEventBus _eventBus;
        private readonly ConcurrentDictionary<string, DateTime> _dedupCache = new(StringComparer.Ordinal);
        private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(3);

        public WebHostAppLogger(ILogger<WebHostAppLogger> logger, IEventBus eventBus)
        {
            _logger = logger;
            _eventBus = eventBus;
        }

        public void Log(ErrorSeverity severity, ErrorCategory category, string message, string source, Exception? exception = null)
        {
            switch (severity)
            {
                case ErrorSeverity.Low:
                    _logger.LogDebug("[{Category}:{Source}] {Message}", category, source, message);
                    break;
                case ErrorSeverity.Medium:
                    _logger.LogInformation("[{Category}:{Source}] {Message}", category, source, message);
                    break;
                case ErrorSeverity.High:
                    _logger.LogWarning(exception, "[{Category}:{Source}] {Message}", category, source, message);
                    break;
                case ErrorSeverity.Critical:
                    _logger.LogCritical(exception, "[{Category}:{Source}] {Message}", category, source, message);
                    break;
            }

            if (severity >= ErrorSeverity.Medium)
            {
                string dedupKey = $"{source}|{message}";
                var now = DateTime.UtcNow;
                bool isDuplicate = false;

                if (_dedupCache.TryGetValue(dedupKey, out var lastSeen) && (now - lastSeen) < DedupWindow)
                {
                    isDuplicate = true;
                }
                else
                {
                    _dedupCache[dedupKey] = now;
                }

                _eventBus.Publish(new AppErrorEvent(severity, category, message, source, exception, isDuplicate));
            }
        }

        public void LogDebug(string message, string source) =>
            Log(ErrorSeverity.Low, ErrorCategory.Informational, message, source);

        public void LogInfo(string message, string source) =>
            Log(ErrorSeverity.Low, ErrorCategory.Informational, message, source);

        public void LogWarning(string message, string source, Exception? exception = null) =>
            Log(ErrorSeverity.Medium, ErrorCategory.Informational, message, source, exception);

        public void LogError(string message, string source, Exception? exception = null) =>
            Log(ErrorSeverity.High, ErrorCategory.Systemic, message, source, exception);

        public void LogCritical(string message, string source, Exception? exception = null) =>
            Log(ErrorSeverity.Critical, ErrorCategory.Systemic, message, source, exception);
    }
}
