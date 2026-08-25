using System.Net;
using System.Text.Json;
using AccessibleTrader.Core.Services.AI;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The three LLM transports. Claude and OpenAI go through
    /// <see cref="PluginHostServices.CreateHttpClient"/>, so the tests install a capturing
    /// <see cref="IPluginHttpClientFactory"/> — which also lets them pin the outbound
    /// host allow-list each provider declares (the SSRF fence for requests that carry a
    /// user's API key). Ollama constructs its own client but validates the user-supplied
    /// endpoint before any bytes leave, which is the part under test.
    ///
    /// In the "ProviderCredentialBridge" collection because PluginHostServices.HttpClientFactory
    /// is process-global mutable state.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public sealed class LLMProviderTransportTests : IDisposable
    {
        private sealed class CapturingFactory : IPluginHttpClientFactory
        {
            private readonly string _providerId;
            private readonly FakeHttpMessageHandler _handler;
            private readonly IPluginHttpClientFactory? _fallback;
            public HttpClientPolicy? LastPolicy;

            public CapturingFactory(string providerId, FakeHttpMessageHandler handler, IPluginHttpClientFactory? fallback)
            {
                _providerId = providerId;
                _handler = handler;
                _fallback = fallback;
            }

            public HttpClient Create(HttpClientPolicy policy)
            {
                // The bridge is process-global, so a provider test in ANOTHER collection can
                // race through here mid-test (observed: a Schwab policy landing in LastPolicy).
                // Intercept only this test's own provider; give everyone else what they would
                // have had if this test were not running.
                if (!policy.ProviderId.Equals(_providerId, StringComparison.OrdinalIgnoreCase))
                {
                    if (_fallback != null) return _fallback.Create(policy);
                    return new HttpClient
                    {
                        MaxResponseContentBufferSize = policy.MaxResponseContentBytes,
                        Timeout = policy.Timeout ?? TimeSpan.FromSeconds(60),
                    };
                }
                LastPolicy = policy;
                return new HttpClient(_handler, disposeHandler: false);
            }
        }

        private readonly IPluginHttpClientFactory? _priorFactory;
        public LLMProviderTransportTests() => _priorFactory = PluginHostServices.HttpClientFactory;
        public void Dispose() => PluginHostServices.HttpClientFactory = _priorFactory;

        private CapturingFactory Install(string providerId, FakeHttpMessageHandler handler)
        {
            var factory = new CapturingFactory(providerId, handler, _priorFactory);
            PluginHostServices.HttpClientFactory = factory;
            return factory;
        }

        // ── Claude ──────────────────────────────────────────────────────────────

        private const string ClaudeOk = """{"content":[{"type":"text","text":"the analysis"}]}""";

        [Fact]
        public async Task Claude_PostsToMessagesApi_WithKeyInHeader_AndParsesText()
        {
            HttpRequestMessage? captured = null;
            string? body = null;
            var handler = new FakeHttpMessageHandler().Add(HttpMethod.Post, @"api\.anthropic\.com/v1/messages", req =>
            {
                captured = req;
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(ClaudeOk) };
            });
            var factory = Install("Claude", handler);

            var result = await new ClaudeProvider().CompleteAsync("sys", "user msg", null, "sk-ant-123");

            Assert.Equal("the analysis", result);
            // Key travels in the x-api-key header, never in the URL.
            Assert.Equal("sk-ant-123", captured!.Headers.GetValues("x-api-key").Single());
            Assert.DoesNotContain("sk-ant-123", captured.RequestUri!.ToString());
            Assert.True(captured.Headers.Contains("anthropic-version"));

            using var doc = JsonDocument.Parse(body!);
            Assert.Equal("sys", doc.RootElement.GetProperty("system").GetString());
            var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
            var only = Assert.Single(content.EnumerateArray());
            Assert.Equal("text", only.GetProperty("type").GetString());
            Assert.Equal("user msg", only.GetProperty("text").GetString());

            // The SSRF fence: this client may talk to api.anthropic.com and nowhere else.
            Assert.Equal(new[] { "api.anthropic.com" }, factory.LastPolicy!.AllowedHosts);
        }

        [Fact]
        public async Task Claude_WithImage_SendsAnImageBlock_BeforeTheText()
        {
            string? body = null;
            var handler = new FakeHttpMessageHandler().Add(HttpMethod.Post, @"api\.anthropic\.com", req =>
            {
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ClaudeOk) };
            });
            Install("Claude", handler);

            await new ClaudeProvider().CompleteAsync("sys", "user", "BASE64PNG", "k");

            using var doc = JsonDocument.Parse(body!);
            var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
            Assert.Equal(2, content.GetArrayLength());
            Assert.Equal("image", content[0].GetProperty("type").GetString());
            var source = content[0].GetProperty("source");
            Assert.Equal("image/png", source.GetProperty("media_type").GetString());
            Assert.Equal("BASE64PNG", source.GetProperty("data").GetString());
            Assert.Equal("text", content[1].GetProperty("type").GetString());
        }

        [Fact]
        public async Task Claude_ApiError_Throws_WithStatusAndBody()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.anthropic\.com", """{"error":"overloaded"}""", HttpStatusCode.TooManyRequests);
            Install("Claude", handler);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new ClaudeProvider().CompleteAsync("s", "u", null, "k"));
            Assert.Contains("429", ex.Message);
            Assert.Contains("overloaded", ex.Message);
        }

        // ── OpenAI ──────────────────────────────────────────────────────────────

        private const string OpenAIOk =
            """{"choices":[{"message":{"content":"gpt says"}}]}""";

        [Fact]
        public async Task OpenAI_PostsToChatCompletions_WithBearerAuth_AndParsesContent()
        {
            HttpRequestMessage? captured = null;
            string? body = null;
            var handler = new FakeHttpMessageHandler().Add(HttpMethod.Post, @"api\.openai\.com/v1/chat/completions", req =>
            {
                captured = req;
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OpenAIOk) };
            });
            var factory = Install("OpenAI", handler);

            var result = await new OpenAIProvider().CompleteAsync("sys", "user msg", null, "sk-oai");

            Assert.Equal("gpt says", result);
            Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
            Assert.Equal("sk-oai", captured.Headers.Authorization.Parameter);

            using var doc = JsonDocument.Parse(body!);
            var messages = doc.RootElement.GetProperty("messages");
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("sys", messages[0].GetProperty("content").GetString());
            Assert.Equal("user msg", messages[1].GetProperty("content").GetString());

            Assert.Equal(new[] { "api.openai.com" }, factory.LastPolicy!.AllowedHosts);
        }

        [Fact]
        public async Task OpenAI_WithImage_SendsADataUriImageBlock()
        {
            string? body = null;
            var handler = new FakeHttpMessageHandler().Add(HttpMethod.Post, @"api\.openai\.com", req =>
            {
                body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OpenAIOk) };
            });
            Install("OpenAI", handler);

            await new OpenAIProvider().CompleteAsync("sys", "user", "BASE64PNG", "k");

            using var doc = JsonDocument.Parse(body!);
            var userContent = doc.RootElement.GetProperty("messages")[1].GetProperty("content");
            Assert.Equal(JsonValueKind.Array, userContent.ValueKind);
            Assert.Equal("data:image/png;base64,BASE64PNG",
                userContent[1].GetProperty("image_url").GetProperty("url").GetString());
        }

        [Fact]
        public async Task OpenAI_ApiError_Throws_WithStatusAndBody()
        {
            var handler = new FakeHttpMessageHandler()
                .Post(@"api\.openai\.com", """{"error":"quota"}""", HttpStatusCode.Unauthorized);
            Install("OpenAI", handler);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new OpenAIProvider().CompleteAsync("s", "u", null, "k"));
            Assert.Contains("401", ex.Message);
            Assert.Contains("quota", ex.Message);
        }

        // ── Ollama endpoint hardening (validation happens before any network I/O) ──

        [Theory]
        [InlineData("not a url")]
        [InlineData("gopher:whatever")]
        public async Task Ollama_InvalidEndpoint_IsRefused(string endpoint)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new OllamaProvider().CompleteAsync("s", "u", null, endpoint));
            Assert.Contains("not a valid absolute URL", ex.Message);
        }

        [Fact]
        public async Task Ollama_CleartextHttpOnANonLoopbackHost_IsRefused()
        {
            // A LAN Ollama over plain http is a MITM vector for trade advice.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new OllamaProvider().CompleteAsync("s", "u", null, "http://192.168.1.50:11434"));
            Assert.Contains("cleartext", ex.Message);
        }

        [Fact]
        public async Task Ollama_NonHttpScheme_IsRefused()
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => new OllamaProvider().CompleteAsync("s", "u", null, "ftp://localhost:11434"));
            Assert.Contains("not supported", ex.Message);
        }

        [Fact]
        public async Task Ollama_LoopbackHttp_PassesValidation()
        {
            // Port 59_999 has no listener, so passing validation surfaces as a transport
            // error (connection refused) rather than the cleartext refusal. If something
            // does listen there, any other failure shape still proves the same thing.
            var ex = await Record.ExceptionAsync(
                () => new OllamaProvider().CompleteAsync("s", "u", null, "http://127.0.0.1:59999"));
            Assert.NotNull(ex);
            Assert.DoesNotContain("cleartext", ex!.Message);
        }
    }
}
