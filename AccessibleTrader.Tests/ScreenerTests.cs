using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests;

/// <summary>
/// Layer 1 of the crypto screen: the deterministic veto.
///
/// <para>
/// The checks are conventions rather than findings, so what is worth pinning is not whether the
/// thresholds are <i>right</i> — nobody knows that yet, and finding out needs the forward universe
/// archive — but whether the screen is <b>honest</b>. Three properties carry that:
/// </para>
/// <list type="number">
///   <item>Missing data must never become a flag. Flagging thin reporting punishes obscurity and
///         calls it a quality measurement.</item>
///   <item>Broken data must never become a confident sentence. The first run reported an asset as
///         having "FDV 999999995.3x market cap", which is a sentinel value wearing prose.</item>
///   <item>A check must mean the same thing for every asset it is applied to, which is why
///         dollar-pegged tokens are excluded from the price-shaped ones.</item>
/// </list>
/// </summary>
public class ScreenerTests
{
    private static UniverseRecorderCommand.Row R(
        double? price = 10, double? mcap = 1e9, double? fdv = 1e9,
        double? circ = 1e8, double? max = 1e8, double? vol = 5e7, double? ath = 12)
        => new()
        {
            Date = "2026-08-02", Id = "test", Symbol = "TST", Name = "Test",
            Rank = 100, Price = price, MarketCap = mcap, FullyDiluted = fdv,
            Circulating = circ, MaxSupply = max, Volume24h = vol, Ath = ath
        };

    private static bool Tripped(UniverseRecorderCommand.Row r, string name)
        => ScreenerCommand.Screen(r).Any(c => c.Name == name && c.Tripped);

    private static bool Present(UniverseRecorderCommand.Row r, string name)
        => ScreenerCommand.Screen(r).Any(c => c.Name == name);

    // ── Missing data is not a finding ───────────────────────────────────────────

    /// <summary>
    /// An asset the aggregator reports thinly must come out clean, not flagged. Otherwise the
    /// screen measures how well covered a token is and presents it as how sound the token is —
    /// and the correlation between those runs the wrong way for a screen meant to find garbage.
    /// </summary>
    [Fact]
    public void AnAssetWithNoReportedFiguresRaisesNothingItCannotKnow()
    {
        var blank = R(price: null, mcap: null, fdv: null, circ: null, max: null, vol: null, ath: null);
        var checks = ScreenerCommand.Screen(blank);

        // "uncapped" is the one legitimate exception: absence IS the measurement there.
        foreach (var c in checks.Where(c => c.Name != "uncapped"))
            Assert.False(c.Tripped, $"'{c.Name}' fired on an asset with no data: {c.Detail}");
    }

    /// <summary>
    /// The exception, stated explicitly so it cannot be "fixed" by someone applying the rule above
    /// mechanically. No maximum supply is a fact about the token, not a gap in the reporting.
    /// </summary>
    [Fact]
    public void AbsenceOfAMaximumSupplyIsItselfTheFinding()
    {
        Assert.True(Tripped(R(max: null), "uncapped"));
        Assert.False(Tripped(R(max: 1e8), "uncapped"));
    }

    // ── Broken data is not a finding either ─────────────────────────────────────

    /// <summary>
    /// The sentinel-value case, caught by running the screen on the real universe rather than by a
    /// test. Stating a fabricated fact in the same confident voice as a real one is worse than
    /// staying quiet, so an implausible ratio trips nothing and says the data is unusable.
    /// </summary>
    [Fact]
    public void AnImpossibleFdvRatioIsReportedAsBadDataNotAsDilution()
    {
        var absurd = R(mcap: 1e6, fdv: 1e15);   // a 10^9 ratio

        Assert.False(Tripped(absurd, "fdv"));
        Assert.True(Present(absurd, "fdv-bad"));
        Assert.False(ScreenerCommand.Screen(absurd).Single(c => c.Name == "fdv-bad").Tripped);
    }

    [Fact]
    public void APlausibleDilutionRatioStillFlags()
        => Assert.True(Tripped(R(mcap: 1e9, fdv: 5e9), "fdv"));

    // ── Stablecoins ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A dollar-pegged token is SUPPOSED to sit at its high and to turn over many times its market
    /// cap. Running the standard checks on one produces flags that are definitionally true and
    /// carry no information — the first live run duly flagged two stablecoins as "100% below
    /// all-time high", which is both wrong and meaningless.
    /// </summary>
    [Fact]
    public void StablecoinsAreNotJudgedOnDrawdownOrTurnover()
    {
        var usd = R(price: 1.0, ath: 1.05, vol: 5e9, mcap: 1e9);

        Assert.True(ScreenerCommand.IsLikelyStablecoin(usd));
        Assert.False(Present(usd, "drawdown"));
        Assert.False(Present(usd, "wash"));
        Assert.False(Present(usd, "illiquid"));
    }

