using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>A dragged pane divider follows the pointer, in real Chromium</b> (Cody, 2026-10-17).
/// ChartArea converted the pointer's travel with a hard-coded 600 px estimate of the chart's
/// height; at 1000x610 the chart is about 330 px tall, so the divider moved about half as far as
/// the pointer. Here a divider is dragged 40 px up at a small window and a large one, and the
/// handle — placed from the renderer's own dividers after the next frame — must have moved 40 px.
/// </summary>
[Collection("Terminal browser")]
public sealed class PaneDividerDragBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public PaneDividerDragBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private const string LastDividerJs = @"() => {
        const ds = [...document.querySelectorAll('#chart-interact-zone .pane-divider')];
        if (ds.length === 0) return null;
        const r = ds[ds.length - 1].getBoundingClientRect();
        return [r.left + r.width / 2, (r.top + r.bottom) / 2];
    }";

    [BrowserTheory]
    [InlineData(1000, 610)]
    [InlineData(1400, 950)]
    public async Task A_dragged_divider_moves_as_far_as_the_pointer(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        var ask = t.Page.GetByRole(AriaRole.Button, new() { Name = "Ask me next visit" });
        if (await ask.CountAsync() > 0 && await ask.First.IsVisibleAsync()) await ask.First.ClickAsync();
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();
        await t.AddIndicatorAsync("RSI");
        await t.Page.WaitForTimeoutAsync(900);

        var start = await t.Page.EvaluateAsync<double[]?>(LastDividerJs);
        Assert.False(start == null, "no pane divider on the chart");
        float x = (float)start![0], y = (float)start[1];

        // Up 40 px in steps, as a hand does: the pane below grows, the price pane gives way.
        await t.Page.Mouse.MoveAsync(x, y);
        await t.Page.Mouse.DownAsync();
        for (int i = 1; i <= 8; i++)
        {
            await t.Page.Mouse.MoveAsync(x, y - 5 * i);
            await t.Page.WaitForTimeoutAsync(40);
        }
        await t.Page.Mouse.UpAsync();

        // The handle is placed from the renderer's dividers, so it moves once a frame lands.
        double[]? end = null;
        for (int waited = 0; waited < 3_000; waited += 100)
        {
            await t.Page.WaitForTimeoutAsync(100);
            end = await t.Page.EvaluateAsync<double[]?>(LastDividerJs);
            if (end != null && Math.Abs((y - end[1]) - 40) < 2) break;
        }
        Assert.NotNull(end);
        double moved = y - end![1];
        Assert.True(Math.Abs(moved - 40) < 2,
            $"{width}x{height}: the pointer moved the divider 40 px up; the divider moved {moved:F1} px");
    }
}
