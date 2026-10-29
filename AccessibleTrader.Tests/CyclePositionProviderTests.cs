using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Analysis;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cycle Position (2026-10-28): Cipher C's picture with one rule set and learned windows.</b>
///
/// <para>The golden answers are properties, not the C#: nothing said on a bar may change when
/// later bars arrive (the windows are learned from cycles known by then); a window is learned only
/// from enough past cycles; the phase is the day against the window; the hit rate scores each
/// cycle against the window it began with; off a daily chart every count is in bars. How well the
/// windows hold on 46 daily charts is measured in docs/CYCLE_POSITION_DESIGN.md.</para>
/// </summary>
public sealed class CyclePositionProviderTests
{
    private static readonly CyclePositionProvider Provider = new();
    private const string D = CipherCProvider.CompDailyWindow, W = CipherCProvider.CompWeeklyWindow, Y = CipherCProvider.CompYearlyWindow;

    private static Dictionary<string, double[]> Run(IReadOnlyList<Ohlcv> bars, string? symbol = "BTC/USD", Dictionary<string, object>? extra = null)
    {
        var p = new Dictionary<string, object>();
        if (symbol != null) p["__symbol"] = symbol;
        foreach (var (k, v) in extra ?? new()) p[k] = v;
        return CyclePositionProvider.Compute(bars.ToArray(), p);
    }

    private static Dictionary<string, double[]>? _btc;
    private static Dictionary<string, double[]> Btc() => _btc ??= Run(CipherSrSpeechTests.BtcDaily());

    private static List<int> Marked(double[] a) => Enumerable.Range(0, a.Length).Where(i => !double.IsNaN(a[i])).ToList();

