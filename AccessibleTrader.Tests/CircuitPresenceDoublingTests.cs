using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Alerts;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// <b>The three minutes after a tab closes, when the alert went out twice.</b>
///
/// <para>
/// Closing a browser tab does not close its circuit: Blazor retains a disconnected circuit for
/// about three minutes in case the client reconnects, and every service in its DI scope goes on
/// running. Since the 2026-09-11 hand-off fix the headless session takes ownership the instant
/// the connection drops — correctly, because a circuit with no connection cannot reach anybody.
/// The two facts together meant that for up to three minutes an alert fired in BOTH pipelines:
/// two desktop notifications, two entries in the recent-alerts list, and — the one that is not
/// merely annoying — <b>two emails, two Telegram messages and two webhook POSTs</b>. A
/// duplicated outbound webhook can place a duplicate order at the far end.
/// </para>
///
/// <para>
/// <see cref="IUserPresence"/> is the answer, and these tests are written in PAIRS, per the rule
/// this whole phase runs on: each asserts the delivery happens when the circuit is connected and
/// does not when it is not. A test that only showed the silence would pass against a delivery
/// service that had stopped working altogether.
/// </para>
/// </summary>
public class CircuitPresenceDoublingTests
{
    private sealed class Presence : IUserPresence
    {
        public bool CanReachUser { get; set; } = true;
    }

    private sealed class SpyChannel : IAlertChannel
    {
        public readonly List<AlertFired> Sent = new();
        public string Id => "spy";
        public string DisplayName => "Spy";
        public bool IsConfigured => true;
        public Task SendAsync(AlertFired alert, CancellationToken ct)
        {
            lock (Sent) Sent.Add(alert);
            return Task.CompletedTask;
        }
    }

    private sealed class SpyNotifier : IDesktopNotifier
    {
        public readonly List<(string Title, string Body)> Shown = new();
        public bool IsAvailable => true;
        public string Describe() => "spy";
        public void Notify(string title, string body) => Shown.Add((title, body));
    }

    private static AlertFired Alert() => new(
        new AlertDefinition
        {
            Id = "a1", Name = "BTC above 100k", Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove, Threshold = 100_000, Delivery = AlertDelivery.Speech,
        },
        TriggeringValue: 100_100, PreviousValue: 99_900,
        SpeechText: "BTC/USD crossed above 100,000.", Symbol: "ETH/USD");

    private static OrderUpdate Fill() => new(
        "o1", "ETH/USD", OrderSide.Buy, FilledQuantity: 1, FilledPrice: 200, RemainingQuantity: 0,
        OrderStatus.Filled, StopTriggered: false, TakeProfitTriggered: false, Timestamp: DateTime.UtcNow,
        RealizedPnL: null);

    // ── The channel fan-out: the one that can place a duplicate order ────────

    [Fact]
    public async Task A_connected_circuit_fans_the_alert_out_to_its_channels()
    {
        var bus = new SpyEventBus();
        var channel = new SpyChannel();
        var presence = new Presence { CanReachUser = true };
        using var _ = new AlertDeliveryService(new[] { (IAlertChannel)channel }, bus, null, presence);

        bus.Publish(new AlertFiredEvent(Alert()));
        await Settle(() => channel.Sent.Count > 0);

        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task A_disconnected_circuit_sends_no_email_no_telegram_and_no_webhook()
    {
        var bus = new SpyEventBus();
        var channel = new SpyChannel();
        var presence = new Presence { CanReachUser = false };   // the retention window
        using var _ = new AlertDeliveryService(new[] { (IAlertChannel)channel }, bus, null, presence);

        bus.Publish(new AlertFiredEvent(Alert()));
        await Task.Delay(50);

        Assert.Empty(channel.Sent);
    }

    /// <summary>
    /// A head with no presence service at all — MAUI, and every test that does not care — must
    /// deliver. A missing presence service failing towards SILENCE is how a whole delivery
    /// channel disappears without anybody noticing.
    /// </summary>
    [Fact]
    public async Task With_no_presence_service_the_fan_out_still_happens()
    {
        var bus = new SpyEventBus();
        var channel = new SpyChannel();
        using var _ = new AlertDeliveryService(new[] { (IAlertChannel)channel }, bus);

        bus.Publish(new AlertFiredEvent(Alert()));
        await Settle(() => channel.Sent.Count > 0);

        Assert.Single(channel.Sent);
    }

    // ── The desktop notification ─────────────────────────────────────────────

    [Fact]
    public void A_connected_circuit_toasts_an_event_the_user_cannot_see()
    {
        var bus = new SpyEventBus();
        var notifier = new SpyNotifier();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(true));
        using var _ = new DesktopNotificationService(
            bus, new MockWorkspaceStore(), settings, notifier, null,
            new Presence { CanReachUser = true });

        bus.Publish(new OrderFilledEvent(Fill()));

        Assert.Single(notifier.Shown);
    }

    [Fact]
    public void A_disconnected_circuit_raises_no_toast_because_the_headless_side_owns_it()
    {
        var bus = new SpyEventBus();
        var notifier = new SpyNotifier();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(true));
        using var _ = new DesktopNotificationService(
            bus, new MockWorkspaceStore(), settings, notifier, null,
            new Presence { CanReachUser = false });

        bus.Publish(new OrderFilledEvent(Fill()));

        Assert.Empty(notifier.Shown);
    }

    private static async Task Settle(Func<bool> until)
    {
        for (int i = 0; i < 100 && !until(); i++) await Task.Delay(10);
    }
}
