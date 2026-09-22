using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>How the chart's vertical space is divided, tested for the first time.</b>
///
/// <para>
/// The allocation had accumulated two bug-fix comments and no tests at all. Both describe
/// defects that nothing but a screenshot could catch — a pane drawn underneath the x-axis strip,
/// a pane pushed off the canvas entirely — which is precisely why it was extracted from
/// <c>Render</c> into a pure function on 2026-09-21.
/// </para>
///
/// <para>
/// The occasion was a third defect of the same invisible kind. Until that day the split was
/// <c>total / (1 + indicatorCount)</c>, described in its own comment as "each pane gets the same
/// vertical space" — so a chart carrying Volume and nothing else gave the CANDLES half the window
/// and the volume bars the other half. Measured on a real screenshot from the Windows head:
/// price 171px, volume 171px.
/// </para>
/// </summary>
public sealed class PaneHeightAllocationTests
{
    /// <summary>The pane area of Cody's maximised 1280x752 window, measured from the screenshot
    /// (dividers at y=242 and y=413, x-axis strip from y=584).</summary>
    private const float RealWindow = 344f;

    private static ChartRenderer.PaneAllocation Allocate(float total, int indicatorCount, float? weight = null)
    {
        var names = Enumerable.Range(0, indicatorCount).Select(i => $"Pane_{i}").ToList();
        return weight is null
            ? ChartRenderer.AllocatePaneHeights(total, names, null, density: 1f)
            : ChartRenderer.AllocatePaneHeights(total, names, null, density: 1f, mainPaneWeight: weight.Value);
    }

    // ── The defect ──────────────────────────────────────────────────────────────

    /// <summary>
    /// One indicator pane — the ordinary chart, Volume and nothing else. The price pane must get
    /// a clear majority rather than exactly half.
    /// </summary>
    [Fact]
    public void WithOnlyVolume_ThePricePaneGetsTwoThirds_NotHalf()
    {
        var a = Allocate(RealWindow, 1);

        Assert.True(a.MainHeight / RealWindow > 0.6f,
            $"the price pane got {100 * a.MainHeight / RealWindow:F0}% — an equal split gives the "
          + "volume bars as much room as the candles, which no charting convention does");
        Assert.InRange(a.MainHeight / RealWindow, 0.6f, 0.72f);
    }

    /// <summary>The old behaviour, stated so the change above is a comparison and not an
    /// assertion about nothing.</summary>
    [Fact]
    public void TheOldEqualWeightRuleGaveVolumeHalfTheChart()
    {
        var old = Allocate(RealWindow, 1, weight: 1f);

        Assert.Equal(0.5f, old.MainHeight / RealWindow, 2);
        Assert.Equal(old.MainHeight, old.IndicatorHeights[0], 1);
    }

    // ── The question Cody asked: does it squash the small panes? ────────────────

    /// <summary>
    /// <b>No indicator pane is ever pushed below its own declared minimum by the weighting.</b>
    /// This is the whole safety argument for the change: the weight only ever spends space the
    /// indicators were not entitled to.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UpToThreeIndicators_NoPaneFallsBelowItsFloor(int count)
    {
        var a = Allocate(RealWindow, count);

        Assert.All(a.IndicatorHeights, h => Assert.True(h >= 80f - 0.5f,
            $"an indicator pane got {h:F0}px, below the 80px minimum"));
    }

    /// <summary>
    /// <b>And from four indicator panes on, the weight changes NOTHING.</b> Cody's question was
    /// what happens with four indicators each in its own pane; the answer is that the 80px
    /// indicator floor and the 25% main floor have already decided the layout by then and the
    /// weight is never consulted. Whatever that chart looked like before, it looks identical now.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public void FromFourIndicatorsOn_TheWeightIsNotConsultedAtAll(int count)
    {
        var now = Allocate(RealWindow, count);
        var old = Allocate(RealWindow, count, weight: 1f);

        Assert.Equal(old.MainHeight, now.MainHeight, 2);
        Assert.Equal(old.IndicatorHeights.Length, now.IndicatorHeights.Length);
        for (int i = 0; i < now.IndicatorHeights.Length; i++)
            Assert.Equal(old.IndicatorHeights[i], now.IndicatorHeights[i], 2);
    }

    // ── The invariants the two bug-fix comments are about ──────────────────────

    /// <summary>
    /// <b>Everything must fit on the canvas.</b> The renderer stacks panes top-down by
    /// accumulating heights, so any overspend lands entirely on the LAST pane — drawn under the
    /// x-axis strip, or off the bottom. Nine panes on a 300px canvas is the case the FINAL FIT
    /// comment was written for.
    /// </summary>
    [Theory]
    [InlineData(300f, 8)]
    [InlineData(344f, 8)]
    [InlineData(260f, 6)]
    [InlineData(200f, 4)]
    [InlineData(1000f, 8)]
    public void ThePanesAlwaysFitTheCanvas(float total, int count)
    {
        var a = Allocate(total, count);
        float sum = a.MainHeight + a.IndicatorHeights.Sum();

        Assert.True(sum <= total + 0.5f,
            $"{count} panes on {total}px total {sum:F1}px — the excess lands on the last pane, "
          + "which is drawn under the x-axis strip or off the canvas");
    }

    /// <summary>Every pane must be visible. A pane of zero height is a pane Alt+PageDown can
    /// reach and the user cannot see.</summary>
    [Theory]
    [InlineData(300f, 8)]
    [InlineData(260f, 6)]
    [InlineData(344f, 4)]
    public void NoPaneIsAllocatedZeroHeight(float total, int count)
    {
        var a = Allocate(total, count);

        Assert.True(a.MainHeight > 0f);
        Assert.All(a.IndicatorHeights, h => Assert.True(h > 0f, "a pane got no height at all"));
    }

    /// <summary>
    /// A ratio the user set by hand wins over the weight. Resizing a pane is a deliberate act and
    /// the default must not argue with it.
    /// </summary>
    [Fact]
    public void AHandSavedRatioBeatsTheDefaultWeight()
    {
        var ratios = new Dictionary<string, float> { ["Pane_0"] = 0.5f };
        var a = ChartRenderer.AllocatePaneHeights(
            RealWindow, new[] { "Pane_0" }, ratios, density: 1f);

        Assert.Equal(RealWindow * 0.5f, a.IndicatorHeights[0], 1);
    }

    /// <summary>
    /// <b>Cody's 1h chart: Volume, RSI and MACD.</b> Pinned as an end-to-end expectation rather
    /// than arithmetic, because on 2026-09-22 a screenshot of exactly this configuration showed
    /// four panes of equal height — the weight-1 signature — and the only way to tell a stale
    /// binary from a broken wiring is to state what the current code must produce.
    /// </summary>
    [Fact]
    public void ThreeIndicatorPanes_ThePriceGetsFortyPercentAndTheRestShareEvenly()
    {
        var a = Allocate(440f, 3);

        Assert.InRange(a.MainHeight / 440f, 0.38f, 0.42f);
        Assert.All(a.IndicatorHeights, h => Assert.InRange(h, 85f, 91f));
        // The signature to look for on screen: the price pane is visibly TALLER than each
        // indicator pane. Equal heights means the running build predates the weight.
        Assert.True(a.MainHeight > a.IndicatorHeights.Max() * 1.5f,
            "the price pane must be clearly taller than an indicator pane, not the same height");
    }

    [Fact]
    public void WithNoIndicatorPanesThePriceTakesEverything()
    {
        var a = Allocate(RealWindow, 0);

        Assert.Equal(RealWindow, a.MainHeight, 2);
        Assert.Empty(a.IndicatorHeights);
    }
}