    /// <summary>The check that replaces them: for a dollar token, being off the dollar is the question.</summary>
    [Fact]
    public void AStablecoinIsJudgedOnItsPeg()
    {
        Assert.False(Tripped(R(price: 0.999, ath: 1.02), "depeg"));
        Assert.True(Tripped(R(price: 0.94, ath: 1.02), "depeg"));
    }

    /// <summary>
    /// Supply checks still apply to a stablecoin — those are about issuance, which matters at least
    /// as much for something claiming to be money.
    /// </summary>
    [Fact]
    public void StablecoinsAreStillJudgedOnIssuance()
        => Assert.True(Tripped(R(price: 1.0, ath: 1.02, max: null), "uncapped"));

    /// <summary>A volatile asset that merely happens to trade near $1 is not a stablecoin.</summary>
    [Fact]
    public void APennyTokenIsNotMistakenForAStablecoin()
        => Assert.False(ScreenerCommand.IsLikelyStablecoin(R(price: 1.02, ath: 40)));

    // ── The checks themselves ───────────────────────────────────────────────────

    [Fact]
    public void LiquidityIsFlaggedAtBothEnds()
    {
        Assert.True(Tripped(R(mcap: 1e9, vol: 1e6), "illiquid"));    // 0.1% turnover
        Assert.True(Tripped(R(mcap: 1e9, vol: 3e9), "wash"));        // 300% turnover
        Assert.False(Tripped(R(mcap: 1e9, vol: 5e7), "illiquid"));   // 5% — normal
        Assert.False(Tripped(R(mcap: 1e9, vol: 5e7), "wash"));
    }

    [Fact]
    public void ALowFloatFlagsEvenWhenFdvLooksFine()
        => Assert.True(Tripped(R(circ: 1e7, max: 1e8), "float"));    // 10% circulating

    [Fact]
    public void DeepDrawdownFlags()
    {
        Assert.True(Tripped(R(price: 1, ath: 100), "drawdown"));     // 99% down
        Assert.False(Tripped(R(price: 60, ath: 100), "drawdown"));   // 40% down
    }

    // ── The screen as a whole ───────────────────────────────────────────────────

    /// <summary>
    /// A screen that flags everything and one that flags nothing are equally useless, and both look
    /// entirely reasonable if you only read the top of the list. A sound asset must come out clean.
    /// </summary>
    [Fact]
    public void AWellFormedAssetRaisesNoFlags()
    {
        var sound = R(price: 100, mcap: 5e9, fdv: 5.2e9, circ: 9.6e7, max: 1e8, vol: 1e8, ath: 200);

        var flags = ScreenerCommand.Screen(sound).Where(c => c.Tripped).ToList();

        Assert.True(flags.Count == 0, "clean asset flagged: " + string.Join("; ", flags.Select(f => f.Detail)));
    }

    /// <summary>
    /// And the mirror: the profile of a token that should light up. Heavy unissued supply, a tiny
    /// float, no liquidity and a full cycle of underwater holders.
    /// </summary>
    [Fact]
    public void AClassicallyBadAssetRaisesSeveral()
    {
        var bad = R(price: 0.01, mcap: 5e6, fdv: 5e7, circ: 5e6, max: 1e8, vol: 1e3, ath: 2.0);

        Assert.True(ScreenerCommand.Screen(bad).Count(c => c.Tripped) >= 4);
    }

    /// <summary>
    /// Every flag must carry its reasoning. A screen output reading only "fdv" teaches nothing and
    /// will be either over-trusted or ignored, and this feature's whole justification is that it
    /// makes a gamble informed rather than blind.
    /// </summary>
    [Fact]
    public void EveryTrippedFlagExplainsItself()
    {
        var bad = R(price: 0.01, mcap: 5e6, fdv: 5e7, circ: 5e6, max: 1e8, vol: 1e3, ath: 2.0);

        foreach (var c in ScreenerCommand.Screen(bad).Where(c => c.Tripped))
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Detail), $"'{c.Name}' has no explanation");
            Assert.True(c.Detail.Length > 20, $"'{c.Name}' explanation is too terse: {c.Detail}");
        }
    }
}
