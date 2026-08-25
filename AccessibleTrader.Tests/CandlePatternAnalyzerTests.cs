using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The candlestick analyzer behind the spoken bar description, the forming-pattern narration and
/// <c>AlertCondition.PatternDetected</c>.
///
/// <para>
/// Written 2026-08-02 during a correctness audit. The analyzer had shipped with no tests at all,
/// which is how four defects survived in it — each one of the kind that produces a confident,
/// plausible-sounding announcement rather than an error. They are called out by name on the tests
/// that would have caught them, because the defect is the interesting part.
/// </para>
/// </summary>
public class CandlePatternAnalyzerTests
{
    private static readonly SdkCandlePatternAnalyzer A = new();

    private static Ohlcv Bar(double o, double h, double l, double c, int day = 1)
        => new(new DateTime(2026, 1, 1).AddDays(day), o, h, l, c, 1000);

    /// <summary>A falling run of `n` bars ending just before the pattern bar.</summary>
    private static List<Ohlcv> Downtrend(int n, double from = 120)
        => Enumerable.Range(0, n).Select(i => Bar(from - i, from - i + 0.5, from - i - 1.5, from - i - 1, i)).ToList();

    private static List<Ohlcv> Uptrend(int n, double from = 100)
        => Enumerable.Range(0, n).Select(i => Bar(from + i, from + i + 1.5, from + i - 0.5, from + i + 1, i)).ToList();

    // ── The defect that inverted the meaning of the announcement ────────────────

