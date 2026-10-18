using AccessibleTrader.StrategyLab.CycleSchool;
using AccessibleTrader.StrategyLab.Macro;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// <b>The cycle school decides every bar from what was known at that bar's close.</b>
    ///
    /// <para>
    /// <c>docs/CYCLE_SCHOOL_PREREG.md</c> scores Camel's checklist and our capitulation score against
    /// lows that a fixed rule confirms later. That only measures something if no rule peeks: a
    /// reference low may be used only from its confirming bar, a weekly input only once its week is
    /// complete, a percentile only against the past, and a macro number only from the day it was
    /// published. Each test below pins one of those properties on synthetic data, mostly as
    /// truncation invariance: whatever the study says about bar t must not change when every bar
    /// after t is deleted. Two more pin the null and the trade arithmetic the verdicts rest on.
    /// </para>
    /// </summary>
    public class CycleSchoolCausalityTests
    {
        /// <summary>A seeded random walk with real candles: every calendar day (crypto) or weekdays
        /// with a holiday Friday every fifth week (equities).</summary>
        private static Bar[] Walk(int n, int seed, bool crypto)
        {
            var rng = new Random(seed);
            var bars = new List<Bar>();
            var day = new DateOnly(2015, 1, 5); // a Monday
            double close = 100;
            int week = 0;
            while (bars.Count < n)
            {
                bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                if (day.DayOfWeek == DayOfWeek.Monday) week++;
                bool holiday = !crypto && day.DayOfWeek == DayOfWeek.Friday && week % 5 == 0;
                if (crypto || (!weekend && !holiday))
                {
                    double r = 0.025 * (rng.NextDouble() * 2 - 1) + 0.008 * Math.Sin(bars.Count / 12.0);
                    double open = close * (1 + 0.01 * (rng.NextDouble() - 0.5));
                    double c = close * Math.Exp(r);
                    double hi = Math.Max(open, c) * (1 + 0.015 * rng.NextDouble());
                    double lo = Math.Min(open, c) * (1 - 0.015 * rng.NextDouble());
                    bars.Add(new Bar(day, open, hi, lo, c));
                    close = c;
                }
                day = day.AddDays(1);
            }
            return bars.ToArray();
        }

        private static int[] Cuts(int n) => new[] { n / 3, n / 2, n / 2 + 7, (2 * n) / 3 + 3, n - 40 };

        [Fact]
        public void A_reference_low_is_never_usable_before_its_confirming_bar()
        {
            var b = Walk(3000, 11, crypto: true);
            var full = CycleSchoolCore.ReferenceLows(b, CycleSchoolCore.Atr(b), 20, 3, out _);
            var fullCalls = CycleSchoolCore.ChecklistCalls(CycleSchoolCore.DailyChecklistInputs(b, full, 60), CycleSchoolCore.RulePrimary, 30);
            var pct = Enumerable.Range(0, b.Length).Select(i => (double)((i * 37) % 100)).ToArray(); // a fixed, causal score
            var fullAlerts = CycleSchoolCore.Alerts(pct, full, 95, useDue: true);
            Assert.True(full.Count > 30, $"the walk must produce reference lows (got {full.Count}, atr {CycleSchoolCore.Atr(b)[500]:F3}, close {b[500].Close:F2})");
            Assert.True(fullCalls.Count > 5, "the walk must produce checklist calls");
            Assert.True(full.Count(r => r.Confirm - r.Low > 1) > 10, "lows must confirm after a delay, or the test is vacuous");

            // A low that confirms after bar i cannot open Camel's window on bar i, however well it is
            // spaced (random walks rarely produce that case, so it is built by hand); once confirmed it can.
            var hand = CycleSchoolCore.DailyChecklistInputs(b, new List<RefLow> { new(400, 520) }, 60);
            Assert.False(CycleSchoolCore.Evaluate(hand, 500, 480).Win);
            Assert.True(CycleSchoolCore.Evaluate(hand, 520, 480).Win);

            foreach (var cut in Cuts(b.Length))
            {
                var pre = b.Take(cut + 1).ToArray();
                var lows = CycleSchoolCore.ReferenceLows(pre, CycleSchoolCore.Atr(pre), 20, 3, out _);
                // What the prefix knows is exactly the full set's lows confirmed by the cut.
                Assert.Equal(full.Where(r => r.Confirm <= cut), lows);
                // Camel's WIN item and the alert's "low due" rule see only those.
                var calls = CycleSchoolCore.ChecklistCalls(CycleSchoolCore.DailyChecklistInputs(pre, lows, 60), CycleSchoolCore.RulePrimary, 30);
                Assert.Equal(fullCalls.Where(c => c.I <= cut).Select(c => (c.I, c.J, c.Win)), calls.Select(c => (c.I, c.J, c.Win)));
                var alerts = CycleSchoolCore.Alerts(pct.Take(cut + 1).ToArray(), lows, 95, useDue: true);
                Assert.Equal(fullAlerts.Where(t => t <= cut), alerts);
            }
        }

        [Fact]
        public void The_capitulation_score_at_bar_t_is_unchanged_when_later_bars_are_removed()
        {
            var bars = Walk(1400, 23, crypto: false);
            var x = new CycleSchoolScore.External();
            // Synthetic external inputs for gold's families (Q3 and M1..M12): weekly and monthly
            // releases published days after their observation date.
            var rng = new Random(5);
            foreach (var (id, fam, step, lag, min, stale) in new[] { ("Q3_gold", "Q", 7, 3, 20, 21) }
                .Concat(Enumerable.Range(1, 12).Select(k => ($"M{k}", "M", k % 2 == 0 ? 30 : 1, k % 2 == 0 ? 12 : 0, k % 2 == 0 ? 10 : 60, k % 2 == 0 ? 75 : 7))))
            {
                var obs = new List<Obs>();
                for (var d = bars[0].Day.AddDays(-400); d <= bars[^1].Day; d = d.AddDays(step))
                    obs.Add(new Obs(d, d.AddDays(lag), d.AddDays(lag), rng.NextDouble()));
                x.Obs[id] = obs;
                x.Spec[id] = (min, stale, fam);
            }
            var full = CycleSchoolScore.Build(new Asset("GLD", "gold", "synthetic", bars), null, x);
            Assert.True(full.PctCap.Count(v => !double.IsNaN(v)) > 300, "pctCAP must be defined on enough bars, or the test is vacuous");
            foreach (var cut in Cuts(bars.Length))
            {
                var pre = CycleSchoolScore.Build(new Asset("GLD", "gold", "synthetic", bars.Take(cut + 1).ToArray()), null, x);
                for (int t = 0; t <= cut; t++)
                {
                    Assert.Equal(full.Cap[t], pre.Cap[t]);
                    Assert.Equal(full.PctCap[t], pre.PctCap[t]);
                    Assert.Equal(full.PctCapP[t], pre.PctCapP[t]);
                }
            }
        }

        [Fact]
        public void A_macro_value_is_never_read_before_its_known_from_date()
        {
            var start = new DateOnly(2020, 1, 1);
            // Values fall day by day, so each new observation ranks lowest; the last one, dated day
            // 50 but published on day 80, is the highest and ranks near 100: if it shows before
            // day 80, it was read early.
            var obs = Enumerable.Range(0, 50).Select(k => new Obs(start.AddDays(k), start.AddDays(k), start.AddDays(k), 100 - k)).ToList();
            obs.Add(new Obs(start.AddDays(50), start.AddDays(80), start.AddDays(80), 1000));
            var days = Enumerable.Range(0, 100).Select(k => start.AddDays(k)).ToList();
            var normal = CycleSchoolInputs.MapReleases(obs, 1, 30, days);
            var strict = CycleSchoolInputs.MapReleases(obs, 1, 30, days, strictlyBefore: true);
            double sentinel = 100.0 * 50.5 / 51;
            for (int k = 0; k < 80; k++) Assert.NotEqual(sentinel, normal[k]);
            Assert.Equal(sentinel, normal[80]);
            Assert.NotEqual(sentinel, strict[80]);
            Assert.Equal(sentinel, strict[81]);

            // Vintaged data: a value is used from its realtime_start, never before.
            var rows = MacroPointInTime.ParseAlfredRealtimeCsv(
                "period_start_date,X,realtime_start_date,realtime_end_date\n" +
                "2020-01-01,10,2020-02-07,2020-03-05\n2020-01-01,12,2020-03-06,\n" +
                "2020-02-01,11,2020-03-06,\n2020-03-01,13,2020-04-03,\n");
            var pv = CycleSchoolInputs.PerVintage(rows, s => s.Values.Last(), 1);
            Assert.All(pv, o => Assert.True(o.Known >= rows.Where(r => r.RealtimeStart <= o.Known).Max(r => r.RealtimeStart)));
            Assert.Equal(new[] { new DateOnly(2020, 2, 7), new DateOnly(2020, 3, 6), new DateOnly(2020, 4, 3) }, pv.Select(o => o.Known));
            Assert.Equal(new[] { 10.0, 11.0, 13.0 }, pv.Select(o => o.Value));
        }

        [Fact]
        public void Weekly_inputs_use_only_completed_weeks()
        {
            foreach (var crypto in new[] { true, false })
            {
                var b = Walk(1300, crypto ? 31 : 37, crypto);
                var fullWeeks = CycleSchoolCore.Weekly(b, crypto);
                var fullAt = CycleSchoolCore.WeekAt(fullWeeks, b.Length);
                if (!crypto) // holiday weeks exist, so the "next week's first bar" branch is exercised
                    Assert.Contains(fullWeeks, w => w.UsableFrom > w.LastDaily && w.UsableFrom != int.MaxValue);
                foreach (var cut in Cuts(b.Length).Concat(new[] { 600, 601, 602, 603, 604 }))
                {
                    var pre = b.Take(cut + 1).ToArray();
                    var weeks = CycleSchoolCore.Weekly(pre, crypto);
                    var at = CycleSchoolCore.WeekAt(weeks, pre.Length);
                    for (int i = 0; i <= cut; i++)
                    {
                        Assert.Equal(fullAt[i], at[i]);
                        if (at[i] >= 0) Assert.Equal(fullWeeks[fullAt[i]].Bar, weeks[at[i]].Bar);
                    }
                }
                // The weekly checklist (A7) mapped to daily bars is the same on any prefix.
                var asset = new Asset(crypto ? "BTC" : "SPY", crypto ? "crypto" : "equity", "synthetic", b);
                var fullRun = AssetRun.Build(asset, null, null, withScore: false);
                foreach (var cut in Cuts(b.Length))
                {
                    var preRun = AssetRun.Build(asset with { Bars = b.Take(cut + 1).ToArray() }, null, null, withScore: false);
                    Assert.Equal(fullRun.A7.Where(t => t <= cut), preRun.A7.Where(t => t <= cut));
                }
            }
        }

        [Fact]
        public void The_circular_shift_null_preserves_call_frequency_and_spacing()
        {
            var calls = new List<int> { 105, 130, 131, 260, 400, 777, 900 };
            int a = 100, z = 999, len = z - a + 1;
            var gaps = Gaps(calls, a, len);
            for (int k = 0; k < 200; k++)
            {
                var s = CycleSchoolCore.CircularShift(calls, a, z, CycleSchoolCore.Rng("test", "X", k));
                Assert.Equal(calls.Count, s.Count);
                Assert.All(s, t => Assert.InRange(t, a, z));
                Assert.Equal(gaps, Gaps(s, a, len));
                Assert.NotEqual(calls, s);
            }

            static List<int> Gaps(List<int> c, int a, int len)
            {
                var g = new List<int>();
                for (int i = 0; i < c.Count; i++) g.Add(((c[(i + 1) % c.Count] - c[i]) % len + len) % len);
                g.Sort();
                return g;
            }
        }

        [Fact]
        public void R_is_measured_from_the_initial_risk()
        {
            // Flat bars at 100 (ATR 2) give a signal at bar 20 with its swing low at 95.
            var bars = new List<Bar>();
            var d = new DateOnly(2020, 1, 1);
            for (int i = 0; i < 100; i++) bars.Add(new Bar(d.AddDays(i), 100, 101, 99, 100));
            bars[18] = new Bar(d.AddDays(18), 97, 97, 95, 96);
            // Entry at 100 (bar 21's open); stop = min(95 − 0.25·2, 100 − 0.5·2) = 94.5; risk 5.5.
            // Bars 21-30 rise, then bar 31 closes at 104 below its SMA(10): exit there at 104.
            for (int i = 21; i <= 30; i++) bars[i] = new Bar(d.AddDays(i), 100 + (i - 21), 101 + (i - 21), 99.5 + (i - 21), 100.5 + (i - 21));
            bars[31] = new Bar(d.AddDays(31), 105, 105.5, 103.5, 104);
            var b = bars.ToArray();
            var atr = Enumerable.Repeat(2.0, b.Length).ToArray();
            var sma = CycleSchoolCore.Sma(b.Select(x => x.Close).ToList(), 10);
            var (t, st) = CycleSchoolCore.CamelTrade(b, atr, sma, 20, 95);
            Assert.Equal(CycleSchoolCore.TradeStatus.Taken, st);
            Assert.Equal(94.5, t!.Value.Stop, 9);
            Assert.Equal(31, t.Value.Exit);
            Assert.Equal((104 - 100) / 5.5, t.Value.RGross, 9);
            Assert.Equal((104 - 100) / 5.5 - 2 * 0.0015 * 100 / 5.5, t.Value.RNet, 9);

            // A stop-out loses exactly one initial risk, plus costs.
            var lose = (Bar[])b.Clone();
            lose[23] = new Bar(d.AddDays(23), 99, 99, 90, 92);
            var (t2, _) = CycleSchoolCore.CamelTrade(lose, atr, sma, 20, 95);
            Assert.Equal(-1.0, t2!.Value.RGross, 9);
            Assert.Equal(-1.0 - 2 * 0.0015 * 100 / 5.5, t2.Value.RNet, 9);
        }

        [Fact]
        public void Cftc_releases_follow_the_published_schedules_and_announcements()
        {
            DateOnly D(int y, int m, int d) => new(y, m, d);
            // The 2026 schedule's starred (holiday-delayed) dates and a normal Friday.
            Assert.Equal(D(2026, 6, 22), CftcReleaseCalendar.RuleRelease(D(2026, 6, 16)));
            Assert.Equal(D(2026, 7, 6), CftcReleaseCalendar.RuleRelease(D(2026, 6, 30)));
            Assert.Equal(D(2026, 11, 16), CftcReleaseCalendar.RuleRelease(D(2026, 11, 10)));
            Assert.Equal(D(2026, 11, 30), CftcReleaseCalendar.RuleRelease(D(2026, 11, 24)));
            Assert.Equal(D(2026, 12, 28), CftcReleaseCalendar.RuleRelease(D(2026, 12, 22)));
            Assert.Equal(D(2026, 9, 11), CftcReleaseCalendar.RuleRelease(D(2026, 9, 8)));   // Labor Day Monday: no delay
            // 2019's starred dates; a Tuesday holiday moves the report to Monday, not the release.
            Assert.Equal(D(2019, 7, 8), CftcReleaseCalendar.RuleRelease(D(2019, 7, 2)));
            Assert.Equal(D(2019, 12, 2), CftcReleaseCalendar.RuleRelease(D(2019, 11, 26)));
            Assert.Equal(D(2019, 12, 30), CftcReleaseCalendar.RuleRelease(D(2019, 12, 23)));
            // Shutdowns and the ION incident come from the CFTC's announcements.
            Assert.Equal(D(2019, 2, 1), CftcReleaseCalendar.KnownFrom(D(2018, 12, 24), CftcReleaseCalendar.Report.Tff));
            Assert.Equal(D(2019, 3, 8), CftcReleaseCalendar.KnownFrom(D(2019, 3, 5), CftcReleaseCalendar.Report.Tff));
            Assert.Equal(D(2023, 3, 3), CftcReleaseCalendar.KnownFrom(D(2023, 2, 7), CftcReleaseCalendar.Report.Tff));
            Assert.Equal(D(2025, 11, 19), CftcReleaseCalendar.KnownFrom(D(2025, 9, 30), CftcReleaseCalendar.Report.Tff));
            // Nothing is known before the report's history was first posted.
            Assert.Equal(D(2010, 12, 3), CftcReleaseCalendar.KnownFrom(D(2008, 10, 7), CftcReleaseCalendar.Report.Tff));
            Assert.Equal(D(2010, 5, 7), CftcReleaseCalendar.KnownFrom(D(2008, 10, 7), CftcReleaseCalendar.Report.Disaggregated));
            // Never before the Friday after the report.
            for (var r = D(2011, 1, 4); r < D(2026, 10, 1); r = r.AddDays(7))
                Assert.True(CftcReleaseCalendar.KnownFrom(r, CftcReleaseCalendar.Report.Tff) >= r.AddDays(3));
        }
    }
}
