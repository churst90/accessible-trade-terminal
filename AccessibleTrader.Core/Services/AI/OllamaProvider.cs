using System.Text;
using System.Text.Json;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.AI;

/// <summary>
/// Sends requests to a locally running Ollama instance.
/// The API key field is used to specify the base URL (e.g. "http://localhost:11434").
/// Defaults to "http://localhost:11434" when the key is empty.
/// Model can be any model installed locally; defaults to "llama3".
/// </summary>
public sealed class OllamaProvider : ILLMProvider
{
    private const string DefaultBase = "http://localhost:11434";
    private const string Model       = "llama3";

    public string ProviderId  => "Ollama";
    public string DisplayName => "Ollama (local)";

    public async Task<string> CompleteAsync(
        string systemPrompt, string userMessage, string? imageBase64,
        string apiKey, CancellationToken ct = default)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiKey) ? DefaultBase : apiKey.TrimEnd('/');

        // Endpoint hardening: cleartext http is only allowed for loopback (localhost/
        // 127.0.0.1/::1). Anything else must use https, otherwise a LAN-local attacker
        // could MITM the analyst response and inject misleading trade advice.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Ollama endpoint '{baseUrl}' is not a valid absolute URL.");

        bool isLoopback = uri.IsLoopback
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host == "127.0.0.1"
            || uri.Host == "::1";

        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && !isLoopback)
            throw new InvalidOperationException(
                $"Ollama endpoint '{baseUrl}' uses cleartext http on a non-loopback host. " +
                "Use https://... for remote Ollama instances, or bind Ollama to localhost.");

        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Ollama endpoint scheme '{uri.Scheme}' is not supported.");

        string endpoint = $"{baseUrl}/api/chat";

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120),
            // LLM responses are text — 32 MB is plenty, protects against an endpoint
            // streaming unbounded data at us.
            MaxResponseContentBufferSize = 32 * 1024 * 1024,
        };

        object userContent;
        if (!string.IsNullOrEmpty(imageBase64))
        {
            // Ollama vision via images array
            userContent = new { role = "user", content = userMessage, images = new[] { imageBase64 } };
        }
        else
        {
            userContent = new { role = "user", content = userMessage };
        }

        var body = new
        {
            model    = Model,
            stream   = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                userContent
            }
        };

        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var raw     = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama error {(int)resp.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var text      = doc.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return text ?? string.Empty;
    }
}
