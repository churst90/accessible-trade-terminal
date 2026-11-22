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
/// What every news story goes through, whichever service sent it: the importance rule, matching
/// a story to the stocks and coins you touch, the gate that gathers and paces them, and the
/// dossier's "Recent news". Written for Alpaca's news wire (stage 2) and kept when that wire was
/// taken out on 2026-11-22, since Finnhub's news (stage 3) uses all of it; the importance cases
/// are real fiscal.ai headlines from Cody's Alpaca account on 2026-11-20. No network, no speech,
/// no sound.
/// </summary>
public class NewsStoryTests
{

    private static readonly DateTime Now = new(2026, 11, 2, 21, 31, 0, DateTimeKind.Utc);

    private static NewsStory Story(long id, string headline, DateTime createdUtc, params string[] symbols)
        => new(id, headline, "", "fiscal.ai", createdUtc, createdUtc, symbols, "https://x");

    private static readonly NewsTarget Aapl = new("AAPL", false);
    private static readonly NewsTarget Btc = new("BTC", true);

    // ── Headline text ────────────────────────────────────────────────────────

    [Fact]
    public void HtmlEntitiesAndRunsOfSpacesAreTakenOutOfAHeadline()
    {
        Assert.Equal("Apple & Google Face New EU Probe", NewsText.Clean(" Apple &amp; Google  Face New EU Probe"));
        Assert.Equal("", NewsText.Clean(null));
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

        var targets = FinnhubNewsLiveNotices.TouchedTargets(state, held);

        Assert.Equal(new[] { "BTC:coin", "NVDA:stock", "MSFT:stock", "ETH:coin", "SOL:coin" },
            targets.Select(t => t.Label + (t.Coin ? ":coin" : ":stock")));
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
        Assert.Equal("finnhub|2", said.Id);
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

    // ── Settings and the dossier ────────────────────────────────────────────

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
    [InlineData(NewsReadStatus.NoKey, DossierStatus.NoData, "Add a Finnhub key in API Keys")]
    [InlineData(NewsReadStatus.Refused, DossierStatus.Unavailable, "Finnhub refused the key")]
    [InlineData(NewsReadStatus.Failed, DossierStatus.Unavailable, "could not be read")]
    [InlineData(NewsReadStatus.RateLimited, DossierStatus.Unavailable, "could not be read")]
    public void TheDossierSaysWhyThereIsNoNews(NewsReadStatus status, DossierStatus expected, string words)
    {
        var section = AssetDossierService.RecentNewsSection(NewsReadResult.Of(status, "Finnhub"), Now);
        Assert.Equal(expected, section.Status);
        Assert.Contains(words, section.StatusNote);
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
