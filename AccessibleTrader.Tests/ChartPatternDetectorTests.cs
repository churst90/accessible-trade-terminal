using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// The opt-in chart-pattern describer.
///
/// <para>
/// Two properties matter more than whether any individual shape is found, because getting either
/// wrong turns an accessibility feature into a misleading one:
/// </para>
/// <list type="number">
///   <item>
///     <b>No lookahead.</b> A pattern must never be reported at a bar earlier than the one at which
///     it could actually have been seen. Panning back through history has to reproduce what was
///     visible at the time, or the feature quietly teaches a false sense of how legible the chart
///     was — the same defect that made the Cipher SR proximity result an artifact.
///   </item>
///   <item>
///     <b>Forming means the trigger has NOT been hit.</b> A "forming" report whose level had already
///     broken would be the exact opposite of the feature's purpose.
///   </item>
/// </list>
/// </summary>
public class ChartPatternDetectorTests
{
    private static readonly ChartPatternDetector Detector = new(new SwingStructureAnalyzer());

    // ── Fixtures ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bars from a close series, each with a small symmetric wick so ATR is non-zero.
    ///
    /// <para>
    /// Open is set equal to close deliberately. The obvious alternative — open = previous close,
    /// high = max(open, close) + wick — produces a TIE between the turning bar and the one after it,
    /// because both share the same extreme. The swing analyzer requires a pivot to STRICTLY dominate
    /// its neighbours (<c>bars[j].High &gt;= bars[i].High</c> disqualifies it), so such a fixture
    /// yields zero pivots and every detection test fails for a reason that has nothing to do with
    /// the detector. Worth the comment: it cost a debugging round.
    /// </para>
    /// </summary>
    private static List<Ohlcv> Bars(IEnumerable<double> closes)
    {
        var list = new List<Ohlcv>();
        int i = 0;
        foreach (var c in closes)
            list.Add(new Ohlcv(new DateTime(2026, 1, 1).AddDays(i++), c, c + 0.3, c - 0.3, c, 1000));
        return list;
    }

