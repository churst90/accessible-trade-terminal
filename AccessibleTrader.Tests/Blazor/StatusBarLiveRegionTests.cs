using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The status strip shows the last spoken sentence — and does not announce it.
///
/// <para><b>Defect one (fixed 2026-09-03).</b> A static
/// <c>&lt;span class="status-label"&gt;Last Feedback:&lt;/span&gt;</c> sat inside the strip's
/// live region. A live region is announced by its WHOLE content on every change, so the label
/// was read before every sentence the application has ever spoken — Cody reported hearing
/// "last feedback, focus on chart area" where "Focus on chart area" was the entire point.</para>
///
/// <para><b>Defect two, which the first fix exposed.</b> Deleting the label made this strip's
/// text IDENTICAL to the assertive speech buffers' text, and the strip was itself a
/// <c>role="status" aria-live="polite"</c> region — a second announcer for one sentence. Every
/// screen reader suppresses a live-region message that duplicates what it just queued, so one
/// copy was always discarded; and when the polite copy reached the accessibility bus FIRST
/// (measured on 6 of 16 presses) the assertive copy purged it and was then dropped as a
/// duplicate of the very message it had purged — the sentence was spoken NEITHER time. That is
/// the intermittent silence after m, h and the nudge keys. <b>The strip is a visual mirror; the
/// speech double-buffer is the announcing channel.</b> A blind user reaches this text by
/// landmark navigation and browse mode, neither of which needs aria-live.</para>
///
/// <para>The assertions are on the region's exact text and on the ABSENCE of any live-region
/// attribute rather than on the string "Last Feedback": a label reworded to "Status:" would be
/// the identical first defect, and <c>role="log"</c> would be the identical second one.</para>
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
                Assert.Equal("Focus on chart area.", cut.Find(".status-content").TextContent.Trim()));
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
            Assert.DoesNotContain("PAPER", cut.Find(".status-content").TextContent);
        }
    }

    /// <summary>
    /// The rendered strip declares no live region anywhere — not on the landmark, not on the
    /// content div, not on the badge. Asserted over the RENDERED markup rather than the source
    /// so that a live-region role arriving via a child component or a splatted attribute is
    /// caught too.
    /// </summary>
    [Fact]
    public void The_strip_declares_no_live_region_at_all()
    {
        var (ctx, bus) = NewContext();
        using (ctx)
        {
            var cut = ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.StatusBar>();
            var msg = new FeedbackRequestEvent(FeedbackType.Navigation, "Focus on chart area.", true);
            cut.InvokeAsync(() => bus.Publish(msg));
            cut.WaitForAssertion(() =>
                Assert.Equal("Focus on chart area.", cut.Find(".status-content").TextContent.Trim()));

            Assert.Empty(cut.FindAll("[aria-live]"));
            Assert.Empty(cut.FindAll("[role='status']"));
            Assert.Empty(cut.FindAll("[role='alert']"));
            Assert.Empty(cut.FindAll("[role='log']"));

            // …and the strip is still reachable, which is what makes dropping aria-live safe.
            // A mirror nobody can navigate to would be a worse trade than the duplicate was.
            var section = cut.Find("section.status-bar");
            Assert.Equal("Terminal status", section.GetAttribute("aria-label"));
        }
    }
}
