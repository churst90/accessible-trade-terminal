using System;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Which tab an arrow key should move to inside a <c>role="tablist"</c>.
    ///
    /// <para>
    /// ── Why this is one function ───────────────────────────────────────────────
    /// A tablist that sets a roving <c>tabindex</c> (<c>0</c> on the active tab, <c>-1</c> on
    /// the rest) has told the browser that Tab reaches the group and ARROWS move within it.
    /// Implement half of that and the other tabs become unreachable by keyboard entirely —
    /// which is what happened to Settings: five of its six tabs, including the whole keyboard
    /// rebinding UI and the paper-account reset, were mouse-only.
    /// </para>
    ///
    /// <para>
    /// The app has eight tablists and they were built by different hands at different times:
    /// one uses <c>aria-activedescendant</c> with its own arrow handler, six leave every tab a
    /// plain Tab stop, and one set a roving tabindex and stopped there. The rule is written
    /// once here so the ninth inherits an answer instead of picking a third convention.
    /// </para>
    /// </summary>
    public static class TablistNavigator
    {
        /// <summary>
        /// The index <paramref name="key"/> should move focus to, or <c>null</c> when the key
        /// is not a tablist navigation key and the caller should leave the event alone.
        ///
        /// <para>
        /// Wraps at both ends, which is what WAI-ARIA specifies for tabs: from the last tab,
        /// Right returns to the first. Wrapping matters more than it looks — without it, a
        /// user who cannot see the row has no cue that they have run out of tabs, and a dead
        /// key is indistinguishable from a broken one.
        /// </para>
        /// </summary>
        /// <param name="key">The <c>KeyboardEventArgs.Key</c> value.</param>
        /// <param name="current">Index of the currently selected tab.</param>
        /// <param name="count">How many tabs are in the list.</param>
        /// <param name="vertical">
        /// True for a vertically stacked tablist, which uses Up/Down instead of Left/Right.
        /// Home and End work in both orientations.
        /// </param>
        public static int? Target(string? key, int current, int count, bool vertical = false)
        {
            if (count <= 0 || string.IsNullOrEmpty(key)) return null;

            // Clamp rather than trust: a stale index from a tab list that shrank would
            // otherwise produce a negative modulo below.
            int at = Math.Clamp(current, 0, count - 1);

            string forward  = vertical ? "ArrowDown" : "ArrowRight";
            string backward = vertical ? "ArrowUp"   : "ArrowLeft";

            int target;
            if (key == forward)       target = (at + 1) % count;
            else if (key == backward) target = (at - 1 + count) % count;
            else if (key == "Home")   target = 0;
            else if (key == "End")    target = count - 1;
            else return null;

            // A single-tab list has nowhere to go. Reporting null lets the caller skip
            // both the focus call and the preventDefault.
            return target == at ? null : target;
        }
    }
}
