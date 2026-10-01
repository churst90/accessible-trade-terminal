using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Accessibility;
using AccessibleTrader.Sdk.Models;
using NSubstitute;

namespace AccessibleTrader.Tests;

/// <summary>
/// How long a higher-priority utterance is protected from the arrow keys, measured on the real
/// clock rather than through the router's reset seam.
///
/// <para>
/// The rule (<see cref="SpeechFeedbackRouter"/>): an order outcome or alert holds off chart
/// speech for roughly as long as it takes to say — so key-repeat on the arrows cannot cut
/// "Order rejec—" off — and NEVER longer than four seconds, so a long message cannot lock the
/// chart out. Holding a slot also has to survive key-repeat: it is the SECOND and later arrow
/// presses that arrive while the rejection is still being read.
/// </para>
///
/// <para>
/// A2q (2026-10-01). <c>SpeechPriorityAndSilenceTests</c> covers one follow-up keypress and
/// expires protection through <c>ResetSpeechPriorityForTests</c>, so nothing measured the clock:
/// the campaign made every short message hold priority for the full four seconds, and let each
/// arrow press take over the protected slot so the second press cut the rejection off. Both
/// survived.
/// </para>
/// </summary>
public sealed class SpeechPriorityTimingTests
{
    private sealed class SpySpeech : ISpeechManager
    {
        public bool IsSpeechEnabled { get; set; } = true;
        public Action<string>? OnSpeak { get; set; }
        public readonly List<string> Calls = new();
        public void Speak(string text, bool interrupt = false) => Calls.Add($"{(interrupt ? "INTERRUPT" : "QUEUE")}:{text}");
        public void Silence() => Calls.Add("SILENCE");
    }

    private static (SpeechFeedbackRouter Router, SpySpeech Speech) Build()
    {
        var speech = new SpySpeech();
        var store = Substitute.For<IWorkspaceStore>();
        store.State.Returns(WorkspaceState.Initial with { IsSpeechEnabled = true, IsEventSpeechEnabled = true });
        return (new SpeechFeedbackRouter(speech, Substitute.For<ISpeechFormatter>(), store), speech);
    }

    [Fact]
    public void Key_repeat_does_not_cut_off_an_order_rejection_on_the_second_or_third_press()
    {
        var (router, speech) = Build();
        router.Speak("Order rejected for BTCUSDT. Insufficient balance.", interrupt: true, SpeechChannel.OrderEvent);
        speech.Calls.Clear();

        router.Speak("61,240. 14:05.", interrupt: true, SpeechChannel.Chart);
        router.Speak("61,250. 14:06.", interrupt: true, SpeechChannel.Chart);
        router.Speak("61,260. 14:07.", interrupt: true, SpeechChannel.Chart);

        Assert.DoesNotContain("SILENCE", speech.Calls);
        Assert.Equal(3, speech.Calls.Count(c => c.StartsWith("QUEUE:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_short_alert_stops_holding_off_the_arrow_keys_once_it_has_been_said()
    {
        // "BTC above 70k." is about a second of speech. Two seconds later the user is arrowing
        // again and the chart must answer at once, not queue behind an alert that has finished.
        var (router, speech) = Build();
        router.Speak("BTC above 70k.", interrupt: true, SpeechChannel.Event);

        await Task.Delay(TimeSpan.FromSeconds(2));
        speech.Calls.Clear();
        router.Speak("61,240.", interrupt: true, SpeechChannel.Chart);

        Assert.Contains("SILENCE", speech.Calls);
        Assert.Contains("INTERRUPT:61,240.", speech.Calls);
    }

    [Fact]
    public async Task A_very_long_message_holds_off_the_arrow_keys_for_at_most_four_seconds()
    {
        // About thirteen seconds of speech at the router's reading rate. The ceiling is the
        // promise that the chart never stops answering arrow keys for longer than that.
        var (router, speech) = Build();
        router.Speak(new string('x', 200), interrupt: true, SpeechChannel.OrderEvent);

        await Task.Delay(TimeSpan.FromSeconds(4.5));
        speech.Calls.Clear();
        router.Speak("61,240.", interrupt: true, SpeechChannel.Chart);

        Assert.Contains("SILENCE", speech.Calls);
    }
}
