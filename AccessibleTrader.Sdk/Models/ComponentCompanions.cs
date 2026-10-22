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
    /// Three suffixes are read by the chart itself, for any indicator:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="Label"/>: a number printed at the bar, on the side opposite a marker (or
    /// above the bar for a value-anchored marker), with the component's <c>MarkerText</c> as its
    /// suffix — Cipher C's 49D above a daily cycle low, whose triangle is under it.</item>
    /// <item><see cref="Tint"/>: what a background tint is painted from instead of the component's
    /// own value, so a component can hold a number (a day count) and tint by a state (inside its
    /// timing window). Present but NaN on a bar = no tint on that bar.</item>
    /// <item><see cref="Stops"/>: the bars Ctrl+Left/Right stops on for this component — any bar
    /// whose companion value is not NaN — for a component with a value on every bar whose news is
    /// on a few (a timing window opening and closing).</item>
    /// </list>
    /// </summary>
    public static class ComponentCompanions
    {
        public const string Label = "_label";
        public const string Tint = "_tint";
        public const string Stops = "_stops";

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
