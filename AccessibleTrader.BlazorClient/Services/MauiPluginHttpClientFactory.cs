using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Host implementation of <see cref="IPluginHttpClientFactory"/>. Every
    /// <see cref="HttpClient"/> returned from <see cref="Create"/> is wrapped
    /// in a <see cref="HostAllowListHandler"/> — requests to any host outside
    /// the policy's allow-list fail fast at the handler before a byte leaves
    /// the process.
    ///
    /// Response size + timeout are applied on the client itself (standard
    /// <see cref="HttpClient"/> behaviour). A default User-Agent is added so
    /// every plugin request is attributable in server logs.
    /// </summary>
    public sealed class MauiPluginHttpClientFactory : IPluginHttpClientFactory
    {
        public HttpClient Create(HttpClientPolicy policy)
        {
            if (policy is null) throw new ArgumentNullException(nameof(policy));
            if (policy.AllowedHosts == null || policy.AllowedHosts.Count == 0)
                throw new ArgumentException(
                    $"HttpClientPolicy for '{policy.ProviderId}' must declare at least one allowed host.",
                    nameof(policy));

            // AllowAutoRedirect = FALSE. HostAllowListHandler is a DelegatingHandler layered
            // ABOVE this inner handler, so a redirect followed inside the inner handler never
            // passes the allow-list at all: it is checked once, on the initial URI, and never
            // on the hop. An allow-listed host answering
            // `302 Location: http://169.254.169.254/latest/meta-data/` was followed and the
            // body handed straight back to the plugin.
            //
            // OutboundNetworkGuard already did this and documented why; the plugin factory did
            // not, and the two copies of this file were byte-identical in that respect.
            var inner = new HttpClientHandler { AllowAutoRedirect = false };
            var handler = new HostAllowListHandler(policy, inner);
            var http    = new HttpClient(handler)
            {
                MaxResponseContentBufferSize = policy.MaxResponseContentBytes,
                Timeout = policy.Timeout ?? TimeSpan.FromSeconds(60),
            };
            http.DefaultRequestHeaders.Add(
                "User-Agent",
                policy.UserAgent ?? "AccessibleTrader/1.0");
            return http;
        }

        /// <summary>
        /// <see cref="DelegatingHandler"/> that rejects any request whose URI
        /// host isn't in the policy allow-list. Matches by host name only
        /// (case-insensitive, subdomains must be listed explicitly). No
        /// scheme / port check — TLS policy is handled by the inner
        /// <see cref="HttpClientHandler"/>.
        /// </summary>
        private sealed class HostAllowListHandler : DelegatingHandler
        {
            private readonly HashSet<string> _allowedHosts;
            private readonly string _providerId;

            public HostAllowListHandler(HttpClientPolicy policy, HttpMessageHandler inner)
                : base(inner)
            {
                _allowedHosts = new HashSet<string>(policy.AllowedHosts, StringComparer.OrdinalIgnoreCase);
                _providerId   = policy.ProviderId;
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
