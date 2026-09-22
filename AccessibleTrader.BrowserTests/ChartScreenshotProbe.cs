using System.Text.Json;
using Microsoft.Playwright;

namespace AccessibleTrader.BrowserTests;

/// <summary>
/// The rendering layer, photographed. Loads the seeded chart, walks it through the states the
/// A2i campaign mutated — linear and log scale, zoomed in, Heikin Ashi, one oscillator pane,
/// then eight — and writes a PNG of the real browser at each one to
/// <c>scratchpad/screenshots/</c> (override with <c>AT_SCREENSHOT_DIR</c>).
///
/// <para>
/// This exists because the suite became a good reviewer of the renderer on 2026-09-17 and is
/// still not a pair of eyes. A blind author cannot look at the chart; the screenshots are what
/// a sighted reviewer looks at instead, and the JSON beside them records what the page said
/// about each state (image size, series on the chart, what was spoken) so the pictures can be
/// read against the app's own account of itself.
/// </para>
///
/// <para>
/// It is also the first browser test to drive <c>AddIndicatorModal</c> end to end. Every
/// other route reaches it cold, where the indicator bar has no series to add to, and
/// <see cref="ModalRoutes"/> records it as unswept for that reason. So it asserts the things it
/// can: each add puts a new entry in the focused-indicator picker, and every state change
/// repaints the chart image. Pictures are the by-product; those assertions are the test.
/// </para>
/// </summary>
[Collection("Terminal browser")]
public sealed class ChartScreenshotProbe
{
    private readonly TerminalBrowserFixture _fixture;
    public ChartScreenshotProbe(TerminalBrowserFixture fixture) => _fixture = fixture;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AccessibleTrader.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }

    private static string OutputDir()
    {
        var dir = Environment.GetEnvironmentVariable("AT_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            dir = Path.Combine(RepoRoot(), "scratchpad", "screenshots");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// The indicators added, in order, matched against the visible name in the dialog's list.
    /// The first four are the presentation's set — an oscillator with declared bounds (RSI), a
    /// zero-cross histogram (MACD), an overlay (Bollinger) and the phase overlay that had no
    /// fixture before A2i (Cipher S). The rest push the pane count to the eight the axis-label
    /// spacing guard needs before it has anything to say.
    /// </summary>
    private static readonly string[] IndicatorsToAdd =
    {
        "RSI", "MACD", "Bollinger", "Cipher S",
        "ADX", "Stochastic", "CCI", "MFI", "ATR",
    };

    [BrowserFact]
    public async Task Photograph_the_chart_in_every_state_the_renderer_campaign_mutated()
    {
        var outDir = OutputDir();
        var report = new List<Dictionary<string, object?>>();

        await using var t = await _fixture.NewPageAsync();
        await t.LoadSeededChartAsync();
        await t.FocusChartAsync();
        await t.WaitForPaintAsync();

        await Snap(t, outDir, report, "01_candles_linear");

        // Log scale and back. Alt+L is the ToggleLogScale chord in ShortcutManager.
        await Toggle(t, "Alt+l");
        await Snap(t, outDir, report, "02_candles_log");
        await Toggle(t, "Alt+l");

        // Zoom in six steps, photograph, zoom back out.
        for (int i = 0; i < 6; i++) await Toggle(t, "=");
        await Snap(t, outDir, report, "03_zoomed_in");
        for (int i = 0; i < 6; i++) await Toggle(t, "-");

        // Heikin Ashi and back. Alt+C.
        await Toggle(t, "Alt+c");
        await Snap(t, outDir, report, "04_heikin_ashi");
        await Toggle(t, "Alt+c");

        // Indicators through the real dialog.
        var added = new List<string>();
        foreach (var name in IndicatorsToAdd)
        {
            var picked = await t.AddIndicatorAsync(name);
            added.Add(picked);
            if (added.Count == 1) await Snap(t, outDir, report, "05_rsi_one_oscillator_pane");
            if (added.Count == 4) await Snap(t, outDir, report, "06_presentation_set_four_indicators");
        }
        await Snap(t, outDir, report, "07_eight_panes");

        await Toggle(t, "Alt+l");
        await Snap(t, outDir, report, "08_eight_panes_log");
        await Toggle(t, "Alt+l");

        for (int i = 0; i < 6; i++) await Toggle(t, "=");
        await Snap(t, outDir, report, "09_eight_panes_zoomed");

        var series = await t.ActiveSeriesNamesAsync();
        Assert.True(series.Count >= IndicatorsToAdd.Length + 1,
            $"Expected at least {IndicatorsToAdd.Length + 1} series after adding "
            + $"{IndicatorsToAdd.Length} indicators; the picker lists {series.Count}: "
            + string.Join(" / ", series));

        File.WriteAllText(Path.Combine(outDir, "report.json"),
            JsonSerializer.Serialize(new { added, series, states = report },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Press a chord on the chart and wait for the chart image to repaint.</summary>
    private static async Task Toggle(TerminalPage t, string chord)
    {
        var before = await t.ImageSrcLengthAsync();
        await t.PressAsync(chord);
        await t.WaitForPaintAsync(before);
    }

    private static async Task Snap(TerminalPage t, string outDir, List<Dictionary<string, object?>> report, string name)
    {
        await t.ClearSpokenAsync();
        var path = Path.Combine(outDir, name + ".png");
        await t.Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false });

        // The chart bitmap itself, at the size the server rendered it, without the chrome.
        var img = t.Page.Locator("#chart-interact-zone img");
        var imgPath = Path.Combine(outDir, name + "_chart.png");
        var src = await img.GetAttributeAsync("src") ?? "";
        if (src.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
            File.WriteAllBytes(imgPath, Convert.FromBase64String(src["data:image/png;base64,".Length..]));

        var factsJson = await t.Page.EvaluateAsync<string>(@"() => JSON.stringify((() => {
            const i = document.querySelector('#chart-interact-zone img');
            const z = document.querySelector('#chart-interact-zone');
            const r = z ? z.getBoundingClientRect() : null;
            return {
                srcLength: i ? i.getAttribute('src').length : -1,
                naturalWidth: i ? i.naturalWidth : -1,
                naturalHeight: i ? i.naturalHeight : -1,
                zoneWidth: r ? Math.round(r.width) : -1,
                zoneHeight: r ? Math.round(r.height) : -1,
                dpr: window.devicePixelRatio,
                title: document.title,
            };
        })())");
        var facts = JsonSerializer.Deserialize<Dictionary<string, object?>>(factsJson) ?? new();
        facts["state"] = name;
        facts["series"] = await t.ActiveSeriesNamesAsync();
        report.Add(facts);
    }
}
