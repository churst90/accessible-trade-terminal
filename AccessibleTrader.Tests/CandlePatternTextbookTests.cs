using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// THE TEXTBOOK RULES, 2026-11-04.
///
/// <para>
/// Cody: "address the candle pattern problem and have it state the direction reversal like
/// bullish reversal or bearish reversal ... make it more textbook and accurate". An audit of 1,000
/// Bitstamp BTCUSDT hours had found four ways the analyser named shapes the books would not:
/// reversals with nothing to reverse (138 of 297), bars that never traded called dojis (236 of
/// 242), size judged only against the bar's own range (a $2 bar with a $2 body a marubozu, lows
/// $70 apart a tweezer), and three inside up confirming on a close barely above a small body.
/// Each rule below is pinned both ways: the shape the books name is named, and the near-miss the
/// app used to name is not.
/// </para>
///
/// <para>
/// The trend bars in these fixtures are small and run against the move (a falling series of small
/// green bars), so they cannot join the pattern's first bar in some longer shape.
/// </para>
/// </summary>
public sealed class CandlePatternTextbookTests
{
    private static readonly SdkCandlePatternAnalyzer A = new();

    private static DateTime Day(int i) => new DateTime(2026, 1, 1).AddDays(i);

    /// <summary>`n` bars whose closes fall one point a bar to `end`, each body against the move.</summary>
    private static (double O, double H, double L, double C)[] Fall(int n, double end, double body = 1, double wick = 0.5)
        => Enumerable.Range(0, n).Select(i =>
        {
            double c = end + (n - 1 - i), o = c - body;
            return (o, c + wick, o - wick, c);
        }).ToArray();

    /// <summary>`n` bars whose closes rise one point a bar to `end`, each body against the move.</summary>
    private static (double O, double H, double L, double C)[] Rise(int n, double end, double body = 1, double wick = 0.5)
        => Enumerable.Range(0, n).Select(i =>
        {
            double c = end - (n - 1 - i), o = c + body;
            return (o, o + wick, c - wick, c);
        }).ToArray();

    private static (double O, double H, double L, double C)[] Then(
        (double O, double H, double L, double C)[] before, params (double O, double H, double L, double C)[] pattern)
        => before.Concat(pattern).ToArray();

    private static List<Ohlcv> Bars((double O, double H, double L, double C)[] t)
        => t.Select((b, i) => new Ohlcv(Day(i), b.O, b.H, b.L, b.C, 1000)).ToList();

    private static CandleAnalysis Last((double O, double H, double L, double C)[] t)
    {
        var bars = Bars(t);
        return A.Analyze(bars[^1], bars.Count > 1 ? bars[^2] : null, bars.Count > 2 ? bars[^3] : null, bars);
    }

    // ── The fixtures ─────────────────────────────────────────────────────────────────────────

    private static readonly (double O, double H, double L, double C)[] EngulfingBars =
    {
        (112, 112.5, 107.5, 108),   // bearish
        (107, 116.5, 106.5, 116),   // bullish, opens below its close and closes above its open
    };
    private static readonly (double O, double H, double L, double C)[] EngulfingAfterAFall = Then(Fall(10, 113), EngulfingBars);

    // Near 10,000, where the old tolerance (0.1% of price) was 10 and the bars' average range is 2.
    private static (double O, double H, double L, double C)[] TweezerTop(double secondHigh) => new[]
    {
        (10013.0, 10018.0, 10012.5, 10017.5),     // bullish
        (10017.5, secondHigh, 10014.0, 10014.5),  // bearish, high 10018 or near it
    };

    private static readonly (double O, double H, double L, double C)[] ThreeInsideFirstTwo =
    {
        (120, 121, 104, 105),       // long bearish
        (110, 113, 109, 111.5),     // short bullish body inside it
    };

    private static readonly (double O, double H, double L, double C)[] SoldierBars =
    {
        (100, 110,  99, 109),
        (103, 118, 102, 117),
        (110, 126, 109, 125),
    };

