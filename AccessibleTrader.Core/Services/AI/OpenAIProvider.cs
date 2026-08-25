using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.AI;

/// <summary>
/// Sends requests to the OpenAI Chat Completions endpoint (gpt-4o default).
/// Supports vision when imageBase64 is provided.
/// </summary>
public sealed class OpenAIProvider : ILLMProvider
{
    private const string ApiEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string ModelId     = "gpt-4o";
    private const int    MaxTokens   = 1024;

    public string ProviderId  => "OpenAI";
    public string DisplayName => "GPT-4o (OpenAI)";

    public async Task<string> CompleteAsync(
        string systemPrompt, string userMessage, string? imageBase64,
        string apiKey, CancellationToken ct = default)
    {
        // Phase 4 Track B2 — allow-listed to api.openai.com. Response caps
        // and default User-Agent come from the factory.
        using var http = AccessibleTrader.Sdk.Services.PluginHostServices.CreateHttpClient(
            providerId:   "OpenAI",
            allowedHosts: new[] { "api.openai.com" },
            timeout:      TimeSpan.FromSeconds(120));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        object userContent;
        if (!string.IsNullOrEmpty(imageBase64))
        {
            userContent = new object[]
            {
                new { type = "text", text = userMessage },
                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{imageBase64}" } }
            };
        }
        else
        {
            userContent = userMessage;
        }

        var body = new
        {
            model      = ModelId,
            max_tokens = MaxTokens,
            messages   = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userContent  }
            }
        };

        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await http.PostAsync(ApiEndpoint, content, ct).ConfigureAwait(false);
        var raw     = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI API error {(int)resp.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(raw);
        var text      = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return text ?? string.Empty;
    }
}
