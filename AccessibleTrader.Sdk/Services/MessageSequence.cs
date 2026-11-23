namespace AccessibleTrader.Sdk.Services
{
    /// <summary>
    /// For venues that number every message on a socket (Coinbase's <c>sequence_num</c>,
    /// Gemini's <c>socket_sequence</c>): a number that does not follow the last one means a
    /// message was lost on the way.
    /// </summary>
    public static class MessageSequence
    {
        /// <summary>True when <paramref name="seq"/> does not follow the last one seen on this
        /// socket. -1 (no number) is not a gap and changes nothing; <paramref name="last"/> is -1
        /// on a new socket, whose first number is never a gap.</summary>
        public static bool IsGap(ref long last, long seq)
        {
            if (seq < 0) return false;
            long before = Interlocked.Exchange(ref last, seq);
            return before >= 0 && seq != before + 1;
        }
    }
}
