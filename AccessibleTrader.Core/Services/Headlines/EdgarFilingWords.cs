using AccessibleTrader.Core.Services.MarketEvents;

namespace AccessibleTrader.Core.Services.Headlines
{
    /// <summary>
    /// What a filing is, in words, and how much it matters — the one table both the live notice
    /// and the dossier's list read.
    ///
    /// <para>
    /// <b>Importance.</b> The same three levels the market-event setting gates. An 8-K is as
    /// important as its most important item: results (2.02), bankruptcy (1.03), a cybersecurity
    /// incident (1.05), an impairment (2.06), a delisting notice (3.01), an auditor change (4.01),
    /// statements no longer to be relied on (4.02) and a change in control (5.01) are High.
    /// Item 9.01 (exhibits) only ever rides along, so it is dropped from the words when another
    /// item is present.
    /// </para>
    ///
    /// <para>
    /// <b>Left out on purpose.</b> A form with no row here is never announced: prospectus
    /// supplements (424B*) and free-writing prospectuses are filed by the dozen every day by any
    /// bank that issues notes, proxy soliciting material repeats the proxy, and an exchange's
    /// 25-NSE is usually a note maturing. The dossier lists them by their form name only.
    /// </para>
    /// </summary>
    public static class EdgarFilingWords
    {
        private sealed record ItemRow(string Words, MarketEventImportance Importance);

        private static readonly Dictionary<string, ItemRow> Items = new(StringComparer.Ordinal)
        {
            ["1.01"] = new("new material agreement", MarketEventImportance.Medium),
            ["1.02"] = new("material agreement ended", MarketEventImportance.Medium),
            ["1.03"] = new("bankruptcy or receivership", MarketEventImportance.High),
            ["1.04"] = new("mine safety", MarketEventImportance.Low),
            ["1.05"] = new("cybersecurity incident", MarketEventImportance.High),
            ["2.01"] = new("acquisition or sale of assets completed", MarketEventImportance.High),
            ["2.02"] = new("earnings results", MarketEventImportance.High),
            ["2.03"] = new("new debt", MarketEventImportance.Medium),
            ["2.04"] = new("debt repayment brought forward", MarketEventImportance.High),
            ["2.05"] = new("exit or job-cut costs", MarketEventImportance.Medium),
            ["2.06"] = new("write-down of assets", MarketEventImportance.High),
            ["3.01"] = new("delisting or listing rule notice", MarketEventImportance.High),
            ["3.02"] = new("unregistered share sale", MarketEventImportance.Medium),
            ["3.03"] = new("change to shareholder rights", MarketEventImportance.Medium),
            ["4.01"] = new("change of auditor", MarketEventImportance.High),
            ["4.02"] = new("past financial statements no longer reliable", MarketEventImportance.High),
            ["5.01"] = new("change in control", MarketEventImportance.High),
            ["5.02"] = new("director or officer change", MarketEventImportance.Medium),
            ["5.03"] = new("bylaws or fiscal year change", MarketEventImportance.Low),
            ["5.04"] = new("employee plan trading halt", MarketEventImportance.Low),
            ["5.05"] = new("ethics code change", MarketEventImportance.Low),
            ["5.06"] = new("shell company status change", MarketEventImportance.Medium),
            ["5.07"] = new("shareholder vote results", MarketEventImportance.Low),
            ["5.08"] = new("director nominations notice", MarketEventImportance.Low),
            ["7.01"] = new("public disclosure", MarketEventImportance.Medium),
            ["8.01"] = new("other events", MarketEventImportance.Medium),
            ["9.01"] = new("financial statements and exhibits", MarketEventImportance.Low),
        };

        private sealed record FormRow(string Spoken, string Words, MarketEventImportance Importance);

