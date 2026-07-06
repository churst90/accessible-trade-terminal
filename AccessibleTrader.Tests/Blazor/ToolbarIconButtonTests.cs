using Bunit;
using Xunit;
using Cmp = AccessibleTrader.BlazorClient.Components;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Pins the toolbar-button help contract: every ToolbarIconButton renders its Tooltip
/// as a hover <c>title</c> and always exposes an accessible name. The Trade button relies
/// on this (Tooltip="Open trading dashboard (Alt+T)", AriaLabel="Trading Dashboard"), so
/// these tests guard against a toolbar button shipping without an associated tooltip/help.
/// </summary>
public sealed class ToolbarIconButtonTests
{
    [Fact]
    public void Renders_tooltip_as_title_and_explicit_aria_label()
    {
        using var ctx = new TestContext();
        var cut = ctx.RenderComponent<Cmp.ToolbarIconButton>(p => p
            .Add(x => x.Icon, "trade")
            .Add(x => x.Label, "Trade")
            .Add(x => x.Tooltip, "Open trading dashboard (Alt+T)")
            .Add(x => x.AriaLabel, "Trading Dashboard"));

        var btn = cut.Find("button");
        Assert.Equal("Open trading dashboard (Alt+T)", btn.GetAttribute("title"));
        Assert.Equal("Trading Dashboard", btn.GetAttribute("aria-label"));
        Assert.Contains("Trade", btn.TextContent);
    }

    [Fact]
    public void Aria_label_falls_back_to_the_visible_label_when_not_set()
    {
        using var ctx = new TestContext();
        var cut = ctx.RenderComponent<Cmp.ToolbarIconButton>(p => p
            .Add(x => x.Icon, "zoom-in")
            .Add(x => x.Label, "Zoom in")
            .Add(x => x.Tooltip, "Zoom in (Plus)"));

        var btn = cut.Find("button");
        Assert.Equal("Zoom in (Plus)", btn.GetAttribute("title"));
        Assert.Equal("Zoom in", btn.GetAttribute("aria-label"));
    }
}
