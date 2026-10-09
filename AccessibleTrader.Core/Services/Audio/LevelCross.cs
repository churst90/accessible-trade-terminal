namespace AccessibleTrader.Core.Services.Audio
{
    /// <summary>
    /// The one rule for "did the value cross this line between two bars", and which way.
    ///
    /// <para>
    /// It was written inline in the sonification strategy, twice, for a reference level's crossing
    /// earcon. Horizontal line drawings now play that same earcon (Cody, 2026-10-09: <i>"vertical
    /// and horizontals should play the crossing earcon when price crosses them"</i>), and Ctrl+Left
    /// / Ctrl+Right stops where they are crossed. Three callers of one rule is the point at which a
    /// copy drifts — a jump that lands one bar away from where the chirp sounded would be the
    /// symptom — so the rule lives here and all three read it.
    /// </para>
    ///
    /// <para>
    /// At-or-above is "above": a value that rises to exactly the line has reached it, and one that
    /// falls from exactly the line to below it has left it. NaN on either side is no crossing.
    /// </para>
    /// </summary>
    public static class LevelCross
    {
        /// <returns>+1 when the value rose to or through <paramref name="level"/>, −1 when it fell
        /// below it, 0 when both bars sit on the same side.</returns>
        public static int Direction(double previous, double current, double level)
        {
            if (previous < level && current >= level) return 1;
            if (previous >= level && current < level) return -1;
            return 0;
        }
    }
}
