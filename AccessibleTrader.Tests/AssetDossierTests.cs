using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The Alt+I asset dossier.
///
/// <para>
/// Most of this file is about what happens when there is NO data, because that is the case the
/// feature exists to handle well and the one most likely to rot. For a person reading by ear there
/// is an enormous difference between "this company filed no Form 4s in 90 days" (a fact, and often
/// the interesting one), "this field does not apply to a coin", and "the source did not answer".
/// A blank row collapses all three into the least useful reading.
/// </para>
/// </summary>
public class AssetDossierTests
{
    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class FakeCrypto : ICryptoProfileSource
    {
        public CryptoProfile? Profile;
        public Exception? Throw;
        public IReadOnlyList<GithubRepoStats> Repos = Array.Empty<GithubRepoStats>();

        public Task<CryptoProfile?> GetAsync(string symbol, CancellationToken ct = default)
            => Throw != null ? Task.FromException<CryptoProfile?>(Throw) : Task.FromResult(Profile);

        public Task<IReadOnlyList<GithubRepoStats>> GetRepoStatsAsync(IEnumerable<string> urls, CancellationToken ct = default)
            => Task.FromResult(Repos);
    }

    private sealed class FakeCompany : ICompanyProfileSource
    {
        public CompanyProfile? Profile;
        public Task<CompanyProfile?> GetAsync(string ticker, CancellationToken ct = default)
            => Task.FromResult(Profile);
    }

    private static CryptoProfile Coin(
        double? mc = 1_000_000_000, double? fdv = 1_100_000_000,
        double? circ = 900, double? max = 1000, double? vol = 50_000_000,
        bool paper = true, bool explorer = true, bool home = true,
        int? commits = 120, IReadOnlyList<string>? repos = null, DateTime? genesis = null)
        => new("test-coin", "Test Coin", 42, genesis ?? new DateTime(2015, 1, 1),
               circ, max, circ, mc, fdv, vol, commits, 500, 100, 20,
               home, paper, explorer,
               repos ?? new List<string> { "https://github.com/org/repo" },
               new List<string> { "Layer 1 (L1)" }, 10000, 5000);

