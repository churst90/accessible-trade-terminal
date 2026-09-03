// The theme editor refuses to save a theme whose text cannot be read.
//
// Until 2026-09-02 its "Hard to read" box was fed by squared Euclidean RGB distance against a
// threshold of 12,000, which said nothing about #0000ff on #000000 (distance 65,025; 2.44:1),
// and nothing stopped Save either way. Now the box is fed by ThemeContrastChecks — the WCAG
// ratio — and a text pair below 4.5:1 keeps the dialog open and reads the number out.

using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Theming;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class ThemeEditorContrastGateTests
{
    private static IRenderedComponent<ThemeEditorModal> Open(BlazorTestHarness h) =>
        h.OpenModal<ThemeEditorModal>(b => b.Publish(new OpenThemeEditorEvent()));

    private static void SetDialogText(IRenderedComponent<ThemeEditorModal> cut, string hex) =>
        cut.Find("input[aria-label='Dialog text hex value']").Change(hex);

    private static void Save(IRenderedComponent<ThemeEditorModal> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save and use")).Click();

    [Fact]
    public void Dialog_text_below_the_floor_is_reported_with_its_ratio_and_stops_save()
    {
        using var h = new BlazorTestHarness();
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();
        var cut = Open(h);

        // Steel Gray's dialog surface is #2B2F36; the audit's headline blue is 1.56:1 on it.
        SetDialogText(cut, "#0000ff");

        // WaitForAssertion, not a bare Find: `Change` queues its handler on the renderer's
        // dispatcher and the re-render can land after the next statement. Read too early, the
        // box still holds the theme's own standing advisory — Steel Gray's falling candle at
        // 1.48:1 — and the failure message reads "Hard to read: The fal…", which looks like the
        // gate reporting the wrong pair rather than the test reading it too soon. Cost the CI
        // run on e2d47c6a and two local full-suite runs before that; green in isolation every
        // time, which is the signature.
        cut.WaitForAssertion(() =>
            Assert.Contains("Dialog text is only 1.56:1", cut.Find("#te-contrast").TextContent));
        var box = cut.Find("#te-contrast");
        Assert.Contains("4.50:1 is needed", box.TextContent);
        Assert.Contains("Fix this before saving", box.TextContent);

        // The refusal is discoverable at the field, not only after Save: both dialog-text inputs
        // are marked invalid and described by the problem box.
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find("#te-dialogText").GetAttribute("aria-invalid")));
        foreach (var input in new[] { cut.Find("#te-dialogText"), cut.Find("input[aria-label='Dialog text hex value']") })
        {
            Assert.Equal("true", input.GetAttribute("aria-invalid"));
            Assert.Contains("te-contrast", input.GetAttribute("aria-describedby"));
        }
        Assert.Equal("false", cut.Find("#te-textPrimary").GetAttribute("aria-invalid"));

        Save(cut);

        library.DidNotReceive().Upsert(Arg.Any<ThemePreset>());
        cut.WaitForAssertion(() => Assert.StartsWith("Not saved.",
            cut.Find("[role='status'][aria-live='polite']:not(#te-contrast)").TextContent));
        var status = cut.Find("[role='status'][aria-live='polite']:not(#te-contrast)").TextContent;
        Assert.Contains("Dialog text is only 1.56:1", status);
        Assert.NotEmpty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void Dialog_text_at_the_floor_saves_and_a_graphic_below_it_only_warns()
    {
        using var h = new BlazorTestHarness();
        var library = h.Ctx.Services.GetRequiredService<IThemeLibrary>();
        var cut = Open(h);

        // A rising candle at #4E545E on Steel Gray's #4E545E chart top is 1.00:1 — a graphic
        // below the floor, which is reported and is NOT a reason to refuse the save.
        cut.Find("input[aria-label='Rising candle hex value']").Change("#4e545e");
        // The positive assertion waits; the negative one is checked only AFTER it holds.
        // A DoesNotContain inside a WaitForAssertion passes on the first poll and proves
        // nothing — it is satisfied by a render that has not happened yet.
        cut.WaitForAssertion(() =>
            Assert.Contains("The rising candle is only 1.00:1", cut.Find("#te-contrast").TextContent));
        Assert.DoesNotContain("Fix this before saving", cut.Find("#te-contrast").TextContent);

        SetDialogText(cut, "#ffffff");
        cut.WaitForAssertion(() =>
            Assert.Equal("false", cut.Find("#te-dialogText").GetAttribute("aria-invalid")));
        Save(cut);

        library.Received(1).Upsert(Arg.Is<ThemePreset>(p => p.Overrides.ContainsKey("dialogText")));
    }

    [Fact]
    public void A_built_in_theme_opens_with_no_blocking_problem()
    {
        // The editor starts from whatever theme is on screen; the default must not open on a
        // problem the person did not create. (ThemeCoverageTests holds this for every theme;
        // this is the same fact seen through the dialog.)
        using var h = new BlazorTestHarness();
        var cut = Open(h);

        var box = cut.Find("#te-contrast").TextContent;
        Assert.DoesNotContain("Fix this before saving", box);
        Assert.Empty(cut.FindAll("[aria-invalid='true']"));
    }
}
