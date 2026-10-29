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
                     W + CyclePositionProvider.LowInSuffix, W + CyclePositionProvider.LowInLowSuffix, Y + CyclePositionProvider.LowInSuffix })
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
        // confirmation mark on by default nothing is hidden on it.
        var unlearned = new[] { CyclePositionProvider.BottomedSuffix, CyclePositionProvider.LearnedSuffix, CyclePositionProvider.HitsSuffix,
            CyclePositionProvider.ScoredSuffix }.Select(x => Y + x).Append(CipherCProvider.CompDclConfirmed + ComponentCompanions.Hide).ToHashSet();
        var neverChecked = causal.Except(everCompared).Where(k => !unlearned.Contains(k) && k != W + ComponentCompanions.Tint
            && k != Y + ComponentCompanions.Tint && k != Y + ComponentCompanions.Stops
            && k != CipherCProvider.CompYclConfirmed + CipherCProvider.ReplacedSuffix
            && k != CipherCProvider.CompWclConfirmed + CipherCProvider.ReplacedSuffix).ToList();
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
        Assert.Equal(cc.Components.Select(c => c.Name), mine.Components.Select(c => c.Name));
        Assert.Equal(cc.Components.Select(c => c.Causality), mine.Components.Select(c => c.Causality));
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
}