    private static List<Ohlcv> Bars(int n = 200)
    {
        var list = new List<Ohlcv>();
        double px = 100;
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            px *= Math.Exp((rng.NextDouble() - 0.5) * 0.04);
            list.Add(new Ohlcv(new DateTime(2026, 1, 1).AddDays(i), px, px + 0.3, px - 0.3, px, 1000));
        }
        return list;
    }

    // ── Asset-class routing ────────────────────────────────────────────────────

    /// <summary>
    /// The class comes from the MARKET the chart was loaded from, not from the ticker. "ETH" is a
    /// coin on Bitstamp and could be an equity ticker elsewhere; guessing from the symbol would
    /// produce a confident dossier about the wrong kind of thing.
    /// </summary>
    [Theory]
    [InlineData("Crypto", true)]
    [InlineData("crypto", true)]
    [InlineData("Stock", false)]
    [InlineData("Economic", false)]
    [InlineData("", false)]
    public void AssetClassComesFromTheMarket_NotTheTicker(string market, bool expectCrypto)
        => Assert.Equal(expectCrypto, AssetDossierService.IsCrypto(market));

    [Fact]
    public async Task ACryptoSymbolBuildsCryptoSections()
    {
        var svc = new AssetDossierService(new FakeCrypto { Profile = Coin() }, new FakeCompany());
        var d = await svc.BuildAsync("BTC/USDT", "Crypto", Bars());

        Assert.Equal("crypto", d.AssetClass);
        Assert.Contains(d.Sections, s => s.Title == "Supply and dilution");
        Assert.Contains(d.Sections, s => s.Title == "Development");
        Assert.DoesNotContain(d.Sections, s => s.Title == "Financials");
    }

    [Fact]
    public async Task AnEquitySymbolBuildsCompanySections()
    {
        var company = new CompanyProfile("AAPL", "Apple Inc.", "Electronic Computers", 320193,
            new[] { (new DateTime(2026, 5, 1), 1.5) },
            new[] { (new DateTime(2026, 5, 1), 90e9) },
            new[] { (new DateTime(2026, 7, 1), 3) },
            new[] { (new DateTime(2026, 7, 15), 1) });

        var svc = new AssetDossierService(new FakeCrypto(), new FakeCompany { Profile = company });
        var d = await svc.BuildAsync("AAPL", "Stock", Bars());

        Assert.Equal("equity", d.AssetClass);
        Assert.Contains(d.Sections, s => s.Title == "Financials");
        Assert.Contains(d.Sections, s => s.Title == "Filing activity");
        Assert.DoesNotContain(d.Sections, s => s.Title == "Supply and dilution");
    }

    // ── The empty cases, which are the point ───────────────────────────────────

    /// <summary>
    /// An unlisted ticker is not a broken dossier. For a brand-new token it is the single most
    /// informative thing the screen can say, and the wording has to make that explicit rather than
    /// leaving the user to infer it from silence.
    /// </summary>
    [Fact]
    public async Task AnUnknownCryptoTicker_SaysNothingCanBeVerified_RatherThanFailing()
    {
        var svc = new AssetDossierService(new FakeCrypto { Profile = null }, new FakeCompany());
        var d = await svc.BuildAsync("SCAMCOIN", "Crypto", Bars());

        Assert.All(d.Sections.Where(s => s.Title != "Chart read"),
            s => Assert.Equal(DossierStatus.NoData, s.Status));
        Assert.Contains(d.Sections, s => s.StatusNote != null && s.StatusNote.Contains("unchecked"));
        Assert.Contains("returned nothing", d.Headline);
    }

    /// <summary>
    /// "The source did not answer" must never be presented as "there is nothing" — the first is a
    /// reason to retry, the second is a finding.
    /// </summary>
    [Fact]
    public async Task ASourceThatThrows_IsMarkedUnavailable_NotNoData()
    {
        var svc = new AssetDossierService(
            new FakeCrypto { Throw = new TimeoutException("upstream timed out") }, new FakeCompany());
        var d = await svc.BuildAsync("BTC/USDT", "Crypto", Bars());

        var broken = d.Sections.Where(s => s.Title != "Chart read").ToList();
        Assert.NotEmpty(broken);
        Assert.All(broken, s => Assert.Equal(DossierStatus.Unavailable, s.Status));
        Assert.All(broken, s => Assert.Contains("timed out", s.StatusNote!));
        Assert.Contains("could not be reached", d.Headline);
    }

    [Fact]
    public async Task AnUnconfiguredSource_IsUnavailableAndSaysSo()
    {
        var svc = new AssetDossierService(crypto: null, company: null);
        var d = await svc.BuildAsync("BTC/USDT", "Crypto", Bars());

        Assert.All(d.Sections.Where(s => s.Title != "Chart read"),
            s => Assert.Equal(DossierStatus.Unavailable, s.Status));
    }

    /// <summary>The chart read never depends on a network, so it must survive every remote failure.</summary>
    [Fact]
    public async Task TheChartReadSurvivesEveryRemoteFailure_AndComesFirst()
    {
        var svc = new AssetDossierService(
            new FakeCrypto { Throw = new Exception("down") }, new FakeCompany(),
            new ChartPatternDetector(new SwingStructureAnalyzer()), new SwingStructureAnalyzer());

        var d = await svc.BuildAsync("BTC/USDT", "Crypto", Bars());

        Assert.Equal("Chart read", d.Sections[0].Title);
        Assert.Equal(DossierStatus.Ok, d.Sections[0].Status);
        Assert.Contains(d.Sections[0].Fields, f => f.Label == "Market structure");
    }

    [Fact]
    public async Task WithTooFewBars_TheChartReadSaysHowManyItHas()
    {
        var svc = new AssetDossierService(new FakeCrypto { Profile = Coin() }, new FakeCompany());
        var d = await svc.BuildAsync("BTC/USDT", "Crypto", Bars(5));

        var chart = d.Sections.First(s => s.Title == "Chart read");
        Assert.Equal(DossierStatus.NoData, chart.Status);
        Assert.Contains("5 bars", chart.StatusNote);
    }

    [Fact]
    public async Task WithNoBarsAtAll_NothingThrows()
    {
        var svc = new AssetDossierService(new FakeCrypto { Profile = Coin() }, new FakeCompany());
        var d = await svc.BuildAsync("BTC/USDT", "Crypto", null);
        Assert.Equal(DossierStatus.NoData, d.Sections.First(s => s.Title == "Chart read").Status);
    }

    // ── Every field carries a source ───────────────────────────────────────────

    /// <summary>
    /// The standing rule for this layer is display everything with its provenance. A field without a
    /// source is not shippable, so this walks every field of every section on both asset classes.
    /// </summary>
    [Fact]
    public async Task EveryFieldNamesItsSource()
    {
        var svc = new AssetDossierService(
            new FakeCrypto { Profile = Coin() },
            new FakeCompany
            {
                Profile = new CompanyProfile("AAPL", "Apple", "Computers", 1,
                    new[] { (new DateTime(2026, 5, 1), 1.5) }, Array.Empty<(DateTime, double)>(),
                    Array.Empty<(DateTime, int)>(), Array.Empty<(DateTime, int)>())
            },
            new ChartPatternDetector(new SwingStructureAnalyzer()), new SwingStructureAnalyzer());

        foreach (var market in new[] { "Crypto", "Stock" })
        {
            var d = await svc.BuildAsync("TEST", market, Bars());
            foreach (var s in d.Sections)
                foreach (var f in s.Fields)
                    Assert.False(string.IsNullOrWhiteSpace(f.Source), $"{s.Title}/{f.Label} has no source");
        }
    }

    // ── The checks ─────────────────────────────────────────────────────────────

    [Fact]
    public void DilutionCheckFires_WhenFullyDilutedValueDwarfsMarketCap()
    {
        var flags = AssetDossierService.CryptoChecks(Coin(), fdvRatio: 12.0, circPct: 8, turnover: 0.05, liveRepo: null).ToList();

        Assert.True(flags.Single(f => f.Check == "Dilution overhang").Triggered);
        Assert.True(flags.Single(f => f.Check == "Supply not yet released").Triggered);
    }

    [Fact]
    public void DilutionCheckIsClear_WhenNearlyEverythingIsCirculating()
    {
        var flags = AssetDossierService.CryptoChecks(Coin(), fdvRatio: 1.02, circPct: 96, turnover: 0.06, liveRepo: null).ToList();

        Assert.False(flags.Single(f => f.Check == "Dilution overhang").Triggered);
        Assert.False(flags.Single(f => f.Check == "Supply not yet released").Triggered);
    }

    /// <summary>Turnover is flagged at BOTH ends: too little is illiquid, too much is a wash tell.</summary>
    [Theory]
    [InlineData(0.001, true, false)]
    [InlineData(0.08, false, false)]
    [InlineData(3.0, false, true)]
    public void TurnoverIsFlaggedAtBothExtremes(double turnover, bool illiquid, bool wash)
    {
        var flags = AssetDossierService.CryptoChecks(Coin(), 1.1, 90, turnover, null).ToList();
        Assert.Equal(illiquid, flags.Single(f => f.Check == "Illiquid").Triggered);
        Assert.Equal(wash, flags.Single(f => f.Check == "Possible wash trading").Triggered);
    }

    [Fact]
    public void MissingDisclosureIsFlagged_BecauseAbsenceIsTheMeasurement()
    {
        var bare = Coin(paper: false, explorer: false, repos: new List<string>());
        var flags = AssetDossierService.CryptoChecks(bare, 1.1, 90, 0.05, null).ToList();

        Assert.True(flags.Single(f => f.Check == "No whitepaper").Triggered);
        Assert.True(flags.Single(f => f.Check == "No public source code").Triggered);
        Assert.True(flags.Single(f => f.Check == "No block explorer").Triggered);
    }

    /// <summary>
    /// The staleness check must prefer the LIVE GitHub push over the aggregator's commit count,
    /// because the aggregator's count is precisely the number that goes wrong. Measured 2026-08-02:
    /// CoinGecko reported Kaspa at zero commits in four weeks because it tracks the superseded
    /// kaspad repository, while rusty-kaspa had been pushed that same day.
    /// </summary>
    [Fact]
    public void DevelopmentStaleness_TrustsTheLiveRepoOverTheAggregatorsZero()
    {
        var kaspaLike = Coin(commits: 0);
        var livePush = new GithubRepoStats("kaspanet/rusty-kaspa", DateTime.UtcNow.AddDays(-1), 843, 290, 195, false);

        var flags = AssetDossierService.CryptoChecks(kaspaLike, 1.0, 96, 0.05, livePush).ToList();

        Assert.False(flags.Single(f => f.Check == "Development stalled").Triggered);
        Assert.Contains("rusty-kaspa", flags.Single(f => f.Check == "Development stalled").Detail);
    }

    [Fact]
    public void DevelopmentStaleness_FiresWhenTheLiveRepoIsGenuinelyOld()
    {
        var old = new GithubRepoStats("org/abandoned", DateTime.UtcNow.AddDays(-400), 10, 2, 0, false);
        var flags = AssetDossierService.CryptoChecks(Coin(), 1.0, 96, 0.05, old).ToList();
        Assert.True(flags.Single(f => f.Check == "Development stalled").Triggered);
    }

    [Fact]
    public void ArchivedRepositoryIsFlagged()
    {
        var archived = new GithubRepoStats("org/repo", DateTime.UtcNow.AddDays(-2), 10, 2, 0, Archived: true);
        var flags = AssetDossierService.CryptoChecks(Coin(), 1.0, 96, 0.05, archived).ToList();
        Assert.True(flags.Single(f => f.Check == "Repository archived").Triggered);
    }

    [Fact]
    public void UncappedIssuanceIsFlagged()
    {
        var flags = AssetDossierService.CryptoChecks(Coin(max: null), 1.0, null, 0.05, null).ToList();
        Assert.True(flags.Single(f => f.Check == "Uncapped issuance").Triggered);
    }

    /// <summary>
    /// Every check must state its reasoning even when it is CLEAR. A bare "clear" gives the user
    /// nothing to disagree with, and the whole posture of this feature is that the user can.
    /// </summary>
    [Fact]
    public void EveryCheckCarriesADetailAndASource_WhetherOrNotItFired()
    {
        var flags = AssetDossierService.CryptoChecks(Coin(), 1.1, 90, 0.05, null).ToList();
        Assert.NotEmpty(flags);
        Assert.All(flags, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Check));
            Assert.False(string.IsNullOrWhiteSpace(f.Detail));
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
        });
    }

    // ── The headline ───────────────────────────────────────────────────────────

    [Fact]
    public void Headline_LeadsWithTheAssetAndHowMuchIsPopulated()
    {
        var sections = new List<DossierSection>
        {
            new("A", DossierStatus.Ok, Array.Empty<DossierField>()),
            new("B", DossierStatus.Ok, Array.Empty<DossierField>()),
            new("C", DossierStatus.NoData, Array.Empty<DossierField>()),
        };
        var flags = new List<DossierFlag> { new("Illiquid", true, "x", "y"), new("Other", false, "x", "y") };

        string h = DossierHeadline.Build("KAS/USDT", "crypto", sections, flags);

        Assert.StartsWith("KAS/USDT, crypto dossier.", h);
        Assert.Contains("2 of 3 sections have data", h);
        Assert.Contains("returned nothing: C", h);
        Assert.Contains("1 of 2 checks raised a flag: Illiquid", h);
    }

    [Fact]
    public void Headline_SaysPlainlyWhenNothingLoaded()
    {
        var sections = new List<DossierSection>
        {
            new("A", DossierStatus.Unavailable, Array.Empty<DossierField>()),
        };
        string h = DossierHeadline.Build("X", "crypto", sections, new List<DossierFlag>());

        Assert.Contains("No section returned data.", h);
        Assert.Contains("could not be reached: A", h);
        Assert.Contains("No checks were run.", h);
    }

    [Fact]
    public void Headline_ListsAtMostThreeFlagsSoItStaysSpeakable()
    {
        var flags = Enumerable.Range(0, 6)
            .Select(i => new DossierFlag($"Check{i}", true, "d", "s")).ToList<DossierFlag>();
        string h = DossierHeadline.Build("X", "crypto",
            new List<DossierSection> { new("A", DossierStatus.Ok, Array.Empty<DossierField>()) }, flags);

        Assert.Contains("6 of 6 checks raised a flag", h);
        Assert.Contains("and others", h);
        Assert.DoesNotContain("Check5", h);
    }

    // ── Symbol normalisation ───────────────────────────────────────────────────

    /// <summary>
    /// Providers spell the same pair three ways. All of them have to resolve to one coin, or the
    /// dossier silently reports on nothing for two of the three.
    /// </summary>
    [Theory]
    [InlineData("BTC/USDT", "BTC")]
    [InlineData("BTC-USD", "BTC")]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("ETHUSD", "ETH")]
    [InlineData("KAS/USDT", "KAS")]
    [InlineData("BTC", "BTC")]
    public void CryptoSymbolsNormaliseToTheBaseAsset(string input, string expected)
        => Assert.Equal(expected, CoinGeckoCryptoProfileSource.BaseSymbol(input));

    [Theory]
    [InlineData("AAPL", "AAPL")]
    [InlineData("AAPL/USD", "AAPL")]
    [InlineData("AAPL.US", "AAPL")]
    [InlineData("NASDAQ:MSFT", "NASDAQ")]
    public void EquityTickersNormalise(string input, string expected)
        => Assert.Equal(expected, EdgarCompanyProfileSource.Normalise(input));

    [Theory]
    [InlineData("https://github.com/kaspanet/rusty-kaspa", "kaspanet/rusty-kaspa")]
    [InlineData("https://github.com/org/repo/", "org/repo")]
    [InlineData("not a url", null)]
    public void GithubUrlsReduceToOwnerSlashRepo(string url, string? expected)
        => Assert.Equal(expected, CoinGeckoCryptoProfileSource.RepoSlug(url));

    // ── Parsing ────────────────────────────────────────────────────────────────

    [Fact]
    public void CoinDocumentParses_IncludingTheAbsenceOfDisclosureLinks()
    {
        var p = CoinGeckoCryptoProfileSource.ParseCoin("""
            {"id":"x","name":"X Coin","market_cap_rank":9,
             "market_data":{"circulating_supply":90,"max_supply":100,"total_supply":95,
                            "market_cap":{"usd":1000},"fully_diluted_valuation":{"usd":1100},
                            "total_volume":{"usd":50}},
             "developer_data":{"commit_count_4_weeks":7,"stars":5,"forks":2,"pull_request_contributors":3},
             "links":{"homepage":[""],"whitepaper":"","repos_url":{"github":[]},"blockchain_site":[""]},
             "community_data":{"twitter_followers":100,"reddit_subscribers":50},
             "categories":["L1"]}
            """);

        Assert.NotNull(p);
        Assert.Equal("X Coin", p!.Name);
        Assert.Equal(90, p.CirculatingSupply);
        Assert.False(p.HasHomepage);
        Assert.False(p.HasWhitepaper);
        Assert.False(p.HasExplorer);
        Assert.Empty(p.Repos);
    }

    [Fact]
    public void AMalformedCoinDocumentReturnsNullRatherThanThrowing()
        => Assert.Null(CoinGeckoCryptoProfileSource.ParseCoin("""{"error":"nope"}"""));

    [Fact]
    public void RepoDocumentParses()
    {
        var r = CoinGeckoCryptoProfileSource.ParseRepo("""
            {"full_name":"kaspanet/rusty-kaspa","pushed_at":"2026-08-02T10:00:00Z",
             "stargazers_count":843,"forks_count":290,"open_issues_count":195,"archived":false}
            """);
        Assert.NotNull(r);
        Assert.Equal("kaspanet/rusty-kaspa", r!.FullName);
        Assert.Equal(843, r.Stars);
        Assert.False(r.Archived);
        Assert.Equal(new DateTime(2026, 8, 2), r.LastPush!.Value.Date);
    }
}
