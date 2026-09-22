using AccessibleTrader.Core.Services;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The y axis: every label sits on a gridline.</b>
///
/// <para>
/// <c>ChartRenderer.RenderYAxis</c> promised it — "Label positions align exactly with major
/// gridlines so the chart reads as a coherent grid, not a grid + an unrelated label track" — and
/// did not deliver it, because the promise was made by one of TWO copies of the nice-number
/// algorithm. <c>BackgroundLayer</c> divided the range by 7 for gridlines; <c>RenderYAxis</c>
/// divided it by 5 for labels. On a pane of range 20 that is a grid stepping by 2 and labels
/// stepping by 5, so the labels at 5 and 15 sat on nothing. Roughly 17.5–24.5 × 10^k does it,
/// which includes a 20,000-dollar window on a BTC chart.
/// </para>
///
/// <para>
/// A blind user cannot detect this and a sighted one sees it on every such chart — which is the
/// reason the rendering path had accumulated 190 test methods and no test of the axis. These
/// pin the arithmetic; <see cref="ChartFrameRenderingTests"/> pins what reaches the pixels.
/// </para>
/// </summary>
public sealed class ChartAxisMathTests
{
    // ── The step ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(30.0, 7, 5.0)]        // 30/7 = 4.29 → the 5 in 1-2-5-10
    [InlineData(20.0, 7, 2.0)]        // 20/7 = 2.86 → 2
    [InlineData(20.0, 5, 5.0)]        // 20/5 = 4.00 → 5   (the two that disagreed)
    [InlineData(100.0, 5, 20.0)]      // 100/5 = 20 → 2×10
    [InlineData(16_000.0, 7, 2000.0)] // a BTC window
    [InlineData(0.00007, 7, 0.00001)] // a sub-cent token
    public void NiceStep_PicksFromOneTwoFiveTen(double range, int target, double expected)
        => Assert.Equal(expected, ChartMath.NiceStep(range, target), 12);

