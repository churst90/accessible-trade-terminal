using System.Collections.Immutable;
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
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AccessibleTrader.Tests;

/// <summary>
/// Headlines, stage 2 (the Alpaca news wire). The two fixtures were captured from Alpaca on
/// 2026-11-20 with Cody's key (a stream frame from the 20:01 UTC batch, and the newest two AAPL
/// stories from the REST endpoint); only the article text in "content" is cut short. Every story
/// on that account comes from fiscal.ai, not Benzinga. The importance cases are real headlines
/// from that day. The stream runs on a fake socket and a fake clock: no network, no speech, no
/// sound.
/// </summary>
public class AlpacaNewsTests
{
    private static string Fixture(string name) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static readonly DateTime Now = new(2026, 11, 2, 21, 31, 0, DateTimeKind.Utc);

    private static NewsStory Story(long id, string headline, DateTime createdUtc, params string[] symbols)
        => new(id, headline, "", "fiscal.ai", createdUtc, createdUtc, symbols, "https://x");

    private static string Frame(long id, string headline, DateTime createdUtc, params string[] symbols)
        => JsonConvert.SerializeObject(new[]
        {
            new
            {
                T = "n", id, headline, summary = "", author = "",
                created_at = createdUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                updated_at = createdUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                url = "https://x", content = "", symbols, source = "fiscal.ai",
            },
        });

    private static readonly NewsTarget Aapl = new("AAPL", false);
    private static readonly NewsTarget Btc = new("BTC", true);

    // ── Parsing ──────────────────────────────────────────────────────────────

    [Fact]
    public void AStreamStoryFrameIsRead()
    {
        var (stories, controls) = AlpacaNewsParser.ParseFrame(Fixture("alpaca-news-stream.json"));

        Assert.Empty(controls);
        var s = Assert.Single(stories);
        Assert.Equal(127778840, s.Id);
        Assert.Equal("S&P Global Ratings Maintains Credit Rating for Boston Mayflower Finance PLC", s.Headline);
        Assert.Equal(new DateTime(2026, 11, 20, 20, 1, 49, DateTimeKind.Utc), s.CreatedUtc);
        Assert.Equal(DateTimeKind.Utc, s.CreatedUtc.Kind);
        Assert.Equal(new[] { "SPGI" }, s.Symbols);
        Assert.Equal("fiscal.ai", s.Source);
        Assert.Equal("", s.Author);
    }

