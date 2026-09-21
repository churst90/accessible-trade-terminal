using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Audio;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>Equal fractions of the pane are equal musical intervals — the second half of the fix whose
/// first half landed on 2026-09-18.</b>
///
/// <para>
/// That first half gave the eye and the ear one normaliser: after it,
/// <see cref="ChartMath.NormalizedPosition"/> answers "what fraction of the pane is this value
/// at" identically for the renderer and for the sonification, on the linear scale and on the log
/// one. It did not touch the step AFTER — turning that fraction into a pitch — which was
/// <c>200 + n × 800</c>, LINEAR IN HERTZ. Pitch perception is logarithmic in frequency, so a
/// hertz-linear ramp is not a straight line to the ear: the bottom quarter of the pane carried a
/// whole octave (200→400 Hz) and the top quarter carried a major third (800→1000). The same
/// distance travelled up the pane sounded five times larger at the bottom than at the top, and a
/// price riding high in the window moved a long way on screen and barely at all in the ear.
/// </para>
///
/// <para>
/// The band is unchanged at 200–1000 Hz, so the floor and the ceiling sound exactly as they
/// always have and only the interior is redistributed.
/// </para>
/// </summary>
public sealed class PerceptualPitchMappingTests
{
    private const double Floor = 200.0, Ceiling = 1000.0;

    private static double Octaves(double a, double b) => Math.Log2(b / a);

    // ── The endpoints did not move ───────────────────────────────────────────────

    [Fact]
    public void TheFloorAndTheCeilingAreWhereTheyAlwaysWere()
    {
        Assert.Equal(Floor,   AudioConstants.PitchForPosition(0.0), 6);
        Assert.Equal(Ceiling, AudioConstants.PitchForPosition(1.0), 6);
    }

    // ── The property, and it is the whole point ──────────────────────────────────

    /// <summary>
    /// Ten equal tenths of the pane, and every one of them must be the same interval. Under the
    /// old hertz-linear ramp the first tenth was 0.263 octaves and the last was 0.116 — a ratio
    /// of 2.3 between two steps a user would describe identically ("I moved up a tenth of the
    /// chart").
    /// </summary>
    [Fact]
    public void EveryTenthOfThePaneIsTheSameInterval()
    {
        var steps = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            double lo = AudioConstants.PitchForPosition(i / 10.0);
            double hi = AudioConstants.PitchForPosition((i + 1) / 10.0);
            steps.Add(Octaves(lo, hi));
        }

        double first = steps[0];
        Assert.All(steps, s => Assert.Equal(first, s, 9));

