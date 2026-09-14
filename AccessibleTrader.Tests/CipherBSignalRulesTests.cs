using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Cipher B's firing rules, asserted against the bars they fired on.</b>
///
/// <para>
/// Before this file, <see cref="CipherBProvider"/> had 1,381 lines and no test that ran
/// <c>Calculate</c>. What it had was seventeen METADATA assertions (frequencies, patch ids,
/// playback layers) and <c>CipherBTimingTests</c>, which exercises the
/// <c>ShiftMarkersForwardExcept</c> HELPER directly and never calls the provider. So the helper
/// was well guarded and the call site was free to stop calling it — which is exactly what the
/// A2h mutant set demonstrated: seven of eight mutants aimed at this file survived a green suite,
/// including deleting the shift exemption, inverting the anchor-suppression direction, dropping
/// the gold confluence requirement to one condition, and turning the divergence depth gate from
/// AND into OR.
/// </para>
///
/// <para>
/// <b>Why these assertions are shaped the way they are.</b> A golden file of bar indices would
/// pin this indicator's output exactly and be worthless: every legitimate tuning change would
/// rewrite it, so it would be regenerated rather than read. Instead each test states the RULE in
/// the form the rule is written in — every marker that fired satisfies the condition that admits
/// it, tightening a gate strictly reduces what fires, and a filter removes a subset rather than a
/// different set. Those survive retuning and still fail when the rule changes.
/// </para>
///
/// <para>
/// The price series is <see cref="CausalityProbeSeries"/>, the deterministic generator the
/// causality contract already uses — seeded xorshift, four flavours, shared with
/// <c>CustomIndicatorCausalityProbe</c>. A second generator would drift, and the one that drifted
/// would be the one nobody was watching.
/// </para>
/// </summary>
public sealed class CipherBSignalRulesTests
{
    private static readonly CipherBProvider Provider = new();

    /// <summary>All three hourly flavours, so a rule is never stated from one price path.</summary>
    public static TheoryData<int> Flavours()
    {
        var d = new TheoryData<int>();
        foreach (var f in CausalityProbeSeries.HourlyFlavours) d.Add(f);
        return d;
    }

    /// <summary>
    /// 1,400 bars, not a few hundred. Measured 2026-09-14: over the three hourly flavours at 600
    /// bars, EVERY WT pivot pair that satisfies the price-and-wave shape has both legs past the
    /// depth threshold, so the depth gate never changes a verdict and a test of it is vacuous. The
    /// pairs that straddle the threshold — the only ones that distinguish AND from OR — first
    /// appear past roughly a thousand bars. A fixture can be too short to contain the case.
    /// </summary>
    private const int Bars = 1400;

    /// <summary>
    /// Defaults plus whatever the caller overrides. <c>TfAware</c> is switched OFF by default here
    /// so the gates under test are the DECLARED ones rather than the bucket-scaled ones — a test
    /// that asserts "past the depth threshold" has to know which threshold, and with TF scaling on
    /// that number is a function of the synthetic series' bar spacing.
    /// </summary>
    private static Dictionary<string, object> Params(params (string Key, object Value)[] overrides)
    {
        var p = new Dictionary<string, object> { ["TfAware"] = false };
        foreach (var (k, v) in overrides) p[k] = v;
        return p;
    }

