namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// <b>Companion arrays: per-bar numbers a component carries beside its own value.</b>
    ///
    /// <para>
    /// A provider may write, next to a component's own array, arrays keyed
    /// <c>"{component Name}{suffix}"</c>. They are not components: they have no keyboard stop, no
    /// sound and no line of their own. They are the component's context. All of them are handed to
    /// <c>IIndicatorProvider.GetComponentSpeech</c>, so a component's speech can say what its value
    /// means ("a 49 day cycle, confirmed 9 days later") without a hidden component carrying each
    /// number. Cody, 2026-10-22: <i>"Each component must hold value, simply hiding and muting it
    /// isn't what I want."</i>
    /// </para>
    ///
    /// <para>
    /// These suffixes are read by the chart itself, for any indicator:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Label"/>: a number printed beside the bar's marker, to its right, with the
    /// component's <c>MarkerText</c> as its suffix — Cipher C's 49D beside a daily cycle low's
    /// triangle, as the original prints it (2026-10-28; it was printed over the bar before).</item>
    /// <item><see cref="Tint"/>: what a background tint is painted from instead of the component's
    /// own value, so a component can hold a number (a day count) and tint by a state (inside its
    /// timing window). Present but NaN on a bar = no tint on that bar.</item>
    /// <item><see cref="Stops"/>: the bars Ctrl+Left/Right stops on for this component — any bar
    /// whose companion value is not NaN — for a component with a value on every bar whose news is
    /// on a few (a timing window opening and closing).</item>
    /// <item><see cref="Path"/>: a line in the component's own units (price, on the price pane)
    /// drawn in the component's colour, thickness and dash: a picture that belongs to the
    /// component, such as Cipher C's cycle arc on its timing window. Not a keyboard stop, not a
    /// sound, no reading of its own; what it shows is said by the component it belongs to. It is
    /// named by the component's <c>PathName</c> when the user lands on the component, and the
    /// user turns it off in Properties → Appearance. It counts toward the pane's fitted range,
    /// so it is not cut off.</item>
    /// <item><see cref="Confirms"/>: on a bar where a component confirms a mark painted in
    /// hindsight (the component another one names as its <c>ConfirmedBy</c>), how many bars back
    /// that mark is painted. New-bar narration names the mark's date and price from it — the
    /// latest mark painted before the confirmation is not always the one it confirmed (that one
    /// may have been cancelled since, or the mark moved onto a lower tier's).</item>
    /// <item><see cref="BoxEnd"/> (with <see cref="BoxStart"/>, <see cref="BoxTop"/>,
    /// <see cref="BoxBottom"/>): a filled box in the component's colour at its <c>BoxAlpha</c>,
    /// from <c>BoxStart</c> bars (default 0, may be negative) to <c>BoxEnd</c> bars from the bar
    /// it is written on, between two prices (NaN = the pane's edge), drawn under the candles.
    /// The right edge may run past the last bar — Cipher C's live timing window. A picture only,
    /// like <see cref="Path"/>: no stop, no sound.</item>
    /// <item><see cref="Badge"/>: small signs drawn under a <c>TriangleUpRing</c> marker's ring
    /// on a bar, a bit set (<see cref="BadgeCheck"/>, <see cref="BadgeClock"/>,
    /// <see cref="BadgeRefresh"/>, <see cref="BadgeCross"/>); <see cref="BadgeWord"/> picks a word
    /// from the component's <c>BadgeWords</c> printed under them. What they mean is said by the
    /// component's speech from its own companions.</item>
    /// <item><see cref="Mark"/>: the component's marker drawn (with its signs and word) on a bar
    /// where the component has no value — Cipher C's pending and cancelled lows, which the
    /// original draws as lows but which are not lows. A picture only: not a stop, a sound, a
    /// value or a dotted line; what it shows is said by another component.</item>
    /// </list>
    /// </summary>
    public static class ComponentCompanions
    {
        public const string Label = "_label";
        public const string Tint = "_tint";
        public const string Stops = "_stops";
        public const string Path = "_path";
        public const string Confirms = "_confirms";
        public const string BoxStart = "_boxStart";
        public const string BoxEnd = "_boxEnd";
        public const string BoxTop = "_boxTop";
        public const string BoxBottom = "_boxBottom";
        public const string Badge = "_badge";
        public const string BadgeWord = "_badgeWord";
        public const string Mark = "_mark";

        /// <summary><see cref="Badge"/> bits, drawn away from the ring in this order: clock,
        /// refresh, check, cross.</summary>
        public const int BadgeCheck = 1, BadgeClock = 2, BadgeRefresh = 4, BadgeCross = 8;

        /// <summary>Whether <paramref name="key"/> is a companion of one of <paramref name="componentNames"/>.</summary>
        public static bool IsCompanionOf(string key, IEnumerable<string> componentNames)
        {
            foreach (var name in componentNames)
                if (key.Length > name.Length + 1 && key.StartsWith(name, StringComparison.Ordinal) && key[name.Length] == '_')
                    return true;
            return false;
        }
    }
}
