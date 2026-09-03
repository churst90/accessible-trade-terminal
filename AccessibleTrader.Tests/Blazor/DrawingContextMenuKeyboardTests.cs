using System.Collections.Immutable;
using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The drawing context menu (right-click, or Shift+F10 / the Application key on a focused
/// drawing) is a <c>role="menu"</c>, and since 2026-09-03 it is the documented route to the
/// keyboard nudge for voice control, switch access and single-pointer users. A menu is
/// operated with the arrow keys; until this pass nothing handled them — keyboard.js releases
/// arrows to any <c>[role="menu"]</c> on the assumption the widget handles them — so Up and
/// Down did nothing, and Tab walked out of the menu into the page while the dispatcher still
/// treated the menu as an open modal.
/// </summary>
public sealed class DrawingContextMenuKeyboardTests
{
    private static (BlazorTestHarness h, IRenderedComponent<DrawingContextMenu> menu) Open()
    {
        var h = new BlazorTestHarness();
        h.Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var config = new SeriesConfig { Id = "d1", Name = "Trend line (1)", FriendlyName = "TrendLine Drawing", IndicatorCode = "DRAWING" };
        var drawing = new ChartSeries(config, new SeriesDataBuffer { SeriesId = "d1" })
            { Drawing = new DrawingData { Type = DrawingType.TrendLine, AnchorPrice1 = 100, AnchorPrice2 = 101 } };
        var state = WorkspaceState.Initial with { ActiveSeries = ImmutableList.Create(drawing), FocusedSeriesId = "d1" };
        h.WorkspaceStore.State.Returns(_ => state);

        var menu = h.Ctx.RenderComponent<DrawingContextMenu>();
        h.EventBus.Publish(new OpenDrawingContextMenuEvent("d1", double.NaN, double.NaN));
        menu.WaitForAssertion(() => Assert.NotEmpty(menu.FindAll("[role='menuitem']")));
        return (h, menu);
    }

    private static IReadOnlyList<string> TabStops(IRenderedComponent<DrawingContextMenu> menu) =>
        menu.FindAll("[role='menuitem']").Where(b => b.GetAttribute("tabindex") == "0").Select(b => b.TextContent.Trim()).ToList();

    [Fact]
    public void The_menu_has_the_six_nudge_items_after_a_separator_and_exactly_one_Tab_stop()
    {
        var (_, menu) = Open();
        var items = menu.FindAll("[role='menuitem']").Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[]
        {
            "Delete", "Duplicate", "Properties",
            "Move anchor one bar earlier", "Move anchor one bar later",
            "Move anchor price up", "Move anchor price down",
            "Select next anchor", "Snap anchor to bar's high, low, open or close",
        }, items);
        Assert.Single(menu.FindAll("[role='separator']"));
        Assert.Equal(new[] { "Delete" }, TabStops(menu));
    }

    [Fact]
    public void ArrowDown_and_ArrowUp_move_the_roving_tabindex_and_wrap()
    {
        var (_, menu) = Open();
        var root = menu.Find("[role='menu']");

        root.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        menu.WaitForAssertion(() => Assert.Equal(new[] { "Duplicate" }, TabStops(menu)));

        root.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        root.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        menu.WaitForAssertion(() => Assert.Equal(new[] { "Snap anchor to bar's high, low, open or close" }, TabStops(menu)));

        root.KeyDown(new KeyboardEventArgs { Key = "Home" });
        menu.WaitForAssertion(() => Assert.Equal(new[] { "Delete" }, TabStops(menu)));
        root.KeyDown(new KeyboardEventArgs { Key = "End" });
        menu.WaitForAssertion(() => Assert.Equal(new[] { "Snap anchor to bar's high, low, open or close" }, TabStops(menu)));
    }

    [Fact]
    public void Tab_leaves_the_menu_by_closing_it_so_the_chart_is_not_left_keyboard_locked()
    {
        var (h, menu) = Open();
        var closes = new List<ModalStateChangedEvent>();
        h.EventBus.Subscribe<ModalStateChangedEvent>(e => { if (!e.IsOpen) closes.Add(e); });

        menu.Find("[role='menu']").KeyDown(new KeyboardEventArgs { Key = "Tab" });

        menu.WaitForAssertion(() => Assert.Empty(menu.FindAll("[role='menu']")));
        Assert.Contains(closes, e => e.ModalName == "DrawingContextMenu");
    }

    [Fact]
    public void Activating_a_nudge_item_publishes_the_nudge_and_keeps_the_menu_open()
    {
        var (h, menu) = Open();
        var nudges = new List<NudgeDrawingAnchorEvent>();
        h.EventBus.Subscribe<NudgeDrawingAnchorEvent>(nudges.Add);

        menu.FindAll("[role='menuitem']").First(b => b.TextContent.Trim() == "Move anchor one bar later").Click();
        menu.FindAll("[role='menuitem']").First(b => b.TextContent.Trim() == "Move anchor one bar later").Click();

        Assert.Equal(new[] { AnchorNudgeDirection.Later, AnchorNudgeDirection.Later }, nudges.Select(n => n.Direction));
        Assert.NotEmpty(menu.FindAll("[role='menu']"));   // still open: "move later" five times is the use case
    }

    [Fact]
    public void A_menu_opened_on_an_unfocused_drawing_focuses_it_before_nudging()
    {
        var (h, menu) = Open();
        var state = h.WorkspaceStore.State with { FocusedSeriesId = "candles" };
        h.WorkspaceStore.State.Returns(_ => state);

        menu.FindAll("[role='menuitem']").First(b => b.TextContent.Trim() == "Select next anchor").Click();

        h.WorkspaceStore.Received().Dispatch(Arg.Is<SelectSeriesAction>(a => a.SeriesId == "d1"));
    }
}
