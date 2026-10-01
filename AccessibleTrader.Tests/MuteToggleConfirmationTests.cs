using AccessibleTrader.Core.Models;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Core.Services.Analysis;
using AccessibleTrader.Sdk.Models;
using AccessibleTrader.Tests.Mocks;

namespace AccessibleTrader.Tests;

/// <summary>
/// A mute key must confirm itself, in words that say which way it went, and the confirmation
/// must be heard even though the key just muted the very channel it would otherwise be spoken on.
/// Pressing F2 and hearing nothing is indistinguishable from F2 not working.
///
/// <para>
/// A2q (2026-10-01): the router's table (<c>F2ChartOnlyMuteTests</c>) pins that Critical is never
/// muted, and <c>MuteTierTests</c> that each tier silences what it should — but nothing pinned
/// that the coordinator SPEAKS the confirmations on a channel that survives the mute they
/// announce, nor which word goes with which state. The campaign moved "Chart speech off" onto
/// the chart channel, "Alerts and events muted" onto the event channel, and inverted "Sound
/// on/off"; all three survived.
/// </para>
///
/// <para>Real store and reducers (the toggle actions the key bindings dispatch), real
/// coordinator, real router.</para>
/// </summary>
public sealed class MuteToggleConfirmationTests
{
    private sealed class Harness
    {
        public WorkspaceStore Store { get; }
        public List<string> Spoken { get; } = new();

        public Harness()
        {
            var bus = new SpyEventBus();
            Store = new WorkspaceStore(bus, new ViewportRangeCalculator(),
                new ViewportNavigationService(), new VolumeStateService());
            var speech = new CounterSpeechManager { OnSpeak = t => Spoken.Add(t) };
            var formatter = new SpeechFormatter();
            var router = new SpeechFeedbackRouter(speech, formatter, Store);
            _ = new AccessibilityFeedbackCoordinator(
                Store, new NavigationFeedbackManager(router, formatter), router,
                new AudioFeedbackRouter(new MockNavigationSonifier(), new MockEarconService()),
                formatter, bus, new MockEarconService(), new SdkCandlePatternAnalyzer(),
                new ChartPatternCache(new ChartPatternDetector(new SwingStructureAnalyzer())),
                new ChartPatternFocus(), new MockAutoNarrationService());
        }

        public List<string> Press(WorkspaceAction toggle)
        {
            Spoken.Clear();
            Store.Dispatch(toggle);
            return Spoken.ToList();
        }
    }

    [Fact]
    public void F2_turning_chart_speech_off_is_confirmed_out_loud()
    {
        var h = new Harness();
        Assert.True(h.Store.State.IsSpeechEnabled);

        var said = h.Press(new ToggleSpeechAction());

        Assert.False(h.Store.State.IsSpeechEnabled);
        Assert.Contains("Chart speech off", said);
    }

    [Fact]
    public void F2_turning_chart_speech_back_on_says_on()
    {
        var h = new Harness();
        h.Press(new ToggleSpeechAction());

        var said = h.Press(new ToggleSpeechAction());

        Assert.Contains("Chart speech on", said);
        Assert.DoesNotContain("Chart speech off", said);
    }

    [Fact]
    public void Shift_F2_muting_alerts_and_events_is_confirmed_out_loud()
    {
        var h = new Harness();

        var said = h.Press(new ToggleEventSpeechAction());

        Assert.False(h.Store.State.IsEventSpeechEnabled);
        Assert.Contains("Alerts and events muted", said);
    }

    [Fact]
    public void F3_says_sound_off_when_it_turns_sound_off_and_on_when_on()
    {
        var h = new Harness();
        Assert.True(h.Store.State.IsSonificationEnabled);

        var off = h.Press(new ToggleSonificationAction());
        var on = h.Press(new ToggleSonificationAction());

        Assert.Equal(new[] { "Sound off" }, off);
        Assert.Equal(new[] { "Sound on" }, on);
    }
}
