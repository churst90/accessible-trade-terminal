using AccessibleTrader.BlazorClient.Components;
using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services.Theming;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The theme editor's three ways in — New, Clone, Edit — and the window blend that moved here
/// from Settings on 2026-09-03.
///
/// <para>
/// Before this pass the Appearance tab offered "New theme" (which actually cloned the theme on
/// screen) and "Customise…" (which edited a custom theme in place, or cloned when none was
/// selected), and the app-level background, gradient, window-fade and up/down colours were
/// layered over every theme from Settings. Cody's decision: one Theme panel with New, Clone and
/// Edit that mean what they say, and colours that belong to a theme.
/// </para>
/// </summary>
public class ThemeEditorModeTests
{
    private static IThemeLibrary Library(BlazorTestHarness h) =>
        h.Ctx.Services.GetRequiredService<IThemeLibrary>();

    private static IRenderedComponent<ThemeEditorModal> Open(BlazorTestHarness h, OpenThemeEditorEvent e)
    {
        h.Ctx.Services.GetRequiredService<AccessibleTrader.Core.Services.ThemeService>()
         .SetTheme(ThemeType.SteelGray);
        return h.OpenModal<ThemeEditorModal>(b => b.Publish(e));
    }

    private static string Field(IRenderedComponent<ThemeEditorModal> cut, string key) =>
        cut.Find($"input#te-{key}").GetAttribute("value")!;

    private static string Heading(IRenderedComponent<ThemeEditorModal> cut) =>
        cut.Find("#theme-editor-title").TextContent.Trim();

    private static void Save(IRenderedComponent<ThemeEditorModal> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains("Save and use")).Click();

    // The override is the crosshair, an overlay no contrast pair blocks on — a dark CHART TOP
    // on Paper's light text would stop Save at the gate, and these tests are about identity,
    // not contrast (ThemeEditorContrastGateTests owns that).
    private static ThemePreset Mine() =>
        ThemePreset.Create("Mine", ThemeType.Paper).With("crosshair", new SKColor(0x12, 0x34, 0x56));

    [Fact]
    public void New_starts_from_the_safe_scheme_not_from_the_theme_on_screen()
    {
        // Black chart, pure green rising, pure red falling — Cody's 000000 / ff0000 / 00ff00.
        // The theme on screen is Steel Gray, whose chart top is NOT black; a "New" that started
        // from it would be the old "New theme" button under a new name.
        using var h = new BlazorTestHarness();

        var cut = Open(h, new OpenThemeEditorEvent(Mode: ThemeEditorMode.New));

        Assert.Equal("New theme", Heading(cut));
        Assert.Equal("#000000", Field(cut, "chartTop"));
        Assert.Equal("#00ff00", Field(cut, "bullBody"));
        Assert.Equal("#ff0000", Field(cut, "bearBody"));
        Assert.Equal("New theme", cut.Find("input#te-name").GetAttribute("value"));
    }

    [Fact]
    public void Clone_of_a_custom_theme_keeps_its_colours_under_a_new_id_and_name()
    {
        using var h = new BlazorTestHarness();
        var mine = Mine();
        Library(h).GetById(mine.Id).Returns(mine);

        var cut = Open(h, new OpenThemeEditorEvent(mine.Id, ThemeEditorMode.Clone));

        Assert.Equal("Clone theme: Mine", Heading(cut));
        Assert.Equal("Mine copy", cut.Find("input#te-name").GetAttribute("value"));
        Assert.Equal("#123456", Field(cut, "crosshair"));

        Save(cut);
        cut.WaitForAssertion(() =>
            Library(h).Received(1).Upsert(Arg.Is<ThemePreset>(p =>
                p.Id != mine.Id && p.Name == "Mine copy" && p.BasedOn == ThemeType.Paper
                && p.Overrides["crosshair"] == "#123456")));
    }

    [Fact]
    public void Clone_of_a_built_in_starts_on_that_base_not_the_one_on_screen()
    {
        using var h = new BlazorTestHarness();

        var cut = Open(h, new OpenThemeEditorEvent(Mode: ThemeEditorMode.Clone, BaseTheme: ThemeType.Paper));

        Assert.Equal("Clone theme: Paper", Heading(cut));
        Assert.Equal("Paper copy", cut.Find("input#te-name").GetAttribute("value"));
        Assert.Equal("Paper", cut.Find("select#te-base").GetAttribute("value"));
    }

    [Fact]
    public void Edit_saves_over_the_same_id()
    {
        // Edit is in place: Save must REPLACE the theme, not file a second one beside it.
        using var h = new BlazorTestHarness();
        var mine = Mine();
        Library(h).GetById(mine.Id).Returns(mine);

        var cut = Open(h, new OpenThemeEditorEvent(mine.Id, ThemeEditorMode.Edit));

        Assert.Equal("Edit theme: Mine", Heading(cut));
        Assert.Equal("Mine", cut.Find("input#te-name").GetAttribute("value"));

        Save(cut);
        cut.WaitForAssertion(() =>
            Library(h).Received(1).Upsert(Arg.Is<ThemePreset>(p => p.Id == mine.Id && p.Name == "Mine")));
    }

    [Fact]
    public void Blend_writes_the_six_band_colours_as_slices_of_one_fade()
    {
        // The arithmetic is UnifiedGradient.Apply's — the same the retired Settings switch used —
        // and the result is six ordinary overrides the pickers show and Revert can undo.
        using var h = new BlazorTestHarness();
        var cut = Open(h, new OpenThemeEditorEvent(Mode: ThemeEditorMode.New));
        var top = new SKColor(0x90, 0x90, 0x90);
        var bottom = new SKColor(0x10, 0x10, 0x10);

        // Driven through the hex text twins — the keyboard path a screen-reader user has.
        cut.Find("input[aria-label='Top of window hex value']").Change("#909090");
        cut.Find("input[aria-label='Bottom of window hex value']").Change("#101010");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Apply blend")).Click();

        string Hex(SKColor c) => ThemePreset.ToHex(c)[..7];
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("#909090", Field(cut, "topBar"));
            Assert.Equal(Hex(UnifiedGradient.Lerp(top, bottom, UnifiedGradient.ChartTopStop)), Field(cut, "topBarEnd"));
            Assert.Equal(Hex(UnifiedGradient.Lerp(top, bottom, UnifiedGradient.ChartTopStop)), Field(cut, "chartTop"));
            Assert.Equal(Hex(UnifiedGradient.Lerp(top, bottom, UnifiedGradient.ChartBottomStop)), Field(cut, "chartBottom"));
            Assert.Equal(Hex(UnifiedGradient.Lerp(top, bottom, UnifiedGradient.ChartBottomStop)), Field(cut, "bottomBar"));
            Assert.Equal("#101010", Field(cut, "bottomBarEnd"));
            Assert.Contains("Blended", cut.Find("[role='status']").TextContent);
        });
    }
}
