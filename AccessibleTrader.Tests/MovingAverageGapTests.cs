// M15 — "SMA silently averages a short window when the source has gaps".
//
// A2's mutant (2026-08-26) changed MovingAverageHelper.Sma's last line from
//     r[i] = cnt == period ? sum / period : double.NaN;
// to
//     r[i] = cnt > 0   ? sum / cnt    : double.NaN;
// and survived a green suite twice — once in A2, once in the 2026-08-28 re-measurement.
// Nothing anywhere fed a gapped source to a moving average, so the whole NaN branch was
// untested: a "20-period SMA" computed from three real bars and seventeen holes came back
// as a number, indistinguishable from an honest one, and was then spoken, drawn and used
// to arm signals.
//
// Gaps are not hypothetical here. Every helper in this file emits NaN for its own warmup
// window, and the composite types feed one MA into another — Hma runs Wma over a diff
// series built from two Wmas, Dema/Tema stack Emas — so a *chained* MA is fed a NaN-headed
// source by construction. The same holds for any indicator built on a resampled or
// gap-filled series.
//
// What is asserted here is the contract the doc-comment already claims ("All methods return
// NaN for bars within the warmup window"), extended to the case the mutant exploited: a
// window that is short because the SOURCE is holed, not because the array just started.

using AccessibleTrader.Core.Services.Indicators;

namespace AccessibleTrader.Tests;

public class MovingAverageGapTests
{
    private const double N = double.NaN;

    /// <summary>
    /// The kill. Every window overlapping the hole must be NaN, and specifically must NOT be
    /// the short-window mean the mutation produces — which is why the expected values are
    /// written next to the average of what is actually present.
    /// </summary>
    [Fact]
    public void Sma_ReturnsNaN_WhereTheWindowSpansAHoleInTheSource()
    {
        // period 3. Index 4 is missing.
        //   i=2 window {1,2,3}      complete   -> 2
        //   i=3 window {2,3,NaN}    2 of 3     -> NaN   (mutation: 2.5)
        //   i=4 window {3,NaN,6}    2 of 3     -> NaN   (mutation: 4.5)
        //   i=5 window {NaN,6,7}    2 of 3     -> NaN   (mutation: 6.5)
        //   i=6 window {6,7,8}      complete   -> 7
        var src = new double[] { 1, 2, 3, N, 6, 7, 8 };

        var sma = MovingAverageHelper.Sma(src, 3);

        Assert.True(double.IsNaN(sma[0]) && double.IsNaN(sma[1]),
            "The first period-1 bars are the warmup window and must be NaN.");
        Assert.Equal(2.0, sma[2], 10);

        foreach (int i in new[] { 3, 4, 5 })
            Assert.True(double.IsNaN(sma[i]),
                $"Sma[{i}] is {sma[i]} — its 3-bar window contains a gap, so only 2 bars were " +
                "summed. Averaging them and calling the result a 3-period SMA is the defect: a " +
                "caller cannot tell a short-window mean from an honest one, and it is announced, " +
                "plotted and used to arm signals exactly like a real value.");

        Assert.Equal(7.0, sma[6], 10);
    }

    /// <summary>
    /// The vacuity check for the test above. A "returns NaN" assertion also passes against an
    /// Sma that returns NaN for everything, so the same input must still produce real numbers
    /// wherever the window is whole.
    /// </summary>
    [Fact]
    public void Sma_StillComputesWhereTheWindowIsWhole()
    {
        var src = new double[] { 1, 2, 3, N, 6, 7, 8 };
        var sma = MovingAverageHelper.Sma(src, 3);

        Assert.Equal(2, sma.Count(v => !double.IsNaN(v)));
    }

    /// <summary>
    /// A hole one bar wide silences a period-length stretch of output, not one bar. Stated
    /// separately because it is the property a caller reasons about ("how much of my series
    /// is unusable?") and it is what makes the mutation attractive to write in the first place.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void Sma_OneHoleSuppressesExactlyPeriodOutputs(int period)
    {
        var src = Enumerable.Range(1, 40).Select(i => (double)i).ToArray();
        var holed = (double[])src.Clone();
        holed[20] = N;

        var clean = MovingAverageHelper.Sma(src, period);
        var gapped = MovingAverageHelper.Sma(holed, period);

        int lostToTheHole = Enumerable.Range(0, src.Length)
            .Count(i => !double.IsNaN(clean[i]) && double.IsNaN(gapped[i]));

        Assert.Equal(period, lostToTheHole);
    }

    /// <summary>
    /// The same contract for the other helpers, so "gapped input yields NaN" is a property of
    /// the file rather than of one method. Wma and the composites already hold it; they are
    /// pinned here because M15's mutation is one character away in each of them and no test
    /// covered any of them either.
    /// </summary>
    [Theory]
    [InlineData("SMA")]
    [InlineData("WMA")]
    [InlineData("HMA")]
    [InlineData("DEMA")]
    [InlineData("TEMA")]
    [InlineData("EMA")]
    public void EveryMaType_RefusesToInventAValueOverAHole(string maType)
    {
        var src = Enumerable.Range(1, 60).Select(i => (double)i).ToArray();
        src[30] = N;

        var r = MovingAverageHelper.Calculate(src, 5, maType);

        Assert.Equal(src.Length, r.Length);
        Assert.True(double.IsNaN(r[30]),
            $"{maType} produced {r[30]} for a bar whose own source value is missing.");

        // ...and it recovers: an MA that goes NaN at the first hole and stays there for the
        // rest of the series is not honest either, it is broken.
        Assert.True(r.Skip(31).Any(v => !double.IsNaN(v)),
            $"{maType} never produced another value after the hole at index 30 — a single " +
            "missing bar must not poison the remainder of the series.");
    }
}
