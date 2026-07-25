using AccessibleTrader.Sdk.Enums;

namespace AccessibleTrader.Sdk.Plugins
{
    /// <summary>
    /// Structured provider error. Providers historically emitted context-free
    /// strings on <c>ErrorStream</c>; this carries severity, category, and the
    /// symbol so the feedback layer can decide how to announce without string
    /// parsing. Emitted alongside the string stream by
    /// <see cref="BaseMarketDataProvider.SurfaceError"/>.
    /// </summary>
    public readonly record struct ProviderError(
        string Provider,
        string Message,
        ErrorSeverity Severity = ErrorSeverity.Medium,
        ErrorCategory Category = ErrorCategory.Provider,
        string? Symbol = null);
}
