namespace AccessibleTrader.Sdk.Interfaces;

/// <summary>
/// Plugin contract for large-language-model backends.
/// Implement this interface to add a new AI provider (Claude, OpenAI, Ollama, etc.).
/// </summary>
public interface ILLMProvider
{
    /// <summary>Machine identifier used to look up the corresponding API key (e.g. "Claude", "OpenAI", "Ollama").</summary>
    string ProviderId { get; }

    /// <summary>Human-readable name shown in the UI.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Sends a prompt to the LLM and returns the response text.
    /// </summary>
    /// <param name="systemPrompt">System-level instructions (analyst persona, output format).</param>
    /// <param name="userMessage">User turn (chart data, indicator summary, question).</param>
    /// <param name="imageBase64">Optional base-64 PNG of the chart canvas. Null when vision is not available.</param>
    /// <param name="apiKey">The API key retrieved from <c>IApiKeyService</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, string? imageBase64, string apiKey, CancellationToken ct = default);
}
