using System.Collections.Concurrent;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// <b>Which stored credential each provider was last configured with.</b>
    ///
    /// <para>
    /// ── Why this exists ───────────────────────────────────────────────────────
    /// Until 2026-09-07 three parts of the app held three different opinions about which key an
    /// order used, and none of them was authoritative:
    /// </para>
    /// <list type="bullet">
    ///   <item>The <b>host</b> an order went to was fixed by whichever active key
    ///   <c>DataService.ConfigureStoredKeyProvidersAsync</c> reached first, and never changed —
    ///   the loop skips any provider already reporting <c>IsConfigured</c>, and seven plugins
    ///   report it unconditionally.</item>
    ///   <item>The <b>signature</b> came from <c>ApiKeyService.GetKeyForProviderAsync</c>, which
    ///   returns the first stored profile whose MarketType matches and never reads Environment
    ///   or IsActive. With a paper and a live profile stored for one venue, every order was
    ///   signed with whichever was saved first.</item>
    ///   <item>The dashboard's <b>"Switch API Key"</b> dropdown flipped the active flag and
    ///   announced a switch that nothing downstream read.</item>
    /// </list>
    ///
    /// <para>
    /// Host from one profile and signature from another is not a theoretical hazard: on the six
    /// venues with no practice environment it is how a key the user believes is paper signs a
    /// real order. One record, written where <c>Configure</c> is actually called and read by
    /// everything that needs to know, is what makes the question have ONE answer.
    /// </para>
    ///
    /// <para>
    /// ── Why a singleton service and not state on DataService ──────────────────
    /// <c>IDataService</c> is a <b>Singleton</b> on the MAUI head and <b>Scoped</b> on the
    /// WebHost (one instance per circuit), while <c>PluginHostServices.ApiKeys</c> — the
    /// checkout the plugins actually sign with — is a process-wide static. A record living on
    /// the scoped service would be invisible to the checkout that reads it. The credential store
    /// underneath is process-wide in every mode (see <c>PluginHostBridges</c>), so a
    /// process-wide record describes a process-wide fact. It is a registered singleton rather
    /// than a static so tests get a fresh one per case instead of racing each other.
    /// </para>
    ///
    /// <para>
    /// This holds a credential in memory for the lifetime of the process, which the
    /// per-request checkout migration deliberately moved away from. It is the same trade the
    /// key store itself already makes — the alternative is re-reading storage on the signing
    /// hot path to answer a question that only changes when the user changes it.
    /// </para>
    /// </summary>
    public interface ICredentialInUseRegistry
    {
        /// <summary>Record the credential just handed to <c>provider.Configure</c>.</summary>
        void Record(string providerName, ApiKeyConfig key);

        /// <summary>The credential in use for a provider, or null when nothing has configured it
        /// yet. Name matching is <see cref="ProviderNames"/>-tolerant, because a store can hold
        /// the same provider under two spellings.</summary>
        ApiKeyConfig? For(string? providerName);

        /// <summary>Forget every record — a full sign-out or a key store reset.</summary>
        void Clear();
    }

    /// <inheritdoc cref="ICredentialInUseRegistry"/>
    public sealed class CredentialInUseRegistry : ICredentialInUseRegistry
    {
        private readonly ConcurrentDictionary<string, ApiKeyConfig> _byProvider = new();

        public void Record(string providerName, ApiKeyConfig key)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return;
            _byProvider[ProviderNames.Normalize(providerName)] = key;
        }

        public ApiKeyConfig? For(string? providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName)) return null;
            return _byProvider.TryGetValue(ProviderNames.Normalize(providerName), out var k) ? k : null;
        }

        public void Clear() => _byProvider.Clear();
    }
}
