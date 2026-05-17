using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost mirror of <c>MauiPluginHttpClientFactory</c>: wraps the
    /// plugin's per-policy outbound allow-list around the standard
    /// <see cref="HttpClientHandler"/>. Same semantics as the MAUI version,
    /// duplicated here so the MAUI head's csproj/source stays untouched.
    /// </summary>
    public sealed class WebHostPluginHttpClientFactory : IPluginHttpClientFactory
    {
        public HttpClient Create(HttpClientPolicy policy)
        {
            if (policy is null) throw new ArgumentNullException(nameof(policy));
            if (policy.AllowedHosts == null || policy.AllowedHosts.Count == 0)
                throw new ArgumentException(
                    $"HttpClientPolicy for '{policy.ProviderId}' must declare at least one allowed host.",
                    nameof(policy));

            var inner = new HttpClientHandler();
            var handler = new HostAllowListHandler(policy, inner);
            var http = new HttpClient(handler)
            {
                MaxResponseContentBufferSize = policy.MaxResponseContentBytes,
                Timeout = policy.Timeout ?? TimeSpan.FromSeconds(60),
            };
            http.DefaultRequestHeaders.Add(
                "User-Agent",
                policy.UserAgent ?? "AccessibleTrader/1.0");
            return http;
        }

        private sealed class HostAllowListHandler : DelegatingHandler
        {
            private readonly HashSet<string> _allowedHosts;
            private readonly string _providerId;

            public HostAllowListHandler(HttpClientPolicy policy, HttpMessageHandler inner)
                : base(inner)
            {
                _allowedHosts = new HashSet<string>(policy.AllowedHosts, StringComparer.OrdinalIgnoreCase);
                _providerId = policy.ProviderId;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var host = request.RequestUri?.Host;
                if (string.IsNullOrEmpty(host) || !_allowedHosts.Contains(host))
                {
                    throw new HttpRequestException(
                        $"Plugin '{_providerId}' attempted request to '{host ?? "<null>"}' which is not in its outbound allow-list. " +
                        $"Declared hosts: {string.Join(", ", _allowedHosts)}.");
                }
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
