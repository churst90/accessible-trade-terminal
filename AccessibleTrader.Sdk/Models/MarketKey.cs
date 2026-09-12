using System;
using System.Collections.Generic;

namespace AccessibleTrader.Sdk.Models
{
    /// <summary>
    /// <b>The composite market key — "Crypto|Spot" — read and written in one place.</b>
    ///
    /// <para>
    /// A chart's <see cref="ChartIdentity.Market"/> is either a bare category ("Crypto",
    /// "Stock") or, for a provider that declares more than one sub-type, the category and the
    /// sub-type joined by a pipe ("Crypto|Spot", "Crypto|Futures"). Both spellings are legal
    /// and the composite one exists because <c>DataService</c> routes a symbol fetch on it.
    /// </para>
    ///
    /// <para>
    /// ── Why this class exists ────────────────────────────────────────────────
    /// The composite was composed in one place and read back in five, each with its own
    /// <c>Split('|')</c>, and one of those readers assigned the WHOLE composite into a field
    /// declared to hold a bare sub-type. The next compose then produced "Crypto|Crypto|Spot",
    /// and the one after that "Crypto|Crypto|Crypto|Spot" — a field that grew by one segment
    /// per Load Chart, forever, and was persisted into the saved workspace. It was found in a
    /// real session file with five segments on one tab and eight on another. Because
    /// <see cref="ChartIdentity"/> equality includes Market, every load minted a fresh identity:
    /// new cache buckets, a cold chart, and a background-monitor watch key whose seed was
    /// orphaned on every load.
    /// </para>
    ///
    /// <para>
    /// The sanitiser that was supposed to catch it (<c>MarketOrchestrator.RefreshSymbolsAsync</c>
    /// resets an unrecognised sub-type to the list default) could not: the re-pollution happened
    /// four lines later in the same method. <b>Ordering defeats a guard that runs beside the
    /// thing it guards.</b> So the rule is stated here instead, as a type: a sub-type is what
    /// <see cref="SubType"/> returns, never a whole market string, and anything read off disk
    /// goes through <see cref="Normalize"/> first.
    /// </para>
    /// </summary>
    public static class MarketKey
    {
        public const char Separator = '|';

        /// <summary>
        /// The category half — "Crypto" out of "Crypto|Spot", and out of a bare "Crypto".
        /// </summary>
        public static string Category(string? market)
        {
            if (string.IsNullOrWhiteSpace(market)) return market ?? "";
            int pipe = market.IndexOf(Separator);
            return pipe < 0 ? market : market.Substring(0, pipe);
        }

        /// <summary>
        /// The sub-type half — "Spot" out of "Crypto|Spot". <b>Empty for a bare category</b>,
        /// because a provider with one sub-type has no sub-type to adopt, and adopting the
        /// category as one is how the growth started.
        /// </summary>
        public static string SubType(string? market)
        {
            var parts = Segments(market);
            return parts.Count > 1 ? parts[parts.Count - 1] : "";
        }

        /// <summary>
        /// Join a category and a sub-type. A blank sub-type yields the bare category, so a
        /// caller never has to special-case the single-sub-type provider.
        /// </summary>
        public static string Compose(string? category, string? subType)
        {
            string cat = (category ?? "").Trim();
            string sub = (subType ?? "").Trim();
            if (sub.Length == 0) return cat;
            if (cat.Length == 0) return sub;
            return string.Equals(cat, sub, StringComparison.OrdinalIgnoreCase)
                ? cat
                : $"{cat}{Separator}{sub}";
        }

        /// <summary>
        /// Collapse a grown key back to its legal form: "Crypto|Crypto|Crypto|Spot" becomes
        /// "Crypto|Spot", and "Crypto|Crypto" becomes the bare "Crypto". Idempotent, and a
        /// no-op on a key that was never polluted.
        ///
        /// <para>Applied on restore so a workspace already on disk heals itself the first time
        /// it is opened — the producer fix stops new growth but cannot shrink Cody's existing
        /// session file.</para>
        /// </summary>
        public static string Normalize(string? market)
        {
            if (string.IsNullOrWhiteSpace(market)) return market ?? "";
            var parts = Segments(market);
            if (parts.Count == 0) return market;
            return parts.Count == 1 ? parts[0] : $"{parts[0]}{Separator}{parts[parts.Count - 1]}";
        }

        /// <summary>Split, trim, drop blanks, and collapse runs of the same segment.</summary>
        private static List<string> Segments(string? market)
        {
            var collapsed = new List<string>();
            if (string.IsNullOrWhiteSpace(market)) return collapsed;

            foreach (var raw in market.Split(Separator))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                if (collapsed.Count > 0 &&
                    string.Equals(collapsed[collapsed.Count - 1], part, StringComparison.OrdinalIgnoreCase))
                    continue;
                collapsed.Add(part);
            }
            return collapsed;
        }
    }
}
