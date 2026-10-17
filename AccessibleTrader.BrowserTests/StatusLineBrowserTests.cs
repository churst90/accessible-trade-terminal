using System.Text.Json;
using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// <b>The status lines and the crosshair's axis labels, in real Chromium</b> (Cody's 2026-10-17
/// decision: TradingView status line).
///
/// <para>Replaces <c>HoverReadoutStaysOffTheHairlineBrowserTests</c>, retired with the floating
/// readout box it measured. The unit tests pin the model (ChartStatusLineTests) and the Razor
/// wiring (StatusLineInChartAreaTests); this is the real chain — Chromium's layout of the DOM text
/// ChartArea draws over the server-rendered image, at the size of a small window and a large one,
/// with Cipher B, RSI, MACD, Bollinger and volume on the chart and then eight panes. It also writes
/// the screenshots a sighted reviewer looks at (to <c>AT_SCREENSHOT_DIR</c> when set).</para>
/// </summary>
[Collection("Terminal browser")]
public sealed class StatusLineBrowserTests
{
    private readonly TerminalBrowserFixture _fixture;
    public StatusLineBrowserTests(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string OutputDir()
    {
        var dir = Environment.GetEnvironmentVariable("AT_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir))
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "AccessibleTrader.slnx"))) root = root.Parent;
            dir = Path.Combine(root?.FullName ?? Directory.GetCurrentDirectory(), "scratchpad", "screenshots");
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Every pane's status block against its pane's band (from the drag handles, which sit on the
    // renderer's dividers) and the chart's right edge; plus whether each line is inside an
    // aria-hidden ancestor. One JSON string back, because Playwright cannot build records.
    private const string MeasureJs = @"() => {
        const zone = document.querySelector('#chart-interact-zone');
        if (!zone) return null;
        const z = zone.getBoundingClientRect();
        const cuts = [...zone.querySelectorAll('.pane-divider')]
            .map(d => { const r = d.getBoundingClientRect(); return (r.top + r.bottom) / 2; })
            .sort((a, b) => a - b);
        const panes = [...zone.querySelectorAll('.chart-status-pane')].map(p => {
            const r = p.getBoundingClientRect();
            const lines = [...p.querySelectorAll('.chart-status-line')];
            const right = Math.max(...lines.map(l => l.getBoundingClientRect().right));
            const nextCut = cuts.find(c => c > r.top + 2);
            return { pane: p.dataset.pane, top: r.top, bottom: r.bottom, right, lineHeights: lines.map(l => Math.round(l.getBoundingClientRect().height)).join(','),
                     limit: nextCut ?? z.bottom, lines: lines.map(l => l.innerText.replace(/\u00a0/g, ' ')),
                     hidden: lines.every(l => l.closest('[aria-hidden=""true""]') !== null) };
        });
        return JSON.stringify({ zone: { left: z.left, top: z.top, right: z.right, bottom: z.bottom }, panes });
    }";

    private sealed record Box(double Left, double Top, double Right, double Bottom);
    private sealed record PaneBlock(string Pane, double Top, double Bottom, double Right, double Limit, string[] Lines, bool Hidden, string LineHeights);
    private sealed record Measure(Box Zone, PaneBlock[] Panes);

