using System.Net;
using System.Reflection;
using AccessibleTrader.Plugins.WikipediaPageviews;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Wikipedia pageviews analytics provider.
///
/// <para>
/// The provider audit's finding was that this codebase's dominant defect class is the SILENT READ
/// PATH: a source changes shape or a symbol stops resolving, the fetch returns nothing, and the
/// chart renders empty with no error anywhere. These tests aim at that class specifically — the
/// window clamp, the 404 distinction, the ordering guarantee, and the fact that a catalogue entry
/// must map to something real.
/// </para>
/// </summary>
[Collection("ProviderCredentialBridge")]
public class WikipediaPageviewsProviderTests
{
    private static WikipediaPageviewsProvider NewProvider(FakeHttpMessageHandler h)
    {
        var p = new WikipediaPageviewsProvider();
        var field = typeof(WikipediaPageviewsProvider)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(f => f.FieldType == typeof(HttpClient));
        field.SetValue(p, new HttpClient(h));
        return p;
    }

    private const string TwoDays = """
        {"items":[
          {"project":"en.wikipedia","article":"Bitcoin","granularity":"daily","timestamp":"2026070100","access":"all-access","agent":"user","views":13137},
          {"project":"en.wikipedia","article":"Bitcoin","granularity":"daily","timestamp":"2026070200","access":"all-access","agent":"user","views":9938}
        ]}
        """;

    // ── Parsing ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ReturnsFlatBarsWithViewsInEveryLeg()
    {
        var provider = NewProvider(new FakeHttpMessageHandler().Get(@"per-article", TwoDays));

        var (bars, _) = await provider.FetchOhlcvAsync(
            new MarketDataRequest("Sentiment", "BTC_VIEWS", "1d", 10));

        Assert.Equal(2, bars.Count);
        Assert.Equal(new DateTime(2026, 7, 1), bars[0].Date);
        Assert.Equal(13137, bars[0].Close);
        Assert.Equal(bars[0].Open, bars[0].Close);
        Assert.Equal(bars[0].High, bars[0].Low);
        Assert.Equal(0, bars[0].Volume);          // analytics series carry no volume
    }

    /// <summary>
    /// The upstream happens to return oldest-first, but that is not a documented guarantee, and an
    /// out-of-order series produces WRONG indicator values rather than an error — nothing downstream
    /// re-sorts. So the provider sorts, and this pins it with a deliberately shuffled payload.
    /// </summary>
    [Fact]
    public void Parse_SortsChronologically_EvenWhenTheSourceDoesNot()
    {
        var shuffled = """
            {"items":[
              {"timestamp":"2026070300","views":3},
              {"timestamp":"2026070100","views":1},
              {"timestamp":"2026070200","views":2}
            ]}
            """;
        var bars = WikipediaPageviewsProvider.Parse(shuffled);
        Assert.Equal(new[] { 1d, 2d, 3d }, bars.Select(b => b.Close));
    }

    [Fact]
    public void Parse_SkipsMalformedEntriesRatherThanThrowing()
    {
        var messy = """
            {"items":[
              {"timestamp":"2026070100","views":10},
              {"timestamp":"nonsense","views":20},
              {"timestamp":"2026070300"},
              {"views":40},
              {"timestamp":"2026070500","views":"not a number"},
              {"timestamp":"2026070600","views":60}
            ]}
            """;
        var bars = WikipediaPageviewsProvider.Parse(messy);
        Assert.Equal(new[] { 10d, 60d }, bars.Select(b => b.Close));
    }

    [Fact]
    public void Parse_ReturnsEmptyForAnUnexpectedShape()
    {
        Assert.Empty(WikipediaPageviewsProvider.Parse("""{"detail":"not found"}"""));
    }

    // ── The window clamp ───────────────────────────────────────────────────────

    /// <summary>
    /// The clamp is not cosmetic. Asking for a range starting before 2015-07-01 makes the endpoint
    /// 404 the ENTIRE request rather than clip it — so a chart requesting "5000 daily bars" would
    /// render completely empty instead of showing the eleven years that do exist. This is the exact
    /// silent-empty failure the provider audit named, and it would look like "the provider is
    /// broken" rather than "you asked for too much history".
    /// </summary>
    [Fact]
    public void ResolveWindow_ClampsToTheFirstDateTheSourceHas()
    {
        var req = new MarketDataRequest("Sentiment", "BTC_VIEWS", "1d", 5000,
            Since: new DateTimeOffset(new DateTime(2001, 1, 1), TimeSpan.Zero).ToUnixTimeMilliseconds(),
            Until: new DateTimeOffset(new DateTime(2020, 1, 1), TimeSpan.Zero).ToUnixTimeMilliseconds());

        var (from, to) = WikipediaPageviewsProvider.ResolveWindow(req, monthly: false);

        Assert.Equal(WikipediaPageviewsProvider.FirstAvailable, from);
        Assert.Equal(new DateTime(2020, 1, 1), to);
    }

    [Fact]
    public void ResolveWindow_DerivesTheStartFromLimitWhenNoSinceIsGiven()
    {
        var until = new DateTime(2026, 7, 10);
        var req = new MarketDataRequest("Sentiment", "BTC_VIEWS", "1d", 30,
            Until: new DateTimeOffset(until, TimeSpan.Zero).ToUnixTimeMilliseconds());

        var (from, to) = WikipediaPageviewsProvider.ResolveWindow(req, monthly: false);

        Assert.Equal(until.AddDays(-30), from);
        Assert.Equal(until, to);
    }

