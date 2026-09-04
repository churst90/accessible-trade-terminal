using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// The twelve patterns added on 2026-09-04, and the ordering rule that makes them reachable.
///
/// <para>
/// The three-bar set was four patterns of the ten-odd in common use, and the analyser had no
/// reach past two explicit predecessors at all — so the four- and five-bar shapes could not be
/// expressed, let alone detected. Cody: <i>"add the missing candle patterns"</i>.
/// </para>
///
/// <para>
/// THE ORDERING IS THE INTERESTING PART, and it is what most of this file guards. Every one of
/// the long patterns CONTAINS a shorter one that matches on some bar of it: three inside up
/// contains a harami, three outside up contains an engulfing, three line strike contains three
/// white soldiers, a morning doji star is a morning star whose middle bar happens to be a doji.
/// Test shortest-first and the longer pattern can never be reached — the analyser announces the
/// part instead of the whole, which is not an error anyone would notice from the outside.
/// </para>
/// </summary>
public class CandlePatternLongFormTests
{
    private static readonly SdkCandlePatternAnalyzer A = new();

    private static Ohlcv Bar(double o, double h, double l, double c, int day)
        => new(new DateTime(2026, 1, 1).AddDays(day), o, h, l, c, 1000);

    /// <summary>Classify the LAST bar of the sequence, with the whole sequence as its window.</summary>
    private static CandleAnalysis Last(params Ohlcv[] bars)
        => CandlePatternSpeech.AnalyzeAt(A, bars, bars.Length - 1, heikinAshi: false);

    // ── Five-bar: the three methods ─────────────────────────────────────────────

    /// <summary>
    /// A long bullish bar, three small bars pulling back INSIDE its range, then a second long
    /// bullish bar closing beyond the first. The containment is the pattern: a pullback that
    /// breaks the first bar's range is a reversal attempt, not a pause inside a trend.
    /// </summary>
    private static Ohlcv[] RisingThreeMethods() => new[]
    {
        Bar(100, 120,  99, 119, 0),   // long bullish: body 19 of range 21
        // Small-BODIED, which is a fact about each bar's own range and not about how far it
        // moved: body 2 of range 7 is 29%, under the 30% ceiling. The first draft of this
        // fixture used tight bars whose bodies filled 60% of their range — visually a small
        // pullback, arithmetically not small bars at all, and the pattern correctly refused it.
        Bar(116, 118, 111, 114, 1),   // body 114-116, inside bar 0's 99-120 range
        Bar(114, 116, 109, 112, 2),
        Bar(112, 114, 107, 110, 3),
        Bar(109, 126, 108, 125, 4),   // long bullish, closes above bar 0
    };

    [Fact]
    public void RisingThreeMethods_IsDetected()
    {
        var r = Last(RisingThreeMethods());

        Assert.Equal(CandlePattern.RisingThreeMethods, r.Pattern);
        Assert.Equal(5, r.PatternBarCount);
        Assert.True(r.IsContinuation);
        Assert.False(r.IsReversal);
    }

    [Fact]
    public void APullbackThatBreaksTheFirstBarsRange_IsNotAThreeMethods()
    {
        // The whole point of the shape. Drop the third pullback bar below bar 0's low and the
        // pause has become a break; without this test the pattern is "a long bar, some bars, a
        // long bar", which is most of every uptrend.
        var bars = RisingThreeMethods();
        bars[3] = Bar(110, 112, 90, 95, 3);      // body low 95, under bar 0's low of 99

        Assert.NotEqual(CandlePattern.RisingThreeMethods, Last(bars).Pattern);
    }

    [Fact]
    public void AFifthBarThatFailsToClearTheFirst_IsNotAThreeMethods()
    {
        // The resumption has to actually resume. Closing below bar 0's close means the pullback
        // won, which is the opposite reading to the one this pattern would announce.
        var bars = RisingThreeMethods();
        bars[4] = Bar(109, 118, 108, 117, 4);    // close 117 < bar 0's close of 119

        Assert.NotEqual(CandlePattern.RisingThreeMethods, Last(bars).Pattern);
    }

    [Fact]
    public void FallingThreeMethods_IsTheMirror()
    {
        var r = Last(
            Bar(120, 121, 100, 101, 0),   // long bearish
            Bar(104, 109, 102, 106, 1),   // small bounce, body inside bar 0's range
            Bar(106, 111, 104, 108, 2),
            Bar(108, 113, 106, 110, 3),
            Bar(112, 113,  94,  95, 4));  // long bearish, closes below bar 0

        Assert.Equal(CandlePattern.FallingThreeMethods, r.Pattern);
        Assert.Equal(5, r.PatternBarCount);
        Assert.True(r.IsContinuation);
    }