        // And the pane as a whole is log2(1000/200) = 2.3219 octaves, so each tenth is 0.2322.
        Assert.Equal(Octaves(Floor, Ceiling) / 10.0, first, 9);
    }

    /// <summary>
    /// The old mapping, written out, so the claim above is a comparison rather than an assertion
    /// about nothing. This is the test that would have failed before the change and the reason
    /// the change is not cosmetic.
    /// </summary>
    [Fact]
    public void TheOldHertzLinearRamp_GaveTheBottomOfThePaneFarMorePitchThanTheTop()
    {
        static double OldRamp(double n) => 200 + n * 800;

        double bottomTenth = Octaves(OldRamp(0.0), OldRamp(0.1));
        double topTenth    = Octaves(OldRamp(0.9), OldRamp(1.0));

        Assert.True(bottomTenth > topTenth * 2.2,
            $"fixture check: the old ramp's bottom tenth ({bottomTenth:F3} oct) should dwarf its top ({topTenth:F3} oct)");

        // The ends of the pane are the extreme case and the one a user meets: 1% of the pane at
        // the floor was 0.0284 octaves — nearly half a semitone — and 1% at the ceiling was
        // 0.0115, a fifth of one. Under the new mapping both are 0.0232.
        double newBottom = Octaves(AudioConstants.PitchForPosition(0.00), AudioConstants.PitchForPosition(0.01));
        double newTop    = Octaves(AudioConstants.PitchForPosition(0.99), AudioConstants.PitchForPosition(1.00));
        Assert.Equal(newBottom, newTop, 9);
    }

    /// <summary>
    /// Half the pane is the geometric mean of the band, not the arithmetic one. This is the
    /// number that moves most: 447 Hz where the old ramp said 600 Hz.
    /// </summary>
    [Fact]
    public void HalfThePaneIsTheGeometricMeanOfTheBand()
    {
        Assert.Equal(Math.Sqrt(Floor * Ceiling), AudioConstants.PitchForPosition(0.5), 6);
        Assert.Equal(447.2136, AudioConstants.PitchForPosition(0.5), 3);
    }

    /// <summary>
    /// Monotonic and inside the band for every input, including the ones a caller should never
    /// send. Audio has no off-canvas: a NaN reaching the driver is a click or a silence, not a
    /// visibly wrong pixel that someone would report.
    /// </summary>
    [Theory]
    [InlineData(-5.0)]
    [InlineData(-0.001)]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.001)]
    [InlineData(17.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TheResultIsAlwaysAUsableFrequencyInsideTheBand(double n)
    {
        double f = AudioConstants.PitchForPosition(n);
        Assert.True(double.IsFinite(f), $"{n} produced {f}");
        Assert.InRange(f, Floor, Ceiling);
    }

    [Fact]
    public void ItIsStrictlyRising()
    {
        double prev = double.MinValue;
        for (int i = 0; i <= 100; i++)
        {
            double f = AudioConstants.PitchForPosition(i / 100.0);
            Assert.True(f > prev, $"pitch fell at n = {i / 100.0}");
            prev = f;
        }
    }

    /// <summary>
    /// A degenerate band must not produce NaN out of <c>Math.Pow</c> on a negative ratio. The
    /// fallback is the linear form, which is wrong perceptually and right arithmetically — the
    /// caller that got here has a bigger problem than the interval spacing.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1000.0)]
    [InlineData(-200.0, 1000.0)]
    [InlineData(1000.0, 200.0)]
    [InlineData(440.0, 440.0)]
    public void ADegenerateBandFallsBackRatherThanReturningNaN(double floorHz, double ceilingHz)
    {
        double f = AudioConstants.PitchForPosition(0.5, floorHz, ceilingHz);
        Assert.True(double.IsFinite(f), $"band {floorHz}–{ceilingHz} produced {f}");
    }

    // ── What this buys the auto-fit change ───────────────────────────────────────

    /// <summary>
    /// <b>Why this had to land alongside the price-pane auto-fit change and not after it.</b>
    /// Including Main-pane overlays widens the price range, which compresses the price line into
    /// a smaller fraction of the pane. Under this mapping that costs a CONSTANT number of
    /// octaves wherever in the pane the price happens to sit — a predictable transposition the
    /// user can reason about, and the thing Alt+F trades away. Under the old ramp the identical
    /// compression cost several times as much pitch at the top of the pane as at the bottom, so
    /// the penalty for adding a Bollinger band depended on where the price was standing.
    /// </summary>
    [Fact]
    public void CompressingThePriceIntoAThirdOfThePane_CostsTheSamePitchWhereverItSits()
    {
        const double share = 1.0 / 3.0;

        double low  = Octaves(AudioConstants.PitchForPosition(0.00), AudioConstants.PitchForPosition(share));
        double mid  = Octaves(AudioConstants.PitchForPosition(0.33), AudioConstants.PitchForPosition(0.33 + share));
        double high = Octaves(AudioConstants.PitchForPosition(1 - share), AudioConstants.PitchForPosition(1.00));

        Assert.Equal(low, mid, 9);
        Assert.Equal(low, high, 9);
        Assert.Equal(Octaves(Floor, Ceiling) * share, low, 9);

        // The old ramp, for contrast: the same third of the pane was worth 1.22 octaves at the
        // bottom and 0.45 at the top — the same gesture, worth two and three-quarter times as
        // much pitch depending on where the price happened to be standing.
        static double OldRamp(double n) => 200 + n * 800;
        double oldLow  = Octaves(OldRamp(0.00), OldRamp(share));
        double oldHigh = Octaves(OldRamp(1 - share), OldRamp(1.00));
        Assert.True(oldLow > oldHigh * 2.5, "fixture check: the old ramp's penalty was position-dependent");
    }
}
