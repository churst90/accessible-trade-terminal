using System.Collections.Immutable;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// ONE PATH for candle-pattern detail, 2026-09-04.
///
/// <para>
/// Four routes name a candle: the bar that just closed, the live bar as it forms, the arrow keys
/// reading history, and the detail key (Ctrl+Shift+D / Alt+Shift+D). Until this pass the first two
/// used <see cref="SdkCandlePatternAnalyzer"/> and its twelve patterns, and the last two each
/// carried a private single-bar classifier with its own thresholds. So the twelve multi-bar
/// patterns were audible ONLY while you were listening live, and on the shapes the copies did know
/// they could still disagree with the live announcement about the same bar.
/// </para>
///
/// <para>
/// These tests are about the JOIN, not about the analyser — <c>CandlePatternAnalyzerTests</c>
/// already pins what each pattern is. What is guarded here is that the four routes cannot drift
/// apart again: the same bar, described by two routes, comes back with the same name.
/// </para>
/// </summary>
public sealed class CandlePatternOnePathTests
{
    // ── Three white soldiers: large green bodies, each opening inside the previous body ─────
    private static readonly (double O, double H, double L, double C)[] ThreeSoldiers =
    {
        (100d, 101d,  99d, 100d),
        (100d, 110d,  99d, 109d),
        (103d, 118d, 102d, 117d),
        (110d, 126d, 109d, 125d),
    };

    // A bar with nothing to say about itself: body 46% of range, neither wick dominant, and
    // deliberately not part of anything either. Getting this fixture wrong is instructive — the
    // obvious "three ordinary green bars" is THREE WHITE SOLDIERS, which is how the first attempt
    // at this vacuity guard came back naming a pattern.
    private static readonly (double O, double H, double L, double C)[] Ordinary =
    {
        (100d, 110d,  98d, 107d),
        (107d, 112d, 100d, 102d),
        (103d, 112d, 101d, 108d),
    };

    // ── The arrow keys ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ArrowKeys_NameAMultiBarPattern()
    {
        // The gap this pass closed. Scanning history with Page Up / Page Down onto the candles
        // and stepping left and right, the reading was "Bullish. Close … Open …" on the last bar
        // of a three-white-soldiers advance — the terminal had detected the pattern (the alert
        // engine could fire on it) and simply had no route that would say it to someone reading
        // the past, which is nearly all of the reading anyone does.
        string msg = ArrowKeyReading(ThreeSoldiers, idx: 3);

        Assert.StartsWith("Three white soldiers.", msg);
        Assert.Contains("Close 125.00", msg);
    }

    [Fact]
    public void ArrowKeys_StillReadAnOrdinaryBarAsJustItsDirection()
    {
        // Vacuity guard. If every bar acquired a pattern name the feature would be noise, and the
        // test above would pass no matter what the classifier did. An unremarkable candle reads
        // exactly as it always has.
        string msg = ArrowKeyReading(Ordinary, idx: 2);

        Assert.StartsWith("Bullish.", msg);
    }

    [Fact]
    public void ArrowKeys_TurningCandlePatternsOff_DropsTheNameAndKeepsTheDirection()
    {
        // "Describe candle patterns" is the switch Cody added on 2026-09-04 for the two live
        // routes; the arrow keys join it here. What it must NOT take away is the direction word:
        // "Bullish" is a fact about the bar rather than a pattern claim, it has led this sentence
        // since the beginning, and without it the reading opens on a price.
        string msg = ArrowKeyReading(ThreeSoldiers, idx: 3, describeCandlePatterns: false);

        Assert.StartsWith("Bullish.", msg);
        Assert.DoesNotContain("Three white soldiers", msg);
        Assert.Contains("Close 125.00", msg);
    }

