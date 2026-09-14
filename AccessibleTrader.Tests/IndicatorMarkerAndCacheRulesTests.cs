using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Four rules about WHEN something is reported, spread across four providers because the
/// mistake is the same one each time.</b>
///
/// <para>
/// A marker is a claim that an event happened on this bar. Each of these mutants survived a green
/// suite by turning a marker into a state — something that is simply true for a while — or by
/// removing the memory that makes an event an event:
/// </para>
///
/// <list type="bullet">
///   <item><b>Cipher A:</b> an oversold condition stopped having to be SUSTAINED, so a one-bar
///         spike through the line became a buy signal.</item>
///   <item><b>Top/Bottom:</b> the rising-edge test went, so an eight-bar capitulation stamped
///         eight bottoms and the earcon fired eight times for one event.</item>
///   <item><b>Cipher S:</b> the staleness margin on the cycle auto-detector went, so the chart
///         re-detected and re-announced its cycle window on essentially every recalculation; and
///         the significance threshold went, so a one-bar shift in the median trough interval
///         produced a full spoken sentence.</item>
/// </list>
///
/// <para>
/// For a user navigating by ear, the difference between a marker and a state is the difference
/// between a signal and a rattle. None of it is visible in a screenshot.
/// </para>
/// </summary>
public sealed class IndicatorMarkerAndCacheRulesTests
{
    private static List<Ohlcv> Bars(Func<int, double> close, int n, double range = 1.0)
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            double c = close(i);
            bars.Add(new Ohlcv(t.AddHours(i), c, c + range, c - range, c, 1000));
        }
        return bars;
    }

    /// <summary>Bars from an explicit (high, low, close) shape — several of these fixtures turn on
    /// where the close sits INSIDE the bar, which a close-only generator cannot express.</summary>
    private static List<Ohlcv> Bars2(Func<int, (double High, double Low, double Close)> shape, int n)
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        for (int i = 0; i < n; i++)
        {
            var (h, l, c) = shape(i);
            bars.Add(new Ohlcv(t.AddHours(i), c, h, l, c, 1000));
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

    private static List<int> FiredBars(Dictionary<string, double[]> r, string component)
    {
        var fired = new List<int>();
        if (!r.TryGetValue(component, out var arr)) return fired;
        for (int i = 0; i < arr.Length; i++)
            if (!double.IsNaN(arr[i])) fired.Add(i);
        return fired;
    }

    /// <summary>
    /// A series with real cycles in it: four roughly 300-bar swings with a 40% peak-to-trough
    /// range. The cycle detector needs peaks separated by at least a hundred bars and a drawdown
    /// large enough to clear its threshold, and the shared probe series — which swing constantly at
    /// a small amplitude — supply neither. A detector asked about a series with no cycles correctly
    /// declines, and a test built on that decline is a test of nothing.
    /// </summary>
    private static List<Ohlcv> CyclicalSeries(int n)
        => Bars(i => 160.0 + 60.0 * Math.Sin(2 * Math.PI * i / 300.0), n, range: 1.5);

    // ── Cipher A: an oversold condition has to be sustained ─────────────────────────

    /// <summary>
    /// <b>A buy signal needs the wave to have been oversold on the previous bar too.</b>
    ///
    /// <para>
    /// <c>sustainedOs</c> is <c>wt1[i] &lt; -ob AND wt1[i-1] &lt; -ob</c>. The A2h mutant dropped
    /// the second half, so a single bar dipping past the line qualified. Asserted on the bars that
    /// fired, over the shared probe series.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EveryCipherABuySignalHasTwoBarsBelowOversold(int flavour)
    {
        const double ObLevel = 53.0;
        var provider = new CipherAProvider();
        var bars = CausalityProbeSeries.Bars(flavour, 1000);
        var r = Run(provider, "CIPHER_A", bars, new Dictionary<string, object> { ["OBLevel"] = ObLevel });

        // NOT CompWtMomentum: that array holds the CLOSE PRICE, because the component is drawn as
        // a dot pinned to the bar. The wave itself — the number the oversold test reads — is in the
        // "_color" companion, which is what the gradient renderer colours the dot from.
        var wt = r[CipherAProvider.CompWtMomentum + "_color"];
        var buys = FiredBars(r, CipherAProvider.CompBuySignal);
        var sells = FiredBars(r, CipherAProvider.CompSellSignal);

        Assert.True(buys.Count + sells.Count > 0,
            $"Cipher A produced no buy or sell signals on series {flavour} — this proves nothing");

        var unsustained = new List<string>();
        foreach (int b in buys)
            if (b > 0 && !(wt[b] < -ObLevel && wt[b - 1] < -ObLevel))
                unsustained.Add($"buy at {b}: wave {wt[b]:F1} now, {wt[b - 1]:F1} before");
        foreach (int b in sells)
            if (b > 0 && !(wt[b] > ObLevel && wt[b - 1] > ObLevel))
                unsustained.Add($"sell at {b}: wave {wt[b]:F1} now, {wt[b - 1]:F1} before");

        Assert.True(unsustained.Count == 0,
            $"{unsustained.Count} Cipher A signals on series {flavour} fired on a single-bar excursion " +
            $"past the oversold/overbought line:\n  " + string.Join("\n  ", unsustained.Take(6)) +
            "\nThe condition has to have held on the previous bar as well, or a one-bar spike is a trade.");
    }

    // ── Top/Bottom: a confirmation is an edge, not a level ──────────────────────────

    /// <summary>
    /// <b>A capitulation lasting eight bars is one bottom, not eight.</b>
    ///
    /// <para>
    /// The confirmation is gated on the PREVIOUS bar's score being below the threshold — a rising
    /// edge. The A2h mutant removed that test and nothing failed, because every existing assertion
    /// about this provider asks whether a bottom fired, not how many times.
    /// </para>
    ///
    /// <para>
    /// Asserted as: no two confirmations on consecutive bars, over a series engineered to hold the
    /// score above the threshold for a stretch. A run of confirmations is exactly what the removed
    /// test permits.
    /// </para>
    /// </summary>
    [Fact]
    public void ASustainedCapitulationStampsOneBottomNotOnePerBar()
    {
        var provider = new TopBottomDetectorProvider();

        // A sharp, sustained selloff whose every bar has a long lower wick and closes near its own
        // high — the shape the capitulation score is built from. Measured 2026-09-14: this holds
        // the score above the confirmation threshold for 108 bars and the provider stamps exactly
        // ONE bottom. The shared probe series never sustain it, which is why a test built on them
        // passed under the mutant.
        var bars = Bars2(i =>
        {
            if (i < 120) return (102.0, 98.0, 100.0);
            double basePrice = 100.0 - (i - 120) * 0.9;
            return (basePrice + 2.0, basePrice - 6.0, basePrice + 1.5);
        }, 260);

        var r = Run(provider, TopBottomDetectorProvider.Code, bars);
        var score = r[TopBottomDetectorProvider.CompCapitulation];
        var bottoms = FiredBars(r, TopBottomDetectorProvider.CompBottom);

        int sustained = score.Count(v => !double.IsNaN(v) && v >= 0.55);
        Assert.True(sustained > 20,
            $"the capitulation score stayed above the confirmation threshold for only {sustained} " +
            "bars — the fixture does not reach the case this test is about.");

        Assert.True(bottoms.Count <= 3,
            $"a single sustained capitulation stamped {bottoms.Count} bottoms (bars " +
            $"{string.Join(", ", bottoms.Take(10))}) while the score was above the threshold for " +
            $"{sustained} bars. A confirmation marks the bar the condition BECAME true; without that " +
            "edge the earcon fires once per bar for one event.");

        var runs = new List<string>();
        for (int i = 1; i < bottoms.Count; i++)
            if (bottoms[i] == bottoms[i - 1] + 1) runs.Add($"{bottoms[i - 1]} and {bottoms[i]}");
        Assert.True(runs.Count == 0,
            $"bottoms fired on consecutive bars: {string.Join("; ", runs.Take(5))}");
    }

    /// <summary>The vacuity twin: the same engineered capitulation must produce a bottom at all.</summary>
    [Fact]
    public void ASustainedCapitulationDoesStampABottom()
    {
        var provider = new TopBottomDetectorProvider();
        var bars = Bars2(i =>
        {
            if (i < 120) return (102.0, 98.0, 100.0);
            double basePrice = 100.0 - (i - 120) * 0.9;
            return (basePrice + 2.0, basePrice - 6.0, basePrice + 1.5);
        }, 260);

        Assert.NotEmpty(FiredBars(Run(provider, TopBottomDetectorProvider.Code, bars),
                                  TopBottomDetectorProvider.CompBottom));
    }

    /// <summary>
    /// <b>And the position gate means what its name says.</b>
    ///
    /// <para>
    /// <c>atTop</c> is "within the top fifth of the trailing range". The A2h mutant inverted the
    /// comparison so it meant the lower four-fifths, and the only thing that went red was a
    /// coverage-bookkeeping guard — whose natural repair is to edit a pinned list rather than fix
    /// the code. Asserted directly: distribution evidence must accumulate on a series that rallies
    /// into a high and not on one that is falling away from it.
    /// </para>
    /// </summary>
    [Fact]
    public void AMarketFallingAwayFromItsHighIsNotDistributing()
    {
        var provider = new TopBottomDetectorProvider();

        // A long, steady decline: price ends at the BOTTOM of its own trailing range. Distribution
        // evidence is gated on being at the top of that range, so none of it may accrue here.
        // Strictly monotonic, deliberately. An earlier version added a small oscillation and the
        // swing highs it created produced RSI-divergence evidence of their own — 0.637 rather than
        // the 0.000 a clean decline gives — so the test failed for a reason unrelated to position.
        var decline = Bars2(i =>
        {
            double c = 200.0 - i * 0.35;
            return (c + 2.0, c - 2.0, c);
        }, 400);

        // A rally that stalls at its high: the same gate, satisfied.
        var rallyAndStall = Bars2(i =>
        {
            double c = i < 260 ? 100.0 + i * 0.35 : 191.0 + 1.5 * Math.Sin(i / 3.0);
            return (c + 2.0, c - 2.0, c);
        }, 400);

        double TailMax(List<Ohlcv> bars)
        {
            var d = Run(provider, TopBottomDetectorProvider.Code, bars)[TopBottomDetectorProvider.CompDistribution];
            var tail = d.Skip(d.Length * 3 / 4).Where(v => !double.IsNaN(v)).ToList();
            return tail.Count == 0 ? 0.0 : tail.Max();
        }

        double atHigh = TailMax(rallyAndStall);
        double falling = TailMax(decline);

        // The rally is the anti-vacuity half: the detector must be capable of accruing here at all.
        Assert.True(atHigh > 0.3,
            $"distribution evidence reached only {atHigh:F3} at the top of a rally that stalled — " +
            "the detector is not accruing anywhere, so the assertion below proves nothing.");

        // And the half that carries the rule. Measured 2026-09-14: a steady decline reads exactly
        // 0.000 here. Comparing the two is NOT enough — with the position gate inverted the rally
        // keeps most of its score from evidence streams that are not position-gated, so the
        // comparison still passes. What the inversion actually does is switch distribution ON in a
        // falling market.
        Assert.True(falling < 0.05,
            $"a market falling steadily away from its high accrued {falling:F3} of distribution " +
            "evidence. Distribution is what happens at a top; the position gate is what says which " +
            "of those a bar is in.");
    }

    // ── Cipher S: an auto-detection that does not re-announce itself ────────────────

    /// <summary>
    /// <b>The cycle auto-detector holds its answer until the series has grown materially.</b>
    ///
    /// <para>
    /// <c>SuggestParameters</c> caches its detection against the bar count it was computed at and
    /// declines to re-run until the series is half as long again. The A2h mutant replaced that
    /// margin with a bare inequality, so any growth at all re-detects — which on a live chart is
    /// every tick, and each re-detection can emit a spoken sentence.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCycleDetectorDoesNotReRunUntilTheSeriesHasGrownMaterially()
    {
        var provider = new CipherSProvider();
        var bars = CyclicalSeries(900);
        var pars = new Dictionary<string, object>();

        var first = provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            "TEST/USD", pars, out string? firstMessage);

        Assert.NotNull(first);
        Assert.False(string.IsNullOrEmpty(firstMessage),
            "the first detection said nothing — this test would then be about silence rather than " +
            "about re-announcement.");

        // One more bar. Nothing about the cycle can have changed materially.
        var oneMore = CyclicalSeries(901);
        var second = provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(oneMore),
            "TEST/USD", pars, out string? secondMessage);

        Assert.True(second == null,
            "the cycle detector re-ran after a single extra bar. On a live chart that is every " +
            "tick, and each re-run can speak.");
        Assert.True(string.IsNullOrEmpty(secondMessage),
            $"the cycle detector spoke again after one extra bar: \"{secondMessage}\"");
    }

    /// <summary>
    /// <b>And a small change in the detected cycle is not worth a sentence.</b>
    ///
    /// <para>
    /// When the detector does re-run, it announces the new window only if it moved by more than
    /// fifteen per cent. The A2h mutant dropped that to "moved at all", so a one-bar shift in the
    /// median trough interval produces a full spoken sentence — on a surface where every sentence
    /// competes with the chart the user is reading.
    /// </para>
    ///
    /// <para>
    /// The fixture is a series whose cycles LENGTHEN partway through, so a later detection differs
    /// from an earlier one by a little rather than not at all. A steady sinusoid will not do: it
    /// gives exactly the same window every time, the change is zero, and neither version speaks.
    /// Measured: the window moves from 375 to 401, a shift of under seven per cent.
    /// </para>
    /// </summary>
    [Fact]
    public void ASmallShiftInTheDetectedCycleIsNotAnnounced()
    {
        var provider = new CipherSProvider();
        var pars = new Dictionary<string, object>();

        var first = provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(DriftingCycleSeries(1400)),
            "DRIFT/USD", pars, out string? firstMessage);

        Assert.NotNull(first);
        Assert.False(string.IsNullOrEmpty(firstMessage),
            "the first detection said nothing, so there is no 'again' to be quiet about");

        // Well past the staleness margin, so the detector is allowed to re-run — and it does,
        // landing on a slightly different window.
        var second = provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(DriftingCycleSeries(2600)),
            "DRIFT/USD", pars, out string? secondMessage);

        Assert.NotNull(second);
        double before = Convert.ToDouble(first!["WindowBars"]);
        double after = Convert.ToDouble(second!["WindowBars"]);
        double moved = Math.Abs(after - before) / before;

        Assert.True(moved > 0.001 && moved < 0.15,
            $"the window moved by {moved:P1} ({before} to {after}) — the fixture needs a SMALL " +
            "non-zero change to say anything about the significance threshold.");

        Assert.True(string.IsNullOrEmpty(secondMessage),
            $"a {moved:P1} shift in the detected cycle window was announced: \"{secondMessage}\". " +
            "The threshold exists so the chart does not narrate its own rounding.");
    }

    /// <summary>A series whose cycles lengthen partway through, so successive detections differ by
    /// a little. A steady sinusoid gives the identical window every time and cannot express
    /// "changed, but not by much".</summary>
    private static List<Ohlcv> DriftingCycleSeries(int n)
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<Ohlcv>(n);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            phase += 2 * Math.PI / (i < 1300 ? 300.0 : 400.0);
            double c = 160.0 + 60.0 * Math.Sin(phase);
            bars.Add(new Ohlcv(t.AddHours(i), c, c + 1.5, c - 1.5, c, 1000));
        }
        return bars;
    }

    /// <summary>
    /// The vacuity twin: a series that HAS grown materially must be allowed to re-detect, or the
    /// test above is satisfied by a detector that only ever runs once.
    /// </summary>
    [Fact]
    public void TheCycleDetectorDoesReRunOnceTheSeriesHasGrown()
    {
        var provider = new CipherSProvider();
        var pars = new Dictionary<string, object>();

        provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(CyclicalSeries(600)),
            "GROWN/USD", pars, out _);

        var grown = CyclicalSeries(1400);                 // well past the 1.5x margin
        var again = provider.SuggestParameters(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(grown),
            "GROWN/USD", pars, out _);

        Assert.NotNull(again);
    }
}
