using AccessibleTrader.Sdk.Indicators;

namespace AccessibleTrader.Tests;

/// <summary>
/// Where <see cref="IndicatorMath"/>'s moving averages stop saying NaN and start saying a number.
///
/// <para>
/// A2/F10: the VALUES these produce are pinned by two library-parity suites, and the NaN edge is
/// not — mutants M07 (EMA) and M15 (SMA) both moved a warmup boundary by one and the suite stayed
/// green. The boundary is not cosmetic here. A number published one bar early is a number computed
/// from fewer samples than the indicator claims, and downstream it is indistinguishable from a
/// real one: it gets spoken as a value, sonified as a pitch, compared against a level, and — for a
/// strategy — traded on. The same class of error as look-ahead, arriving from the other side.
/// </para>
///
/// <para>
/// The expectations below are hand-computed from the definition, never recomputed by calling the
/// function under test. Every constant is derived in the comment beside it.
/// </para>
/// </summary>
public class IndicatorMathWarmupTests
{
    private const double Tol = 1e-12;

    private static int FirstNumber(double[] values)
    {
        for (int i = 0; i < values.Length; i++)
            if (!double.IsNaN(values[i])) return i;
        return -1;
    }

    // ── EMA ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// period 3, k = 2/(3+1) = 0.5, src 10 20 30 40:
    ///   i0 seeds ema = 10 (1 sample)   → NaN
    ///   i1 ema = 20·0.5 + 10·0.5 = 15  (2 samples) → NaN
    ///   i2 ema = 30·0.5 + 15·0.5 = 22.5 (3 samples) → the first published value
    ///   i3 ema = 40·0.5 + 22.5·0.5 = 31.25
    /// </summary>
    [Fact]
    public void Ema_publishes_its_first_value_on_the_period_th_sample_and_not_before()
    {
        var ema = IndicatorMath.Ema(new[] { 10.0, 20.0, 30.0, 40.0 }, period: 3);

        Assert.Equal(2, FirstNumber(ema));
        Assert.True(double.IsNaN(ema[0]));
        Assert.True(double.IsNaN(ema[1]));
        Assert.Equal(22.5, ema[2], Tol);
        Assert.Equal(31.25, ema[3], Tol);
    }

    /// <summary>
    /// A leading NaN is not a sample. src NaN 10 20 30 40 with period 3 must still wait for three
    /// real values, so the first number lands at index 3 rather than 2 — one bar later than the
    /// same series without the gap. An implementation that counted the NaN would publish at 2, on
    /// two samples' worth of information.
    /// </summary>
    [Fact]
    public void Ema_does_not_count_a_leading_gap_towards_its_warmup()
    {
        var ema = IndicatorMath.Ema(new[] { double.NaN, 10.0, 20.0, 30.0, 40.0 }, period: 3);

        Assert.Equal(3, FirstNumber(ema));
        Assert.Equal(22.5, ema[3], Tol);     // identical arithmetic, shifted one bar right
        Assert.Equal(31.25, ema[4], Tol);
    }

    /// <summary>
    /// A gap in the middle suspends the average; it does not restart it. src 10 20 NaN 30, period
    /// 3: the NaN bar publishes NaN and leaves ema = 15 and the sample count at 2, so the next
    /// real bar is the third sample and continues the smoothing — 30·0.5 + 15·0.5 = 22.5.
    /// Re-seeding from 30 instead would give 30 and hide the two bars of history the average is
    /// supposed to carry.
    /// </summary>
    [Fact]
    public void Ema_carries_its_state_across_a_gap_rather_than_reseeding()
    {
        var ema = IndicatorMath.Ema(new[] { 10.0, 20.0, double.NaN, 30.0 }, period: 3);

        Assert.True(double.IsNaN(ema[2]));
        Assert.Equal(3, FirstNumber(ema));
        Assert.Equal(22.5, ema[3], Tol);
    }