    [Fact]
    public void ArrowKeys_ANamedShapeIsNotPrefixedWithADirection()
    {
        // The old classifier's caller prefixed "Bullish" or "Bearish" from the candle's colour, so
        // a doji — a bar that closed where it opened, whose entire meaning is indecision — was
        // announced as "Bullish Doji" whenever the close rounded up, and the same prefix would
        // have produced "Bullish three white soldiers" once the analyser arrived. A named shape
        // carries its own side; the direction word is for the bars that have no name.
        string msg = ArrowKeyReading(new[]
        {
            (100d, 105d, 95d, 102d),
            (104d, 109d, 99d, 104d),   // open == close, long wicks both sides
        }, idx: 1);

        Assert.StartsWith("Long-legged doji.", msg);
        Assert.DoesNotContain("Bullish", msg);
        Assert.DoesNotContain("Bearish", msg);
    }

    // ── The join: two routes, one bar, one name ─────────────────────────────────────────────

    [Theory]
    [InlineData(3)]   // three white soldiers — a pattern only the analyser ever knew
    [InlineData(2)]
    [InlineData(1)]
    public void TheDetailKeyAndTheArrowKeysNameTheSameBarTheSameWay(int idx)
    {
        // THE GUARD THAT MATTERS. Everything else here can be satisfied by two implementations
        // that happen to agree today; this one fails the moment they stop agreeing, which is what
        // actually happened last time — a 90% body was a marubozu to the arrow keys and an
        // ordinary candle to everything else, and nothing in the suite noticed.
        string arrows = ArrowKeyReading(ThreeSoldiers, idx);
        string detail = DetailKeyReading(ThreeSoldiers, idx);

        string shape = arrows[..arrows.IndexOf('.')];
        Assert.Contains(shape, detail);
    }

    // ── The live forming bar ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFormingBarIsNotHandedToTheAnalyserAsItsOwnPredecessor()
    {
        // WorkspaceStore replaces the live bar IN PLACE and publishes IntraBarUpdateEvent with
        // PreviousBar = the state's previous last bar — which, on an intra-bar tick, is an EARLIER
        // SNAPSHOT OF THE SAME BAR (same timestamp, smaller body) rather than the bar before it.
        // The coordinator passed that field straight through, so a bullish engulfing was tested
        // against a younger version of itself, which a growing body engulfs by construction.
        //
        // The same defect was found and fixed on the bar-close route in an earlier pass. It
        // survived here because the event carried a field with exactly the right name.
        var forming = new Ohlcv(Day(2), 100, 112, 99, 111, 10);
        var analyzer = new RecordingAnalyzer();

        PublishIntraBar(analyzer, forming, history: new[]
        {
            new Ohlcv(Day(0), 100, 106,  99, 105, 10),
            new Ohlcv(Day(1), 105, 110, 104, 109, 10),
            new Ohlcv(Day(2), 100, 104,  99, 103, 10),   // the same bar, one tick ago
        });

        var call = analyzer.Call!.Value;
        Assert.Equal(forming, call.Current);
        Assert.Equal(Day(1), call.Prev!.Value.Date);      // the bar BEFORE it, not itself
        Assert.Equal(Day(0), call.Prev2!.Value.Date);
        Assert.Equal(forming, call.Recent![^1]);
    }

    [Fact]
    public void TheFormingBarStillGetsItsPredecessorWhenTheStoreHasNotAppendedItYet()
    {
        // The other arrangement, and the reason the fix is a date test rather than "always drop
        // the last bar": a caller whose history does NOT already contain the forming bar must not
        // lose a real predecessor to the de-duplication.
        var forming = new Ohlcv(Day(2), 100, 112, 99, 111, 10);
        var analyzer = new RecordingAnalyzer();

        PublishIntraBar(analyzer, forming, history: new[]
        {
            new Ohlcv(Day(0), 100, 106,  99, 105, 10),
            new Ohlcv(Day(1), 105, 110, 104, 109, 10),
        });

        var call = analyzer.Call!.Value;
        Assert.Equal(Day(1), call.Prev!.Value.Date);
        Assert.Equal(Day(0), call.Prev2!.Value.Date);
    }

