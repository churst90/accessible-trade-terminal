using System.Diagnostics;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Diagnostics;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// WebHost mirror of <c>MauiApiKeyCheckoutAdapter</c>. Bridges the
    /// SDK-side <see cref="IApiKeyCheckout"/> surface to the Core-side
    /// <see cref="IApiKeyService"/>, records per-provider checkout latency,
    /// and logs slow checkouts above <see cref="LatencyWarnThresholdMs"/>.
    /// Same semantics as the MAUI version.
    /// </summary>
    public sealed class WebHostApiKeyCheckoutAdapter : IApiKeyCheckout
    {
        public const double LatencyWarnThresholdMs = 50.0;

        private readonly IApiKeyService _apiKeys;
        private readonly CheckoutLatencyTracker? _tracker;
        private readonly ILogger<WebHostApiKeyCheckoutAdapter>? _logger;

        public WebHostApiKeyCheckoutAdapter(
            IApiKeyService apiKeys,
            CheckoutLatencyTracker? tracker = null,
            ILogger<WebHostApiKeyCheckoutAdapter>? logger = null)
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
                var cfg = await _apiKeys.GetKeyForProviderAsync(providerId, marketType).ConfigureAwait(false);
                if (cfg == null || string.IsNullOrEmpty(cfg.ApiKey))
                    return ApiKeyCheckoutResult.None;

                return new ApiKeyCheckoutResult(
                    Key: cfg.ApiKey ?? "",
                    Secret: cfg.ApiSecret ?? "",
                    Passphrase: cfg.Passphrase ?? "",
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