    private static IEnumerable<double> Leg(double from, double to, int steps)
    {
        for (int i = 1; i <= steps; i++) yield return from + (to - from) * i / steps;
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Shapes ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FindsADoubleTop_AndNamesTheTroughAsTheNeckline()
    {
        // 100 → 140 → 118 → 139 → 105. Two highs a point apart, a clear trough between them.
        var closes = Leg(100, 140, 14).Concat(Leg(140, 118, 10))
                    .Concat(Leg(118, 139, 12)).Concat(Leg(139, 105, 14));
        var bars = Bars(closes.Prepend(100));

        var found = Detector.Detect(bars);
        var dt = found.Where(p => p.Kind == ChartPatternKind.DoubleTop).ToList();

        Assert.NotEmpty(dt);
        // The neckline is the trough, not either high.
        Assert.All(dt, p => Assert.InRange(p.TriggerLevel, 110, 130));
    }

    [Fact]
    public void FindsADoubleBottom()
    {
        var closes = Leg(140, 100, 14).Concat(Leg(100, 122, 10))
                    .Concat(Leg(122, 101, 12)).Concat(Leg(101, 135, 14));
        var bars = Bars(closes.Prepend(140));

        Assert.Contains(Detector.Detect(bars), p => p.Kind == ChartPatternKind.DoubleBottom);
    }

    [Fact]
    public void FindsHeadAndShoulders_WithTheHeadAboveBothShoulders()
    {
        //        head 150
        //   sh 132        sh 131
        var closes = Leg(100, 132, 12).Concat(Leg(132, 118, 8))
                    .Concat(Leg(118, 150, 12)).Concat(Leg(150, 117, 10))
                    .Concat(Leg(117, 131, 10)).Concat(Leg(131, 100, 14));
        var bars = Bars(closes.Prepend(100));

        var hs = Detector.Detect(bars).Where(p => p.Kind == ChartPatternKind.HeadAndShoulders).ToList();
        Assert.NotEmpty(hs);
        Assert.All(hs, p => Assert.InRange(p.TriggerLevel, 110, 128));   // a neckline, not the head
    }

    [Fact]
    public void ThreePeaksWithNoDominantMiddleOne_AreNotHeadAndShoulders()
    {
        // Three level peaks. That is a range, and calling it head and shoulders would be the
        // failure mode where the detector finds a story in anything.
        var closes = Leg(100, 135, 12).Concat(Leg(135, 118, 8))
                    .Concat(Leg(118, 135, 10)).Concat(Leg(135, 118, 8))
                    .Concat(Leg(118, 135, 10)).Concat(Leg(135, 105, 12));
        var bars = Bars(closes.Prepend(100));

        Assert.DoesNotContain(Detector.Detect(bars), p => p.Kind == ChartPatternKind.HeadAndShoulders);
    }

    [Fact]
    public void FindsAnAscendingTriangle_FlatTopRisingLows()
    {
        var closes = Leg(100, 140, 14)
            .Concat(Leg(140, 118, 8)).Concat(Leg(118, 139.5, 8))
            .Concat(Leg(139.5, 127, 8)).Concat(Leg(127, 140, 8))
            .Concat(Leg(140, 133, 6)).Concat(Leg(133, 152, 10));
        var bars = Bars(closes.Prepend(100));

        var tri = Detector.Detect(bars)
            .Where(p => p.Kind is ChartPatternKind.AscendingTriangle
                              or ChartPatternKind.SymmetricalTriangle
                              or ChartPatternKind.DescendingTriangle).ToList();
        Assert.NotEmpty(tri);
    }

    [Fact]
    public void AMonotoneRise_ProducesNoReversalPatterns()
    {
        var bars = Bars(Enumerable.Range(0, 200).Select(i => 100.0 * Math.Pow(1.004, i)));

        var found = Detector.Detect(bars);
        Assert.DoesNotContain(found, p => p.Kind is ChartPatternKind.DoubleTop
                                                 or ChartPatternKind.HeadAndShoulders
                                                 or ChartPatternKind.DoubleBottom);
    }

    [Fact]
    public void TooFewBars_ReturnsNothingRatherThanThrowing()
    {
        Assert.Empty(Detector.Detect(Bars(Leg(100, 110, 5))));
        Assert.Empty(Detector.Detect(new List<Ohlcv>()));
    }

    // ── The properties that matter ──────────────────────────────────────────────

    /// <summary>
    /// Every pattern must be knowable no earlier than the last pivot it rests on. Since pivots are
    /// only confirmed Span bars after they print, KnownAtIndex has to sit at or after the pattern's
    /// own end — a pattern reported AT its final pivot would be using information from the future.
    /// </summary>
    [Fact]
    public void NoPatternIsKnowableBeforeItsStructureIsComplete()
    {
        var bars = RandomWalk(600, seed: 7);
        var found = Detector.Detect(bars);
        Assert.NotEmpty(found);

        foreach (var p in found)
        {
            Assert.True(p.KnownAtIndex >= p.EndBarIndex,
                $"{p.Kind} ends at {p.EndBarIndex} but claims to be knowable at {p.KnownAtIndex}");
            Assert.True(p.StartBarIndex <= p.EndBarIndex);
            Assert.True(p.KnownAtIndex < bars.Count);
        }
    }

    /// <summary>
    /// The decisive lookahead test. Detect on the full series, then on a truncated one, and require
    /// that every pattern knowable inside the truncation is reported identically. If the detector
    /// used future bars, the two runs would disagree.
    /// </summary>
    [Fact]
    public void TruncatingTheSeries_DoesNotChangeWhatWasKnowableEarlier()
    {
        var bars = RandomWalk(600, seed: 11);
        int cut = 400;

        var full = Detector.Detect(bars);
        var early = Detector.Detect(bars.Take(cut).ToList());

        // Everything the truncated run found must also be found by the full run, with the same
        // knowable-at bar. (The reverse does not hold: the full run legitimately sees more, and a
        // pattern still Forming at the cut may have Completed later.)
        foreach (var e in early)
        {
            Assert.Contains(full, f => f.Kind == e.Kind
                                    && f.StartBarIndex == e.StartBarIndex
                                    && f.EndBarIndex == e.EndBarIndex
                                    && f.KnownAtIndex == e.KnownAtIndex);
        }
    }

    /// <summary>
    /// A forming pattern's trigger must be genuinely untouched on a closing basis from the bar it
    /// became knowable to the end of the data. Otherwise "in progress" is describing something that
    /// already resolved, and the user acts on a level that is gone.
    /// </summary>
    [Fact]
    public void FormingPatterns_HaveNotYetClosedThroughTheirTrigger()
    {
        var bars = RandomWalk(600, seed: 3);

        foreach (var p in Detector.Detect(bars).Where(x => x.State == ChartPatternState.Forming))
        {
            var after = bars.Skip(p.KnownAtIndex).ToList();
            bool above = after.All(b => b.Close >= p.TriggerLevel);
            bool below = after.All(b => b.Close <= p.TriggerLevel);
            Assert.True(above || below,
                $"{p.Kind} reported Forming but price closed through {p.TriggerLevel} after bar {p.KnownAtIndex}");
        }
    }

    [Fact]
    public void CompletedPatterns_NameTheBarThatBrokeTheTrigger_AtOrAfterTheStructureWasKnowable()
    {
        var bars = RandomWalk(600, seed: 5);
        var completed = Detector.Detect(bars).Where(x => x.State == ChartPatternState.Completed).ToList();
        Assert.NotEmpty(completed);

        foreach (var p in completed)
        {
            Assert.NotNull(p.CompletedAtIndex);
            Assert.True(p.CompletedAtIndex >= p.KnownAtIndex,
                $"{p.Kind} completed at {p.CompletedAtIndex} before its structure was knowable at {p.KnownAtIndex}");
            Assert.NotEqual(bars[p.CompletedAtIndex!.Value].Close, p.TriggerLevel);
        }
    }

    [Fact]
    public void FormingPatterns_CarryNoCompletionBar()
    {
        var bars = RandomWalk(600, seed: 5);
        foreach (var p in Detector.Detect(bars).Where(x => x.State == ChartPatternState.Forming))
            Assert.Null(p.CompletedAtIndex);
    }

    // ── Narration ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The hedge is not decoration. The structure is real; the completion is not, and a
    /// confident-sounding announcement in a domain where sounding confident is most of the con is
    /// worse than silence.
    /// </summary>
    [Fact]
    public void FormingPatternsAreHedged_AndAlwaysCarryTheirLevel()
    {
        var p = new ChartPattern(ChartPatternKind.DoubleTop, ChartPatternState.Forming,
            10, 40, 45, 42100, DateTime.Today, DateTime.Today);

        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.StartsWith("Possible ", s);
        Assert.Contains("in progress", s);
        Assert.Contains("neckline", s);
        Assert.Contains("42100", s);
    }

    [Fact]
    public void CompletedPatternsSayCompleted_AndNameTheBrokenLevel()
    {
        var p = new ChartPattern(ChartPatternKind.HeadAndShoulders, ChartPatternState.Completed,
            10, 40, 45, 118.5, DateTime.Today, DateTime.Today);

        string s = ChartPatternNarrator.Describe(p, Fmt);

        Assert.DoesNotContain("Possible", s);
        Assert.Contains("completed", s);
        Assert.Contains("broken", s);
        Assert.Contains("118.5", s);
    }

    /// <summary>
    /// The standing policy is describe freely, score never. Every conventional directional reading —
    /// head and shoulders is bearish, ascending triangles break up — is a claim this project has
    /// either failed to confirm or has not yet tested (<c>triangle-direction-bias</c> is queued and
    /// untested). A single directional word here would be the product endorsing an untested claim.
    /// </summary>
    [Fact]
    public void NarrationNeverClaimsADirectionOrATarget()
    {
        string[] banned = { "bullish", "bearish", "buy", "sell", "target", "expect", "will ", "reversal", "likely" };

        foreach (ChartPatternKind kind in Enum.GetValues<ChartPatternKind>())
        foreach (var state in new[] { ChartPatternState.Forming, ChartPatternState.Completed })
        {
            string s = ChartPatternNarrator.Describe(
                new ChartPattern(kind, state, 1, 2, 3, 100, DateTime.Today, DateTime.Today), Fmt)
                .ToLowerInvariant();

            foreach (var word in banned)
                Assert.False(s.Contains(word), $"{kind}/{state} narration contains '{word}': {s}");
        }
    }

    /// <summary>
    /// A chart region can satisfy several definitions at once. Reading all of them is how a user
    /// learns to switch the feature off, so the readout is capped — and forming patterns come first,
    /// because a completed pattern is history and a forming one is a decision.
    /// </summary>
    [Fact]
    public void DescribeMany_CapsTheReadoutAndPutsFormingFirst()
    {
        var patterns = new List<ChartPattern>
        {
            new(ChartPatternKind.DoubleTop, ChartPatternState.Completed, 1, 2, 3, 100, DateTime.Today, DateTime.Today),
            new(ChartPatternKind.RisingWedge, ChartPatternState.Forming, 1, 2, 4, 110, DateTime.Today, DateTime.Today),
            new(ChartPatternKind.BullFlag, ChartPatternState.Forming, 1, 2, 5, 120, DateTime.Today, DateTime.Today),
            new(ChartPatternKind.BearFlag, ChartPatternState.Completed, 1, 2, 6, 130, DateTime.Today, DateTime.Today),
        };

        string s = ChartPatternNarrator.DescribeMany(patterns, Fmt, max: 2);

        Assert.StartsWith("Possible bull flag", s);        // newest forming first
        Assert.Contains("rising wedge", s);
        Assert.DoesNotContain("double top", s);            // capped out
    }

    /// <summary>
    /// Panning back to bar N must describe only what was visible at bar N. Without this the terminal
    /// would, at bar 400, describe a shape whose final pivot was not confirmed until bar 430.
    /// </summary>
    [Fact]
    public void AtBar_ExcludesPatternsNotYetKnowableAtThatBar()
    {
        var patterns = new List<ChartPattern>
        {
            // Formed over 30 bars, knowable at 45 → relevant 45..75.
            new(ChartPatternKind.DoubleTop, ChartPatternState.Forming, 10, 40, 45, 100, DateTime.Today, DateTime.Today),
            // Formed over 20 bars, knowable at 30 → relevant 30..50.
            new(ChartPatternKind.BullFlag,  ChartPatternState.Forming, 10, 30, 30, 110, DateTime.Today, DateTime.Today),
        };

        Assert.Empty(ChartPatternNarrator.AtBar(patterns, 20));           // neither confirmed yet
        Assert.Single(ChartPatternNarrator.AtBar(patterns, 30));          // the flag, on the bar it confirms
        Assert.Equal(2, ChartPatternNarrator.AtBar(patterns, 45).Count);  // both in view

        // Past the flag's relevance window, the double top is still current.
        var at70 = ChartPatternNarrator.AtBar(patterns, 70);
        Assert.Single(at70);
        Assert.Equal(ChartPatternKind.DoubleTop, at70[0].Kind);

        Assert.Empty(ChartPatternNarrator.AtBar(patterns, 200));          // everything long stale
    }

    /// <summary>
    /// A completed pattern stops being current the moment it resolves. Announcing it forever after
    /// would bury the forming ones, which are the only actionable output.
    /// </summary>
    [Fact]
    public void AtBar_DropsACompletedPatternOnceItHasResolved()
    {
        var patterns = new List<ChartPattern>
        {
            new(ChartPatternKind.DoubleTop, ChartPatternState.Completed, 10, 40, 45, 100,
                DateTime.Today, DateTime.Today, CompletedAtIndex: 52),
        };

        Assert.Single(ChartPatternNarrator.AtBar(patterns, 45));
        Assert.Single(ChartPatternNarrator.AtBar(patterns, 52));
        Assert.Empty(ChartPatternNarrator.AtBar(patterns, 53));
    }

    /// <summary>
    /// The window must be wider than one bar. Because confirmation lag guarantees a pattern's own
    /// span ends BEFORE it becomes knowable, an upper bound of EndBarIndex silently collapses the
    /// window to a single bar — the pattern is then announced once, on one bar, and never when the
    /// user pans into the region it describes. That was the first implementation, and it looked
    /// entirely correct until the coverage was measured.
    /// </summary>
    [Fact]
    public void AtBar_WindowSpansMoreThanTheSingleConfirmationBar()
    {
        var bars = RandomWalk(600, seed: 21);
        var found = Detector.Detect(bars);
        Assert.NotEmpty(found);

        int barsWithAPattern = Enumerable.Range(0, bars.Count)
            .Count(i => ChartPatternNarrator.AtBar(found, i).Count > 0);

        Assert.True(barsWithAPattern > found.Count,
            $"{found.Count} patterns cover only {barsWithAPattern} bars — the window is one bar wide");
    }

    // ── Helper ──────────────────────────────────────────────────────────────────

    private static List<Ohlcv> RandomWalk(int n, int seed)
    {
        var rng = new Random(seed);
        var closes = new List<double>(n);
        double px = 100;
        for (int i = 0; i < n; i++)
        {
            px *= Math.Exp((rng.NextDouble() - 0.5) * 0.05);
            closes.Add(px);
        }
        return Bars(closes);
    }
}
