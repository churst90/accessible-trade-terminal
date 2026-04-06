using AccessibleTrader.Sdk.Enums;
namespace AccessibleTrader.Sdk.Logging;

public interface IAppLogger
{
    void Log(ErrorSeverity severity, ErrorCategory category, string message, string source, Exception? exception = null);
    void LogDebug(string message, string source);
    void LogInfo(string message, string source);
    void LogWarning(string message, string source, Exception? exception = null);
    void LogError(string message, string source, Exception? exception = null);
    void LogCritical(string message, string source, Exception? exception = null);
}
