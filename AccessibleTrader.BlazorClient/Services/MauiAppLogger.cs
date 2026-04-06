using System;
using System.Collections.Concurrent;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Logging;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Models;
using Microsoft.Extensions.Logging;

namespace AccessibleTrader.BlazorClient.Services;

/// <summary>
/// MAUI-specific implementation of IAppLogger.
/// Writes to ILogger for all severities and publishes AppErrorEvent to EventBus for severity >= Medium.
/// Deduplicates identical (message + source) pairs within a 3-second window.
/// </summary>
public sealed class MauiAppLogger : IAppLogger
{
    private readonly ILogger<MauiAppLogger> _logger;
    private readonly IEventBus _eventBus;
    private readonly ConcurrentDictionary<string, DateTime> _dedupCache = new(StringComparer.Ordinal);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(3);

    public MauiAppLogger(ILogger<MauiAppLogger> logger, IEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    public void Log(ErrorSeverity severity, ErrorCategory category, string message, string source, Exception? exception = null)
    {
        // Write to ILogger at appropriate level
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

        // Publish to EventBus for severity >= Medium with deduplication
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
