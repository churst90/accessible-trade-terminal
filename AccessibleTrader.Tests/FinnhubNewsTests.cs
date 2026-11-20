using System.Net;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Headlines;
using AccessibleTrader.Core.Services.MarketEvents;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Services;
using AccessibleTrader.Tests.Mocks;
using AccessibleTrader.WebHost;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests;

/// <summary>
/// Headlines, stage 3 (Finnhub's news). The two fixtures were captured from Finnhub on
/// 2026-11-20 with Cody's free key (four stories from AAPL's company news, four from the crypto
/// list); the summaries and images are left out. The headline cases are real stories from that
/// day. The poller runs on a fake client and a fake clock: no network, no speech, no sound.
/// </summary>
public class FinnhubNewsTests
{
    private static string Fixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly DateTime Now = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);

    private static NewsStory Story(long id, string headline, DateTime createdUtc, string wire, params string[] symbols)
        => new(id, headline, "", "Yahoo", createdUtc, createdUtc, symbols, "https://x", wire);

    private static readonly NewsTarget Aapl = new("AAPL", false);
    private static readonly NewsTarget Btc = new("BTC", true);

    // ── Parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void ACompanyNewsListIsRead()
    {
        var stories = FinnhubNewsParser.Parse(Fixture("finnhub-company-news-aapl.json"));
        Assert.Equal(4, stories.Count);
        var s = stories[0];
        Assert.Equal(143990865, s.Id);
        Assert.Equal("Apple “Has No Idea What It’s Doing” in AI. Oh, and It’s Building Its Own AI Agent Too", s.Headline);
        Assert.Equal("Yahoo", s.Source);
        Assert.Equal(new[] { "AAPL" }, s.Symbols);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1795200540).UtcDateTime, s.CreatedUtc);
        Assert.Equal(NewsStory.FinnhubWire, s.Wire);
        Assert.Equal("finnhub|143990865", s.Key);
    }

    [Fact]
    public void TheCryptoListIsReadWithNoSymbols()
    {
        var stories = FinnhubNewsParser.Parse(Fixture("finnhub-news-crypto.json"));
        Assert.Equal(4, stories.Count);
        Assert.All(stories, s => Assert.Empty(s.Symbols));
        Assert.Contains(stories, s => s.Source == "CoinDesk");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"error\":\"You don't have access to this resource.\"}")]
    [InlineData("[{\"id\":\"x\",\"datetime\":1,\"headline\":\"h\"}]")]
    [InlineData("[{\"id\":1,\"datetime\":0,\"headline\":\"h\"}]")]
    [InlineData("[{\"id\":1,\"datetime\":1795200540,\"headline\":\"  \"}]")]
    public void ABrokenListYieldsNothing(string json) => Assert.Empty(FinnhubNewsParser.Parse(json));

    // ── About the symbol ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Apple Plans an AI Agent to Rival Meta's Muse", "AAPL", "Apple Inc.", true)]
    [InlineData("Underweighting Apple (AAPL) Detracts from the Fund’s Relative Quarterly Returns", "AAPL", null, true)]
    [InlineData("Is Dell Stock's Steep Fall A Bargain Or A Warning?", "AAPL", "Apple Inc.", false)]
    [InlineData("Anthropic shares slide towards IPO price as AI investors reassess valuation", "AAPL", "Apple Inc.", false)]
    [InlineData("Intensifying AI rout wipes out Nasdaq's 2026 gains. Alphabet is a tech bright spot", "GOOGL", "Alphabet Inc.", true)]
    [InlineData("Google's new model tops the charts", "GOOGL", "Alphabet Inc.", true)]
    [InlineData("Bank of America raises its dividend", "BAC", "BANK OF AMERICA CORP /DE/", true)]
    [InlineData("America's banks race to patch vulnerabilities", "BAC", "BANK OF AMERICA CORP /DE/", false)]
    [InlineData("Advanced Micro shares jump on a new chip", "AMD", "ADVANCED MICRO DEVICES INC", true)]
    [InlineData("Walt Disney Looks Fully Priced", "DIS", "WALT DISNEY CO", true)]
    [InlineData("Low prices lift the retailer", "LOW", "LOWE'S COMPANIES INC", false)]
    public void AStockStoryIsKeptOnlyWhenItsHeadlineNamesTheCompany(string headline, string ticker, string? name, bool expected)
        => Assert.Equal(expected, NewsStoryWords.NamesCompany(headline, ticker, name));

    [Theory]
    [InlineData("Bitcoin plunges below $59,000 as rush to cash puts new bear market lows back in play", "BTC", true)]
    [InlineData("Wrapped Monero token crashes 99.8% amid suspected $13.5M exploit", "XMR", true)]
    [InlineData("Wrapped Monero token crashes 99.8% amid suspected $13.5M exploit", "BTC", false)]
    [InlineData("DBS bets it can lose the deposit and keep the client", "BTC", false)]
    [InlineData("XRP holders brace for the ruling", "XRP", true)]
    [InlineData("Ether ETFs see record outflows", "ETH", true)]
    [InlineData("Stellar results from the miners", "XLM", true)]
    [InlineData("stellar results from the miners", "XLM", false)]
    [InlineData("Sol de Janeiro tops beauty sales", "SOL", false)]
    public void ACryptoStoryIsKeptOnlyWhenItsHeadlineNamesTheCoin(string headline, string coin, bool expected)
        => Assert.Equal(expected, NewsStoryWords.NamesCoin(headline, coin));

    [Theory]
    // Real Finnhub stories from 2026-11-20, each a misfire under the stage-2 words.
    [InlineData("Walmart (WMT) Q3 2027 Earnings Call Transcript", MarketEventImportance.Low)]
    [InlineData("Stock Market Today: Dow Futures Fall As Nvidia Slides", MarketEventImportance.Low)]
    [InlineData("Meta Platforms (META) Short Interest Rises", MarketEventImportance.Low)]
    [InlineData("Disney Looks Fully Priced", MarketEventImportance.Low)]
    [InlineData("Could Tesla Merge With SpaceX? Musk Talks Up A Deal", MarketEventImportance.Low)]
    [InlineData("Apple Has No Idea What It's Doing In AI, Analyst Says", MarketEventImportance.Low)]
    [InlineData("A SpaceX-Tesla Merger Needs \"Yes\" From 50% of All Tesla Shares, Not Just Votes Cast. Here's Why That Math Is Harder Than Wall Street Thinks.", MarketEventImportance.Low)]
    [InlineData("What to consider with AMD after reading short seller report", MarketEventImportance.Low)]
    [InlineData("SEC Subpoenas Super Micro Over Accounting", MarketEventImportance.High)]
    [InlineData("FTC Opens Probe Into Microsoft's Cloud Business", MarketEventImportance.Medium)]
    [InlineData("Microsoft 365 Outage Hits Thousands Of Users", MarketEventImportance.Medium)]
    [InlineData("Intel And Microsoft Agree Long-Term Foundry Deal", MarketEventImportance.Medium)]
    public void FinnhubStoriesAreRankedByTheirWords(string headline, MarketEventImportance expected)
        => Assert.Equal(expected, NewsStoryWords.Importance(Story(1, headline, Now, NewsStory.FinnhubWire, "AAPL")));

    [Fact]
    public void ASeekingAlphaPieceIsLowWhateverItSays()
    {
        var story = new NewsStory(1, "Apple: Rating Upgrade As Buyback Accelerates", "", "SeekingAlpha", Now, Now,
            new[] { "AAPL" }, "https://x", NewsStory.FinnhubWire);
        Assert.Equal(MarketEventImportance.Low, NewsStoryWords.Importance(story));
    }

    // ── The shared gate ──────────────────────────────────────────────────────

    private static readonly string[] OnlyAapl = { "AAPL" };

    [Fact]
    public void TheSameStoryFromTheOtherWireIsNotSaidAgain()
    {
        var gate = new NewsHeadlineGate();
        Assert.True(gate.Offer(Story(5, "NYC lawmakers weigh compromise on Apple delivery worker bill, Bloomberg reports", Now,
            NewsStory.AlpacaWire, "AAPL"), new[] { Aapl }, MarketEventImportance.Medium, Now));
        Assert.False(gate.Offer(Story(9, "NYC Lawmakers Weigh Overhaul of Apple Delivery Worker Bill", Now,
            NewsStory.FinnhubWire, "AAPL"), new[] { Aapl }, MarketEventImportance.Medium, Now.AddSeconds(1)));
        Assert.Equal("AAPL: NYC lawmakers weigh compromise on Apple delivery worker bill, Bloomberg reports.",
            Assert.Single(gate.Due(Now.AddSeconds(5), OnlyAapl)).Text);
    }

    [Fact]
    public void TheSameIdOnTheTwoWiresIsTwoStories()
    {
        var gate = new NewsHeadlineGate();
        Assert.True(gate.Offer(Story(7, "Apple Halted", Now, NewsStory.AlpacaWire, "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now));
        Assert.True(gate.Offer(Story(7, "Microsoft Opens A Store In Lagos", Now, NewsStory.FinnhubWire, "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now));
    }

    [Fact]
    public void AStoryMarkedSeenIsNeverSaid()
    {
        var gate = new NewsHeadlineGate();
        var story = Story(3, "Apple Halted", Now, NewsStory.FinnhubWire, "AAPL");
        gate.MarkSeen(story);
        Assert.False(gate.Offer(story, new[] { Aapl }, MarketEventImportance.High, Now));
        Assert.Empty(gate.Due(Now.AddSeconds(5), OnlyAapl));
    }

    // ── The HTTP client ──────────────────────────────────────────────────────

    private sealed class Keys : IApiKeyCheckout
    {
        public bool HasKey = true;
        public int Checkouts;
        public Task<ApiKeyCheckoutResult> CheckoutAsync(string providerId, string marketType = "Spot", CancellationToken ct = default)
        {
            Checkouts++;
            Assert.Equal("Finnhub", providerId);
            return Task.FromResult(HasKey ? new ApiKeyCheckoutResult("finnhub-test-key", "", "", true) : ApiKeyCheckoutResult.None);
        }
    }

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task TheCompanyReadSendsTheKeyInAHeaderNeverTheUrl()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("finnhub-company-news-aapl.json"));
        var client = new FinnhubNewsHttpClient(new HttpClient(handler), new Keys());

        var read = await client.CompanyNewsAsync("AAPL", new DateOnly(2026, 11, 19), new DateOnly(2026, 11, 20));

        Assert.Equal(NewsReadStatus.Ok, read.Status);
        Assert.Equal(4, read.Stories.Count);
        Assert.Equal("Finnhub", read.Who);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://finnhub.io/api/v1/company-news?symbol=AAPL&from=2026-11-19&to=2026-11-20",
            request.RequestUri!.ToString());
        Assert.Equal("finnhub-test-key", Assert.Single(request.Headers.GetValues("X-Finnhub-Token")));
        Assert.DoesNotContain("finnhub-test-key", request.RequestUri.ToString());
    }

    [Fact]
    public async Task TheCryptoReadAsksForTheCryptoList()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("finnhub-news-crypto.json"));
        var read = await new FinnhubNewsHttpClient(new HttpClient(handler), new Keys()).CryptoNewsAsync();
        Assert.Equal(4, read.Stories.Count);
        Assert.Equal("https://finnhub.io/api/v1/news?category=crypto", Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, NewsReadStatus.Refused)]
    [InlineData(HttpStatusCode.Forbidden, NewsReadStatus.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, NewsReadStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, NewsReadStatus.Failed)]
    public async Task AnErrorIsNamedForWhatItMeans(HttpStatusCode code, NewsReadStatus expected)
    {
        var read = await new FinnhubNewsHttpClient(new HttpClient(new Handler(code, "")), new Keys()).CryptoNewsAsync();
        Assert.Equal(expected, read.Status);
        Assert.Equal("Finnhub", read.Who);
    }

    [Fact]
    public async Task NoKeyMeansNoRequest()
    {
        var handler = new Handler(HttpStatusCode.OK, "[]");
        var read = await new FinnhubNewsHttpClient(new HttpClient(handler), new Keys { HasKey = false }).CryptoNewsAsync();
        Assert.Equal(NewsReadStatus.NoKey, read.Status);
        Assert.Empty(handler.Requests);
    }

    // ── The dossier ──────────────────────────────────────────────────────────

    private sealed class FakeClient : IFinnhubNewsClient
    {
        public NewsReadResult Company = new(NewsReadStatus.Ok, Array.Empty<NewsStory>(), "Finnhub");
        public NewsReadResult Crypto = new(NewsReadStatus.Ok, Array.Empty<NewsStory>(), "Finnhub");
        public readonly List<string> Asked = new();
        public Task<NewsReadResult> CompanyNewsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            Asked.Add($"{symbol} {from:yyyy-MM-dd} {to:yyyy-MM-dd}");
            return Task.FromResult(Company);
        }
        public Task<NewsReadResult> CryptoNewsAsync(CancellationToken ct = default)
        {
            Asked.Add("crypto");
            return Task.FromResult(Crypto);
        }
    }

    private sealed class Names : ICompanyNameSource
    {
        public Task<string?> CompanyNameAsync(string ticker, CancellationToken ct = default)
            => Task.FromResult<string?>(ticker == "AAPL" ? "Apple Inc." : null);
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    [Fact]
    public async Task TheDossierListsAWeekOfTheStocksOwnStoriesNewestFirst()
    {
        var client = new FakeClient
        {
            Company = new NewsReadResult(NewsReadStatus.Ok,
                FinnhubNewsParser.Parse(Fixture("finnhub-company-news-aapl.json")), "Finnhub"),
        };
        var source = new FinnhubNewsRestSource(client, new Names(), new FixedClock(Now));

        var read = await source.RecentAsync(Aapl, 10);

        Assert.Equal("AAPL 2026-11-13 2026-11-20", Assert.Single(client.Asked));
        // Dell's story is in Finnhub's AAPL list but never names Apple.
        Assert.Equal(new long[] { 143990865, 143990867, 143989638 }, read.Stories.Select(s => s.Id));
    }

    [Fact]
    public async Task TheDossierListsACoinsStoriesFromTheCryptoList()
    {
        var client = new FakeClient
        {
            Crypto = new NewsReadResult(NewsReadStatus.Ok, FinnhubNewsParser.Parse(Fixture("finnhub-news-crypto.json")), "Finnhub"),
        };
        var read = await new FinnhubNewsRestSource(client).RecentAsync(Btc, 10);
        Assert.Equal(new[] { "crypto" }, client.Asked);
        Assert.Equal(new long[] { 8785494, 8784866 }, read.Stories.Select(s => s.Id));
    }

    private sealed class FakeNews(NewsReadResult result) : INewsSource
    {
        public Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default)
            => Task.FromResult(result);
    }

    [Fact]
    public async Task BothWiresAreListedTogetherNewestFirstWithTheSameStoryOnce()
    {
        var alpaca = new FakeNews(new NewsReadResult(NewsReadStatus.Ok, new[]
        {
            Story(1, "NYC lawmakers weigh compromise on Apple delivery worker bill, Bloomberg reports", Now.AddMinutes(-5), NewsStory.AlpacaWire, "AAPL"),
            Story(2, "Apple Reports Q4 Results", Now.AddHours(-3), NewsStory.AlpacaWire, "AAPL"),
        }, "Alpaca"));
        var finnhub = new FakeNews(new NewsReadResult(NewsReadStatus.Ok, new[]
        {
            Story(9, "NYC Lawmakers Weigh Overhaul of Apple Delivery Worker Bill", Now.AddMinutes(-20), NewsStory.FinnhubWire, "AAPL"),
            Story(8, "Apple Plans an AI Agent", Now.AddHours(-1), NewsStory.FinnhubWire, "AAPL"),
        }, "Finnhub"));

        var read = await new CombinedNewsSource(alpaca, finnhub).RecentAsync(Aapl, 10);

        Assert.Equal(NewsReadStatus.Ok, read.Status);
        Assert.Equal(new[] { "alpaca|1", "finnhub|8", "alpaca|2" }, read.Stories.Select(s => s.Key));
        Assert.Equal("Alpaca and Finnhub", read.Who);

        var section = AssetDossierService.RecentNewsSection(read, Now);
        Assert.Contains("through Alpaca and Finnhub", section.StatusNote);
    }

    [Fact]
    public async Task OneWireAnsweringIsEnough()
    {
        var read = await new CombinedNewsSource(
            new FakeNews(NewsReadResult.Of(NewsReadStatus.NoKey, "Alpaca")),
            new FakeNews(new NewsReadResult(NewsReadStatus.Ok, new[] { Story(8, "Apple Plans an AI Agent", Now, NewsStory.FinnhubWire, "AAPL") }, "Finnhub")))
            .RecentAsync(Aapl, 10);
        Assert.Equal(NewsReadStatus.Ok, read.Status);
        Assert.Equal("Finnhub", read.Who);
        Assert.Single(read.Stories);
    }

    [Theory]
    [InlineData(NewsReadStatus.NoKey, NewsReadStatus.Refused, NewsReadStatus.Refused)]
    [InlineData(NewsReadStatus.Failed, NewsReadStatus.NoKey, NewsReadStatus.Failed)]
    [InlineData(NewsReadStatus.NoKey, NewsReadStatus.NoKey, NewsReadStatus.NoKey)]
    public async Task WithNoWireAnsweringTheWorstNewsIsSaid(NewsReadStatus a, NewsReadStatus b, NewsReadStatus expected)
    {
        var read = await new CombinedNewsSource(
            new FakeNews(NewsReadResult.Of(a, "Alpaca")), new FakeNews(NewsReadResult.Of(b, "Finnhub"))).RecentAsync(Aapl, 10);
        Assert.Equal(expected, read.Status);
    }

    [Fact]
    public void ARefusalNamesTheWireThatRefused()
        => Assert.Contains("Finnhub refused the key",
            AssetDossierService.RecentNewsSection(NewsReadResult.Of(NewsReadStatus.Refused, "Finnhub"), Now).StatusNote);

    // ── The poller ───────────────────────────────────────────────────────────

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeClock(DateTime now) => Set(now);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTime now) => _now = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc));
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new DummyTimer();
        private sealed class DummyTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakePresence : IUserPresence
    {
        public bool CanReachUser { get; set; } = true;
        public bool IsAppVisible { get; set; } = true;
    }

    private sealed class Held(IReadOnlyList<MarketEventHeldPosition> positions) : IHeldPositionReader
    {
        public Task<IReadOnlyList<MarketEventHeldPosition>> ReadHeldPositionsAsync() => Task.FromResult(positions);
    }

    /// <summary>A client whose lists the test fills per symbol, recording when each was read.</summary>
    private sealed class ScriptedClient : IFinnhubNewsClient
    {
        public readonly Dictionary<string, List<NewsStory>> Lists = new();
        public NewsReadStatus Status = NewsReadStatus.Ok;
        public readonly List<(string Job, DateTime At)> Reads = new();
        public Func<DateTime> Clock = () => Now;
        public int Attempts;

        public Task<NewsReadResult> CompanyNewsAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
            => Answer(symbol);
        public Task<NewsReadResult> CryptoNewsAsync(CancellationToken ct = default) => Answer("crypto");

        private Task<NewsReadResult> Answer(string job)
        {
            Attempts++;
            if (Status != NewsReadStatus.NoKey) Reads.Add((job, Clock()));
            if (Status != NewsReadStatus.Ok) return Task.FromResult(NewsReadResult.Of(Status, "Finnhub"));
            return Task.FromResult(new NewsReadResult(NewsReadStatus.Ok,
                Lists.TryGetValue(job, out var l) ? l.ToList() : new List<NewsStory>(), "Finnhub"));
        }
    }

    private sealed class Rig : IDisposable
    {
        public readonly FakeClock Clock = new(Now);
        public readonly ScriptedClient Client = new();
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Store = new();
        public readonly FakePresence Presence = new();
        public readonly NewsHeadlineGate Gate = new();
        public readonly List<HeadlineEvent> Said = new();
        public readonly FinnhubNewsLiveNotices Service;

        public Rig(string level = "all", bool wire = true, string symbol = "AAPL", string market = "Stock|Spot",
                   IReadOnlyList<MarketEventHeldPosition>? held = null)
        {
            Store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity(market, "Alpaca", symbol, "1h"),
                HeadlinesLevel = level,
                HeadlinesNewsWire = wire,
            });
            Bus.Subscribe<HeadlineEvent>(Said.Add);
            Client.Clock = () => Clock.GetUtcNow().UtcDateTime;
            Service = new FinnhubNewsLiveNotices(Store, Bus, Client, Gate, new Names(),
                held == null ? null : new Held(held), Clock, Presence);
        }

        public async Task TickAt(DateTime now)
        {
            Clock.Set(now);
            await Service.TickAsync();
        }

        /// <summary>Every second from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public async Task Run(DateTime from, DateTime to)
        {
            for (var t = from; t <= to; t = t.AddSeconds(1)) await TickAt(t);
        }

        public void Add(string job, long id, string headline, DateTime createdUtc)
        {
            if (!Client.Lists.TryGetValue(job, out var l)) Client.Lists[job] = l = new List<NewsStory>();
            l.Add(new NewsStory(id, headline, "", "Yahoo", createdUtc, createdUtc,
                job == "crypto" ? Array.Empty<string>() : new[] { job }, "https://x", NewsStory.FinnhubWire));
        }

        public void Set(Func<WorkspaceState, WorkspaceState> change) => Store.EmitState(change(Store.State));

        public void Dispose() => Service.Dispose();
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("all", false)]
    public async Task NothingIsAskedWhileTheLevelIsOffOrTheWireIsSwitchedOff(string level, bool wire)
    {
        using var rig = new Rig(level, wire);
        await rig.Run(Now, Now.AddMinutes(3));
        Assert.Empty(rig.Client.Reads);
    }

    [Fact]
    public async Task TheFirstReadIsABaselineAndOnlyANewStoryAboutTheStockIsSaid()
    {
        using var rig = new Rig();
        rig.Add("AAPL", 1, "Apple Plans an AI Agent to Rival Meta's Muse", Now.AddMinutes(-90));
        rig.Add("AAPL", 2, "Apple Halts Sales Of The iPhone In China", Now.AddMinutes(-2));   // inside the first look
        await rig.Run(Now, Now.AddSeconds(10));
        Assert.Equal("AAPL: Apple Halts Sales Of The iPhone In China.", Assert.Single(rig.Said).Text);

        rig.Add("AAPL", 3, "Is Dell Stock's Steep Fall A Bargain Or A Warning?", Now.AddSeconds(30));
        rig.Add("AAPL", 4, "Apple Recalls The Vision Pro", Now.AddSeconds(30));
        await rig.Run(Now.AddSeconds(11), Now.AddSeconds(130));

        Assert.Equal(2, rig.Said.Count);
        Assert.Equal("AAPL: Apple Recalls The Vision Pro.", rig.Said[1].Text);
        Assert.Equal("finnhub|4", rig.Said[1].Id);
    }

    [Fact]
    public async Task ACoinsStoryComesFromTheCryptoList()
    {
        using var rig = new Rig(symbol: "BTCUSDT", market: "Crypto|Spot");
        await rig.Run(Now, Now.AddSeconds(5));                                     // baseline: empty
        rig.Add("crypto", 1, "Bitcoin plunges below $59,000", Now.AddSeconds(30));
        rig.Add("crypto", 2, "Wrapped Monero token crashes 99.8%", Now.AddSeconds(30));
        await rig.Run(Now.AddSeconds(6), Now.AddSeconds(70));

        Assert.All(rig.Client.Reads, r => Assert.Equal("crypto", r.Job));
        Assert.Equal("BTC: Bitcoin plunges below $59,000.", Assert.Single(rig.Said).Text);
    }

    [Fact]
    public async Task ManyStocksShareFifteenRequestsAMinuteAndEachIsStillRead()
    {
        var held = Enumerable.Range(0, 24)
            .Select(i => new MarketEventHeldPosition($"S{(char)('A' + i)}X", 1, "Paper", null, MarketType.Stock))
            .ToList();
        using var rig = new Rig(held: held);
        await rig.Run(Now, Now.AddMinutes(10));

        var reads = rig.Client.Reads;
        for (int i = 1; i < reads.Count; i++)
            Assert.True(reads[i].At - reads[i - 1].At >= FinnhubNewsLiveNotices.CallSpacing,
                $"two reads {reads[i].At - reads[i - 1].At} apart");
        Assert.InRange(reads.Count, 145, 151);                                 // 10 minutes at one per 4 s
        // 25 stocks (AAPL and 24 held): each read every 100 s, none left out.
        foreach (var job in reads.Select(r => r.Job).Distinct())
        {
            var times = reads.Where(r => r.Job == job).Select(r => r.At).ToList();
            Assert.True(times.Count >= 5, $"{job} read {times.Count} times");
            for (int i = 1; i < times.Count; i++)
                Assert.True(times[i] - times[i - 1] <= TimeSpan.FromSeconds(104), $"{job} waited {times[i] - times[i - 1]}");
        }
        Assert.Equal(25, reads.Select(r => r.Job).Distinct().Count());
    }

    [Fact]
    public async Task OneStockIsReadOnceAMinute()
    {
        using var rig = new Rig();
        await rig.Run(Now, Now.AddMinutes(5).AddSeconds(-1));
        Assert.Equal(5, rig.Client.Reads.Count);
    }

    [Theory]
    [InlineData(NewsReadStatus.RateLimited, 1)]
    [InlineData(NewsReadStatus.Refused, 30)]
    public async Task ARefusalOrTooManyRequestsWaits(NewsReadStatus status, int minutes)
    {
        using var rig = new Rig();
        rig.Client.Status = status;
        await rig.Run(Now, Now.AddMinutes(minutes).AddSeconds(-1));
        Assert.Single(rig.Client.Reads);
        await rig.TickAt(Now.AddMinutes(minutes));
        Assert.Equal(2, rig.Client.Reads.Count);
    }

    [Fact]
    public async Task NoKeyIsCheckedAgainEachMinute()
    {
        using var rig = new Rig();
        rig.Client.Status = NewsReadStatus.NoKey;
        await rig.Run(Now, Now.AddSeconds(59));
        Assert.Equal(1, rig.Client.Attempts);                        // no key, nothing sent
        Assert.Equal(0, rig.Service.Calls);
        rig.Client.Status = NewsReadStatus.Ok;
        await rig.TickAt(Now.AddSeconds(60));
        Assert.Single(rig.Client.Reads);
    }

    [Fact]
    public async Task AStoryAlreadyHeardFromAlpacaIsNotSaidAgain()
    {
        using var rig = new Rig();
        await rig.Run(Now, Now.AddSeconds(5));                                     // baseline
        rig.Gate.Offer(new NewsStory(77, "NYC lawmakers weigh compromise on Apple delivery worker bill, Bloomberg reports", "",
            "fiscal.ai", Now.AddSeconds(10), Now.AddSeconds(10), new[] { "AAPL" }, "https://x"),
            new[] { Aapl }, MarketEventImportance.Medium, Now.AddSeconds(10));
        rig.Add("AAPL", 9, "NYC Lawmakers Weigh Overhaul of Apple Delivery Worker Bill", Now.AddSeconds(20));
        await rig.Run(Now.AddSeconds(6), Now.AddSeconds(130));

        Assert.Equal("alpaca|77", Assert.Single(rig.Said).Id);
    }

    [Fact]
    public async Task NothingIsSaidWhileNoWindowIsOpen()
    {
        using var rig = new Rig();
        await rig.Run(Now, Now.AddSeconds(5));
        rig.Presence.CanReachUser = false;
        rig.Add("AAPL", 4, "Apple Recalls The Vision Pro", Now.AddSeconds(30));
        await rig.Run(Now.AddSeconds(6), Now.AddSeconds(70));
        Assert.Empty(rig.Said);
        rig.Presence.CanReachUser = true;
        await rig.TickAt(Now.AddSeconds(71));
        Assert.Single(rig.Said);
    }

    [Fact]
    public async Task ADroppedStockStartsAgainWithAFreshBaseline()
    {
        using var rig = new Rig();
        await rig.Run(Now, Now.AddSeconds(5));
        rig.Set(s => s with { Identity = new ChartIdentity("Stock|Spot", "Alpaca", "MSFT", "1h") });
        rig.Add("AAPL", 4, "Apple Recalls The Vision Pro", Now.AddSeconds(10));
        await rig.Run(Now.AddSeconds(6), Now.AddMinutes(10));
        rig.Set(s => s with { Identity = new ChartIdentity("Stock|Spot", "Alpaca", "AAPL", "1h") });
        await rig.Run(Now.AddMinutes(10).AddSeconds(1), Now.AddMinutes(12));
        Assert.Empty(rig.Said);                                       // ten minutes old when AAPL came back
    }

    // ── The hosts ────────────────────────────────────────────────────────────

    [Fact]
    public void TheHostedContainerGivesBothWiresOneGateAndListsBoth()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(new DemoPolicy(isDemo: false));
        services.AddAccessibleTraderWebHostServices();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        using var alpaca = scope.ServiceProvider.GetRequiredService<AlpacaNewsLiveNotices>();
        using var finnhub = scope.ServiceProvider.GetRequiredService<FinnhubNewsLiveNotices>();
        Assert.Same(alpaca.Gate, finnhub.Gate);
        Assert.IsType<CombinedNewsSource>(scope.ServiceProvider.GetRequiredService<INewsSource>());
    }

    [Fact]
    public void TheDesktopRegistersTheSameNewsServices()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "AccessibleTrader.BlazorClient", "ServiceCollectionExtensions.cs"));
        Assert.Contains("services.AddSingleton<AccessibleTrader.Core.Services.Headlines.NewsHeadlineGate>();", text);
        Assert.Contains("services.AddSingleton<AccessibleTrader.Core.Services.Headlines.FinnhubNewsLiveNotices>();", text);
        Assert.Contains("new AccessibleTrader.Core.Services.Headlines.CombinedNewsSource(", text);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx"))
                           && !Directory.Exists(Path.Combine(dir.FullName, "AccessibleTrader.BlazorClient")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
