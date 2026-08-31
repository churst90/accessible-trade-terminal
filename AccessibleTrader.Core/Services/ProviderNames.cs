namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// One place to decide whether two spellings of a provider name mean the same provider.
    ///
    /// <para>
    /// A provider name is not an identifier the code controls — it is a display string a
    /// plugin picks for itself ("Twelve Data", "FMP Analytics", "SEC EDGAR"), and it gets
    /// re-typed by hand wherever the app has to name a provider it has not loaded yet: a
    /// dropdown of credential targets, a market's fallback provider list, a hosted
    /// whitelist. Every one of those hand-typed copies is free to drift, and this repo has
    /// now been bitten by that four times — <c>"Fred"</c>/<c>"FRED"</c> (case),
    /// <c>"FMPAnalytics"</c>/<c>"FMP Analytics"</c> (a space), a missing <c>"My Data"</c>,
    /// and <c>"TwelveData"</c>/<c>"Twelve Data"</c> in the API-keys dropdown, which stored a
    /// working key against a provider name nothing answered to and left the symbol list
    /// stuck on "API key required".
    /// </para>
    ///
    /// <para>
    /// <b>The rule: exact first, normalized only as a fallback.</b> Callers must prefer an
    /// exact (case-insensitive) match and fall back to <see cref="Match"/> only when the
    /// exact one finds nothing — and only when the loose match is unambiguous. Loose
    /// matching is a repair for a name typed slightly wrong, never a merge: <c>FMP</c> and
    /// <c>FMP Analytics</c> are two providers holding two different keys, and a comparison
    /// that collapsed them would hand one provider the other's credential.
    /// </para>
    ///
    /// <para>
    /// Guards live in <c>ApiKeysProviderNameTests</c> and <c>ProviderNameLiteralTests</c>:
    /// this helper repairs drift at runtime, and those tests stop it being written.
    /// </para>
    /// </summary>
    public static class ProviderNames
    {
        /// <summary>
        /// The comparison key for a provider name: separators dropped, case folded.
        /// Invariant on purpose — a provider name is data, not UI text, and the Turkish
        /// dotless-i would otherwise make "Twelve Data" a different provider under tr-TR.
        /// </summary>
        public static string Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// True when two names denote the same provider, tolerating the separator and case
        /// differences that hand-typed copies pick up. Empty never matches anything,
        /// including another empty: an unset provider name is not a provider.
        /// </summary>
        public static bool Match(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
            return Normalize(a) == Normalize(b);
        }
    }
}
