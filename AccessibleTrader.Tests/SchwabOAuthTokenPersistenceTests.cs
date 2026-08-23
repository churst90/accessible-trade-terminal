using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AccessibleTrader.Plugins.Schwab;
using AccessibleTrader.Sdk.Services;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Pins the 2026-08-22 fix for the third instance of the hand-built
    /// per-user-path defect (see <see cref="PerUserPathPolicyTests"/>):
    /// <c>SchwabOAuthService</c> used to compose <c>%AppData%/AccessibleTrader</c>
    /// itself and write the OAuth refresh token there. The token now persists
    /// ONLY through <see cref="PluginHostServices.SecureStorage"/>; with no
    /// bridge the token is memory-only for the session (non-persist), so a
    /// multi-user host without a bridge can never leak one user's token to
    /// another through a shared file.
    ///
    /// In the collection because it installs/restores the process-wide
    /// <see cref="PluginHostServices.SecureStorage"/> static that provider
    /// construction reads.
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public sealed class SchwabOAuthTokenPersistenceTests : IDisposable
    {
        private sealed class FakeSecureStorage : IPluginSecureStorage
        {
            public readonly ConcurrentDictionary<string, string> Store = new();
            public Task<string?> GetAsync(string key)
                => Task.FromResult(Store.TryGetValue(key, out var v) ? v : (string?)null);
            public Task SetAsync(string key, string value)
            {
                Store[key] = value;
                return Task.CompletedTask;
            }
            public void Remove(string key) => Store.TryRemove(key, out _);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;
            public int Calls;
            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                });
            }
        }

        private readonly IPluginSecureStorage? _previousBridge;

        public SchwabOAuthTokenPersistenceTests()
        {
            _previousBridge = PluginHostServices.SecureStorage;
        }

        public void Dispose()
        {
            PluginHostServices.SecureStorage = _previousBridge;
        }

        private static SchwabOAuthService NewService(HttpMessageHandler? handler = null)
            => new(new HttpClient(handler ?? new StubHandler(HttpStatusCode.OK, "{}")));

        [Fact]
        public void Seed_PersistsToBridge_UnderClientScopedKey()
        {
            var bridge = new FakeSecureStorage();
            PluginHostServices.SecureStorage = bridge;

            using var svc = NewService();
            svc.Configure("client-a", "secret");
            svc.SeedRefreshTokenIfMissing("tok-a");

            Assert.Equal("tok-a", bridge.Store["schwab_refresh_client-a"]);
        }

        [Fact]
        public void Configure_LoadsTokenFromBridge_AndKeysByClientId()
        {
            var bridge = new FakeSecureStorage();
            bridge.Store["schwab_refresh_client-a"] = "tok-a";
            PluginHostServices.SecureStorage = bridge;

            using var sameClient = NewService();
            sameClient.Configure("client-a", "secret");
            Assert.True(sameClient.HasRefreshToken);
            Assert.Equal("tok-a", sameClient.RefreshToken);

            // A different Schwab app registration must not see client-a's token.
            using var otherClient = NewService();
            otherClient.Configure("client-b", "secret");
            Assert.False(otherClient.HasRefreshToken);
        }

        [Fact]
        public void NoBridge_IsNonPersist_TokenDoesNotSurviveTheInstance()
        {
            PluginHostServices.SecureStorage = null;

            using (var first = NewService())
            {
                first.Configure("client-a", "secret");
                first.SeedRefreshTokenIfMissing("tok-a");
                Assert.True(first.HasRefreshToken); // memory-only for the session
            }

            // A fresh instance (fresh session) finds nothing anywhere — no file
            // was written. This is the guarantee that replaced the DPAPI-file
            // tier: without a host bridge there is nothing on disk to share.
            using var second = NewService();
            second.Configure("client-a", "secret");
            Assert.False(second.HasRefreshToken);
        }

        [Fact]
        public async Task RejectedRefresh_ScrubsTokenFromBridge()
        {
            var bridge = new FakeSecureStorage();
            bridge.Store["schwab_refresh_client-a"] = "tok-a";
            PluginHostServices.SecureStorage = bridge;

            var handler = new StubHandler(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
            using var svc = NewService(handler);
            svc.Configure("client-a", "secret");

            await Assert.ThrowsAsync<SchwabReauthRequiredException>(
                () => svc.RefreshAccessTokenAsync());

            Assert.Equal(1, handler.Calls);
            Assert.False(bridge.Store.ContainsKey("schwab_refresh_client-a"));
        }

        [Fact]
        public async Task SuccessfulRefresh_PersistsRotatedTokenToBridge()
        {
            var bridge = new FakeSecureStorage();
            bridge.Store["schwab_refresh_client-a"] = "tok-old";
            PluginHostServices.SecureStorage = bridge;

            var handler = new StubHandler(HttpStatusCode.OK,
                """{"access_token":"at-1","refresh_token":"tok-new","expires_in":1800,"token_type":"Bearer"}""");
            using var svc = NewService(handler);
            svc.Configure("client-a", "secret");

            await svc.RefreshAccessTokenAsync();

            Assert.True(svc.HasAccessToken);
            Assert.Equal("tok-new", bridge.Store["schwab_refresh_client-a"]);
        }
    }
}
