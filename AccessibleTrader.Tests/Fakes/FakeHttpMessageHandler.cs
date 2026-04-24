using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibleTrader.Tests.Fakes
{
    /// <summary>
    /// Route-table HTTP handler for provider parse tests. Each rule pairs a
    /// <see cref="HttpMethod"/> + <see cref="Regex"/> URL matcher with either a
    /// canned response (string body + status) or a functor. The first matching
    /// rule wins. Unmatched requests either throw (strict mode) or return a
    /// 404 JSON body (lenient mode, default) — strict is preferable in tests so
    /// unexpected calls surface immediately instead of silently erroring.
    ///
    /// Shared fixture across every provider parse suite; constructed fresh per
    /// test so rules don't leak across tests.
    /// </summary>
    public sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, Regex UrlPattern, Func<HttpRequestMessage, HttpResponseMessage> Responder)> _rules = new();
        public List<HttpRequestMessage> Captured { get; } = new();
        public bool StrictMode { get; set; } = true;

        /// <summary>Register a rule that returns a fixed JSON body + status.</summary>
        public FakeHttpMessageHandler Add(HttpMethod method, string urlRegex, string body, HttpStatusCode status = HttpStatusCode.OK, string mediaType = "application/json")
        {
            var pattern = new Regex(urlRegex, RegexOptions.Compiled);
            _rules.Add((method, pattern, _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType)
            }));
            return this;
        }

        /// <summary>Register a rule that builds the response from the request.</summary>
        public FakeHttpMessageHandler Add(HttpMethod method, string urlRegex, Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            var pattern = new Regex(urlRegex, RegexOptions.Compiled);
            _rules.Add((method, pattern, responder));
            return this;
        }

        /// <summary>Convenience alias for GET + JSON body.</summary>
        public FakeHttpMessageHandler Get(string urlRegex, string body, HttpStatusCode status = HttpStatusCode.OK)
            => Add(HttpMethod.Get, urlRegex, body, status);

        /// <summary>Convenience alias for POST + JSON body.</summary>
        public FakeHttpMessageHandler Post(string urlRegex, string body, HttpStatusCode status = HttpStatusCode.OK)
            => Add(HttpMethod.Post, urlRegex, body, status);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured.Add(request);
            foreach (var (method, pattern, responder) in _rules)
            {
                if (request.Method != method) continue;
                if (!pattern.IsMatch(request.RequestUri?.ToString() ?? string.Empty)) continue;
                return Task.FromResult(responder(request));
            }

            if (StrictMode)
                throw new InvalidOperationException($"FakeHttpMessageHandler: no rule matched {request.Method} {request.RequestUri}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":\"no rule matched\"}", Encoding.UTF8, "application/json"),
            });
        }
    }
}