    private static Dictionary<string, double[]> Run(int flavour, Dictionary<string, object> pars)
    {
        var bars = CausalityProbeSeries.Bars(flavour, Bars);
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            pars, buffer);
        return results;
    }

    /// <summary>The bar indices where a component has a value.</summary>
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

    // ── The divergence depth gate ───────────────────────────────────────────────────

    /// <summary>
    /// <b>Both legs of a regular divergence must be past the depth threshold, not either.</b>
    ///
    /// <para>
    /// The gate is what separates a divergence from two shallow wiggles with price drift. The A2h
    /// mutant turned its <c>&amp;&amp;</c> into <c>||</c> and nothing failed. Asserted on the bars that
    /// fired, using the <c>_anchorIdx</c> companion array the renderer already reads to draw the
    /// pivot-to-pivot line — so the FIRST leg is checkable, not just the second.
    /// </para>
    ///
    /// <para>
    /// <b>The shallow detector has to be excluded, and it identifies itself.</b> A second,
    /// cross-based detector writes into these same arrays and is deliberately NOT depth-gated —
    /// it fires at a WT crossover inside OB/OS, which is its own qualification. Its markers sit on
    /// crossover bars, and Cipher B publishes exactly those as <c>WaveTrend Cross Bull/Bear</c>,
    /// so they can be removed by reading the provider's own output rather than by guessing. A
    /// pivot row that happens to coincide with a crossover is excluded too; that costs a little
    /// coverage and keeps the assertion strict, which is the right trade.
    /// </para>
    ///
    /// <para>
    /// Run with the confirmation lag OFF so the marker still sits on its pivot bar; with the lag on
    /// it moves to the confirmation bar and the WT value there is a different number.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void EveryPivotDivergenceHasBOTHLegsPastTheDepthThreshold(int flavour)
    {
        const double Depth = 35.0;      // the TfAware=false value, from CipherBProvider
        var r = Run(flavour, Params(("DivergenceConfirmLag", false)));
        var wt1 = Series(r, CipherBProvider.CompWT1);

        int checkedCount = 0;
        var shallowLeg = new List<string>();

        foreach (var (component, crossComponent, wantBelow) in new[]
                 {
                     (CipherBProvider.CompBullDiv, CipherBProvider.CompCrossBull, true),
                     (CipherBProvider.CompBearDiv, CipherBProvider.CompCrossBear, false),
                 })
        {
            var crossBars = FiredBars(r, crossComponent).ToHashSet();
            var anchors = Series(r, component + "_anchorIdx");

            foreach (int b in FiredBars(r, component))
            {
                if (crossBars.Contains(b)) continue;            // the shallow detector's own rows
                int p = (int)anchors[b];
                if (p < 0 || p >= wt1.Length || double.IsNaN(wt1[p]) || double.IsNaN(wt1[b])) continue;

                checkedCount++;
                bool firstDeep = wantBelow ? wt1[p] < -Depth : wt1[p] > Depth;
                bool secondDeep = wantBelow ? wt1[b] < -Depth : wt1[b] > Depth;
                if (!firstDeep || !secondDeep)
                    shallowLeg.Add($"{component} at bar {b}: legs {wt1[p]:F1} and {wt1[b]:F1}");
            }
        }

        Assert.True(checkedCount > 0,
            $"series {flavour} produced no pivot-based divergences — this case proves nothing");
        Assert.True(shallowLeg.Count == 0,
            $"{shallowLeg.Count} of {checkedCount} pivot-based divergences on series {flavour} had a " +
            $"leg inside the +/-{Depth} depth threshold:\n  " + string.Join("\n  ", shallowLeg.Take(6)) +
            "\nBOTH legs must qualify — an OR here admits a deep leg paired with a shallow wiggle.");
    }

    /// <summary>
    /// The other half of the same rule, and the one an AND-to-OR change actually loosens: raising
    /// the depth requirement must not INCREASE the number of divergences. Stated as monotonicity
    /// because the threshold is TF-derived rather than a parameter, so it is reached by moving the
    /// bucket rather than the number.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void ADeeperRequirementNeverProducesMoreDivergences(int flavour)
    {
        // TfAware on with an hourly series puts the bucket at intraday (depth 25); off is daily
        // (depth 35). Same data, a strictly deeper requirement.
        // Both sides together: flavour 2 is a steady rise, so it produces bearish divergences and
        // almost no bullish ones. A per-side count would be vacuous there for a reason that has
        // nothing to do with the gate.
        int shallow = RegularDivergenceCount(Run(flavour, Params(("TfAware", true), ("DivergenceConfirmLag", false))));
        int deep = RegularDivergenceCount(Run(flavour, Params(("TfAware", false), ("DivergenceConfirmLag", false))));

        Assert.True(shallow > 0, $"series {flavour} produced no regular divergences — this proves nothing");
        Assert.True(deep <= shallow,
            $"raising the depth requirement produced MORE divergences on series {flavour} " +
            $"({deep} at depth 35 against {shallow} at depth 25).");
    }

    // ── The pivots the divergences are built on ─────────────────────────────────────

    /// <summary>
    /// <b>A divergence's anchor really is a WT pivot.</b>
    ///
    /// <para>
    /// Every regular divergence is a statement about two WT1 pivots, and the pivot test — lowest
    /// (or highest) in a symmetric window — is the foundation the whole detector stands on. The
    /// A2h mutant replaced the comparison with an arbitrary one and nothing failed, because the
    /// gating <c>if</c> further down still enforces the price/WT relation and the markers kept
    /// coming.
    /// </para>
    ///
    /// <para>
    /// Checked through the <c>_anchorIdx</c> companion, which records the FIRST pivot's bar index.
    /// Shallow-detector rows are skipped: their anchor is a previous crossover bar, not a pivot,
    /// and the file says so.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void ADivergenceAnchorIsALocalExtremeOfTheWave(int flavour)
    {
        const int PivotBars = 3;        // the TfAware=false default
        var r = Run(flavour, Params(("DivergenceConfirmLag", false), ("PivotBars", PivotBars)));
        var wt1 = Series(r, CipherBProvider.CompWT1);

        int checkedCount = 0;
        var notExtreme = new List<string>();

        // Bull divergences anchor on a pivot LOW, bear ones on a pivot HIGH. Both sides, because a
        // given synthetic series produces mostly one of them. The shallow detector's rows are
        // excluded the same way as in the depth test — it anchors on a previous CROSSOVER bar,
        // which is not a pivot and is not claimed to be.
        foreach (var (component, crossComponent, wantMinimum) in new[]
                 {
                     (CipherBProvider.CompBullDiv, CipherBProvider.CompCrossBull, true),
                     (CipherBProvider.CompBearDiv, CipherBProvider.CompCrossBear, false),
                 })
        {
            var crossBars = FiredBars(r, crossComponent).ToHashSet();
            var anchors = Series(r, component + "_anchorIdx");

            foreach (int b in FiredBars(r, component))
            {
                if (crossBars.Contains(b)) continue;
                int p = (int)anchors[b];
                if (p - PivotBars < 0 || p + PivotBars >= wt1.Length || double.IsNaN(wt1[p])) continue;

                bool isExtreme = true;
                for (int j = p - PivotBars; j <= p + PivotBars; j++)
                {
                    if (j == p || double.IsNaN(wt1[j])) continue;
                    if (wantMinimum ? wt1[j] < wt1[p] : wt1[j] > wt1[p]) { isExtreme = false; break; }
                }

                checkedCount++;
                if (!isExtreme)
                    notExtreme.Add($"{component} at bar {b}: anchor {p} is not a local " +
                                   (wantMinimum ? "minimum" : "maximum"));
            }
        }

        Assert.True(checkedCount > 0, $"no pivot-based divergence anchors were checkable on series {flavour}");
        Assert.True(notExtreme.Count == 0,
            $"{notExtreme.Count} of {checkedCount} divergence anchors on series {flavour} are not a " +
            $"local extreme of the wave over +/-{PivotBars} bars:\n  " +
            string.Join("\n  ", notExtreme.Take(6)) +
            "\nEvery regular divergence is a statement about two WT pivots; if the anchor is not a " +
            "pivot the detector is not finding pivots.");
    }

    // ── Anchor suppression ──────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A filter removes a subset of what fires without it, and removes it for the stated reason.</b>
    ///
    /// <para>
    /// Anchor suppression exists to block blue dots fired while the higher-timeframe anchor wave is
    /// strongly BEARISH — a counter-trend entry. The A2h mutant swapped the polarity so it blocked
    /// them in a strongly BULLISH anchor instead, removing exactly the entries it exists to keep,
    /// and nothing failed. Counting dots cannot catch that: both versions remove a similar number.
    /// </para>
    ///
    /// <para>
    /// Two assertions, and the second is the one that bites. Suppression must produce a SUBSET
    /// (it only ever removes), and every bar it removed must satisfy the stated condition —
    /// negative anchor polarity and an anchor wave past the suppression depth.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void AnchorSuppressionRemovesOnlyBlueDotsUnderABearishAnchor(int flavour)
    {
        const double Depth = 40.0;      // AnchorSuppressDepth default

        var off = Run(flavour, Params(("UseAnchorSuppression", false), ("AnchorSuppressDepth", Depth)));
        var on = Run(flavour, Params(("UseAnchorSuppression", true), ("AnchorSuppressDepth", Depth)));

        var withoutIt = FiredBars(off, CipherBProvider.CompBlue).ToHashSet();
        var withIt = FiredBars(on, CipherBProvider.CompBlue).ToHashSet();

        Assert.True(withoutIt.Count > 0, $"series {flavour} produced no blue dots — this proves nothing");

        var added = withIt.Except(withoutIt).ToList();
        Assert.True(added.Count == 0,
            $"switching anchor suppression ON created {added.Count} blue dots that do not fire with " +
            $"it off (bars {string.Join(", ", added.Take(6))}). A suppression filter only removes.");

        // Whether it removes anything on THIS series depends on whether the anchor ever goes
        // strongly bearish while a blue dot fires — flavour 2 is a steady rise, so it may not.
        // That the switch moves something SOMEWHERE is asserted once, below.
        var removed = withoutIt.Except(withIt).ToList();

        var polarity = Series(off, CipherBProvider.CompAnchorPolarity);
        var anchorWave = Series(off, CipherBProvider.CompWT1Anchor);

        var wrongReason = removed
            .Where(b => !double.IsNaN(anchorWave[b])
                        && !(polarity[b] < 0 && anchorWave[b] < -Depth))
            .ToList();

        Assert.True(wrongReason.Count == 0,
            $"anchor suppression removed {wrongReason.Count} blue dots whose anchor was NOT strongly " +
            $"bearish (bars {string.Join(", ", wrongReason.Take(6))}, " +
            $"polarity {string.Join(", ", wrongReason.Take(6).Select(b => polarity[b]))}). " +
            "It is supposed to block counter-trend entries, not with-trend ones.");
    }

    /// <summary>
    /// The anti-vacuity half of the test above, asserted across the whole flavour set rather than
    /// per series: anchor suppression has to remove a blue dot SOMEWHERE, or the subset and reason
    /// checks are statements about an empty collection.
    /// </summary>
    [Fact]
    public void AnchorSuppressionRemovesSomethingOnAtLeastOneSeries()
    {
        int totalRemoved = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            var off = FiredBars(Run(flavour, Params(("UseAnchorSuppression", false))), CipherBProvider.CompBlue).ToHashSet();
            var on = FiredBars(Run(flavour, Params(("UseAnchorSuppression", true))), CipherBProvider.CompBlue).ToHashSet();
            totalRemoved += off.Except(on).Count();
        }

        Assert.True(totalRemoved > 0,
            "anchor suppression removed no blue dot on any synthetic series — the switch moves " +
            "nothing, so the subset and reason assertions are vacuous.");
    }

    /// <summary>Regular (non-hidden) divergences in both directions.</summary>
    private static int RegularDivergenceCount(Dictionary<string, double[]> r)
        => FiredBars(r, CipherBProvider.CompBullDiv).Count + FiredBars(r, CipherBProvider.CompBearDiv).Count;

    // ── The gold dot's confluence requirement ───────────────────────────────────────

    /// <summary>
    /// <b>Gold means SEVERAL things agree, and the count is what says how many.</b>
    ///
    /// <para>
    /// Gold is the rarest marker Cipher B produces and its whole meaning is confluence. The A2h
    /// mutant dropped the requirement to a single condition — gold on nearly every blue dot — and
    /// nothing failed. Asserted as strict monotonicity in the parameter, plus the containment that
    /// makes it a confluence rule rather than a different rule at each K.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void RequiringMoreConfluenceProducesStrictlyFewerGoldDots(int flavour)
    {
        var atOne = FiredBars(Run(flavour, Params(("GoldMinConfluence", 1))), CipherBProvider.CompGold).ToHashSet();
        var atThree = FiredBars(Run(flavour, Params(("GoldMinConfluence", 3))), CipherBProvider.CompGold).ToHashSet();
        var atFour = FiredBars(Run(flavour, Params(("GoldMinConfluence", 4))), CipherBProvider.CompGold).ToHashSet();

        Assert.True(atOne.Count > 0, $"series {flavour} produced no gold dots even at K=1 — this proves nothing");

        Assert.True(atFour.Count < atOne.Count,
            $"requiring all four conditions produced as many gold dots as requiring one " +
            $"({atFour.Count} against {atOne.Count}) on series {flavour} — the confluence count " +
            "is not gating anything.");

        Assert.True(atThree.Count <= atOne.Count && atFour.Count <= atThree.Count,
            $"gold dot counts are not monotonic in the confluence requirement: " +
            $"K=1 {atOne.Count}, K=3 {atThree.Count}, K=4 {atFour.Count}.");

        Assert.True(atFour.IsSubsetOf(atThree) && atThree.IsSubsetOf(atOne),
            "a stricter confluence requirement fired gold on a bar a looser one did not — the " +
            "conditions are being counted differently per K rather than thresholded.");
    }

    /// <summary>
    /// A gold dot only ever appears on a bar that also carries a blue dot: gold is gated INSIDE the
    /// blue branch, and the two are different statements about the same entry.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void EveryGoldDotSitsOnABlueDot(int flavour)
    {
        var r = Run(flavour, Params());
        var blue = FiredBars(r, CipherBProvider.CompBlue).ToHashSet();
        var gold = FiredBars(r, CipherBProvider.CompGold);

        Assert.NotEmpty(gold);
        var orphans = gold.Where(b => !blue.Contains(b)).ToList();
        Assert.True(orphans.Count == 0,
            $"gold fired on {orphans.Count} bars with no blue dot (bars {string.Join(", ", orphans.Take(6))}).");
    }

    // ── The N-bar confirmation ──────────────────────────────────────────────────────

    /// <summary>
    /// <b>ConfirmBars means the wave was ALREADY where the dot says it was.</b>
    ///
    /// <para>
    /// A blue dot fires on a WT cross up while the wave is below oversold, and
    /// <c>ConfirmBars</c> requires it to have been below oversold for the preceding bars too —
    /// a dot on a one-bar spike is a dot on noise. The A2h mutant inverted the loop's test, so the
    /// gate passed exactly when the setup had NOT happened; the only thing that went red was a
    /// coverage-bookkeeping guard, whose natural repair is to edit a pinned list.
    /// </para>
    ///
    /// <para>
    /// Asserted on the bars that fired: at a confirmation of three bars, the two bars before each
    /// blue dot must also be below the oversold line in force at the dot.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void EveryBlueDotHasTheWaveBelowOversoldForItsConfirmationBars(int flavour)
    {
        const int ConfirmBars = 3;
        var r = Run(flavour, Params(("ConfirmBars", ConfirmBars)));

        var wt1 = Series(r, CipherBProvider.CompWT1);
        var os = Series(r, CipherBProvider.CompAdaptiveOs);
        var blue = FiredBars(r, CipherBProvider.CompBlue);

        Assert.True(blue.Count > 0, $"no blue dots on series {flavour} — this proves nothing");

        var unconfirmed = new List<string>();
        foreach (int b in blue)
        {
            for (int k = 1; k < ConfirmBars && (b - k) >= 0; k++)
            {
                double v = wt1[b - k];
                if (double.IsNaN(v) || v > os[b])
                    unconfirmed.Add($"bar {b}: wave {v:F1} at b-{k} against oversold {os[b]:F1}");
            }
        }

        Assert.True(unconfirmed.Count == 0,
            $"{unconfirmed.Count} blue dots on series {flavour} fired without the wave having been " +
            $"below oversold for the preceding {ConfirmBars - 1} bars:\n  " +
            string.Join("\n  ", unconfirmed.Take(6)));
    }

    /// <summary>
    /// And the requirement has to bite: demanding a longer run below oversold must not produce MORE
    /// dots. Counted across the flavour set, since blue dots are sparse on any one of them.
    /// </summary>
    [Fact]
    public void ALongerConfirmationNeverProducesMoreBlueDots()
    {
        int shortRun = 0, longRun = 0;
        foreach (int flavour in CausalityProbeSeries.HourlyFlavours)
        {
            shortRun += FiredBars(Run(flavour, Params(("ConfirmBars", 1))), CipherBProvider.CompBlue).Count;
            longRun += FiredBars(Run(flavour, Params(("ConfirmBars", 8))), CipherBProvider.CompBlue).Count;
        }

        Assert.True(shortRun > 0, "no blue dots at any confirmation length");
        Assert.True(longRun < shortRun,
            $"requiring eight confirmation bars produced as many blue dots as requiring one " +
            $"({longRun} against {shortRun}) — the confirmation is not gating anything.");
    }

    // ── The adaptive thresholds ─────────────────────────────────────────────────────

    /// <summary>
    /// <b><c>MinThresholdFloor</c> is a floor, and a floor holds in the direction it is named for.</b>
    ///
    /// <para>
    /// In Percentile mode the OB/OS lines are rolling percentiles of WT1, floored so they cannot
    /// collapse toward zero in a compressed regime — which is exactly when whipsaw is worst. The
    /// A2h mutant swapped <c>Math.Min</c> for <c>Math.Max</c> on the oversold side, pinning it AT
    /// the floor and removing the adaptation in the one regime the floor was added for.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheAdaptiveThresholdsNeverComeInsideTheirFloor(int flavour)
    {
        const double Floor = 40.0;
        var r = Run(flavour, Params(
            ("ThresholdMode", "Percentile"),
            ("MinThresholdFloor", Floor),
            ("AdaptiveLookback", 200)));

        var ob = Series(r, CipherBProvider.CompAdaptiveOb);
        var os = Series(r, CipherBProvider.CompAdaptiveOs);
        Assert.NotEmpty(ob);

        var obInside = Enumerable.Range(0, ob.Length).Where(i => !double.IsNaN(ob[i]) && ob[i] < Floor).ToList();
        var osInside = Enumerable.Range(0, os.Length).Where(i => !double.IsNaN(os[i]) && os[i] > -Floor).ToList();

        Assert.True(obInside.Count == 0,
            $"the adaptive overbought line came inside its {Floor} floor on {obInside.Count} bars.");
        Assert.True(osInside.Count == 0,
            $"the adaptive oversold line came inside its -{Floor} floor on {osInside.Count} bars " +
            $"(first at bar {osInside.FirstOrDefault()}, value {(osInside.Count > 0 ? os[osInside[0]] : 0):F2}).");
    }

    /// <summary>
    /// The anti-vacuity twin: in Percentile mode the lines must actually MOVE. A pair pinned at the
    /// floor for the whole series satisfies the test above and is the failure it is written against.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void InPercentileModeTheThresholdsActuallyAdapt(int flavour)
    {
        var r = Run(flavour, Params(
            ("ThresholdMode", "Percentile"),
            ("MinThresholdFloor", 40.0),
            ("AdaptiveLookback", 200)));

        var os = Series(r, CipherBProvider.CompAdaptiveOs).Where(v => !double.IsNaN(v)).ToList();
        Assert.True(os.Count > 0);
        Assert.True(os.Distinct().Count() > 1,
            "the adaptive oversold line held one value for the entire series — it is not adapting, " +
            "so the floor test above proves nothing.");
    }

    // ── The Money Flow wave's bounds ────────────────────────────────────────────────

    /// <summary>
    /// The Money Flow wave is clamped to +/-100 so it stays inside the WT pane it shares. The pane's
    /// Y axis auto-fits to what is in it, so a wave that leaves those bounds does not merely look
    /// wrong — it rescales the axis and flattens every other component beside it, including the one
    /// the user is arrowing along.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheMoneyFlowWaveStaysInsideThePane(int flavour)
    {
        var mf = Series(Run(flavour, Params()), CipherBProvider.CompMoneyFlowWave);
        var outside = Enumerable.Range(0, mf.Length)
            .Where(i => !double.IsNaN(mf[i]) && Math.Abs(mf[i]) > 100.0)
            .ToList();

        Assert.True(outside.Count == 0,
            $"the Money Flow wave left the +/-100 pane bounds on {outside.Count} bars " +
            $"(first at {outside.FirstOrDefault()}, value {(outside.Count > 0 ? mf[outside[0]] : 0):F1}).");
    }

    // ── The confirmation-lag exemption ──────────────────────────────────────────────

    /// <summary>
    /// <b>The shallow detector is exempt from the confirmation shift, and this is the test at the
    /// CALL SITE.</b>
    ///
    /// <para>
    /// <c>CipherBTimingTests</c> covers <c>ShiftMarkersForwardExcept</c> thoroughly — and only the
    /// helper. The A2h mutant changed the call in <c>Calculate</c> from the excepting form to the
    /// plain one, reintroducing the defect that helper exists to prevent, and every one of those
    /// helper tests stayed green. A helper with excellent tests and a caller free to stop calling it.
    /// </para>
    ///
    /// <para>
    /// The observable consequence: pivot-based divergences move by <c>pivotBars</c> when the lag is
    /// on, and shallow ones do not. So the two runs must AGREE on a non-empty set of bars — the
    /// shallow ones — while differing elsewhere. Shift everything and that agreement disappears.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheShallowDivergencesStayPutWhileThePivotOnesMove(int flavour)
    {
        const int PivotBars = 3;
        var lagged = Run(flavour, Params(("DivergenceConfirmLag", true), ("PivotBars", PivotBars)));
        var raw = Run(flavour, Params(("DivergenceConfirmLag", false), ("PivotBars", PivotBars)));

        int componentsExercised = 0;
        foreach (var component in new[] { CipherBProvider.CompBullDiv, CipherBProvider.CompBearDiv })
        {
            var withLag = FiredBars(lagged, component).ToHashSet();
            var without = FiredBars(raw, component).ToHashSet();

            // A given synthetic series produces mostly one direction; the other is not a failure.
            if (without.Count == 0) continue;
            componentsExercised++;

            var unmoved = withLag.Intersect(without).ToList();
            Assert.True(unmoved.Count > 0,
                $"every {component} marker on series {flavour} moved when the confirmation lag was " +
                "switched on. The shallow cross-based detector stamps at the WT crossover bar and is " +
                "already causal — shifting it moves a marker to a bar where its condition did not " +
                "occur. Some markers must be exempt.");

            var moved = without.Except(withLag).ToList();
            Assert.True(moved.Count > 0,
                $"no {component} marker moved at all on series {flavour} — the confirmation lag is " +
                "doing nothing, so the exemption above proves nothing.");
        }

        Assert.True(componentsExercised > 0,
            $"series {flavour} produced no regular divergences in either direction.");
    }

    /// <summary>
    /// And the direction of the shift, so "some markers moved" cannot be satisfied by markers moving
    /// backwards. A pivot marker becomes knowable LATER, never sooner.
    /// </summary>
    [Theory]
    [MemberData(nameof(Flavours))]
    public void TheConfirmationLagOnlyEverMovesAMarkerForward(int flavour)
    {
        const int PivotBars = 3;
        var laggedRun = Run(flavour, Params(("DivergenceConfirmLag", true), ("PivotBars", PivotBars)));
        var rawRun = Run(flavour, Params(("DivergenceConfirmLag", false), ("PivotBars", PivotBars)));

        int exercised = 0;
        foreach (var component in new[] { CipherBProvider.CompBullDiv, CipherBProvider.CompBearDiv })
        {
            var lagged = FiredBars(laggedRun, component);
            var raw = FiredBars(rawRun, component);
            if (raw.Count == 0) continue;
            exercised++;

            foreach (int b in lagged)
            {
                bool explained = raw.Contains(b) || raw.Contains(b - PivotBars);
                Assert.True(explained,
                    $"a {component} appeared at bar {b} with the lag on that is neither there without " +
                    $"it nor {PivotBars} bars before it — the shift is not a forward shift.");
            }
        }

        Assert.True(exercised > 0, $"series {flavour} produced no regular divergences.");
    }
}