    private static async Task<Measure> MeasureAsync(TerminalPage t)
    {
        var json = await t.Page.EvaluateAsync<string?>(MeasureJs);
        Assert.False(json == null, "the chart zone is not on the page");
        return JsonSerializer.Deserialize<Measure>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// The WebHost's first-visit "How should the terminal speak?" prompt sits above the app and
    /// pushes the chart down the page; for a picture of the chart it is put off ("Ask me next
    /// visit"), which changes no setting.
    /// </summary>
    private static async Task PutOffSpeechPromptAsync(TerminalPage t)
    {
        var ask = t.Page.GetByRole(AriaRole.Button, new() { Name = "Ask me next visit" });
        if (await ask.CountAsync() > 0 && await ask.First.IsVisibleAsync())
        {
            await ask.First.ClickAsync();
            await t.Page.WaitForTimeoutAsync(300);
        }
    }

    private static void AssertBlocksFit(Measure m, string when, bool mayBeEmpty = false)
    {
        if (!mayBeEmpty) Assert.NotEmpty(m.Panes);
        foreach (var p in m.Panes)
        {
            Assert.True(p.Hidden, $"{when}: a status line in pane {p.Pane} is not inside an aria-hidden ancestor — " +
                "it would be read as page text, which Cody's condition rules out (it is a visual mirror).");
            Assert.True(p.Bottom <= p.Limit + 1,
                $"{when}: pane {p.Pane}'s status block (from y {p.Top:F0}, line heights {p.LineHeights}) runs to y {p.Bottom:F0}, past its pane's bottom {p.Limit:F0} (chart {m.Zone.Top:F0}–{m.Zone.Bottom:F0}): " +
                string.Join(" / ", p.Lines));
            // The price axis is about 60 px; a line must stay in the plot.
            Assert.True(p.Right <= m.Zone.Right - 50,
                $"{when}: pane {p.Pane}'s status line ends at x {p.Right:F0}, over the price axis (chart right {m.Zone.Right:F0}): " +
                string.Join(" / ", p.Lines));
        }
    }

    [BrowserTheory]
    [InlineData(1000, 610)]
    [InlineData(1400, 950)]
    public async Task The_status_lines_sit_inside_their_panes_with_the_presentation_set_and_at_eight_panes(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await PutOffSpeechPromptAsync(t);
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();
        var outDir = OutputDir();

        foreach (var name in new[] { "Cipher B", "RSI", "MACD", "Bollinger" })
            await t.AddIndicatorAsync(name);
        await t.Page.WaitForTimeoutAsync(600);

        var set = await MeasureAsync(t);
        var main = set.Panes.FirstOrDefault(p => p.Pane == "Main");
        Assert.True(main != null, "the price pane has no status block");
        Assert.Contains(main!.Lines, l => l.Contains(" O "));
        Assert.True(set.Panes.Length >= 4,
            $"{width}x{height}: expected status blocks in the price, volume and three oscillator panes; found " +
            string.Join(", ", set.Panes.Select(p => p.Pane)));
        AssertBlocksFit(set, $"{width}x{height} presentation set");
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(outDir, $"statusline_{width}x{height}_set.png") });

