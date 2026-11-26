namespace AccessibleTrader.Sdk.Interfaces
{
    /// <summary>
    /// Optional capability for venues that cancel an account's open orders at a time the
    /// terminal names unless the terminal pushes that time further ahead first: a dead-man's
    /// switch. The terminal keeps moving the time forward while it runs and clears it on a
    /// normal close, so only a crash, a forced shutdown or a lost connection lets it fire.
    /// Hyperliquid's <c>scheduleCancel</c> is the first; it never closes a position.
    /// </summary>
    public interface IDeadMansSwitch
    {
        /// <summary>Whether this provider holds credentials that can set the switch.</summary>
        bool CanScheduleCancel { get; }

        /// <summary>
        /// Cancel every open order at <paramref name="at"/>, replacing any earlier time, or
        /// clear the switch when null. Returns null on success, or the reason in words.
        /// </summary>
        Task<string?> ScheduleCancelAsync(DateTimeOffset? at);
    }
}
