using AccessibleTrader.Core.Services;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// A reference level may only stretch the price axis if it is a price.
///
/// <para>
/// <b>The defect, from a maintainer screenshot.</b> A BTC chart trading near 63,900 rendered with a
/// y-axis running 0 to 70,000, compressing every candle into the top tenth of the pane. Nothing
/// errored and no test failed; the chart was simply unreadable.
/// </para>
///
/// <para>
/// <c>ViewportRangeCalculator</c> expands the main range to include any visible reference level on a
/// main-pane series, which is right for a bounded metric — a Fear &amp; Greed chart showing 10–90
/// should still draw its 0 and 100 zone lines. But a series whose <c>Pane</c> is unset falls back to
/// "Main", and some indicators declare levels in their own units:
/// <c>LoukasCyclesProvider</c> publishes "DC Floor" at <b>0.0</b>, "DC Window Open" at 35.0 and
/// "DC Overdue" at 90.0 — bar counts, days into a cycle.
/// </para>
///
/// <para>
/// The guard is a ratio rather than a fix to pane bookkeeping, because it encodes the actual
/// invariant — a level on a price pane has to be a price — and therefore still holds when a pane
/// assignment is missing or wrong.
/// </para>
/// </summary>
public class ViewportRangeUnitGuardTests
{
    private static bool Ok(double level, double min, double max) =>
        ViewportRangeCalculator.IsPlausiblySamePane(level, min, max, max - min);

    /// <summary>The exact reported case: a bar-count level on a price chart.</summary>
    [Fact]
    public void ABarCountLevelCannotStretchAPriceAxisToZero()
    {
        // BTC 4h, visible 63,000–64,500.
        Assert.False(Ok(0.0, 63_000, 64_500));   // "DC Floor"
        Assert.False(Ok(35.0, 63_000, 64_500));  // "DC Window Open"
        Assert.False(Ok(90.0, 63_000, 64_500));  // "DC Overdue"
    }

    /// <summary>
    /// The case the expansion exists for must keep working. Being too strict here would silently
    /// clip the zone lines the feature was added to show.
    /// </summary>
    [Fact]
    public void BoundedMetricZoneLinesAreStillAllowed()
    {
        // Fear & Greed showing 10–90; its 0/25/50/75/100 zone lines must all survive.
        foreach (double zone in new[] { 0.0, 25.0, 50.0, 75.0, 100.0 })
            Assert.True(Ok(zone, 10, 90), $"zone {zone} was wrongly rejected");
    }

    /// <summary>A support line a little under the visible window is a normal, useful thing.</summary>
    [Fact]
    public void ALevelModestlyOutsideTheWindowIsAllowed()
    {
        // Visible 100–110 (span 10); a level at 95 is half a span below.
        Assert.True(Ok(95, 100, 110));
        // Three spans out is the documented boundary and still allowed.
        Assert.True(Ok(70, 100, 110));
        // Four spans out is not.
        Assert.False(Ok(59, 100, 110));
    }

    [Fact]
    public void ALevelInsideTheWindowIsAlwaysAllowed()
        => Assert.True(Ok(105, 100, 110));

    /// <summary>
    /// A flat series has no span to judge against, so nothing can be ruled out — refusing here
    /// would break the one case where the expansion is the only thing giving the pane a scale.
    /// </summary>
    [Fact]
    public void AFlatSeriesAcceptsAnyLevel()
        => Assert.True(ViewportRangeCalculator.IsPlausiblySamePane(0, 100, 100, 0));

    [Fact]
    public void NonFiniteLevelsAreRejected()
    {
        Assert.False(Ok(double.NaN, 100, 110));
        Assert.False(Ok(double.PositiveInfinity, 100, 110));
    }

    /// <summary>Loose on purpose: this catches unit mismatches, it does not police zones.</summary>
    [Fact]
    public void TheBoundIsDeliberatelyGenerous()
        => Assert.True(ViewportRangeCalculator.MaxLevelSpanMultiple >= 2.0);
}