        foreach (var name in new[] { "ADX", "Stochastic", "CCI", "MFI" })
            await t.AddIndicatorAsync(name);
        await t.Page.WaitForTimeoutAsync(600);
        var eight = await MeasureAsync(t);
        AssertBlocksFit(eight, $"{width}x{height} eight panes");
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(outDir, $"statusline_{width}x{height}_eight.png") });

        // DOM text grows with Settings > Appearance > Text size and the canvas does not, so the
        // line budget is counted in it. At 175% a status line is about 30 px: an eight-pane
        // pane holds none and the price pane one or two, and every block must still fit. Text
        // size is a live preview; the dialog is measured through (it covers the chart, the DOM
        // geometry is unaffected) and then cancelled, which reverts it.
        await t.PressAsync("F12");
        Assert.True(await t.WaitForDialogAsync(), "Settings did not open.");
        Assert.True(await t.ClickTopDialogTabAsync("Appearance"), "Settings has no Appearance tab.");
        await t.Page.SelectOptionAsync("#s-ui-scale", "175");
        await t.Page.WaitForTimeoutAsync(900);
        var big = await MeasureAsync(t);
        // Since 2026-10-17 each block sits in a band above its pane's plot, and a pane shows a line
        // only where the band leaves the plot 40% of the pane (ChartStatusLine.MinPlotFraction).
        // At 1000x610 the price pane of an eight-pane chart is about 60 px: one 175% line's band
        // (38 px) would leave it a 22 px plot, so it shows none — and neither does any other pane.
        // At 1400x950 it is about 128 px and keeps two.
        bool small = width < 1400;
        if (!small) Assert.Contains(big.Panes, p => p.Pane == "Main");
        AssertBlocksFit(big, $"{width}x{height} eight panes at 175% text", mayBeEmpty: small);
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(outDir, $"statusline_{width}x{height}_eight_text175.png") });
        await t.Page.SelectOptionAsync("#s-ui-scale", "100");
        await t.PressAsync("Escape");
        await t.WaitForNoDialogAsync();
    }

    // Each pane's status block against the chart image under it: the server-rendered PNG is read
    // back through a canvas (a data: URL, so same-origin and readable) and, inside the block's
    // rect (mapped from CSS px to image px), the pixels in a DATA colour are counted — saturated
    // ones, max-min channel > 90: candles, volume bars, Cipher B's waves and RSI, the RSI and MACD
    // lines. The background, gridlines, pane borders and the grey crosshair are not saturated.
    // The same count over the rest of the pane (below the block) is the control: data is drawn.
    private const string BlockOverDataJs = @"async () => {
        const zone = document.querySelector('#chart-interact-zone');
        const img = zone && zone.querySelector('img');
        if (!img || !img.naturalWidth) return null;
        try { await img.decode(); } catch (e) { }
        const c = document.createElement('canvas');
        c.width = img.naturalWidth; c.height = img.naturalHeight;
        const g = c.getContext('2d');
        g.drawImage(img, 0, 0);
        const ir = img.getBoundingClientRect();
        const sx = img.naturalWidth / ir.width, sy = img.naturalHeight / ir.height;
        const cuts = [...zone.querySelectorAll('.pane-divider')]
            .map(d => { const r = d.getBoundingClientRect(); return (r.top + r.bottom) / 2; }).sort((a, b) => a - b);
        const plotRight = ir.left + ir.width * 0.9;
        const count = (l, t, r, b) => {
            const x0 = Math.max(0, Math.floor((l - ir.left) * sx)), y0 = Math.max(0, Math.floor((t - ir.top) * sy));
            const x1 = Math.min(c.width, Math.ceil((r - ir.left) * sx)), y1 = Math.min(c.height, Math.ceil((b - ir.top) * sy));
            if (x1 <= x0 || y1 <= y0) return 0;
            const d = g.getImageData(x0, y0, x1 - x0, y1 - y0).data;
            let n = 0;
            for (let i = 0; i < d.length; i += 4) {
                const mx = Math.max(d[i], d[i + 1], d[i + 2]), mn = Math.min(d[i], d[i + 1], d[i + 2]);
                if (mx - mn > 90) n++;
            }
            return n;
        };
        const panes = [...zone.querySelectorAll('.chart-status-pane')].map(p => {
            const rs = [...p.querySelectorAll('.chart-status-line')].map(l => l.getBoundingClientRect());
            const left = Math.min(...rs.map(r => r.left)), top = Math.min(...rs.map(r => r.top));
            const right = Math.max(...rs.map(r => r.right)), bottom = Math.max(...rs.map(r => r.bottom));
            const paneBottom = cuts.find(y => y > top + 2) ?? (ir.bottom - 30);
            return { pane: p.dataset.pane, top, bottom, under: count(left, top, right, bottom),
                     below: count(ir.left + 2, bottom + 4, plotRight, paneBottom - 2) };
        });
        return JSON.stringify(panes);
    }";

    private sealed record BlockOverData(string Pane, double Top, double Bottom, int Under, int Below);

    /// <summary>
    /// <b>Each pane's plot starts below its status lines</b> (Cody, 2026-10-17: Cipher B's RSI and
    /// the waves' peaks disappeared behind the block). In real Chromium, with Cipher B, RSI and MACD
    /// on the chart: under every pane's status block the server-rendered image holds no data
    /// colour, and below it, in the same pane, it does. At 100% and at 175% text, where the band
    /// is taller because the DOM text is.
    /// </summary>
    [BrowserTheory]
    [InlineData(1000, 610)]
    [InlineData(1400, 950)]
    public async Task No_data_is_drawn_under_a_panes_status_block(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await PutOffSpeechPromptAsync(t);
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();
        foreach (var name in new[] { "Cipher B", "RSI", "MACD" })
            await t.AddIndicatorAsync(name);
        await t.Page.WaitForTimeoutAsync(900);

        async Task Check(string when, int minPanes)
        {
            var json = await t.Page.EvaluateAsync<string?>(BlockOverDataJs);
            Assert.False(json == null, $"{when}: the chart image is not on the page");
            var panes = JsonSerializer.Deserialize<BlockOverData[]>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            Assert.True(panes.Length >= minPanes, $"{when}: expected status blocks in at least {minPanes} panes; found {string.Join(", ", panes.Select(p => p.Pane))}");
            foreach (var p in panes)
            {
                Assert.True(p.Under == 0,
                    $"{when}: {p.Under} data-coloured pixels of the chart image are under pane {p.Pane}'s status block (y {p.Top:F0}–{p.Bottom:F0}), where its DOM text hides them");
                Assert.True(p.Below > 20, $"{when}: pane {p.Pane} has no data drawn below its status block ({p.Below} pixels) — the check proves nothing there");
            }
        }

        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDir(), $"statusband_{width}x{height}.png") });
        // The price, volume and three oscillator panes.
        await Check($"{width}x{height}", 5);

        await t.PressAsync("F12");
        Assert.True(await t.WaitForDialogAsync(), "Settings did not open.");
        Assert.True(await t.ClickTopDialogTabAsync("Appearance"), "Settings has no Appearance tab.");
        await t.Page.SelectOptionAsync("#s-ui-scale", "175");
        await t.Page.WaitForTimeoutAsync(1200);
        // Measured through the dialog, as the test above does: it covers the chart on screen, but
        // the DOM geometry and the chart image read here are unaffected. Then cancelled, which
        // reverts the live preview.
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDir(), $"statusband_{width}x{height}_text175.png") });
        // At 1000x610 the oscillator panes (~50 px) cannot keep a 40% plot under a 175% line
        // (38 px) and show none; the price pane keeps its block. At 1400x950 every pane does.
        await Check($"{width}x{height} at 175% text", width < 1400 ? 1 : 5);
        await t.Page.SelectOptionAsync("#s-ui-scale", "100");
        await t.PressAsync("Escape");
        await t.WaitForNoDialogAsync();
    }

    private const string HoverJs = @"() => {
        const zone = document.querySelector('#chart-interact-zone');
        const line = zone && zone.querySelector('.chart-hover-hairline');
        const date = zone && zone.querySelector('.chart-hover-date');
        const price = zone && zone.querySelector('.chart-hover-price');
        if (!line || !date) return null;
        const r = e => { const b = e.getBoundingClientRect(); return [b.left, b.top, b.right, b.bottom]; };
        return JSON.stringify({ line: r(line), date: r(date), price: price ? r(price) : null, zone: r(zone),
            box: !!zone.querySelector('.chart-hover-readout'), dateText: date.innerText,
            symbol: (zone.querySelector("".chart-status-line[data-kind='Symbol']"") || {}).innerText || '' });
    }";

    private sealed record Hover(double[] Line, double[] Date, double[]? Price, double[] Zone, bool Box, string DateText, string Symbol);

    /// <summary>
    /// Hovering: no floating box; the date is a label in the time-axis strip under the hairline and
    /// the pointer's value a label beside the price axis at the pointer; the symbol line reads the
    /// hovered bar.
    /// </summary>
    [BrowserTheory]
    [InlineData(1000, 610)]
    [InlineData(1400, 950)]
    public async Task Hovering_puts_the_date_on_the_time_axis_and_the_price_on_the_price_axis(int width, int height)
    {
        await using var t = await _fixture.NewPageAsync();
        await t.Page.SetViewportSizeAsync(width, height);
        await t.LoadSeededChartAsync();
        await PutOffSpeechPromptAsync(t);
        await t.Page.WaitForTimeoutAsync(600);

        var chart = await t.Page.Locator("#chart-interact-zone").BoundingBoxAsync();
        Assert.NotNull(chart);
        float x = (float)(chart!.X + chart.Width * 0.5), y = (float)(Math.Max(chart.Y, 0) + Math.Min(chart.Height * 0.3, 120));
        await t.Page.Mouse.MoveAsync(x, y);

        Hover? h = null;
        for (int waited = 0; waited < 3_000 && h == null; waited += 50)
        {
            var json = await t.Page.EvaluateAsync<string?>(HoverJs);
            if (json != null)
            {
                var c = JsonSerializer.Deserialize<Hover>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                if (Math.Abs((c.Line[0] + c.Line[2]) / 2 - x) < 40) h = c;
            }
            if (h == null) await t.Page.WaitForTimeoutAsync(50);
        }
        Assert.False(h == null, "hovering the chart showed no hairline and date label");
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(OutputDir(), $"statusline_{width}x{height}_hover.png") });

        Assert.False(h!.Box, "the floating hover readout box is still drawn");
        double hairline = (h.Line[0] + h.Line[2]) / 2, dateMid = (h.Date[0] + h.Date[2]) / 2;
        Assert.True(Math.Abs(dateMid - hairline) < 4,
            $"{width}x{height}: the date label is centred at x {dateMid:F0}, not on the hairline at {hairline:F0}");
        Assert.True(h.Date[1] >= h.Zone[3] - 40 && h.Date[3] <= h.Zone[3] + 1,
            $"{width}x{height}: the date label spans y {h.Date[1]:F0}–{h.Date[3]:F0}, not in the time-axis strip at the chart's bottom ({h.Zone[3]:F0})");
        Assert.NotNull(h.Price);
        Assert.True(h.Price![0] >= h.Zone[2] - 70 && Math.Abs((h.Price[1] + h.Price[3]) / 2 - y) < 3,
            $"{width}x{height}: the price label is at x {h.Price[0]:F0}, y {(h.Price[1] + h.Price[3]) / 2:F0}; wanted it in the price axis at the pointer's y {y:F0}");
        Assert.Contains(" O ", " " + h.Symbol.Replace(' ', ' '));
    }
}
