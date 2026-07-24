using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Sdk.Services;

namespace AccessibleTrader.Tests.Fakes
{
    /// <summary>
    /// Deterministic <see cref="IApiKeyCheckout"/> for provider parse tests.
    /// Returns the same canned credential bundle for every (providerId, marketType)
    /// pair so the auth-gated FetchOhlcvAsync / order-book / signing paths run
    /// to completion against the <see cref="FakeHttpMessageHandler"/>.
    ///
    /// Tests that need to verify "no creds → empty" should pass
    /// <see cref="HasCredentials"/> = false; tests that need the happy path use
    /// the default constructor.
    /// </summary>
    public sealed class FakeApiKeyCheckout : IApiKeyCheckout
    {
        public bool HasCredentials { get; init; } = true;
        public string Key { get; init; } = "test-key";
        // Valid Base64 ("test-secret" encoded): some providers (Kraken) Base64-decode the
        // secret to derive the HMAC key. A non-Base64 secret makes that decode THROW, the
        // request is never signed/sent, and any test that shares this bridge (via xUnit
        // scheduling) sees zero HTTP calls — a rare, order-dependent flake. Keep it Base64.
        public string Secret { get; init; } = "dGVzdC1zZWNyZXQ=";
        public string Passphrase { get; init; } = "";

        public Task<ApiKeyCheckoutResult> CheckoutAsync(string providerId, string marketType = "Spot", CancellationToken ct = default)
            => Task.FromResult(HasCredentials
                ? new ApiKeyCheckoutResult(Key, Secret, Passphrase, true)
                : ApiKeyCheckoutResult.None);

        /// <summary>
        /// Installs this fake into <see cref="PluginHostServices.ApiKeys"/> for
        /// the duration of a test. The returned scope restores the previous
        /// host on dispose so tests don't bleed state across runs.
        /// </summary>
        public ApiKeyCheckoutScope Install()
        {
            var previous = PluginHostServices.ApiKeys;
            PluginHostServices.ApiKeys = this;
            return new ApiKeyCheckoutScope(previous);
        }
    }

    public readonly struct ApiKeyCheckoutScope : System.IDisposable
    {
        private readonly IApiKeyCheckout? _previous;
        public ApiKeyCheckoutScope(IApiKeyCheckout? previous) { _previous = previous; }
        public void Dispose() => PluginHostServices.ApiKeys = _previous;
    }
}
