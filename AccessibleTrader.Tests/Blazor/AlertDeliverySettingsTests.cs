// The alert delivery panel — the Settings dialog's Alerts tab until 2026-09-04, now a view
// inside the Alt+J alerts dialog, reached by its "Delivery settings" button.
//
// Most of this file MOVED from SettingsModalTests rather than being written fresh: the test
// send routing to the right channel, a misconfigured channel not being called, and the
// alerts.* keys being written before a send are all claims about the panel, not about the
// dialog that happens to hold it. Moving them keeps the coverage attached to the code.
//
// The cases that are new are the two the move itself creates: reaching the panel at all, and
// the persistence model changing from save-on-Close to write-on-commit.

using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Sdk.Alerts;
using Bunit;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests.Blazor;

public class AlertDeliverySettingsTests
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

    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>
        OpenAlerts(BlazorTestHarness h) =>
        h.OpenModal<AccessibleTrader.BlazorClient.Components.AlertsModal>(
            bus => bus.Publish(new OpenAlertsEvent()));

    /// <summary>
    /// Opens the dialog and steps into the delivery panel, the way a user does.
    ///
    /// <para>
    /// The click and the find are wrapped in <c>InvokeAsync</c> together: two bUnit triggers
    /// back to back read state before the renderer dispatcher has run the first handler, and
    /// this repository has spent two flakes learning that (2026-09-03).
    /// </para>
    /// </summary>
    private static IRenderedComponent<AccessibleTrader.BlazorClient.Components.AlertsModal>
        OpenDeliveryPanel(BlazorTestHarness h)
    {
        var cut = OpenAlerts(h);
        cut.InvokeAsync(() => cut.Find("button#alerts-delivery-open").Click()).GetAwaiter().GetResult();
        cut.WaitForAssertion(() => cut.Find("#alerts-delivery-title"));
        return cut;
    }

    // ── Reaching the panel ───────────────────────────────────────────────────

    [Fact]
    public void AlertsDialog_OpensOnTheAlertList_NotOnDeliverySettings()
    {
        // Alt+J is "show me my alerts". The vacuity floor for every case below as well: if the
        // dialog rendered the delivery panel unconditionally, every assertion that follows a
        // click on "Delivery settings" would pass without the click doing anything.
        using var h = new BlazorTestHarness();

        var cut = OpenAlerts(h);

        Assert.Contains("Active Alerts", cut.Markup);
        Assert.Empty(cut.FindAll("#alerts-delivery-title"));
        Assert.Single(cut.FindAll("button#alerts-delivery-open"));
    }

    [Fact]
    public void DeliverySettingsButton_ShowsTheChannelsAndHidesTheAlertList()
    {
        using var h = new BlazorTestHarness();

        var cut = OpenDeliveryPanel(h);

        Assert.Single(cut.FindAll("#s-email-host"));
        Assert.Single(cut.FindAll("#s-tg-token"));
        Assert.Single(cut.FindAll("#s-setup-alerts"));
        Assert.DoesNotContain("Active Alerts", cut.Markup);
    }

    [Fact]
    public void BackToAlerts_ReturnsToTheList()
    {
        using var h = new BlazorTestHarness();
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button#alerts-delivery-back").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Active Alerts", cut.Markup);
            Assert.Empty(cut.FindAll("#s-email-host"));
        });
    }

    [Fact]
    public void TheDeliveryPanelHeadingIsFocusable()
    {
        // It is where focus goes on the way in, and an element with no tabindex silently
        // refuses focus — which would leave a screen-reader user in the delivery panel with
        // the dialog still saying "Alerts" and nothing announcing the change of view.
        using var h = new BlazorTestHarness();

        var cut = OpenDeliveryPanel(h);

        Assert.Equal("-1", cut.Find("#alerts-delivery-title").GetAttribute("tabindex"));
    }

    // ── Persistence: write-on-commit, not save-on-close ──────────────────────

    [Fact]
    public void EditingAField_PersistsImmediately()
    {
        // The behaviour change the move makes, and the reason for it: the Settings dialog
        // wrote these on Close, and Escape — how a keyboard user leaves a dialog, and how
        // ModalBase closes one — never called Close. A typed SMTP password was discarded with
        // nothing said about it.
        using var h = new BlazorTestHarness();
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("#s-email-host").Change("smtp.example.com")).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            h.SettingsManager.Received().SetSetting("alerts.email.host",
                Arg.Is<JToken>(t => t.ToString() == "smtp.example.com"));
            h.SettingsManager.Received().SaveSettings();
        });
    }

    [Fact]
    public void NothingIsPersistedBeforeAnEditIsMade()
    {
        // The vacuity check on the case above: a component that wrote everything through on
        // render would pass it without the edit mattering.
        using var h = new BlazorTestHarness();

        OpenDeliveryPanel(h);

        h.SettingsManager.DidNotReceive().SetSetting("alerts.email.host", Arg.Any<JToken>());
    }

    // ── Test sends (moved from SettingsModalTests) ───────────────────────────

    [Fact]
    public void SendTestEmail_NoChannelRegistered_ReportsError()
    {
        using var h = new BlazorTestHarness();
        // Default harness has no IAlertChannel registered.
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test email alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() => Assert.Contains("Channel not registered", cut.Markup));
    }

    [Fact]
    public void SendTestEmail_ChannelMisconfigured_ReportsMissingFields()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email") { IsConfigured = false };
        h.OverrideAlertChannels(email);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test email alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Required fields are missing", cut.Markup);
            Assert.Equal(0, email.SendCallCount);
        });
    }

    [Fact]
    public void SendTestEmail_ChannelConfigured_InvokesSendAsync()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email");
        h.OverrideAlertChannels(email);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test email alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, email.SendCallCount);
            Assert.NotNull(email.LastSent);
            Assert.Contains("Test sent successfully", cut.Markup);
        });
    }

    [Fact]
    public void SendTestEmail_ChannelThrows_ReportsErrorMessage()
    {
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email")
        {
            ThrowOnSend = new InvalidOperationException("SMTP refused"),
        };
        h.OverrideAlertChannels(email);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test email alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Send failed: SMTP refused", cut.Markup);
            Assert.Equal(1, email.SendCallCount); // tried once, threw, caught
        });
    }

    [Fact]
    public void SendTestTelegram_RoutesToTelegramChannel()
    {
        using var h = new BlazorTestHarness();
        var email    = new StubAlertChannel("email",    "Email");
        var telegram = new StubAlertChannel("telegram", "Telegram");
        h.OverrideAlertChannels(email, telegram);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test Telegram alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, telegram.SendCallCount);
            Assert.Equal(0, email.SendCallCount);
        });
    }

    [Fact]
    public void TestSend_PersistsBeforeSending()
    {
        // A test send is a claim about the configuration on screen, so the configuration on
        // screen is what has to be on disk when the channel reads it.
        using var h = new BlazorTestHarness();
        var email = new StubAlertChannel("email", "Email");
        h.OverrideAlertChannels(email);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("button[aria-label='Send test email alert']").Click()).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            h.SettingsManager.Received().SetSetting("alerts.email.host",        Arg.Any<JToken>());
            h.SettingsManager.Received().SetSetting("alerts.telegram.botToken", Arg.Any<JToken>());
            h.SettingsManager.Received().SaveSettings();
        });
    }
}
