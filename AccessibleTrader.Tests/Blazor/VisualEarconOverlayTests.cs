// VisualEarconOverlay — bUnit coverage. Opt-in visual channel for earcons
// (Phase D): DEFAULT OFF (audio-first terminal), badge appears only when the
// accessibility.visualEarcons setting is on, one fade per event (no strobing).

using AccessibleTrader.Core.Models;
using Bunit;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class VisualEarconOverlayTests
{
    private static (BlazorTestHarness h, IRenderedComponent<AccessibleTrader.BlazorClient.Components.VisualEarconOverlay> cut)
        Render(bool settingEnabled)
    {
        var h = new BlazorTestHarness();
        h.SettingsManager.GetSetting("accessibility.visualEarcons", Arg.Any<JToken?>())
            .Returns(settingEnabled ? new JValue(true) : null);
        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.VisualEarconOverlay>();
        return (h, cut);
    }

    [Fact]
    public void DefaultOff_NoBadgeAppears_WhenAnEarconFires()
    {
        var (h, cut) = Render(settingEnabled: false);
        using var _ = h;

        h.EventBus.Publish(new EarconVisualEvent("Buy order filled", "positive"));

        Assert.Empty(cut.FindAll(".visual-earcon"));
    }

    [Fact]
    public void OptedIn_BadgeShowsTheEventLabel_WithToneAccent()
    {
        var (h, cut) = Render(settingEnabled: true);
        using var _ = h;

        h.EventBus.Publish(new EarconVisualEvent("Stop loss hit", "alert"));

        cut.WaitForAssertion(() =>
        {
            var badge = cut.Find(".visual-earcon");
            Assert.Contains("Stop loss hit", badge.TextContent);
            Assert.Contains("tone-alert", badge.ClassName);
            // Purely the visual channel — audio/speech remain the accessible source.
            Assert.Equal("true", badge.GetAttribute("aria-hidden"));
        });
    }

    [Fact]
    public void UnknownTone_FallsBackToNeutralAccent()
    {
        var (h, cut) = Render(settingEnabled: true);
        using var _ = h;

        h.EventBus.Publish(new EarconVisualEvent("Something", "chartreuse"));

        cut.WaitForAssertion(() =>
            Assert.Contains("tone-neutral", cut.Find(".visual-earcon").ClassName));
    }

    [Fact]
    public void NewEvent_ReplacesTheCurrentBadge_InsteadOfStackingFlashes()
    {
        // WCAG 2.3.1 by construction: one badge at a time, one fade each.
        var (h, cut) = Render(settingEnabled: true);
        using var _ = h;

        h.EventBus.Publish(new EarconVisualEvent("First", "neutral"));
        h.EventBus.Publish(new EarconVisualEvent("Second", "positive"));

        cut.WaitForAssertion(() =>
        {
            var badges = cut.FindAll(".visual-earcon");
            Assert.Single(badges);
            Assert.Contains("Second", badges[0].TextContent);
        });
    }
}
