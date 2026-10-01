using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Alerts;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// A fired alert, as the user hears it: spoken, sounded, or both, as the alert says; muted by
/// Shift+F2 (alerts and events) and NOT by F2 (the chart); and an alert the user marked
/// "break through mutes" heard through every mute, words and sound alike.
///
/// <para>
/// A2q (2026-10-01). <see cref="AccessibilityFeedbackCoordinator"/>'s <see cref="AlertFiredEvent"/>
/// subscription — the one place a price alert becomes sound — had no test at all: the suite's
/// alert tests stopped at the evaluator, or at the email/telegram/webhook fan-out. The campaign
/// deleted the Speech arm, routed alerts onto the chart's mute, dropped the break-through, and
/// every one survived.
/// </para>
///
/// <para>
/// Real coordinator, real <see cref="SpeechFeedbackRouter"/> deciding the mutes from the store,
/// real <see cref="EarconService"/> deciding whether its notes play; only the audio device (the
/// sonification manager) is substituted.
/// </para>
/// </summary>
public sealed class FiredAlertDeliveryTests
{
    private sealed class Harness
    {
        public SpyEventBus Bus { get; } = new();
        public MockWorkspaceStore Store { get; } = new();
        public List<string> Spoken { get; } = new();
        public ISonificationManager Device { get; } = Substitute.For<ISonificationManager>();

        public Harness(bool chartSpeech = true, bool eventSpeech = true, bool earcons = true)
        {
            Store.EmitState(WorkspaceState.Initial with
            {
                IsSpeechEnabled = chartSpeech,
                IsEventSpeechEnabled = eventSpeech,
                IsEarconsEnabled = earcons,
            });
            var lib = Substitute.For<ISoundPatchLibrary>();
            lib.EarconOverrides.Returns(new EarconSettings());
            var earcons_ = new EarconService(Device, lib, null, Store);

            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), earcons_),
                formatter, Bus, earcons_, new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
        }

        public void Fire(AlertDelivery delivery, bool breakThrough = false)
        {
            var def = new AlertDefinition
            {
                Id = "a1", Name = "BTC above 70k", Target = AlertTarget.Price,
                Condition = AlertCondition.CrossesAbove, Threshold = 70000,
                Delivery = delivery, BreakThroughMutes = breakThrough,
            };
            Bus.Publish(new AlertFiredEvent(new AlertFired(def, 70010, 69990,
                "Alert: BTC crossed above 70,000.", "BTCUSD")));
        }

        public bool SoundPlayed => Device.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "PlayNote");
    }

    private const string Said = "Alert: BTC crossed above 70,000.";

    [Fact]
    public void An_alert_set_to_speech_is_spoken()
    {
        var h = new Harness();
        h.Fire(AlertDelivery.Speech);
        Assert.Contains(Said, h.Spoken);
        Assert.False(h.SoundPlayed, "a speech-only alert also played its earcon");
    }

    [Fact]
    public void An_alert_set_to_earcon_sounds_and_says_nothing()
    {
        var h = new Harness();
        h.Fire(AlertDelivery.Earcon);
        Assert.DoesNotContain(Said, h.Spoken);
        Assert.True(h.SoundPlayed);
    }

    [Fact]
    public void An_alert_set_to_both_is_spoken_and_sounded()
    {
        var h = new Harness();
        h.Fire(AlertDelivery.Both);
        Assert.Contains(Said, h.Spoken);
        Assert.True(h.SoundPlayed);
    }

    // ── Which mute silences it (Cody, 2026-09-30) ────────────────────────────

    [Fact]
    public void F2_muting_the_chart_does_not_silence_an_alert()
    {
        // F2 is "chart navigation and narration". An alert is something that happened TO the
        // user, and a user who muted the chart to think must still hear BTC cross 70k.
        var h = new Harness(chartSpeech: false);
        h.Fire(AlertDelivery.Speech);
        Assert.Contains(Said, h.Spoken);
    }

    [Fact]
    public void Shift_F2_silences_an_ordinary_alert()
    {
        // The partner: an alert that pierced every mute would pass the test above. Shift+F2 is
        // alerts and events, and an ordinary alert is exactly what it is for.
        var h = new Harness(eventSpeech: false);
        h.Fire(AlertDelivery.Speech);
        Assert.DoesNotContain(Said, h.Spoken);
    }

    [Fact]
    public void A_break_through_alert_is_spoken_through_Shift_F2()
    {
        var h = new Harness(eventSpeech: false);
        h.Fire(AlertDelivery.Speech, breakThrough: true);
        Assert.Contains(Said, h.Spoken);
    }

    [Fact]
    public void A_break_through_alert_is_sounded_with_earcons_muted()
    {
        // The per-alert promise is "break through mutes" — both of them. Words through Shift+F2
        // and the sound through Shift+F3, or the alert the user flagged as the one they must not
        // miss arrives with half of itself missing.
        var h = new Harness(earcons: false);
        h.Fire(AlertDelivery.Earcon, breakThrough: true);
        Assert.True(h.SoundPlayed, "a break-through alert's earcon was silenced by the earcon mute");
    }

    [Fact]
    public void An_ordinary_alert_is_not_sounded_with_earcons_muted()
    {
        var h = new Harness(earcons: false);
        h.Fire(AlertDelivery.Earcon);
        Assert.False(h.SoundPlayed);
    }
}