    // ── Heikin-Ashi ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithHeikinAshiOn_ThePatternIsJudgedOnTheCandlesActuallyDrawn()
    {
        // A Heikin-Ashi candle is a different candle: averaged open and close, routinely shaved of
        // one shadow. The bar readout has described the DRAWN candle since the three-disagreeing-
        // closing-prices fix, but the classification behind it read raw bars, so the terminal could
        // name a shape that is not on screen. The window the analyser sees is transformed as a
        // whole, which is the only way to do it — HA is recursive from bar zero.
        var raw = new[]
        {
            new Ohlcv(Day(0), 100, 106,  99, 105, 10),
            new Ohlcv(Day(1), 105, 110, 104, 109, 10),
            new Ohlcv(Day(2), 109, 115, 108, 114, 10),
        };
        var analyzer = new RecordingAnalyzer();

        CandlePatternSpeech.AnalyzeAt(analyzer, raw, index: 2, heikinAshi: true);

        var call = analyzer.Call!.Value;
        Assert.NotEqual(raw[2], call.Current);                       // transformed, not raw
        Assert.Equal(raw[2].Date, call.Current.Date);                // and still the right bar
        Assert.All(call.Recent!, b => Assert.DoesNotContain(b, raw)); // the whole window, not a mix
    }

    // ── The window ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTrailingWindowIsBoundedAndEndsAtTheClassifiedBar()
    {
        // Every route now assembles context on every keypress, so an unbounded copy of the loaded
        // series would be an O(n) allocation per arrow key on a chart that can hold tens of
        // thousands of bars. The analyser reads three bars plus TrendLookbackBars; the cap is far
        // above both, and the END is what has to be right.
        var bars = Enumerable.Range(0, 500)
            .Select(i => new Ohlcv(Day(i), 100 + i, 101 + i, 99 + i, 100.5 + i, 10)).ToList();
        var analyzer = new RecordingAnalyzer();

        CandlePatternSpeech.AnalyzeAt(analyzer, bars, index: 400, heikinAshi: false);

        var call = analyzer.Call!.Value;
        Assert.Equal(CandlePatternSpeech.ContextBars, call.Recent!.Count);
        Assert.Equal(bars[400], call.Recent[^1]);
        Assert.Equal(bars[399], call.Prev);
    }

    [Fact]
    public void NearTheStartOfTheSeriesTheWindowIsShortRatherThanPadded()
    {
        // A short window is the honest answer on bar two of a fresh load. Padding it — with the
        // first bar repeated, or with zeros — would invent a flat trend and turn every hammer
        // into a hanging man.
        var bars = new[]
        {
            new Ohlcv(Day(0), 100, 106,  99, 105, 10),
            new Ohlcv(Day(1), 105, 110, 104, 109, 10),
        };
        var analyzer = new RecordingAnalyzer();

        CandlePatternSpeech.AnalyzeAt(analyzer, bars, index: 1, heikinAshi: false);

        Assert.Equal(2, analyzer.Call!.Value.Recent!.Count);
        Assert.Null(analyzer.Call!.Value.Prev2);
    }

    // ── The vocabulary ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryPatternAndTypeHasASpokenName()
    {
        // A missing arm in either switch is silent: the shape falls back to the direction word and
        // the user simply never hears that pattern named. Both enums are walked so adding a
        // thirteenth pattern without naming it fails here rather than in the field.
        foreach (CandlePattern p in Enum.GetValues<CandlePattern>())
        {
            if (p == CandlePattern.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(CandlePatternSpeech.PatternName(p)), $"unnamed: {p}");
        }

        foreach (CandleType t in Enum.GetValues<CandleType>())
        {
            if (t == CandleType.Normal) continue;
            Assert.False(string.IsNullOrWhiteSpace(CandlePatternSpeech.TypeName(t)), $"unnamed: {t}");
        }
    }

