using AccessibleTrader.Core.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// The component publishes the refusal of a gated press on the event bus, so it
    /// needs a real one. (A missing bus throws at component initialisation rather than
    /// failing quietly — deliberate: every host that renders a toolbar has one.)
    /// </summary>
    private static TestContext NewContext()
    {
        var ctx = new TestContext();
        ctx.Services.AddSingleton<IEventBus>(new EventBus());
        return ctx;
    }

    [Fact]
    public void A_gated_button_stays_in_the_tab_order_and_says_why_it_is_refusing()
    {
        // The whole point of replacing `bool Disabled` with a gate. Under `disabled` the
        // four pan/zoom buttons and Load left the toolbar entirely on a cold start —
        // five controls a screen reader could not find, and nothing to explain them.
        using var ctx = NewContext();
        var bus = (IEventBus)ctx.Services.GetRequiredService<IEventBus>();
        var spoken = new List<string?>();
        using var sub = bus.Subscribe<AccessibleTrader.Core.Models.FeedbackRequestEvent>(e => spoken.Add(e.Message));

        int clicks = 0;
        var cut = ctx.RenderComponent<Cmp.ToolbarIconButton>(p => p
            .Add(x => x.Icon, "pan-left")
            .Add(x => x.Label, "Pan left")
            .Add(x => x.AriaLabel, "Pan chart left")
            .Add(x => x.Gate, () => "No chart is loaded yet.")
            .Add(x => x.OnClick, () => { clicks++; }));

        var btn = cut.Find("button");
        Assert.False(btn.HasAttribute("disabled"));
        Assert.Equal("true", btn.GetAttribute("aria-disabled"));
        Assert.Equal("No chart is loaded yet.", GatedButtonAssert.ReasonOf(cut, btn));

        btn.Click();
        Assert.Equal(0, clicks);                       // the handler never runs
        Assert.Equal(new[] { "No chart is loaded yet." }, spoken);
    }

    [Fact]
    public void An_ungated_button_announces_no_state_and_runs_its_handler()
    {
        using var ctx = NewContext();
        int clicks = 0;
        var cut = ctx.RenderComponent<Cmp.ToolbarIconButton>(p => p
            .Add(x => x.Icon, "pan-left")
            .Add(x => x.Label, "Pan left")
            .Add(x => x.OnClick, () => { clicks++; }));

        var btn = cut.Find("button");
        GatedButtonAssert.IsAvailable(cut, btn);
        btn.Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Renders_tooltip_as_title_and_explicit_aria_label()
    {
        using var ctx = NewContext();
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
        using var ctx = NewContext();
        var cut = ctx.RenderComponent<Cmp.ToolbarIconButton>(p => p
            .Add(x => x.Icon, "zoom-in")
            .Add(x => x.Label, "Zoom in")
            .Add(x => x.Tooltip, "Zoom in (Plus)"));

        var btn = cut.Find("button");
        Assert.Equal("Zoom in (Plus)", btn.GetAttribute("title"));
        Assert.Equal("Zoom in", btn.GetAttribute("aria-label"));
    }
}