    /// <summary>
    /// The windows are learned from the cycles known on each bar, the hit rate from the cycles
    /// ended by it: computing on the first N bars gives the same value on bar N-1 for every causal
    /// component and companion as computing on the whole history. The arc and the live band are
    /// pictures drawn from the last bar and are left out, as in Cipher C's test.
    /// </summary>
    [Fact]
    public void Nothing_on_a_bar_changes_when_later_bars_arrive()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var whole = Btc();
        var m = Provider.GetIndicators().Single();
        var causalComponents = m.Components.Where(c => (c.Causality ?? m.Causality) == ComponentCausality.Causal).Select(c => c.Name).ToList();
        var causal = whole.Keys.Where(k => (causalComponents.Contains(k) || ComponentCompanions.IsCompanionOf(k, causalComponents))
            && !k.EndsWith(ComponentCompanions.Path, StringComparison.Ordinal)
            && !k.EndsWith(ComponentCompanions.BoxStart, StringComparison.Ordinal) && !k.EndsWith(ComponentCompanions.BoxEnd, StringComparison.Ordinal)).ToList();
        foreach (var k in new[] { D + CyclePositionProvider.BottomedSuffix, D + CyclePositionProvider.HitsSuffix, D + CipherCProvider.StartSuffix,
                     W + CyclePositionProvider.NestSuffix, Y + CyclePositionProvider.PhaseSuffix,
                     D + CyclePositionProvider.LowInSuffix, D + CyclePositionProvider.ExpectedSuffix,
                     W + CyclePositionProvider.LowInSuffix, W + CyclePositionProvider.LowInLowSuffix, Y + CyclePositionProvider.LowInSuffix,
                     D + CyclePositionProvider.ConfluenceSuffix, D + CyclePositionProvider.AoSuffix, D + CyclePositionProvider.VwapSuffix,
                     D + CyclePositionProvider.TopSuffix, D + CyclePositionProvider.TopDaySuffix, D + CyclePositionProvider.TopCameSuffix,
                     D + CyclePositionProvider.TopWaitingSuffix, D + CyclePositionProvider.TopLeftSuffix, D + CyclePositionProvider.TopFailsSuffix,
                     D + CyclePositionProvider.TopFailsOtherSuffix, D + CyclePositionProvider.LeftTopsSuffix, D + CyclePositionProvider.RightTopsSuffix,
                     D + CyclePositionProvider.HalfSuffix, D + CyclePositionProvider.HalfDaySuffix, D + CyclePositionProvider.HalfGaveSuffix,
                     D + CyclePositionProvider.HalfFailsSuffix, W + CyclePositionProvider.CalledSuffix, W + CyclePositionProvider.CalledOfSuffix })
            Assert.Contains(k, causal);
        // Fixed cuts, plus one just after the last yearly low is confirmed and one just after a
        // daily window is first learned.
        var cuts = new List<int> { 3000, 3800, 4500, 5200 };
        int lastYearly = Marked(whole[CipherCProvider.CompYclConfirmed]).LastOrDefault(-1);
        Assert.True(lastYearly > 400, "no yearly low found from the price on Bitcoin");
        cuts.Add(lastYearly + 1);
        int firstLearned = Array.IndexOf(whole[D + CyclePositionProvider.LearnedSuffix], 1.0);
        Assert.True(firstLearned > 200, "the daily window is never learned on Bitcoin");
        cuts.Add(firstLearned + 30);
        var everCompared = new HashSet<string>();
        foreach (int cut in cuts)
        {
            var part = Run(bars.Take(cut).ToList());
            foreach (var c in causal)
                for (int i = cut - 400; i < cut; i++)
                {
                    if (!double.IsNaN(whole[c][i]) && whole[c][i] != 0) everCompared.Add(c);
                    Assert.True(Same(part[c][i], whole[c][i]), $"{c} on {bars[i].Date:yyyy-MM-dd}: {part[c][i]} with {cut} bars, {whole[c][i]} with all");
                }
        }
        // As in Cipher C: the weekly and yearly windows do not tint under the daily one, no yearly
        // window opens or closes in these stretches, and nothing replaces a yearly low. Bitcoin's
        // yearly window is never learned (A_window_is_learned_...), so its share, flag and hit rate
        // stay 0 or empty: the same code is checked on the daily and weekly windows. With the
        // confirmation mark and the very high marks on by default nothing is hidden on them.
        var unlearned = new[] { CyclePositionProvider.BottomedSuffix, CyclePositionProvider.LearnedSuffix, CyclePositionProvider.HitsSuffix,
            CyclePositionProvider.ScoredSuffix }.Select(x => Y + x).Append(CipherCProvider.CompDclConfirmed + ComponentCompanions.Hide)
            .Append(CyclePositionProvider.CompVeryHigh + ComponentCompanions.Hide).Append(CyclePositionProvider.CompWeeklyVeryHigh + ComponentCompanions.Hide).ToHashSet();
        var neverChecked = causal.Except(everCompared).Where(k => !unlearned.Contains(k) && k != W + ComponentCompanions.Tint
            && k != Y + ComponentCompanions.Tint && k != Y + ComponentCompanions.Stops
            && k != CipherCProvider.CompYclConfirmed + CipherCProvider.ReplacedSuffix
            && k != CipherCProvider.CompWclConfirmed + CipherCProvider.ReplacedSuffix
            // A top exactly at a cycle's middle is rare (122 of 7,647 daily cycles on 31 charts):
            // the count is exempt only while Bitcoin has none.
            && !(k == D + CyclePositionProvider.MidTopsSuffix && whole[k].All(v => v == 0))).ToList();
        Assert.True(neverChecked.Count == 0, "never compared: " + string.Join(", ", neverChecked));
        static bool Same(double a, double b) => double.IsNaN(a) ? double.IsNaN(b) : Math.Abs(a - b) < 1e-9;
    }

    /// <summary>
    /// A window is learned once it has <see cref="CyclePositionProvider.MinSamples"/> past cycles
    /// (6 daily, 4 weekly or yearly) and not before; until then it is the default (the shortest
    /// daily cycle to 1.25 times it, 48 to 60 days on Bitcoin).
    /// </summary>
    [Fact]
    public void A_window_is_learned_from_enough_past_cycles_and_is_the_default_before()
    {
        var r = Btc();
        foreach (var (comp, min) in new[] { (D, CyclePositionProvider.MinSamples[0]), (W, CyclePositionProvider.MinSamples[1]), (Y, CyclePositionProvider.MinSamples[2]) })
        {
            var samples = r[comp + CyclePositionProvider.SamplesSuffix];
            var learned = r[comp + CyclePositionProvider.LearnedSuffix];
            for (int t = 0; t < samples.Length; t++) Assert.Equal(samples[t] >= min ? 1 : 0, learned[t]);
            Assert.Contains(0.0, learned);
            // Bitcoin's history holds too few yearly cycles (lows from the price in 2015, 2018,
            // 2022) for its yearly window to be learned: it stays the default.
            if (comp != Y) Assert.Contains(1.0, learned);
            else Assert.DoesNotContain(1.0, learned);
        }
        int first = Array.IndexOf(r[D + CyclePositionProvider.LearnedSuffix], 1.0);
        Assert.Equal(48, r[D + CipherCProvider.StartSuffix][first - 1]);
        Assert.Equal(60, r[D + CipherCProvider.EndSuffix][first - 1]);
        // The learned window is not the default.
        var startsLater = r[D + CipherCProvider.StartSuffix].Skip(first).Distinct().ToList();
        Assert.True(startsLater.Count > 1 || startsLater[0] != 48, "the learned daily window never moved off the default");
    }

    /// <summary>
    /// The running hit rate: hits never exceed the cycles scored, neither ever falls, and each step
    /// in the count of scored cycles is on a bar a cycle became known (its second low confirmed).
    /// </summary>
    [Fact]
    public void The_hit_rate_only_grows_and_never_exceeds_the_cycles_scored()
    {
        var r = Btc();
        foreach (var comp in new[] { D, W, Y })
        {
            var hits = r[comp + CyclePositionProvider.HitsSuffix];
            var scored = r[comp + CyclePositionProvider.ScoredSuffix];
            for (int t = 0; t < hits.Length; t++)
            {
                Assert.True(hits[t] <= scored[t], $"{comp} bar {t}: {hits[t]} hits of {scored[t]}");
                if (t > 0) { Assert.True(hits[t] >= hits[t - 1]); Assert.True(scored[t] >= scored[t - 1]); }
            }
        }
        var d = r[D + CyclePositionProvider.ScoredSuffix];
        Assert.True(d[^1] >= 20, $"only {d[^1]} daily cycles scored on Bitcoin");
        // A window that held every cycle, or none, is not being scored.
        foreach (var comp in new[] { D, W })
            Assert.InRange(r[comp + CyclePositionProvider.HitsSuffix][^1], 1, r[comp + CyclePositionProvider.ScoredSuffix][^1] - 1);
        var known = r[D + CyclePositionProvider.SamplesSuffix];
        for (int t = 1; t < d.Length; t++) if (d[t] > d[t - 1]) Assert.True(known[t] > known[t - 1], $"bar {t}: scored without a cycle becoming known");
    }

    /// <summary>
    /// The learning worked by hand. Lows on bars 0, 10, 20 and 40, each confirmed 3 bars later;
    /// two cycles make a window here, from the 20th percentile to the longest. The cycles are 11,
    /// 11 and 21 days long (both lows counted), known on bars 13, 23 and 43. From bar 23 the window
    /// is 11 to 11; from bar 43, 11 to 21. The third cycle began on bar 23 with the 11-day window,
    /// so it is a miss, although the window learned with it would have held it: a cycle is scored
    /// against the window it began with. The first two began before any window was learned and are
    /// not scored. "Had come by now" on bar 43: on day 12 all three past cycles were still
    /// unconfirmed (their next lows confirmed on days 14, 14 and 24) and two had made their low; on
    /// day 15 only the third was, and it had not.
    /// </summary>
    [Fact]
    public void A_window_is_learned_and_scored_as_worked_by_hand()
    {
        var run = new CipherCEngine.TierRun();
        foreach (int bar in new[] { 0, 10, 20, 40 }) { run.Lows.Add(bar); run.ConfirmedAt.Add(bar + 3); }
        var r = CyclePositionProvider.Learn(run, 50, 1, 2, 0.2, 1.0, 5, 8);
        Assert.Equal((5.0, 8.0, false), (r.Start[22], r.End[22], r.IsLearned[22]));
        Assert.Equal((11.0, 11.0, true), (r.Start[23], r.End[23], r.IsLearned[23]));
        Assert.Equal((11.0, 21.0, 3), (r.Start[43], r.End[43], r.Samples[43]));
        Assert.Equal((0, 0), (r.Hits[42], r.Scored[42]));
        Assert.Equal((0, 1), (r.Hits[49], r.Scored[49]));
        Assert.Equal((2, 3), r.Bottomed(43, 12));
        Assert.Equal((0, 1), r.Bottomed(43, 15));
        Assert.Equal((2, 2), r.Bottomed(23, 12));
        // Percentiles are linear between ranks (numpy's default): the 80th of 11, 11, 21 is 17.
        Assert.Equal(17, CyclePositionProvider.Quantile(new List<double> { 11, 11, 21 }, 0.8), 9);
        Assert.Equal(11, CyclePositionProvider.Quantile(new List<double> { 11, 11, 21 }, 0.2), 9);
    }

    /// <summary>
    /// The phase is the cycle's day against the window on the same bar: early before its first day,
    /// due inside it, late after its last. On a daily chart the day is the count plus one.
    /// </summary>
    [Fact]
    public void The_phase_is_the_day_against_the_window()
    {
        var r = Btc();
        var seen = new HashSet<double>();
        foreach (var comp in new[] { D, W, Y })
        {
            var phase = r[comp + CyclePositionProvider.PhaseSuffix];
            var day = r[comp + CyclePositionProvider.DayOfSuffix];
            var start = r[comp + CipherCProvider.StartSuffix];
            var end = r[comp + CipherCProvider.EndSuffix];
            foreach (int t in Marked(phase))
            {
                double expect = day[t] < start[t] ? 0 : day[t] <= end[t] ? 1 : 2;
                Assert.True(expect == phase[t], $"{comp} bar {t}: day {day[t]}, window {start[t]}-{end[t]}, phase {phase[t]}");
                if (comp == D) seen.Add(phase[t]);
            }
        }
        Assert.Equal(new HashSet<double> { 0, 1, 2 }, seen);
        foreach (int t in Marked(r[D])) Assert.Equal(r[D][t] + 1, r[D + CyclePositionProvider.DayOfSuffix][t]);
    }

    /// <summary>
    /// "The low had come by now in X% of N past cycles": said only with at least
    /// <see cref="CyclePositionProvider.MinWaiting"/> past cycles, a share from 0 to 100, and only
    /// of the past cycles still unconfirmed that far in: a past cycle whose next low was confirmed
    /// earlier in it is not counted, so on a late day fewer are counted than are known.
    /// </summary>
    [Fact]
    public void The_low_had_come_share_needs_three_past_cycles()
    {
        var r = Btc();
        var bottomed = r[D + CyclePositionProvider.BottomedSuffix];
        var waiting = r[D + CyclePositionProvider.WaitingSuffix];
        Assert.NotEmpty(Marked(bottomed));
        for (int t = 0; t < bottomed.Length; t++)
        {
            if (double.IsNaN(bottomed[t])) { Assert.True(double.IsNaN(waiting[t]) || waiting[t] < CyclePositionProvider.MinWaiting); continue; }
            Assert.True(waiting[t] >= CyclePositionProvider.MinWaiting);
            Assert.InRange(bottomed[t], 0, 100);
        }
        Assert.Contains(0.0, bottomed);
        var samples = r[D + CyclePositionProvider.SamplesSuffix];
        Assert.All(Marked(waiting), t => Assert.True(waiting[t] <= samples[t]));
        Assert.Contains(Marked(waiting), t => waiting[t] < samples[t] && waiting[t] >= CyclePositionProvider.MinWaiting);
        Assert.True(bottomed.Where(v => !double.IsNaN(v)).Max() >= 50, "the low never 'had come' in half of past cycles");
    }

    /// <summary>
    /// Weekly lows are picked from the daily lows by time, so a weekly cycle holds however many
    /// daily cycles fit: the nest counts 1, 2, 3 and on, one step at a time, from the newest weekly
    /// low. A weekly low is known when the next daily low holds above it, so a new weekly cycle is
    /// usually in its second daily cycle when the count starts again (in its first when a lower
    /// daily low took the newest's place). A weekly candidate drawn since is flagged.
    /// Bitcoin's weekly lows are each at least half its shortest weekly cycle (22 weeks of 7 days)
    /// after the one before.
    /// </summary>
    [Fact]
    public void The_daily_cycle_of_the_weekly_one_counts_on_from_one()
    {
        var r = Btc();
        var nest = r[W + CyclePositionProvider.NestSuffix];
        var held = r[W + CyclePositionProvider.NestHeldSuffix];
        Assert.Equal(1, Marked(nest).Select(t => nest[t]).Min());
        Assert.True(Marked(nest).Select(t => nest[t]).Max() >= 4, "no weekly cycle held four daily cycles");
        Assert.Contains(1.0, held);
        Assert.Contains(0.0, held);
        int resets = 0;
        for (int t = 1; t < nest.Length; t++)
        {
            if (double.IsNaN(nest[t]) || double.IsNaN(nest[t - 1])) continue;
            if (nest[t] < nest[t - 1]) { Assert.InRange(nest[t], 1, 2); resets++; }
            else Assert.True(nest[t] - nest[t - 1] <= 1, $"bar {t}: the nest jumped {nest[t - 1]} -> {nest[t]}");
        }
        Assert.True(resets >= 10, $"only {resets} weekly cycles began");
        var weekly = Marked(r[CipherCProvider.CompWcl]);
        Assert.True(weekly.Count >= 10);
        double gap = CyclePositionProvider.WeeklyGapShare * 22 * 7;
        for (int k = 1; k < weekly.Count; k++)
            Assert.True(weekly[k] - weekly[k - 1] >= gap, $"weekly lows on bars {weekly[k - 1]} and {weekly[k]} are under {gap} bars apart");
        // The yearly tier counts its weekly cycles.
        Assert.True(Marked(r[Y + CyclePositionProvider.NestSuffix]).Select(t => r[Y + CyclePositionProvider.NestSuffix][t]).Max() >= 3);
    }

    /// <summary>A daily run whose lows are (bar, price, bar confirmed); every other bar's low is 1000.</summary>
    private static (CipherCEngine.TierRun Daily, Ohlcv[] Data) DailyRun(int n, params (int Bar, double Low, int At)[] lows)
    {
        var data = Enumerable.Range(0, n).Select(i => new Ohlcv(new DateTime(2020, 1, 1).AddDays(i), 1000, 1000, 1000, 1000, 0)).ToArray();
        var run = new CipherCEngine.TierRun
        {
            LastLowAt = Enumerable.Repeat(-1, n).ToArray(), ClosedUnder = Enumerable.Repeat(-1, n).ToArray(),
            Confirmed = CipherCEngine.Nan(n), Cancelled = CipherCEngine.Nan(n), Updated = CipherCEngine.Nan(n),
            Pending = CipherCEngine.Nan(n), Failed = CipherCEngine.Nan(n),
        };
        foreach (var (bar, low, at) in lows)
        {
            data[bar] = data[bar] with { Low = low };
            run.Confirmed[at] = low;
            for (int t = at; t < n; t++) run.LastLowAt[t] = bar;
            run.Lows.Add(bar); run.ConfirmedAt.Add(at); run.FoundAt.Add(at);
        }
        return (run, data);
    }

    /// <summary>
    /// The weekly rule by time, worked by hand with a 40-bar gap. 10 is the first weekly low; 30,
    /// lower and inside the gap, takes its place; 50, inside the gap and not lower, is passed over;
    /// 75, past the gap, is the candidate; 95, under it, cancels it and is the candidate; 115, not
    /// under it, makes 95 a weekly low and, only 20 bars on, is not a candidate; 140 is.
    /// </summary>
    [Fact]
    public void A_weekly_low_is_the_lowest_daily_low_past_the_gap()
    {
        var (daily, data) = DailyRun(160, (10, 100, 15), (30, 95, 35), (50, 98, 55), (75, 97, 80), (95, 96, 100), (115, 99, 120), (140, 99.5, 145));
        var w = CipherCEngine.WeeklyByTime(daily, data, 40);
        Assert.Equal(new[] { 30, 95, 140 }, w.Lows);
        Assert.Equal(new[] { 35, 120, -1 }, w.ConfirmedAt);
        Assert.Equal(new[] { 35, 100 }, Marked(w.Cancelled));
        Assert.Equal(100, w.Cancelled[35]); Assert.Equal(10, w.CancelledLow[35]);
        Assert.Equal(97, w.Cancelled[100]); Assert.Equal(75, w.CancelledLow[100]);
        Assert.Equal(95, w.NewestAt[159]); Assert.Equal(140, w.HeldAt[159]);
        Assert.Equal(30, w.LastLowAt[159]);
        Assert.Equal(-1, w.LastLowAt[119]);

        // A seed is the first newest weekly low, drawn nowhere: a daily low inside the gap above it
        // is passed over, one under it takes its place, drawn, and nothing is cancelled.
        var (d2, data2) = DailyRun(160, (10, 100, 15), (30, 95, 35), (75, 97, 80), (95, 99, 100));
        data2[0] = data2[0] with { Low = 96 };
        var seeded = CipherCEngine.WeeklyByTime(d2, data2, 40, seed: 0);
        Assert.Equal(new[] { 30, 75 }, seeded.Lows);
        Assert.Equal(new[] { 35, 100 }, seeded.ConfirmedAt);
        Assert.Empty(Marked(seeded.Cancelled));
    }

    /// <summary>What the bar detail says: the day of each cycle, the share, the window and its hit rate, and where the count came from.</summary>
    [Fact]
    public void The_bar_detail_says_where_the_chart_is_in_each_cycle()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        var r = Btc();
        int t = bars.Count - 1;
        var fact = Provider.GetDetailFact(CyclePositionProvider.Code, bars.ToArray(), r, t, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        Assert.Matches(@"Day \d+ of the daily cycle", fact);
        Assert.Matches(@"Week \d+ of the weekly cycle", fact);
        Assert.Matches(@"[\d.]+ years into the yearly cycle", fact);
        Assert.Matches(@"The daily window has held \d+ of \d+ cycles, \d+%", fact);
        Assert.Contains("this symbol's shortest", fact);
        Assert.Matches(@"(early|due|late)", fact);
    }

    /// <summary>The shortest daily cycle: the symbol's own (Bitcoin 48 days), else 32, or set by hand; and where it came from.</summary>
    [Fact]
    public void The_count_is_the_symbols_own_the_default_or_set_by_hand()
    {
        var bars = CipherSrSpeechTests.BtcDaily().Take(2000).ToList();
        var own = Run(bars);
        Assert.Equal(48, own[CyclePositionProvider.LengthKey][^1]);
        Assert.Equal(1, own[CyclePositionProvider.LengthSourceKey][^1]);
        var none = Run(bars, symbol: null);
        Assert.Equal(CipherCEngine.DefaultCountDailyMin, none[CyclePositionProvider.LengthKey][^1]);
        Assert.Equal(0, none[CyclePositionProvider.LengthSourceKey][^1]);
        var hand = Run(bars, extra: new() { [CyclePositionProvider.ParamDailyCycle] = 40.0 });
        Assert.Equal(40, hand[CyclePositionProvider.LengthKey][^1]);
        Assert.Equal(2, hand[CyclePositionProvider.LengthSourceKey][^1]);
        int first = Marked(hand[CipherCProvider.CompDailyWindow + CipherCProvider.StartSuffix])[0];
        Assert.Equal(40, hand[D + CipherCProvider.StartSuffix][first]);
        Assert.Equal(50, hand[D + CipherCProvider.EndSuffix][first]);
    }

    /// <summary>The same marks as Cipher C: every component of Cipher C, by the same name; the misses shown by default.</summary>
    [Fact]
    public void It_draws_with_Cipher_Cs_marks_and_shows_its_misses()
    {
        var mine = Provider.GetIndicators().Single();
        var cc = new CipherCProvider().GetIndicators().Single();
        // Cipher C's, then its own: the half-cycle low, drawn on its bar and found later; the very
        // high marks, on the bar they are said on; and the lines for strategies and alerts, left out.
        var own = new[] { (CyclePositionProvider.CompHalfLow, ComponentCausality.Lookahead),
                          (CyclePositionProvider.CompVeryHigh, ComponentCausality.Causal), (CyclePositionProvider.CompWeeklyVeryHigh, ComponentCausality.Causal),
                          (CyclePositionProvider.CompLowInLine, ComponentCausality.Causal), (CyclePositionProvider.CompWeeklyLowInLine, ComponentCausality.Causal),
                          (CyclePositionProvider.CompYearlyLowInLine, ComponentCausality.Causal), (CyclePositionProvider.CompExpectedLine, ComponentCausality.Causal),
                          (CyclePositionProvider.CompCallLine, ComponentCausality.Causal), (CyclePositionProvider.CompWeeklyCallLine, ComponentCausality.Causal) };
        Assert.Equal(cc.Components.Select(c => c.Name).Concat(own.Select(c => c.Item1)), mine.Components.Select(c => c.Name));
        Assert.Equal(cc.Components.Select(c => c.Causality).Concat(own.Select(c => (ComponentCausality?)c.Item2)), mine.Components.Select(c => c.Causality));
        Assert.True(On(CyclePositionProvider.ParamHalfLows));
        Assert.True(On(CyclePositionProvider.ParamVeryHighMarks));
        foreach (var c in mine.Components.Skip(cc.Components.Count + 3))
            Assert.True(c.DisplayType == ComponentDisplayType.Line && c.DefaultIsEnabled == false && c.DefaultIsDrawn == false && c.DefaultIsMuted == true, c.Name);
        foreach (var c in mine.Components.Skip(cc.Components.Count + 1).Take(2))
            Assert.True(c.DisplayType == ComponentDisplayType.Diamond && c.DefaultIsEnabled != false && c.DefaultIsDrawn != false, c.Name);
        bool On(string name) => (bool)mine.Parameters.Single(p => p.Name == name).DefaultValue!;
        Assert.True(On(CipherCProvider.ParamCancelledLows));
        Assert.True(On(CipherCProvider.ParamConfirmationMark));
        Assert.True(On(CipherCProvider.ParamTable));
        Assert.DoesNotContain(mine.Parameters, p => p.Name == CipherCProvider.ParamPreset);
        var r = Btc();
        Assert.NotEmpty(Marked(r[CipherCProvider.CompDcl]));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompWcl]));
        Assert.NotEmpty(Marked(r[CipherCProvider.CompYcl]));
    }

    /// <summary>
    /// Off a daily chart every tier counts in the chart's own bars, as Cipher C's does: the day of
    /// each cycle is the bars since its low plus one, and the speech says bars.
    /// </summary>
    [Fact]
    public void Off_a_daily_chart_every_count_is_in_bars()
    {
        // Bitcoin's daily bars, re-dated four hours apart: the same shapes on a 4-hour clock.
        var daily = CipherSrSpeechTests.BtcDaily();
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = daily.Select((b, i) => new Ohlcv { Date = start.AddHours(4 * i), Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume }).ToList();
        var r = Run(bars, symbol: null);
        Assert.Equal(1, r[CipherCProvider.InBarsKey][^1]);
        Assert.Equal(32, r[D + CipherCProvider.StartSuffix][Marked(r[D + CipherCProvider.StartSuffix])[0]]);
        foreach (var comp in new[] { D, W })
        {
            var day = r[comp + CyclePositionProvider.DayOfSuffix];
            Assert.NotEmpty(Marked(r[comp]));
            foreach (int t in Marked(r[comp])) Assert.Equal(r[comp][t] + 1, day[t]);
        }
        var fact = Provider.GetDetailFact(CyclePositionProvider.Code, bars.ToArray(), r, bars.Count - 1, new Dictionary<string, object>());
        Assert.Matches(@"Bar \d+ of the daily cycle", fact);
        Assert.DoesNotContain("Day ", fact);
        Assert.Contains("counted as bars", fact);
    }

    /// <summary>
    /// The original's confluence grade, while a daily low is held: Cipher C's own grade on those
    /// bars and on no others, its points summed (the Awesome Oscillator above zero, the close above
    /// the month's VWAP), said as the table shows it: "confluence: mid, the Awesome Oscillator above
    /// zero, the close not above this month's VWAP"; with no volume, no VWAP point.
    /// </summary>
    [Fact]
    public void The_confluence_grade_is_the_originals_while_a_low_is_held()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var grade = CipherCProvider.Grade(bars, bars.Select(b => b.High).ToArray(), bars.Select(b => b.Low).ToArray(), bars.Select(b => b.Close).ToArray());
        var tiers = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var c = r[D + CyclePositionProvider.ConfluenceSuffix];
        for (int t = 0; t < bars.Length; t++)
            Assert.Equal(tiers.D.HeldAt[t] >= 0 ? grade[t] : double.NaN, c[t]);
        foreach (int g in new[] { 0, 1, 2 }) Assert.Contains(Marked(c), t => c[t] == g);
        for (int t = 0; t < bars.Length; t++)
            if (!double.IsNaN(c[t])) Assert.Equal((r[D + CyclePositionProvider.AoSuffix][t] > 0 ? 1 : 0) + (r[D + CyclePositionProvider.VwapSuffix][t] > 0 ? 1 : 0), c[t]);

        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        string Cell(int t) => CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Confluence")[1].Text;
        int mid = Marked(c).Last(t => c[t] == 1);
        Assert.Equal("MID", Cell(mid));
        string said = Assert.Single(CyclePositionProvider.StateWords(comp, mid), w => w.StartsWith("confluence: ", StringComparison.Ordinal));
        Assert.Equal("confluence: mid, the Awesome Oscillator " + (r[D + CyclePositionProvider.AoSuffix][mid] > 0 ? "above zero, the close not above" : "not above zero, the close above")
            + " this month's VWAP", said);
        int free = Marked(r[D + CipherCProvider.StartSuffix]).Last(t => double.IsNaN(c[t]));
        Assert.Equal("—", Cell(free));
        Assert.DoesNotContain(CyclePositionProvider.StateWords(comp, free), w => w.StartsWith("confluence", StringComparison.Ordinal));

        var dry = Run(bars.Select(b => new Ohlcv(b.Date, b.Open, b.High, b.Low, b.Close, 0)).ToList());
        Func<string, double[]?> dryComp = k => dry.TryGetValue(k, out var a) ? a : null;
        var dc = dry[D + CyclePositionProvider.ConfluenceSuffix];
        Assert.DoesNotContain(Marked(dc), t => dc[t] == 2);
        int held = Marked(dc)[^1];
        Assert.EndsWith("no volume, so no VWAP", Assert.Single(CyclePositionProvider.StateWords(dryComp, held), w => w.StartsWith("confluence: ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The live windows' dates are the dates of the bars the band spans, past the last bar too: on
    /// a daily chart the bars Cipher C's clock would print (Bitcoin trades every day); on a 4-hour
    /// chart around the clock, four hours a bar on. Said as the window stands against the last
    /// bar: before it "opens … and closes …", in it "is open, to …", after it "closed …".
    /// </summary>
    [Fact]
    public void The_next_windows_dates_are_the_bars_its_band_spans()
    {
        var bars = CipherSrSpeechTests.BtcDaily();
        // Cut a day after a daily low is confirmed, so the next window lies past the last bar.
        var conf = Marked(Btc()[CipherCProvider.CompDclConfirmed]);
        int cut = conf[^2] + 2;
        var part = bars.Take(cut).ToList();
        var r = Run(part);
        var clock = new CipherCFutureClock(part.ToArray(), everyDay: true);
        int n = part.Count;
        DateTime Stamp(int i) => i < n ? part[i].Date : clock.Future(i - n + 1).Last();
        foreach (var comp in new[] { D, W, Y })
        {
            int a = n - 1 + (int)r[comp + ComponentCompanions.BoxStart][n - 1], b = n - 1 + (int)r[comp + ComponentCompanions.BoxEnd][n - 1];
            Assert.Equal(Stamp(a).ToOADate(), r[CyclePositionProvider.DatesKey(comp, false)][n - 1]);
            Assert.Equal(Stamp(b).ToOADate(), r[CyclePositionProvider.DatesKey(comp, true)][n - 1]);
            Assert.All(r[CyclePositionProvider.DatesKey(comp, false)].Take(n - 1), v => Assert.True(double.IsNaN(v)));
        }
        int da = n - 1 + (int)r[D + ComponentCompanions.BoxStart][n - 1];
        Assert.True(da >= n, "the daily window should open past the last bar here");
        Func<string, double[]?> comp0 = k => r.TryGetValue(k, out var v) ? v : null;
        string next = Assert.Single(CyclePositionProvider.StateWords(comp0, n - 1), w => w.StartsWith("the next daily low's window", StringComparison.Ordinal));
        Assert.Equal($"the next daily low's window opens {Stamp(da):MMMM d yyyy} and closes {Stamp(n - 1 + (int)r[D + ComponentCompanions.BoxEnd][n - 1]):MMMM d yyyy}", next);
        string cell = CyclePositionProvider.TableRows(comp0, n - 1).Single(row => row[0].Text == "Next low")[1].Text;
        Assert.Equal($"{Stamp(da):MMM d}–{Stamp(n - 1 + (int)r[D + ComponentCompanions.BoxEnd][n - 1]):MMM d}", cell);

        // The whole history, against its own last bar: each tier's words and cell as its stored dates stand.
        var whole = Btc();
        Func<string, double[]?> all = k => whole.TryGetValue(k, out var v) ? v : null;
        var lastRow = CyclePositionProvider.TableRows(all, bars.Count - 1).Single(row => row[0].Text == "Next low");
        foreach (var (tier, comp, col, fmt) in new[] { ("daily", D, 1, "MMM d"), ("weekly", W, 2, "MMM d yyyy"), ("yearly", Y, 3, "MMM yyyy") })
        {
            DateTime o = DateTime.FromOADate(whole[CyclePositionProvider.DatesKey(comp, false)][^1]), c = DateTime.FromOADate(whole[CyclePositionProvider.DatesKey(comp, true)][^1]);
            DateTime now = bars[^1].Date;
            string want = o > now ? $"the next {tier} low's window opens {o:MMMM d yyyy} and closes {c:MMMM d yyyy}"
                : c >= now ? $"the {tier} window is open, to {c:MMMM d yyyy}" : $"the {tier} window closed {c:MMMM d yyyy}";
            Assert.Contains(want, CyclePositionProvider.StateWords(all, bars.Count - 1));
            string F(DateTime x) => x.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(o > now ? $"{F(o)}–{F(c)}" : c >= now ? $"now–{F(c)}" : $"closed {F(c)}", lastRow[col].Text);
        }

        // In the window and after it, from the same stored dates.
        double opens = r[CyclePositionProvider.DatesKey(D, false)][n - 1], closes = r[CyclePositionProvider.DatesKey(D, true)][n - 1];
        Func<double, Func<string, double>> At = now => k => k == CyclePositionProvider.NowKey ? now : k == CyclePositionProvider.BarSecondsKey ? 86400
            : k == CyclePositionProvider.DatesKey(D, false) ? opens : k == CyclePositionProvider.DatesKey(D, true) ? closes : double.NaN;
        Assert.Equal($"the daily window is open, to {DateTime.FromOADate(closes):MMMM d yyyy}", CyclePositionProvider.NextWords("daily", At(opens), D));
        Assert.Equal($"the daily window is open, to {DateTime.FromOADate(closes):MMMM d yyyy}", CyclePositionProvider.NextWords("daily", At(closes), D));
        Assert.Equal($"the daily window closed {DateTime.FromOADate(closes):MMMM d yyyy}", CyclePositionProvider.NextWords("daily", At(closes + 1), D));
        Assert.Equal($"closed {DateTime.FromOADate(closes):MMM d}", CyclePositionProvider.NextCell(At(closes + 1), D));
        Assert.Null(CyclePositionProvider.NextWords("weekly", At(closes), W));

        // Four-hour bars around the clock: the bar four hours on, again and again.
        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var four = part.Select((x, i) => new Ohlcv { Date = start.AddHours(4 * i), Open = x.Open, High = x.High, Low = x.Low, Close = x.Close, Volume = x.Volume }).ToArray();
        var future = CyclePositionProvider.FutureStamps(four, 500, everyDay: true);
        for (int k = 0; k < future.Count; k++) Assert.Equal(start.AddHours(4 * (n + k)), future[k]);
        var r4 = Run(four, symbol: null);
        int b4 = n - 1 + (int)r4[D + ComponentCompanions.BoxEnd][n - 1];
        Assert.Equal(start.AddHours(4 * b4).ToOADate(), r4[CyclePositionProvider.DatesKey(D, true)][n - 1], 9);
        // Intraday, the time is said, on the user's clock as every intraday bar is.
        string? said4 = CyclePositionProvider.NextWords("daily", k => r4.TryGetValue(k, out var v) ? v[n - 1] : double.NaN, D);
        Assert.NotNull(said4);
        Assert.EndsWith(SpeechTimeFormatter.ToBarDisplay(start.AddHours(4 * b4), 4 * 3600).ToString("MMMM d, HH:mm", System.Globalization.CultureInfo.InvariantCulture), said4);
    }

    /// <summary>
    /// A weekday chart's 4-hour bars (six a day, Monday to Friday): the next week's bars are this
    /// week's again, a week on, the weekend skipped.
    /// </summary>
    [Fact]
    public void Past_the_last_bar_an_intraday_chart_keeps_its_sessions()
    {
        var bars = new List<Ohlcv>();
        for (var day = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc); bars.Count < 300; day = day.AddDays(1))
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                for (int h = 0; h < 6; h++) bars.Add(new Ohlcv(day.AddHours(4 * h), 1, 1, 1, 1, 0));
        var future = CyclePositionProvider.FutureStamps(bars.ToArray(), 61, everyDay: false);
        Assert.Equal(61, future.Count);
        Assert.All(future, t => Assert.True(t.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday), $"{t} is a weekend"));
        Assert.True(future.Zip(future.Skip(1)).All(p => p.Second > p.First));
        Assert.Equal(bars[^1].Date.AddDays(3).AddHours(-20), future[0]);   // Friday 20:00, then Monday 00:00
        Assert.Equal(future[0].AddDays(7), future[30]);
    }

    /// <summary>
    /// The daily cycle's top so far, worked from the bars: the highest high since the last
    /// confirmed daily low (the first of equal highs), the day it came on, whether it came before
    /// the cycle's middle (from half the window on), and the failure rates for its stage, said only
    /// while the cycle has not failed. The "top had come" share is the past cycles, known by the
    /// bar and not yet confirmed this far in, whose top had come by this day (from three of them).
    /// </summary>
    [Fact]
    public void The_top_so_far_is_worked_from_the_bars_and_past_cycles()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        var tiers = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" });
        var d = tiers.D;
        var lows = Enumerable.Range(0, d.Lows.Count).Where(k => d.ConfirmedAt[k] >= 0).Select(k => (Bar: d.Lows[k], At: d.ConfirmedAt[k])).OrderBy(x => x.Bar).ToList();
        var past = lows.Zip(lows.Skip(1), (x, y) => (At: Math.Max(x.At, y.At),
            Top: Enumerable.Range(x.Bar, y.Bar - x.Bar + 1).Aggregate((i, j) => bars[j].High > bars[i].High ? j : i) - x.Bar + 1,
            Conf: Math.Max(y.Bar, y.At) - x.Bar + 1)).ToList();
        var failedFor = new HashSet<int>();
        int seenLeft = 0, seenRight = 0, seenFails = 0, seenCame = 0;
        for (int t = 0; t < bars.Length; t++)
        {
            int a = d.LastLowAt[t];
            if (!double.IsNaN(d.Failed[t])) failedFor.Add(d.ClosedUnder[t]);
            if (a < 0 || a == d.Start) { Assert.True(double.IsNaN(r[D + CyclePositionProvider.TopSuffix][t])); continue; }
            int tp = Enumerable.Range(a, t - a + 1).Aggregate((i, j) => bars[j].High > bars[i].High ? j : i);
            Assert.Equal(bars[tp].High, r[D + CyclePositionProvider.TopSuffix][t]);
            Assert.Equal(tp - a + 1, r[D + CyclePositionProvider.TopDaySuffix][t]);
            double day = t - a + 1, mid = (r[D + CipherCProvider.StartSuffix][t] + r[D + CipherCProvider.EndSuffix][t]) / 2;
            double left = r[D + CyclePositionProvider.TopLeftSuffix][t], fails = r[D + CyclePositionProvider.TopFailsSuffix][t];
            if (day < 0.5 * mid) { Assert.True(double.IsNaN(left)); Assert.True(double.IsNaN(fails)); }
            else
            {
                Assert.Equal(2 * (tp - a) < mid ? 1 : 0, left);
                if (left == 1) seenLeft++; else seenRight++;
                var stage = CyclePositionProvider.TopFails.Where(s => day >= s.From * mid && day < s.To * mid).ToList();
                if (failedFor.Contains(a) || stage.Count == 0) Assert.True(double.IsNaN(fails));
                else
                {
                    seenFails++;
                    Assert.Equal(left == 1 ? stage[0].Left : stage[0].Right, fails);
                    Assert.Equal(left == 1 ? stage[0].Right : stage[0].Left, r[D + CyclePositionProvider.TopFailsOtherSuffix][t]);
                }
            }
            double came = r[D + CyclePositionProvider.TopCameSuffix][t];
            if (r[D + CyclePositionProvider.LearnedSuffix][t] == 1)
            {
                var still = past.Where(c => c.At <= t && c.Conf > day).ToList();
                Assert.Equal(still.Count, r[D + CyclePositionProvider.TopWaitingSuffix][t]);
                if (still.Count >= CyclePositionProvider.MinWaiting) { seenCame++; Assert.Equal(Math.Round(100.0 * still.Count(c => c.Top <= day) / still.Count), came); }
                else Assert.True(double.IsNaN(came));
            }
            else Assert.True(double.IsNaN(came));
        }
        Assert.True(seenLeft > 300 && seenRight > 300 && seenFails > 300 && seenCame > 1000, $"{seenLeft} {seenRight} {seenFails} {seenCame}");
        // The chart's finished cycles by where their top came (the first of equal highs; exactly
        // halfway is mid), counted once both lows are confirmed: on the last bar, every one.
        var sides = lows.Zip(lows.Skip(1), (x, y) => (Lead: 2 * (Enumerable.Range(x.Bar, y.Bar - x.Bar + 1).Aggregate((i, j) => bars[j].High > bars[i].High ? j : i) - x.Bar), Len: y.Bar - x.Bar)).ToList();
        Assert.Equal(sides.Count(c => c.Lead < c.Len), r[D + CyclePositionProvider.LeftTopsSuffix][^1]);
        Assert.Equal(sides.Count(c => c.Lead == c.Len), r[D + CyclePositionProvider.MidTopsSuffix][^1]);
        Assert.Equal(sides.Count(c => c.Lead > c.Len), r[D + CyclePositionProvider.RightTopsSuffix][^1]);
        int known = 3000;
        Assert.Equal(past.Count(c => c.At <= known), r[D + CyclePositionProvider.LeftTopsSuffix][known] + r[D + CyclePositionProvider.MidTopsSuffix][known] + r[D + CyclePositionProvider.RightTopsSuffix][known]);
        Assert.Contains($"this chart's daily tops: {r[D + CyclePositionProvider.LeftTopsSuffix][^1]} left, {r[D + CyclePositionProvider.MidTopsSuffix][^1]} mid, {r[D + CyclePositionProvider.RightTopsSuffix][^1]} right",
            CyclePositionProvider.StateWords(k => r.TryGetValue(k, out var v) ? v : null, bars.Length - 1, bars[^1].Date));
        Assert.True(failedFor.Count > 5);
    }

    /// <summary>
    /// The top so far, said as the table shows it, and a failed daily cycle's warning, in the
    /// bar's words and the failure mark's speech: the next daily low was a weekly one 56% of the
    /// time after a failed daily cycle, 26% after one that held (Cycle Position's lows on 31 daily
    /// charts, 3,182 and 4,465 cycles).
    /// </summary>
    [Fact]
    public void The_top_and_a_failed_cycle_are_said_as_shown()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        int t = Marked(r[D + CyclePositionProvider.TopFailsSuffix]).Last(i => r[D + CyclePositionProvider.TopLeftSuffix][i] == 1 && !double.IsNaN(r[D + CyclePositionProvider.TopCameSuffix][i]) && !double.IsNaN(r[D][i]));
        double V(string x) => r[D + x][t];
        double mid = (V(CipherCProvider.StartSuffix) + V(CipherCProvider.EndSuffix)) / 2;
        string said = Assert.Single(CyclePositionProvider.StateWords(comp, t), w => w.StartsWith("top so far: ", StringComparison.Ordinal));
        Assert.Equal($"top so far: {SpeechPriceFormatter.FormatPrice(V(CyclePositionProvider.TopSuffix))} on day {V(CyclePositionProvider.TopDaySuffix)}, before the cycle's middle (day {(mid / 2).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} of about {mid.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}), left-translated so far; "
            + $"cycles this far in with the top this early went on to fail {V(CyclePositionProvider.TopFailsSuffix)}% of the time, against {V(CyclePositionProvider.TopFailsOtherSuffix)}% with a later top; "
            + $"the top had come by this day in {V(CyclePositionProvider.TopCameSuffix)}% of {V(CyclePositionProvider.TopWaitingSuffix)} past cycles this far in unconfirmed", said);
        Assert.Equal($"day {V(CyclePositionProvider.TopDaySuffix)}, LEFT, came {V(CyclePositionProvider.TopCameSuffix)}%",
            CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Top so far")[1].Text);
        Assert.EndsWith("; " + said, Provider.GetComponentSpeech(D, r[D][t], bars[t], r, t));

        const string why = "so a weekly low is likely next: after a failed daily cycle the next daily low was a weekly low 56% of the time, against 26% after one that held";
        int f = Marked(r[CipherCProvider.CompFailed]).Last(i => r[CipherCProvider.EventsTierKey][i] == 1);
        Assert.Contains("daily cycle failed, " + why, CyclePositionProvider.StateWords(comp, f));
        Assert.EndsWith(", " + why + ".", Provider.GetComponentSpeech(CipherCProvider.CompFailed, r[CipherCProvider.CompFailed][f], bars[f], r, f)!);
        Assert.Contains(CyclePositionProvider.StateWords(comp, f + 1), w => w.StartsWith("the daily cycle failed 1 day ago, so a weekly low", StringComparison.Ordinal));
        // Measured on daily cycles only: a weekly cycle's failure says nothing more.
        Func<string, double[]?> weekly = k => k == CipherCProvider.EventsTierKey ? new[] { 2.0 } : k == CipherCProvider.CompFailed ? new[] { 1.0 } : null;
        Assert.Contains("weekly cycle failed", CyclePositionProvider.StateWords(weekly, 0));
    }

    /// <summary>
    /// A top exactly halfway between two lows is mid-translated, counted apart from the left and
    /// the right ones. Bitcoin has none, so one is made: a cycle's middle bar raised above it.
    /// </summary>
    [Fact]
    public void A_top_exactly_halfway_is_counted_as_mid()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var d = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" }).D;
        var lows = Enumerable.Range(0, d.Lows.Count).Where(k => d.ConfirmedAt[k] >= 0).Select(k => d.Lows[k]).Order().ToList();
        int k0 = Enumerable.Range(1, lows.Count - 1).First(k => (lows[k] - lows[k - 1]) % 2 == 0 && lows[k - 1] > 2000);
        int middle = (lows[k0 - 1] + lows[k0]) / 2;
        double peak = Enumerable.Range(lows[k0 - 1], lows[k0] - lows[k0 - 1] + 1).Max(i => bars[i].High);
        bars[middle] = new Ohlcv(bars[middle].Date, bars[middle].Open, peak * 1.001, bars[middle].Low, bars[middle].Close, bars[middle].Volume);
        var r = Run(bars);
        var d2 = CyclePositionProvider.BuildTiers(bars, new Dictionary<string, object> { ["__symbol"] = "BTC/USD" }).D;
        var lows2 = Enumerable.Range(0, d2.Lows.Count).Where(k => d2.ConfirmedAt[k] >= 0).Select(k => d2.Lows[k]).Order().ToList();
        var lead = lows2.Zip(lows2.Skip(1), (a, b) => (Lead: 2 * (Enumerable.Range(a, b - a + 1).Aggregate((i, j) => bars[j].High > bars[i].High ? j : i) - a), Len: b - a)).ToList();
        Assert.Contains(lead, c => c.Lead == c.Len);
        Assert.Equal(lead.Count(c => c.Lead == c.Len), r[D + CyclePositionProvider.MidTopsSuffix][^1]);
        Assert.Equal(lead.Count(c => c.Lead > c.Len), r[D + CyclePositionProvider.RightTopsSuffix][^1]);
        Assert.Equal(lead.Count(c => c.Lead < c.Len), r[D + CyclePositionProvider.LeftTopsSuffix][^1]);
    }

    /// <summary>
    /// The half-cycle low, from its definition, cycle by cycle: from each confirmed daily low
    /// <c>a</c> (not the Start), the first bar from 0.3 to 0.7 of the usual cycle in (the window's
    /// middle on the bar <c>a</c> became the newest low) whose low is above <c>a</c>'s, under every
    /// low of the k bars before it and no higher than every low of the k after (k = an eighth of the
    /// shortest cycle, at least 2). It is marked on its bar, found k bars on or when <c>a</c> is
    /// confirmed, whichever is later, and from then to the next daily low it is said with the share
    /// of the rise before it that it gave back and, until the cycle fails, the failure share of
    /// cycles that gave back as much (18, 31, 43, 49% from 0, 50, 70 and 80%).
    /// </summary>
    [Fact]
    public void The_half_cycle_low_is_found_as_defined_and_said_with_its_depth()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var lows = HalfByHand(bars, Btc());
        Assert.True(lows.Count > 50 && lows.Count(c => c.Gave >= 0.8) > 3, $"{lows.Count} half-cycle lows on Bitcoin");
        // A window that moves at nearly every new low (its full range): the middle is the one on
        // the bar the cycle's low became the newest, not the bar before.
        var wide = new Dictionary<string, object> { [CyclePositionProvider.ParamBandFrom] = 0.0, [CyclePositionProvider.ParamBandTo] = 100.0 };
        HalfByHand(bars, Run(bars, extra: wide), wide);

        // Cases Bitcoin's own bars do not have, made by changing one bar and worked again by hand.
        // A tie after it: the next bar's low equal to the half-cycle low's (229 on 31 charts).
        var c0 = lows[lows.Count / 2];
        var tie = (Ohlcv[])bars.Clone();
        var nb = tie[c0.Half + 1];
        tie[c0.Half + 1] = new Ohlcv(nb.Date, Math.Max(nb.Open, tie[c0.Half].Low), Math.Max(nb.High, tie[c0.Half].Low), tie[c0.Half].Low, Math.Max(nb.Close, tie[c0.Half].Low), nb.Volume);
        Assert.Contains(HalfByHand(tie, Run(tie)), c => c.Half == c0.Half);
        // Its own bar's high over the top before it: the top it gave back from includes it.
        var own = (Ohlcv[])bars.Clone();
        var hb = own[c0.Half];
        own[c0.Half] = new Ohlcv(hb.Date, hb.Open, c0.Top * 1.01, hb.Low, hb.Close, hb.Volume);
        Assert.Contains(HalfByHand(own, Run(own)), c => c.Half == c0.Half && c.Top == own[c0.Half].High);
        // Exactly on each band's edge (631 bars on 31 charts): the higher band.
        foreach (double edge in new[] { 0.5, 0.7, 0.8 })
        {
            bool hit = false;
            foreach (var c in lows.OrderBy(c => Math.Abs(c.Gave - edge)).Take(12))
            {
                var e = (Ohlcv[])bars.Clone();
                var b = e[c.Half];
                double low = c.Top - edge * (c.Top - bars[c.Low].Low);
                if (low > Math.Min(b.Open, b.Close)) continue;
                e[c.Half] = new Ohlcv(b.Date, b.Open, b.High, low, b.Close, b.Volume);
                if (HalfByHand(e, Run(e)).Any(x => x.Half == c.Half && x.Gave == edge)) { hit = true; break; }
            }
            Assert.True(hit, $"no half-cycle low made to give back exactly {edge}");
        }
    }

    /// <summary>
    /// Works the half-cycle lows by hand from their definition on <paramref name="bars"/> and
    /// checks every value <paramref name="r"/> holds for them, bar by bar; returns each one found.
    /// </summary>
    private static List<(int Low, int Half, double Top, double Gave)> HalfByHand(Ohlcv[] bars, Dictionary<string, double[]> r, Dictionary<string, object>? extra = null)
    {
        var p = new Dictionary<string, object> { ["__symbol"] = "BTC/USD" };
        foreach (var (key0, v) in extra ?? new()) p[key0] = v;
        var tiers = CyclePositionProvider.BuildTiers(bars, p);
        var d = tiers.D;
        int n = bars.Length, k = Math.Max(2, (int)Math.Round(tiers.L / 8, MidpointRounding.AwayFromZero));
        var marks = new double[n]; var lag = new double[n]; var price = new double[n]; var day = new double[n]; var gave = new double[n]; var fails = new double[n];
        foreach (var a0 in new[] { marks, lag, price, day, gave, fails }) Array.Fill(a0, double.NaN);
        var found = new List<(int Low, int Half, double Top, double Gave)>();
        for (int t0 = 0; t0 < n;)
        {
            int a = d.LastLowAt[t0];
            if (a < 0 || a == d.Start) { t0++; continue; }
            int t1 = t0;
            while (t1 < n && d.LastLowAt[t1] == a) t1++;
            double mid = (r[D + CipherCProvider.StartSuffix][t0] + r[D + CipherCProvider.EndSuffix][t0]) / 2;
            int? h = Enumerable.Range(0, n).Cast<int?>().FirstOrDefault(c => c >= a + 0.3 * mid && c >= a + k && c <= a + 0.7 * mid && c + k < n
                && bars[c!.Value].Low > bars[a].Low
                && Enumerable.Range(c.Value - k, k).All(j => bars[j].Low > bars[c.Value].Low)
                && Enumerable.Range(c.Value + 1, k).All(j => bars[j].Low >= bars[c.Value].Low));
            int at = h is { } hh ? Math.Max(t0, hh + k) : n;
            if (at < t1)
            {
                int hv = h!.Value;
                marks[hv] = bars[hv].Low; lag[hv] = at - hv;
                double top = Enumerable.Range(a, hv - a + 1).Max(i => bars[i].High), g = (top - bars[hv].Low) / (top - bars[a].Low);
                found.Add((a, hv, top, g));
                int failedAt = Enumerable.Range(t0, t1 - t0).FirstOrDefault(i => !double.IsNaN(d.Failed[i]) && d.ClosedUnder[i] == a, int.MaxValue);
                for (int t = at; t < t1; t++)
                {
                    price[t] = bars[hv].Low; day[t] = hv - a + 1; gave[t] = g;
                    if (t < failedAt) fails[t] = g < 0.5 ? 18 : g < 0.7 ? 31 : g < 0.8 ? 43 : 49;
                }
            }
            t0 = t1;
        }
        foreach (var (key, want) in new[]
                 {
                     (CyclePositionProvider.CompHalfLow, marks), (CyclePositionProvider.CompHalfLow + CyclePositionProvider.HalfFoundSuffix, lag),
                     (D + CyclePositionProvider.HalfSuffix, price), (D + CyclePositionProvider.HalfDaySuffix, day),
                     (D + CyclePositionProvider.HalfGaveSuffix, gave), (D + CyclePositionProvider.HalfFailsSuffix, fails),
                 })
            for (int t = 0; t < n; t++)
                Assert.True(double.IsNaN(want[t]) ? double.IsNaN(r[key][t]) : Math.Abs(want[t] - r[key][t]) < 1e-12, $"{key} on {bars[t].Date:yyyy-MM-dd}: {r[key][t]}, by hand {want[t]}");
        // A cycle that failed after its half-cycle low keeps the low but no longer the share.
        if (extra == null) Assert.Contains(Enumerable.Range(0, n), t => !double.IsNaN(gave[t]) && double.IsNaN(fails[t]));
        return found;
    }

    /// <summary>The half-cycle low said as the table shows it, in the bar's words, the daily window's and its own mark's.</summary>
    [Fact]
    public void The_half_cycle_low_is_said_as_shown()
    {
        var bars = CipherSrSpeechTests.BtcDaily().ToArray();
        var r = Btc();
        Func<string, double[]?> comp = k => r.TryGetValue(k, out var a) ? a : null;
        int t = Marked(r[D + CyclePositionProvider.HalfFailsSuffix]).Last(i => !double.IsNaN(r[D][i]));
        double V(string x) => r[D + x][t];
        string pct = Math.Round(100 * V(CyclePositionProvider.HalfGaveSuffix)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        double f = V(CyclePositionProvider.HalfFailsSuffix);
        string band = f switch { 18 => "under 50%", 31 => "50 to 70%", 43 => "70 to 80%", 49 => "80% or more", _ => throw new Xunit.Sdk.XunitException($"fails {f}") };
        string said = Assert.Single(CyclePositionProvider.StateWords(comp, t), w => w.StartsWith("half-cycle low: ", StringComparison.Ordinal));
        Assert.Equal($"half-cycle low: {SpeechPriceFormatter.FormatPrice(V(CyclePositionProvider.HalfSuffix))} on day {V(CyclePositionProvider.HalfDaySuffix)}, giving back {pct}% of the rise before it; "
            + $"cycles whose half-cycle low gave back {band} went on to fail {f}% of the time, against 30% of all", said);
        Assert.Equal($"day {V(CyclePositionProvider.HalfDaySuffix)}, gave {pct}%, fails {f}%",
            CyclePositionProvider.TableRows(comp, t).Single(row => row[0].Text == "Half-cycle low")[1].Text);
        Assert.EndsWith("; " + said, Provider.GetComponentSpeech(D, r[D][t], bars[t], r, t));
        // Every band in words.
        foreach (var (fails, words) in new[] { (18.0, "under 50%"), (31.0, "50 to 70%"), (43.0, "70 to 80%"), (49.0, "80% or more") })
        {
            Func<string, double> at = k => k == D + CyclePositionProvider.HalfSuffix ? 100 : k == D + CyclePositionProvider.HalfGaveSuffix ? 0.6
                : k == D + CyclePositionProvider.HalfFailsSuffix ? fails : k == D + CyclePositionProvider.HalfDaySuffix ? 20 : double.NaN;
            Assert.EndsWith($"gave back {words} went on to fail {fails}% of the time, against 30% of all", CyclePositionProvider.HalfWords(at));
        }
        // Once the cycle has failed: the low and its depth, no share.
        int failed = Marked(r[D + CyclePositionProvider.HalfGaveSuffix]).Last(i => double.IsNaN(r[D + CyclePositionProvider.HalfFailsSuffix][i]));
        string after = Assert.Single(CyclePositionProvider.StateWords(comp, failed), w => w.StartsWith("half-cycle low: ", StringComparison.Ordinal));
        Assert.EndsWith("% of the rise before it", after);
        Assert.DoesNotContain("fails", CyclePositionProvider.TableRows(comp, failed).Single(row => row[0].Text == "Half-cycle low")[1].Text);
        // The mark: its low, its day in the cycle and how much later it was found.
        int h = Marked(r[CyclePositionProvider.CompHalfLow]).Last();
        Assert.Equal($"Half-cycle low: the low of {SpeechPriceFormatter.FormatPrice(bars[h].Low)}, {CipherCProvider.A(r[D][h] + 1)} {r[D][h] + 1} day into the daily cycle, found {r[CyclePositionProvider.CompHalfLow + CyclePositionProvider.HalfFoundSuffix][h]} days later.",
            Provider.GetComponentSpeech(CyclePositionProvider.CompHalfLow, r[CyclePositionProvider.CompHalfLow][h], bars[h], r, h));
    }

    /// <summary>The half-cycle lows' switch hides their marks, not what is said: on by default, nothing hidden.</summary>
    [Fact]
    public void The_half_cycle_lows_switch_hides_only_their_marks()
    {
        var on = Btc();
        var off = Run(CipherSrSpeechTests.BtcDaily(), extra: new() { [CyclePositionProvider.ParamHalfLows] = false });
        var marked = Marked(on[CyclePositionProvider.CompHalfLow]);
        Assert.NotEmpty(marked);
        string hide = CyclePositionProvider.CompHalfLow + ComponentCompanions.Hide;
        Assert.All(on[hide], v => Assert.True(double.IsNaN(v)));
        for (int t = 0; t < off[hide].Length; t++)
            Assert.Equal(marked.Contains(t) ? ComponentCompanions.HideMarker : double.NaN, off[hide][t]);
        Assert.Equal(on[CyclePositionProvider.CompHalfLow], off[CyclePositionProvider.CompHalfLow]);
        Assert.Equal(on[D + CyclePositionProvider.HalfFailsSuffix], off[D + CyclePositionProvider.HalfFailsSuffix]);
    }
}
