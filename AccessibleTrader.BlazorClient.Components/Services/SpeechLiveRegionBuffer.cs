namespace AccessibleTrader.BlazorClient.Services
{
    /// <summary>
    /// The two ARIA live regions at the bottom of <c>MainLayout</c>, and the rule for which one
    /// a phrase goes into. This is the application's one announcing channel: nothing a screen
    /// reader says about the terminal reaches the user by any other route on the hosted site.
    ///
    /// <para><b>Why there are two.</b> A live region is announced when its text CHANGES. Saying
    /// the same sentence twice in a row — "Candles, body unmuted" after toggling twice — writes
    /// the same string to the same node, which is not a change, so the second press is silent.
    /// Alternating between two regions makes every phrase a change to whichever region it lands
    /// in, and empties the other.</para>
    ///
    /// <para><b>The defect this class exists to make testable.</b> The alternation lived inline
    /// in <c>MainLayout</c> and flipped on EVERY callback, including the empty one. Interrupting
    /// speech — the default, and what every chart command uses — is implemented by
    /// <c>SpeechFeedbackRouter</c> as <c>Silence()</c> then <c>Speak()</c>, and
    /// <c>BlazorSpeechManager.Silence</c> invokes the callback with <c>""</c>. Two callbacks, two
    /// flips: the phrase landed back on the region the PREVIOUS phrase had used, and the second
    /// region was never written at all. Measured on a cold start — seven consecutive utterances,
    /// every one of them in <c>aria-speech-1</c>. So the double buffer did not double-buffer for
    /// exactly the speech that needed it most. <see cref="Push"/> flips only for real text.</para>
    ///
    /// <para><b>Why <see cref="Clear"/> does not flip.</b> Clearing is a cosmetic step — it stops
    /// the buffers from holding their last sentence in the accessibility tree forever, which made
    /// the bottom of the page read as three lines in browse mode instead of one. If it flipped,
    /// the next phrase would land on the region the last one used and the alternation would be
    /// defeated from the other side.</para>
    /// </summary>
    public sealed class SpeechLiveRegionBuffer
    {
        /// <summary>Which region (1 or 2) currently holds <see cref="Text"/>.</summary>
        public int ActiveRegion { get; private set; } = 1;

        /// <summary>The phrase held by <see cref="ActiveRegion"/>. The other region is empty.</summary>
        public string Text { get; private set; } = "";

        /// <summary>The text rendered into region <paramref name="region"/> (1 or 2).</summary>
        public string TextFor(int region) => ActiveRegion == region ? Text : "";

        /// <summary>
        /// Accepts one callback from the speech manager. Returns true when a real phrase was
        /// announced — the caller's cue to (re)start the linger timer that later calls
        /// <see cref="Clear"/>.
        /// </summary>
        public bool Push(string? text)
        {
            Text = text ?? "";
            if (string.IsNullOrEmpty(Text)) return false;

            ActiveRegion = ActiveRegion == 1 ? 2 : 1;
            return true;
        }

        /// <summary>Empties both regions, leaving <see cref="ActiveRegion"/> where it is.</summary>
        public void Clear() => Text = "";
    }
}
