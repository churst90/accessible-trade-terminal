using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>The chart must keep a usable amount of the window at every size the app can be opened at.</b>
///
/// <para>
/// Reported from a Windows VM on 2026-09-21, from a screenshot: at 946x536 the terminal showed
/// its toolbar, its symbol row, its timeframe pills, its tab bar, its indicator bar, its status
/// line and its footer — and a <b>thirty-pixel</b> strip of flat background where the chart
/// should be, with nothing drawn in it. The title bar said <c>BTCUSDT 1d on Bitstamp</c> and the
/// data was loaded; there was simply nowhere to put it. Maximising the window brought the chart
/// straight back, which is what identified the cause as layout rather than rendering.
/// </para>
///
/// <para>
/// <b>Why the existing suite could never have seen it.</b> <c>TerminalBrowserFixture</c> opens
/// every page at 1400x950 — including <c>ChartScreenshotProbe</c>, the pass that photographed
/// nine chart states specifically to find what the tests could not. Every picture this project
/// has ever taken of itself was taken at a comfortable size. <b>A harness with one viewport is
/// a harness that has never met a small window.</b>
/// </para>
///
/// <para>
/// The mechanism: <c>.app-container</c> was a <c>height: 100vh</c> flex column and
/// <c>&lt;main&gt;</c> was <c>flex: 1</c> with no floor. <c>flex: 1</c> means "take what is
/// LEFT", and the chrome above the chart GROWS as the window narrows — the icon toolbar wraps to
/// a second row, the timeframe pills to a third — so what is left is not predictable from the
/// window height. With no minimum, the flex column resolved its overflow by crushing the one
/// flexible child, which is the chart. It now has <c>--chart-min-height</c> and the container
/// scrolls instead. WCAG 1.4.10 (Reflow) calls that loss of content; more plainly, a chart
/// terminal had no chart.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ChartSurvivesASmallWindowTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ChartSurvivesASmallWindowTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>The floor declared in app.css as <c>--chart-min-height</c>.</summary>
    private const int DeclaredFloorPx = 260;

    /// <summary>
    /// The window from the report, and a couple of steps smaller. 946x536 is an ordinary
    /// restored window on a laptop; 800x480 is the sort of size a VM or a side-by-side split
    /// produces. None of these is exotic.
    /// </summary>
    public static TheoryData<int, int> SmallWindows() => new()
    {
        { 946, 536 },   // the size Cody reported
        { 1024, 600 },
        { 800, 480 },
    };

    [BrowserTheory]
    [MemberData(nameof(SmallWindows))]
    public async Task TheChartKeepsItsDeclaredFloor(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await t.Page.WaitForTimeoutAsync(400);

        var box = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(box);

        Assert.True(box!.Height >= DeclaredFloorPx - 1,
            $"At {width}x{height} the chart area is {box.Height:F0}px tall, below the "
          + $"--chart-min-height floor of {DeclaredFloorPx}px. The chrome grows as the window "
          + "narrows, so 'flex: 1' alone hands the chart whatever is left — which was 30px on "
          + "the window this test was written for, with nothing drawn in it.");
    }

    /// <summary>
    /// The floor is only honest if the overflow goes somewhere. A minimum height on a clipped
    /// container would trade an invisible chart for an unreachable footer — the same defect
    /// wearing different clothes.
    /// </summary>
    [BrowserFact]
    public async Task WhenTheFloorForcesOverflow_TheShellScrollsRatherThanClipping()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(946, 536);
        await t.LoadSeededChartAsync();
        await t.Page.WaitForTimeoutAsync(400);

        var scrollable = await t.Page.EvaluateAsync<bool>(
            @"() => {
                const el = document.querySelector('.app-container');
                if (!el) return false;
                const style = getComputedStyle(el);
                const canScroll = style.overflowY === 'auto' || style.overflowY === 'scroll';
                return canScroll && el.scrollHeight > el.clientHeight + 1;
              }");

        Assert.True(scrollable,
            "The chart's floor pushed the content past the window, but .app-container is not "
          + "scrollable — so whatever fell off the bottom (the indicator bar, the status line, "
          + "the footer) is simply unreachable.");
    }

    /// <summary>
    /// <b>And the chart must still be DRAWN, not merely allotted space.</b> The reported
    /// screenshot had a chart region of a definite size that contained nothing at all, so a test
    /// that only measured the box would have passed on the very bug it was written for.
    /// </summary>
    [BrowserFact]
    public async Task TheChartIsActuallyPaintedAtASmallSize()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(946, 536);
        await t.LoadSeededChartAsync();
        await t.Page.WaitForTimeoutAsync(600);

        // The web head paints the chart into an <img> inside the interaction zone; a blank or
        // absent src is the "region exists, nothing in it" state the screenshot showed.
        var srcLength = await t.Page.EvaluateAsync<int>(
            "() => { const i = document.querySelector('#chart-interact-zone img'); return i && i.getAttribute('src') ? i.getAttribute('src').length : -1; }");

        Assert.True(srcLength > 1000,
            $"The chart region exists but carries no painted image (src length {srcLength}). "
          + "A region of the right size with nothing in it is exactly what was reported.");
    }

    /// <summary>
    /// The rect the desktop head positions its native canvas from is a VIEWPORT rect, so it moves
    /// when the page scrolls and not only when it resizes. <c>canvasRegion.js</c> listened to
    /// <c>resize</c> and a <c>ResizeObserver</c> and nothing else, which was invisible while
    /// nothing could scroll. Introducing the floor made scrolling reachable, so the listener had
    /// to exist before the floor did — otherwise the chart pixels on Windows would stay put while
    /// the interaction zone slid out from under them.
    /// </summary>
    [BrowserFact]
    public async Task TheCanvasRegionReporterListensForScrollAndNotOnlyResize()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();

        var source = await t.Page.EvaluateAsync<string>(
            "async () => { const r = await fetch('/_content/AccessibleTrader.BlazorClient.Components/js/canvasRegion.js'); return await r.text(); }");

        Assert.Contains("addEventListener('scroll'", source, StringComparison.Ordinal);
        Assert.Contains("capture", source, StringComparison.Ordinal);
    }
}
