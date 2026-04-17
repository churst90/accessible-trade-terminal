using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// Bridges the SDK-side <see cref="IApiKeyCheckout"/> surface to the
    /// Core-side <see cref="IApiKeyService"/> (which talks to SecureStorage).
    /// Plugins call <see cref="IApiKeyCheckout.CheckoutAsync"/>; this adapter
    /// reads the currently-active credential for the requested provider and
    /// market type, and hands back a use-and-discard
    /// <see cref="ApiKeyCheckoutResult"/>.
    ///
    /// One read per checkout by default — keeps the credential string live
    /// only for the duration of a single sign operation. Hot-path providers
    /// that can't tolerate the SecureStorage latency per request should
    /// maintain a local 60-second session cache internally; that's a
    /// per-provider decision, not something encoded here.
    /// </summary>
    public sealed class MauiApiKeyCheckoutAdapter : IApiKeyCheckout
    {
        private readonly IApiKeyService _apiKeys;

        public MauiApiKeyCheckoutAdapter(IApiKeyService apiKeys)
        {
            _apiKeys = apiKeys;
        }

        public async Task<ApiKeyCheckoutResult> CheckoutAsync(
            string providerId,
            string marketType = "Spot",
            CancellationToken ct = default)
        {
            // GetKeyForProviderAsync returns the first profile matching
            // (provider, marketType). If it returns null there is no profile
            // configured — callers should treat that as "not configured".
            // We do not fall back to GetActiveKeyForProviderAsync because the
            // active-flag semantics are tied to Paper vs Live environment,
            // and providers usually pick the right Environment themselves.
            var cfg = await _apiKeys.GetKeyForProviderAsync(providerId, marketType).ConfigureAwait(false);
            if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
                return ApiKeyCheckoutResult.None;

            return new ApiKeyCheckoutResult(
                Key:           cfg.ApiKey      ?? "",
                Secret:        cfg.ApiSecret   ?? "",
                Passphrase:    cfg.Passphrase  ?? "",
                HasCredentials: true);
        }
    }
}
