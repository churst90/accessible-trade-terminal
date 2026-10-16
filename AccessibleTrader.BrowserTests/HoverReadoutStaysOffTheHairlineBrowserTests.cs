namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>The hover readout box sits on the half of the chart away from the pointer.</b>
///
/// <para>Cody's 2026-10-15 BTCUSDT 1d screenshots, normal and focus mode: the readout box was fixed
/// at the top right (<c>top:6px; right:72px</c>) and covered the newest candles and their highs,
/// which is where the pointer usually is. <c>ChartHoverTracker.PlaceReadout</c> now puts it at the
/// top of the plot on the half away from the hairline; <c>HoverReadoutPlacementTests</c> pins that
/// rule. This is the real chain: Chromium's layout of the box the Razor draws from it, measured
/// against the hairline's own element, at the size of a small window and of a large one.</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class HoverReadoutStaysOffTheHairlineBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public HoverReadoutStaysOffTheHairlineBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private sealed record Rect(double Left, double Top, double Right, double Bottom);
    private sealed record Shown(Rect Readout, Rect Hairline, Rect Chart, string Text);

    // Playwright cannot build a positional record from a JS object, so the rects come back as one
    // flat array: readout (l, t, r, b), hairline (l, t, r, b), chart (l, t, r, b).
    private const string MeasureJs = @"() => {
        const zone = document.querySelector('#chart-interact-zone');
        const box = zone && zone.querySelector('.chart-hover-readout');
        const line = zone && zone.querySelector('.chart-hover-hairline');
        if (!box || !line) return null;
        return [box, line, zone].flatMap(e => { const b = e.getBoundingClientRect(); return [b.left, b.top, b.right, b.bottom]; });
    }";

    private const string TextJs =
        "() => { const d = document.querySelector('#chart-interact-zone .chart-hover-readout'); return d ? d.innerText : ''; }";

    private static async Task<Shown?> HoverAsync(TerminalPage t, float x, float y)
    {
        await t.Page.Mouse.MoveAsync(x, y);
        for (int waited = 0; waited < 3_000; waited += 50)
        {
            var v = await t.Page.EvaluateAsync<double[]?>(MeasureJs);
            // The hairline must have arrived where the pointer is, or this is the previous sample.
            if (v is { Length: 12 } && Math.Abs((v[4] + v[6]) / 2 - x) < 40)
                return new Shown(new Rect(v[0], v[1], v[2], v[3]), new Rect(v[4], v[5], v[6], v[7]),
                    new Rect(v[8], v[9], v[10], v[11]), await t.Page.EvaluateAsync<string>(TextJs));
            await t.Page.WaitForTimeoutAsync(50);
        }
        return null;
    }

    [BrowserTheory]
    [InlineData(1000, 610)]
    [InlineData(1400, 950)]
    public async Task The_readout_box_never_sits_over_the_bar_the_pointer_is_on(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await t.Page.WaitForTimeoutAsync(600);

        var chart = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(chart);
        float y = (float)(Math.Max(chart!.Y, 0) + Math.Min(chart.Height * 0.3, 120));

        // The newest bar: the live edge keeps empty slots on the right, so walk in from the price
        // axis until the pointer lands on a bar.
        Shown? newest = null;
        for (double f = 0.94; f > 0.55 && newest == null; f -= 0.02)
            newest = await HoverAsync(t, (float)(chart.X + chart.Width * f), y);
        Assert.False(newest == null, "Hovering the right half of the chart never showed a readout.");

        Assert.True(newest!.Readout.Right < newest.Hairline.Left,
            $"{width}x{height}: on the newest bars (hairline x {newest.Hairline.Left:F0}) the readout box spans " +
            $"x {newest.Readout.Left:F0}–{newest.Readout.Right:F0} — it covers the bar the pointer is on, as it covered " +
            $"the newest candles in Cody's 2026-10-15 screenshots. Box text: '{newest.Text}'.");
        Assert.True(newest.Readout.Left >= newest.Chart.Left && newest.Readout.Top >= newest.Chart.Top,
            $"{width}x{height}: the readout box ({newest.Readout.Left:F0},{newest.Readout.Top:F0}) starts outside the chart " +
            $"({newest.Chart.Left:F0},{newest.Chart.Top:F0}).");

        // And on the oldest bars it moves to the other side, still inside the chart.
        var oldest = await HoverAsync(t, (float)(chart.X + chart.Width * 0.08), y);
        Assert.False(oldest == null, "Hovering the left of the chart showed no readout.");
        Assert.True(oldest!.Readout.Left > oldest.Hairline.Right,
            $"{width}x{height}: on the oldest bars (hairline x {oldest.Hairline.Left:F0}) the readout box spans " +
            $"x {oldest.Readout.Left:F0}–{oldest.Readout.Right:F0}, over the bar the pointer is on.");
        Assert.True(oldest.Readout.Right <= oldest.Chart.Right,
            $"{width}x{height}: the readout box ends at x {oldest.Readout.Right:F0}, past the chart's right edge {oldest.Chart.Right:F0}.");
    }
}
