namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>The hover readout follows the chart when the page scrolls under a still mouse.</b>
///
/// <para>Cody's decision, 2026-10-15: a scroll moves the chart, not the pointer, so the readout
/// re-maps the pointer's unchanged spot on screen onto the chart's new position instead of
/// clearing until the next mouse move. The unit tests drive <c>ChartHoverTracker</c> with
/// hand-written rects; this is the one place the real chain runs: Chromium's layout, the
/// mouse handler's move, <c>canvasRegion.js</c>'s scroll report, the circuit, the tracker and the
/// readout's DOM text.</para>
///
/// <para>Chromium may send a synthetic mousemove after a scroll. That would make this test pass
/// whatever the tracker did, so every mousemove is stopped at the window, in the capture phase,
/// from the moment the pointer is parked until the scroll's readout has been read.</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ChartHoverFollowsAScrollBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ChartHoverFollowsAScrollBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string ReadoutJs =
        "() => { const d = document.querySelector('#chart-interact-zone .chart-hover-readout'); return d ? d.innerText : null; }";

    private static async Task<string?> WaitForReadoutAsync(TerminalPage t, Func<string?, bool> done, int timeoutMs = 4_000)
    {
        string? text = null;
        for (int waited = 0; waited < timeoutMs; waited += 50)
        {
            text = await t.Page.EvaluateAsync<string?>(ReadoutJs);
            if (done(text)) return text;
            await t.Page.WaitForTimeoutAsync(50);
        }
        return text;
    }

    [BrowserFact]
    public async Task Scrolling_the_page_under_a_still_mouse_re_maps_the_readout()
    {
        await using var t = await _fixture.NewPageAsync();
        // Shorter than the chrome plus the chart's floor, so .app-container scrolls
        // (see ChartSurvivesASmallWindowTests).
        await t.Page.SetViewportSizeAsync(946, 400);
        await t.LoadSeededChartAsync();
        await t.Page.WaitForTimeoutAsync(600);

        const int scrollBy = 40;
        double room = await t.Page.EvaluateAsync<double>(
            "() => { const c = document.querySelector('.app-container'); return c ? c.scrollHeight - c.clientHeight - c.scrollTop : -1; }");
        Assert.True(room >= scrollBy, $"The page cannot scroll {scrollBy}px (room {room}); this test needs it to.");

        var box = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(box);
        float px = box!.X + box.Width * 0.4f, py = Math.Max(box.Y, 0) + 30;
        Assert.True(py < 400 - scrollBy, $"The chart starts too low to park the mouse on it (box y {box.Y}).");

        await t.Page.Mouse.MoveAsync(px, py);
        string? before = await WaitForReadoutAsync(t, s => s != null);
        Assert.False(before == null, "Hovering the chart showed no readout at all.");

        await t.Page.EvaluateAsync(
            "() => { window.__blockMoves = true; window.__blocked = 0; window.addEventListener('mousemove', e => { if (window.__blockMoves) { window.__blocked++; e.stopImmediatePropagation(); } }, true); }");
        await t.Page.EvaluateAsync($"() => {{ document.querySelector('.app-container').scrollTop += {scrollBy}; }}");

        var moved = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.True(Math.Abs(moved!.Y - (box.Y - scrollBy)) < 1.5,
            $"The scroll did not move the chart up {scrollBy}px (y {box.Y} -> {moved.Y}); the test is not exercising a scroll.");

        string? still = await WaitForReadoutAsync(t, s => s != before);

        // The oracle: a real move onto the same spot, now that the chart has moved.
        int blocked = await t.Page.EvaluateAsync<int>("() => { window.__blockMoves = false; return window.__blocked; }");
        await t.Page.Mouse.MoveAsync(px + 3, py);
        await t.Page.WaitForTimeoutAsync(150);
        await t.Page.Mouse.MoveAsync(px, py);
        await t.Page.WaitForTimeoutAsync(400);
        string? fresh = await t.Page.EvaluateAsync<string?>(ReadoutJs);

        Assert.False(fresh == null, "After the scroll a real mouse move on the chart showed no readout.");
        Assert.True(before != fresh,
            $"Vacuity: the scroll did not change what this spot reads ('{before}'), so nothing is proved.");
        Assert.True(still == fresh,
            $"After the page scrolled {scrollBy}px under a still mouse the readout said '{still ?? "(nothing)"}', " +
            $"but a mouse move at the same spot reads '{fresh}'. Before the scroll it said '{before}'. " +
            $"({blocked} synthetic mousemove(s) from the browser were held back during the scroll.)");
    }
}
