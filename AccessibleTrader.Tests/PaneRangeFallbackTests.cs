using System.Collections.Immutable;
using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>A missing pane range never falls back to the PRICE range for an indicator pane.</b>
///
/// <para>
/// §5 of <c>docs/SHARED_OSCILLATOR_PANE_2026-09-11.md</c>. Both audio sites — navigation pitch and
/// playback — read <c>state.PaneRanges[pane]</c> and, when the key was absent, used
/// <c>state.ViewportRange</c>: the price range. For the Main pane that is the right answer by
/// definition. For an oscillator it is a 0–100 value normalised against 99,900–100,100 — one flat
/// tone, no log. It did not cause Cody's report (the key was present; the pane was shared), but it
/// fails silently in the same direction, and <c>PaneRanges</c> can lag the series list.
/// </para>
/// </summary>
public sealed class PaneRangeFallbackTests
{
    private static readonly (double, double) Price = (99_900, 100_100);

    private static ImmutableDictionary<string, (double Min, double Max)> Ranges(params (string Key, (double, double) Range)[] entries) =>
        entries.ToImmutableDictionary(e => e.Key, e => e.Range, StringComparer.Ordinal);

    [Fact]
    public void APresentPaneKey_IsUsed()
    {
        var ranges = Ranges(("Pane_Rsi", (25, 75)));
        Assert.Equal((25, 75), ViewportRangeCalculator.RangeFor(ranges, "Pane_Rsi", null, Price));
    }

    [Fact]
    public void ASubPaneKey_WinsOverItsPane_AndFallsBackToIt()
    {
        var ranges = Ranges(("Pane_CIPHER_B", (-100, 100)), ("Pane_CIPHER_B/MF", (-3, 3)));
        Assert.Equal((-3, 3), ViewportRangeCalculator.RangeFor(ranges, "Pane_CIPHER_B", "MF", Price));
        Assert.Equal((-100, 100), ViewportRangeCalculator.RangeFor(ranges, "Pane_CIPHER_B", "FY", Price));
    }

    /// <summary>The defect: an oscillator with no range entry must not be measured in dollars.</summary>
    [Fact]
    public void AMissingIndicatorPane_FallsBackToItsEmptyRange_NotThePriceRange()
    {
        var ranges = Ranges(("Main", Price));
        var range = ViewportRangeCalculator.RangeFor(ranges, "Pane_Rsi", null, Price);
        Assert.Equal((0, 100), range);
        Assert.NotEqual(Price, range);
    }

    [Fact]
    public void AMissingCipherBPane_FallsBackToPlusMinus100()
    {
        Assert.Equal((-100, 100), ViewportRangeCalculator.RangeFor(Ranges(), "Pane_CIPHER_B", null, Price));
        // A sub-pane of it takes the plain default, as the range calculator itself does.
        Assert.Equal((0, 100), ViewportRangeCalculator.RangeFor(Ranges(), "Pane_CIPHER_B", "MF", Price));
    }

    /// <summary>Main's range IS the viewport range, so for Main the old fallback stays right.</summary>
    [Theory]
    [InlineData("Main")]
    [InlineData("")]
    [InlineData(null)]
    public void AMissingMainPane_StillFallsBackToTheViewportRange(string? pane)
    {
        Assert.Equal(Price, ViewportRangeCalculator.RangeFor(Ranges(), pane, null, Price));
        Assert.Equal(Price, ViewportRangeCalculator.RangeFor(null, pane, null, Price));
    }

    /// <summary>The calculator's own empty default and the fallback are the same function.</summary>
    [Fact]
    public void TheEmptyDefault_IsTheOneTheCalculatorUses()
    {
        Assert.Equal((0, 100), ViewportRangeCalculator.EmptyPaneRange("Pane_Rsi"));
        Assert.Equal((-100, 100), ViewportRangeCalculator.EmptyPaneRange("Pane_CIPHER_B"));
        Assert.Equal((0, 100), ViewportRangeCalculator.EmptyPaneRange("Pane_CIPHER_B/MF"));
    }
}