        // Keyed on the form with any "/A" removed; an amendment is said as "amended".
        private static readonly Dictionary<string, FormRow> Forms = new(StringComparer.OrdinalIgnoreCase)
        {
            ["10-Q"] = new("a 10-Q", "quarterly report", MarketEventImportance.Medium),
            ["10-K"] = new("a 10-K", "annual report", MarketEventImportance.Medium),
            ["20-F"] = new("a 20-F", "annual report", MarketEventImportance.Medium),
            ["40-F"] = new("a 40-F", "annual report", MarketEventImportance.Medium),
            ["6-K"] = new("a 6-K", "foreign company report", MarketEventImportance.Low),
            ["NT 10-Q"] = new("a late-filing notice", "quarterly report will be late", MarketEventImportance.Medium),
            ["NT 10-K"] = new("a late-filing notice", "annual report will be late", MarketEventImportance.Medium),
            ["4"] = new("a Form 4", "insider trade", MarketEventImportance.Low),
            ["3"] = new("a Form 3", "new insider", MarketEventImportance.Low),
            ["5"] = new("a Form 5", "insider trades for the year", MarketEventImportance.Low),
            ["144"] = new("a Form 144", "insider plans to sell", MarketEventImportance.Low),
            ["SC 13D"] = new("a 13D", "stake over 5 percent, may seek influence", MarketEventImportance.Medium),
            ["SCHEDULE 13D"] = new("a 13D", "stake over 5 percent, may seek influence", MarketEventImportance.Medium),
            ["SC 13G"] = new("a 13G", "passive stake over 5 percent", MarketEventImportance.Low),
            ["SCHEDULE 13G"] = new("a 13G", "passive stake over 5 percent", MarketEventImportance.Low),
            ["SC TO-T"] = new("a tender offer", "offer to buy shares from holders", MarketEventImportance.High),
            ["SC 14D9"] = new("a tender offer response", "company's answer to a tender offer", MarketEventImportance.High),
            ["S-1"] = new("an S-1", "registration of shares for sale", MarketEventImportance.Medium),
            ["S-3"] = new("an S-3", "registration of shares for sale", MarketEventImportance.Low),
            ["S-3ASR"] = new("an S-3", "registration of shares for sale", MarketEventImportance.Low),
            ["F-1"] = new("an F-1", "registration of shares for sale", MarketEventImportance.Medium),
            ["DEF 14A"] = new("a proxy statement", "shareholder meeting and vote", MarketEventImportance.Low),
            ["PREM14A"] = new("a merger proxy", "vote on a merger or sale", MarketEventImportance.High),
            ["DEFM14A"] = new("a merger proxy", "vote on a merger or sale", MarketEventImportance.High),
            ["15-12B"] = new("a Form 15", "deregistering its shares", MarketEventImportance.Medium),
            ["15-12G"] = new("a Form 15", "deregistering its shares", MarketEventImportance.Medium),
            ["15-15D"] = new("a Form 15", "deregistering its shares", MarketEventImportance.Medium),
        };

        private static bool IsAmendment(string form, out string baseForm)
        {
            baseForm = form.Trim();
            if (baseForm.EndsWith("/A", StringComparison.OrdinalIgnoreCase))
            {
                baseForm = baseForm[..^2];
                return true;
            }
            return false;
        }

        private static bool Is8K(string baseForm)
            => string.Equals(baseForm, "8-K", StringComparison.OrdinalIgnoreCase);

        /// <summary>How much the filing matters, or null when it is never announced.</summary>
        public static MarketEventImportance? Importance(string form, IReadOnlyList<string> items)
        {
            IsAmendment(form, out var baseForm);
            if (Is8K(baseForm))
            {
                MarketEventImportance? best = null;
                foreach (var item in items)
                    if (Items.TryGetValue(item, out var row) && (best == null || row.Importance < best))
                        best = row.Importance;
                // An 8-K whose items could not be read is still a current report; Medium is what
                // "other events" would be.
                return best ?? MarketEventImportance.Medium;
            }
            return Forms.TryGetValue(baseForm, out var f) ? f.Importance : null;
        }

        /// <summary>The form as it is said after "filed": "an 8-K", "an amended 10-Q", "a Form 4".</summary>
        public static string SpokenForm(string form)
        {
            bool amended = IsAmendment(form, out var baseForm);
            string spoken = Is8K(baseForm) ? "an 8-K"
                : Forms.TryGetValue(baseForm, out var f) ? f.Spoken
                : "a " + baseForm;
            if (!amended) return spoken;
            // "an 8-K" → "an amended 8-K"; "a Form 4" → "an amended Form 4".
            int space = spoken.IndexOf(' ');
            return "an amended" + spoken[space..];
        }

        /// <summary>What it is about: "earnings results", "director or officer change, other
        /// events", "insider trade". Empty for a form with no words of its own.</summary>
        public static string About(string form, IReadOnlyList<string> items)
        {
            IsAmendment(form, out var baseForm);
            if (Is8K(baseForm))
            {
                var known = items.Where(Items.ContainsKey).Distinct().ToList();
                if (known.Count > 1) known.Remove("9.01");
                if (known.Count == 0) return "current report";
                return string.Join(", ", known.Select(i => Items[i].Words));
            }
            return Forms.TryGetValue(baseForm, out var f) ? f.Words : "";
        }
    
        /// <summary>The dossier list's words: "8-K: earnings results", "Form 4: insider trade",
        /// "424B2" for a form with no words of its own.</summary>
        public static string Label(string form, IReadOnlyList<string> items)
        {
            string spoken = SpokenForm(form);
            int space = spoken.IndexOf(' ');
            string bare = space > 0 ? spoken[(space + 1)..] : spoken;
            string about = About(form, items);
            return about.Length == 0 ? bare : $"{bare}: {about}";
        }
    }
}
