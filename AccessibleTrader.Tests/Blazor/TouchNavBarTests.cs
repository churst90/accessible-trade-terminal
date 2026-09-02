// TouchNavBar — bUnit coverage. The Phase C touch toolbar: large plain buttons
// (the most robust mobile screen-reader pattern) that route through the exact
// commands the equivalent keys use, so speech + sonification are identical.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class TouchNavBarTests
{
    private static (BlazorTestHarness h, INavigationEngine nav) NewHarness(WorkspaceState? state = null)
    {
        var h = new BlazorTestHarness();
        h.WorkspaceStore.State.Returns(_ => state ?? WorkspaceState.Initial);
        var nav = Substitute.For<INavigationEngine>();
        h.Ctx.Services.AddSingleton(nav);
        // The bar renders only on touch-capable devices (JS probe on first render).
        h.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(true);
        return (h, nav);
    }

    [Fact]
    public void HideOverride_WinsEvenOnTouchDevices()
    {
        // Device probes have misfired on desktop input stacks before — the user's
        // explicit "Never show" must beat any probe result.
        var h = new BlazorTestHarness();
        using var _1 = h;
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial);
        h.Ctx.Services.AddSingleton(Substitute.For<INavigationEngine>());
        h.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(true);
        h.SettingsManager.GetSetting(AccessibleTrader.Core.Services.SettingsKeys.TouchNavBar)
            .Returns(Newtonsoft.Json.Linq.JToken.FromObject("hide"));

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TouchNavBar>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("nav[aria-label='Touch navigation']")));
    }

    [Fact]
    public void SelectingNever_HidesTheBarImmediately_LiveFlow()
    {
        // The full user path: bar visible (probe true, mode auto) → user picks
        // "Never" in Settings (setting saved + TouchNavBarModeChangedEvent) →
        // bar leaves the DOM without closing the modal or restarting.
        var h = new BlazorTestHarness();
        using var _1 = h;
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial);
        h.Ctx.Services.AddSingleton(Substitute.For<INavigationEngine>());
        h.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(true);

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TouchNavBar>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("nav[aria-label='Touch navigation']")));

        // The settings handler's exact effects:
        h.SettingsManager.GetSetting(AccessibleTrader.Core.Services.SettingsKeys.TouchNavBar)
            .Returns(Newtonsoft.Json.Linq.JToken.FromObject("hide"));
        cut.InvokeAsync(() => h.EventBus.Publish(new TouchNavBarModeChangedEvent("hide")));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("nav[aria-label='Touch navigation']")));
    }

    [Fact]
    public void Stays_out_of_the_DOM_on_non_touch_devices()
    {
        // Desktop (probe returns false): the toolbar must not exist AT ALL —
        // hidden-by-CSS still cluttered desktop screen readers' tab order.
        var h = new BlazorTestHarness();
        using var _1 = h;
        h.WorkspaceStore.State.Returns(_ => WorkspaceState.Initial);
        h.Ctx.Services.AddSingleton(Substitute.For<INavigationEngine>());
        h.Ctx.JSInterop.Setup<bool>("accessibleTrader.isTouchCapable").SetResult(false);

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TouchNavBar>();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("nav[aria-label='Touch navigation']")));
    }

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.TouchNavBar>
        Render(BlazorTestHarness h) =>
        h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TouchNavBar>();

    [Fact]
    public void Renders_a_toolbar_with_labelled_buttons_for_every_core_action()
    {
        var (h, _) = NewHarness();
        using var _1 = h;
        var cut = Render(h);

        Assert.NotEmpty(cut.FindAll("nav[aria-label='Touch navigation']"));
        var labels = cut.FindAll("button").Select(b => b.GetAttribute("aria-label")).ToList();
        Assert.Contains("Previous bar", labels);
        Assert.Contains("Next bar", labels);
        Assert.Contains("Previous component", labels);
        Assert.Contains("Next component", labels);
        Assert.Contains("Play chart", labels);
        Assert.Contains("Chart menu", labels);
    }

    [Theory]
    [InlineData("Previous bar", "NAV_LEFT")]
    [InlineData("Next bar", "NAV_RIGHT")]
    [InlineData("Previous component", "NAV_COMP_UP")]
    [InlineData("Next component", "NAV_COMP_DOWN")]
    public void Navigation_buttons_route_through_the_keyboards_navigation_commands(
        string label, string expectedCommand)
    {
        var (h, nav) = NewHarness();
        using var _1 = h;
        var cut = Render(h);

        cut.FindAll("button").First(b => b.GetAttribute("aria-label") == label).Click();

        nav.Received(1).ProcessNavigation(expectedCommand);
    }

    [Fact]
    public void Play_button_starts_chart_playback_like_the_space_key()
    {
        var (h, _) = NewHarness();
        using var _1 = h;
        var cut = Render(h);

        cut.FindAll("button").First(b => b.GetAttribute("aria-label") == "Play chart").Click();

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetPlaybackAction>(a => a.IsPlaying && a.Scope == PlaybackScope.Chart));
    }

    [Fact]
    public void Play_button_becomes_stop_while_playing()
    {
        var playing = WorkspaceState.Initial with { IsPlaying = true };
        var (h, _) = NewHarness(playing);
        using var _1 = h;
        var cut = Render(h);

        var stop = cut.FindAll("button").First(b => b.GetAttribute("aria-label") == "Stop playback");
        stop.Click();

        h.WorkspaceStore.Received(1).Dispatch(
            Arg.Is<SetPlaybackAction>(a => !a.IsPlaying));
    }

    [Fact]
    public void Menu_button_opens_the_chart_context_menu_at_the_current_cursor_bar()
    {
        var state = WorkspaceState.Initial with { CurrentDataIndex = 42 };
        var (h, _) = NewHarness(state);
        using var _1 = h;
        OpenChartContextMenuEvent? seen = null;
        h.EventBus.Subscribe<OpenChartContextMenuEvent>(ev => seen = ev);
        var cut = Render(h);

        cut.FindAll("button").First(b => b.GetAttribute("aria-label") == "Chart menu").Click();

        Assert.NotNull(seen);
        Assert.Equal(42, seen!.BarIndex);
        Assert.True(double.IsNaN(seen.ViewportX)); // keyboard-origin self-positioning
    }
}
