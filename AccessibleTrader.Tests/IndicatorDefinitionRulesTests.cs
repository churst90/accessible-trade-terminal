using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Each of these indicators computes the thing its name says, and says the thing it computed.</b>
///
/// <para>
/// Ten A2h mutants, across seven providers, all of which survived a green suite. What they have in
/// common is that none is a subtle tuning question — each one makes the indicator compute a
/// different quantity, or describe it with the opposite word:
/// </para>
///
/// <list type="bullet">
///   <item>the Hurst exponent computed on price DIFFERENCES rather than log returns, which makes a
///         scale-free statistic a function of the asset's price level;</item>
///   <item>an anchored VWAP weighting the bar's midpoint instead of its typical price, and
///         re-anchoring on a "pivot" that only looks backwards;</item>
///   <item>an <c>HMA</c> that is silently an <c>SMA</c>;</item>
///   <item>Fear and Greed reading each other's thresholds, and announcing a sentiment FLIP on the
///         first reading of a freshly loaded chart;</item>
///   <item>a spoken divergence hint naming the wrong direction;</item>
///   <item>a Tenkan/Kijun cross that keeps its marker after immediately reversing;</item>
///   <item>a moving-average cloud described as "expanding" on any increase at all;</item>
///   <item>a daily cycle low that does not have to be a low, and a flat stretch minting two
///         intermediate cycle lows;</item>
///   <item>and external data back-filled onto bars that predate its publication.</item>
/// </list>
///
/// <para>
/// Four of the seven providers here are named by no other test in the suite.
/// </para>
/// </summary>
public sealed class IndicatorDefinitionRulesTests
{
    private static List<Ohlcv> Bars(Func<int, (double High, double Low, double Close)> shape, int n,
        double volume = 1000)
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (h, l, c) = shape(i);
            bars.Add(new Ohlcv(t.AddHours(i), c, h, l, c, volume));
        }
        return bars;
    }

    private static Dictionary<string, double[]> Run(IIndicatorProvider provider, string code,
        IReadOnlyList<Ohlcv> bars, Dictionary<string, object>? pars = null)
    {
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        provider.Calculate(code,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan((List<Ohlcv>)bars),
            pars ?? new Dictionary<string, object>(), buffer);
        return results;
    }

    // ── Hurst: a scale-free statistic must be scale-free ────────────────────────────

    /// <summary>
    /// <b>On a geometric random walk with independent returns, the Hurst exponent is one half.</b>
    ///
    /// <para>
    /// That is the definition of the statistic: 0.5 means no memory, above means persistent, below
    /// means mean-reverting. It is also the only assertion that separates log returns from raw
    /// price differences, and the reason is worth recording because the obvious test does NOT work.
    /// </para>
    ///
    /// <para>
    /// <b>Scale invariance is not the discriminator.</b> Multiplying every price by ten multiplies
    /// every raw difference by ten as well, and rescaled-range analysis divides a range by a
    /// standard deviation — so both forms are invariant under it and the test passes either way.
    /// It was measured doing exactly that before this was rewritten.
    /// </para>
    ///
    /// <para>
    /// What separates them is a series whose moves are PROPORTIONAL to price. Here the price rises
    /// from 100 to several thousand with a constant log-return volatility: in log returns that is a
    /// stationary series and the exponent lands near 0.5, while in raw differences the early bars
    /// move by pennies and the late ones by tens, which reads as strong persistence and pushes the
    /// estimate far above a half.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHurstExponentOfAGeometricRandomWalkIsAboutOneHalf()
    {
        var provider = new HurstExponentProvider();

        // Independent log returns, constant volatility, a large price range. Seeded so a failure
        // reproduces exactly.
        var rng = new Random(20260914);
        double price = 100.0;
        var bars = new List<Ohlcv>(1200);
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 1200; i++)
        {
            // Box-Muller, so the returns are normal rather than uniform.
            double u1 = 1.0 - rng.NextDouble(), u2 = 1.0 - rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            price *= Math.Exp(0.004 + z * 0.02);
            bars.Add(new Ohlcv(t.AddHours(i), price, price * 1.004, price * 0.996, price, 1000));
        }

        Assert.True(bars[^1].Close > bars[0].Close * 5,
            "the walk did not travel far enough for the price level to matter — this proves nothing");

        var h = Run(provider, HurstExponentProvider.Code, bars)[HurstExponentProvider.CompHurst]
            .Where(v => !double.IsNaN(v)).ToList();

        Assert.True(h.Count > 50, $"only {h.Count} Hurst values were produced");
        double median = h.OrderBy(v => v).ElementAt(h.Count / 2);

        Assert.InRange(median, 0.35, 0.68);
    }

    // ── Anchored VWAP: the price it weights, and the anchor it weights from ─────────

    /// <summary>
    /// <b>A VWAP weights the TYPICAL price — high, low and close — not the bar's midpoint.</b>
    ///
    /// <para>
    /// Dropping the close ignores where the bar actually finished, so the level drifts away from
    /// the traded average it claims to be. Asserted by computing the answer independently from the
    /// anchor the provider chose, which is the only way to state "this is a volume-weighted mean of
    /// HLC3" without restating the implementation.
    /// </para>
    /// </summary>
    [Fact]
    public void TheAnchoredVwapIsAVolumeWeightedMeanOfTheTypicalPrice()
    {
        var provider = new AnchoredVwapProvider();

        // One clean swing high, then STRICTLY DECLINING highs so no second pivot can form and the
        // anchor never moves. That matters: the independent recomputation below assumes a single
        // accumulation window, and a first version of this fixture used a sine whose later peaks
        // re-anchored the VWAP — so the test failed on 114 bars for a reason that had nothing to do
        // with which price is being weighted.
        var bars = Bars(i =>
        {
            // The close is deliberately NOT the midpoint of the bar. A first version used
            // (c+2, c-2, c), for which HLC3 and (high+low)/2 are the SAME number — so the fixture
            // could not tell the typical price from the midpoint at all, which is the one thing
            // this test exists to do.
            if (i == 40) return (160.0, 150.0, 158.5);
            if (i < 40) { double up = 100.0 + i * 0.5; return (up + 3.0, up - 1.0, up + 2.4); }
            double c = 118.0 - (i - 41) * 0.2;
            return (c + 3.0, c - 1.0, c + 2.4);
        }, 300);

        var vwap = Run(provider, "ANCHORED_VWAP", bars)[AnchoredVwapProvider.CompFromHigh];

        int firstValue = Array.FindIndex(vwap, v => !double.IsNaN(v));
        Assert.True(firstValue >= 0, "the anchored VWAP produced no values at all");

        // Recompute from the provider's own anchor: the first bar it emitted a value on.
        double pv = 0, vol = 0;
        var mismatches = new List<string>();
        for (int i = firstValue; i < bars.Count; i++)
        {
            double typical = ((double)bars[i].High + (double)bars[i].Low + (double)bars[i].Close) / 3.0;
            double v = (double)bars[i].Volume;
            pv += typical * v;
            vol += v;
            if (double.IsNaN(vwap[i])) continue;

            double expected = pv / vol;
            if (Math.Abs(vwap[i] - expected) > 0.01)
                mismatches.Add($"bar {i}: {vwap[i]:F4} against HLC3-weighted {expected:F4}");
        }

        Assert.True(mismatches.Count == 0,
            $"the anchored VWAP differs from a volume-weighted mean of HLC3 on {mismatches.Count} " +
            $"bars:\n  " + string.Join("\n  ", mismatches.Take(5)));
    }

    /// <summary>
    /// <b>An anchor is a swing high, and a swing high is only a swing high because of the bars
    /// AFTER it.</b>
    ///
    /// <para>
    /// The A2h mutant removed the forward half of the pivot test, so every new high became an
    /// anchor. On a rising market the VWAP then re-anchors on the way up and the level the user is
    /// measuring against never settles — it simply follows price. Asserted on a steady uptrend:
    /// the anchor must hold, so the level must stay BELOW price rather than tracking it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHighAnchorDoesNotFollowPriceUpwards()
    {
        var provider = new AnchoredVwapProvider();

        // An early swing high, a pullback, then a long steady climb making new highs all the way.
        var bars = Bars(i =>
        {
            if (i == 40) return (140.0, 130.0, 135.0);
            if (i < 80) { double c = 110.0 - (i > 40 ? (i - 40) * 0.4 : 0); return (c + 1.5, c - 1.5, c); }
            double up = 95.0 + (i - 80) * 0.8;                 // new highs from ~bar 135 onwards
            return (up + 1.5, up - 1.5, up);
        }, 400);

        var vwap = Run(provider, "ANCHORED_VWAP", bars)[AnchoredVwapProvider.CompFromHigh];

        // In the last quarter price is far above where it started. A VWAP anchored at a fixed bar
        // lags well below it; one that re-anchors on every new high sits right on top of it.
        var gaps = new List<double>();
        for (int i = bars.Count * 3 / 4; i < bars.Count; i++)
            if (!double.IsNaN(vwap[i])) gaps.Add((double)bars[i].Close - vwap[i]);

        Assert.True(gaps.Count > 20, "too few anchored-VWAP values in the tail to judge");
        Assert.True(gaps.Average() > 5.0,
            $"in a sustained uptrend the anchored VWAP sat only {gaps.Average():F2} below price on " +
            "average. An anchor that re-establishes on every new high is not an anchor — the level " +
            "follows price instead of measuring against it.");
    }

    // ── The moving-average dispatch ─────────────────────────────────────────────────

    /// <summary>
    /// <b>Six moving-average types, six different answers.</b>
    ///
    /// <para>
    /// <c>MACloudProvider</c> offers six and the user picks one per component. The A2h mutant made
    /// <c>HMA</c> dispatch to <c>SMA</c>, so a setting that names a different calculation quietly
    /// produced another one. Asserted as mutual distinctness rather than against golden values, so
    /// the test says what the feature promises: these are six different things.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMovingAverageTypeProducesADifferentSeries()
    {
        var src = Enumerable.Range(0, 300)
            .Select(i => 100.0 + i * 0.4 + 8.0 * Math.Sin(i / 9.0))
            .ToArray();

        var types = new[] { "SMA", "EMA", "WMA", "HMA", "DEMA", "TEMA" };
        var byType = types.ToDictionary(t => t, t => MovingAverageHelper.Calculate(src, 20, t));

        foreach (var t in types)
            Assert.True(byType[t].Any(v => !double.IsNaN(v)), $"{t} produced nothing at all");

        for (int a = 0; a < types.Length; a++)
            for (int b = a + 1; b < types.Length; b++)
            {
                var x = byType[types[a]];
                var y = byType[types[b]];
                int compared = 0;
                double maxDiff = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    if (double.IsNaN(x[i]) || double.IsNaN(y[i])) continue;
                    compared++;
                    maxDiff = Math.Max(maxDiff, Math.Abs(x[i] - y[i]));
                }

                Assert.True(compared > 50, $"{types[a]} and {types[b]} shared too few bars to compare");
                Assert.True(maxDiff > 1e-6,
                    $"{types[a]} and {types[b]} produced the SAME series (largest difference " +
                    $"{maxDiff:E2} over {compared} bars). A user who picks one of them is choosing " +
                    "between two names for one calculation.");
            }
    }

    // ── The cross-series forward fill ───────────────────────────────────────────────

    /// <summary>
    /// <b>External data may not be written onto bars that predate it.</b>
    ///
    /// <para>
    /// <c>CrossSeriesForwardFill</c> is the single path every external series flows through —
    /// sentiment, COT positioning, funding, open interest, crowding. The A2h mutant removed the
    /// guard that leaves a bar untouched when the first tick is later than it, so the first
    /// reading is back-filled across the whole earlier chart: a Fear and Greed value on dates
    /// before that value existed, and a strategy leaf reading it.
    /// </para>
    ///
    /// <para>
    /// The causality contract could not catch this. It runs providers over synthetic OHLCV, and a
    /// cross-series provider with no external data to fetch produces nothing for it to compare.
    /// </para>
    /// </summary>
    [Fact]
    public void ExternalDataIsNotBackFilledOntoBarsThatPredateIt()
    {
        var bars = Bars(i => (101.0, 99.0, 100.0), 100);

        long TsOf(int i) => new DateTimeOffset(bars[i].Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

        // The series begins at bar 40. Everything before it is unknown, not zero and not the first
        // value.
        var ticks = new List<(long Ts, double Value)>
        {
            (TsOf(40), 25.0),
            (TsOf(70), 80.0),
        };

        var output = new double[bars.Count];
        Array.Fill(output, double.NaN);
        CrossSeriesForwardFill.Fill(ticks,
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars), output);

        var tooEarly = Enumerable.Range(0, 40).Where(i => !double.IsNaN(output[i])).ToList();
        Assert.True(tooEarly.Count == 0,
            $"{tooEarly.Count} bars before the first reading were given a value (first at bar " +
            $"{tooEarly.FirstOrDefault()}, value {(tooEarly.Count > 0 ? output[tooEarly[0]] : 0)}). " +
            "A bar cannot carry a number that had not been published yet.");

        // And the fill itself still works forwards.
        Assert.Equal(25.0, output[40]);
        Assert.Equal(25.0, output[69]);
        Assert.Equal(80.0, output[70]);
        Assert.Equal(80.0, output[99]);
    }

    // ── Fear and Greed ──────────────────────────────────────────────────────────────

    private static FearGreedProvider FearGreedWith(params (int BarIndex, double Value)[] readings)
    {
        var xs = Substitute.For<ICrossSeriesCache>();
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ticks = readings
            .Select(r => (Ts: new DateTimeOffset(t.AddHours(r.BarIndex), TimeSpan.Zero).ToUnixTimeMilliseconds(),
                          Value: r.Value))
            .ToList();
        xs.GetOrFetch(Arg.Any<CrossSeriesRequest>()).Returns((IReadOnlyList<(long, double)>)ticks);
        return new FearGreedProvider(xs);
    }

    /// <summary>
    /// <b>Extreme fear is not extreme greed.</b>
    ///
    /// <para>
    /// The whole output of this indicator is which of the two a reading is. The A2h mutant swapped
    /// the comparisons, so a reading of 10 landed in Greed and a reading of 90 in Fear. Nothing
    /// failed, because no test in the suite named this provider.
    /// </para>
    /// </summary>
    [Fact]
    public void ALowReadingIsFearAndAHighReadingIsGreed()
    {
        var bars = Bars(i => (101.0, 99.0, 100.0), 60);
        var provider = FearGreedWith((5, 12.0), (25, 50.0), (40, 88.0));

        var r = Run(provider, "FEAR_GREED", bars);
        var fear = r[FearGreedProvider.CompExtremeFear];
        var greed = r[FearGreedProvider.CompExtremeGreed];

        // Bar 10 is under the reading of 12; bar 45 under the reading of 88.
        Assert.True(!double.IsNaN(fear[10]) && fear[10] <= 25.0,
            $"a reading of 12 was not marked as extreme fear (fear[10] = {fear[10]}).");
        Assert.True(double.IsNaN(greed[10]),
            $"a reading of 12 was marked as extreme GREED (greed[10] = {greed[10]}).");

        Assert.True(!double.IsNaN(greed[45]) && greed[45] >= 75.0,
            $"a reading of 88 was not marked as extreme greed (greed[45] = {greed[45]}).");
        Assert.True(double.IsNaN(fear[45]),
            $"a reading of 88 was marked as extreme FEAR (fear[45] = {fear[45]}).");
    }

    /// <summary>
    /// <b>A flip needs something to flip FROM.</b>
    ///
    /// <para>
    /// The first non-neutral reading on a freshly loaded chart is not a change of sentiment; it is
    /// the first thing known about it. The A2h mutant dropped the previous-side requirement, so
    /// every chart opened by announcing a flip that did not occur.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFirstReadingOnAChartIsNotASentimentFlip()
    {
        var bars = Bars(i => (101.0, 99.0, 100.0), 60);

        // One side only, for the whole chart. Nothing flipped.
        var oneSided = Run(FearGreedWith((5, 20.0), (20, 30.0), (40, 25.0)), "FEAR_GREED", bars);
        var flips = oneSided[FearGreedProvider.CompSentimentFlip];
        var fired = Enumerable.Range(0, flips.Length).Where(i => !double.IsNaN(flips[i])).ToList();

        Assert.True(fired.Count == 0,
            $"a chart whose sentiment never crossed 50 reported {fired.Count} flips " +
            $"(bars {string.Join(", ", fired.Take(5))}). The first reading is not a change.");
    }

    /// <summary>The vacuity twin: a reading that really does cross from fear to greed IS a flip.</summary>
    [Fact]
    public void ACrossingFromFearToGreedIsASentimentFlip()
    {
        var bars = Bars(i => (101.0, 99.0, 100.0), 60);
        var r = Run(FearGreedWith((5, 20.0), (30, 80.0)), "FEAR_GREED", bars);
        var flips = r[FearGreedProvider.CompSentimentFlip];

        Assert.Contains(flips, v => !double.IsNaN(v));
    }

    // ── Ichimoku: a confirmed cross stays confirmed ─────────────────────────────────

    /// <summary>
    /// <b>The TK marker needs the bar AFTER the cross to agree with it.</b>
    ///
    /// <para>
    /// The cross is detected on bar <c>i-1</c> and the marker is placed at <c>i</c> only if the
    /// lines are still on the new side there. The A2h mutant dropped that second half, so a cross
    /// that immediately reverses still stamps a bullish marker — a signal for an event that
    /// un-happened on the very next bar.
    /// </para>
    ///
    /// <para>
    /// Engineered as a single-bar whipsaw: Tenkan pokes above Kijun for exactly one bar and drops
    /// back. The shared probe series contain whipsaws only by chance and at unknown bars.
    /// </para>
    /// </summary>
    [Fact]
    public void ATenkanKijunCrossThatImmediatelyReversesIsNotMarked()
    {
        var provider = new IchimokuProvider();

        // A ONE-BAR Tenkan window, deliberately. Tenkan is the midpoint of a rolling high/low
        // range, so with the default nine-bar window a single spike keeps the fast line elevated
        // for nine bars — a cross and a later cross back, not a whipsaw. At a window of one, the
        // spike raises Tenkan for exactly the bar it happens on and the line is back below on the
        // next, which is the shape the confirmation bar exists to refuse.
        // The baseline DECLINES, so Tenkan sits strictly below Kijun beforehand. On a flat series
        // the two midpoints are equal and "crossed up from below" is never true — measured, and it
        // is why an earlier version of this fixture found no whipsaw at all. The spike bar also
        // needs a high LOW, or its own low drags the one-bar midpoint back down to Kijun's.
        var bars = Bars(i =>
        {
            double baseline = 120.0 - i * 0.06;
            if (i == 200) return (200.0, 190.0, 195.0);                 // one-bar spike
            return (baseline + 0.5, baseline - 0.5, baseline);
        }, 320);

        var r = Run(provider, "ICHIMOKU", bars,
            new Dictionary<string, object> { ["TenkanPeriod"] = 1 });
        var tenkan = r[IchimokuProvider.CompTenkan];
        var kijun = r[IchimokuProvider.CompKijun];
        var bull = r[IchimokuProvider.CompTkBull];

        // Find every bar where the previous bar crossed up but this bar is back below: the marker
        // must not be there.
        var wrong = new List<int>();
        for (int i = 2; i < bars.Count; i++)
        {
            if (double.IsNaN(tenkan[i]) || double.IsNaN(kijun[i]) ||
                double.IsNaN(tenkan[i - 1]) || double.IsNaN(kijun[i - 1]) ||
                double.IsNaN(tenkan[i - 2]) || double.IsNaN(kijun[i - 2])) continue;

            bool crossedPrior = tenkan[i - 2] < kijun[i - 2] && tenkan[i - 1] >= kijun[i - 1];
            bool reversed = tenkan[i] < kijun[i];
            if (crossedPrior && reversed && !double.IsNaN(bull[i])) wrong.Add(i);
        }

        Assert.True(wrong.Count == 0,
            $"a Tenkan/Kijun cross that had already reversed still stamped a bullish marker on bars " +
            $"{string.Join(", ", wrong.Take(6))}. The confirmation bar is what separates a cross from " +
            "a whipsaw.");
    }

    /// <summary>The vacuity twin: a cross that HOLDS must be marked, or the test above is satisfied
    /// by a provider that never marks anything.</summary>
    [Fact]
    public void ATenkanKijunCrossThatHoldsIsMarked()
    {
        var provider = new IchimokuProvider();
        // A decline first, so Tenkan is genuinely BELOW Kijun before the turn — from a flat start
        // the two midpoints are equal and "crossed up from below" is never true.
        var bars = Bars(i =>
        {
            double c = i < 150 ? 200.0 - i * 0.6 : 110.0 + (i - 150) * 0.9;
            return (c + 1.0, c - 1.0, c);
        }, 320);

        var bull = Run(provider, "ICHIMOKU", bars)[IchimokuProvider.CompTkBull];
        Assert.Contains(bull, v => !double.IsNaN(v));
    }

    // ── MA Cloud: a described change has to be a change ─────────────────────────────

    /// <summary>
    /// <b>"Expanding" needs a deadband, or the sentence changes on rounding.</b>
    ///
    /// <para>
    /// The spoken description of the cloud calls it expanding when its width grows by more than
    /// two per cent. The A2h mutant removed the margin, so any increase at all qualifies and the
    /// sentence flips between expanding and contracting on almost every bar. For a user who hears
    /// this on every arrow press, a description that changes constantly is a description that
    /// carries nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void ACloudWidthThatBarelyMovedIsNotDescribedAsExpanding()
    {
        var provider = new MACloudProvider();

        // Two bars of cloud width a tenth of a per cent apart — well inside the deadband.
        var data = new Dictionary<string, double[]>
        {
            [MACloudProvider.CompCloud] = new[] { 10.000, 10.010 },
            [MACloudProvider.DataFastMA] = new[] { 105.0, 105.0 },
            [MACloudProvider.DataSlowMA] = new[] { 95.0, 95.0 },
        };
        var bar = new Ohlcv(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1000);

        string? speech = provider.GetComponentSpeech(MACloudProvider.CompCloud, 10.010, bar, data, 1);

        Assert.False(string.IsNullOrEmpty(speech), "the cloud said nothing at all");
        Assert.DoesNotContain("expanding", speech!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The vacuity twin: a cloud that really is widening must say so.</summary>
    [Fact]
    public void ACloudWidthThatGrewIsDescribedAsExpanding()
    {
        var provider = new MACloudProvider();
        var data = new Dictionary<string, double[]>
        {
            [MACloudProvider.CompCloud] = new[] { 10.0, 14.0 },
            [MACloudProvider.DataFastMA] = new[] { 105.0, 107.0 },
            [MACloudProvider.DataSlowMA] = new[] { 95.0, 93.0 },
        };
        var bar = new Ohlcv(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100, 101, 99, 100, 1000);

        string? speech = provider.GetComponentSpeech(MACloudProvider.CompCloud, 14.0, bar, data, 1);

        Assert.Contains("expanding", speech!, StringComparison.OrdinalIgnoreCase);
    }

    // ── The spoken divergence hint ──────────────────────────────────────────────────

    /// <summary>
    /// <b>RSI rising while price falls is BULLISH, and this is a sentence the user hears.</b>
    ///
    /// <para>
    /// <see cref="SkenderDetailFactProvider"/> is named by no other test in the suite. The A2h
    /// mutant swapped the word, so the detail key announces the opposite direction — which is worse
    /// than saying nothing, because it is confidently wrong and there is no second channel to check
    /// it against.
    /// </para>
    /// </summary>
    [Fact]
    public void RsiRisingWhilePriceFallsIsSpokenAsABullishDivergence()
    {
        var provider = new SkenderDetailFactProvider();

        // Price down over the five-bar window, RSI up over the same window.
        var bars = Bars(i => (101.0 - i, 99.0 - i, 100.0 - i), 10);
        var rsi = new double[10];
        for (int i = 0; i < 10; i++) rsi[i] = 30.0 + i * 2.0;      // rising

        string? fact = provider.GetDetailFact("RSI",
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            new Dictionary<string, double[]> { ["Rsi"] = rsi }, 9,
            new Dictionary<string, object>());

        Assert.False(string.IsNullOrEmpty(fact), "the RSI detail fact said nothing");
        Assert.Contains("Bullish divergence", fact!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearish divergence", fact!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>And the mirror image, so the two cannot both map to one word.</summary>
    [Fact]
    public void RsiFallingWhilePriceRisesIsSpokenAsABearishDivergence()
    {
        var provider = new SkenderDetailFactProvider();

        var bars = Bars(i => (101.0 + i, 99.0 + i, 100.0 + i), 10);
        var rsi = new double[10];
        for (int i = 0; i < 10; i++) rsi[i] = 70.0 - i * 2.0;      // falling

        string? fact = provider.GetDetailFact("RSI",
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            new Dictionary<string, double[]> { ["Rsi"] = rsi }, 9,
            new Dictionary<string, object>());

        Assert.Contains("Bearish divergence", fact!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bullish divergence", fact!, StringComparison.OrdinalIgnoreCase);
    }

    // ── Loukas cycles: a low, and a tie ─────────────────────────────────────────────

    /// <summary>
    /// <b>A daily cycle low has to be a low, and a flat base is one of them.</b>
    ///
    /// <para>
    /// The symmetric strict local-minimum test is the only selectivity this detector has — its own
    /// comment records two windowed-lowest filters that were tried and removed as regime-fragile.
    /// The A2h mutant allowed ties, so a flat base emits a cycle low on each of its bars, and every
    /// day count downstream is measured from the wrong one.
    /// </para>
    /// </summary>
    [Fact]
    public void AFlatBaseProducesAtMostOneDailyCycleLow()
    {
        var provider = new LoukasCyclesProvider();
        const int SwingLookback = 10;

        // A decline into a perfectly flat base, then a recovery. Every bar of the base ties with
        // every other, so none of them is strictly the lowest in its window.
        var bars = Bars(i =>
        {
            if (i < 120) { double c = 200.0 - i * 0.8; return (c + 1.0, c - 1.0, c); }
            if (i < 160) return (105.0, 104.0, 104.5);              // the flat base — all equal
            double up = 104.5 + (i - 160) * 0.8;
            return (up + 1.0, up - 1.0, up);
        }, 320);

        var r = Run(provider, "LOUKAS_CYCLES", bars,
            new Dictionary<string, object> { ["SwingLookback"] = SwingLookback, ["DcMinBars"] = 20 });

        var confirmed = r[LoukasCyclesProvider.CompDclConfirmed];
        var inBase = Enumerable.Range(120, 40)
            .Where(i => i < confirmed.Length && !double.IsNaN(confirmed[i]))
            .ToList();

        Assert.True(inBase.Count <= 1,
            $"a perfectly flat base produced {inBase.Count} daily cycle lows (bars " +
            $"{string.Join(", ", inBase.Take(10))}). A tie is not a low: with equal bars on both " +
            "sides, none of them is the cycle's bottom, and every day count afterwards is measured " +
            "from whichever one happened to be picked.");
    }

    /// <summary>
    /// <b>When two cycle lows tie, the earlier one keeps the title.</b>
    ///
    /// <para>
    /// An intermediate cycle low is the DCL whose low is the lowest of the last <c>IcDcCount</c>
    /// daily cycle lows, itself included. The comparison is <c>&lt;=</c> deliberately — strictly
    /// lower, so a tie leaves the earlier low holding it and one flat stretch cannot mint two
    /// intermediate lows. The A2h mutant relaxed it to <c>&lt;</c>.
    /// </para>
    ///
    /// <para>
    /// It matters more than a duplicate marker: the intermediate-cycle count is what every
    /// downstream day count is measured from, so a spurious ICL shifts the phase of the whole
    /// chart from that bar onward.
    /// </para>
    ///
    /// <para>
    /// Engineered as a repeating cycle whose trough is the SAME price every time. Every cycle low
    /// after the first ties with its predecessors, so at most one of them can be an intermediate
    /// low.
    /// </para>
    /// </summary>
    [Fact]
    public void RepeatedCycleLowsAtTheSamePriceMintAtMostOneIntermediateLow()
    {
        var provider = new LoukasCyclesProvider();
        const int Period = 60;

        // A sawtooth: down to exactly 100, back up to 160, repeat. Every trough ties.
        var bars = Bars(i =>
        {
            int phase = i % Period;
            double c = phase < Period / 2
                ? 160.0 - phase * (60.0 / (Period / 2))
                : 100.0 + (phase - Period / 2) * (60.0 / (Period / 2));
            return (c + 0.5, c - 0.5, c);
        }, 900);

        var r = Run(provider, "LOUKAS_CYCLES", bars, new Dictionary<string, object>
        {
            ["SwingLookback"] = 5,
            ["DcMinBars"] = 20,
            ["IcDcCount"] = 3,
        });

        var dcls = r[LoukasCyclesProvider.CompDclConfirmed].Count(v => !double.IsNaN(v));
        var icls = Enumerable.Range(0, r[LoukasCyclesProvider.CompIclConfirmed].Length)
            .Where(i => !double.IsNaN(r[LoukasCyclesProvider.CompIclConfirmed][i]))
            .ToList();

        Assert.True(dcls >= 4,
            $"the sawtooth produced only {dcls} daily cycle lows — too few for the tie rule to be " +
            "reachable, so this case proves nothing.");

        Assert.True(icls.Count <= 1,
            $"{dcls} cycle lows all at exactly the same price produced {icls.Count} intermediate " +
            $"lows (bars {string.Join(", ", icls.Take(8))}). A tie leaves the EARLIER low holding " +
            "the title; otherwise one flat stretch mints several, and every day count afterwards is " +
            "measured from the wrong one.");
    }

    /// <summary>The vacuity twin: a clean V bottom IS a daily cycle low.</summary>
    [Fact]
    public void ACleanTroughProducesADailyCycleLow()
    {
        var provider = new LoukasCyclesProvider();

        var bars = Bars(i =>
        {
            double c = i < 140 ? 200.0 - i * 0.8 : 88.0 + (i - 140) * 0.8;
            return (c + 1.0, c - 1.0, c);
        }, 320);

        var r = Run(provider, "LOUKAS_CYCLES", bars,
            new Dictionary<string, object> { ["SwingLookback"] = 10, ["DcMinBars"] = 20 });

        Assert.Contains(r[LoukasCyclesProvider.CompDclConfirmed], v => !double.IsNaN(v));
    }
}
