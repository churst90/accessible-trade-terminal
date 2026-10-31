using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The setup grader's harness (<c>docs/SETUP_GRADER_PREREG.md</c>, <c>setup-grader</c>). The
    /// pre-registration names three ways the app's own machinery would have falsified this study:
    /// a weekly bar treated as closed once it opens, R computed from a stop that has since moved,
    /// and a missing exit harness. These tests pin the properties the study depends on, not the
    /// spelling of any line: (a) a daily bar sees only completed weeks, (b) R is per position from
    /// the initial risk, (c) a grade computed at bar t does not change when the bars after t are
    /// removed, and (d) every trigger's matched random control is drawn from bars in the same
    /// context grade. Each was proven red by reintroducing the defect it guards.
    /// </summary>
    [Collection(nameof(LabCipherProfileTests))]
    public class SetupGraderTests
    {
        private static readonly DateTime Monday = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static List<Ohlcv> Days(DateTime start, IEnumerable<int> dayOffsets)
        {
            var bars = new List<Ohlcv>();
            foreach (int d in dayOffsets)
                bars.Add(new Ohlcv(start.AddDays(d), 100 + d, 101 + d, 99 + d, 100.5 + d, 1000));
            return bars;
        }

        // ── (a) completed weeks only ──────────────────────────────────────────

        [Fact]
        public void CryptoDailyBarsSeeOnlyCompletedWeeks()
        {
            // 30 consecutive days from a Monday: four full weeks, then a partial one (Mon, Tue).
            var bars = Days(Monday, Enumerable.Range(0, 30));
            var cfg = GraderSeriesConfig.DailyCrypto;
            var h = SetupGraderCore.Aggregate(bars, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);

            Assert.Equal(4, h.Bars.Count);                       // the unfinished final week is dropped
            // Wednesday of week 3 (index 16) sees week 2 (Jan 8-14), whose close is Sunday Jan 14's.
            Assert.Equal(1, h.UsableAt[16]);
            Assert.Equal(bars[13].Close, h.Bars[h.UsableAt[16]].Close);
            // Saturday Jan 13 cannot yet see the week it belongs to; Sunday Jan 14 can.
            Assert.Equal(0, h.UsableAt[12]);
            Assert.Equal(1, h.UsableAt[13]);

            // The property, on every bar: nothing in the usable week lies after the bar, and the
            // week's calendar span has ended by the bar's close.
            for (int k = 0; k < bars.Count; k++)
            {
                int w = h.UsableAt[k];
                if (w < 0) continue;
                Assert.True(h.LastLowerIndex[w] <= k, $"bar {k} sees a week that ends at bar {h.LastLowerIndex[w]}");
                Assert.True(h.BucketStart[w].AddDays(7) <= bars[k].Date.AddDays(1), $"bar {k} sees an unfinished week");
                Assert.True(w + 1 >= h.Bars.Count || h.LastLowerIndex[w + 1] > k, $"bar {k} misses a completed week");
            }

            // A series that ends on a Sunday has finished its last week, and its last bar sees it.
            var toSunday = Days(Monday, Enumerable.Range(0, 28));
            var h2 = SetupGraderCore.Aggregate(toSunday, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);
            Assert.Equal(4, h2.Bars.Count);
            Assert.Equal(3, h2.UsableAt[^1]);
            Assert.Equal(2, h2.UsableAt[^2]);
        }

        [Fact]
        public void EquityWeekIsCompleteOnItsLastTradingDayIncludingAHolidayWeek()
        {
            // Mon Jan 1 - Fri Jan 5; Mon Jan 8 - Thu Jan 11 (Friday a holiday); Mon Jan 15 - Fri Jan 19;
            // Mon Jan 22 - Tue Jan 23 (the data ends mid-week).
            var offs = new[] { 0, 1, 2, 3, 4, 7, 8, 9, 10, 14, 15, 16, 17, 18, 21, 22 };
            var bars = Days(Monday, offs);
            var cfg = GraderSeriesConfig.DailyEquity;
            var h = SetupGraderCore.Aggregate(bars, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);

            Assert.Equal(3, h.Bars.Count);
            int wed10 = Array.IndexOf(offs, 9), thu11 = Array.IndexOf(offs, 10), wed17 = Array.IndexOf(offs, 16);
            Assert.Equal(0, h.UsableAt[wed10]);                  // mid-week: last week only
            Assert.Equal(1, h.UsableAt[thu11]);                  // the holiday week ends on Thursday
            Assert.Equal(bars[thu11].Close, h.Bars[1].Close);
            Assert.Equal(1, h.UsableAt[wed17]);
            Assert.Equal(2, h.UsableAt[^1]);                     // the unfinished last week is never usable
            for (int k = 0; k < bars.Count; k++)
                if (h.UsableAt[k] >= 0) Assert.True(h.LastLowerIndex[h.UsableAt[k]] <= k);
        }

        // ── (b) R per position from the initial risk ──────────────────────────

        private static SeriesInputs Minimal(List<Ohlcv> bars)
        {
            int n = bars.Count;
            var nan = Enumerable.Repeat(double.NaN, n).ToArray();
            var no = new bool[n];
            var cfg = GraderSeriesConfig.DailyCrypto;
            var h = SetupGraderCore.Aggregate(bars, cfg.BucketOf, cfg.BucketLen, cfg.LowerTf, cfg.Calendar);
            return new SeriesInputs
            {
                Symbol = "T", Cfg = cfg, Bars = bars,
                Atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14),
                Sma200 = nan, Mf = nan, Wt2 = nan,
                BuyOnset = no, ShortTrig = no, RedOnset = no, CrossBear = no, BullMark = no, BearMark = no,
                Htf = h, Trend = SetupGraderCore.BodyTrend(h.Bars),
                HtfEma10 = new double[h.Bars.Count], HtfMf = new double[h.Bars.Count], PhaseAt = new int[n],
            };
        }

        [Fact]
        public void RIsPerPositionFromTheInitialRiskAfterTheStopMovesToBreakeven()
        {
            // 30 flat bars (range 2, so ATR = 2), signal on bar 29, entry at bar 30's open = 100.
            var bars = new List<Ohlcv>();
            for (int k = 0; k < 30; k++) bars.Add(new Ohlcv(Monday.AddDays(k), 100, 101, 99, 100, 1));
            bars.Add(new Ohlcv(Monday.AddDays(30), 100, 100.8, 99.2, 100.2, 1));   // entry bar
            bars.Add(new Ohlcv(Monday.AddDays(31), 100.2, 105, 100.5, 104.5, 1));  // 2R (104) hit: half off at +4
            bars.Add(new Ohlcv(Monday.AddDays(32), 100.2, 100.4, 99, 99.5, 1));    // stop now at entry: out at 100
            for (int k = 33; k < 40; k++) bars.Add(new Ohlcv(Monday.AddDays(k), 99.5, 100, 99, 99.5, 1));
            var s = Minimal(bars);

            // Initial stop 98 = 2 below the entry. Per position: 0.5 x 4 + 0.5 x 0, over the initial 2.
            var r = SetupGraderCore.Ladder(s, 29, 98.0);
            Assert.Equal(1.0, r.RGross, 9);
            Assert.True(double.IsFinite(r.RNet));
            Assert.Equal(2, r.Hold);

            // Both rungs: 0.5 x 4 + 0.5 x 8 over 2 = 3R.
            var up = new List<Ohlcv>(bars.Take(31))
            {
                new(Monday.AddDays(31), 100.2, 105, 100.5, 104.5, 1),
                new(Monday.AddDays(32), 104.5, 109, 104, 108.5, 1),
            };
            for (int k = 33; k < 40; k++) up.Add(new Ohlcv(Monday.AddDays(k), 108.5, 109, 108, 108.5, 1));
            Assert.Equal(3.0, SetupGraderCore.Ladder(Minimal(up), 29, 98.0).RGross, 9);
        }

        // ── (c) no look-ahead ────────────────────────────────────────────────

        /// <summary>A long random walk with slow regimes (so the weekly trend turns) and fast
        /// cycles (so the wave reaches -60 and Buy dots fire).</summary>
        private static List<Ohlcv> Walk(int n = 2200, int seed = 11)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(n);
            double px = 100;
            for (int i = 0; i < n; i++)
            {
                double drift = 0.006 * Math.Sin(i / 160.0) + 0.03 * Math.Sin(i / 9.0) / 3 + 0.03 * (rng.NextDouble() - 0.5);
                double next = px * Math.Exp(drift);
                double o = px * (1 + 0.004 * (rng.NextDouble() - 0.5));
                double hi = Math.Max(o, next) * (1 + 0.012 * rng.NextDouble());
                double lo = Math.Min(o, next) * (1 - 0.012 * rng.NextDouble());
                bars.Add(new Ohlcv(Monday.AddDays(i), o, hi, lo, next, 1000 + rng.Next(1000)));
                px = next;
            }
            return bars;
        }

        private static IIndicatorEngine Engine() => LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();

        [Fact]
        public async Task AGradeAtBarTIsIdenticalWhenTheBarsAfterTAreRemoved()
        {
            var engine = Engine();
            var bars = Walk();
            var cfg = GraderSeriesConfig.DailyCrypto;
            var full = await SetupGraderCore.BuildAsync(engine, "T", bars, cfg, null);

            // Every Buy dot and mirror trigger the full series can grade, plus a sample of plain bars.
            var ts = Enumerable.Range(0, bars.Count)
                .Where(i => full.BuyOnset[i] || full.ShortTrig[i] || i % 97 == 0)
                .Where(i => i > 500 && i < bars.Count - 1)
                .ToList();
            int graded = 0;
            var gradesSeen = new HashSet<SetupGrade>();
            foreach (int t in ts)
            {
                var cut = await SetupGraderCore.BuildAsync(engine, "T", bars.Take(t + 1).ToList(), cfg, null);
                Assert.Equal(full.BuyOnset[t], cut.BuyOnset[t]);
                Assert.Equal(full.ShortTrig[t], cut.ShortTrig[t]);
                foreach (bool isLong in new[] { true, false })
                {
                    Assert.Equal(SetupGraderCore.CgAt(full, t, isLong), SetupGraderCore.CgAt(cut, t, isLong));
                    var wf = SetupGraderCore.InputsAt(full, t, isLong, out var gf, out var lf);
                    var wc = SetupGraderCore.InputsAt(cut, t, isLong, out var gc, out var lc);
                    Assert.Equal(wf, wc);
                    Assert.True(gf == gc, $"bar {t} {(isLong ? "long" : "short")}: {gf} with the future, {gc} without");
                    Assert.Equal(lf, lc);
                    if (wf == null)
                    {
                        graded++;
                        gradesSeen.Add(SetupGraderCore.GradeOf(gf));
                    }
                }
            }
            // Vacuity: the comparison has to have covered real, defined grades of more than one kind.
            Assert.True(graded >= 20, $"only {graded} defined grades were compared");
            Assert.True(gradesSeen.Count >= 2, "every compared grade was the same");
        }

        // ── (d) the matched control shares the context grade ──────────────────

        [Fact]
        public async Task EveryTriggersMatchedRandomControlComesFromBarsWithTheSameContextGrade()
        {
            var s = await SetupGraderCore.BuildAsync(Engine(), "T", Walk(), GraderSeriesConfig.DailyCrypto, null);
            var run = SetupGraderCommand.BuildRun("T", "crypto", s);

            var evs = run.Long.Concat(run.Short).ToList();
            Assert.True(evs.Select(e => e.In.Cg).Distinct().Count() >= 2, "the synthetic triggers all share one context grade");
            foreach (var e in run.Long)
            {
                Assert.Equal(run.CgL[e.Bar], e.In.Cg);
                var expected = Enumerable.Range(0, s.Bars.Count)
                    .Where(i => run.CgL[i] == e.In.Cg && run.Era[i] == e.Era && !double.IsNaN(run.RL[i]))
                    .ToList();
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Average(i => run.RL[i]), e.Ctl, 9);
            }
            foreach (var e in run.Short)
            {
                Assert.Equal(run.CgS[e.Bar], e.In.Cg);
                var expected = Enumerable.Range(0, s.Bars.Count)
                    .Where(i => run.CgS[i] == e.In.Cg && run.Era[i] == e.Era && !double.IsNaN(run.RS[i]))
                    .ToList();
                Assert.Equal(expected.Average(i => run.RS[i]), e.Ctl, 9);
            }
            // The permutation strata are the same bars: one context grade each.
            foreach (var ((era, cg, _), st) in run.StrataL)
                Assert.All(st.Rows, i => { Assert.Equal(cg, run.CgL[i]); Assert.Equal(era, run.Era[i]); });
        }

        /// <summary>
        /// The three setup graders refuse to run since 2026-10-31: their pre-registered rules read
        /// Cipher B's divergences and Overbought Crossover, which went that day (Cody: "remove all
        /// of that old code from cipher b"), and a run today would be a different experiment under
        /// the same name. Refused before any snapshot is read, so a folder that does not exist is
        /// enough; without the refusal each run fails on the missing snapshots instead.
        /// </summary>
        [Fact]
        public async Task The_graders_refuse_to_run_without_Cipher_Bs_removed_parts()
        {
            var nowhere = TestTemp.NewPath("no-snapshots-");
            Assert.Equal(2, await SetupGraderCommand.RunAsync(nowhere, countsOnly: true));
            Assert.Equal(2, await SetupGraderMc2Command.RunAsync(nowhere, countsOnly: true));
            Assert.Equal(2, await SetupGraderMc3Command.RunAsync(nowhere, countsOnly: true));
            Assert.False(Directory.Exists(nowhere));
        }
    }
}
