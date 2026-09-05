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
        private readonly bool _blockPrivateNetworks;

        /// <param name="policy">
        /// The host's feature policy. <see cref="AccessibleTrader.Core.Services.DemoPolicy.BlockPrivateNetworkTargets"/>
        /// decides whether an allow-listed host that RESOLVES to a private, loopback or
        /// link-local address is refused at connect time. Null fails CLOSED — a factory built
        /// with no policy behaves as the public server does, never as the desktop does.
        /// </param>
        public MauiPluginHttpClientFactory(AccessibleTrader.Core.Services.DemoPolicy? policy = null)
            => _blockPrivateNetworks = policy?.BlockPrivateNetworkTargets ?? true;

        public HttpClient Create(HttpClientPolicy policy)
        {
            if (policy is null) throw new ArgumentNullException(nameof(policy));
            if (policy.AllowedHosts == null || policy.AllowedHosts.Count == 0)
                throw new ArgumentException(
                    $"HttpClientPolicy for '{policy.ProviderId}' must declare at least one allowed host.",
                    nameof(policy));

            // The inner handler is the SHARED outbound guard: redirects never followed (a hop
            // inside the inner handler never passes the allow-list, which is checked once on
            // the initial URI), and on a hosted head the socket connects only to an address
            // that resolved public. Until 2026-09-05 this was a bare client handler with
            // only AllowAutoRedirect switched off, in both copies of this file — the redirect
            // half of the hole closed, the DNS half open: the allow-list
            // matched the NAME and never what it resolved to. See OutboundNetworkGuard.
            var inner = AccessibleTrader.Core.Services.Alerts.OutboundNetworkGuard.CreateHandler(_blockPrivateNetworks);
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