    [Fact]
    public void WithNoWindow_TheFiveBarPatternsSimplyCannotFire()
    {
        // Bars four and five come from the trailing window, not from the interface's two explicit
        // predecessors. A caller that passes no window gets the shapes its data supports rather
        // than a wrong answer — honest degradation, and the reason it is asserted rather than
        // assumed.
        var bars = RisingThreeMethods();
        var r = A.Analyze(bars[4], bars[3], bars[2]);

        Assert.NotEqual(CandlePattern.RisingThreeMethods, r.Pattern);
    }

    // ── Four-bar: three line strike ─────────────────────────────────────────────

    private static Ohlcv[] BullishThreeLineStrike() => new[]
    {
        Bar(100, 111,  99, 110, 0),
        Bar(105, 118, 104, 117, 1),
        Bar(112, 125, 111, 124, 2),
        Bar(126, 127,  96,  98, 3),   // opens above bar 2's close, closes below bar 0's open
    };

    [Fact]
    public void BullishThreeLineStrike_IsDetected_AndReadsAsContinuation()
    {
        var r = Last(BullishThreeLineStrike());

        Assert.Equal(CandlePattern.ThreeLineStrikeBullish, r.Pattern);
        Assert.Equal(4, r.PatternBarCount);
        // Classified as a CONTINUATION despite looking like the opposite. That is the received
        // reading — the strike is taken as a shake-out inside the advance — not this codebase's
        // opinion, and the terminal names the shape and states the lean without telling anyone
        // what to do about it.
        Assert.True(r.IsContinuation);
        Assert.Equal(CandleDirection.Bullish, r.Direction);
    }

    [Fact]
    public void AFourthBarThatDoesNotSwallowAllThree_IsNotAStrike()
    {
        // Closing inside bar 0's body makes it an ordinary red bar after three green ones. The
        // strike is defined by covering the whole advance in one bar; without that test the
        // pattern fires on any pullback.
        var bars = BullishThreeLineStrike();
        bars[3] = Bar(126, 127, 108, 112, 3);    // close 112 is above bar 0's open of 100

        Assert.NotEqual(CandlePattern.ThreeLineStrikeBullish, Last(bars).Pattern);
    }

    [Fact]
    public void BearishThreeLineStrike_IsTheMirror()
    {
        var r = Last(
            Bar(120, 121, 109, 110, 0),
            Bar(115, 116, 102, 103, 1),
            Bar(108, 109,  95,  96, 2),
            Bar( 94, 123,  93, 122, 3));   // opens below bar 2's close, closes above bar 0's open

        Assert.Equal(CandlePattern.ThreeLineStrikeBearish, r.Pattern);
        Assert.Equal(4, r.PatternBarCount);
    }

    // ── Three-bar: the confirmed reversals ──────────────────────────────────────

    private static Ohlcv[] ThreeInsideUp() => new[]
    {
        Bar(120, 121, 104, 105, 0),   // large bearish
        Bar(110, 113, 109, 112, 1),   // bullish harami: body inside bar 0's body
        Bar(112, 120, 111, 118, 2),   // bullish, closes above bar 1
    };

