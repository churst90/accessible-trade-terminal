using System.Net;
using System.Reflection;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>A provider's HTTP failure carries its status code in the property, not just the text.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// Binance (three sites) and Schwab (three sites, counting the OAuth token endpoint) threw
    /// <c>new HttpRequestException($"{(int)resp.StatusCode} …")</c> — the status code in the
    /// <i>message</i> and nowhere else. <see cref="TransportFailure.IsTransient"/> reads
    /// <c>if (http.StatusCode is not { } status) return true;</c>, on the entirely reasonable
    /// grounds that an exception with no status never reached the venue at all.
    /// </para>
    ///
    /// <para>
    /// So a Binance <b>401 (bad key) or 404 (no such symbol) was classified as transient</b>:
    /// retried by the pipeline, counted against the per-provider circuit breaker, and announced
    /// to the user as a network problem. That is precisely the outcome
    /// <c>TransportFailure</c>'s own documentation says must not happen — it suspends a
    /// provider that is working perfectly and tells a blind user the wrong story about why
    /// their chart is empty. <c>RateLimiter.ShouldRetry</c> was defeated the same way and
    /// retried a known-bad key three times.
    /// </para>
    ///
    /// <para>
    /// ── What is enforced ───────────────────────────────────────────────────────
    /// These tests assert the <b>observable consequence</b>, not the constructor call. A test
    /// that only checked <c>ex.StatusCode != null</c> would restate the fix in different words.
    /// What actually matters is the branch both providers' fetch paths take:
    /// <c>if (TransportFailure.IsTransient(ex)) throw;</c> — a transient fault propagates so
    /// the pipeline's retry and circuit breaker can see it, and a permanent one is eaten
    /// locally. Before the fix, every status was transient, so an expired key went round the
    /// retry loop and tripped a breaker labelled "network issue" against a provider that was
    /// answering perfectly.
    /// </para>
    ///
    /// <para>
    /// 429 and 408 are pinned as still-propagating in the same breath, since a fix that made
    /// everything permanent would be exactly as wrong in the other direction.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class ProviderStatusCodeClassificationTests
    {
        private static void Swap(object provider, FakeHttpMessageHandler handler)
        {
            HttpClientSwap.ReplaceAll(provider, handler);
        }

        private static AccessibleTrader.Plugins.Binance.BinanceProvider Binance(FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Binance.BinanceProvider();
            Swap(p, h);
            return p;
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, false)] // bad key — retrying cannot help
        [InlineData(HttpStatusCode.NotFound, false)]     // no such symbol — same
        [InlineData(HttpStatusCode.Forbidden, false)]
        [InlineData((HttpStatusCode)429, true)]          // rate limited — waiting IS the fix
        [InlineData(HttpStatusCode.RequestTimeout, true)]
        [InlineData(HttpStatusCode.BadGateway, true)]    // 5xx — genuinely transient
        public async Task Binance_only_lets_a_genuinely_transient_fetch_failure_reach_the_pipeline(
            HttpStatusCode status, bool shouldReachPipeline)
        {
            var h = new FakeHttpMessageHandler()
                .Get(@"/api/v3/klines", """{"code":-1121,"msg":"Invalid symbol."}""", status);

            var ex = await Record.ExceptionAsync(() =>
                Binance(h).FetchOhlcvAsync(new MarketDataRequest("Crypto", "NOPE/USDT", "1m", 10)));

            if (shouldReachPipeline)
            {
                Assert.NotNull(ex);
                Assert.True(TransportFailure.IsTransient(ex),
                    $"HTTP {(int)status} must stay transient so the retry and breaker still work.");
            }
            else
            {
                Assert.Null(ex); // eaten locally; the breaker never sees a working provider fail
            }
        }

        [Fact]
        public void Binance_puts_the_status_code_where_the_classifier_reads_it()
        {
            // The mechanism, pinned directly: IsTransient reads the PROPERTY, and reading a
            // status out of the message text is not something it can do or should have to.
            var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var made = (HttpRequestException)typeof(AccessibleTrader.Plugins.Binance.BinanceProvider)
                .GetMethod("HttpFailure", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { resp, """{"code":-2015,"msg":"Invalid API-key."}""" })!;

            Assert.Equal(HttpStatusCode.Unauthorized, made.StatusCode);
            Assert.False(TransportFailure.IsTransient(made));
        }

        // ── Schwab ────────────────────────────────────────────────────────────

        /// <summary>
        /// A Schwab provider that will actually issue the HTTP request.
        ///
        /// <para>Both <c>IsConfigured</c> (client id + secret) and a live OAuth access token
        /// have to be in place or <c>FetchOhlcvAsync</c> returns an empty result before
        /// touching the network — which would make every assertion below pass for the wrong
        /// reason. <see cref="Schwab_fixture_actually_reaches_the_network"/> is the vacuity
        /// check that keeps this honest.</para>
        /// </summary>
        private static AccessibleTrader.Plugins.Schwab.SchwabProvider ConfiguredSchwab(
            FakeHttpMessageHandler h)
        {
            var p = new AccessibleTrader.Plugins.Schwab.SchwabProvider();
            p.Configure(new Dictionary<string, string>
            {
                ["ApiKey"] = "client-id",
                ["ApiSecret"] = "client-secret",
                // IsConfigured also requires a refresh token; Configure maps Passphrase onto it.
                ["Passphrase"] = "seeded-refresh-token",
            });
            Swap(p, h);

            // Seed a non-expired access token so GetValidAccessTokenAsync short-circuits
            // instead of attempting a refresh against the token endpoint.
            var oauthField = typeof(AccessibleTrader.Plugins.Schwab.SchwabProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType.Name == "SchwabOAuthService");
            var oauth = oauthField.GetValue(p)!;
            oauth.GetType().GetProperty("AccessToken")!
                 .SetValue(oauth, "seeded-access-token");
            oauth.GetType().GetProperty("AccessTokenExpiresAtUtc")!
                 .SetValue(oauth, DateTime.UtcNow.AddHours(1));

            return p;
        }

        [Fact]
        public async Task Schwab_fixture_actually_reaches_the_network()
        {
            // Vacuity check. FetchOhlcvAsync returns empty without a request when the provider
            // is unconfigured or has no token, so a green classification theory below would
            // otherwise prove nothing at all.
            var h = new FakeHttpMessageHandler()
                .Get(@"/marketdata/v1/pricehistory", """{"candles":[]}""");

            await ConfiguredSchwab(h).FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 10));

            Assert.NotEmpty(h.Captured);
        }

        [Theory]
        [InlineData(HttpStatusCode.NotFound, false)]
        [InlineData(HttpStatusCode.BadRequest, false)]
        [InlineData((HttpStatusCode)429, true)]
        [InlineData(HttpStatusCode.ServiceUnavailable, true)]
        public async Task Schwab_only_lets_a_genuinely_transient_fetch_failure_reach_the_pipeline(
            HttpStatusCode status, bool shouldReachPipeline)
        {
            var h = new FakeHttpMessageHandler()
                .Get(@"/marketdata/v1/pricehistory", """{"errors":["nope"]}""", status);

            var p = ConfiguredSchwab(h);

            var ex = await Record.ExceptionAsync(() =>
                p.FetchOhlcvAsync(new MarketDataRequest("Stock", "AAPL", "1m", 10)));

            if (shouldReachPipeline)
            {
                Assert.NotNull(ex);
                Assert.True(TransportFailure.IsTransient(ex),
                    $"HTTP {(int)status} must stay transient so the retry and breaker still work.");
            }
            else
            {
                Assert.Null(ex);
            }
        }
    }
}
