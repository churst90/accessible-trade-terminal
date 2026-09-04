// SettingsModal — bUnit coverage. The largest of the four big modals; injects
// 10 services. Coverage focuses on the regression-prone surfaces:
// tab navigation, the per-channel "Send test" flow on the Alerts tab, and the
// Settings.SetSetting persistence path that PersistAlertSettings invokes
// before each test send.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Theming;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SkiaSharp;

namespace AccessibleTrader.Tests.Blazor;

public class SettingsModalTests
{
    private sealed class StubAlertChannel : IAlertChannel
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool IsConfigured { get; set; } = true;
        public int SendCallCount { get; private set; }
        public AlertFired? LastSent { get; private set; }
        public Exception? ThrowOnSend { get; set; }

        public StubAlertChannel(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public Task SendAsync(AlertFired alert, CancellationToken ct = default)
        {
            SendCallCount++;
            LastSent = alert;
            if (ThrowOnSend != null) throw ThrowOnSend;
            return Task.CompletedTask;
        }
    }

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
    // and the paper-account reset button. Eight tabs since the 2026-09-03 restructure:
    // General, Speech, Sonification, Appearance, Keyboard, Alerts, License, About.

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
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        foreach (var id in new[] { "tab-speech", "tab-sonification", "tab-appearance", "tab-keyboard", "tab-alerts", "tab-license", "tab-about" })
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
        Assert.NotNull(cut.Find("#tabpanel-speech #s-chart-patterns"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-sonif-enabled"));
        Assert.NotNull(cut.Find("#tabpanel-sonification #s-sound-theme"));
        Assert.NotNull(cut.Find("#tabpanel-general #s-quick-sizing"));
        Assert.Empty(cut.FindAll("#s-wasapi"));
        Assert.Empty(cut.FindAll("#tabpanel-general #s-speech-enabled"));
    }

    [Fact]
    public void SettingsModal_ThePatternToggle_IsCalledDescribeChartPatterns()
    {
        // It gates the new-bar announcement's pattern outcome as well as arrow-key narration
        // now, so "while navigating" had become a false claim about its scope.
        using var h = new BlazorTestHarness();
        var cut = OpenSettings(h);

        Assert.Equal("Describe chart patterns", cut.Find("label[for='s-chart-patterns']").TextContent.Trim());
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

    [Fact]
    public void SettingsModal_ClickAlertsTab_SwitchesActiveSelection()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenSettings(h);

        cut.Find("button#tab-alerts").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true",  cut.Find("button#tab-alerts").GetAttribute("aria-selected"));
            Assert.Equal("false", cut.Find("button#tab-general").GetAttribute("aria-selected"));
        });
    }

    [Fact]
    public void SettingsModal_AlertsTabPanel_HiddenWhenNotActive()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenSettings(h);

        // The Alerts tabpanel exists but starts with hidden=true. Blazor
        // renders boolean hidden as the literal "hidden" attribute when
        // truthy.
        var alertsPanel = cut.Find("#tabpanel-alerts");
        Assert.True(alertsPanel.HasAttribute("hidden"));
    }

    [Fact]
    public void SettingsModal_AlertsTabPanel_VisibleAfterClickingAlertsTab()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            var alertsPanel = cut.Find("#tabpanel-alerts");
            Assert.False(alertsPanel.HasAttribute("hidden"));
        });
    }

    [Fact]
    public void SettingsModal_SendTestEmail_NoChannelRegistered_ReportsError()
    {
        using var h = new BlazorTestHarness();
        // Default harness has no IAlertChannel registered.

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();

        cut.Find("button[aria-label='Send test email alert']").Click();

        // The Email status <p role="status"> appears with the error message.
        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Channel not registered", cut.Markup);
        });
    }

    [Fact]
    public void SettingsModal_SendTestEmail_ChannelMisconfigured_ReportsMissingFields()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email") { IsConfigured = false };
        h.OverrideAlertChannels(email);

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();
        cut.Find("button[aria-label='Send test email alert']").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Required fields are missing", cut.Markup);
            Assert.Equal(0, email.SendCallCount);
        });
    }

    [Fact]
    public void SettingsModal_SendTestEmail_ChannelConfigured_InvokesSendAsync()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email");
        h.OverrideAlertChannels(email);

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();
        cut.Find("button[aria-label='Send test email alert']").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, email.SendCallCount);
            Assert.NotNull(email.LastSent);
            Assert.Contains("Test sent successfully", cut.Markup);
        });
    }

    [Fact]
    public void SettingsModal_SendTestEmail_ChannelThrows_ReportsErrorMessage()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email")
        {
            ThrowOnSend = new InvalidOperationException("SMTP refused"),
        };
        h.OverrideAlertChannels(email);

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();
        cut.Find("button[aria-label='Send test email alert']").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Send failed: SMTP refused", cut.Markup);
            Assert.Equal(1, email.SendCallCount); // tried once, threw, caught
        });
    }

    [Fact]
    public void SettingsModal_SendTestTelegram_RoutesToTelegramChannel()
    {
        using var h = new BlazorTestHarness();
        var email    = new StubAlertChannel("email",    "Email");
        var telegram = new StubAlertChannel("telegram", "Telegram");
        h.OverrideAlertChannels(email, telegram);

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();
        cut.Find("button[aria-label='Send test Telegram alert']").Click();

        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, telegram.SendCallCount);
            Assert.Equal(0, email.SendCallCount);
        });
    }

    [Fact]
    public void SettingsModal_TestSend_PersistsAlertSettingsBeforeSending()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email");
        h.OverrideAlertChannels(email);

        var cut = OpenSettings(h);
        cut.Find("button#tab-alerts").Click();
        cut.Find("button[aria-label='Send test email alert']").Click();

        // Production code calls PersistAlertSettings BEFORE SendTestAlertAsync,
        // which writes through every alerts.* key and then SaveSettings().
        // Verify the persistence side-effects fired.
        // Post-click renders can lag on starved CI runners; poll instead of
        // asserting a single frame (this class of test flaked 3 CI runs in a row).
        cut.WaitForAssertion(() =>
        {
            h.SettingsManager.Received().SetSetting("alerts.email.host",       Arg.Any<JToken>());
            h.SettingsManager.Received().SetSetting("alerts.telegram.botToken", Arg.Any<JToken>());
            h.SettingsManager.Received().SaveSettings();
        });
    }
}
