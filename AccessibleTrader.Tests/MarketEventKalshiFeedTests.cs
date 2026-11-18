using System.Net;
using System.Text;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Fakes;
using AccessibleTrader.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Kalshi expected-figure feed, end to end against the real saved responses. Every request
/// is served from the fixture manifest; any request not in it is a 404.
/// </summary>
public class MarketEventKalshiFeedTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 6, 0, 0, DateTimeKind.Utc);

    private static MarketEvent Release(
        string source, DateOnly date, params MarketEventFigure[] figures)
        => MarketEventSeed.CreateRelease(
            source, date, new MarketEventFigures(figures), "2026-11-18");

    private static MarketEventFigure Percent(
        string name, double? actual, double? previous,
        MarketEventFigureDirection direction = MarketEventFigureDirection.HigherIsHotter)
        => new(name, actual, previous, MarketEventFigureUnit.Percent, direction);

    private static MarketEventFigure Thousands(
        string name, double? actual, double? previous,
        MarketEventFigureDirection direction = MarketEventFigureDirection.HigherIsStronger)
        => new(name, actual, previous, MarketEventFigureUnit.Thousands, direction);

    private static MarketEvent Cpi(DateOnly date, double? actual, double? previous = null)
        => Release("cpi", date,
            Percent("CPI", actual, previous),
            Percent("core CPI", actual, previous));

    private static MarketEvent Jobs(DateOnly date, double payrolls, double unemployment)
        => Release("nfp", date,
            Thousands("payrolls", payrolls, null),
            Percent("unemployment", unemployment, null, MarketEventFigureDirection.HigherIsWeaker));

    private static MarketEvent Gdp(DateOnly date, double actual)
        => Release("gdp", date,
            Percent("GDP", actual, null, MarketEventFigureDirection.HigherIsStronger));

    public static IEnumerable<object[]> ReferenceAnswers()
    {
        yield return new object[] { Cpi(new DateOnly(2026, 7, 14), -0.4), "cpi", new DateOnly(2026, 7, 14), "CPI", -0.2 };
        yield return new object[] { Cpi(new DateOnly(2026, 7, 14), -0.4), "cpi", new DateOnly(2026, 7, 14), "core CPI", 0.2 };
        yield return new object[] { Jobs(new DateOnly(2026, 7, 2), 57, 4.2), "nfp", new DateOnly(2026, 7, 2), "payrolls", 128.0 };
        yield return new object[] { Jobs(new DateOnly(2026, 7, 2), 57, 4.2), "nfp", new DateOnly(2026, 7, 2), "unemployment", 4.3 };
        yield return new object[] { Gdp(new DateOnly(2026, 4, 30), 2.0), "gdp", new DateOnly(2026, 4, 30), "GDP", 2.6 };
        yield return new object[] { Gdp(new DateOnly(2026, 7, 30), 1.5), "gdp", new DateOnly(2026, 7, 30), "GDP", 1.9 };
        yield return new object[] { Cpi(new DateOnly(2026, 11, 10), 0.4), "cpi", new DateOnly(2026, 11, 10), "CPI", 0.3 };
        yield return new object[] { Cpi(new DateOnly(2024, 11, 13), 0.2), "cpi", new DateOnly(2024, 11, 13), "CPI", 0.2 };
        yield return new object[] { Jobs(new DateOnly(2024, 11, 1), 12, 4.1), "nfp", new DateOnly(2024, 11, 1), "payrolls", 137.0 };
        yield return new object[] { Cpi(new DateOnly(2025, 11, 13), 0.2), "cpi", new DateOnly(2025, 11, 13), "CPI", 0.2 };
    }

    [Theory]
    [MemberData(nameof(ReferenceAnswers))]
    public async Task EveryReferenceRowComesOutOfTheSavedResponses(
        MarketEvent marketEvent, string source, DateOnly date, string figure, double expected)
    {
        var rig = new Rig(new[] { marketEvent });

        await rig.Feed.RunPassAsync();

        Assert.Equal(expected, rig.Feed.ExpectedFor(source, date, figure));
    }

    [Fact]
    public async Task AnUpcomingReleaseUsesTheMarketListQuotes()
    {
        var rig = new Rig(
            new[] { Cpi(new DateOnly(2026, 12, 10), null, 0.2) },
            new DateTime(2026, 11, 30, 6, 0, 0, DateTimeKind.Utc));

        await rig.Feed.RunPassAsync();

        Assert.Equal(-0.1, rig.Feed.ExpectedFor("cpi", new DateOnly(2026, 12, 10), "CPI"));
    }

    [Fact]
    public async Task ACacheReadBackGivesTheSameAnswerWithNoRequests()
    {
        var rig = new Rig(new[] { Cpi(new DateOnly(2026, 7, 14), -0.4) });
        await rig.Feed.RunPassAsync();
        double? before = rig.Feed.ExpectedFor("cpi", new DateOnly(2026, 7, 14), "CPI");
        Assert.Equal(-0.2, before);

        var reread = rig.BuildFromCache();
        Assert.Equal(before, reread.ExpectedFor("cpi", new DateOnly(2026, 7, 14), "CPI"));

        int requestsBefore = rig.Handler.Captured.Count;
        await reread.RunPassAsync();
        Assert.Equal(requestsBefore, rig.Handler.Captured.Count);
    }

    [Fact]
    public async Task A429IsNeverCachedAsNoMarket()
    {
        var rig = new Rig(new[] { Cpi(new DateOnly(2026, 7, 14), -0.4) });
        rig.FailKeys.Add("/markets?event_ticker=KXCPI-26JUN");

        await rig.Feed.RunPassAsync();
        Assert.Null(rig.Feed.ExpectedFor("cpi", new DateOnly(2026, 7, 14), "CPI"));

        rig.FailKeys.Clear();
        await rig.Feed.RunPassAsync();

        Assert.Equal(-0.2, rig.Feed.ExpectedFor("cpi", new DateOnly(2026, 7, 14), "CPI"));
    }

    [Fact]
    public async Task LevelOffMakesNoRequestAndLevelAllStartsAPass()
    {
        var rig = new Rig(new[] { Cpi(new DateOnly(2026, 7, 14), -0.4) });
        var off = WorkspaceState.Initial with { MarketEventsLevel = "off" };

        rig.Feed.RequestRefresh(off);
        await Task.Delay(100);
        Assert.Empty(rig.Handler.Captured);

        var all = WorkspaceState.Initial with { MarketEventsLevel = "all" };
        rig.Feed.RequestRefresh(all);
        await WaitUntilAsync(() => rig.Handler.Captured.Count > 0, TimeSpan.FromSeconds(5));
        Assert.NotEmpty(rig.Handler.Captured);
    }

    [Fact]
    public async Task TheBarMapRebuildsWhenTheKalshiVersionChanges()
    {
        var first = new Rig(new[] { Cpi(new DateOnly(2026, 7, 14), -0.4) });
        await first.Feed.RunPassAsync();

        var second = new Rig(
            new[] { Cpi(new DateOnly(2026, 7, 14), -0.4), Cpi(new DateOnly(2025, 11, 13), 0.2) },
            cachePath: first.CachePath);
        var bars = new[]
        {
            MarketEventTestData.Bar(new DateTime(2025, 11, 13, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc)),
            MarketEventTestData.Bar(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc)),
        };
        var state = MarketEventTestData.State(bars, timeframe: "1d", level: "off");
        var map = new MarketEventBarMapCache(kalshiFeed: second.Feed);

        map.For(state, Now);
        Assert.Equal(1, map.BuildCount);

        await second.Feed.RunPassAsync();
        map.For(state, Now);

        Assert.Equal(2, map.BuildCount);
    }

    [Fact]
    public async Task TheStatusSaysWhenKalshiHasNotBeenReachedAndThenHowManyReleases()
    {
        var rig = new Rig(new[] { Cpi(new DateOnly(2026, 7, 14), -0.4) });

        Assert.Contains("not reached yet", rig.Feed.Status.Describe());

        await rig.Feed.RunPassAsync();

        string status = rig.Feed.Status.Describe();
        Assert.Contains("Expected figures: Kalshi", status);
        Assert.Contains("1 release read", status);
    }

    [Theory]
    [InlineData("AccessibleTrader.WebHost/ServiceCollectionExtensions.cs")]
    [InlineData("AccessibleTrader.BlazorClient/ServiceCollectionExtensions.cs")]
    public void TheFeedIsOneSingletonOnBothHeads(string file)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), file));

        Assert.Contains("services.AddSingleton<MarketEventKalshiFeed>();", text);
        Assert.DoesNotContain("services.AddScoped<MarketEventKalshiFeed>();", text);
    }

    [Fact]
    public void TheHostedHeadGivesTheFeedTheInstancesOwnPaths()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(),
            "AccessibleTrader.WebHost/AccountsServiceExtensions.cs"));

        Assert.Contains("RemoveAll<AccessibleTrader.Core.Services.MarketEvents.MarketEventKalshiFeed>()", text);
        Assert.Contains("new AccessibleTrader.Core.Services.MarketEvents.MarketEventKalshiFeed(", text);
    }

    [Fact]
    public void TheHostedContainerResolvesTheFeed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(new DemoPolicy(isDemo: false));
        services.AddAccessibleTraderWebHostServices();
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        var feed = provider.GetRequiredService<MarketEventKalshiFeed>();

        Assert.Same(feed,
            provider.GetRequiredService<AccessibleTrader.Core.Services.MarketEvents.IMarketEventExpectedFeed>());
    }

    private sealed class Rig
    {
        public readonly DateTime Now;
        public readonly FakeHttpMessageHandler Handler = new();
        public readonly HashSet<string> FailKeys = new(StringComparer.Ordinal);
        public readonly string CachePath;
        public readonly MarketEventKalshiFeed Feed;

        public Rig(
            IReadOnlyList<MarketEvent> events,
            DateTime? now = null,
            string? cachePath = null)
        {
            Now = now ?? MarketEventKalshiFeedTests.Now;
            CachePath = cachePath ?? Path.Combine(TestTemp.NewDir("kalshi-"), "kalshi.json");
            var manifest = LoadManifest();
            Handler.Add(HttpMethod.Get, ".*", request => Respond(request, manifest));
            Feed = new MarketEventKalshiFeed(
                new HttpClient(Handler), events, () => Now, CachePath,
                NullLogger<MarketEventKalshiFeed>.Instance);
        }

        public MarketEventKalshiFeed BuildFromCache()
            => new(new HttpClient(Handler), Array.Empty<MarketEvent>(), () => Now, CachePath,
                NullLogger<MarketEventKalshiFeed>.Instance);

        private HttpResponseMessage Respond(
            HttpRequestMessage request, IReadOnlyDictionary<string, string> manifest)
        {
            string key = KeyFor(request.RequestUri!);
            if (FailKeys.Contains(key))
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (!manifest.TryGetValue(key, out string? file))
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"error":"not saved"}""", Encoding.UTF8, "application/json"),
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(Path.Combine(FixtureDir, file)),
                    Encoding.UTF8, "application/json"),
            };
        }

        private static string KeyFor(Uri uri)
        {
            const string prefix = "/trade-api/v2";
            string path = uri.AbsolutePath;
            if (path.StartsWith(prefix, StringComparison.Ordinal)) path = path[prefix.Length..];
            if (path is "/markets" or "/historical/markets")
            {
                string query = uri.Query;
                int at = query.IndexOf("event_ticker=", StringComparison.Ordinal);
                if (at >= 0)
                {
                    string value = query[(at + "event_ticker=".Length)..];
                    int amp = value.IndexOf('&');
                    if (amp >= 0) value = value[..amp];
                    path += "?event_ticker=" + Uri.UnescapeDataString(value);
                }
            }
            return path;
        }
    }

    private static string FixtureDir
        => Path.Combine(RepoRoot(), "AccessibleTrader.Tests", "Fixtures", "MarketEvents", "Kalshi");

    private static IReadOnlyDictionary<string, string> LoadManifest()
    {
        var root = JObject.Parse(File.ReadAllText(Path.Combine(FixtureDir, "manifest.json")));
        return root.Properties().ToDictionary(
            p => p.Name,
            p => (string?)p.Value["file"] ?? "",
            StringComparer.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout) return;
            await Task.Delay(20);
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
