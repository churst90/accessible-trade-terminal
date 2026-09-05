using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Notifications;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Sdk.Plugins;
using AccessibleTrader.Sdk.Trading;
using AccessibleTrader.Tests.Mocks;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// Desktop toasts for alerts, fills and new bars — each behind its own switch, all three off
/// by default. Cody, 2026-09-05: "is it possible for the webhost to send desktop notifications
/// using the mate notification center? How about the maui head…"
///
/// <para>
/// The delivery is a seam (<see cref="IDesktopNotifier"/>) and this file drives the policy
/// through a recording one, the way the background monitor's presenter is tested: no process
/// is spawned, and what would have been shown is asserted word for word.
/// </para>
/// </summary>
public class DesktopNotificationServiceTests
{
    private sealed class SpyNotifier : IDesktopNotifier
    {
        public bool IsAvailable { get; set; } = true;
        public readonly List<(string Title, string Body)> Shown = new();
        public string Describe() => "spy";
        public void Notify(string title, string body) => Shown.Add((title, body));
    }

    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public MockWorkspaceStore Store { get; } = new();
        public ISettingsManager Settings { get; } = Substitute.For<ISettingsManager>();
        public SpyNotifier Notifier { get; } = new();

        public Harness(params string[] switchedOn)
        {
            foreach (var key in switchedOn)
                Settings.GetSetting(key).Returns(JToken.FromObject(true));
            _ = new DesktopNotificationService(Bus, Store, Settings, Notifier);
        }
    }

    private static AlertFired Alert(string name = "BTC above 100k") => new(
        new AlertDefinition
        {
            Id = "a1", Name = name, Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove, Threshold = 100_000, Delivery = AlertDelivery.Speech,
        },
        TriggeringValue: 100_100, PreviousValue: 99_900,
        SpeechText: "BTC/USD crossed above 100,000.", Symbol: "BTC/USD");

    private static OrderUpdate Fill(double? pnl = null) => new(
        "o1", "BTC/USD", OrderSide.Buy, FilledQuantity: 1, FilledPrice: 200, RemainingQuantity: 0,
        OrderStatus.Filled, StopTriggered: false, TakeProfitTriggered: false, Timestamp: DateTime.UtcNow,
        RealizedPnL: pnl);

    private static Ohlcv Bar(DateTime at) => new(at, 100, 110, 95, 105.5, 1000);

    // ── Defaults ─────────────────────────────────────────────────────────────

    [Fact]
    public void EverythingIsOffByDefault()
    {
        // A bare settings substitute answers null for every key: that IS the shipped default,
        // and it must mean silence — a one-minute chart is a toast a minute otherwise.
        var h = new Harness();
        h.Bus.Publish(new AlertFiredEvent(Alert()));
        h.Bus.Publish(new OrderFilledEvent(Fill()));
        h.Bus.Publish(new NewBarEvent(Bar(DateTime.UtcNow), Bar(DateTime.UtcNow)));
        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void AHeadWithNoToastPath_ShowsNothing_EvenWithEverySwitchOn()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyAlerts, SettingsKeys.DesktopNotifyOrderFills, SettingsKeys.DesktopNotifyNewBars);
        h.Notifier.IsAvailable = false;
        h.Bus.Publish(new AlertFiredEvent(Alert()));
        h.Bus.Publish(new OrderFilledEvent(Fill()));
        h.Bus.Publish(new NewBarEvent(Bar(DateTime.UtcNow), Bar(DateTime.UtcNow)));
        Assert.Empty(h.Notifier.Shown);
    }

    // ── Each switch gates exactly its own event ──────────────────────────────

    [Fact]
    public void TheAlertSwitch_ToastsAFiredAlert_AndNothingElse()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyAlerts);
        h.Bus.Publish(new AlertFiredEvent(Alert()));
        h.Bus.Publish(new OrderFilledEvent(Fill()));
        h.Bus.Publish(new NewBarEvent(Bar(DateTime.UtcNow), Bar(DateTime.UtcNow)));

        var (title, body) = Assert.Single(h.Notifier.Shown);
        Assert.Equal("Alert: BTC above 100k", title);
        Assert.Equal("BTC/USD crossed above 100,000.", body);
    }

    [Fact]
    public void TheFillSwitch_ToastsFillsStopsAndTakeProfits_InTheSpeechLayersWords()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyOrderFills);
        h.Bus.Publish(new OrderFilledEvent(Fill()));
        h.Bus.Publish(new StopHitEvent(Fill(pnl: -50)));
        h.Bus.Publish(new TakeProfitHitEvent(Fill(pnl: 75) with { Trailing = true }));
        h.Bus.Publish(new AlertFiredEvent(Alert()));

        Assert.Equal(3, h.Notifier.Shown.Count);
        Assert.Equal("Order filled", h.Notifier.Shown[0].Title);
        Assert.StartsWith("Bought 1 BTC/USD at 200.", h.Notifier.Shown[0].Body);
        Assert.Equal("Stop loss hit", h.Notifier.Shown[1].Title);
        Assert.Contains("Loss 50", h.Notifier.Shown[1].Body);
        Assert.Equal("Trailing take profit hit", h.Notifier.Shown[2].Title);
        Assert.Contains("Profit 75", h.Notifier.Shown[2].Body);
        // The body never repeats the title: "Order filled. Order filled. Bought…" is what a
        // naive reuse of the speech sentence would have produced.
        Assert.All(h.Notifier.Shown, s => Assert.DoesNotContain(s.Title, s.Body));
    }

    [Fact]
    public void TheNewBarSwitch_NamesTheChartAndTheBar()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyNewBars);
        h.Store.EmitState(WorkspaceState.Initial with
        {
            SymbolDisplayName = "BTC/USD",
            Identity = WorkspaceState.Initial.Identity with { Timeframe = "1h" },
        });
        var closed = Bar(new DateTime(2026, 9, 5, 14, 0, 0, DateTimeKind.Local));
        h.Bus.Publish(new NewBarEvent(closed, Bar(closed.Date.AddHours(1))));

        var (title, body) = Assert.Single(h.Notifier.Shown);
        Assert.Equal("BTC/USD 1h: bar closed", title);
        Assert.StartsWith("Close 105.50 at ", body); // the time of day, on an intraday chart
    }

    [Fact]
    public void ADailyChart_SaysTheDate_NotAMidnightClock()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyNewBars);
        h.Store.EmitState(WorkspaceState.Initial with
        {
            SymbolDisplayName = "SPY",
            Identity = WorkspaceState.Initial.Identity with { Timeframe = "1d" },
        });
        var closed = Bar(new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Local));
        h.Bus.Publish(new NewBarEvent(closed, Bar(closed.Date.AddDays(1))));

        var (_, body) = Assert.Single(h.Notifier.Shown);
        Assert.Contains(" on ", body);
        Assert.DoesNotContain("00:00", body);
    }

    [Fact]
    public void PlaybackBars_AreNotTheMarket_AndAreSkipped()
    {
        var h = new Harness(SettingsKeys.DesktopNotifyNewBars);
        h.Store.EmitState(WorkspaceState.Initial with { IsPlaying = true });
        h.Bus.Publish(new NewBarEvent(Bar(DateTime.UtcNow), Bar(DateTime.UtcNow)));
        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void ANotifierThatThrows_IsALogLine_NotACrashOnTheBus()
    {
        var bus = new SpyEventBus();
        var notifier = Substitute.For<IDesktopNotifier>();
        notifier.IsAvailable.Returns(true);
        notifier.When(n => n.Notify(Arg.Any<string>(), Arg.Any<string>())).Do(_ => throw new InvalidOperationException("toast broke"));
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.DesktopNotifyAlerts).Returns(JToken.FromObject(true));
        _ = new DesktopNotificationService(bus, new MockWorkspaceStore(), settings, notifier);

        var ex = Record.Exception(() => bus.Publish(new AlertFiredEvent(Alert())));
        Assert.Null(ex);
    }
}
