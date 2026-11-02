namespace AccessibleTrader.Sdk.Interfaces;

/// <summary>
/// Monitors narrated series for new indicator signals and zone transitions,
/// announcing them via TTS as they occur on live bar closes.
/// Self-wires via EventBus subscriptions in the constructor — no public trigger methods.
/// </summary>
public interface IAutoNarrationService
{
    /// <summary>
    /// Whether a bar-close narration scan is going to run for the bar that just closed — i.e.
    /// whether handing this service a sentence with <see cref="DeferBarCloseSentence"/> will
    /// get it spoken.
    ///
    /// <para>
    /// Exists so that ONE utterance describes a bar close. The new-bar announcement and the
    /// indicator narration are produced by two different services from two different events
    /// (the store publishes <c>NewBarEvent</c> the moment it commits; the narration scan runs on
    /// the <c>RedrawEvent</c> that follows the recalculation), and both were calling Speak. On
    /// the web head speech is an ARIA live region, so the second write replaces the first before
    /// a screen reader announces it — which is exactly the defect
    /// <c>NavigationFeedbackManager</c>'s "one utterance per bar" composition was written for,
    /// arriving on the other route.
    /// </para>
    /// </summary>
    bool WillNarrateBarClose();

    /// <summary>
    /// Hand the bar-close sentence to the narrator: <paramref name="sentence"/> (the close) is
    /// spoken FIRST in the utterance the bar-close scan is about to produce, and
    /// <paramref name="last"/> (the new bar's open) after the scan's findings. Cody, 2026-11-02:
    /// "when a bar closes, I should hear the close and the indicator narration ladder. Then the
    /// open of the newest bar should be read as well." Speak both directly instead when
    /// <see cref="WillNarrateBarClose"/> is false — nothing here will.
    /// </summary>
    void DeferBarCloseSentence(string sentence, string? last = null);
}