    [Fact]
    public void ControlMessagesAreReadWithTheirCodes()
    {
        var (stories, controls) = AlpacaNewsParser.ParseFrame(
            "[{\"T\":\"success\",\"msg\":\"authenticated\"},{\"T\":\"subscription\",\"news\":[\"*\"]},"
            + "{\"T\":\"error\",\"code\":402,\"msg\":\"auth failed\"}]");

        Assert.Empty(stories);
        Assert.Equal(new[] { "success", "subscription", "error" }, controls.Select(c => c.Type));
        Assert.Equal("authenticated", controls[0].Message);
        Assert.Equal(402, controls[2].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[{\"T\":\"n\",\"headline\":\"no id\",\"created_at\":\"2026-11-02T21:00:00Z\"}]")]
    [InlineData("[{\"T\":\"n\",\"id\":1,\"headline\":\"   \",\"created_at\":\"2026-11-02T21:00:00Z\"}]")]
    public void ABrokenFrameYieldsNothing(string json)
    {
        var (stories, _) = AlpacaNewsParser.ParseFrame(json);
        Assert.Empty(stories);
    }

    [Fact]
    public void TheRestListIsRead()
    {
        var stories = AlpacaNewsParser.ParseRest(Fixture("alpaca-news-rest.json"));

        Assert.Equal(new long[] { 127411699, 127398827 }, stories.Select(s => s.Id));
        Assert.Equal("Apple Developing Autonomous AI Agent", stories[1].Headline);
        Assert.Equal(new DateTime(2026, 11, 20, 12, 31, 54, DateTimeKind.Utc), stories[1].CreatedUtc);
        Assert.Equal(new[] { "AAPL" }, stories[1].Symbols);
        Assert.Equal("fiscal.ai", stories[1].Source);
    }

    [Fact]
    public void HtmlEntitiesAndRunsOfSpacesAreTakenOutOfAHeadline()
    {
        var stories = AlpacaNewsParser.ParseRest("{\"news\":[{\"id\":1,\"headline\":\"Apple &amp; Google  Face New EU Probe\","
            + "\"created_at\":\"2026-11-20T12:31:54Z\",\"symbols\":[\"AAPL\",\"GOOGL\"]}]}");

        Assert.Equal("Apple & Google Face New EU Probe", Assert.Single(stories).Headline);
    }

    // ── Importance (Claude's rule; Cody to correct) ────────────────────────

    [Theory]
    [InlineData("Apple Reports Q4 Results, Beats Estimates", MarketEventImportance.High)]
    [InlineData("Apple Q4 EPS $1.64 Beats $1.60 Estimate", MarketEventImportance.High)]
    [InlineData("Apple Raises FY25 Revenue Guidance", MarketEventImportance.High)]
    [InlineData("Microsoft To Acquire Activision Blizzard For $68.7B", MarketEventImportance.High)]
    [InlineData("FDA Approves Pfizer's RSV Vaccine", MarketEventImportance.High)]
    [InlineData("SEC Approves Spot Bitcoin ETFs", MarketEventImportance.High)]
    [InlineData("Tesla Shares Halted, News Pending", MarketEventImportance.High)]
    [InlineData("Exchange Says Hackers Drained $20M From Hot Wallet", MarketEventImportance.High)]
    [InlineData("Boeing CEO To Step Down At Year End", MarketEventImportance.High)]
    [InlineData("Morgan Stanley Upgrades Apple To Overweight", MarketEventImportance.Medium)]
    [InlineData("Apple Declares $0.25 Quarterly Dividend", MarketEventImportance.Low)]
    [InlineData("Apple & Google Face New EU Probe", MarketEventImportance.Medium)]
    [InlineData("Why Apple Shares Are Trading Lower After Q4 Results", MarketEventImportance.Low)]
    [InlineData("Apple's Options Activity: What's Going On?", MarketEventImportance.Low)]
    [InlineData("Apple Earnings Preview: What to Expect", MarketEventImportance.Low)]
    [InlineData("Goldman Sachs Maintains Buy on Apple, Raises Price Target to $250", MarketEventImportance.Low)]
    [InlineData("Apple Leader in Phone Sales in China for Second Straight Month", MarketEventImportance.Low)]
    // Real fiscal.ai headlines from 2026-11-20, each the case for one part of the rule.
    [InlineData("IES Holdings Reports Fiscal 2026 Fourth Quarter and Full Year Results; Board Authorizes Additional $350 Million for Share Repurchase Program", MarketEventImportance.High)]
    [InlineData("Smith & Wesson Brands, Inc. Announces Second Quarter Fiscal 2027 Financial Results and Conference Call", MarketEventImportance.High)]
    [InlineData("Revival Gold Reports Financial Results for the Quarter Ended September 30, 2026", MarketEventImportance.High)]
    [InlineData("Valkea Resources Reports Results for Three Months Ended September 2026 and Provides Exploration Update", MarketEventImportance.High)]
    [InlineData("Rambus Issues Mixed Guidance with Lowered Expectations", MarketEventImportance.High)]
    [InlineData("Bending Spoons to Acquire Mailchimp from Intuit", MarketEventImportance.High)]
    [InlineData("Invesque Shareholders Approve Go-Private Transaction", MarketEventImportance.High)]
    [InlineData("Cable One Receives NYSE Continued Listing Standards Notice", MarketEventImportance.High)]
    [InlineData("N-able Confirms Security Incident Involving Unauthorized Access to Hosted Email Server", MarketEventImportance.High)]
    [InlineData("Yesway, Inc. Announces Pricing of Secondary Offering of Class A Common Stock", MarketEventImportance.High)]
    [InlineData("San Juan Basin Royalty Trust Declares No Cash Distribution for November 2026", MarketEventImportance.High)]
    [InlineData("KKR in Talks to Acquire EdgeCore", MarketEventImportance.Medium)]
    [InlineData("Hub Group Opposes Proposed Union Pacific-Norfolk Southern Merger", MarketEventImportance.Medium)]
    [InlineData("Tripadvisor Shares Rise After Czech Investment Firm Acquires 7.4% Stake", MarketEventImportance.Medium)]
    [InlineData("Jefferies Initiates Coverage on Moderna with Hold Rating", MarketEventImportance.Medium)]
    [InlineData("Karman Space & Defense Wins $31 Million US Navy Contract", MarketEventImportance.Medium)]
    [InlineData("PHINIA CFO Chris Gropp to Retire; Gordon Muir Appointed as Successor", MarketEventImportance.Medium)]
    [InlineData("Lufthansa to Cut 1,000 Jobs at Core Airline Division", MarketEventImportance.Medium)]
    [InlineData("Jack Henry & Associates Announces 2026 Dividend Increase", MarketEventImportance.Medium)]
    [InlineData("Danaher Announces Strategic Partnership with Microsoft for Intelligent Laboratory Solutions", MarketEventImportance.Medium)]
    [InlineData("California Attorney General Launches Investigation into NVIDIA", MarketEventImportance.Medium)]
    [InlineData("IC Group Announces Q3 2026 Earnings Release Date and Investor Webinar", MarketEventImportance.Low)]
    [InlineData("Rent the Runway to Report Third Quarter 2026 Financial Results on December 10, 2026", MarketEventImportance.Low)]
    [InlineData("Analysts Update Ratings and Price Targets for Saipem Following Q3 Results", MarketEventImportance.Low)]
    [InlineData("Berenberg Maintains Buy Rating on Ithaca Energy Following Q3 Results", MarketEventImportance.Low)]
    [InlineData("Mercury Insurance Issues Guidance on Flood-Damaged Vehicles", MarketEventImportance.Low)]
    [InlineData("Andersen Served as Advisor to Rock Gate Partners in Acquisition of Blast Deflectors, Inc.", MarketEventImportance.Low)]
    [InlineData("Director Transaction: Shervin J. Korangy Acquires Shares in Fresenius Medical Care AG", MarketEventImportance.Low)]
    [InlineData("HSBC Holdings plc Announces Results of Cash Tender Offer for Contingent Convertible Securities", MarketEventImportance.Low)]
    [InlineData("Halper Sadeh LLC Investigates Merger Between Werewolf Therapeutics and Ambros Therapeutics", MarketEventImportance.Low)]
    [InlineData("Levi & Korsinsky Announces Securities Investigation into Cellebrite DI Ltd.", MarketEventImportance.Low)]
    [InlineData("Levi & Korsinsky Announces Investigation into Liquidity Services", MarketEventImportance.Low)]
    // The next two are real headlines reworded (prefix dropped; words reordered) to reach the one
    // pattern each guards.
    [InlineData("Shervin J. Korangy Acquires Shares in Fresenius Medical Care AG", MarketEventImportance.Low)]
    [InlineData("Kincora Copper Announces Annual General and Special Meeting Results", MarketEventImportance.Low)]
    [InlineData("Sonoro Gold Completes Geotechnical Investigation and Initiates Advanced Engineering for Cerro Caliche", MarketEventImportance.Low)]
    [InlineData("Carrier Connect Data Solutions Inc. Upgrades to OTCQX Best Market", MarketEventImportance.Low)]
    [InlineData("UGG Partners with Bloomingdale's and Forty Carrots for Holiday 'UGG Parlor' Activation", MarketEventImportance.Low)]
    [InlineData("Kincora Copper Announces Results of Annual General and Special Meeting", MarketEventImportance.Low)]
    [InlineData("Butterfield Announces Pricing of $600 Million Subordinated Notes Offering", MarketEventImportance.Low)]
    [InlineData("M.P. Evans Group PLC Announces Share Buyback Transaction", MarketEventImportance.Low)]
    [InlineData("Moog Declares Quarterly Dividend", MarketEventImportance.Low)]
    public void WireStoriesAreRankedByTheirWords(string headline, MarketEventImportance expected)
        => Assert.Equal(expected, NewsStoryWords.Importance(Story(1, headline, Now, "AAPL")));

    [Fact]
    public void ARoundupOfManySymbolsIsLowWhateverItSays()
    {
        Assert.Equal(MarketEventImportance.High,
            NewsStoryWords.Importance(Story(1, "Apple Reports Q4 Results", Now, "AAPL", "MSFT", "NVDA", "AMZN")));
        Assert.Equal(MarketEventImportance.Low,
            NewsStoryWords.Importance(Story(1, "Apple Reports Q4 Results", Now, "AAPL", "MSFT", "NVDA", "AMZN", "META")));
    }

    // ── Matching ─────────────────────────────────────────────────────────────

    [Fact]
    public void StocksMatchByTickerAndCoinsByTheirBase()
    {
        var targets = new[] { Aapl, Btc, new NewsTarget("BRK.B", false) };

        Assert.Equal(new[] { Aapl }, NewsStoryWords.Matches(Story(1, "h", Now, "AAPL"), targets));
        Assert.Empty(NewsStoryWords.Matches(Story(1, "h", Now, "AAP"), targets));
        Assert.Equal(new[] { Btc }, NewsStoryWords.Matches(Story(1, "h", Now, "BTCUSD"), targets));
        Assert.Equal(new[] { Btc }, NewsStoryWords.Matches(Story(1, "h", Now, "XBTUSD"), targets));
        Assert.Equal("BRK.B", NewsStoryWords.Matches(Story(1, "h", Now, "BRK-B"), targets).Single().Label);
        // A STOCK called BTC is not the coin (no quote, so not a coin pair).
        Assert.Empty(NewsStoryWords.Matches(Story(1, "h", Now, "BTC"), new[] { Btc }));
        // Targets keep their own order (the focused chart first).
        Assert.Equal(new[] { Aapl, Btc }, NewsStoryWords.Matches(Story(1, "h", Now, "BTCUSD", "AAPL"), targets));
    }

    [Fact]
    public void TouchedTargetsAreStocksThenCoinsWithTheFocusedChartFirst()
    {
        static TabSnapshot Tab(string market, string symbol)
        {
            var s = WorkspaceState.Initial;
            return new TabSnapshot(1, new ChartIdentity(market, "Kraken", symbol, "1d"), s.Data, s.ActiveSeries,
                s.FocusedSeriesIndex, s.FocusedSeriesId, s.FocusedComponentIndex, s.FocusedBinIndex,
                s.CurrentDataIndex, s.ViewportStartIndex, s.ViewportLength, s.RightMarginBars, s.ViewportRange,
                s.PaneRanges, false, false, s.LastInteractionContext, s.PaneHeightRatios,
                InitializationStatus.Ready, DataStatus.Ready, false, null, 0, -1, SymbolDisplayName: symbol);
        }

        var state = WorkspaceState.Initial with
        {
            Identity = new ChartIdentity("Crypto|Spot", "Kraken", "XBT/USD", "1h"),
            TabSnapshots = ImmutableList.Create(Tab("Stock|Spot", "NVDA"), Tab("Crypto|Spot", "ETHUSDT"),
                Tab("Crypto|Spot", "BTC-USD")),
        };
        var held = new[]
        {
            new MarketEventHeldPosition("SOL/USD", 1, "Paper", null, MarketType.Crypto),
            new MarketEventHeldPosition("MSFT", 1, "Paper", null, MarketType.Stock),
        };

        var targets = AlpacaNewsLiveNotices.TouchedTargets(state, held);

        Assert.Equal(new[] { "BTC:coin", "NVDA:stock", "MSFT:stock", "ETH:coin", "SOL:coin" },
            targets.Select(t => t.Label + (t.Coin ? ":coin" : ":stock")));
        Assert.Equal("BTCUSD", targets[0].RestSymbol);
    }

    // ── The gate ─────────────────────────────────────────────────────────────

    private static readonly string[] OnlyAapl = { "AAPL" };

    [Fact]
    public void ABurstIsGatheredIntoOneSentenceLedByTheMostImportant()
    {
        var gate = new NewsHeadlineGate();
        gate.Offer(Story(1, "Apple Q4 Sales Beat", Now, "AAPL"), new[] { Aapl }, MarketEventImportance.Medium, Now);
        gate.Offer(Story(2, "Apple Reports Q4 Results, Beats Estimates", Now, "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now.AddSeconds(1));

        Assert.Empty(gate.Due(Now.AddSeconds(2), OnlyAapl));          // still gathering
        var said = Assert.Single(gate.Due(Now.AddSeconds(3), OnlyAapl));

        Assert.Equal("AAPL: Apple Reports Q4 Results, Beats Estimates. 1 more headline.", said.Text);
        Assert.Equal(MarketEventImportance.High, said.Importance);
        Assert.Equal("alpaca|2", said.Id);
        Assert.Equal("AAPL", said.Symbol);
    }

    [Fact]
    public void AnEditedStoryIsNotSaidAgain()
    {
        var gate = new NewsHeadlineGate();
        Assert.True(gate.Offer(Story(7, "Apple Halted", Now, "AAPL"), new[] { Aapl }, MarketEventImportance.High, Now));
        Assert.False(gate.Offer(Story(7, "Apple Halted, News Pending", Now, "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now.AddSeconds(1)));
        Assert.Single(gate.Due(Now.AddSeconds(5), OnlyAapl));
        Assert.False(gate.Offer(Story(7, "Apple Halted, News Pending", Now, "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now.AddMinutes(5)));
        Assert.Empty(gate.Due(Now.AddMinutes(6), OnlyAapl));
    }

    [Fact]
    public void ASymbolIsSaidAtMostOnceAMinute()
    {
        var gate = new NewsHeadlineGate();
        gate.Offer(Story(1, "First", Now, "AAPL"), new[] { Aapl }, MarketEventImportance.High, Now);
        Assert.Single(gate.Due(Now.AddSeconds(3), OnlyAapl));

        gate.Offer(Story(2, "Second", Now.AddSeconds(10), "AAPL"), new[] { Aapl }, MarketEventImportance.High, Now.AddSeconds(10));
        gate.Offer(Story(3, "Third", Now.AddSeconds(20), "AAPL"), new[] { Aapl }, MarketEventImportance.High, Now.AddSeconds(20));
        Assert.Empty(gate.Due(Now.AddSeconds(30), OnlyAapl));
        Assert.Empty(gate.Due(Now.AddSeconds(62), OnlyAapl));

        var next = Assert.Single(gate.Due(Now.AddSeconds(63), OnlyAapl));
        Assert.Equal("AAPL: Third. 1 more headline.", next.Text);
    }

    [Fact]
    public void LateStoriesAndUntouchedSymbolsAreDroppedAndAnOldOneGetsItsAge()
    {
        var gate = new NewsHeadlineGate();
        Assert.False(gate.Offer(Story(1, "Stale", Now.AddHours(-2).AddSeconds(-1), "AAPL"), new[] { Aapl },
            MarketEventImportance.High, Now));

        gate.Offer(Story(2, "Gone", Now, "MSFT"), new[] { new NewsTarget("MSFT", false) }, MarketEventImportance.High, Now);
        Assert.Empty(gate.Due(Now.AddSeconds(5), OnlyAapl));          // MSFT chart closed
        Assert.Equal(0, gate.PendingCount);

        gate.Offer(Story(3, "Apple Halted", Now.AddMinutes(-25), "AAPL"), new[] { Aapl }, MarketEventImportance.High, Now);
        Assert.Equal("AAPL: Apple Halted. Published 25 minutes ago.",
            Assert.Single(gate.Due(Now.AddSeconds(3), OnlyAapl)).Text);
    }

    [Fact]
    public void AStoryAboutTwoWatchedSymbolsNamesBoth()
        => Assert.Equal("AAPL, BTC: Apple Adds Bitcoin To Its Balance Sheet?",
            NewsHeadlineWording.Sentence(Story(1, "Apple Adds Bitcoin To Its Balance Sheet?", Now, "AAPL", "BTCUSD"),
                new[] { Aapl, Btc }));

    // ── The live service ─────────────────────────────────────────────────────

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

    private sealed class Keys : IApiKeyCheckout
    {
        public bool HasKey = true;
        public int Checkouts;
        public Task<ApiKeyCheckoutResult> CheckoutAsync(string providerId, string marketType = "Spot", CancellationToken ct = default)
        {
            Checkouts++;
            Assert.Equal("Alpaca", providerId);
            return Task.FromResult(HasKey ? new ApiKeyCheckoutResult("key-id", "key-secret", "", true) : ApiKeyCheckoutResult.None);
        }
    }

    private sealed class FakeSocket : INewsSocket
    {
        public bool IsConnected { get; set; } = true;
        public bool ConnectWorks = true;
        public bool Disposed;
        public readonly List<string> Sent = new();
        private Action<string>? _onMessage;

        public async Task<bool> ConnectAsync(Func<Task> onConnected, Action<string> onMessage)
        {
            _onMessage = onMessage;
            if (!ConnectWorks) { IsConnected = false; return false; }
            await onConnected();
            return true;
        }

        public Task SendAsync(string message) { Sent.Add(message); return Task.CompletedTask; }
        public void Deliver(string json) => _onMessage!(json);
        public ValueTask DisposeAsync() { Disposed = true; IsConnected = false; return ValueTask.CompletedTask; }
    }

    private sealed class Sockets : INewsSocketFactory
    {
        public readonly List<FakeSocket> Made = new();
        public bool NextConnectWorks = true;
        public INewsSocket Create()
        {
            var s = new FakeSocket { ConnectWorks = NextConnectWorks };
            Made.Add(s);
            return s;
        }
        public FakeSocket Last => Made[^1];
    }

    private sealed class FakePresence : IUserPresence
    {
        public bool CanReachUser { get; set; } = true;
        public bool IsAppVisible { get; set; } = true;
    }

    private sealed class Rig : IDisposable
    {
        public readonly FakeClock Clock = new(Now);
        public readonly Keys Keys = new();
        public readonly Sockets Sockets = new();
        public readonly SpyEventBus Bus = new();
        public readonly MockWorkspaceStore Store = new();
        public readonly FakePresence Presence = new();
        public readonly List<HeadlineEvent> Said = new();
        public readonly AlpacaNewsLiveNotices Service;

        public Rig(string level = "all", bool wire = true)
        {
            Store.EmitState(WorkspaceState.Initial with
            {
                Identity = new ChartIdentity("Stock|Spot", "Alpaca", "AAPL", "1h"),
                HeadlinesLevel = level,
                HeadlinesNewsWire = wire,
            });
            Bus.Subscribe<HeadlineEvent>(Said.Add);
            Service = new AlpacaNewsLiveNotices(Store, Bus, Keys, Sockets, null, Clock, Presence);
        }

        public async Task TickAt(DateTime now)
        {
            Clock.Set(now);
            await Service.TickAsync();
        }

        /// <summary>Connects and plays Alpaca's side of the handshake.</summary>
        public async Task<FakeSocket> Subscribe()
        {
            await TickAt(Now);
            var s = Sockets.Last;
            s.Deliver("[{\"T\":\"success\",\"msg\":\"connected\"}]");
            s.Deliver("[{\"T\":\"success\",\"msg\":\"authenticated\"}]");
            s.Deliver("[{\"T\":\"subscription\",\"news\":[\"*\"]}]");
            return s;
        }

        public void Set(Func<WorkspaceState, WorkspaceState> change) => Store.EmitState(change(Store.State));

        public void Dispose() => Service.Dispose();
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("all", false)]
    public async Task NoSocketIsOpenedWhileTheLevelIsOffOrTheWireIsSwitchedOff(string level, bool wire)
    {
        using var rig = new Rig(level, wire);
        await rig.TickAt(Now);
        await rig.TickAt(Now.AddMinutes(30));
        Assert.Empty(rig.Sockets.Made);
        Assert.Equal(0, rig.Keys.Checkouts);
    }

    [Fact]
    public async Task NoKeyMeansNoSocketUntilOneIsAdded()
    {
        using var rig = new Rig();
        rig.Keys.HasKey = false;
        await rig.TickAt(Now);
        await rig.TickAt(Now.AddSeconds(30));
        Assert.Empty(rig.Sockets.Made);
        Assert.Equal(1, rig.Keys.Checkouts);                           // re-checked once a minute

        rig.Keys.HasKey = true;
        await rig.TickAt(Now.AddMinutes(1));
        Assert.Single(rig.Sockets.Made);
    }

    [Fact]
    public async Task TheHandshakeAuthenticatesThenSubscribesToEverything()
    {
        using var rig = new Rig();
        var s = await rig.Subscribe();

        Assert.Equal(2, s.Sent.Count);
        var auth = JObject.Parse(s.Sent[0]);
        Assert.Equal("auth", (string?)auth["action"]);
        Assert.Equal("key-id", (string?)auth["key"]);
        Assert.Equal("key-secret", (string?)auth["secret"]);
        Assert.Equal("{\"action\":\"subscribe\",\"news\":[\"*\"]}", s.Sent[1]);
        Assert.True(rig.Service.Subscribed);
    }

    [Fact]
    public async Task ATouchedStoryIsSaidAfterTheGatherAndOthersAreNot()
    {
        using var rig = new Rig(level: "importantOnly");
        var s = await rig.Subscribe();

        s.Deliver(Frame(1, "Apple Reports Q4 Results, Beats Estimates", Now, "AAPL"));
        s.Deliver(Frame(2, "Morgan Stanley Upgrades Apple To Overweight", Now, "AAPL"));   // medium: below the level
        s.Deliver(Frame(3, "Microsoft Reports Q1 Results", Now, "MSFT"));                  // not touched

        await rig.TickAt(Now.AddSeconds(1));
        Assert.Empty(rig.Said);
        await rig.TickAt(Now.AddSeconds(3));

        var said = Assert.Single(rig.Said);
        Assert.Equal("AAPL: Apple Reports Q4 Results, Beats Estimates.", said.Text);
        Assert.Equal("alpaca|1", said.Id);
        Assert.Equal(MarketEventImportance.High, said.Importance);
    }

    [Fact]
    public async Task NothingIsSaidWhileNoWindowIsOpenButItIsAfterwardIfStillFresh()
    {
        using var rig = new Rig();
        var s = await rig.Subscribe();
        rig.Presence.CanReachUser = false;
        s.Deliver(Frame(1, "Apple Halted", Now, "AAPL"));
        await rig.TickAt(Now.AddSeconds(5));
        Assert.Empty(rig.Said);

        rig.Presence.CanReachUser = true;
        await rig.TickAt(Now.AddSeconds(6));
        Assert.Single(rig.Said);
    }

    [Fact]
    public async Task SwitchingTheWireOffClosesTheSocketAndForgetsWhatWasWaiting()
    {
        using var rig = new Rig();
        var s = await rig.Subscribe();
        s.Deliver(Frame(1, "Apple Halted", Now, "AAPL"));

        rig.Set(st => st with { HeadlinesNewsWire = false });
        await rig.TickAt(Now.AddSeconds(5));
        Assert.True(s.Disposed);
        Assert.False(rig.Service.HasSocket);

        rig.Set(st => st with { HeadlinesNewsWire = true });
        await rig.TickAt(Now.AddSeconds(10));
        Assert.Equal(2, rig.Sockets.Made.Count);                      // reconnects at once
        Assert.Empty(rig.Said);                                       // the waiting story was forgotten
    }

    [Theory]
    [InlineData(402, 30)]
    [InlineData(406, 5)]
    [InlineData(409, 360)]
    public async Task AnErrorFromAlpacaClosesTheSocketAndWaits(int code, int minutes)
    {
        using var rig = new Rig();
        var s = await rig.Subscribe();
        s.Deliver($"[{{\"T\":\"error\",\"code\":{code},\"msg\":\"refused\"}}]");

        await rig.TickAt(Now.AddSeconds(1));
        Assert.True(s.Disposed);
        await rig.TickAt(Now.AddMinutes(minutes).AddSeconds(-1));
        Assert.Single(rig.Sockets.Made);
        await rig.TickAt(Now.AddMinutes(minutes).AddSeconds(1));
        Assert.Equal(2, rig.Sockets.Made.Count);
    }

    [Fact]
    public async Task ASocketDownForTwoMinutesIsReplacedAfterABackoff()
    {
        using var rig = new Rig();
        var s = await rig.Subscribe();
        s.IsConnected = false;                                        // the SDK socket is retrying

        await rig.TickAt(Now.AddSeconds(10));
        await rig.TickAt(Now.AddSeconds(10).AddMinutes(2).AddSeconds(-1));
        Assert.False(s.Disposed);                                     // its own retries get two minutes
        await rig.TickAt(Now.AddSeconds(10).AddMinutes(2));
        Assert.True(s.Disposed);

        await rig.TickAt(Now.AddSeconds(10).AddMinutes(3).AddSeconds(-1));
        Assert.Single(rig.Sockets.Made);
        await rig.TickAt(Now.AddSeconds(10).AddMinutes(3));
        Assert.Equal(2, rig.Sockets.Made.Count);
    }

    [Fact]
    public async Task AFailedConnectBacksOffDoublingToAQuarterHour()
    {
        using var rig = new Rig();
        rig.Sockets.NextConnectWorks = false;
        await rig.TickAt(Now);
        Assert.True(rig.Sockets.Last.Disposed);
        await rig.TickAt(Now.AddSeconds(59));
        Assert.Single(rig.Sockets.Made);
        await rig.TickAt(Now.AddMinutes(1));
        Assert.Equal(2, rig.Sockets.Made.Count);
        await rig.TickAt(Now.AddMinutes(2).AddSeconds(59));           // second wait is two minutes
        Assert.Equal(2, rig.Sockets.Made.Count);

        Assert.Equal(TimeSpan.FromMinutes(8), AlpacaNewsLiveNotices.Backoff(3));
        Assert.Equal(TimeSpan.FromMinutes(15), AlpacaNewsLiveNotices.Backoff(4));
        Assert.Equal(TimeSpan.FromMinutes(15), AlpacaNewsLiveNotices.Backoff(40));
    }

    // ── Settings, dossier, REST ─────────────────────────────────────────────

    private sealed class DictSettings : ISettingsManager
    {
        public readonly Dictionary<string, JToken> Store = new(StringComparer.Ordinal);
        public JToken? GetSetting(string keyPath, JToken? defaultValue = null)
            => Store.TryGetValue(keyPath, out var v) ? v : defaultValue;
        public void SetSetting(string keyPath, JToken value) => Store[keyPath] = value;
        public JObject GetEffectiveSettingsForSeries(string seriesId) => new();
        public void SaveSettings() { }
        public void ResetToDefaults() { }
        public void Reload() { }
    }

    [Fact]
    public void TheWireSwitchDefaultsOnAndSurvivesASaveAndReload()
    {
        Assert.True(WorkspaceState.Initial.HeadlinesNewsWire);
        Assert.True(new AppSettings(new DictSettings()).HeadlinesNewsWire);

        var settings = new AppSettings(new DictSettings()) { HeadlinesNewsWire = false };
        var store = new MockWorkspaceStore();
        var clock = new System.Reactive.Concurrency.HistoricalScheduler();
        using var svc = new PreferencePersistenceService(store, settings,
            NullLogger<PreferencePersistenceService>.Instance, clock);
        svc.Initialize();

        var seeded = store.DispatchedActions.OfType<UpdateSettingsAction>()
            .Select(a => a.Updater(WorkspaceState.Initial))
            .Last();
        Assert.False(seeded.HeadlinesNewsWire);

        // Ticking the box back on in Settings is written back to the saved settings.
        store.EmitState(seeded);
        store.EmitState(seeded with { HeadlinesNewsWire = true });
        clock.AdvanceBy(TimeSpan.FromSeconds(2));
        Assert.True(settings.HeadlinesNewsWire);
    }

    [Fact]
    public void TheDossierListsRecentNewsNewestFirstWithItsImportance()
    {
        var now = new DateTime(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);
        var read = new NewsReadResult(NewsReadStatus.Ok, new[]
        {
            Story(1, "Morgan Stanley Upgrades Apple To Overweight", new DateTime(2026, 11, 18, 13, 5, 0, DateTimeKind.Utc), "AAPL"),
            Story(2, "Apple Reports Q4 Results, Beats Estimates", new DateTime(2026, 11, 19, 21, 30, 0, DateTimeKind.Utc), "AAPL"),
        });

        var section = AssetDossierService.RecentNewsSection(read, now);

        Assert.Equal("Recent news", section.Title);
        Assert.Equal(DossierStatus.Ok, section.Status);
        Assert.Equal(new[] { "2026-11-19 4:30 PM New York", "2026-11-18 8:05 AM New York" },
            section.Fields.Select(f => f.Label));
        Assert.Equal("Apple Reports Q4 Results, Beats Estimates", section.Fields[0].Value);
        Assert.Equal(new[] { "Important", "Medium" }, section.Fields.Select(f => f.Note));
        Assert.Equal("Fiscal.ai", section.Fields[0].Source);
    }

    [Theory]
    [InlineData(NewsReadStatus.NoKey, DossierStatus.NoData, "Add an Alpaca key in API Keys")]
    [InlineData(NewsReadStatus.Refused, DossierStatus.Unavailable, "Alpaca refused the key")]
    [InlineData(NewsReadStatus.Failed, DossierStatus.Unavailable, "could not be read")]
    public void TheDossierSaysWhyThereIsNoNews(NewsReadStatus status, DossierStatus expected, string words)
    {
        var section = AssetDossierService.RecentNewsSection(NewsReadResult.Of(status), Now);
        Assert.Equal(expected, section.Status);
        Assert.Contains(words, section.StatusNote);
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
    public async Task TheRestReadAsksForTheSymbolWithTheKeyHeaders()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("alpaca-news-rest.json"));
        var source = new AlpacaNewsRestSource(new HttpClient(handler), new Keys());

        var read = await source.RecentAsync(Btc, 20);

        Assert.Equal(NewsReadStatus.Ok, read.Status);
        Assert.Equal(2, read.Stories.Count);
        var req = Assert.Single(handler.Requests);
        Assert.StartsWith("https://data.alpaca.markets/v1beta1/news?symbols=BTCUSD&limit=20", req.RequestUri!.ToString());
        Assert.Equal("key-id", req.Headers.GetValues("APCA-API-KEY-ID").Single());
        Assert.Equal("key-secret", req.Headers.GetValues("APCA-API-SECRET-KEY").Single());
    }

    [Fact]
    public async Task TheRestReadSendsNothingWithoutAKeyAndNamesARefusal()
    {
        var handler = new Handler(HttpStatusCode.Unauthorized, "{\"message\":\"unauthorized.\"}");
        var keys = new Keys { HasKey = false };
        var source = new AlpacaNewsRestSource(new HttpClient(handler), keys);

        Assert.Equal(NewsReadStatus.NoKey, (await source.RecentAsync(Aapl, 20)).Status);
        Assert.Empty(handler.Requests);

        keys.HasKey = true;
        Assert.Equal(NewsReadStatus.Refused, (await source.RecentAsync(Aapl, 20)).Status);
    }

    private sealed class FakeNews(NewsReadResult result) : INewsSource
    {
        public readonly List<NewsTarget> Asked = new();
        public Task<NewsReadResult> RecentAsync(NewsTarget target, int limit, CancellationToken ct = default)
        {
            Asked.Add(target);
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task TheDossierAsksForTheCoinOrTheTicker()
    {
        var news = new FakeNews(NewsReadResult.Of(NewsReadStatus.NoKey));
        var dossier = new AssetDossierService(news: news);

        var coin = await dossier.BuildAsync("XBT/USD", "Crypto|Spot", null);
        var stock = await dossier.BuildAsync("AAPL.US", "Stock|Spot", null);

        Assert.Equal(new[] { Btc, Aapl }, news.Asked);
        Assert.Contains(coin.Sections, s => s.Title == "Recent news");
        Assert.Contains(stock.Sections, s => s.Title == "Recent news");
    }
}