    private static (double O, double H, double L, double C)[] BabyAfterAFall((double O, double H, double L, double C) middle)
        => Then(Fall(10, 111),
            (110, 110.5, 101.5, 102),   // long bearish
            middle,                     // below its low
            (99.8, 108.5, 99.6, 108));  // long bullish, its low above the middle's high

    // ── 1. A reversal needs a trend to reverse ───────────────────────────────────────────────

    [Fact]
    public void AnEngulfingIsAReversalOnlyAfterTheMoveItReverses()
    {
        var after = Last(EngulfingAfterAFall);
        Assert.Equal(CandlePattern.BullishEngulfing, after.Pattern);
        Assert.True(after.IsReversal);

        // The same two bars at the top of a rise reverse nothing. Until 2026-11-04 they were a
        // bullish engulfing anyway: Cody's 2026-11-03 18:00 bar came after three rising closes.
        var atTheTop = Last(Then(Rise(10, 111), EngulfingBars));
        Assert.Equal(CandlePattern.None, atTheTop.Pattern);
        Assert.False(atTheTop.IsReversal);

        // With no bars before them there is no trend to read, and no reversal is claimed.
        Assert.Equal(CandlePattern.None, Last(EngulfingBars).Pattern);
    }

    [Fact]
    public void ATweezerTopAfterAFallIsNotOne()
    {
        Assert.Equal(CandlePattern.TweezerTop, Last(Then(Rise(10, 10012.5), TweezerTop(10018.05))).Pattern);
        Assert.Equal(CandlePattern.None, Last(Then(Fall(10, 10012.5), TweezerTop(10018.05))).Pattern);
    }

    [Fact]
    public void SoldiersAreAReversal_AfterAFall()
    {
        // Nison's advancing white soldiers end a decline; Bulkowski, TA-Lib and Investopedia file
        // them with the reversals. This app called them a continuation and asked for no trend.
        var r = Last(Then(Fall(10, 101), SoldierBars));
        Assert.Equal(CandlePattern.ThreeWhiteSoldiers, r.Pattern);
        Assert.True(r.IsReversal);
        Assert.False(r.IsContinuation);
        Assert.Equal("bullish reversal", CandlePatternSpeech.Lean(r));

        Assert.Equal(CandlePattern.None, Last(Then(Rise(10, 99), SoldierBars)).Pattern);
    }

    // ── 2. A bar that never moved is "No movement", and in no pattern ────────────────────────

    [Fact]
    public void ABarWithNoRangeIsNoMovement_NotADoji()
    {
        var r = Last(new[] { (100d, 100d, 100d, 100d) });
        Assert.Equal(CandleType.NoMovement, r.Type);
        Assert.Equal(CandleDirection.Neutral, r.Direction);
        Assert.Equal(CandlePattern.None, r.Pattern);
        Assert.Equal("No movement", CandlePatternSpeech.WithLean(r));
    }

    [Fact]
    public void ABarWithNoRangeCannotBeTheMiddleOfAPattern()
    {
        // The control: a real doji below the first bar's low, the third bar's low above the
        // doji's high. That is a bullish abandoned baby.
        Assert.Equal(CandlePattern.AbandonedBabyBullish,
            Last(BabyAfterAFall((99.2, 99.5, 98.5, 99.2))).Pattern);

        // An hour with no trades in the same place was a "doji" too, so 9 of the 10 abandoned
        // babies in 1,000 Bitstamp hours had an empty middle. Now it ends every pattern through it.
        Assert.Equal(CandlePattern.None, Last(BabyAfterAFall((99, 99, 99, 99))).Pattern);
    }

    [Fact]
    public void ABarWithNoRangeCannotBeTheFirstBarOfAPattern()
    {
        // The abandoned baby and the stars are also refused by their doji and small-body tests,
        // which need a range. An engulfing is not: a flat bar has a body of nothing, and its close
        // equals its open, so it read as a tiny green bar that any red bar after a rise engulfs.
        var red = (100.5, 101, 95, 95.5);

        Assert.Equal(CandlePattern.BearishEngulfing,
            Last(Then(Rise(10, 100), (100, 100.4, 99.9, 100.3), red)).Pattern);
        Assert.Equal(CandlePattern.None,
            Last(Then(Rise(10, 100), (100, 100, 100, 100), red)).Pattern);
    }

