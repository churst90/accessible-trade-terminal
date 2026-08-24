using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Models;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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

        // The bar re-rendered on the state stream, so both tabs are present now.
        Assert.Equal(2, cut.FindAll("button[role='tab']").Count);
    }

    [Fact]
    public void Close_affordance_is_a_presentational_span_not_a_nested_button()
    {
        // Regression: the close control was a <button> nested inside the role="tab"
        // button — invalid HTML the parser hoists out, corrupting the tablist's owned
        // elements (tab, close, tab, close…). It is now a mouse-only aria-hidden span;
        // the keyboard route is Delete on the tablist.
        using var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var store = NewStore();
        ctx.Services.AddSingleton<IWorkspaceStore>(store);
        ctx.Services.AddSingleton<IEventBus>(new EventBus());

        var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.TabBar>();
        cut.InvokeAsync(() => store.Dispatch(new AddTabAction()));   // 2 tabs → closable

        Assert.Empty(cut.FindAll("button.tab-close"));
        var closes = cut.FindAll("span.tab-close");
        Assert.Equal(2, closes.Count);
        Assert.All(closes, c => Assert.Equal("true", c.GetAttribute("aria-hidden")));
    }
}
