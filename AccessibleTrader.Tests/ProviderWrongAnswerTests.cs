using System.Net;
using System.Reflection;
using AccessibleTrader.Plugins.Kraken;
using AccessibleTrader.Plugins.Tradier;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>Kraken's own asset vocabulary, on both sides of the comparison.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>GetFillsAsync</c> and <c>GetOpenOrdersAsync</c> filtered Kraken's answer against
    /// <c>CleanSymbol(symbol)</c> — so a request for <c>BTC/USD</c> looked for the substring
    /// <c>BTCUSD</c> inside pairs Kraken actually returns as <c>XXBTZUSD</c> and <c>XBTUSD</c>.
    /// It matched nothing. The History tab and the symbol-scoped orders list came back empty for
    /// the single most-traded pair on the venue, and an empty History tab is exactly what an
    /// account with no trades looks like. The file already owned the translation
    /// (<c>NormaliseAsset</c>) and neither call site used it.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class KrakenPairVocabularyTests
    {
        [Theory]
        // Kraken's legacy four-character codes, X for crypto and Z for fiat.
        [InlineData("XXBTZUSD", "BTCUSD")]
        [InlineData("XETHZUSD", "ETHUSD")]
        [InlineData("XXDGXXBT", "DOGEBTC")]
        [InlineData("XXTZZUSD", "XTZUSD")]
        // The modern spellings, which carry no prefixes.
        [InlineData("XBTUSD", "BTCUSD")]
        [InlineData("ETHUSDT", "ETHUSDT")]
        [InlineData("SOLUSD", "SOLUSD")]
        // What the user types.
        [InlineData("BTC/USD", "BTCUSD")]
        [InlineData("btc-usd", "BTCUSD")]
        [InlineData("DOGE/BTC", "DOGEBTC")]
        public void One_pair_has_one_canonical_spelling(string input, string expected)
        {
            Assert.Equal(expected, KrakenProvider.CanonicalKrakenPair(input));
        }

        [Fact]
        public void The_users_spelling_and_krakens_spelling_agree()
        {
            // This is the defect itself: the two sides of the comparison, which used to be
            // "BTCUSD" and "XXBTZUSD".
            Assert.Equal(
                KrakenProvider.CanonicalKrakenPair("BTC/USD"),
                KrakenProvider.CanonicalKrakenPair("XXBTZUSD"));
        }

        /// <summary>
        /// USD and USDT stay different markets.
        ///
        /// <para>
        /// The old filter used <c>Contains</c>, so had the vocabularies ever lined up a request
        /// for <c>BTCUSD</c> would have swept in every <c>BTCUSDT</c> fill as well. That is the
        /// conflation <c>GetCanonicalSymbol</c>'s doc exists to prevent, and it is why the
        /// comparison is now equality.
        /// </para>
        /// </summary>
        [Fact]
        public void A_usd_request_does_not_match_a_usdt_pair()
        {
            Assert.NotEqual(
                KrakenProvider.CanonicalKrakenPair("BTC/USD"),
                KrakenProvider.CanonicalKrakenPair("BTCUSDT"));
        }
    }

    /// <summary>
    /// <b>Tradier: a fill keeps its identity, and disconnect means disconnected.</b>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class TradierIdentityAndTeardownTests
    {
        /// <summary>
        /// The same fill has the same id on every fetch.
        ///
        /// <para>
        /// Tradier's history events carry no id of their own and this passed
        /// <c>Guid.NewGuid()</c>, so every History-tab refresh presented the entire history as
        /// new to anything that dedupes or reconciles by id. The event does carry date, symbol,
        /// quantity and price.
        /// </para>
        /// </summary>
        [Fact]
        public void A_fill_id_is_derived_from_the_fill_not_from_a_new_guid()
        {
            var at = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc);

            string first = TradierProvider.FillId("AAPL", at, 10, 190.25);
            string again = TradierProvider.FillId("AAPL", at, 10, 190.25);

            Assert.Equal(first, again);
            Assert.NotEmpty(first);
        }

        [Theory]
        [InlineData("MSFT", 10, 190.25)]   // different symbol
        [InlineData("AAPL", 11, 190.25)]   // different quantity
        [InlineData("AAPL", 10, 190.26)]   // different price
        public void Fills_that_differ_in_any_field_get_different_ids(string symbol, double qty, double price)
        {
            var at = new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc);

            Assert.NotEqual(
                TradierProvider.FillId("AAPL", at, 10, 190.25),
                TradierProvider.FillId(symbol, at, qty, price));
        }

        [Fact]
        public void Fills_at_different_times_get_different_ids()
        {
            Assert.NotEqual(
                TradierProvider.FillId("AAPL", new DateTime(2026, 8, 20, 14, 30, 0, DateTimeKind.Utc), 10, 190.25),
                TradierProvider.FillId("AAPL", new DateTime(2026, 8, 20, 14, 31, 0, DateTimeKind.Utc), 10, 190.25));
        }

        /// <summary>
        /// Disconnect drops the credentials and tears the account socket down.
        ///
        /// <para>
        /// It used to cancel one token source, null two strings and return — leaving the access
        /// token and the <c>Bearer</c> header live on both HTTP clients (every other provider
        /// calls <c>ScrubCredentials</c> here), and leaving <c>_accountWs</c> reconnecting and
        /// pushing order updates into the subject after the user had disconnected the provider.
        /// </para>
        /// </summary>
        [Fact]
        public async Task Disconnect_scrubs_the_token_and_clears_the_authorization_headers()
        {
            var provider = new TradierProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "secret-token", ["AccountId"] = "acct",
            });
            Assert.True(provider.IsConfigured);

            await provider.DisconnectAsync();

            Assert.False(provider.IsConfigured);   // IsConfigured reads _accessToken
            foreach (var field in typeof(TradierProvider)
                         .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(f => f.FieldType == typeof(HttpClient)))
            {
                var client = (HttpClient?)field.GetValue(provider);
                Assert.Null(client?.DefaultRequestHeaders.Authorization);
            }
        }

        [Fact]
        public async Task Disconnect_releases_the_account_websocket()
        {
            var provider = new TradierProvider();
            provider.Configure(new Dictionary<string, string>
            {
                ["AccessToken"] = "secret-token", ["AccountId"] = "acct",
            });

            var wsField = typeof(TradierProvider)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(f => f.FieldType == typeof(AccessibleTrader.Sdk.Services.ReconnectingWebSocket));
            wsField.SetValue(provider,
                new AccessibleTrader.Sdk.Services.ReconnectingWebSocket("ws://127.0.0.1:9/"));

            await provider.DisconnectAsync();

            // Nulled, not merely cancelled — it was only ever released in Dispose, so it kept
            // reconnecting and kept announcing fills on an account the user had left.
            Assert.Null(wsField.GetValue(provider));
        }

        /// <summary>
        /// The two calls that used to bypass the rate limiter go through it.
        ///
        /// <para>
        /// <c>GetOrderStatusAsync</c> is the one the order poller calls in a LOOP while an order
        /// is working, against a 120 req/min budget shared with the chart's own fetches — so a
        /// poll loop plus a chart refresh could push the account into a 429 during exactly the
        /// window where the user is waiting to hear whether they were filled.
        /// </para>
        ///
        /// <para>
        /// This reads the source because the limiter is an internal field with no observable
        /// effect under a fake transport that never rate-limits. It checks the two method bodies
        /// specifically rather than the file as a whole, so a limiter elsewhere cannot satisfy
        /// it.
        /// </para>
        /// </summary>
        [Theory]
        [InlineData("public async Task<List<TradeFill>> GetFillsAsync")]
        [InlineData("public async Task<OrderStatusSnapshot?> GetOrderStatusAsync")]
        public void The_history_and_status_calls_go_through_the_rate_limiter(string signature)
        {
            string src = File.ReadAllText(
                ProviderSourceFiles.ProviderFile("Providers", "Tradier", "TradierProvider.cs"));

            int start = src.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Method not found: {signature}");
            // Up to the next method-level doc comment, which is where the body ends.
            int end = src.IndexOf("\n        /// <summary>", start, StringComparison.Ordinal);
            if (end < 0) end = src.Length;
            string body = src[start..end];

            int getAt = body.IndexOf("_httpClient.GetStringAsync", StringComparison.Ordinal);
            Assert.True(getAt >= 0, $"{signature} no longer makes the HTTP call this guards.");
            int limiterAt = body.IndexOf("_rateLimiter.ExecuteAsync", StringComparison.Ordinal);
            Assert.True(limiterAt >= 0 && limiterAt < getAt,
                $"{signature} must take a rate-limit slot BEFORE the request, not go straight to the wire.");
        }
    }

    /// <summary>
    /// <b>The stale <c>GetOrderStatusAsync</c> contract is gone from every copy of it.</b>
    ///
    /// <para>
    /// The interface and both implementations said "Returns null on a transient failure (the
    /// poller retries)", and the comment in the very next line of each implementation said the
    /// opposite and explained why: a null read as "still resolving" turned a dead endpoint into
    /// a silent infinite retry, so the user waits forever for a fill announcement that cannot
    /// arrive. A third provider written from the doc would have reintroduced it exactly.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class OrderStatusContractDocTests
    {
        [Fact]
        public void No_copy_of_the_stale_transient_failure_doc_survives()
        {
            var offenders = ProviderSourceFiles.SdkAndPlugins()
                .Where(f => File.ReadAllText(f)
                    .Contains("Returns null on a transient failure", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .ToList();

            Assert.True(offenders.Count == 0,
                "A transient failure MUST throw, not return null. Stale doc still in:\n"
                + string.Join("\n", offenders));
        }
    }
}