    // ── 3. Size is judged against the bars before ────────────────────────────────────────────

    [Fact]
    public void AMarubozuMustBeLongAgainstTheBarsBefore()
    {
        var tiny = (120d, 120.5d, 120d, 120.5d);   // no wicks, but a body of 0.5

        Assert.Equal(CandleType.Normal, Last(Then(Fall(10, 120, body: 10, wick: 1), tiny)).Type);
        Assert.Equal(CandleType.MarubozuBullish, Last(Then(Fall(10, 120, body: 0.3, wick: 0.1), tiny)).Type);

        // With nothing to compare against it is judged by its own range, as before.
        Assert.Equal(CandleType.MarubozuBullish, Last(new[] { tiny }).Type);
    }

    [Fact]
    public void SilentHoursDoNotMakeTheNextBarLong()
    {
        // Bars that never moved are skipped by the average, not counted as zero, and the average
        // reaches back past them to bars that did move. Counted, ten silent hours average a body
        // of 0 and the next half-point bar is "long"; skipped but bounded to the last ten slots,
        // there is nothing left to compare with and it is judged by its own range, and is again.
        var flat = Enumerable.Repeat((120d, 120d, 120d, 120d), 10).ToArray();
        var tiny = (120d, 120.5d, 120d, 120.5d);

        Assert.Equal(CandleType.Normal, Last(Then(Then(Fall(5, 120, body: 10, wick: 1), flat), tiny)).Type);
    }

    [Fact]
    public void EqualHighsAreEqualAgainstTheBarsRange_NotThePrice()
    {
        // The bars before have a range of 2, so "equal" is within 0.1. Highs half a point apart
        // were equal under 0.1% of the price, which near 10,000 is 10.
        Assert.Equal(CandlePattern.TweezerTop, Last(Then(Rise(10, 10012.5), TweezerTop(10018.05))).Pattern);
        Assert.Equal(CandlePattern.None, Last(Then(Rise(10, 10012.5), TweezerTop(10018.5))).Pattern);
    }

    // ── 4. Three inside up confirms above the first bar's open ───────────────────────────────

    [Fact]
    public void ThreeInsideUpConfirmsAboveTheFirstBarsOpen()
    {
        var before = Then(Fall(10, 121), ThreeInsideFirstTwo);

        Assert.Equal(CandlePattern.ThreeInsideUp, Last(Then(before, (112, 123, 111, 122))).Pattern);

        // Above the small middle body (111.5) but still below where the long bar opened (120):
        // the harami's stall has not resolved upward yet. This confirmed until 2026-11-04.
        Assert.Equal(CandlePattern.None, Last(Then(before, (112, 119, 111, 118))).Pattern);
    }

    // ── The lean ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryNamedFixtureLeansTheWayItsNameSays()
    {
        // Two sources for one fact: Lean reads the analysis (what the bar's own reading says), and
        // PatternLean reads the name (what the membership clause on the other bars says). They
        // must agree or bar 1 and bar 2 of one pattern would say different things.
        var named = new[]
        {
            Last(EngulfingAfterAFall),
            Last(Then(Rise(10, 10012.5), TweezerTop(10018.05))),
            Last(Then(Fall(10, 101), SoldierBars)),
            Last(BabyAfterAFall((99.2, 99.5, 98.5, 99.2))),
            Last(Then(Then(Fall(10, 121), ThreeInsideFirstTwo), (112, 123, 111, 122))),
        };

        foreach (var r in named)
        {
            Assert.NotEqual(CandlePattern.None, r.Pattern);
            Assert.Equal(CandlePatternSpeech.PatternLean(r.Pattern), CandlePatternSpeech.Lean(r));
        }
        Assert.Equal("bearish reversal", CandlePatternSpeech.Lean(named[1]));
    }

    // ── Every route says the lean ────────────────────────────────────────────────────────────

    private const int Engulfing = 11;   // index of the engulfing bar in EngulfingAfterAFall

