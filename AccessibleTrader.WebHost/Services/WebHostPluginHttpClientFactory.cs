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
