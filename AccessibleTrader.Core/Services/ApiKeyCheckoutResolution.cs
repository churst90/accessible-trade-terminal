namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// <b>Which credential a plugin signs with, answered once for both heads.</b>
    ///
    /// <para>
    /// The MAUI and WebHost checkout adapters are deliberate mirrors of each other, and a rule
    /// this consequential written out twice is a rule that will be changed once. It lives here so
    /// the two heads cannot drift.
    /// </para>
    ///
    /// <para>
    /// ── The rule ──────────────────────────────────────────────────────────────
    /// <b>The credential IN USE wins.</b> That is the profile <c>DataService</c> last pushed into
    /// <c>provider.Configure</c>, which is what chose the venue's HOST. Signing with anything
    /// else means the host comes from one stored profile and the signature from another, and on
    /// the six venues with no practice environment that mismatch is how a Paper-labelled key
    /// signs a real order.
    /// </para>
    ///
    /// <para>
    /// The old behaviour — <c>GetKeyForProviderAsync</c>, first profile whose MarketType matches,
    /// blind to both Environment and IsActive — survives only as the fallback for a provider
    /// nothing has configured yet. That is the lazy first-fetch case, where no host has been
    /// chosen either, so there is nothing for the signature to disagree with.
    /// </para>
    ///
    /// <para>
    /// The old MAUI comment said the fallback was safe because "providers usually pick the right
    /// Environment themselves". They pick it from the dictionary <c>Configure</c> was given —
    /// that is, from the credential in use — which is precisely the record this reads.
    /// </para>
    /// </summary>
    public static class ApiKeyCheckoutResolution
    {
        public static async Task<ApiKeyConfig?> ResolveAsync(
            ICredentialInUseRegistry inUse,
            IApiKeyService apiKeys,
            string providerId,
            string marketType)
        {
            var recorded = inUse.For(providerId);
            // A withdrawal profile must never sign a trade. It cannot get into the record —
            // both write paths exclude it — but the check is cheap and the cost of being wrong
            // here is the one thing the separate-profiles design exists to prevent.
            if (recorded != null && !recorded.AllowsWithdrawal && !string.IsNullOrEmpty(recorded.ApiKey))
                return recorded;

            return await apiKeys.GetKeyForProviderAsync(providerId, marketType).ConfigureAwait(false);
        }
    }
}
