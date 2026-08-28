namespace AccessibleTrader.Sdk.Configuration
{
    /// <summary>
    /// Legacy duplicate of <see cref="AccessibleTrader.Sdk.Models.TimeframeUtility"/>.
    /// Prefer the <c>Models</c> version — it uses a single regex-based parser that
    /// handles any <c>&lt;N&gt;&lt;unit&gt;</c> combination (3m, 8h, 2w, etc.) rather
    /// than a hardcoded switch, and also exposes <c>AllTimeframes</c> /
    /// <c>GetBestBaseTimeframe</c> / <c>GetPeriodStart</c> for provider timeframe
    /// translation. Kept here so existing plugin DLLs that binary-link to this
    /// namespace continue to load, but all in-tree callers have migrated.
    /// </summary>
    [Obsolete("Use AccessibleTrader.Sdk.Models.TimeframeUtility — it handles arbitrary " +
              "<N><unit> combinations via regex and carries the shared timeframe list.")]
    public static class TimeframeUtility
    {
        public static int ToSeconds(string timeframe)
        {
            // NOTE: "1M" (month) must be checked BEFORE ToLower() to avoid collision with "1m" (minute).
            if (timeframe == "1M" || timeframe == "1month" || timeframe == "1mth") return 2592000;

            return timeframe.ToLowerInvariant() switch
            {
                "1m" or "1min" => 60,
                "3m" or "3min" => 180,
                "5m" or "5min" => 300,
                "15m" or "15min" => 900,
                "30m" or "30min" => 1800,
                "1h" or "1hour" => 3600,
                "2h" or "2hour" => 7200,
                "4h" or "4hour" => 14400,
                "6h" or "6hour" => 21600,
                "12h" or "12hour" => 43200,
                "1d" or "1day" => 86400,
                "3d" or "3day" => 259200,
                "1w" or "1week" => 604800,
                _ => -1
            };
        }

        public static string Normalize(string timeframe)
        {
            int seconds = ToSeconds(timeframe);
            return seconds switch
            {
                60 => "1m",
                180 => "3m",
                300 => "5m",
                900 => "15m",
                1800 => "30m",
                3600 => "1h",
                7200 => "2h",
                14400 => "4h",
                21600 => "6h",
                43200 => "12h",
                86400 => "1d",
                259200 => "3d",
                604800 => "1w",
                2592000 => "1M",
                _ => timeframe
            };
        }
    }
}
