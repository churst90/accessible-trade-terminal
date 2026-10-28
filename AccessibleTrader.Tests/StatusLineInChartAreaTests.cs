using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.BlazorClient.Services;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Rendering;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.WebHost;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// The status lines as ChartArea draws them: real DOM text over the chart image (Cody's
/// 2026-10-17 decision: TradingView status line). The model is pinned in ChartStatusLineTests;
/// these pin the wiring — that the DOM exists, is hidden from the accessibility tree, reads the
/// right bar, and that the floating hover readout box it replaces is gone with its fields carried
/// by the status line and the two axis labels.
/// </summary>
public sealed class StatusLineInChartAreaTests
{
    private const double W = 1280, H = 720;

    private static List<Ohlcv> Bars()
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 100; i++)
            bars.Add(new Ohlcv(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
                80_000 + i * 10, 80_030 + i * 10, 79_970 + i * 10, 80_005 + i * 10, 1000));
        return bars;
    }

    private sealed class Rig : IDisposable
    {
        public WorkspaceState State;
        public readonly Blazor.BlazorTestHarness Harness;
        public readonly IRenderedComponent<ChartArea> Cut;
        public readonly BlazorInputService Input;
        public readonly ChartHoverTracker Tracker;

        public Rig(Func<WorkspaceState, WorkspaceState>? tweak = null, int? backingPercent = null,
            IReadOnlyList<(string, float)>? dividers = null, IReadOnlyDictionary<string, PaneStatusBand>? bands = null)
        {
            var bars = Bars();
            State = WorkspaceState.Initial with
            {
                Data = new TimeSeriesBuffer<Ohlcv>(bars),
                ViewportStartIndex = 0,
                ViewportLength = 100,
                ViewportRange = (79_000, 82_000),
                CurrentDataIndex = 90,
                Identity = new ChartIdentity("Spot", "Bitstamp", "BTCUSDT", "1d"),
            };
            if (tweak != null) State = tweak(State);
            Harness = ChartAreaBrowserCanvasBranchTests.BuildHarness(isBrowserHost: false, state: State);
            Harness.WorkspaceStore.State.Returns(_ => State);
            if (backingPercent is { } p)
                Harness.SettingsManager.GetSetting(SettingsKeys.StatusLineBackingOpacity, Arg.Any<JToken?>())
                    .Returns(new JValue(p));
            if (dividers != null)
                Harness.Ctx.Services.GetRequiredService<IPaneLayoutService>().Dividers.Returns(dividers);
            if (bands != null)
                Harness.Ctx.Services.GetRequiredService<IPaneLayoutService>().StatusBands.Returns(bands);
            Cut = Harness.Ctx.RenderComponent<ChartArea>();
            Input = Harness.Ctx.Services.GetRequiredService<BlazorInputService>();
            Tracker = Harness.Ctx.Services.GetRequiredService<ChartHoverTracker>();
        }

        public void SetState(WorkspaceState s)
        {
            State = s;
            Cut.Render();
        }

        public void Mouse(double x, double y) =>
            Cut.InvokeAsync(() => Input.ProcessMouse(x, y, "MouseMove", W, H)).GetAwaiter().GetResult();

        public IElement? SymbolLine() => Cut.FindAll(".chart-status-line[data-kind='Symbol']").FirstOrDefault();

        public void Dispose() => Harness.Dispose();
    }

    private static string Close(WorkspaceState s, int i) => ChartStatusLine.FormatReading(s.Data[i].Close);

    /// <summary>
    /// The price pane's status line is DOM text naming the chart and the cursor bar's O H L C,
    /// hidden from the accessibility tree (a visual mirror of what is spoken — Cody's condition) and
    /// transparent to the mouse.
    /// </summary>
    [Fact]
    public void The_status_lines_are_hidden_dom_text_naming_the_chart_and_the_cursor_bar()
    {
        using var rig = new Rig();
        var block = rig.Cut.Find(".chart-status");
        Assert.Equal("true", block.GetAttribute("aria-hidden"));
        Assert.Contains("pointer-events:none", block.GetAttribute("style")!.Replace(" ", ""));

        var line = rig.SymbolLine();
        Assert.NotNull(line);
        Assert.Contains("BTCUSDT · 1d · Bitstamp", line!.TextContent);
        var bar = rig.State.Data[90];
        foreach (var v in new[] { bar.Open, bar.High, bar.Low, bar.Close })
            Assert.Contains(ChartStatusLine.FormatReading(v), line.TextContent);
    }

    /// <summary>
    /// Cipher C's cycle table is drawn in the price pane's corner, as the original draws its
    /// table, hidden from the accessibility tree (what it shows is spoken) and transparent to
    /// the mouse, and reads the cursor bar.
    /// </summary>
    [Fact]
    public void Cipher_Cs_table_is_hidden_dom_text_reading_the_cursor_bar()
    {
        var bars = new List<Ohlcv>();
        for (int i = 0; i < 400; i++)
        {
            double c = 80_000 + 4_000 * Math.Sin(2 * Math.PI * i / 60.0) + i * 5;
            bars.Add(new Ohlcv(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i), c - 50, c + 300, c - 300, c, 1000));
        }
        var data = new SeriesDataBuffer { SeriesId = "cc" };
        foreach (var (k, v) in AccessibleTrader.Core.Services.Indicators.CipherCProvider.Compute(bars.ToArray(),
                     new Dictionary<string, object> { ["__symbol"] = "BTCUSDT" }))
            data.ComponentData[k] = v;
        var series = new ChartSeries(new SeriesConfig { Id = "cc", Name = "Cipher C", IndicatorCode = "CIPHER_C", Pane = "Main" }, data);
        using var rig = new Rig(s => s with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(bars), ViewportStartIndex = 300, CurrentDataIndex = 390,
            ViewportRange = (70_000, 90_000), ActiveSeries = System.Collections.Immutable.ImmutableList.Create(series),
        });
        var block = rig.Cut.Find(".chart-info-tables");
        Assert.Equal("true", block.GetAttribute("aria-hidden"));
        Assert.Contains("pointer-events:none", block.GetAttribute("style")!.Replace(" ", ""));
        var table = rig.Cut.Find(".chart-info-table");
        Assert.Contains("BITCOIN CYCLE COUNT", table.TextContent);
        Assert.Equal(7, table.QuerySelectorAll("tr").Length);
        string Count() => rig.Cut.Find(".chart-info-table").QuerySelectorAll("tr")[2].QuerySelectorAll("td")[^1].TextContent;
        string at390 = Count();
        Assert.Matches("^[0-9]+$", at390);
        rig.SetState(rig.State with { CurrentDataIndex = 380 });
        Assert.NotEqual(at390, Count());

        rig.SetState(rig.State with { ActiveSeries = System.Collections.Immutable.ImmutableList<ChartSeries>.Empty });
        Assert.Empty(rig.Cut.FindAll(".chart-info-tables"));
    }

    /// <summary>The line follows the keyboard cursor: an arrow key's move is a new reading.</summary>
    [Fact]
    public void The_status_line_follows_the_keyboard_cursor()
    {
        using var rig = new Rig();
        Assert.Contains(Close(rig.State, 90), rig.SymbolLine()!.TextContent);
        rig.SetState(rig.State with { CurrentDataIndex = 40 });
        Assert.Contains(Close(rig.State, 40), rig.SymbolLine()!.TextContent);
        Assert.DoesNotContain(Close(rig.State, 90), rig.SymbolLine()!.TextContent);
    }

    /// <summary>
    /// <b>A moving pointer leads; a parked one does not.</b> Hovering a bar shows that bar, as
    /// TradingView's crosshair does. Then the keyboard cursor moves with the mouse still on the
    /// chart — Cody's way of working (2026-10-15) — and the line goes back to the bar he is hearing,
    /// not the one under a mouse he is not using.
    /// </summary>
    [Fact]
    public void The_status_line_follows_the_hover_until_the_keyboard_cursor_moves()
    {
        using var rig = new Rig();
        rig.Mouse(200, 200);
        var hovered = rig.Tracker.Current;
        Assert.NotNull(hovered);
        Assert.NotEqual(90, hovered!.BarIndex);   // vacuity: the hover is on another bar
        rig.Cut.WaitForAssertion(() => Assert.Contains(Close(rig.State, hovered.BarIndex), rig.SymbolLine()!.TextContent));

        rig.SetState(rig.State with { CurrentDataIndex = 91 });
        Assert.NotNull(rig.Tracker.Current);       // the mouse is still there ...
        Assert.Contains(Close(rig.State, 91), rig.SymbolLine()!.TextContent);   // ... the cursor leads
        Assert.DoesNotContain(Close(rig.State, hovered.BarIndex), rig.SymbolLine()!.TextContent);
    }

    /// <summary>
    /// The hovered bar's O H L C is not a copy taken at the mouse move: a live tick that rewrites
    /// the hovered bar reaches the status line on the next render. (The retired readout box had to
    /// re-map for this; ChartHoverStillMouseTests' "live tick on the hovered bar" case went with it.)
    /// </summary>
    [Fact]
    public void A_live_tick_on_the_hovered_bar_reaches_the_status_line()
    {
        using var rig = new Rig();
        rig.Mouse(200, 200);
        int i = rig.Tracker.Current!.BarIndex;
        var bars = rig.State.Data.ToList();
        bars[i] = bars[i] with { Close = bars[i].Close + 7.25 };
        rig.SetState(rig.State with { Data = new TimeSeriesBuffer<Ohlcv>(bars) });
        Assert.Contains(ChartStatusLine.FormatReading(bars[i].Close), rig.SymbolLine()!.TextContent);
    }

    /// <summary>
    /// <b>The floating readout box is retired, and nothing it showed is lost.</b> Its date is now a
    /// label on the time axis at the hairline, its pointer price a label on the price axis at the
    /// pointer, and its O H L C the symbol line (above).
    /// </summary>
    [Fact]
    public void The_hover_box_is_gone_and_its_date_and_price_are_axis_labels()
    {
        using var rig = new Rig();
        rig.Mouse(200, 200);
        var h = rig.Tracker.Current!;
        rig.Cut.WaitForAssertion(() => Assert.NotEmpty(rig.Cut.FindAll(".chart-hover-date")));

        Assert.Empty(rig.Cut.FindAll(".chart-hover-readout"));
        Assert.Equal(h.DateText, rig.Cut.Find(".chart-hover-date").TextContent.Trim());
        Assert.Equal(h.PriceText, rig.Cut.Find(".chart-hover-price").TextContent.Trim());
        Assert.False(string.IsNullOrEmpty(h.PriceText));
    }

    /// <summary>Settings → Appearance → Status line background reaches the backing's opacity.</summary>
    [Theory]
    [InlineData(null, "0.961")]
    [InlineData(60, "0.6")]
    [InlineData(100, "1")]
    public void The_backing_opacity_setting_reaches_the_lines(int? percent, string alpha)
    {
        using var rig = new Rig(backingPercent: percent);
        var style = rig.SymbolLine()!.GetAttribute("style")!.Replace(" ", "");
        Assert.Matches($@"background:rgba\(\d+,\d+,\d+,{System.Text.RegularExpressions.Regex.Escape(alpha)}\)", style);
    }

    /// <summary>
    /// <b>The pointer's value is on the scale of the pane it is in.</b> The readout box printed the
    /// price pane's range mapped onto whichever pane the pointer was over, so over RSI it showed a
    /// "price" that was no price at all. The axis label over the RSI pane is an RSI value.
    /// </summary>
    [Fact]
    public void Over_an_indicator_pane_the_pointer_value_is_on_that_panes_scale()
    {
        var store = Substitute.For<Core.Services.IWorkspaceStore>();
        var state = WorkspaceState.Initial with
        {
            Data = new TimeSeriesBuffer<Ohlcv>(Bars()),
            ViewportStartIndex = 0,
            ViewportLength = 100,
            ViewportRange = (79_000, 82_000),
            PaneRanges = System.Collections.Immutable.ImmutableDictionary<string, (double Min, double Max)>.Empty.Add("Pane_RSI", (0, 100)),
        };
        store.State.Returns(state);
        store.StateStream.Returns(System.Reactive.Linq.Observable.Empty<WorkspaceState>());
        var layout = new PaneLayoutService();
        layout.Update(new[] { ("Pane_RSI", 0.5f) }, 0.1f, 0.1f);
        var input = new BlazorInputService();
        using var tracker = new ChartHoverTracker(input, store, paneLayout: layout);

        double y = H * 0.7;   // 40% of the way down a pane running from 50% to 90% of the height
        input.ProcessMouse(300, y, "MouseMove", W, H);

        double expected = Core.Services.ChartMath.MapYToPriceInPane(y, H, layout.Dividers, 0.1f, 0, 100, false);
        Assert.InRange(expected, 1, 99);
        Assert.Equal(ChartStatusLine.FormatReading(expected), tracker.Current!.PriceText);

        input.ProcessMouse(300, H * 0.2, "MouseMove", W, H);   // and over the price pane, a price
        Assert.InRange(double.Parse(tracker.Current!.PriceText!.Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture), 79_000, 82_000);
    }

    /// <summary>
    /// An indicator pane's lines sit at the top of THAT pane, placed from the renderer's dividers —
    /// the ones the drag handles use — and read the same bar.
    /// </summary>
    [Fact]
    public void An_indicator_panes_lines_sit_at_its_divider()
    {
        var rsi = new ChartSeries(
            new SeriesConfig { Id = "rsi", Name = "RSI 14", Pane = "Pane_RSI", IndicatorCode = "RSI" },
            new SeriesDataBuffer { SeriesId = "rsi" });
        rsi.Components.Add(new ComponentConfig { Name = "RSI", DisplayName = "RSI", DisplayType = ComponentDisplayType.Line, ColorHex = "#7E57C2", IsVisible = true });
        rsi.Data.ComponentData["RSI"] = Enumerable.Range(0, 100).Select(i => 30.0 + i * 0.25).ToArray();

        using var rig = new Rig(s => s with { ActiveSeries = System.Collections.Immutable.ImmutableList.Create(rsi) },
            dividers: new[] { ("Pane_RSI", 0.7f) });

        var pane = rig.Cut.Find(".chart-status-pane[data-pane='Pane_RSI']");
        Assert.Contains("top:calc(70.000%", pane.GetAttribute("style")!.Replace(" ", ""));
        var line = pane.QuerySelector(".chart-status-line")!;
        Assert.Contains("RSI", line.TextContent);
        Assert.Contains(ChartStatusLine.FormatReading(30.0 + 90 * 0.25), line.TextContent);
    }

    /// <summary>
    /// <b>The block is no taller than the band the renderer left for it</b> (2026-10-17: each
    /// pane's plot starts below its status lines). ChartArea hands the published bands to the
    /// model, so a pane whose band has room for one line shows one — the first indicator, with the
    /// others counted on it — even where the pane, counted on its own, would have room for three.
    /// A block taller than its band would sit on the plot again.
    /// </summary>
    [Fact]
    public void A_panes_block_has_no_more_lines_than_the_band_the_renderer_left()
    {
        ChartSeries Ind(string id, string name)
        {
            var s = new ChartSeries(
                new SeriesConfig { Id = id, Name = name, Pane = "Pane_RSI", IndicatorCode = id.ToUpperInvariant() },
                new SeriesDataBuffer { SeriesId = id });
            s.Components.Add(new ComponentConfig { Name = "V", DisplayName = "V", DisplayType = ComponentDisplayType.Line, ColorHex = "#7E57C2", IsVisible = true });
            s.Data.ComponentData["V"] = Enumerable.Repeat(40.0, 100).ToArray();
            return s;
        }
        var three = System.Collections.Immutable.ImmutableList.Create(Ind("a", "RSI 14"), Ind("b", "CCI 20"), Ind("c", "MFI 14"));

        // Control: no band published (no frame yet) — the pane, 30% of 720 px, holds all three.
        using (var free = new Rig(s => s with { ActiveSeries = three }, dividers: new[] { ("Pane_RSI", 0.6f) }))
            Assert.Equal(3, free.Cut.FindAll(".chart-status-pane[data-pane='Pane_RSI'] .chart-status-line").Count);

        var bands = new Dictionary<string, PaneStatusBand>
        {
            ["Main"] = new(1, 25f / (float)H),
            ["Pane_RSI"] = new(1, 25f / (float)H),
        };
        using var rig = new Rig(s => s with { ActiveSeries = three }, dividers: new[] { ("Pane_RSI", 0.6f) }, bands: bands);
        var lines = rig.Cut.FindAll(".chart-status-pane[data-pane='Pane_RSI'] .chart-status-line");
        var only = Assert.Single(lines);
        Assert.Contains("RSI", only.TextContent);
        Assert.Contains("+2 more", only.TextContent);
    }

    /// <summary>
    /// The model's measures ARE the block's CSS: the lines are 0.75rem with a 1.4167 line height
    /// (<see cref="ChartStatusLine.FontPx"/>, <see cref="ChartStatusLine.LinePx"/> at 100% text),
    /// and each pane's block starts <see cref="ChartStatusLine.PadPx"/> px below its top. The band
    /// the renderer keeps is counted in these; a restyle that changed one without the other would
    /// put the text back on the plot.
    /// </summary>
    [Fact]
    public void The_blocks_css_is_the_measure_the_band_is_counted_in()
    {
        using var rig = new Rig();
        var style = rig.Cut.Find(".chart-status").GetAttribute("style")!.Replace(" ", "");
        var font = System.Text.RegularExpressions.Regex.Match(style, @"font-size:([\d.]+)rem");
        var lh = System.Text.RegularExpressions.Regex.Match(style, @"line-height:([\d.]+);");
        Assert.True(font.Success && lh.Success, $"the block's style has no rem font size and unitless line height: {style}");
        double fontPx = double.Parse(font.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 16;
        double linePx = fontPx * double.Parse(lh.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(ChartStatusLine.FontPx, fontPx, 3);
        Assert.Equal(ChartStatusLine.LinePx, linePx, 1);

        var pane = rig.Cut.Find(".chart-status-pane[data-pane='Main']").GetAttribute("style")!.Replace(" ", "");
        Assert.Contains($"+{ChartStatusLine.PadPx.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)", pane);
    }
}
