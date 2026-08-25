using AccessibleTrader.StrategyLab;

namespace AccessibleTrader.Tests;

/// <summary>
/// Analyst revision breadth — the measurement, not the verdict.
///
/// <para>
/// The study itself came back <b>UNTESTED rather than null</b>: FMP's free tier caps grades history
/// at ten rows per symbol, which left six usable monthly cross-sections and a minimum detectable
/// effect of 6.5% a month. What these tests pin is therefore not a result but the two things that
/// would silently corrupt one — how breadth is computed, and what happens when coverage is absent.
/// </para>
/// </summary>
public class GradesTests
{
    private static GradesCommand.Row R(int sb, int b, int h, int s, int ss) =>
        new() { Date = "2026-01-01", Symbol = "TST", StrongBuy = sb, Buy = b, Hold = h, Sell = s, StrongSell = ss };

    // ── Breadth ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BreadthIsTheBullishShareOfTheMix()
    {
        Assert.Equal(0.5, R(1, 4, 3, 1, 1).Breadth!.Value, 6);   // 5 of 10
        Assert.Equal(1.0, R(2, 3, 0, 0, 0).Breadth!.Value, 6);
        Assert.Equal(0.0, R(0, 0, 4, 1, 1).Breadth!.Value, 6);
    }

    /// <summary>
    /// The distinction that decides whether the cross-sectional sort means anything.
    ///
    /// <para>
    /// "No analyst covers this company" and "every analyst is bearish" are opposite facts, and a
    /// breadth of zero would express them identically. Collapsing them loads every uncovered stock
    /// onto the bearish end of the sort — and uncovered stocks are exactly the small, thinly
    /// followed names where any real revision effect is supposed to live. The bug would therefore
    /// not merely add noise, it would systematically mis-sort the part of the universe the
    /// hypothesis is about.
    /// </para>
    /// </summary>
    [Fact]
    public void NoCoverageIsUndefinedBreadthNotZeroBreadth()
    {
        Assert.Null(R(0, 0, 0, 0, 0).Breadth);
        Assert.Equal(0.0, R(0, 0, 1, 0, 0).Breadth!.Value, 6);   // covered, and nobody is bullish
    }

    [Fact]
    public void TotalCountsEveryRatingBucket()
        => Assert.Equal(15, R(1, 2, 3, 4, 5).Total);

    // ── The universe ────────────────────────────────────────────────────────────

    /// <summary>
    /// Funds are excluded because an ETF has no analyst rating mix, so asking about one spends a
    /// request to be told nothing — and on a tier that blocks symbols outright, a wasted request is
    /// a symbol we did not get to ask about.
    /// </summary>
    [Fact]
    public void TheEquityUniverseExcludesFunds()
    {
        string dir = Path.Combine(Path.GetTempPath(), "grades-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var f in new[] { "yahoo_AAPL_1d.json", "yahoo_SPY_1d.json", "yahoo_XLK_1d.json",
                                      "yahoo_JPM_1d.json", "xs_binancevision_btc_funding_1d.json" })
                File.WriteAllText(Path.Combine(dir, f), "{}");

            var universe = GradesCommand.EquityUniverse(dir);

            Assert.Contains("AAPL", universe);
            Assert.Contains("JPM", universe);
            Assert.DoesNotContain("SPY", universe);
            Assert.DoesNotContain("XLK", universe);
            Assert.DoesNotContain(universe, u => u.Contains("funding"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void AMissingSnapshotDirectoryYieldsAnEmptyUniverseRatherThanThrowing()
        => Assert.Empty(GradesCommand.EquityUniverse(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())));
}
