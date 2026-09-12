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
/// <b>Desktop notifications for the things the trader cannot see.</b>
///
/// <para>
/// Cody, 2026-09-11: <i>"When monitoring other tabs that are open in the browser, receiving
/// toast notifications for those would be appropriate as that's the only way you'll see them.
/// The aria live region should only be used for the tab currently open in front of the
/// trader."</i> So the CHANNEL is decided by the event's SUBJECT, and this file is where that
/// rule is pinned: the focused chart is spoken and never toasted; everything else is toasted.
/// </para>
///
/// <para>
/// What this replaces: until 2026-09-11 there were three switches, all default OFF, and this
/// service toasted the FOCUSED chart's bar close. With a 1-minute chart open that is a MATE
/// toast a minute for a bar the browser was already announcing — the report that opened the
/// background-monitor quality pass. The <c>NewBarEvent</c> subscription is gone, and the three
/// switches are now one (<see cref="SettingsKeys.NotifyUnseenEvents"/>, default ON).
/// </para>
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

        /// <param name="notify">null leaves the key unset — which is the SHIPPED DEFAULT, and
        /// since 2026-09-11 that default is ON. A test that wants silence must say so.</param>
        public Harness(bool? notify = null)
        {
            if (notify.HasValue)
                Settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(notify.Value));
            _ = new DesktopNotificationService(Bus, Store, Settings, Notifier);
        }

        /// <summary>Put a symbol on screen, so the subject rule has something to compare against.</summary>
        public void Watching(string symbol, string timeframe = "1h") =>
            Store.EmitState(WorkspaceState.Initial with
            {
                SymbolDisplayName = symbol,
                Identity = WorkspaceState.Initial.Identity with { Symbol = symbol, Timeframe = timeframe },
            });
    }

    private static AlertFired Alert(string name = "BTC above 100k", string? symbol = "BTC/USD") => new(
        new AlertDefinition
        {
            Id = "a1", Name = name, Target = AlertTarget.Price,
            Condition = AlertCondition.CrossesAbove, Threshold = 100_000, Delivery = AlertDelivery.Speech,
        },
        TriggeringValue: 100_100, PreviousValue: 99_900,
        SpeechText: "BTC/USD crossed above 100,000.", Symbol: symbol);

    private static OrderUpdate Fill(double? pnl = null, string symbol = "BTC/USD") => new(
        "o1", symbol, OrderSide.Buy, FilledQuantity: 1, FilledPrice: 200, RemainingQuantity: 0,
        OrderStatus.Filled, StopTriggered: false, TakeProfitTriggered: false, Timestamp: DateTime.UtcNow,
        RealizedPnL: pnl);

    private static Ohlcv Bar(DateTime at) => new(at, 100, 110, 95, 105.5, 1000);

    private static BackgroundBarClosedEvent BackgroundClose(
        string symbol, string timeframe, DateTime closedAt, TimeSpan step) =>
        new(new ChartIdentity("Crypto", "Bitstamp", symbol, timeframe),
            Bar(closedAt), Bar(closedAt + step));

    // ── THE RULE: the chart in front of the trader is never toasted ──────────

    /// <summary>
    /// The defect that opened the pass. <c>NewBarEvent</c> is by definition the focused chart's
    /// bar close, and the live region says it. There is no subscription to it any more, so this
    /// is unreachable rather than merely unhandled — the same standard the category mask holds
    /// itself to.
    /// </summary>
    [Fact]
    public void TheFocusedChartsBarClose_IsNeverToasted()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD", "1m");

        for (int i = 0; i < 5; i++)
            h.Bus.Publish(new NewBarEvent(Bar(DateTime.UtcNow), Bar(DateTime.UtcNow)));

        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void AnAlertOnTheChartInFrontOfYou_IsNotToasted()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(symbol: "BTC/USD")));

        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void AnAlertOnAMarketWithNoTabOpen_IsToasted()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(name: "ETH above 5k", symbol: "ETH/USD")));

        var (title, body) = Assert.Single(h.Notifier.Shown);
        Assert.Equal("Alert: ETH above 5k", title);
        Assert.Equal("BTC/USD crossed above 100,000.", body);   // the fixture's own sentence
    }

    [Fact]
    public void AFillOnTheChartInFrontOfYou_IsNotToasted()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD");

        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "BTC/USD")));

        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void AFillOnAnotherMarket_IsToastedInTheSpeechLayersWords()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD");

        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        h.Bus.Publish(new StopHitEvent(Fill(pnl: -50, symbol: "ETH/USD")));
        h.Bus.Publish(new TakeProfitHitEvent(Fill(pnl: 75, symbol: "ETH/USD") with { Trailing = true }));

        Assert.Equal(3, h.Notifier.Shown.Count);
        Assert.Equal("Order filled", h.Notifier.Shown[0].Title);
        Assert.StartsWith("Bought 1 ETH/USD at 200.", h.Notifier.Shown[0].Body);
        Assert.Equal("Stop loss hit", h.Notifier.Shown[1].Title);
        Assert.Contains("Loss 50", h.Notifier.Shown[1].Body);
        Assert.Equal("Trailing take profit hit", h.Notifier.Shown[2].Title);
        Assert.Contains("Profit 75", h.Notifier.Shown[2].Body);
        // The body never repeats the title: "Order filled. Order filled. Bought…" is what a
        // naive reuse of the speech sentence would have produced.
        Assert.All(h.Notifier.Shown, s => Assert.DoesNotContain(s.Title, s.Body));
    }

    /// <summary>
    /// A bar closing on a tab the user has open but is not looking at. The title names the
    /// EVENT'S own symbol and timeframe, never the store's — a toast reading "BTC/USD 1h: bar
    /// closed" for an ETH bar close would be worse than silence.
    /// </summary>
    [Fact]
    public void ABackgroundTabsBarClose_IsToasted_NamingItsOwnChart()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD", "1m");

        var closedAt = new DateTime(2026, 9, 5, 14, 0, 0, DateTimeKind.Local);
        h.Bus.Publish(BackgroundClose("ETH/USD", "1h", closedAt, TimeSpan.FromHours(1)));

        var (title, body) = Assert.Single(h.Notifier.Shown);
        Assert.Equal("ETH/USD 1h: bar closed", title);
        Assert.StartsWith("Close 105.50 at ", body);   // the time of day, on an intraday chart
    }

    [Fact]
    public void ADailyChart_SaysTheDate_NotAMidnightClock()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD", "1m");

        var closedAt = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Local);
        h.Bus.Publish(BackgroundClose("SPY", "1d", closedAt, TimeSpan.FromDays(1)));

        var (_, body) = Assert.Single(h.Notifier.Shown);
        Assert.Contains(" on ", body);
        Assert.DoesNotContain("00:00", body);
    }

    /// <summary>
    /// The tab the bar closed on has since become the focused chart. The live region is
    /// announcing it now, so the toast must stand down — the subject rule is evaluated at
    /// delivery time, not at publication time.
    /// </summary>
    [Fact]
    public void ABackgroundCloseOnTheNowFocusedChart_IsNotToasted()
    {
        var h = new Harness(notify: true);
        h.Watching("ETH/USD", "1h");

        h.Bus.Publish(BackgroundClose("ETH/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

        Assert.Empty(h.Notifier.Shown);
    }

    // ── The switch ───────────────────────────────────────────────────────────

    /// <summary>
    /// <b>ON by default.</b> Cody, 2026-09-11. Three default-off switches in a dialog away from
    /// the feature they gated is how a working feature came to be reported as broken in the
    /// thirty-ninth pass, and for a blind user an accidental silence has no second channel.
    /// </summary>
    [Fact]
    public void NotificationsAreOnByDefault()
    {
        var h = new Harness();   // key never written, which IS the shipped state
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD")));
        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        h.Bus.Publish(BackgroundClose("SOL/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

        Assert.Equal(3, h.Notifier.Shown.Count);
    }

    [Fact]
    public void TurningItOff_SilencesEveryCategory()
    {
        var h = new Harness(notify: false);
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD")));
        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        h.Bus.Publish(BackgroundClose("SOL/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

        Assert.Empty(h.Notifier.Shown);
    }

    /// <summary>
    /// Someone who had deliberately turned all three retired switches OFF keeps their silence;
    /// the new default must not switch them back on behind their back.
    /// </summary>
    [Fact]
    public void AnExplicitLegacyAllOff_StaysOff()
    {
        var settings = Substitute.For<ISettingsManager>();
        foreach (var key in new[] { "notifications.desktop.alerts", "notifications.desktop.newBars",
                                    "notifications.desktop.orderFills" })
            settings.GetSetting(key).Returns(JToken.FromObject(false));

        Assert.False(NotificationPolicy.NotifyUnseen(settings));
    }

    /// <summary>…but a PARTIAL legacy state reads as on: they had asked to be told something.</summary>
    [Fact]
    public void AnExplicitLegacyPartialOn_ReadsAsOn()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting("notifications.desktop.alerts").Returns(JToken.FromObject(true));
        settings.GetSetting("notifications.desktop.newBars").Returns(JToken.FromObject(false));

        Assert.True(NotificationPolicy.NotifyUnseen(settings));
    }

    /// <summary>The new key wins over any legacy value — it is the one the dialog writes.</summary>
    [Fact]
    public void TheNewKeyWinsOverTheRetiredOnes()
    {
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting("notifications.desktop.alerts").Returns(JToken.FromObject(true));
        settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(false));

        Assert.False(NotificationPolicy.NotifyUnseen(settings));
    }

    // ── The delivery seam ────────────────────────────────────────────────────

    [Fact]
    public void AHeadWithNoToastPath_ShowsNothing_EvenWithTheSwitchOn()
    {
        var h = new Harness(notify: true);
        h.Notifier.IsAvailable = false;
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD")));
        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        h.Bus.Publish(BackgroundClose("SOL/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

        Assert.Empty(h.Notifier.Shown);
    }

    [Fact]
    public void PlaybackBars_AreNotTheMarket_AndAreSkipped()
    {
        var h = new Harness(notify: true);
        h.Store.EmitState(WorkspaceState.Initial with { IsPlaying = true });

        h.Bus.Publish(BackgroundClose("ETH/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

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
        settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(true));
        _ = new DesktopNotificationService(bus, new MockWorkspaceStore(), settings, notifier);

        var ex = Record.Exception(() => bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD"))));
        Assert.Null(ex);
    }

    // ── The category mask ────────────────────────────────────────────────────

    /// <summary>
    /// The mask decides whether the SUBSCRIPTION exists, not whether the handler returns early,
    /// so an unowned category is unreachable rather than merely unhandled.
    /// </summary>
    [Fact]
    public void AMaskedInstanceDoesNotSubscribeToTheCategoriesItDoesNotOwn()
    {
        var bus = new SpyEventBus();
        var settings = Substitute.For<ISettingsManager>();
        settings.GetSetting(SettingsKeys.NotifyUnseenEvents).Returns(JToken.FromObject(true));
        var notifier = new SpyNotifier();

        using var _ = new DesktopNotificationService(
            bus, new MockWorkspaceStore(), settings, notifier,
            DesktopNotificationCategories.OrderFills | DesktopNotificationCategories.NewBars);

        bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD")));
        Assert.Empty(notifier.Shown);

        // Vacuity floor: the two categories it DOES own still arrive, so the silence above is
        // the mask and not a broken harness.
        bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        bus.Publish(BackgroundClose("SOL/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));
        Assert.Equal(2, notifier.Shown.Count);
    }

    [Fact]
    public void TheDefaultInstanceOwnsAllThreeCategories()
    {
        var h = new Harness(notify: true);
        h.Watching("BTC/USD");

        h.Bus.Publish(new AlertFiredEvent(Alert(symbol: "ETH/USD")));
        h.Bus.Publish(new OrderFilledEvent(Fill(symbol: "ETH/USD")));
        h.Bus.Publish(BackgroundClose("SOL/USD", "1h", DateTime.Now, TimeSpan.FromHours(1)));

        Assert.Equal(3, h.Notifier.Shown.Count);
    }
}
