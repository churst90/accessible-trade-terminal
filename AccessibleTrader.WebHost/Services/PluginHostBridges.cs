using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Security;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.WebHost.Services
{
    /// <summary>
    /// The <see cref="IApiKeyCheckout"/> instance handed to
    /// <see cref="PluginHostServices.ApiKeys"/> on this head.
    ///
    /// <para>
    /// <b>Why a separate registration.</b> <c>PluginHostServices.ApiKeys</c> is a
    /// process-wide static and <c>IApiKeyCheckout</c> is registered <c>Scoped</c>, so the
    /// 2026-08-24 assessment refused to bridge it: assigning a per-user service to a
    /// process-wide static pins one user's credentials for everyone. That reasoning was
    /// sound for the shape of the registration and wrong about the shape of the data —
    /// <c>IApiKeyService</c> is a <b>Singleton</b> here, backed by the singleton
    /// <c>WebHostSecureStorageService</c> whose secret store is deliberately shared
    /// process-wide (see <c>AccountsServiceExtensions</c>). There is exactly one credential
    /// store per WebHost process in every mode, so there is nothing per-user to pin. The
    /// adapter is only Scoped because <c>CheckoutLatencyTracker</c> is.
    /// </para>
    ///
    /// <para>
    /// <b>What it turns on.</b> Six trading providers (Kraken, Alpaca, Binance, Bitstamp,
    /// Coinbase, MEXC) branch on <c>PluginHostServices.ApiKeys == null</c> and fall back to
    /// credentials stashed in long-lived <c>Configure</c>-populated fields. With the bridge
    /// null, the whole per-request credential-checkout migration
    /// (<c>docs/CREDENTIAL_CHECKOUT_MIGRATION.md</c>) was inert on this head — including
    /// local <c>HostMode.Full</c>, the one WebHost mode that trades real money with real
    /// keys. That was the entire point of the migration: shrink the window a credential is
    /// GC-reachable from "lifetime of the app" to "lifetime of one sign operation".
    /// </para>
    ///
    /// <para>
    /// Latency tracking is deliberately absent: the tracker is per-circuit, and a
    /// process-wide bridge attributing every plugin's checkout to whichever circuit
    /// happened to exist first is worse than no number at all. Circuit-resolved callers
    /// still get the tracked Scoped adapter.
    /// </para>
    /// </summary>
    public sealed class PluginHostApiKeyBridge : IApiKeyCheckout
    {
        private readonly WebHostApiKeyCheckoutAdapter _inner;

        public PluginHostApiKeyBridge(IApiKeyService apiKeys, ILogger<WebHostApiKeyCheckoutAdapter>? logger = null)
            => _inner = new WebHostApiKeyCheckoutAdapter(apiKeys, tracker: null, logger: logger);

        public Task<ApiKeyCheckoutResult> CheckoutAsync(
            string providerId, string marketType = "Spot", CancellationToken ct = default)
            => _inner.CheckoutAsync(providerId, marketType, ct);
    }

    /// <summary>
    /// The <see cref="ISecurityEventLog"/> handed to
    /// <see cref="PluginHostServices.SecurityEvents"/> on this head.
    ///
    /// <para>
    /// <b>Why not the scoped one.</b> 22 call sites across <c>SchwabOAuthService</c>, the
    /// scripting sandbox (<c>OutOfProcessScriptHost</c>, <c>SandboxPolicy</c>,
    /// <c>WindowsAppContainerLauncher</c>) and <c>AlertDeliveryService</c> push to this
    /// static, which was never assigned on the WebHost — so on the hosted server every one
    /// of those audit records was dropped on the floor, silently, including
    /// <c>UnsandboxedScriptOverride</c> and <c>PluginTrustRejected</c>. The obvious fix,
    /// assigning the Scoped <c>ISecurityEventLog</c>, really would pin one user's audit sink
    /// for the whole process, which is why the earlier pass left it open.
    /// </para>
    ///
    /// <para>
    /// The resolution is that these events are not per-user in the first place. A sandbox
    /// that launched without its seccomp filter, a plugin DLL refused by the trust policy,
    /// an OAuth token that would not persist — those are properties of the <i>instance</i>,
    /// and an operator looking for them should not have to guess which user's directory
    /// they landed in. So this is an instance-level sink at
    /// <c>{dataRoot}/SecurityEvents</c>, a sibling of the per-user
    /// <c>{dataRoot}/users/{id}/SecurityEvents</c> the Razor Pages write to. Authentication
    /// events stay per-user; host events become findable.
    /// </para>
    /// </summary>
    /// <summary>
    /// The instance's own, NON-per-user data location.
    ///
    /// <para>
    /// On the hosted head <see cref="IPlatformPathService"/> is Scoped and routes per user,
    /// which is right for a visitor's settings and wrong for anything belonging to the
    /// server itself. This is the sibling handle for the latter, so a singleton does not
    /// have to either capture the per-user service (a captive dependency) or reach for the
    /// OS default and miss the configured <c>Accounts:DataRoot</c> entirely.
    /// </para>
    /// </summary>
    public sealed class InstancePaths
    {
        public InstancePaths(IPlatformPathService paths) => Paths = paths;

        public IPlatformPathService Paths { get; }

        /// <summary>Instance-level audit log — sibling of <c>users/{id}/SecurityEvents</c>.</summary>
        public string SecurityEventDirectory => Path.Combine(Paths.AppDataDirectory, "SecurityEvents");
    }

    public sealed class PluginHostSecurityEventLog : ISecurityEventLog, IDisposable
    {
        private readonly ISecurityEventLog _inner;
        private readonly IDisposable? _disposableInner;

        public PluginHostSecurityEventLog(InstancePaths paths, ILoggerFactory? loggerFactory = null)
        {
            var ring = new SecurityEventLog(loggerFactory?.CreateLogger<SecurityEventLog>());

            // Same environment-variable contract as every other sink on both heads: an
            // explicit *_DIR override wins (operators shipping to a log collector), and
            // persistence can be switched off entirely.
            var persistEnv = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_PERSIST");
            bool persistEnabled = string.IsNullOrEmpty(persistEnv)
                || !(persistEnv.Equals("0", StringComparison.Ordinal)
                  || persistEnv.Equals("false", StringComparison.OrdinalIgnoreCase));

            if (!persistEnabled)
            {
                _inner = ring;
                return;
            }

            string dir = Environment.GetEnvironmentVariable("ACCESSIBLETRADER_SECURITY_EVENT_DIR")
                ?? paths.SecurityEventDirectory;

            var sink = new SecurityEventFileSink(ring, dir, loggerFactory?.CreateLogger<SecurityEventFileSink>());
            _inner = sink;
            _disposableInner = sink;
        }

        public void Record(SecurityEvent ev) => _inner.Record(ev);

        public IReadOnlyList<SecurityEvent> Recent(int limit = 200, DateTime? since = null)
            => _inner.Recent(limit, since);

        public void Dispose() => _disposableInner?.Dispose();
    }
}
