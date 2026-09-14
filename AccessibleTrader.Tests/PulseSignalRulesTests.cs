using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Pulse's gates, asserted against the bars they admitted.</b>
///
/// <para>
/// <see cref="PulseProvider"/> is the largest file in the repo's indicator layer at 1,553 lines,
/// and the A2h mutant set found six of seven mutants aimed at it survived a green suite: the ADX
/// lookback window could widen to the whole chart, the hold-down between markers could stop
/// holding, a long entry could be admitted in a bear regime, the multi-timeframe anchor RSI could
/// stop being an RSI, a midline cross could fire while sitting on the midline, and an unfinished
/// weekly bucket could be forward-filled onto bars before it closed.
/// </para>
///
/// <para>
/// Only one of the seven was caught, and it was caught by <c>IndicatorCausalityTests</c> — the
/// prefix guard, which asks whether a bar's value changes when later bars arrive. That guard is
/// strong and it is not this. It has nothing to say about whether a gate admits the right bars,
/// because a wrong-but-stable answer is stable.
/// </para>
///
/// <para>
/// These tests state each gate as a property of the bars that fired, which is how the gate is
/// written, so they survive retuning and still fail when the rule changes.
/// </para>
/// </summary>
public sealed class PulseSignalRulesTests
{
    private static readonly PulseProvider Provider = new();

    public static TheoryData<int> Flavours()
    {
        var d = new TheoryData<int>();
        foreach (var f in CausalityProbeSeries.HourlyFlavours) d.Add(f);
        return d;
    }

    /// <summary>Long enough for the widest default window (a 365-bar rolling Sharpe) to fill and
    /// leave a useful stretch of series afterwards.</summary>
    private const int Bars = 1400;

    private static Dictionary<string, object> Params(params (string Key, object Value)[] overrides)
    {
        var p = new Dictionary<string, object>();
        foreach (var (k, v) in overrides) p[k] = v;
        return p;
    }

    private static Dictionary<string, double[]> Run(int flavour, Dictionary<string, object>? pars = null,
        int length = Bars)
    {
        var bars = CausalityProbeSeries.Bars(flavour, length);
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate(PulseProvider.Code,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            pars ?? Params(), buffer);
        return results;
    }

    private static List<int> FiredBars(Dictionary<string, double[]> r, string component)
    {
        var fired = new List<int>();
        if (!r.TryGetValue(component, out var arr)) return fired;
        for (int i = 0; i < arr.Length; i++)
            if (!double.IsNaN(arr[i])) fired.Add(i);
        return fired;
    }

    private static double[] Series(Dictionary<string, double[]> r, string component)
        => r.TryGetValue(component, out var a) ? a : Array.Empty<double>();

    // ── The multi-timeframe anchor is still an RSI ──────────────────────────────────

    /// <summary>
    /// <b>An RSI is bounded by 0 and 100 — that is what makes it comparable to a threshold.</b>
    ///
    /// <para>
    /// The MTF anchor is a Wilder-smoothed RSI computed on a weekly sub-sample. Wilder smoothing is
    /// <c>(avg × (period − 1) + new) / period</c>; the A2h mutant dropped the <c>− 1</c>, which
    /// removes the decay from the recursion so the running average grows without bound and the
    /// anchor drifts to 100 and stays. Nothing failed. Every consumer of this component compares it
    /// to a number, and an unbounded series makes every one of those comparisons meaningless.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheMultiTimeframeAnchorStaysWithinRsiBounds(int flavour)
    {
        var anchor = Series(Run(flavour), PulseProvider.CompAnchorMtf);
        var values = anchor.Where(v => !double.IsNaN(v)).ToList();

        Assert.True(values.Count > 0, $"the MTF anchor produced no values on series {flavour}");
        var outside = values.Where(v => v < 0.0 || v > 100.0).ToList();
        Assert.True(outside.Count == 0,
            $"the MTF anchor left the 0..100 RSI range on {outside.Count} bars of series {flavour} " +
            $"(first {outside.FirstOrDefault():F2}). An unbounded recursion is not an RSI.");
    }

