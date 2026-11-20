namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// The order Cody decided the catch-up speaks in. Every category exists from stage 5b so
    /// stage 5c only has to add producers for <see cref="Unusual"/> and <see cref="Signal"/>.
    /// <see cref="News"/> (headlines) was added 2026-11-20 after Events: a filing is news about
    /// one company, read after the market-wide events. Stored by name, so inserting it is safe.
    /// </summary>
    public enum ChangeLogCategory
    {
        Money,
        Alert,
        Event,
        News,
        Unusual,
        Signal,
        Connection,
    }

    /// <summary>
    /// One thing that changed while the user was not listening. <see cref="Key"/> is the
    /// de-duplication key: the same fill seen by a browser circuit and by the headless session
    /// must make one entry, while two different fills must make two.
    /// <see cref="Text"/> is the sentence the terminal already spoke (or would have spoken).
    /// </summary>
    public sealed record ChangeLogEntry(
        string Key,
        DateTime TimeUtc,
        ChangeLogCategory Category,
        string Text,
        string? Symbol,
        bool Heard);

    /// <summary>The shape of <c>change-log.json</c>.</summary>
    public sealed record ChangeLogDocument(
        List<ChangeLogEntry> Entries,
        DateTime? LastCatchUpUtc = null,
        DateTime? LastPresentUtc = null)
    {
        public static ChangeLogDocument Empty => new(new List<ChangeLogEntry>());
    }

    /// <summary>Which unheard entries one press reads, and how many unheard there are in all.</summary>
    public sealed record ChangeLogPlan(IReadOnlyList<ChangeLogEntry> Items, int TotalUnheard)
    {
        public int Remaining => TotalUnheard - Items.Count;
    }
}
