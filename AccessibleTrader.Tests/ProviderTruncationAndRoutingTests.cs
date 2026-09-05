using System.Net;
using System.Reflection;
using AccessibleTrader.Plugins.SecEdgar;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>SEC EDGAR reads the whole filing history, not just the last page of it.</b>
    ///
    /// <para>
    /// ── What went wrong ────────────────────────────────────────────────────────
    /// <c>ParseFilingCounts</c> read only <c>filings.recent</c>. That block caps out around
    /// 1,000 entries, which for a large filer is well under a single year of Form 4s — so a
    /// "Form 4 clustering against its own baseline" study over five years ran against four years
    /// of implicit zeros, while the class doc promised "every filing with its form type and
    /// date". EDGAR paginates the rest into <c>filings.files</c>, a list of supplementary
    /// documents this never looked at.
    /// </para>
    ///
    /// <para>
    /// The provider's own doc gets the principle right — "days with no filings are absent rather
    /// than zero-filled: the consumer decides whether absence means zero" — and truncation
    /// defeats it in practice, because the consumer cannot tell truncation from absence. That is
    /// why the cap on how many pages are read is SAID rather than silently applied.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class SecEdgarFilingHistoryTests
    {
        /// <summary>The primary submissions document: recent filings plus a pointer to older pages.</summary>
        private const string PrimaryWithOlderPages = """
        {
          "filings": {
            "recent": { "form": ["4","4","8-K"], "filingDate": ["2026-08-20","2026-08-20","2026-08-19"] },
            "files":  [ { "name": "CIK0000320193-submissions-001.json" },
                        { "name": "CIK0000320193-submissions-002.json" } ]
          }
        }
        """;

        /// <summary>A supplementary page: the same arrays, at the ROOT rather than under filings.recent.</summary>
        private const string OlderPageOne = """
        { "form": ["4","4","4"], "filingDate": ["2021-03-10","2021-03-10","2021-03-11"] }
        """;

        private const string OlderPageTwo = """
        { "form": ["4","8-K"], "filingDate": ["2019-05-02","2019-05-02"] }
        """;

        [Fact]
        public void The_supplementary_pages_are_read_from_the_primary_document()
        {
            var names = SecEdgarProvider.SupplementaryFilingDocuments(PrimaryWithOlderPages);

            Assert.Equal(
                new[] { "CIK0000320193-submissions-001.json", "CIK0000320193-submissions-002.json" },
                names);
        }

        [Fact]
        public void A_filer_whose_history_fits_in_recent_has_no_supplementary_pages()
        {
            string onlyRecent = """
            { "filings": { "recent": { "form": ["4"], "filingDate": ["2026-08-20"] } } }
            """;

            Assert.Empty(SecEdgarProvider.SupplementaryFilingDocuments(onlyRecent));
        }

        /// <summary>
        /// A supplementary page carries its arrays at the root. Parsing it as though it were a
        /// primary document — looking under <c>filings.recent</c> — reads nothing at all, which
        /// is the failure mode that looks most like success: the fetch succeeds and the counts
        /// are simply short.
        /// </summary>
        [Fact]
        public void Both_document_shapes_parse()
        {
            var primary = SecEdgarProvider.ParseFilingCountsInto(PrimaryWithOlderPages, "4");
            Assert.Equal(2, primary[new DateTime(2026, 8, 20)]);

            var supplementary = SecEdgarProvider.ParseFilingCountsInto(OlderPageOne, "4");
            Assert.Equal(2, supplementary[new DateTime(2021, 3, 10)]);
            Assert.Equal(1, supplementary[new DateTime(2021, 3, 11)]);
        }

        [Fact]
        public async Task Filing_counts_span_every_page_not_just_the_recent_block()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(@".*/CIK0000320193\.json.*", PrimaryWithOlderPages);
            handler.Get(".*submissions-001.*", OlderPageOne);
            handler.Get(".*submissions-002.*", OlderPageTwo);
            handler.Get(".*company_tickers.*", """{"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."}}""");

            var provider = new SecEdgarProvider();
            HttpClientSwap.ReplaceAll(provider, handler);

            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("Fundamentals", "AAPL_INSIDER", "1d", 5000));

            var dates = bars.Select(b => b.Date).ToList();

            // The recent block alone stops at 2026 and the study loses everything older.
            Assert.Contains(new DateTime(2026, 8, 20), dates);
            Assert.Contains(new DateTime(2021, 3, 10), dates);
            Assert.Contains(new DateTime(2019, 5, 2), dates);
            // Counts per day survive the merge.
            Assert.Equal(2, bars.First(b => b.Date == new DateTime(2026, 8, 20)).Close);
            Assert.Equal(2, bars.First(b => b.Date == new DateTime(2021, 3, 10)).Close);
            // Only Form 4s — the 8-Ks on those same days are a different series.
            Assert.Equal(1, bars.First(b => b.Date == new DateTime(2019, 5, 2)).Close);
        }
    }

    /// <summary>
    /// <b>MyData will not draw a dataset at a spacing it does not have.</b>
    ///
    /// <para>
    /// <c>GetSupportedTimeframesAsync</c> has no symbol parameter, so it returns the union of
    /// every imported dataset's inferred timeframe for ANY dataset. Import one daily file and
    /// one monthly file, chart the daily one, pick "1M" from the dropdown that now offers it —
    /// and the fetch used to return the daily bars anyway, placed on a chart that had been told
    /// they were monthly. There is no resampling here, so the mismatch cannot be honoured; what
    /// it can be is refused, naming the spacing the dataset actually has.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class MyDataTimeframeHonestyTests
    {
        private sealed class TempPaths : AccessibleTrader.Core.Services.IPlatformPathService
        {
            public TempPaths(string root) { AppDataDirectory = root; CacheDirectory = root; }
            public string AppDataDirectory { get; }
            public string CacheDirectory { get; }
        }

        private static async Task<AccessibleTrader.Core.Services.MyData.MyDataProvider> ProviderWithTwoSpacingsAsync(
            string root)
        {
            var store = new AccessibleTrader.Core.Services.MyData.MyDataStore(
                new TempPaths(root),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AccessibleTrader.Core.Services.MyData.MyDataStore>.Instance);

            var daily = string.Join("\n", new[] { "date,open,high,low,close" }
                .Concat(Enumerable.Range(0, 6).Select(i => $"2026-01-{i + 1:00},10,11,9,{10 + i}")));
            await store.ImportAsync("daily", daily);

            var monthly = string.Join("\n", new[] { "date,Value" }
                .Concat(Enumerable.Range(0, 6).Select(i => $"2026-{i + 1:00}-01,{100 + i}")));
            await store.ImportAsync("monthly", monthly);

            return new AccessibleTrader.Core.Services.MyData.MyDataProvider(store);
        }

        [Fact]
        public async Task A_timeframe_the_dataset_does_not_have_is_refused_and_named()
        {
            string root = TestTemp.NewDir("mydata-timeframe");
            var provider = await ProviderWithTwoSpacingsAsync(root);

            var frames = await provider.GetSupportedTimeframesAsync();
            // The dropdown offers both spacings for either dataset — that is the union this
            // interface cannot avoid, and the reason the fetch has to be the honest one.
            Assert.True(frames.Count >= 2, $"Expected two spacings, got: {string.Join(",", frames)}");

            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            string other = frames.First(f => f != "1d");
            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("MyData", "daily", other, 500));

            Assert.Empty(bars);
            Assert.Contains(said, m => m.Contains("not resampled", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task The_dataset_still_charts_at_its_own_spacing()
        {
            // Vacuity check: refusing everything would satisfy the test above.
            string root = TestTemp.NewDir("mydata-timeframe-ok");
            var provider = await ProviderWithTwoSpacingsAsync(root);
            var said = new List<string>();
            provider.ErrorStream.Subscribe(said.Add);

            var (bars, _) = await provider.FetchOhlcvAsync(
                new MarketDataRequest("MyData", "daily", "1d", 500));

            Assert.NotEmpty(bars);
            Assert.Empty(said);
        }
    }

    /// <summary>
    /// <b>Alpaca picks its endpoint from the symbol, not from whatever chart is focused.</b>
    ///
    /// <para>
    /// <c>GetOrderBookAsync</c> chose crypto-vs-stock from <c>_currentMarket</c> — shared
    /// subscription state — so any caller asking about a symbol other than the focused chart's
    /// got the wrong endpoint and an empty book back. Latent while both call sites happen to
    /// pass the focused symbol, and silently wrong the moment one does not.
    /// </para>
    ///
    /// <para>
    /// The recount found a second defect in the same six lines that the report had not filed:
    /// the crypto branch used the CONCATENATED spelling for both the query and the response key,
    /// where v1beta3 requires the slashed pair — the exact rule <c>FetchOhlcvAsync</c> documents
    /// having already been caught by. So the crypto order book was empty whenever it was reached
    /// at all.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class AlpacaOrderBookRoutingTests
    {
        [Theory]
        [InlineData("AAPL", false)]
        [InlineData("TSLA", false)]
        [InlineData("BTC/USD", true)]
        [InlineData("BTCUSD", true)]
        [InlineData("ETH-USD", true)]
        public void Crypto_is_decided_by_the_symbol_alone(string symbol, bool expected)
        {
            Assert.Equal(expected, AccessibleTrader.Plugins.Alpaca.AlpacaProvider.IsCryptoSymbol(symbol));
        }

        private static (AccessibleTrader.Plugins.Alpaca.AlpacaProvider, FakeHttpMessageHandler) NewProvider()
        {
            var handler = new FakeHttpMessageHandler { StrictMode = false };
            handler.Get(".*", """{"quote":{"bp":1,"bs":2,"ap":3,"as":4}}""");
            var provider = new AccessibleTrader.Plugins.Alpaca.AlpacaProvider();
            provider.Configure(new Dictionary<string, string> { ["ApiKey"] = "k", ["ApiSecret"] = "s" });
            foreach (var field in provider.GetType()
                         .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(f => f.FieldType == typeof(HttpClient)))
            {
                field.SetValue(provider, new HttpClient(handler));
            }
            return (provider, handler);
        }

        [Fact]
        public async Task A_stock_goes_to_the_stock_endpoint_even_while_a_crypto_chart_is_focused()
        {
            var (provider, handler) = NewProvider();
            // The focused chart is crypto — the state this used to read.
            provider.GetType()
                .GetField("_currentMarket", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(provider, "Crypto");

            await provider.GetOrderBookAsync("AAPL");

            Assert.NotEmpty(handler.Captured);
            string url = handler.Captured[^1].RequestUri!.ToString();
            Assert.Contains("/stocks/AAPL/quotes/latest", url, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_crypto_pair_asks_for_the_slashed_symbol_the_api_requires()
        {
            var (provider, handler) = NewProvider();
            // The focused chart is a stock — again, the state this used to read.
            provider.GetType()
                .GetField("_currentMarket", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(provider, "Stock");

            await provider.GetOrderBookAsync("BTC/USD");

            Assert.NotEmpty(handler.Captured);
            var uri = handler.Captured[^1].RequestUri!;
            Assert.Contains("orderbooks", uri.AbsolutePath, StringComparison.Ordinal);
            // The pair must survive as BTC/USD, not arrive as BTCUSD.
            Assert.Equal("BTC/USD", Uri.UnescapeDataString(
                uri.Query.Split("symbols=")[1].Split('&')[0]));
        }
    }

    /// <summary>
    /// <b>An analytics provider releases the HttpClient it holds.</b>
    ///
    /// <para>
    /// <c>GlassnodeProvider</c> had no <c>Dispose(bool)</c> override at all, unlike every sibling
    /// (BGeometrics, Etherscan, Mempool, FRED), and <c>SecEdgarProvider.Configure</c> replaced
    /// its client without disposing the previous one on every reconfigure. Low blast radius on
    /// the desktop head, higher on the WebHost where providers are rebuilt per configuration
    /// change.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class AnalyticsProviderDisposalTests
    {
        private static HttpClient ClientOf(object provider) => HttpClientSwap.Single(provider);

        [Fact]
        public void Glassnode_disposes_its_http_client()
        {
            var provider = new AccessibleTrader.Plugins.Glassnode.GlassnodeProvider();
            var client = ClientOf(provider);

            provider.Dispose();

            // A disposed HttpClient throws on use; a leaked one does not.
            Assert.Throws<ObjectDisposedException>(() => client.CancelPendingRequests());
        }

        [Fact]
        public void SecEdgar_disposes_the_client_it_replaces_on_reconfigure()
        {
            var provider = new SecEdgarProvider();
            var first = ClientOf(provider);

            provider.Configure(new Dictionary<string, string> { ["ContactEmail"] = "someone@example.com" });
            var second = ClientOf(provider);

            Assert.NotSame(first, second);
            Assert.Throws<ObjectDisposedException>(() => first.CancelPendingRequests());

            provider.Dispose();
            Assert.Throws<ObjectDisposedException>(() => second.CancelPendingRequests());
        }
    }

    /// <summary>
    /// <b>The Wikipedia payload is deserialised once.</b>
    ///
    /// <para>
    /// <c>return (Parse(json), Parse(json).Select(...))</c> deserialised a payload of up to
    /// 4,000 points twice and materialised two throwaway lists to return one of them. Trivial,
    /// and invisible from outside — which is why this is a source check.
    /// </para>
    /// </summary>
    [Collection("ProviderCredentialBridge")]
    public class WikipediaSingleParseTests
    {
        [Fact]
        public void The_response_is_parsed_once_per_request()
        {
            // Comment lines are stripped first: the fix is documented by quoting the old code,
            // and a guard that counted the quotation would report the bug it just fixed.
            string src = string.Join("\n", File
                .ReadAllLines(ProviderSourceFiles.ProviderFile(
                    "Analytics", "WikipediaPageviews", "WikipediaPageviewsProvider.cs"))
                .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            // `(?<![.\w])` excludes JObject.Parse(json) inside the helper — a different call.
            int occurrences = System.Text.RegularExpressions.Regex
                .Matches(src, @"(?<![.\w])Parse\(json\)").Count;
            Assert.True(occurrences == 1,
                $"Expected exactly one Parse(json) call; found {occurrences}. Hoist it to a local.");
        }
    }
}
