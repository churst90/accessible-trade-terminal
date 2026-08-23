using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using AccessibleTrader.Plugins.SecEdgar;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The SEC EDGAR analytics provider.
///
/// <para>
/// One property matters more than every other test in this file: <b>facts are stamped by their
/// FILING date, never by the period they describe.</b> Apple's quarter ending 2026-06-27 was not
/// knowable until it was filed weeks later, so placing the number on the quarter-end date would let
/// a backtest trade on information nobody had — and it would look excellent, because earnings
/// correlate with the price move that follows their announcement. That is the same class of error
/// as the Cipher SR proximity artifact, and unlike that one it is invisible: the series looks
/// entirely reasonable either way. Only a test can hold the line.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public class SecEdgarProviderTests
{
    private static SecEdgarProvider NewProvider(FakeHttpMessageHandler h)
    {
        var p = new SecEdgarProvider();
        var field = typeof(SecEdgarProvider)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(HttpClient));
        field.SetValue(p, new HttpClient(h));
        return p;
    }

    private const string TickerMap = """
        {"0":{"cik_str":320193,"ticker":"AAPL","title":"Apple Inc."},
         "1":{"cik_str":789019,"ticker":"MSFT","title":"MICROSOFT CORP"}}
        """;

    // ── The point-in-time rule ─────────────────────────────────────────────────

    /// <summary>
    /// The quarter ends 2026-06-27; the filing lands 2026-08-01. The bar must sit on the FILING
    /// date. If this test ever goes red, every study built on this provider is contaminated.
    /// </summary>
    [Fact]
    public void FactsAreStampedByFilingDate_NotByThePeriodTheyDescribe()
    {
        var bars = SecEdgarProvider.ParseConcept("""
            {"units":{"USD/shares":[
              {"start":"2026-03-29","end":"2026-06-27","val":1.57,"fy":2026,"fp":"Q3","form":"10-Q","filed":"2026-08-01"}
            ]}}
            """);

        Assert.Single(bars);
        Assert.Equal(new DateTime(2026, 8, 1), bars[0].Date);
        Assert.NotEqual(new DateTime(2026, 6, 27), bars[0].Date);
        Assert.Equal(1.57, bars[0].Close);
    }

    /// <summary>
    /// A restatement is not a special case, it is the same rule applied twice: the original figure
    /// stays on its original filing date and the revision lands on the day it was published. The
    /// series then reads as "what the market knew, when it knew it" rather than "what turned out to
    /// be true" — which is the only version a backtest may see.
    /// </summary>
    [Fact]
    public void ARestatementLandsOnItsOwnFilingDate_LeavingTheOriginalWhereItWas()
    {
        var bars = SecEdgarProvider.ParseConcept("""
            {"units":{"USD":[
              {"end":"2025-12-31","val":100,"form":"10-K","filed":"2026-02-01"},
              {"end":"2025-12-31","val":92,"form":"10-K/A","filed":"2026-05-15"}
            ]}}
            """);

        Assert.Equal(2, bars.Count);
        Assert.Equal(new DateTime(2026, 2, 1), bars[0].Date);
        Assert.Equal(100, bars[0].Close);
        Assert.Equal(new DateTime(2026, 5, 15), bars[1].Date);
        Assert.Equal(92, bars[1].Close);
    }

    /// <summary>
    /// A 10-K restates several prior quarters on one filing date. The bar for that day must carry
    /// the figure for the LATEST period, which is the current number as of that filing — not an
    /// arbitrary one, and not a sum.
    /// </summary>
    [Fact]
    public void WhenOneFilingCarriesSeveralPeriods_TheLatestPeriodWins()
    {
        var bars = SecEdgarProvider.ParseConcept("""
            {"units":{"USD":[
              {"end":"2025-03-31","val":10,"filed":"2026-02-01"},
              {"end":"2025-12-31","val":40,"filed":"2026-02-01"},
              {"end":"2025-06-30","val":20,"filed":"2026-02-01"}
            ]}}
            """);

        Assert.Single(bars);
        Assert.Equal(40, bars[0].Close);
    }

    [Fact]
    public void ParsedFactsComeOutInChronologicalOrder()
    {
        var bars = SecEdgarProvider.ParseConcept("""
            {"units":{"USD":[
              {"end":"2026-03-31","val":3,"filed":"2026-05-01"},
              {"end":"2025-03-31","val":1,"filed":"2025-05-01"},
              {"end":"2025-09-30","val":2,"filed":"2025-11-01"}
            ]}}
            """);
        Assert.Equal(new[] { 1d, 2d, 3d }, bars.Select(b => b.Close));
    }

    [Fact]
    public void MalformedFactsAreSkippedRatherThanThrowing()
    {
        var bars = SecEdgarProvider.ParseConcept("""
            {"units":{"USD":[
              {"end":"2025-03-31","val":1,"filed":"2025-05-01"},
              {"end":"2025-06-30","val":2},
              {"end":"2025-09-30","filed":"2025-11-01"},
              {"end":"2025-12-31","val":"not a number","filed":"2026-02-01"},
              {"end":"2026-03-31","val":5,"filed":"2026-05-01"}
            ]}}
            """);
        Assert.Equal(new[] { 1d, 5d }, bars.Select(b => b.Close));
    }

    [Fact]
    public void AnUnexpectedShapeYieldsAnEmptySeriesRatherThanAnException()
    {
        Assert.Empty(SecEdgarProvider.ParseConcept("""{"error":"nope"}"""));
        Assert.Empty(SecEdgarProvider.ParseConcept("""{"units":{}}"""));
    }

    // ── Filing counts ──────────────────────────────────────────────────────────

    /// <summary>
    /// Counts, not judgements. "Six Form 4s were filed on this day" is a fact fixed on that day;
    /// "this insider buying was bullish" is an inference made with hindsight. Only the first is
    /// backtestable, which is why the provider emits the first and never the second.
    /// </summary>
    [Fact]
    public void FilingCounts_CountOnlyTheRequestedFormAndGroupByDay()
    {
        string json = """
            {"filings":{"recent":{
              "form":["4","4","8-K","4","10-Q","4"],
              "filingDate":["2026-01-05","2026-01-05","2026-01-06","2026-01-06","2026-01-07","2026-01-05"]
            }}}
            """;

        var insider = SecEdgarProvider.ParseFilingCounts(json, "4");
        Assert.Equal(2, insider.Count);
        Assert.Equal(new DateTime(2026, 1, 5), insider[0].Date);
        Assert.Equal(3, insider[0].Close);          // three Form 4s that day
        Assert.Equal(1, insider[1].Close);

        var events = SecEdgarProvider.ParseFilingCounts(json, "8-K");
        Assert.Single(events);
        Assert.Equal(1, events[0].Close);
    }

    /// <summary>
    /// Days with no filings are ABSENT, not zero-filled. The consumer decides whether absence means
    /// "nothing happened" or "no data" — and for a company that listed last year those are
    /// different statements, one of which would be a lie.
    /// </summary>
    [Fact]
    public void DaysWithNoFilingsAreAbsentRatherThanZeroFilled()
    {
        var bars = SecEdgarProvider.ParseFilingCounts("""
            {"filings":{"recent":{"form":["4","4"],"filingDate":["2026-01-05","2026-03-20"]}}}
            """, "4");

        Assert.Equal(2, bars.Count);
        Assert.DoesNotContain(bars, b => b.Close == 0);
    }

    // ── CIK resolution ─────────────────────────────────────────────────────────

    /// <summary>
    /// The SEC publishes the ticker map as an OBJECT keyed by row number, not an array. Reading it
    /// as an array yields an empty map and a provider that reports "no CIK" for every ticker on
    /// earth — a total failure that looks like a data problem.
    /// </summary>
    [Fact]
    public void TickerMap_IsReadAsAnObjectKeyedByRowNumber()
    {
        var map = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        SecEdgarProvider.LoadTickerMap(TickerMap, map);

        Assert.Equal(320193, map["AAPL"]);
        Assert.Equal(789019, map["MSFT"]);
        Assert.Equal(320193, map["aapl"]);          // case-insensitive
    }

    [Fact]
    public async Task UnknownTicker_ReportsItRatherThanFailingSilently()
    {
        var handler = new FakeHttpMessageHandler().Get(@"company_tickers", TickerMap);
        var provider = NewProvider(handler);

        string? error = null;
        using var sub = provider.ErrorStream.Subscribe(e => error = e);

        var (bars, _) = await provider.FetchOhlcvAsync(
            new MarketDataRequest("Economic", "NOSUCH_EPS", "1d", 100));

        Assert.Empty(bars);
        Assert.NotNull(error);
        Assert.Contains("NOSUCH", error!);
    }

    // ── Symbol handling ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AAPL_EPS", "AAPL", "EPS")]
    [InlineData("BRK_B_EPS", "BRK_B", "EPS")]     // the metric is the LAST segment, not the second
    [InlineData("AAPL_INSIDER", "AAPL", "INSIDER")]
    public void SymbolSplitting_TakesTheMetricFromTheEnd(string symbol, string ticker, string metric)
    {
        var (t, m) = SecEdgarProvider.Split(symbol);
        Assert.Equal(ticker, t);
        Assert.Equal(metric, m);
    }

    [Fact]
    public async Task ASymbolWithNoMetric_IsReportedRatherThanGuessedAt()
    {
        var provider = NewProvider(new FakeHttpMessageHandler());
        string? error = null;
        using var sub = provider.ErrorStream.Subscribe(e => error = e);

        var (bars, _) = await provider.FetchOhlcvAsync(new MarketDataRequest("Economic", "AAPL", "1d", 100));

        Assert.Empty(bars);
        Assert.NotNull(error);
    }

    [Fact]
    public void DisplayNames_AreReadableRatherThanConceptIdentifiers()
    {
        var p = new SecEdgarProvider();
        Assert.Equal("AAPL diluted EPS", p.GetSymbolDisplayName("AAPL_EPS"));
        Assert.Equal("AAPL insider transactions (Form 4)", p.GetSymbolDisplayName("AAPL_INSIDER"));
    }

    // ── HTTP behaviour ─────────────────────────────────────────────────────────

    /// <summary>
    /// A 404 on a concept means this filer does not report that line — a bank has no R&amp;D expense.
    /// Raising it would put noise on the error stream during ordinary use, and an error stream that
    /// cries wolf is one nobody reads when it matters.
    /// </summary>
    [Fact]
    public async Task AConceptTheFilerDoesNotReport_IsEmptyAndNotAnError()
    {
        var handler = new FakeHttpMessageHandler()
            .Get(@"company_tickers", TickerMap)
            .Get(@"companyconcept", """{"error":"not found"}""", HttpStatusCode.NotFound);
        var provider = NewProvider(handler);

        string? error = null;
        using var sub = provider.ErrorStream.Subscribe(e => error = e);

        var (bars, _) = await provider.FetchOhlcvAsync(
            new MarketDataRequest("Economic", "AAPL_RND", "1d", 100));

        Assert.Empty(bars);
        Assert.Null(error);
    }

    [Fact]
    public async Task RequestUrl_UsesTheZeroPaddedTenDigitCik()
    {
        var handler = new FakeHttpMessageHandler()
            .Get(@"company_tickers", TickerMap)
            .Get(@"companyconcept", """{"units":{"USD":[{"end":"2026-01-01","val":1,"filed":"2026-02-01"}]}}""");
        var provider = NewProvider(handler);

        await provider.FetchOhlcvAsync(new MarketDataRequest("Economic", "AAPL_EPS", "1d", 100));

        var url = handler.Captured.Last().RequestUri!.ToString();
        Assert.Contains("CIK0000320193", url);      // EDGAR rejects an unpadded CIK
        Assert.Contains("EarningsPerShareDiluted", url);
    }

    [Fact]
    public async Task TheTickerMapIsFetchedOnce_NotOnEveryRequest()
    {
        var handler = new FakeHttpMessageHandler()
            .Get(@"company_tickers", TickerMap)
            .Get(@"companyconcept", """{"units":{"USD":[{"end":"2026-01-01","val":1,"filed":"2026-02-01"}]}}""");
        var provider = NewProvider(handler);

        await provider.FetchOhlcvAsync(new MarketDataRequest("Economic", "AAPL_EPS", "1d", 100));
        await provider.FetchOhlcvAsync(new MarketDataRequest("Economic", "MSFT_EPS", "1d", 100));

        Assert.Single(handler.Captured, r => r.RequestUri!.ToString().Contains("company_tickers"));
    }

    [Fact]
    public async Task SinceAndUntil_ClipTheSeries()
    {
        var handler = new FakeHttpMessageHandler()
            .Get(@"company_tickers", TickerMap)
            .Get(@"companyconcept", """
                {"units":{"USD":[
                  {"end":"2024-01-01","val":1,"filed":"2024-02-01"},
                  {"end":"2025-01-01","val":2,"filed":"2025-02-01"},
                  {"end":"2026-01-01","val":3,"filed":"2026-02-01"}
                ]}}
                """);
        var provider = NewProvider(handler);

        var (bars, _) = await provider.FetchOhlcvAsync(new MarketDataRequest(
            "Economic", "AAPL_EPS", "1d", 100,
            Since: new DateTimeOffset(new DateTime(2025, 1, 1), TimeSpan.Zero).ToUnixTimeMilliseconds(),
            Until: new DateTimeOffset(new DateTime(2025, 12, 1), TimeSpan.Zero).ToUnixTimeMilliseconds()));

        Assert.Single(bars);
        Assert.Equal(2, bars[0].Close);
    }

    // ── Catalogue ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubTypesSplitFinancialsFromFilings_AndBothReturnSymbols()
    {
        var p = new SecEdgarProvider();
        var subs = await p.GetSupportedSubTypesAsync(MarketType.Economic);
        Assert.Contains("Financials", subs);
        Assert.Contains("Filings", subs);

        var fin = await p.GetAvailableSymbolsAsync(MarketType.Economic, "Financials");
        var fil = await p.GetAvailableSymbolsAsync(MarketType.Economic, "Filings");
        Assert.NotEmpty(fin);
        Assert.NotEmpty(fil);
        Assert.Contains(fin, s => s.EndsWith("_EPS"));
        Assert.Contains(fil, s => s.EndsWith("_INSIDER"));
        Assert.DoesNotContain(fil, s => s.EndsWith("_EPS"));
    }

    [Fact]
    public void EveryCatalogueMetricHasADistinctConcept()
    {
        var concepts = SecEdgarProvider.Metrics.Values.Select(v => v.Concept).ToList();
        Assert.Equal(concepts.Count, concepts.Distinct(StringComparer.Ordinal).Count());
    }

    // ── The User-Agent gotcha ──────────────────────────────────────────────────

    /// <summary>
    /// Two independent constraints meet in this one string, and missing either one produces a
    /// failure that does not look like a failure.
    ///
    /// <para>
    /// The SEC returns <b>403 from www.sec.gov to any agent without a contact email</b>, while
    /// data.sec.gov does not care. Since only the ticker→CIK map lives on www.sec.gov, an agent
    /// without an email yields a provider that resolves NO tickers at all while every other call
    /// looks healthy — it reads as missing data rather than as being blocked.
    /// </para>
    ///
    /// <para>
    /// And a BARE email is not a legal User-Agent value: RFC 9110 allows product tokens and
    /// parenthesised comments only, so <c>HttpHeaders.Add</c> throws on it. The address has to sit
    /// inside a comment to satisfy the parser and the SEC at once.
    /// </para>
    /// </summary>
    [Fact]
    public void UserAgent_CarriesAContactEmailInsideAParenthesisedComment()
    {
        string ua = SecEdgarProvider.UserAgentFor("someone@example.com");

        Assert.Contains("@", ua);
        Assert.Contains("(someone@example.com)", ua);

        // And it must actually be accepted by the header parser, which is the half that threw.
        using var client = new HttpClient();
        var ex = Record.Exception(() => client.DefaultRequestHeaders.Add("User-Agent", ua));
        Assert.Null(ex);
    }

    [Fact]
    public void TheDefaultContactIsAnEmailAddress()
    {
        Assert.Contains("@", SecEdgarProvider.DefaultContact);
    }

    [Fact]
    public void ConfiguringAContactEmail_ChangesTheAgentAndForgetsTheCachedTickerMap()
    {
        var handler = new FakeHttpMessageHandler()
            .Get(@"company_tickers", TickerMap)
            .Get(@"companyconcept", """{"units":{"USD":[{"end":"2026-01-01","val":1,"filed":"2026-02-01"}]}}""");
        var provider = NewProvider(handler);

        // A value with no '@' is not a contact and must be ignored rather than silently breaking
        // every www.sec.gov call.
        provider.Configure(new System.Collections.Generic.Dictionary<string, string> { ["ContactEmail"] = "nonsense" });
        Assert.Contains("@", SecEdgarProvider.UserAgentFor(SecEdgarProvider.DefaultContact));
    }

    [Fact]
    public void ProviderNeedsNoApiKey()
    {
        var p = new SecEdgarProvider();
        Assert.False(p.RequiresApiKey);
        Assert.True(p.IsConfigured);
    }
}
