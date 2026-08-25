namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Categorises a <see cref="JournalEntry"/> so the journal modal can filter
    /// and so screen-reader review can quickly partition speech vs alerts vs setups.
    /// </summary>
    public enum JournalEntryKind
    {
        /// <summary>A line of TTS speech that was emitted by <see cref="ISpeechManager.Speak"/>.</summary>
        Speech,
        /// <summary>A general info / status announcement (FeedbackRequestEvent of type Info).</summary>
        Info,
        /// <summary>A user-defined price/indicator alert that fired.</summary>
        Alert,
        /// <summary>A strategy emitted a buy/sell signal (Suggestion or Auto mode).</summary>
        StrategySignal,
        /// <summary>A backtest run completed and produced a summary.</summary>
        Backtest,
        /// <summary>An error or warning surfaced from a service.</summary>
        Error
    }

    /// <summary>
    /// One row in the journal — every TTS phrase, alert, strategy signal, error, etc.
    /// Stored in a ring buffer in <see cref="IJournalService"/>.
    /// </summary>
    /// <param name="Timestamp">Local timestamp the entry was recorded.</param>
    /// <param name="Kind">High-level category for filtering.</param>
    /// <param name="Source">Free-form source label, e.g. indicator code, strategy name, "TTS".</param>
    /// <param name="Symbol">Symbol associated with the entry, or null if not bound to a chart.</param>
    /// <param name="Text">Plain text body — exactly what would be spoken or displayed.</param>
    public record JournalEntry(
        DateTime Timestamp,
        JournalEntryKind Kind,
        string Source,
        string? Symbol,
        string Text);

    /// <summary>
    /// A persistent, filterable, copyable history of every speech utterance and alert
    /// emitted by the application. Designed so a blind trader can review what was just
    /// announced, copy it out as text, and replay the reasoning behind a strategy alert.
    ///
    /// The journal is intentionally a single chokepoint: speech goes through
    /// <see cref="ISpeechManager"/>, alerts/strategy signals come in via the EventBus.
    /// Anything that should be reviewable later must end up here.
    /// </summary>
    public interface IJournalService
    {
        /// <summary>Append a new entry. Capped to a fixed maximum (oldest evicted).</summary>
        void Add(JournalEntry entry);

        /// <summary>Convenience: log a TTS phrase that just left the speech manager.</summary>
        void AddSpeech(string text);

        /// <summary>Snapshot of all entries currently in the buffer, oldest first.</summary>
        IReadOnlyList<JournalEntry> Snapshot();

        /// <summary>Drops every entry. Used by the modal's Clear button.</summary>
        void Clear();

        /// <summary>Maximum number of entries retained (the ring buffer size).</summary>
        int Capacity { get; }

        /// <summary>Fired whenever an entry is added so the modal can re-render live.</summary>
        event Action<JournalEntry>? EntryAdded;
    }
}