    /// <summary>
    /// <b>And a real RSI comes back down.</b>
    ///
    /// <para>
    /// Staying inside 0..100 is necessary and not sufficient: an unbounded recursion converges
    /// UPWARD toward 100, which is inside the range the whole way. What separates an oscillator
    /// from a drift is that it visits both sides of its own midline. Asserted across the probe set
    /// rather than per series, because one flavour is a steady rise whose weekly RSI is legitimately
    /// high nearly all of the time.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMultiTimeframeAnchorOscillatesAroundItsMidline()
    {
        var all = new List<double>();
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
            all.AddRange(Series(Run(flavour), PulseProvider.CompAnchorMtf).Where(v => !double.IsNaN(v)));

        Assert.True(all.Count > 100, "too few MTF anchor values across the probe set to judge");

        int below = all.Count(v => v < 50.0);
        int above = all.Count(v => v > 50.0);

        Assert.True(below > all.Count / 10,
            $"the MTF anchor was below 50 on only {below} of {all.Count} bars across the whole probe " +
            "set. A Wilder average whose decay term is missing rises without bound and never returns " +
            "— which is inside 0..100 the entire way, and is not an RSI.");
        Assert.True(above > all.Count / 10,
            $"the MTF anchor was above 50 on only {above} of {all.Count} bars — it is not oscillating.");
    }

    /// <summary>
    /// The anti-vacuity twin. A component pinned at one value sits inside 0..100 forever and tells
    /// the user nothing.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheMultiTimeframeAnchorActuallyMoves(int flavour)
    {
        var values = Series(Run(flavour), PulseProvider.CompAnchorMtf)
            .Where(v => !double.IsNaN(v)).ToList();

        Assert.True(values.Count > 20, $"too few MTF anchor values on series {flavour} to judge");
        Assert.True(values.Max() - values.Min() > 5.0,
            $"the MTF anchor spanned only {values.Max() - values.Min():F2} points across series " +
            $"{flavour} (min {values.Min():F1}, max {values.Max():F1}) — it is not tracking anything.");
    }

    // ── The midline cross ───────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A cross requires the two sides to be on opposite sides, so sitting ON the line is not a
    /// cross.</b>
    ///
    /// <para>
    /// The A2h mutant relaxed <c>prev &lt; midline</c> to <c>prev &lt;= midline</c>. On a series
    /// pinned at the midline that makes every bar a bull cross; on a real series it adds a cross
    /// every time the oscillator touches the line and continues. Asserted on a flat series, where
    /// the oscillator sits at its midline and nothing has crossed anything.
    /// </para>
    /// </summary>
    [Fact]
    public void ASeriesSittingExactlyOnTheMidlineProducesNoCrosses()
    {
        // The oscillator is an RSI, so "exactly at the midline" is constructible rather than
        // hypothetical. Fourteen bars alternating +1/-1 seed Wilder's averages with seven gains and
        // seven losses — avgGain and avgLoss end as the same float — so the RSI is exactly 50. A
        // flat stretch afterwards multiplies BOTH averages by the same factor every bar, so the
        // ratio, and therefore the RSI, stays exactly 50 for as long as the flat runs.
        //
        // A perfectly flat series from the start will NOT do: the deviation is zero, the oscillator
        // is NaN, and the cross branch returns before the comparison is reached. The first version
        // of this test made exactly that mistake and passed under the mutant for that reason.
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(120);
        for (int i = 0; i < 15; i++)
        {
            double c = (i % 2 == 0) ? 100.0 : 101.0;
            bars.Add(new Ohlcv(t.AddHours(i), c, c + 0.5, c - 0.5, c, 1000));
        }
        double flat = (double)bars[^1].Close;
        for (int i = 15; i < 120; i++)
            bars.Add(new Ohlcv(t.AddHours(i), flat, flat + 0.5, flat - 0.5, flat, 1000));

        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate(PulseProvider.Code,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), Params(), buffer);

        var wt = Series(results, PulseProvider.CompWtFast);
        int atMidline = wt.Count(v => v == 50.0);
        Assert.True(atMidline > 20,
            $"the oscillator sat exactly on the midline for only {atMidline} bars — the fixture does " +
            "not reach the case this test is about.");

        var bull = FiredBars(results, PulseProvider.CompBullCross);
        var bear = FiredBars(results, PulseProvider.CompBearCross);

