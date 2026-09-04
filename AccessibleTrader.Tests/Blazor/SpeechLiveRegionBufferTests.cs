using AccessibleTrader.BlazorClient.Services;

namespace AccessibleTrader.Tests.Blazor;

/// <summary>
/// The ARIA live-region double buffer alternates for the speech that actually needs it.
///
/// <para><b>The defect.</b> The alternation lived inline in <c>MainLayout</c> and flipped on
/// every callback from the speech manager, including the empty one. Interrupting speech — the
/// DEFAULT, and what every chart command uses — is <c>Silence()</c> then <c>Speak()</c>, and
/// <c>BlazorSpeechManager.Silence</c> invokes the callback with <c>""</c>. Two callbacks, two
/// flips, so the phrase landed back on the region the previous phrase had used and the second
/// region was never written. Measured in the browser on a cold start: seven consecutive
/// utterances, all seven in <c>aria-speech-1</c>. The whole purpose of the pair — making a
/// REPEATED identical phrase a text change somewhere, and therefore an announcement — did not
/// operate for interrupting speech, which is nearly all of it.</para>
/// </summary>
public sealed class SpeechLiveRegionBufferTests
{
    /// <summary>
    /// The property, stated as the user experiences it: press the same key twice, hear it twice.
    /// Asserted on the REGION the phrase lands in, because that is what makes it a DOM change —
    /// asserting the text alone passes on the defect.
    /// </summary>
    [Fact]
    public void A_repeated_phrase_lands_in_the_other_region_even_when_it_interrupts()
    {
        var buffer = new SpeechLiveRegionBuffer();

        // One interrupting utterance = Silence() then Speak(): "" then the phrase.
        buffer.Push("");
        buffer.Push("Candles, body unmuted.");
        int first = buffer.ActiveRegion;

        buffer.Push("");
        buffer.Push("Candles, body unmuted.");
        int second = buffer.ActiveRegion;

        Assert.NotEqual(first, second);
        Assert.Equal("Candles, body unmuted.", buffer.TextFor(second));
        Assert.Equal("", buffer.TextFor(first));
    }

    [Fact]
    public void Non_interrupting_speech_alternates_too()
    {
        var buffer = new SpeechLiveRegionBuffer();

        buffer.Push("Order book.");
        int first = buffer.ActiveRegion;
        buffer.Push("Order book.");

        Assert.NotEqual(first, buffer.ActiveRegion);
    }

    /// <summary>Exactly one region holds text at a time — the other must be empty, or the
    /// bottom of the page reads as two identical lines in browse mode.</summary>
    [Fact]
    public void Only_one_region_ever_holds_text()
    {
        var buffer = new SpeechLiveRegionBuffer();

        foreach (string phrase in new[] { "One.", "", "Two.", "Two.", "", "Three." })
            buffer.Push(phrase);

        Assert.Equal("", buffer.TextFor(buffer.ActiveRegion == 1 ? 2 : 1));
        Assert.Equal("Three.", buffer.TextFor(buffer.ActiveRegion));
    }

    /// <summary>
    /// The linger clear empties the page without moving the alternation on. A clear that also
    /// flipped would put the next phrase back on the region the last one used — the same defect
    /// this class fixes, arriving from the other side.
    /// </summary>
    [Fact]
    public void Clearing_empties_both_regions_without_flipping()
    {
        var buffer = new SpeechLiveRegionBuffer();
        buffer.Push("Candles, body unmuted.");
        int announced = buffer.ActiveRegion;

        buffer.Clear();

        Assert.Equal("", buffer.TextFor(1));
        Assert.Equal("", buffer.TextFor(2));
        Assert.Equal(announced, buffer.ActiveRegion);

        // …and the next phrase still alternates away from the region that just spoke.
        buffer.Push("Candles, body unmuted.");
        Assert.NotEqual(announced, buffer.ActiveRegion);
    }

    /// <summary>
    /// <c>Push</c> tells the caller whether to (re)arm the linger timer. An empty callback must
    /// not arm it — otherwise <c>Silence()</c> alone would schedule a clear of a region that is
    /// about to be written, and a burst of speech would blank mid-burst.
    /// </summary>
    [Fact]
    public void Only_a_real_phrase_asks_for_the_linger_timer()
    {
        var buffer = new SpeechLiveRegionBuffer();

        Assert.False(buffer.Push(""));
        Assert.False(buffer.Push(null));
        Assert.True(buffer.Push("Ready."));
    }
}
