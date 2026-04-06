using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Interfaces;

namespace AccessibleTrader.Core.Services.AI;

/// <summary>
/// Sends requests to the Anthropic Claude Messages API (claude-sonnet-4-6 default).
/// Supports vision when imageBase64 is provided.
/// </summary>
public sealed class ClaudeProvider : ILLMProvider
{
    private const string ApiEndpoint = "https://api.anthropic.com/v1/messages";
    private const string ModelId     = "claude-sonnet-4-6";
    private const string ApiVersion  = "2023-06-01";
    private const int    MaxTokens   = 1024;

    public string ProviderId  => "Claude";
    public string DisplayName => "Claude (Anthropic)";

    public async Task<string> CompleteAsync(
        string systemPrompt, string userMessage, string? imageBase64,
        string apiKey, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);

        // Build content blocks
        object[] contentBlocks;
        if (!string.IsNullOrEmpty(imageBase64))
        {
            contentBlocks = new object[]
            {
                new { type = "image", source = new { type = "base64", media_type = "image/png", data = imageBase64 } },
                new { type = "text", text = userMessage }
            };
        }
        else
        {
            contentBlocks = new object[] { new { type = "text", text = userMessage } };
        }

        var body = new
        {
            model      = ModelId,
            max_tokens = MaxTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = contentBlocks } }
        };

        var json    = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp    = await http.PostAsync(ApiEndpoint, content, ct);
        var raw     = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API error {(int)resp.StatusCode}: {raw}");

        using var doc  = JsonDocument.Parse(raw);
        var textBlock  = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        return textBlock ?? string.Empty;
    }
}
