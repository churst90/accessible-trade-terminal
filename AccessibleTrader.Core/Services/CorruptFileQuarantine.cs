namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// Recovery net for unreadable persistence files (2026-06-12 audit fix 6).
    ///
    /// Every JSON store in the app (config, settings, strategy library, indicator
    /// preferences) handled corrupt files the same dangerous way: catch → log at
    /// debug/trace level → return empty. Two problems with that:
    ///
    ///  1. The NEXT save overwrites the corrupt-but-recoverable original with the
    ///     empty state, permanently destroying user data (worst case: a corrupt
    ///     strategies.json silently becomes seeds-only and the user's strategy
    ///     library is gone on the following save).
    ///  2. A blind user gets no feedback at all — their settings just "reset"
    ///     with no explanation.
    ///
    /// This helper fixes both: the unreadable file is MOVED aside to
    /// <c>&lt;name&gt;.corrupt-&lt;utc-stamp&gt;</c> (so the fresh default starts clean and
    /// the original stays recoverable), and the event is recorded in a session
    /// report that <c>AppStartupService</c> announces through speech once the
    /// accessibility pipeline is up. Stores load far too early to speak for
    /// themselves — the report decouples detection from announcement.
    /// </summary>
    public static class CorruptFileQuarantine
    {
        private static readonly object _lock = new();
        private static readonly List<string> _reports = new();

        /// <summary>
        /// Human-readable descriptions of every quarantine performed this session.
        /// Read (and typically announced) by AppStartupService; cleared by tests.
        /// </summary>
        public static IReadOnlyList<string> SessionReports
        {
            get { lock (_lock) return _reports.ToArray(); }
        }

        public static void ClearSessionReports()
        {
            lock (_lock) _reports.Clear();
        }

        /// <summary>
        /// Moves the unreadable file at <paramref name="path"/> aside and records the
        /// event. Never throws — if even the move fails the event is still recorded
        /// so the user hears about the problem. Returns the quarantine path, or null
        /// when the file could not be moved.
        /// </summary>
        public static string? MoveAside(string path, Exception cause)
        {
            string name = Path.GetFileName(path);
            string? quarantinePath = null;
            try
            {
                if (File.Exists(path))
                {
                    quarantinePath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    // Stamp collisions (two quarantines in the same second) get a suffix.
                    if (File.Exists(quarantinePath))
                        quarantinePath += "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
                    File.Move(path, quarantinePath);
                }
            }
            catch
            {
                quarantinePath = null; // recorded below either way
            }

            string report = quarantinePath != null
                ? $"{name} was unreadable ({cause.GetType().Name}) and has been reset. " +
                  $"The original was saved as {Path.GetFileName(quarantinePath)}."
                : $"{name} was unreadable ({cause.GetType().Name}) and has been reset.";
            lock (_lock) _reports.Add(report);
            return quarantinePath;
        }
    }
}
