using System;
using System.Collections.Generic;
using System.Linq;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using Xunit;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// Coverage for <see cref="LevelRespectAnalyzer"/> and <see cref="MaRespectRanker"/>.
    ///
    /// These tests exist because the whole feature is only worth having if the numbers are
    /// trustworthy. The invariants pinned here are the ones that decide that:
    ///   • a wick through a level and back is a HOLD (that is a sweep, not a failure);
    ///   • a decisive close through is a BREAK;
    ///   • drifting sideways along a line is NEITHER — it must not score as respect;
    ///   • lingering against a line for six bars is ONE interaction, not six;
    ///   • a 2-for-2 record must not outrank a 24-of-34 record;
    ///   • a multi-timeframe average must never read a higher-timeframe bar that had not
    ///     closed yet at the bar being evaluated.
    /// </summary>
    public class LevelRespectAnalyzerTests
    {
        private static readonly DateTime Start = new(2026, 1, 1);

        private static Ohlcv Bar(int day, double open, double high, double low, double close) =>
            new(Start.AddDays(day), open, high, low, close, 100);

        /// <summary>Flat filler bars far above the line, used to warm up ATR without touching it.</summary>
        private static List<Ohlcv> Filler(int count, double price, double range = 1.0)
        {
            var bars = new List<Ohlcv>(count);
            for (int i = 0; i < count; i++)
                bars.Add(Bar(i, price, price + range, price - range, price));
            return bars;
        }

        private static LineCandidate Flat(double price, int barCount, string id = "L")
        {
            var values = new double[barCount];
            Array.Fill(values, price);
            return new LineCandidate(id, id, LineKind.Horizontal, values);
        }

        private static RespectStats Single(List<Ohlcv> bars, LineCandidate line, RespectOptions? opts = null) =>
            Assert.Single(new LevelRespectAnalyzer().Analyze(bars, new[] { line }, opts));

        // ── Touch outcome semantics ───────────────────────────────────────────

        [Fact]
        public void WickThroughAndBack_CountsAsAHold_BecauseThatIsASweep()
        {
            // 30 warmup bars at 110 (ATR ≈ 2), then one bar that wicks to 99 (through the 100
            // line) but closes back at 110, then bars that carry price further away.
            var bars = Filler(30, 110);
            bars.Add(Bar(30, 110, 111, 99, 110));       // sweep: low pierces, close rejects
            for (int i = 31; i < 40; i++) bars.Add(Bar(i, 112, 118, 111, 117));

            var stats = Single(bars, Flat(100, bars.Count));

            Assert.Equal(1, stats.Touches);
            Assert.Equal(1, stats.Holds);
            Assert.Equal(1.0, stats.HoldRate);
            Assert.Equal(1, stats.SupportHolds);
            Assert.Equal(0, stats.ResistanceHolds);
        }

        [Fact]
        public void DecisiveCloseThrough_CountsAsABreak()
        {
            var bars = Filler(30, 110);
            bars.Add(Bar(30, 110, 111, 99, 100));        // reaches the line
            for (int i = 31; i < 40; i++) bars.Add(Bar(i, 95, 96, 88, 90)); // closes far below and stays

            var stats = Single(bars, Flat(100, bars.Count));

            Assert.Equal(1, stats.Touches);
            Assert.Equal(0, stats.Holds);
            Assert.Equal(0.0, stats.HoldRate);
        }

        [Fact]
        public void SidewaysDriftAlongTheLine_IsNotAHold()
        {
            // Price reaches the line and then does nothing — never breaks, never bounces.
            // Counting this as respect is the single easiest way to make a level look real.
            var bars = Filler(30, 110);
            for (int i = 30; i < 45; i++) bars.Add(Bar(i, 100, 100.4, 99.8, 100.1));

            var stats = Single(bars, Flat(100, bars.Count));

            Assert.True(stats.Touches >= 1);
            Assert.Equal(0, stats.Holds);
        }

        [Fact]
        public void LingeringAgainstTheLine_CountsAsOneInteractionNotMany()
        {
            var bars = Filler(30, 110);
            // Six consecutive bars all touching the line.
            for (int i = 30; i < 36; i++) bars.Add(Bar(i, 100.5, 101, 99.5, 100.5));
            for (int i = 36; i < 45; i++) bars.Add(Bar(i, 112, 118, 111, 117));

            var opts = RespectOptions.Default with { MinSeparationBars = 5 };
            var stats = Single(bars, Flat(100, bars.Count), opts);

            // Six touching bars inside one 5-bar separation window must not become six touches.
            Assert.InRange(stats.Touches, 1, 2);
        }

        [Fact]
        public void ApproachFromBelow_IsClassifiedAsResistance()
        {
            var bars = Filler(30, 90);
            bars.Add(Bar(30, 90, 101, 89, 90));          // pokes up into the line and rejects
            for (int i = 31; i < 40; i++) bars.Add(Bar(i, 88, 89, 82, 83));

            var stats = Single(bars, Flat(100, bars.Count));

            Assert.Equal(1, stats.Touches);
            Assert.Equal(1, stats.Holds);
            Assert.Equal(1, stats.ResistanceHolds);
            Assert.Equal(0, stats.SupportHolds);
        }

        [Fact]
        public void PriceThatNeverReachesTheLine_ProducesNoTouches()
        {
            var bars = Filler(60, 200);
            var stats = Single(bars, Flat(100, bars.Count));

            Assert.Equal(0, stats.Touches);
            Assert.True(double.IsNaN(stats.HoldRate));
            Assert.Equal(-1, stats.BarsSinceLastTouch);
        }

        // ── Reporting ─────────────────────────────────────────────────────────

        [Fact]
        public void MismatchedCandidateLength_IsSkippedRatherThanThrowing()
        {
            var bars = Filler(40, 100);
            var good = Flat(100, bars.Count, "good");
            var bad = new LineCandidate("bad", "bad", LineKind.Horizontal, new double[3]);

            var results = new LevelRespectAnalyzer().Analyze(bars, new[] { good, bad });

            Assert.Single(results);
            Assert.Equal("good", results[0].Id);
        }

        [Fact]
        public void NaNSegmentsOfALine_AreIgnoredNotTreatedAsZero()
        {
            // An indicator's warmup region is NaN. Treating those as a price of 0 would
            // manufacture touches at the very start of every series.
            var bars = Filler(60, 100);
            var values = new double[bars.Count];
            for (int i = 0; i < bars.Count; i++) values[i] = i < 40 ? double.NaN : 100;

            var stats = Single(bars, new LineCandidate("L", "L", LineKind.MovingAverage, values));

            Assert.All(stats.TouchDetail, t => Assert.True(t.BarIndex >= 40));
        }

        [Fact]
        public void CurrentValueAndDistance_AreMeasuredAtTheLastBar()
        {
            var bars = Filler(40, 110);
            var stats = Single(bars, Flat(100, bars.Count));

            Assert.Equal(100, stats.CurrentValue);
            Assert.True(stats.DistanceAtr > 0); // close is above the line
        }

        // ── Scoring ───────────────────────────────────────────────────────────

        [Fact]
        public void Score_ShrinksTowardsHalf_SoATinySampleCannotOutrankALargeOne()
        {
            var tiny = new RespectStats("a", "a", LineKind.Horizontal, 2, 2, 1.0, 2, 2, 2, 0,
                null, 1, 100, 0, Array.Empty<LineTouch>());
            var large = new RespectStats("b", "b", LineKind.Horizontal, 34, 24, 24 / 34.0, 2, 2, 12, 12,
                null, 1, 100, 0, Array.Empty<LineTouch>());

            Assert.True(large.Score > tiny.Score,
                "a 24-of-34 record must outrank a 2-of-2 record");
        }

        [Fact]
        public void IsReliable_GatesOnMinTouches()
        {
            var opts = RespectOptions.Default with { MinTouches = 5 };
            var thin = new RespectStats("a", "a", LineKind.Horizontal, 4, 4, 1.0, 2, 2, 4, 0,
                null, 1, 100, 0, Array.Empty<LineTouch>());
            var solid = thin with { Touches = 5, Holds = 4 };

            Assert.False(thin.IsReliable(opts));
            Assert.True(solid.IsReliable(opts));
        }

        [Fact]
        public void Score_IsNaN_WhenThereWereNoTouches()
        {
            var none = new RespectStats("a", "a", LineKind.Horizontal, 0, 0, double.NaN, double.NaN,
                double.NaN, 0, 0, null, -1, 100, 0, Array.Empty<LineTouch>());
            Assert.True(double.IsNaN(none.Score));
        }

        // ── Multi-timeframe projection ────────────────────────────────────────

        [Fact]
        public void MultiTimeframeMa_UsesOnlyTheLastCLOSEDHigherTimeframeBar()
        {
            // 40 weeks of daily bars with a hard step: the first 20 weeks at 100, the rest at 200.
            // A weekly average stepped onto the daily chart must never show the new level on a bar
            // inside the week that produced it — that would be reading a close from the future.
            var bars = new List<Ohlcv>();
            var day = new DateTime(2026, 1, 5); // a Monday
            for (int w = 0; w < 40; w++)
            {
                double price = w < 20 ? 100 : 200;
                for (int d = 0; d < 7; d++)
                {
                    bars.Add(new Ohlcv(day, price, price + 1, price - 1, price, 10));
                    day = day.AddDays(1);
                }
            }

            var ranker = new MaRespectRanker(new LevelRespectAnalyzer(), new ResamplerService());
            var candidate = ranker.BuildCandidate(bars, "1d", new MaSpec("SMA", 3, "1w"));

            Assert.NotNull(candidate);
            Assert.Equal(LineKind.MultiTimeframeMovingAverage, candidate!.Kind);

            // The line must be a step function: within a week it never changes.
            for (int w = 4; w < 40; w++)
            {
                var week = Enumerable.Range(w * 7, 7)
                    .Select(i => candidate.Values[i])
                    .Where(v => !double.IsNaN(v))
                    .Distinct()
                    .ToList();
                Assert.True(week.Count <= 1, $"week {w} changed mid-week — the projection leaked an unclosed bar");
            }

            // And it must lag the step, never lead it. Bars in the first week at 200 (week 20)
            // can only see averages built from the 100-era weeks.
            for (int i = 20 * 7; i < 21 * 7; i++)
                if (!double.IsNaN(candidate.Values[i]))
                    Assert.True(candidate.Values[i] < 150, "the weekly average led its own step");
        }

        [Fact]
        public void MultiTimeframeMa_RefusesATimeframeThatIsNotHigher()
        {
            var bars = Filler(200, 100);
            var ranker = new MaRespectRanker(new LevelRespectAnalyzer(), new ResamplerService());

            var candidate = ranker.BuildCandidate(bars, "1d", new MaSpec("EMA", 10, "1h"));

            // An "HTF" that is actually lower resamples to itself and makes the step meaningless,
            // so the candidate is all-NaN rather than quietly wrong.
            Assert.NotNull(candidate);
            Assert.All(candidate!.Values, v => Assert.True(double.IsNaN(v)));
        }

        [Fact]
        public void Rank_PutsReliableCandidatesAboveThinOnes()
        {
            var bars = Filler(300, 100, range: 3);
            var ranker = new MaRespectRanker(new LevelRespectAnalyzer(), new ResamplerService());

            var ranked = ranker.Rank(bars, "1d", new[]
            {
                new MaSpec("SMA", 10),
                new MaSpec("SMA", 200),
            });

            Assert.Equal(2, ranked.Count);
            var opts = RespectOptions.Default;
            // Whatever the scores, every reliable candidate must sort ahead of every thin one.
            bool sawThin = false;
            foreach (var r in ranked)
            {
                if (!r.IsReliable(opts)) sawThin = true;
                else Assert.False(sawThin, "a reliable candidate was ranked below a thin-sample one");
            }
        }

        [Fact]
        public void DefaultSpecs_IncludeHigherTimeframeVariantsForDailyCharts()
        {
            var specs = MaRespectRanker.DefaultSpecs("1d");

            Assert.Contains(specs, s => s.SourceTimeframe == null && s.Period == 89 && s.MaType == "EMA");
            Assert.Contains(specs, s => s.SourceTimeframe == "1w");
            // The label is what gets spoken, so it must name the source timeframe.
            var weekly = specs.First(s => s.SourceTimeframe == "1w" && s.Period == 10);
            Assert.Equal("1w 10 EMA", weekly.Label);
        }
    }
}
