namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// How "hidden" and "muted" are said — in ONE place, because they are two independent flags
    /// and every site that spoke them treated them as one.
    ///
    /// <para><b>The defect, reported from real use on 2026-09-04.</b> Cody: "if I hide and mute
    /// both at once, if I unhide it should say muted but it doesn't. If both hidden and muted,
    /// then both hidden and muted should be reported when I up/down over the components, and when
    /// one is unmuted or unhidden, only that qualifier should be removed from being spoken."</para>
    ///
    /// <para>He is describing a lattice of four states being reported as a chain of two. The
    /// navigation readout in <c>SpeechFormatter</c> read
    /// <c>!IsVisible ? "Hidden. " : IsMuted ? "Muted. " : ""</c> — an if/else, so hidden WON and a
    /// component that was both never said so; <c>HiddenComponentStrategy</c> said
    /// "{name}: hidden" and never mentioned mute at all; and the toggle confirmations announced
    /// only the flag they had just flipped, so unhiding something still muted said "visible" and
    /// left the user to discover the silence.</para>
    ///
    /// <para><b>Why it matters more than it sounds.</b> The two flags fail the same way from the
    /// user's side — the component makes no sound — and are cleared by different keys. Being told
    /// "visible" by a component that stays silent is the terminal reporting a state it is not in;
    /// the user presses h again, hides it, and is now further from what they wanted. Hidden AND
    /// muted also means two keys are needed, and a readout that names one of them is a readout
    /// that guarantees a second wrong guess.</para>
    /// </summary>
    public static class VisibilityStateSpeech
    {
        /// <summary>
        /// The state of a series or component as one spoken clause — every combination of the two
        /// flags, lower case and unpunctuated so a caller can place it: <c>"hidden and muted"</c>,
        /// <c>"hidden"</c>, <c>"muted"</c>, or <c>""</c> when neither applies.
        /// </summary>
        public static string Qualifier(bool isVisible, bool isMuted) => (isVisible, isMuted) switch
        {
            (false, true) => "hidden and muted",
            (false, false) => "hidden",
            (true, true) => "muted",
            _ => "",
        };

        /// <summary>
        /// The sentence-leading form a navigation readout puts in front of the value:
        /// <c>"Hidden and muted. "</c>, or <c>""</c> when there is nothing to say. Spoken on
        /// Up/Down (the component switch), never on the Left/Right value scan — a qualifier
        /// repeated in front of every bar of a sweep is the prefix this repo has deleted twice.
        /// </summary>
        public static string Prefix(bool isVisible, bool isMuted)
        {
            string q = Qualifier(isVisible, isMuted);
            if (q.Length == 0) return "";
            return char.ToUpperInvariant(q[0]) + q.Substring(1) + ". ";
        }

        /// <summary>
        /// The clause a toggle confirmation appends when the OTHER flag is still set:
        /// <c>", muted"</c> after an unhide that leaves it muted, <c>", hidden"</c> after an
        /// unmute that leaves it hidden, <c>""</c> when the component is now plainly audible.
        ///
        /// <para>The verb the user just caused stays in front of it ("Close visible, muted"), so
        /// the sentence says what the keypress did AND what is still standing in the way.</para>
        /// </summary>
        public static string OtherFlagClause(bool otherFlagIsSet, string word)
            => otherFlagIsSet ? $", {word}" : "";
    }
}
