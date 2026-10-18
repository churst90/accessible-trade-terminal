using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The round-2 Market Cipher setup grader (<c>docs/SETUP_GRADER_ROUND2_MC_PREREG.md</c>,
    /// <c>setup-grader-mc2</c>). These pin the properties the study's numbers depend on, not the
    /// spelling of any line: the trigger-wave entry never fills before its trigger bar has closed;
    /// nothing at bar t (context, dot, anchor, void, trigger, the two secondary waits) changes when
    /// the bars after t are removed; the higher-timeframe context at a bar comes only from completed
    /// higher bars; the anchor trough includes the wave's lag after the dot, a deeper wave voids the
    /// setup and a higher cross with money flow falling is skipped; the noise function keeps each
    /// bar's opening gap; every trade's control is drawn from bars in the same context, read at the
    /// trade's own signal bar; and R is per position from the initial risk after the stop moves.
    /// Each was seen red with its defect put back.
    /// </summary>
    [Collection(nameof(LabCipherProfileTests))]
    public class SetupGraderMc2Tests
    {
        private static readonly DateTime Monday = new(2016, 1, 4, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>A long random walk with slow regimes (the weekly money flow turns) and fast
        /// cycles (the daily wave reaches -60, Buy dots fire, trigger waves follow).</summary>
        private static List<Ohlcv> Walk(int n = 2400, int seed = 7)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(n);
            double px = 100;
            for (int i = 0; i < n; i++)
            {
                // A 40-bar motif: a plunge (the anchor wave), a bounce, a smaller and slower dip (the
                // trigger wave), a rally, a drift; the slow regime turns the weekly money flow.
                int m = i % 40;
                double motif = m < 6 ? -0.03 : m < 11 ? 0.02 : m < 16 ? -0.012 : m < 28 ? 0.015 : 0.002 * Math.Sin(m);
                double drift = 0.006 * Math.Sin(i / 150.0) + motif + 0.012 * (rng.NextDouble() - 0.5);
                double next = px * Math.Exp(drift);
                double o = px * (1 + 0.006 * (rng.NextDouble() - 0.5));
                double hi = Math.Max(o, next) * (1 + 0.012 * rng.NextDouble());
                double lo = Math.Min(o, next) * (1 - 0.012 * rng.NextDouble());
                bars.Add(new Ohlcv(Monday.AddDays(i), o, hi, lo, next, 1000 + rng.Next(1000)));
                px = next;
            }
            return bars;
        }

        private static IIndicatorEngine Engine() => LabHost.Build().Services.GetRequiredService<IIndicatorEngine>();
        private static Mc2Series Build(IIndicatorEngine e, List<Ohlcv> bars) =>
            SetupGraderMc2Core.BuildAsync(e, "T", bars, GraderSeriesConfig.DailyCrypto).Result;

        private static IEnumerable<int> Dots(Mc2Series s) => Enumerable.Range(0, s.Bars.Count).Where(i => s.BuyDot[i]);

        // ── E1 never enters before its trigger bar has closed ────────────────

        [Fact]
        public void TheTriggerWaveEntryNeverFillsBeforeItsTriggerBarHasClosed()
        {
            var engine = Engine();
            var bars = Walk();
            var full = Build(engine, bars);
            var run = SetupGraderMc2Command.BuildRun("T", "crypto", full);
            SetupGraderMc2Command.AddOutcomes(run);

            // Every booked trigger-wave trade is entered at bar j + 1's open, after bar j has closed.
            int booked = 0;
            foreach (var su in run.Setups.Where(x => x.E1 != null))
            {
                int j = su.Path.E1Bar;
                Assert.Equal(j, su.E1!.Sig);
                Assert.True(j > su.Dot, "the trigger must come after the dot");
                double cost = SetupGraderCore.CostR(bars[j + 1].Open, full.Atr[j]);
                Assert.Equal(SetupGraderCore.StandardR(bars, j, full.Atr[j], true) - cost, su.E1.R, 9);
                booked++;
            }

            // Every trigger the full series finds is found, at the same bar, with the series cut at
            // that bar (nothing after j is read), and is not yet there with it cut one bar earlier.
            int checkedTriggers = 0;
            foreach (int i in Dots(full))
            {
                var f = SetupGraderMc2Core.Follow(full, i);
                if (f.E1 != WaitStatus.Confirmed) continue;
                int j = f.E1Bar;
                var p = SetupGraderMc2Core.Follow(Build(engine, bars.Take(j + 1).ToList()), i);
                Assert.True(p.E1 == WaitStatus.Confirmed && p.E1Bar == j, $"dot {i}: trigger {j} with the future, {p.E1} {p.E1Bar} without");
                var q = SetupGraderMc2Core.Follow(Build(engine, bars.Take(j).ToList()), i);
                Assert.True(q.E1 == WaitStatus.Pending, $"dot {i}: confirmed ({q.E1} at {q.E1Bar}) before its trigger bar {j} closed");
                if (++checkedTriggers >= 15) break;
            }
            Assert.True(checkedTriggers >= 5 && booked >= 1, $"only {checkedTriggers} triggers and {booked} booked trades were checked");
        }

        // ── Nothing at bar t changes when the bars after t are removed ───────

        private static bool ResolvedBy(WaitStatus st, int bar, int expiry, int t) =>
            st is WaitStatus.Confirmed or WaitStatus.Void ? bar <= t : st == WaitStatus.Expired && expiry <= t;

        [Fact]
        public void NothingAtBarTChangesWhenTheBarsAfterTAreRemoved()
        {
            var engine = Engine();
            var bars = Walk();
            var full = Build(engine, bars);
            var dots = Dots(full).ToList();
            var cuts = Enumerable.Range(0, bars.Count).Where(i => i > 700 && i < bars.Count - 1 &&
                    (i % 61 == 0 || dots.Contains(i - 3) || dots.Contains(i - 14) || dots.Contains(i - 31)))
                .Take(120).ToList();
            int ctxCompared = 0, e1Resolved = 0, voids = 0, confirmed = 0;
            var ctxSeen = new HashSet<int>();
            foreach (int t in cuts)
            {
                var cut = Build(engine, bars.Take(t + 1).ToList());
                for (int k = 0; k <= t; k++)
                {
                    Assert.Equal(full.BuyDot[k], cut.BuyDot[k]);
                    Assert.True(full.Ctx[k] == cut.Ctx[k], $"cut {t}, bar {k}: context {full.Ctx[k]} with the future, {cut.Ctx[k]} without");
                    if (full.Ctx[k] != SetupGraderMc2Core.Undef) { ctxCompared++; ctxSeen.Add(full.Ctx[k]); }
                }
                foreach (int i in dots.Where(d => d <= t && d >= t - 40))
                {
                    var f = SetupGraderMc2Core.Follow(full, i);
                    var c = SetupGraderMc2Core.Follow(cut, i);
                    Assert.Equal(f.AnchorStart, c.AnchorStart);
                    if (f.AnchorEnd >= 0 && f.AnchorEnd <= t)
                    {
                        Assert.Equal(f.AnchorEnd, c.AnchorEnd);
                        Assert.Equal(f.AnchorTrough, c.AnchorTrough);
                    }
                    int exp = i + SetupGraderMc2Core.Window;
                    foreach (var (fs, fb, cs, cb, e, name) in new[]
                             {
                                 (f.E1, f.E1 == WaitStatus.Void ? f.VoidBar : f.E1Bar, c.E1, c.E1 == WaitStatus.Void ? c.VoidBar : c.E1Bar, exp, "E1"),
                                 (f.E1h, f.E1hBar, c.E1h, c.E1hBar, i + 1, "E1h"),
                                 (f.E1m, f.E1mBar, c.E1m, c.E1mBar, exp, "E1m"),
                             })
                    {
                        if (ResolvedBy(fs, fb, e, t)) Assert.True(fs == cs && fb == cb, $"cut {t}, dot {i} {name}: {fs}@{fb} with the future, {cs}@{cb} without");
                        else Assert.True(cs == WaitStatus.Pending, $"cut {t}, dot {i} {name}: resolved as {cs}@{cb} before the data could show it ({fs}@{fb})");
                    }
                    if (ResolvedBy(f.E1, f.E1 == WaitStatus.Void ? f.VoidBar : f.E1Bar, exp, t) && f.AnchorStart >= 0)
                    {
                        e1Resolved++;
                        if (f.E1 == WaitStatus.Void) voids++;
                        if (f.E1 == WaitStatus.Confirmed) confirmed++;
                    }
                }
            }
            // Vacuity: real contexts of more than one kind, and resolved waits of both outcomes.
            Assert.True(ctxCompared > 1000, $"only {ctxCompared} defined contexts compared");
            Assert.True(ctxSeen.Count >= 3, $"contexts seen: {string.Join(",", ctxSeen)}");
            Assert.True(e1Resolved >= 10 && voids >= 1 && confirmed >= 1, $"resolved {e1Resolved}, void {voids}, confirmed {confirmed}");
        }

        // ── The higher-timeframe context comes only from completed higher bars ─

        [Fact]
        public void TheContextAtABarReadsOnlyCompletedHigherTimeframeBars()
        {
            var engine = Engine();
            var bars = Walk();
            var full = Build(engine, bars);
            var h = full.Htf;
            for (int k = 0; k < bars.Count; k++)
            {
                int w = h.UsableAt[k];
                if (w < 0) continue;
                Assert.True(h.LastLowerIndex[w] <= k, $"bar {k} reads a week that ends at bar {h.LastLowerIndex[w]}");
                Assert.True(h.End[w] <= bars[k].Date.AddDays(1), $"bar {k} reads an unfinished week");
            }
            // Wreck every bar after t, the rest of t's own week included: the context at t must not move.
            int compared = 0;
            var seen = new HashSet<int>();
            foreach (int t in Enumerable.Range(900, 1300).Where(i => i % 37 == 0))
            {
                var wrecked = bars.Take(t + 1).Concat(bars.Skip(t + 1).Select((b, k) =>
                {
                    double f = k % 2 == 0 ? 0.55 : 1.6;
                    return new Ohlcv(b.Date, b.Open * f, b.High * f * 1.05, b.Low * f * 0.95, b.Close * f, b.Volume * 3);
                })).ToList();
                var w = Build(engine, wrecked);
                Assert.True(full.Ctx[t] == w.Ctx[t], $"bar {t} ({bars[t].Date:ddd}): context {full.Ctx[t]} became {w.Ctx[t]} when later bars changed");
                Assert.Equal(full.HtfDotRecent[t], w.HtfDotRecent[t]);
                if (full.Ctx[t] != SetupGraderMc2Core.Undef) { compared++; seen.Add(full.Ctx[t]); }
            }
            Assert.True(compared >= 25 && seen.Count >= 3, $"compared {compared} defined contexts, {seen.Count} kinds");
        }

        // ── The anchor, the void rule and the skipped trigger, on hand-made waves ─

        /// <summary>WT2 values bar by bar, with the crosses flagged where given. WT1 = WT2 (E1h holds).</summary>
        private static (double[] Wt1, double[] Wt2, double[] Mf, bool[] Bull, bool[] Bear) Waves(double[] wt2, int[] bulls, int[] bears, double[] mf)
        {
            int n = wt2.Length;
            var bull = new bool[n]; var bear = new bool[n];
            foreach (int b in bulls) bull[b] = true;
            foreach (int b in bears) bear[b] = true;
            return ((double[])wt2.Clone(), wt2, mf, bull, bear);
        }

        private static double[] Line(int n, params (int Bar, double V)[] knots)
        {
            var y = new double[n];
            for (int k = 0; k + 1 < knots.Length; k++)
                for (int i = knots[k].Bar; i <= knots[k + 1].Bar; i++)
                    y[i] = knots[k].V + (knots[k + 1].V - knots[k].V) * (i - knots[k].Bar) / (knots[k + 1].Bar - knots[k].Bar);
            return y;
        }

        [Fact]
        public void TheAnchorTroughIncludesTheWavesLagAfterTheDot()
        {
            // Bear cross at 2; the dot (bull cross) at 10 with WT2 -66; WT2 keeps falling to -72 at 12
            // (its 3-bar lag); bear cross at 16. The next wave bottoms at -68 (a higher low than -72,
            // but lower than the -66 printed on the dot's bar) and crosses up at 24 with money flow rising.
            int n = 60;
            var wt2 = Line(n, (0, 20), (2, 10), (10, -66), (12, -72), (16, -20), (20, -68), (24, -50), (30, 10), (59, 10));
            var mf = Enumerable.Range(0, n).Select(i => -20.0 + i).ToArray();
            var w = Waves(wt2, new[] { 10, 24 }, new[] { 2, 16 }, mf);
            var p = SetupGraderMc2Core.Follow(10, w.Wt1, w.Wt2, w.Mf, w.Bull, w.Bear);

            Assert.Equal(2, p.AnchorStart);
            Assert.Equal(16, p.AnchorEnd);
            Assert.Equal(-72, p.AnchorTrough, 9);
            Assert.True(p.E1 == WaitStatus.Confirmed && p.E1Bar == 24, $"E1 {p.E1} at {p.E1Bar}, void {p.VoidBar}");
            Assert.Equal(-68, p.TriggerTrough, 9);
        }

        [Fact]
        public void ADeeperWaveVoidsTheSetupAndAHigherCrossWithMoneyFlowFallingIsSkipped()
        {
            int n = 70;
            // Skipped, then taken: a higher sub-zero cross at 22 with money flow falling is passed over;
            // the next higher wave crosses at 32 with money flow rising and is taken.
            var wt2 = Line(n, (0, 20), (2, 10), (10, -70), (12, -75), (16, -20), (20, -50), (22, -45), (26, -10), (29, -40), (32, -30), (40, 10), (69, 10));
            var mf = new double[n];
            for (int i = 0; i < n; i++) mf[i] = i <= 22 ? -10 - i : -40 + 2 * (i - 22);   // falling to bar 22, rising after
            var w = Waves(wt2, new[] { 10, 22, 32 }, new[] { 2, 16, 26 }, mf);
            var p = SetupGraderMc2Core.Follow(10, w.Wt1, w.Wt2, w.Mf, w.Bull, w.Bear);
            Assert.Equal(-75, p.AnchorTrough, 9);
            Assert.True(p.E1 == WaitStatus.Confirmed && p.E1Bar == 32, $"expected the cross at 32 (22 has money flow falling); got {p.E1} at {p.E1Bar}");

            // A cross whose wave bottoms at or above zero is not a trigger either.
            var wz = Line(n, (0, 20), (2, 10), (10, -70), (12, -75), (16, 20), (20, 5), (22, 15), (26, 30), (69, 30));
            var z = Waves(wz, new[] { 10, 22 }, new[] { 2, 16 }, Enumerable.Range(0, n).Select(i => (double)i).ToArray());
            var pz = SetupGraderMc2Core.Follow(10, z.Wt1, z.Wt2, z.Mf, z.Bull, z.Bear);
            Assert.Equal(WaitStatus.Expired, pz.E1);

            // Void: the next wave goes to -80, below the anchor's -75, before any higher cross. The
            // later higher cross at 40 must not be taken.
            var wv = Line(n, (0, 20), (2, 10), (10, -70), (12, -75), (16, -20), (21, -80), (24, -60), (30, -10), (36, -40), (40, -30), (50, 10), (69, 10));
            var v = Waves(wv, new[] { 10, 24, 40 }, new[] { 2, 16, 30 }, Enumerable.Range(0, n).Select(i => (double)i).ToArray());
            var pv = SetupGraderMc2Core.Follow(10, v.Wt1, v.Wt2, v.Mf, v.Bull, v.Bear);
            Assert.Equal(WaitStatus.Void, pv.E1);
            Assert.Equal(-1, pv.E1Bar);
            Assert.True(pv.VoidBar > 16 && pv.VoidBar <= 21 && wv[pv.VoidBar] <= -75, $"void at {pv.VoidBar}");
        }

        // ── The gap-keeping perturbation ─────────────────────────────────────

        [Fact]
        public void TheNewPerturbationKeepsEachBarsOpeningGap()
        {
            var bars = Walk(800, 3);
            var noisy = SetupGraderMc2Core.PerturbKeepGap(bars, 0.25, 1);
            var shared = GateCommand.Perturb(bars, 0.25, 1);
            int distinct = 0;
            for (int i = 1; i < bars.Count; i++)
            {
                double gap = bars[i].Open / bars[i - 1].Close;
                Assert.Equal(gap, noisy[i].Open / noisy[i - 1].Close, 9);
                Assert.Equal(shared[i].Close, noisy[i].Close, 9);        // the closes follow the shared path
                Assert.True(noisy[i].High >= Math.Max(noisy[i].Open, noisy[i].Close) - 1e-12);
                Assert.True(noisy[i].Low <= Math.Min(noisy[i].Open, noisy[i].Close) + 1e-12);
                Assert.Equal(bars[i].Date, noisy[i].Date);
                if (Math.Abs(noisy[i].Open - noisy[i].Close) > 1e-9 * noisy[i].Close) distinct++;
            }
            Assert.True(distinct > bars.Count * 9 / 10, $"only {distinct} noisy bars have an open distinct from the close");
            // The noise is real: the closes moved.
            Assert.True(Enumerable.Range(1, bars.Count - 1).Count(i => Math.Abs(noisy[i].Close / bars[i].Close - 1) > 1e-6) > bars.Count / 2);
            // The shared function is unchanged: it still sets the open equal to the close.
            Assert.All(shared.Skip(1), b => Assert.Equal(b.Close, b.Open));
        }

        // ── Every control is drawn from the trade's own context ──────────────

        [Fact]
        public void EveryTradesControlComesFromBarsInTheSameContextReadAtItsSignalBar()
        {
            var s = Build(Engine(), Walk());
            var run = SetupGraderMc2Command.BuildRun("T", "crypto", s);
            SetupGraderMc2Command.AddOutcomes(run);
            var trades = run.Setups.SelectMany(x => new[] { (x, t: x.E0), (x, t: x.E1), (x, t: x.E1h), (x, t: x.E1m) }).Where(z => z.t != null).ToList();
            Assert.True(trades.Select(z => z.t!.Ctx).Distinct().Count() >= 2, "the synthetic trades all share one context");
            Assert.Contains(trades, z => z.t != z.x.E0 && z.t!.Ctx != z.x.Grade);   // a wait that ended in another context
            foreach (var (su, t) in trades)
            {
                Assert.Equal(s.Ctx[t!.Sig], t.Ctx);
                var expected = Enumerable.Range(0, s.Bars.Count)
                    .Where(i => s.Ctx[i] == t.Ctx && run.Era[i] == t.Era && !double.IsNaN(run.R[i])).ToList();
                Assert.NotEmpty(expected);
                Assert.Equal(expected.Average(i => run.R[i]), t.Ctl, 9);
            }
            Assert.All(run.Setups, x => Assert.Equal(s.Ctx[x.Dot], x.Grade));
            foreach (var ((era, ctx, _), st) in run.Strata)
                Assert.All(st.Rows, i => { Assert.Equal(ctx, s.Ctx[i]); Assert.Equal(era, run.Era[i]); });
        }

        // ── R per position from the initial risk ─────────────────────────────

        [Fact]
        public void RIsPerPositionFromTheInitialRiskAfterTheStopMoves()
        {
            // 30 flat bars (range 2, so ATR = 2), signal on bar 29, entry at bar 30's open = 100.
            var bars = new List<Ohlcv>();
            for (int k = 0; k < 30; k++) bars.Add(new Ohlcv(Monday.AddDays(k), 100, 101, 99, 100, 1));
            bars.Add(new Ohlcv(Monday.AddDays(30), 100, 100.8, 99.2, 100.2, 1));   // entry bar
            bars.Add(new Ohlcv(Monday.AddDays(31), 100.2, 105, 100.5, 104.5, 1));  // 2R (104) hit: half off at +4, stop to entry
            bars.Add(new Ohlcv(Monday.AddDays(32), 100.2, 100.4, 99, 99.5, 1));    // stopped at the entry, 100
            for (int k = 33; k < 40; k++) bars.Add(new Ohlcv(Monday.AddDays(k), 99.5, 100, 99, 99.5, 1));
            var atr = AccessibleTrader.Sdk.Indicators.IndicatorMath.Atr(bars.ToArray(), 14);

            // The overload the round-2 harness calls. Initial stop 98 = 2 below the entry:
            // 0.5 x 4 + 0.5 x 0, over the initial 2 = 1R (the stop in force at the second exit is the entry).
            var r = SetupGraderCore.Ladder(bars, atr, 29, 98.0);
            Assert.Equal(1.0, r.RGross, 9);
            Assert.True(double.IsFinite(r.RNet) && r.RNet < r.RGross);
            Assert.Equal(2, r.Hold);
            // The stop-and-rule overload: the stop is never touched, so the trade runs to the end of the
            // data (bar 39's close, 99.5), over the same initial risk of 2.
            var x = SetupGraderCore.StopAndRule(bars, atr, 29, 98.0, null, 29 + 20);
            Assert.Equal((99.5 - 100.0) / 2.0, x.RGross, 9);
        }
    }
}
