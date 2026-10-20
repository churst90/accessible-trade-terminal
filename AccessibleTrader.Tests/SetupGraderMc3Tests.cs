using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.StrategyLab;
using Microsoft.Extensions.DependencyInjection;
using Mc3 = AccessibleTrader.StrategyLab.SetupGraderMc3Command;

namespace AccessibleTrader.Tests
{
    /// <summary>
    /// The round-3 Market Cipher setup grader with Cipher SR location
    /// (<c>docs/SETUP_GRADER_ROUND3_MC_SR_PREREG.md</c>, <c>setup-grader-mc3</c>). These pin the
    /// properties the study's numbers depend on: the support used at bar t is the one live at bar
    /// t-1's close, so on a bar where a support is replaced the OLD level is the one tested; nothing at
    /// bar t (the level, the location flag, the level's age, the older/newer label) changes when later
    /// bars are removed; the location control is drawn from bars in the same context AND at support,
    /// read at the trade's own signal bar; XR never fills at a resistance that was not live when its
    /// bar opened; the noise rebuild recomputes Cipher SR on the noisy bars; a placebo offset is drawn
    /// once per level, at the bar it became live, 3 to 6 of that bar's ATR either way; the older/newer
    /// label compares the level's pivot bar with b(i), the anchor wave's start; and round 3 reproduces
    /// round 2's populations through round 2's own code. Each was seen red with its defect put back.
    /// The oracles below recompute from the indicator's RAW arrays and the bars, not from the
    /// harness's own derived arrays.
    /// </summary>
    [Collection(nameof(LabCipherProfileTests))]
    public class SetupGraderMc3Tests
    {
        private static readonly DateTime Monday = new(2016, 1, 4, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Round 2's synthetic walk: slow regimes and fast 40-bar plunge/bounce cycles.</summary>
        private static List<Ohlcv> Walk(int n = 2400, int seed = 7)
        {
            var rng = new Random(seed);
            var bars = new List<Ohlcv>(n);
            double px = 100;
            for (int i = 0; i < n; i++)
            {
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

        private static Mc2Series Mc2(IIndicatorEngine e, List<Ohlcv> bars) =>
            SetupGraderMc2Core.BuildAsync(e, "T", bars, GraderSeriesConfig.DailyCrypto).Result;

        private static Mc3Sr Sr(IIndicatorEngine e, Mc2Series s) =>
            SetupGraderMc3Core.BuildAsync(e, "T", s.Bars, s.Atr, "1d").Result;

        /// <summary>The indicator's raw arrays, straight from the engine.</summary>
        private static Dictionary<string, double[]> Raw(IIndicatorEngine e, List<Ohlcv> bars) =>
            e.CalculateAsync("CIPHER_SR", bars, new Dictionary<string, object> { ["__symbol"] = "T", ["__timeframe"] = "1d" }, default).Result;

        /// <summary>Section 2.1 by hand: at support against <paramref name="level"/>.</summary>
        private static int Hand(Ohlcv b, double level, double atr) =>
            double.IsNaN(level) || double.IsNaN(atr) || atr <= 0 ? SetupGraderMc3Core.Undef
            : b.Low <= level + 0.5 * atr && b.Close > level ? 1 : 0;

        private static Mc3.Run3 Run(IIndicatorEngine e, List<Ohlcv> bars)
        {
            var s = Mc2(e, bars);
            var run = new Mc3.Run3 { R2 = SetupGraderMc2Command.BuildRun("T", "crypto", s), Sr = Sr(e, s) };
            Mc3.BuildPositions(run);
            return run;
        }

        // ── 1. The support used at bar t is the one live at t-1's close ──────

        [Fact]
        public void OnABarWhereTheSupportIsReplacedTheOldLevelIsTheOneTested()
        {
            var engine = Engine();
            var bars = Walk();
            var s = Mc2(engine, bars);
            var sr = Sr(engine, s);
            var raw = Raw(engine, bars);
            var sup = raw[CipherSrProvider.CompSupportLine];
            var replaced = raw[CipherSrProvider.CompSupportReplaced];
            int replacements = 0, decisive = 0;
            for (int t = 1; t < bars.Count; t++)
            {
                if (double.IsNaN(replaced[t])) continue;
                replacements++;
                int old = Hand(bars[t], replaced[t], s.Atr[t]);
                int fresh = Hand(bars[t], sup[t], s.Atr[t]);
                Assert.True(sr.L[t] == old, $"bar {t}: support replaced ({replaced[t]:0.###} -> {sup[t]:0.###}); L {sr.L[t]}, but against the old level it is {old}");
                Assert.Equal(replaced[t], sr.SStar[t]);
                if (old != fresh) decisive++;
            }
            // On every other bar the level is the one the previous bar closed with.
            for (int t = 1; t < bars.Count; t++) Assert.True(sr.L[t] == Hand(bars[t], sup[t - 1], s.Atr[t]), $"bar {t}");
            // Vacuity: replacement bars where reading the new level would change the answer.
            Assert.True(replacements >= 50 && decisive >= 5, $"{replacements} replacement bars, {decisive} where the level read decides L");
        }

        // ── 2. Nothing at bar t changes when later bars are removed ──────────

        [Fact]
        public void TheLocationAtBarTIsUnchangedWhenLaterBarsAreRemoved()
        {
            var engine = Engine();
            var bars = Walk();
            var full = Run(engine, bars);
            var sr = full.Sr;
            var fresh = Enumerable.Range(1, bars.Count - 1).Where(t => sr.LiveBar[t] == t).ToList();
            var dots = full.R2.Setups.Select(x => x.Dot).ToHashSet();
            var cuts = Enumerable.Range(700, bars.Count - 701)
                .Where(t => t % 97 == 0 || fresh.Contains(t + 1) || dots.Contains(t) || dots.Contains(t - 9))
                .Where((t, k) => k % 3 == 0).Take(110).ToList();
            int compared = 0, at = 0, notAt = 0, older = 0, newer = 0, positions = 0;
            foreach (int t in cuts)
            {
                var cut = Run(engine, bars.Take(t + 1).ToList());
                for (int k = 1; k <= t; k++)
                {
                    Assert.True(full.Sr.SStar[k].Equals(cut.Sr.SStar[k]), $"cut {t}, bar {k}: S* {full.Sr.SStar[k]} with the future, {cut.Sr.SStar[k]} without");
                    Assert.True(full.Sr.L[k] == cut.Sr.L[k], $"cut {t}, bar {k}: L {full.Sr.L[k]} with the future, {cut.Sr.L[k]} without");
                    Assert.Equal(SetupGraderMc3Core.Age(full.Sr, k), SetupGraderMc3Core.Age(cut.Sr, k));
                    Assert.True(full.Sr.RStar[k].Equals(cut.Sr.RStar[k]), $"cut {t}, bar {k}: R*");
                    compared++;
                    if (full.Sr.L[k] == 1) at++; else if (full.Sr.L[k] == 0) notAt++;
                }
                // Positions whose signal bar is at or before t: same level, age and older/newer label.
                var cutPos = cut.E0.Concat(cut.E1).ToDictionary(p => (p.Setup.Dot, p.Pol));
                foreach (var p in full.E0.Concat(full.E1).Where(p => p.Sig <= t))
                {
                    if (!cutPos.TryGetValue((p.Setup.Dot, p.Pol), out var c)) continue;   // setup not eligible in the cut (too near its end)
                    Assert.True(p.L == c.L && p.Age == c.Age && p.Older == c.Older && p.Level.Equals(c.Level),
                        $"cut {t}, {p.Pol} at {p.Sig}: L/age/older {p.L}/{p.Age}/{p.Older} with the future, {c.L}/{c.Age}/{c.Older} without");
                    positions++;
                    if (p.Older) older++; else newer++;
                }
            }
            Assert.True(compared > 50_000 && at > 1000 && notAt > 1000, $"compared {compared}, at {at}, not {notAt}");
            Assert.True(positions >= 20 && older >= 1 && newer >= 1, $"positions {positions}, older {older}, newer {newer}");
        }

        // ── 3. The location control: same context AND at support, read at the signal bar ─

        [Fact]
        public void TheLocationControlComesFromBarsInTheSameContextAndAtSupportReadAtTheSignalBar()
        {
            var engine = Engine();
            int atTotal = 0, differs = 0, e1Moved = 0;
            var ctxs = new HashSet<int>();
            foreach (int seed in new[] { 7, 8, 9, 10, 11, 12 })
            {
                var bars = Walk(2400, seed);
                var run = Run(engine, bars);
                Mc3.AddOutcomes3(run);
                var s = run.S;
                var sup = Raw(engine, bars)[CipherSrProvider.CompSupportLine];
                int HandL(int i) => i < 1 ? SetupGraderMc3Core.Undef : Hand(bars[i], sup[i - 1], s.Atr[i]);
                var all = run.E0.Concat(run.E1).ToList();
                foreach (var p in all)
                {
                    Assert.Equal(p.T!.Sig, p.Sig);
                    Assert.True(p.L == HandL(p.T.Sig), $"seed {seed}, {p.Pol} trade at {p.T.Sig} (dot {p.Setup.Dot}): L {p.L}, but at its signal bar it is {HandL(p.T.Sig)}");
                }
                foreach (var p in all.Where(p => p.L == 1))
                {
                    var t = p.T!;
                    var rows = Enumerable.Range(0, bars.Count)
                        .Where(i => s.Ctx[i] == t.Ctx && run.R2.Era[i] == t.Era && !double.IsNaN(run.R2.R[i]) && HandL(i) == 1).ToList();
                    Assert.NotEmpty(rows);
                    Assert.Equal(rows.Average(i => run.R2.R[i]), p.LocCtl, 9);
                    Assert.Equal(t.R - p.LocCtl, p.LocEx, 9);
                    if (Math.Abs(p.LocCtl - t.Ctl) > 1e-9) differs++;
                    ctxs.Add(t.Ctx);
                    atTotal++;
                }
                e1Moved += run.E1.Count(p => HandL(p.Setup.Dot) != HandL(p.Sig));
            }
            // Vacuity: enough at-support trades, the location matters to the control, contexts vary, and
            // some E1 trades sit at a different location from their dot.
            Assert.True(atTotal >= 20, $"only {atTotal} at-support positions");
            Assert.True(differs > atTotal / 2, $"the location control differs from the context control on only {differs} of {atTotal}");
            Assert.True(ctxs.Count >= 2, "every at-support trade shares one context");
            Assert.True(e1Moved >= 3, $"only {e1Moved} E1 trades whose location differs from their dot's");
        }

        // ── 4. XR fills only at a resistance live when its bar opened ────────

        [Fact]
        public void XrNeverFillsAtAResistanceThatWasNotLiveWhenItsBarOpened()
        {
            var engine = Engine();
            var bars = Walk();
            var s = Mc2(engine, bars);
            var sr = Sr(engine, s);
            var res = Raw(engine, bars)[CipherSrProvider.CompResistanceLine];
            int fills = 0, stops = 0, caps = 0;
            for (int i = 100; i < bars.Count - 70; i += 3)
            {
                double entry = bars[i + 1].Open, stop = entry - 1.5 * s.Atr[i];
                var x = SetupGraderMc3Core.ResistanceExit(bars, s.Atr, sr.RStar, i, stop, i + 60);
                int k = i + 1 + x.Hold;
                double fill = x.RGross * (entry - stop) + entry;
                // Oracle, from the raw Resistance Zone: the stop first, then the resistance live at k-1's close.
                int exitBar = -1; double expect = double.NaN; string kind = "";
                for (int j = i + 1; j <= i + 60; j++)
                {
                    if (bars[j].Low <= stop) { exitBar = j; expect = Math.Min(bars[j].Open, stop); kind = "stop"; break; }
                    double r = res[j - 1];
                    if (!double.IsNaN(r) && r > entry && bars[j].High >= r) { exitBar = j; expect = Math.Max(bars[j].Open, r); kind = "res"; break; }
                    if (j == i + 60) { exitBar = j; expect = bars[j].Close; kind = "cap"; }
                }
                Assert.True(k == exitBar && Math.Abs(fill - expect) < 1e-7 * entry,
                    $"entry after bar {i}: exit at {k} for {fill:0.####}, expected {kind} at {exitBar} for {expect:0.####} (resistance at {k}: {res[k]:0.####}, at {k - 1}: {res[k - 1]:0.####})");
                if (kind == "res") fills++; else if (kind == "stop") stops++; else caps++;
            }
            Assert.True(fills >= 30 && stops >= 10, $"resistance fills {fills}, stops {stops}, caps {caps}");
        }

        // ── 5. The noise rebuild recomputes Cipher SR on the noisy bars ──────

        [Fact]
        public async Task TheNoiseRebuildRecomputesCipherSrOnTheNoisyBars()
        {
            var engine = Engine();
            var snaps = new List<(string Label, string Symbol, List<Ohlcv> Bars)> { ("T", "T", Walk(1600, 11)), ("U", "U", Walk(1600, 12)) };
            var clean = await Mc3.BuildClass3(engine, "crypto", snaps, GraderSeriesConfig.DailyCrypto);
            var noisy = await Mc3.NoisyClass(engine, "crypto", snaps, GraderSeriesConfig.DailyCrypto, 1);
            for (int k = 0; k < snaps.Count; k++)
            {
                var nb = noisy[k].S.Bars;
                Assert.True(Enumerable.Range(1, nb.Count - 1).Count(i => Math.Abs(nb[i].Close / snaps[k].Bars[i].Close - 1) > 1e-6) > nb.Count / 2, "the bars are not noisy");
                var sup = Raw(engine, nb)[CipherSrProvider.CompSupportLine];
                int differ = 0;
                for (int t = 1; t < nb.Count; t++)
                {
                    Assert.True(noisy[k].Sr.Sup[t].Equals(sup[t]), $"{snaps[k].Label} bar {t}: noisy support {noisy[k].Sr.Sup[t]}, Cipher SR on the noisy bars {sup[t]}");
                    Assert.True(noisy[k].Sr.L[t] == Hand(nb[t], sup[t - 1], noisy[k].S.Atr[t]), $"{snaps[k].Label} bar {t}: L not read on the noisy bars' own levels");
                    if (!clean[k].Sr.Sup[t].Equals(sup[t])) differ++;
                }
                Assert.True(differ > nb.Count / 2, $"{snaps[k].Label}: the noisy levels equal the clean ones on most bars ({differ} differ)");
            }
        }

        // ── 6. Placebo offsets: per level, drawn where it became live, 3-6 ATR ─

        [Fact]
        public void APlaceboOffsetIsFixedForALevelsWholeLifeAndDrawnAtTheBarItBecameLive()
        {
            var engine = Engine();
            var bars = Walk();
            var s = Mc2(engine, bars);
            var sr = Sr(engine, s);
            var raw = Raw(engine, bars);
            var sup = raw[CipherSrProvider.CompSupportLine];
            var since = raw[CipherSrProvider.CompSupportSince];
            int ups = 0, downs = 0, levels = 0, changed = 0;
            for (int d = 0; d < 5; d++)
            {
                var off = SetupGraderMc3Core.PlaceboOffsets(sr, s.Atr, new Random(1000 + d));
                var lp = SetupGraderMc3Core.PlaceboL(sr, bars, s.Atr, off);
                var born = new Dictionary<double, (int Bar, double Off)>();   // keyed by the level's _since time
                for (int t = 0; t < bars.Count; t++)
                {
                    if (double.IsNaN(sup[t])) { Assert.True(double.IsNaN(off[t])); continue; }
                    if (!born.TryGetValue(since[t], out var b))
                    {
                        // The first bar of this level's life: the offset is drawn here, scaled by this bar's ATR.
                        Assert.Equal(SetupGraderMc3Core.UnixSeconds(bars[t].Date), since[t], 3);
                        b = (t, off[t]); born[since[t]] = b;
                        if (!double.IsNaN(s.Atr[t]) && s.Atr[t] > 0)
                        {
                            double u = Math.Abs(off[t]) / s.Atr[t];
                            Assert.True(u >= 3 - 1e-9 && u <= 6 + 1e-9, $"bar {t}: offset {u:0.###} ATR");
                            if (off[t] > 0) ups++; else downs++;
                            levels++;
                        }
                    }
                    else Assert.True(off[t].Equals(b.Off), $"bar {t}: the level born at bar {b.Bar} moved its offset from {b.Off} to {off[t]}");
                    if (t >= 1)
                    {
                        double lev = double.IsNaN(sup[t - 1]) ? double.NaN : sup[t - 1] + off[t - 1];
                        Assert.True(lp[t] == Hand(bars[t], lev, s.Atr[t]), $"bar {t}: placebo L");
                        if (lp[t] != sr.L[t]) changed++;
                    }
                }
            }
            Assert.True(levels >= 100 && ups >= 20 && downs >= 20 && changed > 100, $"levels {levels}, up {ups}, down {downs}, changed {changed}");
        }

        // ── 7. Older/newer: the level's pivot bar against b(i) ───────────────

        [Fact]
        public void TheOlderLabelComparesTheLevelsPivotBarWithTheAnchorWavesStart()
        {
            var engine = Engine();
            var bars = Walk();
            var run = Run(engine, bars);
            var s = run.S;
            var sup = Raw(engine, bars)[CipherSrProvider.CompSupportLine];
            int older = 0, newer = 0, between = 0;
            foreach (var p in run.E0.Concat(run.E1).Where(p => p.L != SetupGraderMc3Core.Undef))
            {
                double level = sup[p.Sig - 1];
                // Oracle: the level is a bar's exact low; its pivot is the latest such bar before the level
                // was first on the chart (the run of equal support values ending at Sig - 1).
                int start = p.Sig - 1;
                while (start - 1 >= 0 && sup[start - 1].Equals(level)) start--;
                int pivot = Enumerable.Range(0, start).Last(k => bars[k].Low == level);
                int b = Enumerable.Range(0, p.Setup.Dot).Last(k => s.BearX[k]);
                Assert.True(p.Older == (pivot < b), $"{p.Pol} at {p.Sig}: pivot {pivot}, anchor start {b}, dot {p.Setup.Dot}: labelled {(p.Older ? "older" : "newer")}");
                if (p.Older) older++; else newer++;
                if (pivot >= b && pivot < p.Setup.Dot) between++;
            }
            Assert.True(older >= 5 && newer >= 5 && between >= 5, $"older {older}, newer {newer}, pivot inside the anchor wave {between}");
        }

        // ── 8. Round 3 reproduces round 2's populations through round 2's code ─

        [Fact]
        public async Task RoundThreeReproducesRoundTwosCountsThroughRoundTwosOwnCode()
        {
            var engine = Engine();
            var snaps = new List<(string Label, string Symbol, List<Ohlcv> Bars)> { ("T", "T", Walk(2400, 7)), ("U", "U", Walk(2000, 21)) };
            var r3 = await Mc3.BuildClass3(engine, "crypto", snaps, GraderSeriesConfig.DailyCrypto);
            var r2 = await SetupGraderMc2Command.BuildClass(engine, "crypto", snaps, GraderSeriesConfig.DailyCrypto);
            int setups = 0, e1 = 0, vetoed = 0;
            for (int k = 0; k < snaps.Count; k++)
            {
                var a = r3[k].R2.Setups; var b = r2[k].Setups;
                Assert.Equal(b.Count, a.Count);
                for (int j = 0; j < a.Count; j++)
                {
                    Assert.Equal(b[j].Dot, a[j].Dot); Assert.Equal(b[j].Grade, a[j].Grade); Assert.Equal(b[j].Era, a[j].Era);
                    Assert.Equal(b[j].Path.E1, a[j].Path.E1); Assert.Equal(b[j].Path.E1Bar, a[j].Path.E1Bar);
                }
                // Independently counted: development-era, non-vetoed setups and their confirmed triggers.
                setups += b.Count(x => x.Era == 0 && x.Grade != SetupGraderMc2Core.CtxV);
                e1 += b.Count(x => x.Era == 0 && x.Grade != SetupGraderMc2Core.CtxV && x.Path.E1 == WaitStatus.Confirmed);
                vetoed += b.Count(x => x.Era == 0 && x.Grade == SetupGraderMc2Core.CtxV);
            }
            Assert.True(setups >= 10 && e1 >= 3 && vetoed >= 1, $"setups {setups}, E1 {e1}, vetoed {vetoed}");
            Assert.Equal((setups, e1), Mc3.Round2Counts(r3));
            var built = new Dictionary<string, List<Mc3.Run3>> { ["crypto"] = r3 };
            Assert.Empty(Mc3.Reproduce(built, new[] { ("crypto", setups, e1) }, out _));
            Assert.NotEmpty(Mc3.Reproduce(built, new[] { ("crypto", setups + 1, e1) }, out _));
            Assert.NotEmpty(Mc3.Reproduce(built, new[] { ("crypto", setups, e1 - 1) }, out _));
            // The pre-registered targets are round 2's published numbers.
            Assert.Equal(new[] { ("crypto4h", 334, 158), ("crypto", 70, 33), ("equities", 1_423, 721) }, Mc3.Round2Dev);
        }
    }
}
