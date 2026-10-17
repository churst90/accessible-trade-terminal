namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// <b>Indicators that used to exist, and why each went.</b>
    ///
    /// <para>A saved chart, a strategy or an alert written while one of these existed still names
    /// it by code. Without this list each of those finds nothing: the restore path rebuilt an
    /// unknown code verbatim, as a series with no provider behind it (a chart line that never
    /// draws and never speaks); a strategy leaf resolved to no signal and was false on every bar;
    /// an alert on it stayed armed and could not fire. For a user who cannot see the chart all
    /// three are silence, and silence is indistinguishable from a market that never got there.
    /// Listed here, each of those paths says ONCE, in plain words, what was removed and what to
    /// use instead — the same treatment <c>SignalCatalog.Retired</c> gives a retired component.</para>
    ///
    /// <para><b>SPIDER_LINES</b> — removed 2026-10-17 on Cody's report. It was eight
    /// Fibonacci-length EMAs plus a "Stacking Score", every EMA flagged as a zone line, and had
    /// nothing to do with Market Cipher's real spider lines (CryptoFace's diagonal support and
    /// resistance lines on the BTC log chart). "I'd just remove the indicator for now."</para>
    /// </summary>
    public static class RetiredIndicators
    {
        private sealed record Entry(string Name, string RetiredOn, string What, string Instead, Func<string, string?> InsteadFor);

        private static readonly Dictionary<string, Entry> ByCode = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPIDER_LINES"] = new Entry(
                Name: "Spider Lines",
                RetiredOn: "2026-10-17",
                What: "it was eight exponential moving averages, lengths 8 to 200, under a misleading name, "
                    + "and had nothing to do with Market Cipher's spider lines, which are diagonal support and resistance lines",
                Instead: "add an EMA for each length you want",
                // Its components were "EMA 8" … "EMA 200" and "Stacking Score". A leaf on one EMA
                // has an exact replacement; the score has none.
                InsteadFor: component =>
                    component.StartsWith("EMA ", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(component.AsSpan(4), out int length)
                        ? $"use an EMA with length {length} instead"
                        : null),
        };

        /// <summary>Whether <paramref name="code"/> names an indicator that was removed.</summary>
        public static bool IsRetired(string? code) => code != null && ByCode.ContainsKey(code);

        /// <summary>Every retired code. For guards: none of them may be registered again.</summary>
        public static IReadOnlyCollection<string> Codes => ByCode.Keys;

        /// <summary>The name the user knew it by, or null when the code is not retired.</summary>
        public static string? NameOf(string? code) =>
            code != null && ByCode.TryGetValue(code, out var e) ? e.Name : null;

        /// <summary>
        /// Why an indicator is gone, as a clause the caller can frame ("Alert … cannot fire: "
        /// + this + "."). No trailing full stop. Null when the code is not retired.
        /// </summary>
        public static string? Reason(string? code) =>
            code != null && ByCode.TryGetValue(code, out var e)
                ? $"{e.Name} was removed on {e.RetiredOn}: {e.What}; {e.Instead} instead"
                : null;

        /// <summary>
        /// Why a strategy signal <c>{CODE}.{Component}</c> on a retired indicator cannot be read,
        /// or null when its indicator is not retired. Component-specific where there is an exact
        /// replacement ("use an EMA with length 21 instead"). No trailing full stop.
        /// </summary>
        public static string? SignalReason(string? signalId)
        {
            if (string.IsNullOrEmpty(signalId)) return null;
            int dot = signalId.IndexOf('.');
            if (dot <= 0) return null;
            if (!ByCode.TryGetValue(signalId[..dot], out var e)) return null;
            string component = signalId[(dot + 1)..];
            string instead = e.InsteadFor(component) ?? $"{e.Instead} instead";
            return $"{component} on {e.Name} cannot be read because {e.Name} was removed on {e.RetiredOn}: {e.What}; {instead}";
        }

        /// <summary>
        /// The one sentence a workspace load says about the retired indicators it left off the
        /// chart, or null when it left none. Each code is named once however many tabs carried it.
        /// </summary>
        public static string? ChartNotice(IEnumerable<string> retiredCodes)
        {
            var entries = retiredCodes
                .Where(c => c != null && ByCode.ContainsKey(c))
                .Select(c => ByCode[c])
                .Distinct()
                .ToList();
            if (entries.Count == 0) return null;
            return string.Join(" ", entries.Select(e =>
                $"{e.Name} was removed on {e.RetiredOn}, so it was left off this chart: {e.What}. To get the same lines back, {e.Instead}."));
        }
    }
}
