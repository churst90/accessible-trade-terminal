namespace AccessibleTrader.Sdk.Configuration
{
    /// <summary>
    /// Global registry for series and component metadata, including visibility blacklists
    /// and display preferences that should be consistent across all providers.
    /// </summary>
    public static class SeriesMetadataRegistry
    {
        /// <summary>
        /// Components that should be hidden from the UI and Object Tree by default,
        /// typically because they are intermediate calculation results.
        /// </summary>
        public static readonly HashSet<string> ComponentBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "FastEma",
            "SlowEma",
            "Divergence"
        };
    }
}