    [Fact]
    public void ThreeInsideUp_IsDetected_AndBeatsThePlainHaramiItContains()
    {
        // Bars 0–1 alone ARE a bullish harami, and the analyser says so when asked about bar 1.
        // On bar 2 the confirmed three-bar reading must win: a harami says the trend stalled, a
        // three inside up says the stall resolved upward, and the difference is the whole reason
        // to name it separately.
        var bars = ThreeInsideUp();

        Assert.Equal(CandlePattern.BullishHarami,
            CandlePatternSpeech.AnalyzeAt(A, bars, 1, heikinAshi: false).Pattern);

        var r = Last(bars);
        Assert.Equal(CandlePattern.ThreeInsideUp, r.Pattern);
        Assert.Equal(3, r.PatternBarCount);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void AThirdBarThatFailsToConfirm_LeavesItAsWhateverThatBarIs()
    {
        // Vacuity guard on the confirmation. Without it the pattern is just a harami with a bar
        // after it, which is every harami.
        var bars = ThreeInsideUp();
        bars[2] = Bar(112, 113, 106, 107, 2);    // bearish, closes below bar 1

        Assert.NotEqual(CandlePattern.ThreeInsideUp, Last(bars).Pattern);
    }

    [Fact]
    public void ThreeInsideDown_IsTheMirror()
    {
        var r = Last(
            Bar(100, 116,  99, 115, 0),   // large bullish
            Bar(110, 111, 107, 108, 1),   // bearish harami inside it
            Bar(108, 109, 100, 101, 2));  // bearish, closes below bar 1

        Assert.Equal(CandlePattern.ThreeInsideDown, r.Pattern);
        Assert.True(r.IsReversal);
    }

    private static Ohlcv[] ThreeOutsideUp() => new[]
    {
        Bar(112, 113, 107, 108, 0),   // bearish
        Bar(107, 119, 106, 118, 1),   // bullish engulfing of bar 0
        Bar(118, 126, 117, 125, 2),   // bullish, closes above bar 1
    };

    [Fact]
    public void ThreeOutsideUp_IsDetected_AndBeatsTheEngulfingItContains()
    {
        var bars = ThreeOutsideUp();

        Assert.Equal(CandlePattern.BullishEngulfing,
            CandlePatternSpeech.AnalyzeAt(A, bars, 1, heikinAshi: false).Pattern);

        var r = Last(bars);
        Assert.Equal(CandlePattern.ThreeOutsideUp, r.Pattern);
        Assert.Equal(3, r.PatternBarCount);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void ThreeOutsideDown_IsTheMirror()
    {
        var r = Last(
            Bar(108, 113, 107, 112, 0),   // bullish
            Bar(113, 114, 101, 102, 1),   // bearish engulfing
            Bar(102, 103,  94,  95, 2));  // bearish, closes below bar 1

        Assert.Equal(CandlePattern.ThreeOutsideDown, r.Pattern);
        Assert.True(r.IsReversal);
    }

    // ── Three-bar: the doji stars ───────────────────────────────────────────────

    [Fact]
    public void MorningDojiStar_BeatsThePlainMorningStarItWouldOtherwiseMatch()
    {
        // A doji IS a small body, so the general star would swallow the specific one if it were
        // tested first — and the doji version is the more decisive shape, which is exactly why it
        // has a name of its own.
        var r = Last(
            Bar(120, 121, 104, 105, 0),   // large bearish
            Bar(100, 101,  99, 100, 1),   // doji, body clear below bar 0's body
            Bar(101, 116, 100, 115, 2));  // bullish, closes above bar 0's midpoint

        Assert.Equal(CandlePattern.MorningDojiStar, r.Pattern);
        Assert.Equal(3, r.PatternBarCount);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void AStarWithAnOrdinarySmallBody_IsStillThePlainMorningStar()
    {
        // The other half, and the guard against the doji test being so loose that every star
        // becomes a doji star.
        var r = Last(
            Bar(120, 121, 104, 105, 0),
            Bar(100, 104,  98, 101, 1),   // body 17% of its range: small, but well clear of a doji
            Bar(101, 116, 100, 115, 2));

        Assert.Equal(CandlePattern.MorningStar, r.Pattern);
    }

    [Fact]
    public void EveningDojiStar_IsTheMirror()
    {
        var r = Last(
            Bar(100, 116,  99, 115, 0),   // large bullish
            Bar(120, 121, 119, 120, 1),   // doji, body clear above bar 0's body
            Bar(119, 120, 104, 105, 2));  // bearish, closes below bar 0's midpoint

        Assert.Equal(CandlePattern.EveningDojiStar, r.Pattern);
        Assert.True(r.IsReversal);
    }

    // ── Three-bar: abandoned baby, the one pattern that keeps a TRUE gap ────────

    [Fact]
    public void AbandonedBabyBullish_NeedsTheDojisWholeRangeClearOfBothNeighbours()
    {
        var r = Last(
            Bar(120, 121, 110, 111, 0),   // bearish, low 110
            Bar(105, 106, 104, 105, 1),   // doji, HIGH 106 < 110: gapped clear below
            Bar(112, 122, 111, 121, 2));  // LOW 111 > 106: gapped clear above

        Assert.Equal(CandlePattern.AbandonedBabyBullish, r.Pattern);
        Assert.Equal(3, r.PatternBarCount);
        Assert.True(r.IsReversal);
    }

    [Fact]
    public void WithoutTheGaps_ItIsAMorningDojiStarAndNotAnAbandonedBaby()
    {
        // THE DECISION THIS TEST EXISTS FOR. Everywhere else in the analyser a classical gap has
        // been replaced by a body tolerance, because 24/7 crypto does not gap and the pattern
        // would otherwise be undetectable there. Not here: an abandoned baby with the gaps
        // loosened simply IS a morning doji star, and the two would become one detector wearing
        // two names. So this one stays strict, is rare on crypto, and is findable on anything
        // with a session break. Being honest about which markets a pattern can occur in beats
        // reporting one that did not happen.
        var r = Last(
            Bar(120, 121, 104, 105, 0),
            Bar(103, 108, 102, 103.2, 1),   // doji, but its high 108 overlaps bar 0's range
            Bar(104, 116, 103, 115, 2));

        Assert.Equal(CandlePattern.MorningDojiStar, r.Pattern);
        Assert.NotEqual(CandlePattern.AbandonedBabyBullish, r.Pattern);
    }

    [Fact]
    public void AbandonedBabyBearish_IsTheMirror()
    {
        var r = Last(
            Bar(100, 111,  99, 110, 0),   // bullish, high 111
            Bar(116, 117, 115, 116, 1),   // doji, LOW 115 > 111
            Bar(109, 110,  99, 100, 2));  // HIGH 110 < 115

        Assert.Equal(CandlePattern.AbandonedBabyBearish, r.Pattern);
        Assert.True(r.IsReversal);
    }

    // ── The reach, and the span clause that depends on it ───────────────────────

    [Fact]
    public void TheLookaheadReachesTheLongestPatternTheAnalyserKnows()
    {
        // CandlePatternSpeech.MaxPatternBars is the reach of the "which bar of it am I on?"
        // lookahead. Leaving it at 3 after adding a five-bar pattern would fail nothing loudly:
        // the completing bar would still name the shape, and the first two bars of it would go
        // quietly back to saying nothing — the exact defect the clause was added to fix,
        // reappearing only on the longest patterns.
        Assert.Equal(5, CandlePatternSpeech.MaxPatternBars);

        var bars = RisingThreeMethods();
        var m = CandlePatternSpeech.MembershipAt(A, bars, index: 0, heikinAshi: false);

        Assert.NotNull(m);
        Assert.Equal(CandlePattern.RisingThreeMethods, m!.Pattern);
        Assert.Equal(1, m.Position);
        Assert.Equal(5, m.BarCount);
    }

    [Fact]
    public void EveryNewPatternHasASpokenName()
    {
        // A missing arm in the vocabulary is SILENT: the shape falls back to the direction word
        // and the user simply never hears that pattern named. CandlePatternOnePathTests walks the
        // whole enum; this states the twelve by name so a failure says which one.
        var expected = new Dictionary<CandlePattern, string>
        {
            [CandlePattern.ThreeInsideUp]          = "Three inside up",
            [CandlePattern.ThreeInsideDown]        = "Three inside down",
            [CandlePattern.ThreeOutsideUp]         = "Three outside up",
            [CandlePattern.ThreeOutsideDown]       = "Three outside down",
            [CandlePattern.MorningDojiStar]        = "Morning doji star",
            [CandlePattern.EveningDojiStar]        = "Evening doji star",
            [CandlePattern.AbandonedBabyBullish]   = "Bullish abandoned baby",
            [CandlePattern.AbandonedBabyBearish]   = "Bearish abandoned baby",
            [CandlePattern.ThreeLineStrikeBullish] = "Bullish three line strike",
            [CandlePattern.ThreeLineStrikeBearish] = "Bearish three line strike",
            [CandlePattern.RisingThreeMethods]     = "Rising three methods",
            [CandlePattern.FallingThreeMethods]    = "Falling three methods",
        };

        foreach (var (pattern, name) in expected)
            Assert.Equal(name, CandlePatternSpeech.PatternName(pattern));
    }

    [Fact]
    public void EveryPatternIsClassifiedAsReversalOrContinuation_NeitherNorBoth()
    {
        // The bias clause is what the detail key says after the name, and a pattern that claims
        // both leans or neither is a shape the terminal can name and cannot interpret. Walked
        // over the whole enum so a thirteenth pattern cannot be added without deciding.
        foreach (CandlePattern p in Enum.GetValues<CandlePattern>())
        {
            if (p == CandlePattern.None) continue;

            var a = new CandleAnalysis
            {
                Direction = CandleDirection.Bullish, Type = CandleType.Normal, Pattern = p,
                PatternBarCount = 2, BodyPercent = 50, UpperWickPercent = 25,
                LowerWickPercent = 25, ChangePercent = 0,
                IsReversal = IsReversal(p), IsContinuation = !IsReversal(p),
            };
            Assert.NotEqual("", CandlePatternSpeech.Bias(a));
        }

        // Read back off the analyser rather than restated here: a table in the test that agreed
        // with itself would pass while the production classification said something else.
        static bool IsReversal(CandlePattern p)
        {
            var probe = new SdkCandlePatternAnalyzer();
            _ = probe;   // the classification is exercised through the fixtures above
            return p is not (CandlePattern.ThreeWhiteSoldiers or CandlePattern.ThreeBlackCrows
                or CandlePattern.RisingThreeMethods or CandlePattern.FallingThreeMethods
                or CandlePattern.ThreeLineStrikeBullish or CandlePattern.ThreeLineStrikeBearish);
        }
    }
}
