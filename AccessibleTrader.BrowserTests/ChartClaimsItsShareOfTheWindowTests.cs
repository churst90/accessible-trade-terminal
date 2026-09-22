using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>The chart is the product, and it must get most of the window.</b>
///
/// <para>
/// Measured on 2026-09-22 from a screenshot of a MAXIMISED browser on a 2560x1562 display at
/// 2x — 1280x781 CSS px, of which the browser's own chrome took 109 and the terminal got 672.
/// The chart carried Candles + Volume + RSI + MACD, the most conventional indicator set there
/// is, and it got <b>279 of those 672 pixels: 41.5%</b>. Our own chrome above and below it
/// came to 394 — two icon rows with captions, the symbol row, the tab bar, the indicator bar,
/// the status line and the footer. TradingView's equivalent vertical chrome is about 70.
/// </para>
///
/// <para>
/// The consequence was worse than "small": the pane weighting that gives the price pane twice
/// an indicator's share could not express itself at all, because three indicator panes at
/// their 80 CSS px floor consumed the stack and the crowded rebalance flattened every pane to
/// an equal quarter. The chart needed roughly 100 more CSS px before its own layout rules
/// applied. So this is not a taste question about padding; it is whether the renderer's
/// decisions reach the screen.
/// </para>
///
/// <para>
/// <b>Why nothing saw it.</b> <c>TerminalBrowserFixture</c> opens every page at 1400x950 —
/// including the screenshot probe that photographed nine chart states specifically to find
/// what the tests could not — and the suite never asked what fraction of the window the chart
/// had. This test asks, at the size of the real window, with the real indicator set, through
/// the real dialog. The failure message prints every band of the shell with its height, so a
/// red run says where the window went and not only that it went.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ChartClaimsItsShareOfTheWindowTests
{
    private readonly TerminalBrowserFixture _fixture;
    public ChartClaimsItsShareOfTheWindowTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The app's viewport in a maximised Brave window on a 1280x781 CSS px screen: the browser
    /// keeps 109 for its tabs and address bar. Playwright's viewport IS the app's viewport, so
    /// the height here is what the terminal actually had, not what the monitor had.
    /// </summary>
    private const int Width = 1280;
    private const int Height = 672;

    /// <summary>
    /// The maximised window the defect was measured on, and the restored window Cody launched
    /// into on 2026-09-22 (about 1000x610 CSS px, where the thirteen Bitstamp timeframe pills
    /// wrap the cascade row and the four toggles wrap the button row). The chrome is a fixed
    /// cost, so a shorter window gives the chart a smaller fraction by arithmetic: 55% is the
    /// maximised bar; the restored window measured 53.9% on the day and carries 50%, so a
    /// regression there is caught without pretending the two windows are the same case.
    /// </summary>
    public static TheoryData<int, int, double> Windows() => new()
    {
        { Width, Height, RequiredShare },
        { 1000, 610, 0.50 },
    };

    /// <summary>
    /// The bar. 41.5% is where it started; TradingView sits above 85%. 55% is the point at
    /// which, with three indicator panes, the price pane's 2:1 weight becomes visible again.
    /// </summary>
    private const double RequiredShare = 0.55;

    [BrowserTheory]
    [MemberData(nameof(Windows))]
    public async Task WithVolumeRsiAndMacd_TheChartGetsMostOfTheWindow(int width, int height, double requiredShare)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();

        // The presentation's set, through the real Add Indicator dialog. The seeded chart may
        // already carry Volume; add it only when it does not, so the pane count is three either way.
        var series = await t.ActiveSeriesNamesAsync();
        if (!series.Any(n => n.Contains("Volume", StringComparison.OrdinalIgnoreCase)))
            await t.AddIndicatorAsync("Volume");
        await t.AddIndicatorAsync("RSI");
        await t.AddIndicatorAsync("MACD");
        await t.Page.WaitForTimeoutAsync(400);

        series = await t.ActiveSeriesNamesAsync();
        foreach (var want in new[] { "Volume", "RSI", "MACD" })
            Assert.True(series.Any(n => n.Contains(want, StringComparison.OrdinalIgnoreCase)),
                $"The chart never gained {want}; the picker lists: {string.Join(" / ", series)}");

        var box = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(box);

        double share = box!.Height / height;
        var bands = await t.ShellBandsAsync();

        // A picture beside the number, for whoever changes the chrome next: the band table
        // says how tall each strip is, the photograph says whether anything in it wrapped,
        // clipped or collided. Same directory the screenshot probe uses.
        var shotDir = Environment.GetEnvironmentVariable("AT_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(shotDir))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx"))) dir = dir.Parent;
            shotDir = Path.Combine(dir?.FullName ?? Directory.GetCurrentDirectory(), "scratchpad", "screenshots");
        }
        Directory.CreateDirectory(shotDir);
        await t.Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(shotDir, $"10_window_{width}x{height}_volume_rsi_macd.png"),
            FullPage = false,
        });

        Assert.True(share >= requiredShare,
            $"At {width}x{height} with Volume + RSI + MACD the chart gets {box.Height:F0}px of "
          + $"{height}: {share:P1}, below the {requiredShare:P0} bar. The shell's bands, top to "
          + "bottom:\n    " + string.Join("\n    ", bands)
          + "\n  Every pixel in a band that is not the chart is a pixel the chart does not get.");
    }

    /// <summary>
    /// <b>Focus mode is the feature F11 was impersonating.</b> Alt+Z hides the toolbar, the tab
    /// bar, the indicator bar and the footer; the chart takes their height and the status line
    /// stays with a visible way back. This asserts all four things a user meets: the share, the
    /// hidden bands leaving the accessibility tree, the announcement, and the round trip.
    /// </summary>
    [BrowserFact]
    public async Task FocusMode_GivesTheChartNearlyTheWholeWindow_AndComesBack()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(Width, Height);
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();

        var before = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(before);

        await t.ClearSpokenAsync();
        await t.PressAsync("Alt+z");
        await t.Page.WaitForFunctionAsync(
            "() => document.querySelector('.app-container')?.classList.contains('focus-mode') === true",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        await t.Page.WaitForTimeoutAsync(400);

        var after = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(after);
        double share = after!.Height / Height;
        Assert.True(share >= 0.85,
            $"In focus mode the chart gets {after.Height:F0}px of {Height}: {share:P1}, below 85%. Bands:\n    "
          + string.Join("\n    ", await t.ShellBandsAsync()));

        // Hidden means gone from the accessibility tree, not merely transparent.
        var toolbarVisible = await t.Page.Locator("nav[aria-label='Main toolbar']").IsVisibleAsync();
        var indicatorBarVisible = await t.Page.Locator("nav[aria-label='Indicator controls']").IsVisibleAsync();
        Assert.False(toolbarVisible, "the main toolbar is still visible in focus mode");
        Assert.False(indicatorBarVisible, "the indicator bar is still visible in focus mode");

        // The way back is visible, and the chord was spoken.
        Assert.True(await t.Page.Locator("button:has-text('Exit focus mode')").IsVisibleAsync(),
            "no visible way out of focus mode");
        var spoken = await t.SpokenAsync();
        Assert.Contains(spoken, u => u.Text.Contains("Focus mode on", StringComparison.OrdinalIgnoreCase)
                                  && u.Text.Contains("Alt plus Z", StringComparison.OrdinalIgnoreCase));

        // And back, from the chart, which is where focus was put.
        await t.PressAsync("Alt+z");
        await t.Page.WaitForFunctionAsync(
            "() => document.querySelector('.app-container')?.classList.contains('focus-mode') === false",
            null, new PageWaitForFunctionOptions { Timeout = 5_000 });
        Assert.True(await t.Page.Locator("nav[aria-label='Main toolbar']").IsVisibleAsync(),
            "the toolbar did not come back");
        var restored = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(restored);
        Assert.InRange(restored!.Height, before!.Height - 2, before.Height + 2);
    }

    /// <summary>
    /// The share only means something if the chart is DRAWN into it: an empty region of the
    /// right size is what the small-window bug looked like, and a layout change that grew the
    /// box while starving the renderer would pass the test above.
    /// </summary>
    [BrowserFact]
    public async Task TheReclaimedSpaceIsActuallyPainted()
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(Width, Height);
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();

        var box = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(box);

        // The image the server paints into the zone must fill the zone, not sit in a corner
        // of it. Compare the <img>'s rendered box to the zone's.
        var imgBox = await t.Page.Locator("#chart-interact-zone img").First.BoundingBoxAsync();
        Assert.NotNull(imgBox);
        Assert.True(imgBox!.Height >= box!.Height * 0.9,
            $"The chart image is {imgBox.Height:F0}px tall inside a {box.Height:F0}px zone — "
          + "the zone grew but the picture did not follow it.");

        var srcLength = await t.ImageSrcLengthAsync();
        Assert.True(srcLength > 1000, $"The chart region carries no painted image (src length {srcLength}).");
    }
}
