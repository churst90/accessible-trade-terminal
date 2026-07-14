// SettingsModal — bUnit coverage. The largest of the four big modals; injects
// 10 services. Coverage focuses on the regression-prone surfaces:
// tab navigation, the per-channel "Send test" flow on the Alerts tab, and the
// Settings.SetSetting persistence path that PersistAlertSettings invokes
// before each test send.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Alerts;
using Bunit;
using Newtonsoft.Json.Linq;
using NSubstitute;

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

        cut.Find("button[aria-label='Send a test email alert']").Click();

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
        cut.Find("button[aria-label='Send a test email alert']").Click();

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
        cut.Find("button[aria-label='Send a test email alert']").Click();

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
        cut.Find("button[aria-label='Send a test email alert']").Click();

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
        cut.Find("button[aria-label='Send a test Telegram alert']").Click();

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
        cut.Find("button[aria-label='Send a test email alert']").Click();

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
