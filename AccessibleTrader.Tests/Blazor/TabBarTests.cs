using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// Regression: a workspace tab created from OUTSIDE the tab bar's own DOM events
/// (the Ctrl+T / Alt+Shift+N keyboard command, or "Open in New Tab" from the Toolbar)
/// used to not appear until an unrelated click on the bar forced a re-render, because
/// TabBar read Store.State directly but never subscribed to Store.StateStream. This
/// pins the subscription: dispatching AddTabAction re-renders the bar immediately.
/// </summary>
public sealed class TabBarTests
{
    private static WorkspaceStore NewStore()
        => new WorkspaceStore(
            new EventBus(),
            new ViewportRangeCalculator(),
            new ViewportNavigationService(),
            new VolumeStateService());

    [Fact]
    public void New_tab_appears_in_the_bar_without_a_click()
    {
        using var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = NewStore();
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton<IEventBus>(new EventBus());

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TabBar>();
        Assert.Single(cut.FindAll("button[role='tab']"));

        // Dispatch from outside the component — no DOM event on the bar.
        cut.InvokeAsync(() => store.Dispatch(new AddTabAction()));

        // The bar re-renders on the state stream, so wait for it: InvokeAsync returns a
        // Task this test does not await, and asserting synchronously is the race that took
        // five other bUnit tests red on CI while passing locally every run (2026-08-24).
        cut.WaitForAssertion(() =>
            Assert.Equal(2, cut.FindAll("button[role='tab']").Count));
    }

    [Fact]
    public void Close_is_a_named_button_beside_the_tab_never_nested_inside_it()
    {
        // Two regressions guarded at once. (1) The close control was once a <button> nested
        // inside the role="tab" button — invalid HTML the parser hoists out, corrupting the
        // tablist's owned elements (tab, close, tab, close…). (2) It was then an aria-hidden
        // span, which fixed the nesting by making the control invisible to a screen reader's
        // browse mode. Cody asked for "a delete button too next to each tab" (2026-09-05): a
        // real button, a sibling of the tab, with a name that says which tab it closes.
        using var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = NewStore();
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton<IEventBus>(new EventBus());

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TabBar>();
        // Blocking so the assertions below are a real claim rather than a read of a DOM that
        // has not re-rendered yet — the same race that took five bUnit tests red on CI while
        // passing locally (2026-08-24).
        cut.InvokeAsync(() => store.Dispatch(new AddTabAction())).GetAwaiter().GetResult();   // 2 tabs → closable

        Assert.Empty(cut.FindAll("button[role='tab'] button"));       // never nested
        Assert.Empty(cut.FindAll("button[role='tab'] .tab-close"));   // not even as a span
        var closes = cut.FindAll("button.tab-close");
        Assert.Equal(2, closes.Count);
        Assert.All(closes, c =>
        {
            Assert.StartsWith("Close tab ", c.GetAttribute("aria-label"));
            Assert.Equal("-1", c.GetAttribute("tabindex"));           // Delete is the keyboard route
            Assert.Null(c.GetAttribute("aria-hidden"));
        });
    }

    [Fact]
    public void Clicking_a_tabs_close_button_closes_that_tab_not_the_active_one()
    {
        using var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = NewStore();
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton<IEventBus>(new EventBus());

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TabBar>();
        cut.InvokeAsync(() => store.Dispatch(new AddTabAction())).GetAwaiter().GetResult();
        cut.InvokeAsync(() => store.Dispatch(new AddTabAction())).GetAwaiter().GetResult();   // 3 tabs, active = 2
        Assert.Equal(3, store.State.TabCount);

        // Close tab 1 (index 0) while tab 3 is active.
        cut.FindAll("button.tab-close")[0].Click();

        cut.WaitForAssertion(() => Assert.Equal(2, store.State.TabCount));
    }

    [Fact]
    public void With_one_tab_there_is_nothing_to_close_and_no_close_button()
    {
        using var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = NewStore();
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton<IEventBus>(new EventBus());

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TabBar>();

        Assert.Single(cut.FindAll("button[role='tab']"));
        Assert.Empty(cut.FindAll("button.tab-close"));
    }
}
