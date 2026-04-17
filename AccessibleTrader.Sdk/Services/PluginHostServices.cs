using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// Host-provided secure-storage interface that plugins can use to persist
    /// small secrets (OAuth refresh tokens, session tokens, etc) in a way that
    /// survives process restarts without ending up in plaintext on disk.
    ///
    /// On Windows the host implementation is backed by
    /// <c>Microsoft.Maui.Storage.SecureStorage</c> which delegates to DPAPI.
    /// On macOS / iOS / Android the same <c>SecureStorage</c> API delegates to
    /// the platform keychain / KeyStore. Keys are application-scoped; a second
    /// app on the same device cannot read them.
    ///
    /// Plugins do not take a direct reference to the MAUI assembly (that would
    /// force them to target net10.0-* TFMs). Instead the host sets
    /// <see cref="PluginHostServices.SecureStorage"/> once at startup and
    /// plugins read it through this interface.
    /// </summary>
    public interface IPluginSecureStorage
    {
        Task<string?> GetAsync(string key);
        Task SetAsync(string key, string value);
        void Remove(string key);
    }

    /// <summary>
    /// Sign-time credential checkout. Replaces the phase-3 pattern of stashing
    /// API keys in long-lived <c>_apiKey</c> / <c>_apiSecret</c> provider
    /// fields. Providers call <see cref="CheckoutAsync"/> at each HTTP-signing
    /// site, use the returned <see cref="ApiKeyCheckoutResult"/> locally, and
    /// let it go out of scope. Because .NET strings are interned the bytes
    /// aren't zeroed — but the GC root window drops from "lifetime of the app"
    /// to "lifetime of one sign operation", which is the best we can do
    /// without reworking the sign paths to use <c>byte[]</c>.
    ///
    /// Default implementation does one read per checkout. Hot-path providers
    /// that can't tolerate SecureStorage latency per request should maintain
    /// a local 60-second session cache (unlock at connect-time, scrub on
    /// idle) — that's a provider-level decision, not something encoded on
    /// this interface.
    /// </summary>
    public interface IApiKeyCheckout
    {
        /// <summary>
        /// Fetches the currently-active credential for <paramref name="providerId"/>
        /// and <paramref name="marketType"/>. Returns <see cref="ApiKeyCheckoutResult.None"/>
        /// if no matching active key is configured — callers should treat that
        /// as "provider not configured" and return early rather than throwing.
        ///
        /// Callers must not cache the returned strings across operations.
        /// Treat the result as use-and-discard.
        /// </summary>
        Task<ApiKeyCheckoutResult> CheckoutAsync(
            string providerId,
            string marketType = "Spot",
            CancellationToken ct = default);
    }

    /// <summary>
    /// Use-and-discard credential bundle. <see cref="HasCredentials"/> is
    /// <c>false</c> when the host has no active profile matching the request.
    /// </summary>
    public readonly record struct ApiKeyCheckoutResult(
        string Key,
        string Secret,
        string Passphrase,
        bool HasCredentials)
    {
        public static readonly ApiKeyCheckoutResult None = new("", "", "", false);
    }

    /// <summary>
    /// Host-owned <see cref="HttpClient"/> factory. Centralizes the response
    /// size cap, the default User-Agent, and (importantly) a per-provider
    /// outbound-host allow-list — so a future provider bug that interpolates
    /// user input into a URL can't redirect requests at an attacker-chosen
    /// host.
    /// </summary>
    public interface IPluginHttpClientFactory
    {
        HttpClient Create(HttpClientPolicy policy);
    }

    /// <summary>
    /// Per-provider HTTP policy. <see cref="AllowedHosts"/> is a strict
    /// allow-list — a request to any other host is rejected at the handler
    /// level before bytes leave the process. If the provider legitimately
    /// talks to multiple base URLs (REST + WebSocket endpoints on different
    /// subdomains) list all of them.
    /// </summary>
    /// <param name="ProviderId">
    /// Stable identifier used in error messages and telemetry.
    /// </param>
    /// <param name="AllowedHosts">
    /// Host names (no scheme, no port). Subdomains must be listed explicitly;
    /// there is no suffix-matching shortcut. Case-insensitive.
    /// </param>
    /// <param name="MaxResponseContentBytes">
    /// Applied to the constructed <c>HttpClient.MaxResponseContentBufferSize</c>.
    /// Defaults to 32 MB — large enough for any legitimate analytics or
    /// market-data payload, small enough that a hostile CDN can't OOM the app.
    /// </param>
    /// <param name="Timeout">
    /// Request timeout. Defaults to 60 s.
    /// </param>
    /// <param name="UserAgent">
    /// Optional User-Agent override. Defaults to <c>AccessibleTrader/1.0</c>.
    /// </param>
    public sealed record HttpClientPolicy(
        string ProviderId,
        IReadOnlyList<string> AllowedHosts,
        long MaxResponseContentBytes = 32L * 1024 * 1024,
        TimeSpan? Timeout = null,
        string? UserAgent = null);

    /// <summary>
    /// Static service-locator bridge from plugins to the MAUI host. The host
    /// sets each property once during startup; plugins read them lazily and
    /// tolerate nulls (falling back to whatever local behaviour they used to
    /// ship with).
    ///
    /// Intentionally lightweight: plugins are activated through
    /// <see cref="System.Activator.CreateInstance(System.Type)"/> by
    /// <c>PluginLoaderService</c>, which gives them no DI container, so a
    /// static accessor is the pragmatic way to thread host-owned services
    /// across the plugin boundary without adding a whole plugin-contract
    /// revision.
    /// </summary>
    public static class PluginHostServices
    {
        /// <summary>
        /// Host-provided secure-storage bridge. <c>null</c> if the host has not
        /// configured one (e.g. in unit tests or a bare `dotnet run` of a
        /// plugin's CLI entry point). Plugins must null-check and fall through.
        /// </summary>
        public static IPluginSecureStorage? SecureStorage { get; set; }

        /// <summary>
        /// Host-provided credential checkout. See <see cref="IApiKeyCheckout"/>.
        /// </summary>
        public static IApiKeyCheckout? ApiKeys { get; set; }

        /// <summary>
        /// Host-provided <see cref="HttpClient"/> factory with size caps and
        /// outbound-host allow-list. See <see cref="IPluginHttpClientFactory"/>.
        /// </summary>
        public static IPluginHttpClientFactory? HttpClientFactory { get; set; }

        /// <summary>
        /// Host-provided audit log for security-relevant runtime events.
        /// See <see cref="ISecurityEventLog"/>. Plugins and Core services
        /// push to it via <see cref="ISecurityEventLog.Record"/>; the host
        /// retains the ring buffer for operator review. Null in tests / CLI
        /// contexts — callers null-check and no-op.
        /// </summary>
        public static ISecurityEventLog? SecurityEvents { get; set; }

        /// <summary>
        /// Convenience helper for provider plugins: get a capped, allow-listed
        /// <see cref="HttpClient"/> using the host factory if available, or a
        /// plain capped client otherwise (for unit tests / CLI scenarios
        /// where the host bridge hasn't been wired).
        ///
        /// The fallback path still applies <paramref name="maxResponseBytes"/>
        /// and <paramref name="timeout"/> so tests don't accidentally allow
        /// unbounded responses.
        /// </summary>
        public static HttpClient CreateHttpClient(
            string providerId,
            IReadOnlyList<string> allowedHosts,
            long maxResponseBytes = 32L * 1024 * 1024,
            TimeSpan? timeout = null,
            string? userAgent = null)
        {
            var factory = HttpClientFactory;
            if (factory != null)
            {
                return factory.Create(new HttpClientPolicy(
                    providerId, allowedHosts, maxResponseBytes, timeout, userAgent));
            }

            var http = new HttpClient
            {
                MaxResponseContentBufferSize = maxResponseBytes,
                Timeout = timeout ?? TimeSpan.FromSeconds(60),
            };
            if (!string.IsNullOrEmpty(userAgent))
                http.DefaultRequestHeaders.Add("User-Agent", userAgent);
            else
                http.DefaultRequestHeaders.Add("User-Agent", "AccessibleTrader/1.0");
            return http;
        }
    }
}
