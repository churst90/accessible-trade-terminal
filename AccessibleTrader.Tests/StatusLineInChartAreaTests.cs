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
            IReadOnlyList<(string, float)>? dividers = null)
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
}
