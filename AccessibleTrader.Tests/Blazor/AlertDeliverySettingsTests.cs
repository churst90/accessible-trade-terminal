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
using Microsoft.Extensions.DependencyInjection;
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

    // ── Desktop notifications ────────────────────────────────────────────────

    /// <summary>
    /// The panel belongs to hosts that HAVE a browser-closed half. The gate is the host mode,
    /// not whether this particular desktop happens to have notify-send — see
    /// <see cref="WithNoNotificationTool_TheControlsRemain_AndSayWhatWillHappenInstead"/>.
    /// </summary>
    [Fact]
    public void TheNotificationPanel_IsAbsent_WhereThereIsNoBrowserClosedHalf()
    {
        using var h = new BlazorTestHarness();
        h.Ctx.Services.AddSingleton(new AccessibleTrader.Core.Services.DemoPolicy(AccessibleTrader.Core.Services.HostMode.Hosted));
        var cut = OpenDeliveryPanel(h);
        Assert.Empty(cut.FindAll("#s-notify-unseen"));
        Assert.Empty(cut.FindAll("#s-bg-bar-floor"));
    }

    /// <summary>
    /// <b>The hosted gating, pinned.</b> Cody, 2026-09-11: the paper-only website should not
    /// offer background alerts and their settings — and "the rest of the alert options other
    /// than the background options can stay like telegram and all that". So on a Hosted policy
    /// the desktop-notification switches and the browser-notification (Web Push) panel are
    /// absent even where a toast could be delivered, while email, Telegram and webhooks — which
    /// deliver from the in-browser pipeline with the browser open — remain. Every other test in
    /// this file runs at HostMode.Full, so until this one the hosted shape was unpinned.
    /// </summary>
    [Fact]
    public void OnTheHostedTerminal_OnlyTheBackgroundPanelsAreGone()
    {
        using var h = new BlazorTestHarness();
        h.Ctx.Services.AddSingleton(new AccessibleTrader.Core.Services.DemoPolicy(AccessibleTrader.Core.Services.HostMode.Hosted));
        h.DesktopNotifier.IsAvailable.Returns(true);
        var cut = OpenDeliveryPanel(h);

        Assert.Empty(cut.FindAll("#s-notify-unseen"));
        Assert.Empty(cut.FindAll("#s-bg-bar-floor"));
        Assert.DoesNotContain("Browser notifications", cut.Markup);

        Assert.Single(cut.FindAll("#s-email-host"));
        Assert.Single(cut.FindAll("#s-tg-token"));
        Assert.Single(cut.FindAll("#s-setup-alerts"));
    }

    /// <summary>
    /// ONE switch since 2026-09-11, and it is ON by default — Cody's call. The three default-off
    /// switches this replaces lived in a different dialog from the feature they gated, which is
    /// how the thirty-ninth pass's "broken" bar closes turned out to be switched-off ones.
    /// </summary>
    [Fact]
    public void TheNotificationSwitch_IsPresentAndOnByDefault_WhereAToastCanBeDelivered()
    {
        using var h = new BlazorTestHarness();
        h.DesktopNotifier.IsAvailable.Returns(true);
        h.DesktopNotifier.Describe().Returns("notify-send");
        var cut = OpenDeliveryPanel(h);

        Assert.Single(cut.FindAll("#s-notify-unseen"));
        Assert.NotNull(cut.Find("#s-notify-unseen").GetAttribute("checked"));
        Assert.Contains("notify-send", cut.Markup);
    }

    /// <summary>
    /// <b>The panel survives a machine with no notification tool.</b> It used to be gated on
    /// <c>IDesktopNotifier.IsAvailable</c>, so a desktop without notify-send lost the
    /// background-tab SPEECH switch and the timeframe floor along with the toast switch — which
    /// is backwards, because on that machine speech is the delivery channel
    /// (<c>DesktopAnnouncement.Present</c> speaks exactly where nothing reads a toast).
    /// </summary>
    [Fact]
    public void WithNoNotificationTool_TheControlsRemain_AndSayWhatWillHappenInstead()
    {
        using var h = new BlazorTestHarness();
        h.DesktopNotifier.IsAvailable.Returns(false);
        var cut = OpenDeliveryPanel(h);

        Assert.Single(cut.FindAll("#s-notify-unseen"));
        Assert.Single(cut.FindAll("#s-speak-bg-bars"));
        Assert.Single(cut.FindAll("#s-bg-bar-floor"));
        Assert.Contains("spoken aloud instead", cut.Markup);
    }

    [Fact]
    public void TickingTheNotificationSwitch_WritesItsKeyImmediately()
    {
        // Same commit rule as the SMTP fields: no Save button, and Escape cannot lose it.
        using var h = new BlazorTestHarness();
        h.DesktopNotifier.IsAvailable.Returns(true);
        var cut = OpenDeliveryPanel(h);

        cut.InvokeAsync(() => cut.Find("#s-notify-unseen").Change(false)).GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            h.SettingsManager.Received().SetSetting(SettingsKeys.NotifyUnseenEvents,
                Arg.Is<JToken>(t => t.Type == JTokenType.Boolean && !(bool)t));
            h.SettingsManager.Received().SaveSettings();
        });
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
