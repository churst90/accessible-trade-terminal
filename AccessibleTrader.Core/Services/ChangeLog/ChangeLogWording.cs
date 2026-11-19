using AccessibleTrader.Core.Services.Accessibility;

namespace AccessibleTrader.Core.Services.ChangeLog
{
    /// <summary>
    /// The words of a catch-up. Every item is the sentence the terminal already said when the
    /// thing happened; this class only adds the time, the category heading the first time that
    /// category appears, and the header or follow-up opener.
    /// </summary>
    public static class ChangeLogWording
    {
        public static string Press(
            ChangeLogDocument doc, ChangeLogPlan plan, DateTime nowUtc, bool followUp = false)
        {
            if (plan.Items.Count == 0) return Nothing(doc);

            var parts = new List<string>();

            if (followUp)
            {
                parts.Add($"{plan.TotalUnheard} more.");
            }
            else
            {
                // The earlier of the last catch-up and the oldest unheard item: a press that
                // left items unread moves the mark forward, and "since" must not then be later
                // than an item this press is about to read.
                DateTime oldest = ChangeLogRules.Unheard(doc).Min(e => e.TimeUtc);
                DateTime since = doc.LastCatchUpUtc is { } last && last < oldest ? last : oldest;
                parts.Add(
                    $"{plan.TotalUnheard} {(plan.TotalUnheard == 1 ? "change" : "changes")} "
                    + $"since {Time(since)}, {Age(since, nowUtc)} ago.");
            }

            var seen = new HashSet<ChangeLogCategory>();
            foreach (var entry in plan.Items)
            {
                string body = Strip(entry.Text);
                string item = body.Length == 0
                    ? $"{Time(entry.TimeUtc)}."
                    : $"{Time(entry.TimeUtc)}, {body}.";
                if (seen.Add(entry.Category))
                    item = $"{CategoryWord(entry.Category)}: {item}";
                parts.Add(item);
            }

            if (plan.Remaining > 0)
                parts.Add($"Press Alt+U again for {plan.Remaining} more.");

            return string.Join(" ", parts);
        }

        public static string Nothing(ChangeLogDocument doc)
        {
            var entries = doc.Entries ?? new List<ChangeLogEntry>();
            if (entries.Count == 0 && doc.LastCatchUpUtc == null)
                return "Nothing recorded yet.";

            DateTime since = doc.LastCatchUpUtc ?? entries.Max(e => e.TimeUtc);
            return $"Nothing new since {Time(since)}.";
        }

        public static string ReturnPrompt(DateTime lastPresentUtc, int changedCount, DateTime nowUtc)
            => $"You were away {Age(lastPresentUtc, nowUtc)}, "
             + $"{changedCount} {(changedCount == 1 ? "thing" : "things")} changed, "
             + "Alt+U to hear them.";

        /// <summary>
        /// "47 minutes", "1 hour 5 minutes", "2 days" — the spoken age the header and the return
        /// prompt share. Rounded down, so it never claims more time away than there was.
        /// </summary>
        public static string Age(DateTime fromUtc, DateTime toUtc)
        {
            TimeSpan span = toUtc - fromUtc;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;

            long minutes = (long)span.TotalMinutes;
            if (minutes < 1) return "under a minute";
            if (minutes < 60) return Unit(minutes, "minute");

            long hours = minutes / 60;
            long remainder = minutes % 60;
            if (hours < 24)
                return remainder == 0
                    ? Unit(hours, "hour")
                    : $"{Unit(hours, "hour")} {Unit(remainder, "minute")}";

            return Unit(hours / 24, "day");
        }

        public static string CategoryWord(ChangeLogCategory category) => category switch
        {
            ChangeLogCategory.Money => "Money",
            ChangeLogCategory.Alert => "Alerts",
            ChangeLogCategory.Event => "Events",
            ChangeLogCategory.Unusual => "Unusual moves",
            ChangeLogCategory.Signal => "Signals",
            ChangeLogCategory.Connection => "Connection",
            _ => "Changes",
        };

        private static string Time(DateTime timeUtc) => SpeechTimeFormatter.FormatTime(timeUtc);

        /// <summary>A stored sentence keeps its words; only its trailing period is removed so
        /// the separator never says ". .".</summary>
        private static string Strip(string? text)
            => string.IsNullOrEmpty(text) ? "" : text.Trim().TrimEnd('.').TrimEnd();

        private static string Unit(long value, string unit)
            => value == 1 ? $"1 {unit}" : $"{value} {unit}s";
    }
}
