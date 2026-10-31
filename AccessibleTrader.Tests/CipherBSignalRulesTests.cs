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
/// <b>2026-10-31:</b> the rules this file stated — the divergence depth gate and pivots, the anchor
/// suppression, the N-bar confirmation, the adaptive thresholds and the confirmation-lag exemption —
/// went with the parts they ruled (Cody: "remove all of that old code from cipher b"). Cipher B is
/// now Market Cipher B's formulas, pinned value for value in CipherBMcbFormulaTests and
/// CipherBWaveTrendParityTests and its dots in CipherBLookTests. The Money Flow bound stays here.
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

    /// <summary>1,400 bars: long enough for the divergence cases the removed tests needed, kept so
    /// the Money Flow bound is checked on the same paths.</summary>
    private const int Bars = 1400;

    private static Dictionary<string, double[]> Run(int flavour, Dictionary<string, object> pars)
    {
        var bars = CausalityProbeSeries.Bars(flavour, Bars);
        var results = new Dictionary<string, double[]>();
        var buffer = new IndicatorResultBuffer(results, bars.Count);
        Provider.Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            pars, buffer);
        return results;
    }

    private static double[] Series(Dictionary<string, double[]> r, string component)
        => r.TryGetValue(component, out var a) ? a : Array.Empty<double>();

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
        var mf = Series(Run(flavour, new Dictionary<string, object>()), CipherBProvider.CompMoneyFlowWave);
        var outside = Enumerable.Range(0, mf.Length)
            .Where(i => !double.IsNaN(mf[i]) && Math.Abs(mf[i]) > 100.0)
            .ToList();

        Assert.True(outside.Count == 0,
            $"the Money Flow wave left the +/-100 pane bounds on {outside.Count} bars " +
            $"(first at {outside.FirstOrDefault()}, value {(outside.Count > 0 ? mf[outside[0]] : 0):F1}).");
    }
}