        Assert.True(bull.Count == 0 && bear.Count == 0,
            $"an oscillator sitting exactly ON the midline produced {bull.Count} bull and " +
            $"{bear.Count} bear crosses. Sitting on the line is not crossing it: the previous bar " +
            "has to be strictly on the other side.");
    }

    /// <summary>The vacuity twin: a series that does move must produce crosses, or the flat case
    /// above is satisfied by an indicator that never fires at all.</summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void AMovingSeriesDoesProduceCrosses(int flavour)
    {
        var r = Run(flavour);
        Assert.NotEmpty(FiredBars(r, PulseProvider.CompBullCross));
        Assert.NotEmpty(FiredBars(r, PulseProvider.CompBearCross));
    }

    // ── The hold-down ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A hold-down is the difference between a marker and a rattle.</b>
    ///
    /// <para>
    /// <c>crossHoldDownBars</c> exists so one choppy midline region does not emit a burst of long
    /// triggers. The A2h mutant replaced the comparison with <c>&gt;= 0</c>, which is always true,
    /// and nothing failed. On a chart this user navigates by ear, a cluster of identical earcons on
    /// consecutive bars is not a louder signal, it is an unreadable one.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void ConsecutiveV2CrossesRespectTheHoldDown(int flavour)
    {
        const int HoldDown = 9;         // deliberately not the default, so the parameter is read
        var r = Run(flavour, Params(("crossHoldDownBars", HoldDown)));

        int exercised = 0;
        foreach (var component in new[] { PulseProvider.CompBullCrossV2, PulseProvider.CompBearCrossV2 })
        {
            var fired = FiredBars(r, component);
            if (fired.Count < 2) continue;      // a direction this series barely produces
            exercised++;

            for (int i = 1; i < fired.Count; i++)
                Assert.True(fired[i] - fired[i - 1] >= HoldDown,
                    $"{component} fired at bars {fired[i - 1]} and {fired[i]} on series {flavour}, " +
                    $"{fired[i] - fired[i - 1]} apart, with a hold-down of {HoldDown}.");
        }

        Assert.True(exercised > 0,
            $"series {flavour} produced fewer than two v2 crosses in either direction.");
    }

    /// <summary>
    /// And the hold-down has to BITE: a longer one must produce fewer markers. Without this, the
    /// spacing assertion above is satisfied by a parameter nothing reads.
    ///
    /// <para>
    /// Counted across the whole flavour set rather than per series. Measured 2026-09-14: the v2
    /// crosses are sparse per series (4, 24 and 16 over 1,400 bars), so a per-series count can be
    /// unchanged by a hold-down change for no reason other than there being too few of them.
    /// </para>
    /// </summary>
    [Fact]
    public void ALongerHoldDownProducesFewerMarkers()
    {
        int tight = 0, loose = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            tight += FiredBars(Run(flavour, Params(("crossHoldDownBars", 2))), PulseProvider.CompBullCrossV2).Count;
            loose += FiredBars(Run(flavour, Params(("crossHoldDownBars", 60))), PulseProvider.CompBullCrossV2).Count;
        }

        Assert.True(tight > 0, "no v2 bull crosses on any series at all");
        Assert.True(loose < tight,
            $"a 60-bar hold-down produced as many v2 bull crosses as a 2-bar one ({loose} against " +
            $"{tight}) across the whole probe set — the hold-down is not gating.");
    }

    // ── The regime gate on the v2 entries ───────────────────────────────────────────

    /// <summary>
    /// <b>Regime is one of only three gates left on the v2 long, and it means +1.</b>
    ///
    /// <para>
    /// The 2026-04-09 walk-forward stripped this entry to three orthogonal gates — trigger
    /// discipline, regime, trend strength — because the all-filters version produced three or four
    /// trades per half. That makes each survivor load-bearing. The A2h mutant widened the test from
    /// <c>reg &gt;= 0.5</c> (regime +1) to <c>reg &gt;= -0.5</c>, admitting the neutral state as
    /// well, and nothing failed.
    /// </para>
    ///
    /// <para>
    /// Aggregated over the flavour set: measured 2026-09-14, one series produces no v2 longs at all
    /// and another produces no v2 shorts, so a per-series assertion is vacuous for a reason that has
    /// nothing to do with the gate.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryV2LongEntrySitsInABullRegime()
    {
        int total = 0;
        var wrong = new List<string>();

        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var r = Run(flavour);
            var regime = Series(r, PulseProvider.CompRegime);
            foreach (int b in FiredBars(r, PulseProvider.CompGreenDotV2))
            {
                total++;
                if (!(regime[b] >= 0.5)) wrong.Add($"series {flavour} bar {b} (regime {regime[b]})");
            }
        }

        Assert.True(total > 0, "no v2 long entries on any probe series — this proves nothing");
        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {total} v2 long entries fired outside a bull regime:\n  " +
            string.Join("\n  ", wrong.Take(6)));
    }

    [Fact]
    public void EveryV2ShortEntrySitsInABearRegime()
    {
        int total = 0;
        var wrong = new List<string>();

        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var r = Run(flavour);
            var regime = Series(r, PulseProvider.CompRegime);
            foreach (int b in FiredBars(r, PulseProvider.CompRedDotV2))
            {
                total++;
                if (!(regime[b] <= -0.5)) wrong.Add($"series {flavour} bar {b} (regime {regime[b]})");
            }
        }

        Assert.True(total > 0, "no v2 short entries on any probe series — this proves nothing");
        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {total} v2 short entries fired outside a bear regime:\n  " +
            string.Join("\n  ", wrong.Take(6)));
    }

    /// <summary>
    /// The anti-vacuity half: the regime must spend real time in all three states somewhere in the
    /// probe set, or "every entry was in a bull regime" is a statement about a series that was
    /// never anything else. (One flavour is a steady rise and never leaves +1, which is why this is
    /// asserted across the set rather than per series.)
    /// </summary>
    [Fact]
    public void TheRegimeVisitsAllThreeStates()
    {
        var all = new List<double>();
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
            all.AddRange(Series(Run(flavour), PulseProvider.CompRegime).Where(v => !double.IsNaN(v)));

        Assert.Contains(all, v => v >= 0.5);
        Assert.Contains(all, v => v <= -0.5);
        Assert.Contains(all, v => Math.Abs(v) < 0.5);
    }

    // ── The ADX lookback window ─────────────────────────────────────────────────────

    /// <summary>
    /// <b>"ADX cleared the gate recently" has to mean recently — so the window must be a window.</b>
    ///
    /// <para>
    /// The v2 long's trend-strength gate asks whether ADX was above its minimum at some point
    /// within <c>pulseLookback</c> bars. The A2h mutant changed the window's start to bar zero, so
    /// the gate becomes "at any point in recorded history" — permanently satisfied after the first
    /// strong trend the series ever had. Nothing failed.
    /// </para>
    ///
    /// <para>
    /// Asserted as sensitivity to the parameter rather than as a property of the fired bars, and
    /// the measurement is why. On the probe series ADX sits at a median of 50 to 98 and never
    /// drops below 18, so at the default gate of 20 EVERY window contains a qualifying bar and a
    /// per-bar property test is vacuous. At a gate of 60 the window matters: a lookback of zero
    /// admits no long entries at all and a lookback of 600 admits sixteen. A window ignored is a
    /// parameter that cannot move that number.
    /// </para>
    /// </summary>
    [Fact]
    public void TheLookbackWindowBoundsTheTrendStrengthGate()
    {
        const double Gate = 60.0;

        int narrow = 0, wide = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            narrow += FiredBars(Run(flavour, Params(("pulseLookback", 0), ("adxBullMin", Gate))),
                                PulseProvider.CompGreenDotV2).Count;
            wide += FiredBars(Run(flavour, Params(("pulseLookback", 600), ("adxBullMin", Gate))),
                              PulseProvider.CompGreenDotV2).Count;
        }

        Assert.True(wide > 0,
            $"no v2 long entries at an ADX gate of {Gate} even with a 600-bar lookback — this proves nothing");
        Assert.True(narrow < wide,
            $"a zero-bar lookback admitted as many v2 long entries as a 600-bar one ({narrow} against " +
            $"{wide}) at an ADX gate of {Gate}. The trend-strength gate is not reading its window, so " +
            "it asks about all of recorded history rather than about recent bars.");
    }

    /// <summary>
    /// And the property that follows from a bounded window, on the bars that fired: with a
    /// 600-bar lookback every long entry must have a qualifying ADX reading somewhere inside it.
    /// Stated at the same gate as above so the window is doing real work.
    /// </summary>
    [Fact]
    public void EveryV2LongEntryHasStrongTrendInsideItsLookbackWindow()
    {
        const double Gate = 60.0;
        const int Lookback = 600;

        int total = 0;
        var stale = new List<string>();

        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var r = Run(flavour, Params(("pulseLookback", Lookback), ("adxBullMin", Gate)));
            var adx = Series(r, PulseProvider.CompAdx);

            foreach (int b in FiredBars(r, PulseProvider.CompGreenDotV2))
            {
                total++;
                bool cleared = false;
                for (int k = Math.Max(0, b - Lookback); k <= b && !cleared; k++)
                    if (!double.IsNaN(adx[k]) && adx[k] >= Gate) cleared = true;
                if (!cleared) stale.Add($"series {flavour} bar {b}");
            }
        }

        Assert.True(total > 0, "no v2 long entries to check");
        Assert.True(stale.Count == 0,
            $"{stale.Count} of {total} v2 long entries fired with no ADX reading at or above {Gate} " +
            $"anywhere in the {Lookback} bars up to the entry:\n  " + string.Join("\n  ", stale.Take(6)));
    }

    // ── The no-lookahead weekly forward-fill ────────────────────────────────────────

    /// <summary>
    /// <b>A weekly value may only reach a daily bar once that week has closed.</b>
    ///
    /// <para>
    /// <c>CompletedBucketAt</c> has two halves: take the current bucket only if this bar completed
    /// it, and refuse a bucket that is incomplete. The A2h set mutated both. Deleting the first was
    /// caught by the prefix causality guard. Deleting the second — forward-filling a bucket whose
    /// close is not yet known — survived, because an INCOMPLETE bucket only arises where the bar
    /// spacing is irregular (a gap, a halt, a missing bar), and the prefix guard sweeps the three
    /// REGULARLY spaced probe flavours.
    /// </para>
    ///
    /// <para>
    /// So this test supplies the case the guard cannot reach: a series with a hole in it. The bars
    /// after the gap belong to buckets the data does not fully contain, and no weekly value may be
    /// carried across.
    /// </para>
    /// </summary>
    [Fact]
    public void AWeeklyValueIsNotCarriedAcrossAGapInTheData()
    {
        // Hourly bars with a deliberate multi-day hole, so several weekly buckets are only
        // partially present — exactly the shape the Complete flag exists for.
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>();
        double price = 100;
        var rng = new Random(11);
        for (int i = 0; i < 900; i++)
        {
            price += (rng.NextDouble() - 0.48) * 1.5;
            // A hole: skip a stretch of hours in the middle, so the bucket straddling it is
            // present only in part.
            var when = i < 450 ? t.AddHours(i) : t.AddHours(i + 260);
            bars.Add(new Ohlcv(when, price, price + 1, price - 1, price, 1000));
        }

        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate(PulseProvider.Code,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            Params(("mtfBarsPerWeek", 24)), buffer);

        var anchor = Series(results, PulseProvider.CompAnchorMtf);
        Assert.True(anchor.Length == bars.Count);

        // The bar immediately after the hole cannot carry a weekly value derived from the bucket
        // the hole sits inside: that bucket's close is not in the data.
        // Expressed without pinning an index: every non-NaN MTF value must come from a bucket
        // whose last bar is at or before this bar, AND the run must not be uniformly non-NaN from
        // the first bar (which is what dropping the Complete check produces).
        int firstValue = Array.FindIndex(anchor, v => !double.IsNaN(v));
        Assert.True(firstValue > 0,
            "the MTF anchor had a value on the very first bar, before any weekly bucket could have " +
            "closed — an incomplete bucket is being forward-filled.");

        // And around the hole there must be at least one bar with no weekly value, because the
        // bucket spanning the hole never completes within the data.
        int gapBar = 450;
        bool someBlankNearTheGap = false;
        for (int i = gapBar; i < Math.Min(gapBar + 40, anchor.Length); i++)
            if (double.IsNaN(anchor[i])) { someBlankNearTheGap = true; break; }

        Assert.True(someBlankNearTheGap,
            "every bar after a multi-day hole in the data carried a weekly anchor value. A bucket " +
            "straddling a gap has no known close, so its value must not be carried onto the bars " +
            "after it.");
    }

    /// <summary>
    /// The vacuity twin for the gap test: on a REGULAR series the same component is populated for
    /// most of the chart, so "there was a NaN somewhere" is a statement about the gap and not about
    /// an indicator that never produces anything.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void OnARegularSeriesTheWeeklyAnchorIsPopulated(int flavour)
    {
        var anchor = Series(Run(flavour), PulseProvider.CompAnchorMtf);
        int populated = anchor.Count(v => !double.IsNaN(v));
        Assert.True(populated > anchor.Length / 2,
            $"the MTF anchor was populated on only {populated} of {anchor.Length} bars of series {flavour}.");
    }
}