    [Fact]
    public void TheArrowKeysAndTheDetailKeySayTheLean()
    {
        const string said = "Bullish engulfing, bullish reversal, bar 2 of 2.";
        Assert.StartsWith(said, CandlePatternOnePathTests.ArrowKeyReading(EngulfingAfterAFall, Engulfing));
        Assert.Contains(said, CandlePatternOnePathTests.DetailKeyReading(EngulfingAfterAFall, Engulfing));

        // And its first bar names the pattern it starts, with the same lean.
        Assert.Contains("bar 1 of 2, Bullish engulfing, bullish reversal",
            CandlePatternOnePathTests.ArrowKeyReading(EngulfingAfterAFall, Engulfing - 1));
    }

    [Fact]
    public void TheIndicatorDetailFactSaysTheLean()
    {
        var bars = Bars(EngulfingAfterAFall).ToArray();
        string fact = new CoreIndicatorProvider().GetDetailFact(
            "CANDLES", bars, new Dictionary<string, double[]>(), Engulfing, new());
        Assert.StartsWith("Bullish engulfing, bullish reversal.", fact);
    }

    [Fact]
    public void TheBarCloseSaysTheLean()
    {
        var rig = new Rig();
        var bars = Bars(EngulfingAfterAFall);
        var opened = new Ohlcv(Day(bars.Count), 116, 117, 115, 116.5, 1000);
        rig.Store.EmitState(rig.State(bars.Append(opened).ToList()));

        rig.Bus.Publish(new NewBarEvent(bars[Engulfing], opened));

        Assert.Contains(rig.Spoken, s => s.Contains("Close 116.00") && s.Contains(", Bullish engulfing, bullish reversal."));
    }

    [Fact]
    public void TheFormingBarSaysTheLean()
    {
        var rig = new Rig();
        var bars = Bars(EngulfingAfterAFall);
        rig.Store.EmitState(rig.State(bars.Take(Engulfing).ToList()));

        rig.Bus.Publish(new IntraBarUpdateEvent(bars[Engulfing], bars[Engulfing - 1], bars[Engulfing - 2]));

        Assert.Contains("Bullish engulfing forming, bullish reversal", rig.Spoken);
    }

    [Fact]
    public void AFormingBarThatHasNotMovedSaysNothing()
    {
        // Every bar's first tick has its high at its low. "No movement forming" on each would be
        // a sentence an hour that means nothing.
        var rig = new Rig();
        var bars = Bars(EngulfingAfterAFall);
        rig.Store.EmitState(rig.State(bars.Take(Engulfing).ToList()));

        rig.Spoken.Clear();   // "Chart ready."; TheFormingBarSaysTheLean is the same rig speaking

        var firstTick = new Ohlcv(Day(Engulfing), 108, 108, 108, 108, 0);
        rig.Bus.Publish(new IntraBarUpdateEvent(firstTick, bars[Engulfing - 1], bars[Engulfing - 2]));

        Assert.Empty(rig.Spoken);
    }

    private sealed class Rig
    {
        public MockWorkspaceStore Store { get; } = new();
        public SpyEventBus Bus { get; } = new();
        public List<string> Spoken { get; } = new();
        private readonly ChartSeries _candles;

        public Rig()
        {
            var speech = new CounterSpeechManager();
            speech.OnSpeak = t => Spoken.Add(t);
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);

            var cfg = new SeriesConfig
            {
                Id = CoreSeriesIds.Candles, Name = "Candles", IndicatorCode = "CANDLES",
                Pane = "Main", IsAutoNarrated = true,
            };
            _candles = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });

            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
        }

        public WorkspaceState State(List<Ohlcv> bars) => WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars),
            CurrentDataIndex = bars.Count - 1,
            ActiveSeries = ImmutableList.Create(_candles),
            PrimarySeriesId = CoreSeriesIds.Candles,
            InitStatus = InitializationStatus.Ready,
            DataStatus = DataStatus.Ready,
            IsSpeechEnabled = true,
            AnnounceNewBars = true,
            DescribeCandlePatterns = true,
        };
    }
}