    [Fact]
    public void ANameThatAlreadyCarriesADirectionIsNotPrefixedWithOne()
    {
        // "Bearish Bearish Marubozu" was a real reported reading. It comes back the moment two
        // places decide independently who says the direction, which is why the decision lives in
        // exactly one function.
        var bearishMarubozu = Analysis(CandleDirection.Bearish, CandleType.MarubozuBearish, CandlePattern.None);
        Assert.Equal("Bearish marubozu", CandlePatternSpeech.DescribeShape(bearishMarubozu));

        var bullishEngulfing = Analysis(CandleDirection.Bullish, CandleType.Normal, CandlePattern.BullishEngulfing);
        Assert.Equal("Bullish engulfing", CandlePatternSpeech.DescribeShape(bullishEngulfing));

        // A shape whose name does not spell the direction out is still not prefixed: hammer and
        // hanging man ARE the direction, which is the entire reason they have two names.
        var hangingMan = Analysis(CandleDirection.Bearish, CandleType.HangingMan, CandlePattern.None);
        Assert.Equal("Hanging man", CandlePatternSpeech.DescribeShape(hangingMan));

        // And a bar with no shape falls back to the direction, which is what it has always said.
        var plain = Analysis(CandleDirection.Bullish, CandleType.Normal, CandlePattern.None);
        Assert.Equal("Bullish", CandlePatternSpeech.DescribeShape(plain));
    }

    [Fact]
    public void TheBiasClauseStatesTheSpanOnlyForMultiBarShapes()
    {
        // The span is the fact a reader of history cannot recover by ear: hearing "morning star"
        // on one bar gives no clue that the two bars behind the cursor are part of it. On a
        // one-bar shape the same words would be noise.
        Assert.Equal("3-bar reversal",
            CandlePatternSpeech.Bias(Analysis(CandleDirection.Bullish, CandleType.Normal,
                CandlePattern.MorningStar, barCount: 3, reversal: true)));

        Assert.Equal("reversal",
            CandlePatternSpeech.Bias(Analysis(CandleDirection.Bullish, CandleType.Hammer,
                CandlePattern.None, barCount: 1, reversal: true)));

        Assert.Equal("",
            CandlePatternSpeech.Bias(Analysis(CandleDirection.Bullish, CandleType.Normal,
                CandlePattern.None)));
    }

    // ── Scaffolding ─────────────────────────────────────────────────────────────────────────

    private static DateTime Day(int i) => new DateTime(2026, 1, 1).AddDays(i);

    private sealed class RecordingAnalyzer : ISdkCandlePatternAnalyzer
    {
        public (Ohlcv Current, Ohlcv? Prev, Ohlcv? Prev2, IReadOnlyList<Ohlcv>? Recent)? Call;
        public CandleAnalysis Analyze(Ohlcv current, Ohlcv? previous = null, Ohlcv? twoBarsAgo = null,
                                      IReadOnlyList<Ohlcv>? recent = null)
        {
            Call = (current, previous, twoBarsAgo, recent);
            return Analysis(CandleDirection.Bullish, CandleType.Normal, CandlePattern.None);
        }
    }

    private static CandleAnalysis Analysis(
        CandleDirection dir, CandleType type, CandlePattern pattern,
        int barCount = 1, bool reversal = false, bool continuation = false) => new()
    {
        Direction = dir, Type = type, Pattern = pattern, PatternBarCount = barCount,
        BodyPercent = 50, UpperWickPercent = 25, LowerWickPercent = 25, ChangePercent = 0,
        IsReversal = reversal, IsContinuation = continuation,
    };

    private static TimeSeriesBuffer<Ohlcv> Buffer((double O, double H, double L, double C)[] bars)
        => new(bars.Select((b, i) => new Ohlcv(Day(i), b.O, b.H, b.L, b.C, 1000)).ToList());

