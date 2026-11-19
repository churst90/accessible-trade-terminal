using AccessibleTrader.Core.Services.Settings;

namespace AccessibleTrader.Core.Services.GoBar
{
    /// <summary>How a setting matched. Labels use the command tiers; keywords are last.</summary>
    public enum GoBarSettingMatchKind
    {
        ExactLabel,
        Prefix,
        WordStart,
        NearMiss,
        Keyword,
    }

    public sealed record GoBarSettingMatch(SettingEntry Entry, GoBarSettingMatchKind How);

    /// <summary>
    /// The Settings registry seen through the Go bar's rules: a label is matched like a command
    /// name (exact, prefix, word start, then a near miss for four characters or more), and a
    /// keyword only when the typed text is a whole word from the row's keyword list. A keyword
    /// match ranks below every label match, including a near miss.
    ///
    /// <para>Only rows this host renders are offered. The flags come from a
    /// <see cref="SettingsHost"/> built from the same services the Settings dialog reads, so
    /// the bar can never send the user to a control that is not there.</para>
    /// </summary>
    public static class GoBarSettingsSearch
    {
        public const int MaxMatches = 20;

        public static IReadOnlyList<GoBarSettingMatch> Find(string? input, SettingsHost host)
        {
            string wanted = GoBarMatcher.Normalize(input);
            if (wanted.Length == 0 || host == null) return Array.Empty<GoBarSettingMatch>();

            var found = new List<(int Tier, int Index, GoBarSettingMatch Match)>();
            for (int i = 0; i < SettingsRegistry.All.Count; i++)
            {
                var entry = SettingsRegistry.All[i];
                if (!SettingsRegistry.IsAvailable(entry.ControlId, host)) continue;

                var match = BestFor(wanted, entry);
                if (match == null) continue;
                found.Add((Tier(match.How), i, match));
            }

            return found
                .OrderBy(x => x.Tier)
                .ThenBy(x => x.Index)
                .Take(MaxMatches)
                .Select(x => x.Match)
                .ToList();
        }

        private static int Tier(GoBarSettingMatchKind how) => how switch
        {
            GoBarSettingMatchKind.ExactLabel => 0,
            GoBarSettingMatchKind.Prefix => 1,
            GoBarSettingMatchKind.WordStart => 2,
            GoBarSettingMatchKind.NearMiss => 3,
            _ => 4,
        };

        private static GoBarSettingMatch? BestFor(string wanted, SettingEntry entry)
        {
            string label = GoBarMatcher.Normalize(entry.Label);

            if (label == wanted)
                return new GoBarSettingMatch(entry, GoBarSettingMatchKind.ExactLabel);

            if (label.StartsWith(wanted, StringComparison.Ordinal))
                return new GoBarSettingMatch(entry, GoBarSettingMatchKind.Prefix);

            foreach (string word in label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.StartsWith(wanted, StringComparison.Ordinal))
                    return new GoBarSettingMatch(entry, GoBarSettingMatchKind.WordStart);
            }

            if (wanted.Length >= 4)
            {
                int allowed = wanted.Length >= 8 ? 2 : 1;
                if (GoBarMatcher.WithinDistance(label, wanted, allowed))
                    return new GoBarSettingMatch(entry, GoBarSettingMatchKind.NearMiss);
            }

            foreach (string keyword in entry.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (GoBarMatcher.Normalize(keyword) == wanted)
                    return new GoBarSettingMatch(entry, GoBarSettingMatchKind.Keyword);
            }

            return null;
        }
    }
}
