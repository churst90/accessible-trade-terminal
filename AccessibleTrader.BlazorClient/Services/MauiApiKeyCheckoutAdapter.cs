using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Diagnostics;
using AccessibleTrader.Sdk.Services;
using Microsoft.Extensions.Logging;

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
    ///
    /// Each call is timed and recorded into a per-provider rolling window
    /// (see <see cref="CheckoutLatencyTracker"/>). This is the measurement
    /// half of the "Hot-path credential cache" decision in <c>docs/TODO.md</c>:
    /// before adding a 60-second session cache we want real numbers from a
    /// live session, especially on Android KeyStore. Sustained P95 above
    /// <see cref="LatencyWarnThresholdMs"/> emits a Debug-level log so the
    /// JournalModal can surface it.
    /// </summary>
    public sealed class MauiApiKeyCheckoutAdapter : IApiKeyCheckout
    {
        private readonly IApiKeyService _apiKeys;
        private readonly CheckoutLatencyTracker? _tracker;
        private readonly ILogger<MauiApiKeyCheckoutAdapter>? _logger;

        // Per-call latency above this threshold logs at Debug level. Picked at
        // 50 ms because below that the cache wouldn't pay for the added
        // complexity (network round-trips dwarf the read on every platform);
        // above that it starts being user-visible on tick-rate hot paths.
        public const double LatencyWarnThresholdMs = 50.0;

        public MauiApiKeyCheckoutAdapter(
            IApiKeyService apiKeys,
            CheckoutLatencyTracker? tracker = null,
            ILogger<MauiApiKeyCheckoutAdapter>? logger = null)
        {
            _apiKeys = apiKeys;
            _tracker = tracker;
            _logger = logger;
        }

        public async Task<ApiKeyCheckoutResult> CheckoutAsync(
            string providerId,
            string marketType = "Spot",
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            try
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
            finally
            {
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                _tracker?.Record(providerId, ms);
                if (ms >= LatencyWarnThresholdMs)
                {
                    _logger?.LogDebug(
                        "Credential checkout for {ProviderId} ({Market}) took {Ms:F1} ms — exceeds {Threshold} ms threshold.",
                        providerId, marketType, ms, LatencyWarnThresholdMs);
                }
            }
        }
    }
}
