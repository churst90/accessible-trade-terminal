using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The status strip's live region announces the MESSAGE, and nothing else.
///
/// <para><b>The defect.</b> A static <c>&lt;span class="status-label"&gt;Last Feedback:&lt;/span&gt;</c>
/// sat inside the <c>role="status"</c> div. A live region is announced by its WHOLE content on
/// every change, so the label was read before every sentence the application has ever spoken —
/// Cody reported reading "last feedback, focus on chart area" where "Focus on chart area" was
/// the entire point. It is the same mistake the paper-trading badge made, in the same element,
/// and the badge was already moved out for it; this one survived because it never changed, and
/// a static string inside a live region looks harmless right up until it is spoken.</para>
///
/// <para>The assertion is on the region's exact text rather than on the absence of the string
/// "Last Feedback": a label reworded to "Status:" would be the identical defect and would pass
/// a substring check. What is being pinned is that the region carries one thing.</para>
/// </summary>
public sealed class StatusBarLiveRegionTests
{
    private static (TestContext ctx, IEventBus bus) NewContext()
    {
        var ctx = new TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var bus = new EventBus();
        ctx.Services.AddSingleton<IEventBus>(bus);
        ctx.Services.AddSingleton<ISettingsManager>(Substitute.For<ISettingsManager>());
        ctx.Services.AddSingleton(new DemoPolicy(isDemo: false));
        return (ctx, bus);
    }

    [Fact]
    public void The_live_region_holds_the_message_alone()
    {
        var (ctx, bus) = NewContext();
        using (ctx)
        {
            var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.StatusBar>();

            var msg = new FeedbackRequestEvent(FeedbackType.Navigation, "Focus on chart area.", true);
            cut.InvokeAsync(() => bus.Publish(msg));

            cut.WaitForAssertion(() =>
                Assert.Equal("Focus on chart area.", cut.Find("[role='status']").TextContent.Trim()));
        }
    }

    /// <summary>
    /// The paper badge stays OUTSIDE the region, which is the half of this contract that was
    /// already fixed once. Asserted here so the two halves cannot drift apart again — putting
    /// the label back next to the badge is how the first fix would be undone.
    /// </summary>
    [Fact]
    public void The_paper_badge_is_not_inside_the_live_region()
    {
        var (ctx, bus) = NewContext();
        using (ctx)
        {
            var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.StatusBar>();
            cut.InvokeAsync(() => bus.Publish(new PaperModeToggledEvent(true)));

            cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".paper-indicator")));
            Assert.DoesNotContain("PAPER", cut.Find("[role='status']").TextContent);
        }
    }
}