    private static ChartSeries Candles()
    {
        var cfg = new SeriesConfig { Id = CoreSeriesIds.Candles, Name = "Candles", IndicatorCode = "CANDLES", Pane = "Main" };
        cfg.Components.Add(new ComponentConfig { Name = "Body", DisplayName = "Body", IsVisible = true });
        return new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });
    }

    private static WorkspaceState CandleState(
        (double O, double H, double L, double C)[] bars, int idx, bool describeCandlePatterns)
    {
        var series = Candles();
        return WorkspaceState.Initial with
        {
            Data = Buffer(bars),
            CurrentDataIndex = idx,
            ActiveSeries = ImmutableList.Create(series),
            FocusedSeriesId = CoreSeriesIds.Candles,
            PrimarySeriesId = CoreSeriesIds.Candles,
            LastInteractionContext = InteractionContext.Series,
            DescribeCandlePatterns = describeCandlePatterns,
            SpeakTimestamps = false,
            TimestampReadLocation = "None",
            ReadColumnHeaders = false,
            SpeechOrder = "ValueOnly",
        };
    }

    /// <summary>What the series-context arrow-key reading says about the bar at <paramref name="idx"/>.</summary>
    private static string ArrowKeyReading(
        (double O, double H, double L, double C)[] bars, int idx, bool describeCandlePatterns = true)
    {
        var state = CandleState(bars, idx, describeCandlePatterns);
        var series = state.ActiveSeries[0];
        return new SpeechFormatter().FormatPointFeedback(
            state, isXMove: true, isYMove: false, series, state.Data![idx], "");
    }

    /// <summary>What Ctrl+Shift+D says about the same bar.</summary>
    private static string DetailKeyReading((double O, double H, double L, double C)[] bars, int idx)
    {
        var bus = new SpyEventBus();
        new BarDetailService(bus).AnnounceDetails(CandleState(bars, idx, describeCandlePatterns: true));
        return bus.Log.OfType<AnnouncementEvent>().Last().Message;
    }

    private static void PublishIntraBar(RecordingAnalyzer analyzer, Ohlcv forming, Ohlcv[] history)
    {
        var store = new MockWorkspaceStore();
        var bus = new SpyEventBus();
        var speech = new CounterSpeechManager();
        var formatter = new SpeechFormatter();
        var speechRouter = new SpeechFeedbackRouter(speech, formatter, store);

        var cfg = new SeriesConfig
        {
            Id = CoreSeriesIds.Candles, Name = "Candles", IndicatorCode = "CANDLES",
            Pane = "Main", IsAutoNarrated = true,
        };
        var candles = new ChartSeries(cfg, new SeriesDataBuffer { SeriesId = CoreSeriesIds.Candles });

        _ = new AccessibilityFeedbackCoordinator(
            store,
            new NavigationFeedbackManager(speechRouter, formatter),
            speechRouter,
            new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
            formatter,
            bus,
            new MockEarconService(),
            analyzer,
            new NoPatterns(),
            new ChartPatternFocus(),
            new MockAutoNarrationService());

        store.EmitState(WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(history),
            CurrentDataIndex = history.Length - 1,
            ActiveSeries = ImmutableList.Create(candles),
            PrimarySeriesId = CoreSeriesIds.Candles,
            AnnounceNewBars = true,
            DescribeCandlePatterns = true,
        });

        // PreviousBar / TwoBarsAgo are populated exactly as WorkspaceStore populates them on an
        // intra-bar tick — from the PREVIOUS state's last two bars — so that a coordinator reading
        // those fields instead of the series would still look correct here and fail on the asserts.
        bus.Publish(new IntraBarUpdateEvent(
            forming,
            history.Length >= 1 ? history[^1] : null,
            history.Length >= 2 ? history[^2] : null));
    }

    private sealed class NoPatterns : IChartPatternCache
    {
        public IReadOnlyList<ChartPattern> For(ChartIdentity identity, IReadOnlyList<Ohlcv>? bars)
            => Array.Empty<ChartPattern>();
    }
}
