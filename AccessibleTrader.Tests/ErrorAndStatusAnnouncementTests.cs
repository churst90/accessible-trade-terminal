using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Enums;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;

namespace AccessibleTrader.Tests;

/// <summary>
/// What <see cref="GlobalErrorCoordinator"/> and <see cref="NotificationHub"/> make the user HEAR,
/// through the real coordinator and router: the words, and which mute can silence them.
///
/// <para>
/// A2q (2026-10-01). Every caller of both classes in the suite was a substitute, so nothing
/// observed the sentence that reached the screen reader. The campaign dropped "CRITICAL" from a
/// critical error, put a strategy's "order placed" back on the chart's mute, turned "Connection
/// lost" into chart chatter that F2 silences, and let an indicator advisory pierce Shift+F2 — all
/// survived.
/// </para>
/// </summary>
public sealed class ErrorAndStatusAnnouncementTests
{
    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public MockWorkspaceStore Store { get; } = new();
        public List<string> Spoken { get; } = new();
        public GlobalErrorCoordinator Errors { get; }
        public NotificationHub Hub { get; }

        public Harness(bool chartSpeech = true, bool eventSpeech = true)
        {
            Store.EmitState(WorkspaceState.Initial with
            {
                IsSpeechEnabled = chartSpeech,
                IsEventSpeechEnabled = eventSpeech,
            });
            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            var audio = new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService());
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router, audio,
                formatter, Bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
            Errors = new GlobalErrorCoordinator(Bus, NullLogger<GlobalErrorCoordinator>.Instance, audio);
            Hub = new NotificationHub(router, audio);
        }
    }

    // ── Errors ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_critical_error_is_announced_as_critical()
    {
        var h = new Harness();
        h.Bus.Publish(new AppErrorEvent(ErrorSeverity.Critical, ErrorCategory.Systemic,
            "Position state is unknown.", "Order router"));

        Assert.Contains(h.Spoken, s => s.Contains("CRITICAL", StringComparison.Ordinal)
                                    && s.Contains("Position state is unknown.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_high_error_is_not_announced_as_critical()
    {
        // The partner: "CRITICAL" on everything would pass the test above and teach the user to
        // ignore the word.
        var h = new Harness();
        h.Bus.Publish(new AppErrorEvent(ErrorSeverity.High, ErrorCategory.Provider,
            "Request timed out.", "Provider"));

        Assert.Contains(h.Spoken, s => s.Contains("Request timed out.", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Spoken, s => s.Contains("CRITICAL", StringComparison.Ordinal));
    }

    // ── A strategy's order going through ─────────────────────────────────────

    [Fact]
    public void A_strategy_order_placed_is_heard_with_chart_speech_off()
    {
        // Money moved. F2 mutes the CHART; with it on, a strategy's order used to be announced
        // only if it failed.
        var h = new Harness(chartSpeech: false);
        h.Errors.ReportSuccess("Order placed: buy 0.1 BTCUSD.");

        Assert.Contains("Order placed: buy 0.1 BTCUSD.", h.Spoken);
    }

    // ── A leveraged position near liquidation ────────────────────────────────

    [Fact]
    public void A_margin_warning_is_heard_with_alerts_and_events_muted()
    {
        // Money about to be taken. Shift+F2 quietens alerts and monitoring; a position drifting
        // toward liquidation is an order outcome, which breaks through both mutes by default.
        var h = new Harness(chartSpeech: false, eventSpeech: false);
        h.Bus.Publish(new MarginWarningEvent("BTCUSD", 0.12,
            "Margin warning for BTCUSD. Liquidation is 3 percent away."));

        Assert.Contains("Margin warning for BTCUSD. Liquidation is 3 percent away.", h.Spoken);
    }

    // ── Connection status ────────────────────────────────────────────────────

    [Fact]
    public void Connection_lost_is_heard_with_chart_speech_off()
    {
        // Connection status is an EVENT (Shift+F2's), not chart speech (F2's). A user who muted
        // the chart must still learn the feed has dropped.
        var h = new Harness(chartSpeech: false);
        h.Errors.ReportNetworkRetry("Kraken", 2, 8);

        Assert.Contains(h.Spoken, s => s.StartsWith("Connection lost to Kraken", StringComparison.Ordinal));
    }

    [Fact]
    public void Connection_lost_is_silenced_by_Shift_F2()
    {
        // The partner: a retry that pierced every mute would pass the test above, and a
        // chattering reconnect loop is exactly what Shift+F2 exists to quieten.
        var h = new Harness(eventSpeech: false);
        h.Errors.ReportNetworkRetry("Kraken", 2, 8);

        Assert.DoesNotContain(h.Spoken, s => s.StartsWith("Connection lost", StringComparison.Ordinal));
    }

    // ── Advisories ───────────────────────────────────────────────────────────

    [Fact]
    public void An_indicator_advisory_is_silenced_by_Shift_F2()
    {
        // NotificationHub.NotifyInfo carries the indicator orchestrator's "parameters changed to
        // fit this market" advisory: something that happened, not something asked for, so the
        // event mute governs it.
        var h = new Harness(eventSpeech: false);
        h.Hub.NotifyInfo("Cipher B adjusted its periods for this timeframe.", interrupt: false);

        Assert.Empty(h.Spoken);
    }

    [Fact]
    public void An_indicator_advisory_is_heard_with_chart_speech_off()
    {
        var h = new Harness(chartSpeech: false);
        h.Hub.NotifyInfo("Cipher B adjusted its periods for this timeframe.", interrupt: false);

        Assert.Contains("Cipher B adjusted its periods for this timeframe.", h.Spoken);
    }
}
