using System.Net;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Stage 2 part C: the optional live FRED feed. Every request here is a fake; there is no
/// network, and the key must never appear in the cache or the status text.
/// </summary>
public class MarketEventFredFeedTests
{
    private const string Secret = "top-secret-&+key";

    private static readonly DateTime Start = new(2026, 11, 18, 6, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<MarketEvent> SeedEvents()
    {
        var cpi = MarketEventSeed.CreateRelease("cpi", new DateOnly(2026, 7, 14),
            new MarketEventFigures(new[]
            {
                new MarketEventFigure("CPI", 9.9, 9.9,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
                new MarketEventFigure("core CPI", 9.9, 9.9,
                    MarketEventFigureUnit.Percent, MarketEventFigureDirection.HigherIsHotter),
            }), "2026-08-01");
        return new[]
        {
            cpi,
            MarketEventSeed.CreateRelease("nfp", new DateOnly(2026, 7, 2), null, "2026-08-01"),
            MarketEventSeed.CreateRelease("ppi", new DateOnly(2026, 7, 15), null, "2026-08-01"),
            MarketEventSeed.CreateRelease("gdp", new DateOnly(2026, 7, 30), null, "2026-08-01"),
        };
    }

    private static IApiKeyService Keys(string? key)
    {
        var service = Substitute.For<IApiKeyService>();
        service.GetKeyForProviderAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(_ => Task.FromResult(key == null
                ? null
                : new ApiKeyConfig("FRED", "test", key, "")));
        return service;
    }

    private sealed class Rig
    {
        public DateTime Now = Start;
        public bool Fail;
        public bool NetworkFail;
        public readonly FakeHttpMessageHandler Handler = new();
        public readonly string CachePath =
            Path.Combine(TestTemp.NewDir("market-events-"), "fred.json");
        public readonly MarketEventFredFeed Feed;
        public readonly IApiKeyService Keys;

        public Rig(string? key = Secret)
        {
            void Rule(string regex, string body) =>
                Handler.Add(HttpMethod.Get, regex, _ => NetworkFail
                    ? throw new HttpRequestException("connection refused")
                    : Respond(body));

            Rule("release_id=10", """{"release_dates":[{"release_id":10,"date":"2026-07-14"},{"release_id":10,"date":"2026-09-11"}]}""");
            Rule("release_id=50", """{"release_dates":[]}""");
            Rule("release_id=46", """{"release_dates":[]}""");
            Rule("release_id=53", """{"release_dates":[]}""");
            Rule("series_id=CPIAUCSL", CpiObservations);
            Rule("series_id=CPILFESL", """{"observations":[]}""");
            Rule("series_id=PAYEMS", """{"observations":[]}""");
            Rule("series_id=UNRATE", """{"observations":[]}""");
            Rule("series_id=GDPC1", """{"observations":[]}""");
            Rule("series_id=PPIFIS", """{"observations":[]}""");
            Keys = MarketEventFredFeedTests.Keys(key);
            Feed = new MarketEventFredFeed(
                Keys, new TempWorkspacePaths(), NullLogger<MarketEventFredFeed>.Instance,
                new HttpClient(Handler), SeedEvents(), () => Now, CachePath);
        }

        private HttpResponseMessage Respond(string body)
            => Fail
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""{"error":"nope"}"""),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body),
                };

        public MarketEventFredFeed BuildFromCache()
            => new(Keys, new TempWorkspacePaths(), NullLogger<MarketEventFredFeed>.Instance,
                new HttpClient(new FakeHttpMessageHandler()),
                SeedEvents(), () => Now, CachePath);
    }

    /// <summary>The 2026-07-14 vintage makes June CPI 101 against May 100, and May 100 against April 99.</summary>
    private const string CpiObservations = """
        {"observations":[
          {"realtime_start":"2026-04-10","realtime_end":"9999-12-31","date":"2026-03-01","value":"98"},
          {"realtime_start":"2026-05-12","realtime_end":"9999-12-31","date":"2026-04-01","value":"99"},
          {"realtime_start":"2026-06-10","realtime_end":"9999-12-31","date":"2026-05-01","value":"100"},
          {"realtime_start":"2026-07-14","realtime_end":"9999-12-31","date":"2026-06-01","value":"101"},
          {"realtime_start":"2026-08-12","realtime_end":"9999-12-31","date":"2026-07-01","value":"103"}
        ]}
        """;

    [Fact]
    public async Task NoKeyMakesNoRequest()
    {
        var rig = new Rig(key: null);

        var status = await rig.Feed.UpdateNowAsync();

        Assert.Empty(rig.Handler.Captured);
        Assert.Equal(MarketEventFeedState.NoKey, status.State);
        Assert.Contains("Add a FRED key in API Keys", status.Describe());
        Assert.Contains("30 July 2026", status.Describe());
    }

    [Fact]
    public async Task AFetchOverridesTheSeedDateAndFigure()
    {
        var rig = new Rig();

        var status = await rig.Feed.UpdateNowAsync();

        Assert.Equal(MarketEventFeedState.Fetched, status.State);
        Assert.True(rig.Feed.Version > 0);
        var cpi = rig.Feed.Events.Single(e => e.Id == "cpi-2026-07-14");
        var figure = cpi.Figures!.Figures.Single(f => f.Name == "CPI");
        Assert.Equal(1.0, figure.Actual);
        Assert.Equal(1.0, figure.Previous);
        Assert.Null(cpi.Figures.Figures.Single(f => f.Name == "core CPI").Actual);
        Assert.Contains(rig.Feed.Events, e => e.Id == "cpi-2026-09-11");
    }

    [Fact]
    public async Task TheNextEventReadSeesTheFetchedDateAndFigureOverTheSeed()
    {
        var rig = new Rig();
        await rig.Feed.UpdateNowAsync();
        var calendar = new MarketEventCalendar(SeedEvents(), rig.Feed);

        var cpi = calendar.Between(
                new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                MarketType.Crypto)
            .Single(e => e.Id == "cpi-2026-07-14");

        Assert.Equal(1.0, cpi.Figures!.Figures.Single(f => f.Name == "CPI").Actual);
    }

    [Fact]
    public async Task AFailedFetchKeepsTheOldData()
    {
        var rig = new Rig();
        await rig.Feed.UpdateNowAsync();
        int before = rig.Feed.Events.Count;
        rig.Fail = true;

        var status = await rig.Feed.UpdateNowAsync();

        Assert.Equal(MarketEventFeedState.Failed, status.State);
        Assert.Equal(before, rig.Feed.Events.Count);
        Assert.Contains("HTTP 500", status.Describe());
        Assert.Contains("Using what was stored before", status.Describe());
    }

    [Fact]
    public async Task ANoAnswerFailureSaysNoAnswerAndKeepsTheSeed()
    {
        var rig = new Rig();
        rig.NetworkFail = true;

        var status = await rig.Feed.UpdateNowAsync();

        Assert.Equal(MarketEventFeedState.Failed, status.State);
        Assert.Contains("no answer", status.Describe());
        Assert.Empty(rig.Feed.Events);
    }

    [Fact]
    public async Task TheStatusTextNamesTheFetchTimeAndTheFailureTime()
    {
        var fetched = new DateTime(2026, 11, 18, 6, 10, 0, DateTimeKind.Utc);
        var fetchedLocal = fetched.ToLocalTime()
            .ToString("d MMMM yyyy 'at' HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var failed = fetched.AddHours(1);
        var failedLocal = failed.ToLocalTime()
            .ToString("d MMMM yyyy 'at' HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(
            $"Release dates and figures updated from FRED on {fetchedLocal}.",
            new MarketEventFeedStatus(MarketEventFeedState.Fetched, fetched, null, null,
                new DateOnly(2026, 7, 30)).Describe());
        Assert.Equal(
            $"The last update from FRED failed on {failedLocal}: no answer. Using what was stored before.",
            new MarketEventFeedStatus(MarketEventFeedState.Failed, fetched, failed, "no answer",
                new DateOnly(2026, 7, 30)).Describe());
    }

    [Fact]
    public async Task TheTwelveHourGateHoldsAndForceSkipsIt()
    {
        var rig = new Rig();
        await rig.Feed.RefreshIfDueAsync();
        int afterFirst = rig.Handler.Captured.Count;
        Assert.True(afterFirst > 0);

        rig.Now = Start.AddHours(1);
        await rig.Feed.RefreshIfDueAsync();
        Assert.Equal(afterFirst, rig.Handler.Captured.Count);

        await rig.Feed.UpdateNowAsync();
        Assert.True(rig.Handler.Captured.Count > afterFirst);

        int afterForce = rig.Handler.Captured.Count;
        rig.Now = Start.AddHours(13);
        await rig.Feed.RefreshIfDueAsync();
        Assert.True(rig.Handler.Captured.Count > afterForce);
    }

    [Fact]
    public async Task TheKeyIsNotInTheCacheFileOrTheStatusText()
    {
        var rig = new Rig();
        var status = await rig.Feed.UpdateNowAsync();

        Assert.True(File.Exists(rig.CachePath));
        string cache = File.ReadAllText(rig.CachePath);
        Assert.DoesNotContain(Secret, cache);
        Assert.DoesNotContain(Uri.EscapeDataString(Secret), cache);
        Assert.DoesNotContain(Secret, status.Describe());
        Assert.DoesNotContain(Secret, status.ToString());
    }

    [Fact]
    public async Task ARestartWithNoNetworkStillHasTheLastFetch()
    {
        var rig = new Rig();
        await rig.Feed.UpdateNowAsync();
        int fetched = rig.Feed.Events.Count;

        var restarted = rig.BuildFromCache();
        var status = await restarted.GetStatusAsync();

        Assert.Equal(fetched, restarted.Events.Count);
        Assert.Equal(MarketEventFeedState.Fetched, status.State);
        Assert.True(restarted.Version > 0);
    }

    [Fact]
    public async Task ACorruptCacheIsIgnoredAndLoggedNeverThrown()
    {
        string path = Path.Combine(TestTemp.NewDir("market-events-"), "fred.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        var feed = new MarketEventFredFeed(
            Keys(Secret), new TempWorkspacePaths(), NullLogger<MarketEventFredFeed>.Instance,
            new HttpClient(new FakeHttpMessageHandler()),
            SeedEvents(), () => Start, path);

        var status = await feed.GetStatusAsync();

        Assert.Empty(feed.Events);
        Assert.Equal(MarketEventFeedState.NotFetched, status.State);
    }

    [Fact]
    public async Task TheBarMapStampIncludesTheFeedVersion()
    {
        var rig = new Rig();
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = MarketEventTestData.State(bars, timeframe: "1d", level: "off");
        var map = new MarketEventBarMapCache(feed: rig.Feed);

        map.For(state, Start);
        Assert.Equal(1, map.BuildCount);

        await rig.Feed.UpdateNowAsync();
        map.For(state, Start);

        Assert.Equal(2, map.BuildCount);
    }

    [Theory]
    [InlineData("AccessibleTrader.WebHost/ServiceCollectionExtensions.cs")]
    [InlineData("AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs")]
    public void TheFeedIsOneSingletonOnBothHeads(string file)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), file));

        Assert.Contains("services.AddSingleton<MarketEventFredFeed>();", text);
        Assert.DoesNotContain("services.AddScoped<MarketEventFredFeed>();", text);
    }

    /// <summary>
    /// A singleton may not take a scoped service. The WebHost registers settings, the event bus
    /// and most of the accessibility graph as Scoped, so an eager future dependency on one of
    /// those would capture the first circuit's instance and hand it to every other user.
    /// </summary>
    [Fact]
    public void TheFeedTakesNoScopedService()
    {
        var constructor = typeof(MarketEventFredFeed).GetConstructors().Single(c => c.IsPublic);
        var names = constructor.GetParameters().Select(p => p.ParameterType.Name).ToList();
        string web = File.ReadAllText(Path.Combine(RepoRoot(),
            "AccessibleTrader.WebHost/ServiceCollectionExtensions.cs"));

        foreach (string name in names)
        {
            Assert.DoesNotContain($"AddScoped<{name}", web);
            Assert.DoesNotContain($"AddScoped<I{name}", web);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

}