    /// <summary>
    /// Hammer and hanging man are the SAME SHAPE. Which one it is depends on the trend it
    /// interrupts, and they mean opposite things.
    ///
    /// <para>
    /// The old code decided this from the colour of the single preceding candle. So a green bar
    /// inside a sustained decline — completely ordinary — turned a hammer into a hanging man, and
    /// the terminal told the user "hanging man", a bearish reversal, at the bottom of a downtrend.
    /// This test is the trap: the bar immediately before the hammer closes UP while the trend is
    /// clearly down.
    /// </para>
    /// </summary>
    [Fact]
    public void HammerInADowntrend_IsAHammer_EvenWhenThePreviousCandleClosedUp()
    {
        var history = Downtrend(6);                       // falling hard
        history.Add(Bar(105, 106, 104.5, 106, 6));        // one green bar — the old discriminator
        // Body must stay above DojiBodyMaxPercent, or this is a dragonfly doji (which is what a
        // doji-bodied hammer correctly is) and never reaches the hammer branch at all.
        var hammer = Bar(100, 100.6, 94, 100.4, 7);       // long lower wick, small body up top
        history.Add(hammer);

        var r = A.Analyze(hammer, history[^2], history[^3], history);

        Assert.Equal(CandleType.Hammer, r.Type);
        Assert.Equal(CandleDirection.Bullish, r.Direction);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void TheSameShapeInAnUptrend_IsAHangingMan()
    {
        var history = Uptrend(8);
        var shape = Bar(112, 112.6, 106, 112.4, 8);
        history.Add(shape);

        var r = A.Analyze(shape, history[^2], history[^3], history);

        Assert.Equal(CandleType.HangingMan, r.Type);
        Assert.Equal(CandleDirection.Bearish, r.Direction);
    }

    [Fact]
    public void ShootingStarAndInvertedHammer_AreAlsoDecidedByTheTrend()
    {
        // Opens above the previous close so the (now stricter) three-white-soldiers rule does not
        // claim the bar first — multi-bar patterns outrank single-bar types by design.
        var up = Uptrend(8);
        var star = Bar(109, 116, 108.6, 109.6, 8);
        up.Add(star);
        Assert.Equal(CandleType.ShootingStar, A.Analyze(star, up[^2], up[^3], up).Type);

        // Low kept clear of the previous low, or the tweezer-bottom rule takes it first.
        var down = Downtrend(8);
        var inv = Bar(112.5, 119.5, 112.1, 113.1, 8);
        down.Add(inv);
        Assert.Equal(CandleType.InvertedHammer, A.Analyze(inv, down[^2], down[^3], down).Type);
    }

    /// <summary>
    /// With no trailing context the analyzer cannot know which of the pair it is looking at. It must
    /// not claim a reversal it has no basis for — silence about direction is the honest output, and
    /// the alternative (guessing from one candle) is the defect above.
    /// </summary>
    [Fact]
    public void WithoutContext_TheShapeIsNamedButNoReversalIsAsserted()
    {
        var shape = Bar(100, 100.6, 94, 100.4);
        var r = A.Analyze(shape, Bar(107, 107.5, 104, 105));

        Assert.True(r.Type is CandleType.Hammer or CandleType.HangingMan);
        Assert.False(r.IsReversal);
    }

    // ── Star patterns ──────────────────────────────────────────────────────────

    /// <summary>
    /// The star must sit outside the body it is reversing. Without that test the pattern fired on
    /// any [long red, small body, green closing above the midpoint] — including arrangements where
    /// the middle bar sat in the upper half of the first bar, which is not a star at all.
    /// </summary>
    [Fact]
    public void MorningStar_RequiresTheStarToSitBelowTheFirstBody()
    {
        var first = Bar(120, 120.5, 108, 108.5, 1);           // long red body 108.5–120
        var real = Bar(107, 107.6, 106.4, 107.2, 2);          // star clearly BELOW it
        var third = Bar(107.5, 116, 107.2, 115.5, 3);         // closes above the midpoint (114.25)
        Assert.Equal(CandlePattern.MorningStar,
            A.Analyze(third, real, first, new List<Ohlcv> { first, real, third }).Pattern);

        // Same three bars except the middle one sits INSIDE the first body — not a star.
        var notAStar = Bar(117, 117.6, 116.4, 117.2, 2);
        Assert.NotEqual(CandlePattern.MorningStar,
            A.Analyze(third, notAStar, first, new List<Ohlcv> { first, notAStar, third }).Pattern);
    }

    [Fact]
    public void EveningStar_RequiresTheStarToSitAboveTheFirstBody()
    {
        var first = Bar(100, 112.5, 99.5, 112, 1);            // long green body 100–112
        var real = Bar(113.5, 114.2, 112.9, 113.7, 2);        // star above it
        var third = Bar(113, 113.2, 104, 104.5, 3);           // closes below the midpoint (106)
        Assert.Equal(CandlePattern.EveningStar,
            A.Analyze(third, real, first, new List<Ohlcv> { first, real, third }).Pattern);
    }

    // ── Three white soldiers / three black crows ────────────────────────────────

    /// <summary>
    /// Each soldier opens INSIDE the previous body. The old test only required each open to be
    /// above the previous OPEN, so three bars gapping away from each other — a different pattern,
    /// with a different meaning — counted as a steady advance.
    /// </summary>
    [Fact]
    public void ThreeWhiteSoldiers_RequireEachOpenInsideThePreviousBody()
    {
        var b1 = Bar(100, 106.5, 99.5, 106, 1);
        var b2 = Bar(103, 112.5, 102.5, 112, 2);              // opens inside b1's body
        var b3 = Bar(109, 118.5, 108.5, 118, 3);              // opens inside b2's body
        Assert.Equal(CandlePattern.ThreeWhiteSoldiers,
            A.Analyze(b3, b2, b1, new List<Ohlcv> { b1, b2, b3 }).Pattern);

        var gapped = Bar(114, 124.5, 113.5, 124, 3);          // opens ABOVE b2's close
        Assert.NotEqual(CandlePattern.ThreeWhiteSoldiers,
            A.Analyze(gapped, b2, b1, new List<Ohlcv> { b1, b2, gapped }).Pattern);
    }

    [Fact]
    public void ThreeBlackCrows_MirrorTheSoldiers()
    {
        var b1 = Bar(120, 120.5, 113.5, 114, 1);
        var b2 = Bar(117, 117.5, 107.5, 108, 2);
        var b3 = Bar(111, 111.5, 101.5, 102, 3);
        Assert.Equal(CandlePattern.ThreeBlackCrows,
            A.Analyze(b3, b2, b1, new List<Ohlcv> { b1, b2, b3 }).Pattern);
    }

    // ── Harami ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A harami is containment inside the previous body and nothing else. The old code additionally
    /// required the inside bar's body to be under half of its OWN range, which is not part of the
    /// definition and rejected a valid harami whose small body happened to fill its small range.
    /// </summary>
    [Fact]
    public void BullishHarami_IsFoundEvenWhenTheInsideBarIsAlmostAllBody()
    {
        var prev = Bar(120, 120.5, 105.5, 106, 1);           // long red body 106–120
        var inside = Bar(110, 114.2, 109.8, 114, 2);         // body 110–114, ~95% of its own range
        var r = A.Analyze(inside, prev, null, new List<Ohlcv> { prev, inside });

        Assert.Equal(CandlePattern.BullishHarami, r.Pattern);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void BearishHarami_IsTheMirror()
    {
        var prev = Bar(106, 120.5, 105.5, 120, 1);
        var inside = Bar(114, 114.2, 109.8, 110, 2);
        Assert.Equal(CandlePattern.BearishHarami,
            A.Analyze(inside, prev, null, new List<Ohlcv> { prev, inside }).Pattern);
    }

    // ── Patterns that were already right, pinned so they stay right ─────────────

    [Fact]
    public void BullishEngulfing_CoversThePreviousBodyEntirely()
    {
        var prev = Bar(112, 112.5, 107.5, 108, 1);
        var cur = Bar(107, 116.5, 106.5, 116, 2);
        var r = A.Analyze(cur, prev, null, new List<Ohlcv> { prev, cur });

        Assert.Equal(CandlePattern.BullishEngulfing, r.Pattern);
        Assert.Equal(CandleDirection.Bullish, r.Direction);
        Assert.Equal(2, r.PatternBarCount);
    }

    [Fact]
    public void BearishEngulfing_IsTheMirror()
    {
        var prev = Bar(108, 112.5, 107.5, 112, 1);
        var cur = Bar(113, 113.5, 106.5, 107, 2);
        Assert.Equal(CandlePattern.BearishEngulfing,
            A.Analyze(cur, prev, null, new List<Ohlcv> { prev, cur }).Pattern);
    }

    [Fact]
    public void PiercingLine_RequiresAnOpenBelowThePreviousLow()
    {
        var prev = Bar(120, 120.5, 108, 108.5, 1);           // midpoint 114.25
        var cur = Bar(107, 116, 106.5, 115.5, 2);            // opens below prev.Low, closes past mid
        Assert.Equal(CandlePattern.PiercingLine,
            A.Analyze(cur, prev, null, new List<Ohlcv> { prev, cur }).Pattern);

        var noGap = Bar(110, 116, 109.5, 115.5, 2);          // opens inside the previous range
        Assert.NotEqual(CandlePattern.PiercingLine,
            A.Analyze(noGap, prev, null, new List<Ohlcv> { prev, noGap }).Pattern);
    }

    [Fact]
    public void Doji_IsNeutralAndCarriesNoDirection()
    {
        var r = A.Analyze(Bar(100, 104, 96, 100.05));
        Assert.Equal(CandleDirection.Neutral, r.Direction);
        Assert.Equal(CandleType.LongLeggedDoji, r.Type);
    }

    [Fact]
    public void ZeroRangeBar_DoesNotDivideByZeroOrThrow()
    {
        var flat = Bar(100, 100, 100, 100);
        var r = A.Analyze(flat, flat, flat, new List<Ohlcv> { flat, flat, flat });
        Assert.False(double.IsNaN(r.BodyPercent));
        Assert.False(double.IsInfinity(r.BodyPercent));
    }

    // ── Structural properties ──────────────────────────────────────────────────

    /// <summary>
    /// The same bar, classified twice: once with history ending on it, once with the whole loaded
    /// series in hand. It must be the same candle both times.
    ///
    /// <para>
    /// The trend that separates a hammer from a hanging man was measured from the END of the
    /// <c>recent</c> list on the assumption that callers always pass a list ending at the bar being
    /// classified. Every caller does today. But the assumption is invisible at the call site, and
    /// when it breaks the analyzer does not fail — it measures the trend from bars after the one it
    /// is describing and announces the opposite direction, confidently, to someone who has no other
    /// channel. The bar's position is now found from <c>previous</c> instead of assumed.
    /// </para>
    /// </summary>
    [Fact]
    public void AHistoricalBarIsClassifiedTheSameWayWhateverElseTheCallerHandsOver()
    {
        var history = Downtrend(8);
        var hammer = Bar(100, 100.6, 94, 100.4, 8);          // hammer ending a decline
        history.Add(hammer);

        // What happened next: a rally. Enough of one to reverse the trend measurement.
        var later = new List<Ohlcv>(history);
        for (int i = 1; i <= 6; i++) later.Add(Bar(100 + i * 3, 103 + i * 3, 99 + i * 3, 102 + i * 3, 8 + i));

        var atTheTime = A.Analyze(hammer, history[^2], history[^3], history);
        var inHindsight = A.Analyze(hammer, history[^2], history[^3], later);

        Assert.Equal(CandleType.Hammer, atTheTime.Type);
        Assert.Equal(atTheTime.Type, inHindsight.Type);
        Assert.Equal(atTheTime.Direction, inHindsight.Direction);
        Assert.Equal(atTheTime.IsReversal, inHindsight.IsReversal);
    }

    /// <summary>
    /// The analyzer must only ever read bars at or before the one it is classifying. A candlestick
    /// pattern that needed a later bar to confirm would be undetectable live, which is the whole
    /// point of announcing it.
    ///
    /// <para>
    /// This could not fail until 2026-08-21. The hindsight list was built as
    /// <c>bars.Take(i + 6).Take(i + 1)</c>, and chained LINQ <c>Take</c> takes the MINIMUM — so it
    /// was exactly <c>Take(i + 1)</c>, the same list as "at the time", and the test compared a value
    /// to itself. The comment above stated the invariant correctly the whole time.
    /// </para>
    /// </summary>
    [Fact]
    public void ClassificationOfABarNeverChangesWhenLaterBarsArrive()
    {
        var rng = new Random(99);
        var bars = new List<Ohlcv>();
        double px = 100;
        for (int i = 0; i < 200; i++)
        {
            double o = px, c = px * Math.Exp((rng.NextDouble() - 0.5) * 0.05);
            bars.Add(Bar(o, Math.Max(o, c) * 1.005, Math.Min(o, c) * 0.995, c, i));
            px = c;
        }

        for (int i = 3; i < bars.Count - 5; i++)
        {
            var atTheTime = A.Analyze(bars[i], bars[i - 1], bars[i - 2], bars.Take(i + 1).ToList());
            var withHindsight = A.Analyze(bars[i], bars[i - 1], bars[i - 2], bars.Take(i + 6).ToList());
            Assert.Equal(atTheTime.Pattern, withHindsight.Pattern);
            Assert.Equal(atTheTime.Type, withHindsight.Type);
        }
    }
}