    [Fact]
    public void ResolveWindow_CountsMonthsNotDaysOnTheMonthlyTimeframe()
    {
        var until = new DateTime(2026, 7, 10);
        var req = new MarketDataRequest("Sentiment", "BTC_VIEWS", "1M", 12,
            Until: new DateTimeOffset(until, TimeSpan.Zero).ToUnixTimeMilliseconds());

        var (from, _) = WikipediaPageviewsProvider.ResolveWindow(req, monthly: true);

        Assert.Equal(until.AddMonths(-12), from);
    }

    // ── HTTP behaviour ─────────────────────────────────────────────────────────

    /// <summary>
    /// A 404 from this endpoint means "no data for this article in this window" — an article created
    /// last year genuinely has nothing in 2016. Treating it as an error would push noise onto the
    /// error stream on every ordinary chart load, and an error stream that cries wolf is one nobody
    /// reads when it matters.
    /// </summary>
    [Fact]
    public async Task NoDataForTheWindow_ReturnsEmptyWithoutRaisingAnError()
    {
        var provider = NewProvider(new FakeHttpMessageHandler()
            .Get(@"per-article", """{"type":"not_found"}""", HttpStatusCode.NotFound));

        string? error = null;
        using var sub = provider.ErrorStream.Subscribe(e => error = e);

        var (bars, _) = await provider.FetchOhlcvAsync(
            new MarketDataRequest("Sentiment", "BTC_VIEWS", "1d", 10));

        Assert.Empty(bars);
        Assert.Null(error);
    }

    /// <summary>
    /// A real transport failure, by contrast, must be visible — and visible TWICE over:
    /// reported on the error stream, and rethrown so DataOrchestrator's retry and
    /// circuit breaker can act on it. It used to be reported and then swallowed into an
    /// empty result, which is indistinguishable from "this article has no pageviews".
    /// See TransportFailure.
    /// </summary>
    [Fact]
    public async Task ServerError_ReportsOnTheErrorStreamAndRethrows()
    {
        var provider = NewProvider(new FakeHttpMessageHandler()
            .Get(@"per-article", "boom", HttpStatusCode.InternalServerError));

        string? error = null;
        using var sub = provider.ErrorStream.Subscribe(e => error = e);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.FetchOhlcvAsync(
            new MarketDataRequest("Sentiment", "BTC_VIEWS", "1d", 10)));

        Assert.NotNull(error);
    }

    [Fact]
    public async Task RequestUrl_UsesTheMappedArticleAndTheUserAgentFilter()
    {
        var handler = new FakeHttpMessageHandler().Get(@"per-article", TwoDays);
        var provider = NewProvider(handler);

        await provider.FetchOhlcvAsync(new MarketDataRequest("Sentiment", "TSLA_VIEWS", "1d", 10));

        var url = handler.Captured.Single().RequestUri!.ToString();
        Assert.Contains("Tesla%2C_Inc.", url);      // the comma is why symbols are tickers, not titles
        Assert.Contains("/user/", url);             // agent=user: bot traffic is not attention
        Assert.Contains("/daily/", url);
    }

    [Fact]
    public async Task MonthlyTimeframe_AsksForMonthlyGranularity()
    {
        var handler = new FakeHttpMessageHandler().Get(@"per-article", TwoDays);
        var provider = NewProvider(handler);

        await provider.FetchOhlcvAsync(new MarketDataRequest("Sentiment", "BTC_VIEWS", "1M", 12));

        Assert.Contains("/monthly/", handler.Captured.Single().RequestUri!.ToString());
    }

    // ── Catalogue integrity ────────────────────────────────────────────────────

    /// <summary>
    /// Every catalogue entry must map to a distinct article. A duplicated article behind two tickers
    /// would silently draw the same series twice — the kind of thing that looks like confirmation
    /// when two "different" attention series agree.
    /// </summary>
    [Fact]
    public void Catalogue_MapsEachSymbolToADistinctArticle()
    {
        var articles = WikipediaPageviewsProvider.Catalogue.Values.Select(v => v.Article).ToList();
        Assert.Equal(articles.Count, articles.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogue_ArticlesAreNonEmptyAndCarryNoSpaces()
    {
        foreach (var (symbol, entry) in WikipediaPageviewsProvider.Catalogue)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Article), symbol);
            Assert.DoesNotContain(' ', entry.Article);
            Assert.False(string.IsNullOrWhiteSpace(entry.Display), symbol);
        }
    }

    [Fact]
    public async Task EverySubTypeReturnsSymbols_AndTheUnionIsTheWholeCatalogue()
    {
        var provider = new WikipediaPageviewsProvider();
        var subTypes = await provider.GetSupportedSubTypesAsync(MarketType.Sentiment);

        var union = new List<string>();
        foreach (var st in subTypes)
        {
            var syms = await provider.GetAvailableSymbolsAsync(MarketType.Sentiment, st);
            Assert.NotEmpty(syms);
            union.AddRange(syms);
        }

        Assert.Equal(WikipediaPageviewsProvider.Catalogue.Count, union.Distinct().Count());
    }

    [Fact]
    public void UnknownSymbol_FallsThroughAsARawArticleTitle()
    {
        // Deliberate: a hand-edited workspace can point at any article without a code change.
        Assert.Equal("Nikola_Tesla", WikipediaPageviewsProvider.ResolveArticle("Nikola Tesla"));
        Assert.Equal("Bitcoin", WikipediaPageviewsProvider.ResolveArticle("BTC_VIEWS"));
    }

    [Fact]
    public void DeclaredTimeframes_ExcludeHourly_WhichTheEndpointRejects()
    {
        // Per-article hourly is HTTP 400 upstream (verified 2026-08-02). Declaring it would give the
        // user a selectable timeframe that renders an empty chart.
        var tfs = new WikipediaPageviewsProvider().NativelySupportedTimeframes;
        Assert.DoesNotContain("1h", tfs);
        Assert.Contains("1d", tfs);
    }
}