    [Theory]
    [InlineData(0.0)] [InlineData(-5.0)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void NiceStep_OnADegenerateRange_IsZero_SoTheCallerDrawsNothing(double range)
        => Assert.Equal(0.0, ChartMath.NiceStep(range, 7));

    // ── The property the defect broke ────────────────────────────────────────

    /// <summary>
    /// <b>Red on the code as it stood.</b> Over four decades of range and both pane sizes, the
    /// label step must be a whole multiple of the gridline step — which is what makes every label
    /// land on a line, for every range, rather than for most of them.
    /// </summary>
    [Fact]
    public void EveryLabelStep_IsAWholeMultipleOfTheGridlineStep()
    {
        var offenders = new List<string>();
        foreach (double magnitude in new[] { 0.0001, 0.01, 1.0, 100.0, 10_000.0 })
        foreach (double mantissa in Enumerable.Range(10, 191).Select(i => i / 10.0))   // 1.0 … 20.0
        foreach (float paneHeight in new[] { 60f, 400f })
        {
            double range = mantissa * magnitude;
            double grid = ChartMath.GridStep(range);
            double label = ChartMath.LabelStep(range, ChartMath.TargetLabelCount(paneHeight, 1f), grid);

            double multiple = label / grid;
            if (Math.Abs(multiple - Math.Round(multiple)) > 1e-9 || Math.Round(multiple) < 1)
                offenders.Add($"range {range}, pane {paneHeight}px: grid {grid}, label {label}");
        }
        Assert.True(offenders.Count == 0,
            $"{offenders.Count} ranges put a label between gridlines:\n  " +
            string.Join("\n  ", offenders.Take(10)));
    }

    /// <summary>
    /// The case from the report, spelled out. A pane of range 20 used to grid at 2 and label at 5.
    /// </summary>
    [Fact]
    public void ThePaneOfRangeTwenty_LabelsOnItsGridlines()
    {
        double grid = ChartMath.GridStep(20);
        double label = ChartMath.LabelStep(20, 5, grid);

        Assert.Equal(2.0, grid, 12);
        Assert.Equal(4.0, label, 12);   // a multiple of 2 — 5 was not
        for (double v = 0; v <= 20; v += label)
            Assert.True(ChartMath.IsOnLabel(v, label), $"{v} should be a labelled line");
    }

    /// <summary>
    /// The label step is never finer than the grid step. That would put labels BETWEEN lines,
    /// which is the same defect read the other way round, and a small pane asking for three
    /// labels on a range the grid already covers in six steps is exactly where it could happen.
    /// </summary>
    [Theory]
    [InlineData(1.0)] [InlineData(7.0)] [InlineData(45.0)] [InlineData(0.003)]
    public void TheLabelStep_IsNeverFinerThanTheGrid(double range)
    {
        double grid = ChartMath.GridStep(range);
        Assert.True(ChartMath.LabelStep(range, 5, grid) >= grid);
        Assert.True(ChartMath.LabelStep(range, 3, grid) >= grid);
    }

    /// <summary>A small indicator pane wants fewer labels than a full-height price pane.</summary>
    [Fact]
    public void ASmallPaneAsksForFewerLabels()
    {
        Assert.Equal(3, ChartMath.TargetLabelCount(paneHeightPx: 80f, density: 1f));
        Assert.Equal(5, ChartMath.TargetLabelCount(paneHeightPx: 400f, density: 1f));
        // Density scales the threshold — a 160px pane on a 2× display is still a small pane.
        Assert.Equal(3, ChartMath.TargetLabelCount(paneHeightPx: 160f, density: 2f));
    }

    [Fact]
    public void IsOnLabel_IsToleratedAgainstTheAxisWalk()
    {
        // The axis walks by repeated addition, so a value reached in twenty steps carries
        // twenty steps' worth of residue. An exact modulo would call that line minor.
        double step = 0.1, v = 0;
        for (int i = 0; i < 20; i++) v += step;
        Assert.True(ChartMath.IsOnLabel(v, step));
        Assert.False(ChartMath.IsOnLabel(v + step / 2, step));
    }

    // ── The label text ───────────────────────────────────────────────────────

    /// <summary>
    /// A flat F2 collapses a sub-cent token to "0.00" and says nothing. The decimals come from
    /// the range's magnitude, so the label always carries about two digits past the range scale.
    /// </summary>
    [Theory]
    [InlineData(0.00003, 0.00001, "0.0000300")]
    [InlineData(64_120.5, 16_000.0, "64120.50")]
    [InlineData(70.0, 100.0, "70.00")]
    [InlineData(0.0831, 0.01, "0.0831")]
    public void FormatAxisValue_CarriesTheDigitsTheRangeNeeds(double value, double range, string expected)
        => Assert.Equal(expected, ChartMath.FormatAxisValue(value, range));

    /// <summary>
    /// The decimal count is clamped at ten. Without the clamp a range near zero asks for
    /// arbitrarily many, and "F17" is not a price.
    /// </summary>
    [Fact]
    public void FormatAxisValue_StopsAtTenDecimals()
    {
        string text = ChartMath.FormatAxisValue(0.000000000001, 0.0000000000001);
        Assert.Equal(10, text.Length - text.IndexOf('.') - 1);
    }

    /// <summary>
    /// "−0.00" is a rounding residue from the axis-step arithmetic wearing a minus sign. On a
    /// price axis it reads as a data error and makes a careful reader distrust every other number
    /// on the screen.
    /// </summary>
    [Theory]
    [InlineData(-0.0000001, 100.0)]
    [InlineData(-0.004, 1.0)]
    public void FormatAxisValue_NeverReturnsANegativeZero(double value, double range)
    {
        string text = ChartMath.FormatAxisValue(value, range);
        Assert.False(text.StartsWith('-') && text.Skip(1).All(c => c is '0' or '.' or ','),
            $"got {text}");
    }

    /// <summary>A real negative keeps its sign — the strip must not eat one.</summary>
    [Theory]
    [InlineData(-50.0, 100.0)]
    [InlineData(-0.01, 1.0)]
    public void FormatAxisValue_KeepsARealNegative(double value, double range)
        => Assert.StartsWith("-", ChartMath.FormatAxisValue(value, range));

    // ── The short form, for a strip that cannot hold nine digits ─────────────

    /// <summary>
    /// The form a person would say. US large-cap share volume is routinely nine digits, so
    /// "120000000.00" is the COMMON case on a volume axis, not an outlier — and the strip is
    /// 60 CSS px wide.
    /// </summary>
    [Theory]
    [InlineData(120_000_000.0, "120M")]
    [InlineData(100_000_000.0, "100M")]
    [InlineData(1_500_000.0, "1.5M")]
    [InlineData(950_000.0, "950K")]
    [InlineData(2_500_000_000.0, "2.5B")]
    [InlineData(1_200_000_000_000.0, "1.2T")]
    [InlineData(440.0, "440")]
    [InlineData(0.0, "0")]
    public void FormatAxisValueCompact_SaysTheNumberTheWayAPersonWould(double value, string expected)
        => Assert.Equal(expected, ChartMath.FormatAxisValueCompact(value));

    /// <summary>
    /// Whatever else it does, the short form is SHORTER for the values it exists for. If it
    /// were not, the renderer's measurement would keep the full text and this whole path would
    /// be dead code that still passed its own unit tests.
    /// </summary>
    [Theory]
    [InlineData(120_000_000.0, 140_000_000.0)]
    [InlineData(1_500_000.0, 2_000_000.0)]
    [InlineData(2_500_000_000.0, 3_000_000_000.0)]
    public void FormatAxisValueCompact_IsShorterThanTheFullForm(double value, double range)
        => Assert.True(
            ChartMath.FormatAxisValueCompact(value).Length < ChartMath.FormatAxisValue(value, range).Length,
            $"compact '{ChartMath.FormatAxisValueCompact(value)}' is not shorter than full "
          + $"'{ChartMath.FormatAxisValue(value, range)}'");

    /// <summary>A real negative keeps its sign here too.</summary>
    [Theory]
    [InlineData(-120_000_000.0, "-120M")]
    [InlineData(-2_500.0, "-2.5K")]
    public void FormatAxisValueCompact_KeepsARealNegative(double value, string expected)
        => Assert.Equal(expected, ChartMath.FormatAxisValueCompact(value));

    /// <summary>
    /// And the same negative-zero rule as the full form: a minus sign in front of a zero is a
    /// rounding residue, not a quantity. Note the boundary — <c>-0.4</c> is a real value and
    /// keeps both its sign and its digit; only what ROUNDS to zero loses the sign.
    /// </summary>
    [Theory]
    [InlineData(-0.0000001, "0")]
    [InlineData(-0.04, "0")]
    [InlineData(-0.4, "-0.4")]
    public void FormatAxisValueCompact_NeverReturnsANegativeZero(double value, string expected)
        => Assert.Equal(expected, ChartMath.FormatAxisValueCompact(value));

    // ── The x axis ───────────────────────────────────────────────────────────

    /// <summary>
    /// A 6h chart showing "06:00 06:00 06:00 06:00 06:00" tells the reader nothing: every bar sits
    /// on a multiple of 6h UTC. The format follows the visible SPAN, not the bar interval.
    /// </summary>
    [Theory]
    [InlineData(0.25, "HH:mm", true)]
    [InlineData(1.0, "HH:mm", true)]
    [InlineData(1.99, "HH:mm", true)]
    [InlineData(2.0, "MM/dd", false)]
    [InlineData(30.0, "MM/dd", false)]
    [InlineData(59.9, "MM/dd", false)]
    [InlineData(60.0, "MMM d", false)]
    [InlineData(400.0, "MMM d", false)]
    public void XAxisFormat_FollowsTheVisibleSpan(double days, string expectedFormat, bool marksMidnight)
    {
        var (format, marks) = ChartMath.XAxisFormat(TimeSpan.FromDays(days));
        Assert.Equal(expectedFormat, format);
        Assert.Equal(marksMidnight, marks);
    }
}
