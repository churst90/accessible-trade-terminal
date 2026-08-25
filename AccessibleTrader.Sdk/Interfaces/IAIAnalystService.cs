namespace AccessibleTrader.Sdk.Interfaces;

/// <summary>
/// Orchestrates AI-powered technical analysis of the current chart.
/// Builds a structured prompt from live OHLCV data and indicator summaries,
/// captures an offscreen chart screenshot, and forwards the request to the
/// configured <see cref="ILLMProvider"/>.
/// </summary>
public interface IAIAnalystService
{
    /// <summary>
    /// Runs AI analysis on the current chart viewport.
    /// Returns the analysis text on success, or null if no LLM API key is configured.
    /// Throws <see cref="System.Exception"/> on network/provider failure.
    /// </summary>
    Task<string?> AnalyseAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends a free-form prompt to the configured <c>ILLMProvider</c> and returns its response.
    /// Used by callers like the "Review my setups today" feature in <c>AIAnalystModal</c>, which
    /// build their own structured prompt from journal entries + strategies.json before invoking
    /// the analyst. Returns null if no LLM provider is configured.
    /// </summary>
    Task<string?> AskAsync(string prompt, CancellationToken ct = default);
}
