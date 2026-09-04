// SettingsModal — bUnit coverage. The largest of the four big modals; injects
// 10 services. Coverage focuses on the regression-prone surface that is left after the
// 2026-09-04 alerts consolidation: tab navigation.
//
// The per-channel "Send test" flow and the alerts.* persistence path used to live here.
// They moved with the markup, to AlertDeliverySettingsTests — the Alerts tab is now a panel
// inside the Alt+J alerts dialog. Tests were MOVED rather than rewritten: what they assert
// (a test send routes to the right channel; a misconfigured channel is not called; the
// settings are written before the send) is unchanged by which dialog the fields sit in.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Theming;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests.Blazor;

public class SettingsModalTests
{
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.SettingsModal>
        OpenSettings(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.SettingsModal>(
            bus => bus.Publish(new OpenSettingsEvent()));

    [Fact]
    public void SettingsModal_HiddenByDefault_RendersEmpty()
    {
        using var h = new BlazorTestHarness();

        var cut = h.Ctx.RenderComponent<AccessibleTrader.BlazorClient.Components.SettingsModal>();

        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void SettingsModal_OpenViaEvent_RendersDialog()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenSettings(h);

        var dialog = cut.Find("[role='dialog']");
        Assert.Equal("settings-title", dialog.GetAttribute("aria-labelledby"));
    }

    // ── Keyboard reachability of the tab row ─────────────────────────────
    //
    // The markup sets a roving tabindex (0 on the active tab, -1 on the rest), which
    // promises the browser that arrows move within the group. Without a handler, five
    // of the then six tabs were mouse-only — including the whole keyboard-rebinding UI
    // and the paper-account reset button. EIGHT tabs as of 2026-09-04: General, Speech,
    // Narration, Sonification, Appearance, Keyboard, License, About. It was seven for part
    // of that day — the Alerts tab's delivery channels moved into the Alt+J dialog next to
    // the alerts they deliver, and Narration arrived in the same release to hold what the
    // terminal says when the user pressed nothing.

    private static void ArrowOnTabList(
        IRenderedComponent<AccessibleTrader.BlazorClient.Components.SettingsModal> cut, string key)
    {
        // Deliberately the synchronous KeyDown, and deliberately NOT
        // KeyDownAsync(...).GetAwaiter().GetResult(): OnTabListKeyDown awaits Task.Yield(),
        // whose continuation needs the renderer dispatcher, and blocking the calling thread
        // inside the dispatch deadlocks bUnit outright (verified — the run never returns).
        // The waiting is done at the assertion instead; see BlazorTestHarness.WaitForFocus.
        cut.Find("[role='tablist']")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key });
    }

    private static void AssertSelected(
        IRenderedComponent<AccessibleTrader.BlazorClient.Components.SettingsModal> cut, string id)
    {
        cut.WaitForAssertion(() =>
            Assert.Equal("true", cut.Find($"button#{id}").GetAttribute("aria-selected")));
    }

    [Fact]
    public void SettingsModal_RightArrow_MovesToTheNextTab()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        ArrowOnTabList(cut, "ArrowRight");

        AssertSelected(cut, "tab-speech");
    }

    [Fact]
    public void SettingsModal_ArrowKeys_ReachEverySettingsTab()
    {
        // The actual user-facing claim, stated as a walk rather than as a single step:
        // starting at General, seven Right presses visit all eight tabs in order.
        // Narration joined between Speech and Sonification on 2026-09-04.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        foreach (var id in new[] { "tab-speech", "tab-narration", "tab-sonification", "tab-appearance", "tab-keyboard", "tab-license", "tab-about" })
        {
            ArrowOnTabList(cut, "ArrowRight");
            AssertSelected(cut, id);
        }
    }

    [Fact]
    public void SettingsModal_ArrowsWrapAtBothEnds()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        // Left from the first tab lands on the last.
        ArrowOnTabList(cut, "ArrowLeft");
        AssertSelected(cut, "tab-about");

        // ...and Right from the last comes back to the first.
        ArrowOnTabList(cut, "ArrowRight");
        AssertSelected(cut, "tab-general");
    }

    [Fact]
    public void SettingsModal_HomeAndEnd_JumpToTheFirstAndLastTab()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        ArrowOnTabList(cut, "End");
        AssertSelected(cut, "tab-about");

        ArrowOnTabList(cut, "Home");
        AssertSelected(cut, "tab-general");
    }

    [Fact]
    public void SettingsModal_ArrowNavigation_MovesFocusOntoTheTabItSelects()
    {
        // Selection follows focus in a tablist. Selecting a tab without moving focus
        // leaves a screen reader announcing the tab the user is standing on rather than
        // the one that just became active — the state and the announcement disagree.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        ArrowOnTabList(cut, "ArrowRight");

        h.WaitForFocus("tab-speech");
    }

    // ── The 2026-09-03 restructure ───────────────────────────────────────

    [Fact]
    public void SettingsModal_SpeechAndSonificationHaveTheirOwnTabs_AndTheWasapiFieldIsGone()
    {
        // Speech and sonification used to be two fieldsets in a 440-line General panel that
        // also held paper trading inside the SPEECH fieldset. The WASAPI latency number was
        // read by nothing in the audio stack — a control that saved, persisted and changed
        // nothing — so it went with the move. The negatives are read after the positives
        // hold, so they cannot pass on a panel that has not rendered.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        Assert.NotNull(cut.Find("#tabpanel-speech #s-speech-enabled"));
        Assert.NotNull(cut.Find("#tabpanel-speech #s-read-headers"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-sonif-enabled"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-sound-theme"));
        Assert.NotNull(cut.Find("#tabpanel-general #s-quick-sizing"));
        Assert.Empty(cut.FindAll("#s-wasapi"));
        Assert.Empty(cut.FindAll("#tabpanel-general #s-speech-enabled"));
    }

    [Fact]
    public void SettingsModal_BothPatternToggles_AreOnGeneral_AndNameWhichPatternsTheyMean()
    {
        // Two different analyses, and until 2026-09-04 only one of them had a switch: candle
        // patterns (one to three bars) were spoken unconditionally while the Narration tab's
        // help text promised them in a sentence nothing could make false. They sit next to
        // each other, on General under Analysis, because that adjacency is what makes the
        // distinction legible — and because neither belongs to a single trigger: each changes
        // the arrow keys AND the bar close AND playback.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        Assert.NotNull(cut.Find("#tabpanel-general #s-chart-patterns"));
        Assert.NotNull(cut.Find("#tabpanel-general #s-candle-patterns"));
        Assert.Equal("Describe chart patterns", cut.Find("label[for='s-chart-patterns']").TextContent.Trim());
        Assert.Equal("Describe candle patterns", cut.Find("label[for='s-candle-patterns']").TextContent.Trim());

        // Neither may be left behind on Speech or Narration: two controls writing one
        // preference is the shape that produced the alerts-tab duplicate in the twelfth pass.
        Assert.Empty(cut.FindAll("#tabpanel-speech #s-chart-patterns"));
        Assert.Empty(cut.FindAll("#tabpanel-narration #s-candle-patterns"));
    }

    [Fact]
    public void SettingsModal_TheNewBarHint_DoesNotPromiseAPatternUnconditionally()
    {
        // The defect Cody reported: "on new bar announcements the candle pattern is announced,
        // but this isn't always true". Help text that states an outcome a setting can withdraw
        // has to name the setting.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        var hint = cut.Find("#s-announce-bars-hint").TextContent;
        Assert.Contains("Describe candle patterns", hint);
    }

    [Fact]
    public void SettingsModal_Appearance_IsOneThemePanel_TheColourOverridesAreGone()
    {
        // The chart background, its gradient, the window fade and the up/down pair were
        // app-level colours layered over every theme; they are fields of a theme now. A
        // control left here would write a key nothing reads.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        Assert.NotNull(cut.Find("#tabpanel-appearance select#s-theme"));
        Assert.NotNull(cut.Find("#tabpanel-appearance button#s-theme-new"));
        Assert.NotNull(cut.Find("#tabpanel-appearance button#s-theme-clone"));
        Assert.NotNull(cut.Find("#tabpanel-appearance button#s-theme-edit"));
        Assert.NotNull(cut.Find("#tabpanel-appearance #s-visual-earcons"));
        foreach (var gone in new[] { "#s-bg-color", "#s-bg-gradient", "#s-unified-gradient", "#s-bull-color", "#s-bear-color" })
            Assert.Empty(cut.FindAll(gone));
    }

    [Fact]
    public void SettingsModal_EditTheme_IsGatedNotDisabled_OnABuiltInTheme()
    {
        // A built-in cannot be edited in place. `disabled` would delete the button for a
        // screen-reader user; the gate keeps it, names it, and says why — and pressing it
        // speaks the reason on the Boundary channel instead of opening nothing.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);
        var spoken = new List<FeedbackRequestEvent>();
        h.EventBus.Subscribe<FeedbackRequestEvent>(spoken.Add);
        var opened = new List<OpenThemeEditorEvent>();
        h.EventBus.Subscribe<OpenThemeEditorEvent>(opened.Add);

        var edit = cut.Find("button#s-theme-edit");
        Assert.Equal("true", edit.GetAttribute("aria-disabled"));
        Assert.Null(edit.GetAttribute("disabled"));
        string reasonId = edit.GetAttribute("aria-describedby")!.Split(' ')[0];
        Assert.Contains("Built-in themes can't be edited. Use Clone", cut.Find("#" + reasonId).TextContent);

        edit.Click();

        cut.WaitForAssertion(() => Assert.Contains(spoken, e =>
            e.Type == FeedbackType.Boundary && e.Message!.Contains("Use Clone")));
        Assert.Empty(opened);
    }

    private static ThemePreset SelectCustomTheme(BlazorTestHarness h)
    {
        var mine = ThemePreset.Create("Mine", ThemeType.Paper).With("chartTop", new SKColor(0x12, 0x34, 0x56));
        var library = h.Ctx.Services.GetRequiredService<AccessibleTrader.Core.Services.Theming.IThemeLibrary>();
        library.GetById(mine.Id).Returns(mine);
        library.All.Returns(new List<ThemePreset> { mine });
        h.SettingsManager.GetSetting(SettingsKeys.CustomThemeId, Arg.Any<JToken?>()).Returns(new JValue(mine.Id));
        return mine;
    }

    [Fact]
    public void SettingsModal_NewCloneEdit_OpenTheEditorInTheirOwnModes()
    {
        using var h = new BlazorTestHarness();
        var mine = SelectCustomTheme(h);
        var cut = OpenSettings(h);
        var opened = new List<OpenThemeEditorEvent>();
        h.EventBus.Subscribe<OpenThemeEditorEvent>(opened.Add);

        Assert.Equal($"custom:{mine.Id}", cut.Find("select#s-theme").GetAttribute("value"));
        var edit = cut.Find("button#s-theme-edit");
        Assert.Null(edit.GetAttribute("aria-disabled"));
        // With the gate open no aria-describedby is emitted, so the title is the description —
        // an aria-describedby resolving to "" would have suppressed it (measured 2026-09-03).
        Assert.Null(edit.GetAttribute("aria-describedby"));
        Assert.Equal("Change Mine in place", edit.GetAttribute("title"));

        cut.Find("button#s-theme-new").Click();
        cut.Find("button#s-theme-clone").Click();
        cut.Find("button#s-theme-edit").Click();

        cut.WaitForAssertion(() => Assert.Equal(3, opened.Count));
        Assert.Equal(ThemeEditorMode.New,   opened[0].Mode);
        Assert.Equal(ThemeEditorMode.Clone, opened[1].Mode);
        Assert.Equal(mine.Id,               opened[1].PresetId);
        Assert.Equal(ThemeEditorMode.Edit,  opened[2].Mode);
        Assert.Equal(mine.Id,               opened[2].PresetId);
    }

    [Fact]
    public void SettingsModal_CloneOfABuiltIn_NamesTheBuiltIn()
    {
        // The picker shows Classic (the shipped default); Clone must copy THAT, not whatever
        // the service last had — the picker and the service agree on open, so this pins the
        // base type reaching the editor.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);
        var opened = new List<OpenThemeEditorEvent>();
        h.EventBus.Subscribe<OpenThemeEditorEvent>(opened.Add);

        cut.Find("button#s-theme-clone").Click();

        cut.WaitForAssertion(() => Assert.Single(opened));
        Assert.Equal(ThemeEditorMode.Clone, opened[0].Mode);
        Assert.Null(opened[0].PresetId);
        Assert.Equal(ThemeService.DefaultTheme, opened[0].BaseTheme);
    }

    [Fact]
    public void SettingsModal_DeleteTheme_MovesFocusToThePicker()
    {
        // The Delete button renders only while a custom theme is selected, so it vanishes on
        // the render its own click causes; without an explicit move focus falls to <body>,
        // outside the aria-modal dialog. The picker shows the theme that took over.
        using var h = new BlazorTestHarness();
        SelectCustomTheme(h);
        var cut = OpenSettings(h);

        cut.Find("button#s-theme-delete").Click();

        h.WaitForFocus("s-theme");
    }

    [Fact]
    public void SettingsModal_SearchResults_NameOnlyControlsThatRender()
    {
        // Seven registry rows point at controls under an @if. On this harness the speech-output
        // picker (browser-TTS heads only) is one of them: a result for it would switch tab and
        // drop focus on the body. Every result's target must be in the DOM of THIS build.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        cut.Find("input#s-search").Input("e");   // matches most rows

        cut.WaitForAssertion(() =>
        {
            var results = cut.FindAll("[data-control-id]");
            Assert.True(results.Count > 20, $"only {results.Count} results — the search did not run");
            foreach (var r in results)
            {
                string id = r.GetAttribute("data-control-id")!;
                Assert.True(cut.FindAll("#" + id).Count == 1, $"search offers {id}, which this build does not render");
            }
        });
        Assert.Empty(cut.FindAll("[data-control-id='s-speech-output']"));
        Assert.Empty(cut.FindAll("#s-speech-output"));
    }

    [Fact]
    public void SettingsModal_ThemePicker_FollowsTheEditorsSave()
    {
        // Press New, save in the editor, and focus returns to this dialog: the picker must
        // show the theme now in use and Edit must be available on it. Before 2026-09-03 the
        // service change only refreshed three colour pickers, so the select still named the
        // old theme and the gated Edit refused the user's own theme (design review, finding 2).
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);
        Assert.Equal("true", cut.Find("button#s-theme-edit").GetAttribute("aria-disabled"));

        var mine = SelectCustomTheme(h);   // the library and settings now answer for it …
        h.Ctx.Services.GetRequiredService<AccessibleTrader.Core.Services.ThemeService>()
         .SetCustomTheme(mine);            // … and the editor's "Save and use" applies it

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"custom:{mine.Id}", cut.Find("select#s-theme").GetAttribute("value"));
            Assert.Null(cut.Find("button#s-theme-edit").GetAttribute("aria-disabled"));
        });
    }

    [Fact]
    public void SettingsModal_UnrelatedKeys_LeaveTheActiveTabAlone()
    {
        // The handler must claim arrows and nothing else. Claiming Tab would trap focus
        // inside the tab row, which is a worse bug than the one being fixed.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        foreach (var key in new[] { "Tab", "Enter", " ", "a", "Escape" })
            ArrowOnTabList(cut, key);

        AssertSelected(cut, "tab-general");
    }

    [Fact]
    public void SettingsModal_DefaultActiveTab_IsGeneral()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenSettings(h);

        var generalTab = cut.Find("button#tab-general");
        Assert.Equal("true", generalTab.GetAttribute("aria-selected"));
    }
    // ── Save and Cancel ──────────────────────────────────────────────────
    //
    // Until 2026-09-04 the footer held one button reading "Close", and closing is what
    // COMMITTED: the button saved, Escape saved, the backdrop saved. Two keystrokes away,
    // PropertiesModal has the identical wiring and DISCARDS on Escape. Cody's rule, and now the
    // app's: Escape closes and discards; a dialog that can save says so on a button.

    /// <summary>
    /// Escape must not write. Asserted through the store dispatch, which is where every pending
    /// speech and narration preference goes, because that is the thing the old code did on the
    /// way out — a "the dialog closed" assertion passes identically either way.
    /// </summary>
    [Fact]
    public void SettingsModal_Escape_ClosesWithoutWritingAnything()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);
        h.WorkspaceStore.ClearReceivedCalls();

        h.EventBus.Publish(new CloseTopModalEvent("Settings"));

        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Markup.Trim()));
        h.WorkspaceStore.DidNotReceive().Dispatch(Arg.Any<UpdateSettingsAction>());
    }

    /// <summary>
    /// The positive half, and it has to be here: a Cancel that writes nothing is trivially
    /// satisfied by a dialog that writes nothing at all.
    /// </summary>
    [Fact]
    public void SettingsModal_Save_WritesThePendingPreferences_AndCloses()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);
        h.WorkspaceStore.ClearReceivedCalls();

        cut.InvokeAsync(() => cut.Find("#settings-save").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => Assert.Equal(string.Empty, cut.Markup.Trim()));
        h.WorkspaceStore.Received().Dispatch(Arg.Any<UpdateSettingsAction>());
    }

    [Fact]
    public void SettingsModal_TheFooterOffersSaveAndCancel_NotAnImpliedSaveCalledClose()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        var footer = cut.Find("div.modal-footer");
        var labels = footer.QuerySelectorAll("button")
            .Select(b => b.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Cancel", "Save" }, labels);
    }

    [Fact]
    public void SettingsModal_TheProfileButtons_SitOnTheTabWhoseSettingsTheyWrite()
    {
        // They were in a "Settings Profiles" box on General, exporting settings that belong to
        // two other tabs (Cody, 2026-09-04).
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        Assert.NotNull(cut.Find("#tabpanel-appearance #s-visual-profile-export"));
        Assert.NotNull(cut.Find("#tabpanel-appearance #s-visual-profile-import"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-audio-profile-export"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-audio-profile-import"));
        Assert.Empty(cut.FindAll("#tabpanel-general #s-visual-profile-export"));
        Assert.Empty(cut.FindAll("#tabpanel-general #s-audio-profile-export"));
    }

}