    /// <summary>
    /// period 1 has no warmup to serve: every bar is its own average. This is the degenerate end
    /// of the boundary and the one an off-by-one is most likely to swallow whole.
    /// </summary>
    [Fact]
    public void Ema_of_period_one_publishes_from_the_first_bar()
    {
        var ema = IndicatorMath.Ema(new[] { 10.0, 20.0, 30.0 }, period: 1);

        Assert.Equal(0, FirstNumber(ema));
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, ema);
    }

    /// <summary>Shorter than the period means no answer at all — not a partial one.</summary>
    [Fact]
    public void Ema_of_a_series_shorter_than_its_period_is_entirely_NaN()
    {
        var ema = IndicatorMath.Ema(new[] { 10.0, 20.0 }, period: 5);

        Assert.Equal(2, ema.Length);
        Assert.All(ema, v => Assert.True(double.IsNaN(v)));
    }

    // ── SMA ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// period 3, src 10 20 30 40: i2 = (10+20+30)/3 = 20, i3 = (20+30+40)/3 = 30.
    /// </summary>
    [Fact]
    public void Sma_publishes_its_first_value_on_the_period_th_bar_and_not_before()
    {
        var sma = IndicatorMath.Sma(new[] { 10.0, 20.0, 30.0, 40.0 }, period: 3);

        Assert.Equal(2, FirstNumber(sma));
        Assert.True(double.IsNaN(sma[0]));
        Assert.True(double.IsNaN(sma[1]));
        Assert.Equal(20.0, sma[2], Tol);
        Assert.Equal(30.0, sma[3], Tol);
    }

    /// <summary>
    /// SMA's gap policy is the opposite of EMA's and deliberately so: a window is either complete
    /// or it is nothing. One NaN at index 1 of 10 NaN 30 40 50 therefore blanks exactly the three
    /// windows that contain it (indices 2, 3 — index 1 is inside warmup anyway) and index 4 is the
    /// first clean window: (30+40+50)/3 = 40.
    ///
    /// <para>
    /// Averaging the two survivors instead would return 20 at index 2 — a "3-period average" of
    /// two bars, published under the same name, with nothing marking it as thinner.
    /// </para>
    /// </summary>
    [Fact]
    public void Sma_refuses_a_window_that_contains_a_gap_rather_than_averaging_what_is_left()
    {
        var sma = IndicatorMath.Sma(new[] { 10.0, double.NaN, 30.0, 40.0, 50.0 }, period: 3);

        Assert.True(double.IsNaN(sma[2]));   // window {10, NaN, 30}
        Assert.True(double.IsNaN(sma[3]));   // window {NaN, 30, 40}
        Assert.Equal(4, FirstNumber(sma));
        Assert.Equal(40.0, sma[4], Tol);     // window {30, 40, 50}
    }

    [Fact]
    public void Sma_of_period_one_publishes_from_the_first_bar()
    {
        var sma = IndicatorMath.Sma(new[] { 10.0, 20.0, 30.0 }, period: 1);

        Assert.Equal(0, FirstNumber(sma));
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, sma);
    }

    [Fact]
    public void Sma_of_a_series_shorter_than_its_period_is_entirely_NaN()
    {
        var sma = IndicatorMath.Sma(new[] { 10.0, 20.0 }, period: 5);

        Assert.Equal(2, sma.Length);
        Assert.All(sma, v => Assert.True(double.IsNaN(v)));
    }

    /// <summary>
    /// The two agree on WHEN, and disagree on the arithmetic — which is the point of having both.
    /// A refactor that unified the warmup rule by accident would be caught by the first half; one
    /// that unified the smoothing would be caught by the second.
    /// </summary>
    [Fact]
    public void Ema_and_Sma_start_speaking_on_the_same_bar_and_say_different_things()
    {
        var src = new[] { 10.0, 20.0, 30.0, 40.0, 50.0 };

        var ema = IndicatorMath.Ema(src, period: 3);
        var sma = IndicatorMath.Sma(src, period: 3);

        Assert.Equal(FirstNumber(sma), FirstNumber(ema));
        Assert.Equal(2, FirstNumber(ema));
        Assert.NotEqual(sma[3], ema[3], Tol);   // 30 vs 31.25
    }
}
