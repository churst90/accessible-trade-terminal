using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services
{
    /// <summary>
    /// What an indicator instance is CALLED — the name Page Up / Page Down reads, the Object Tree
    /// shows, and every signal announcement used to carry.
    ///
    /// <para>
    /// Reported by Cody, 2026-09-04: <i>"when I nav to a series it doesn't list the parameters in
    /// the name like cipher b reads as 'cipher b 9 12 60 50 14 …' not necessary"</i>. The name was
    /// built by joining EVERY parameter value onto the indicator's name, unlabelled, in dictionary
    /// order. On a one-parameter indicator that gives "EMA 20", which is exactly right and is why
    /// it was written. On Cipher B it gives eight bare numbers a listener cannot map back to
    /// anything, on the name they hear most often in the app.
    /// </para>
    ///
    /// <para>
    /// THE NAME'S JOB IS TO TELL TWO INSTANCES APART, and that is almost the whole of it. Which
    /// means the question the suffix answers is not "did the user change anything" but <b>"is
    /// there anything else on this chart it could be confused with"</b>.
    /// </para>
    ///
    /// <para>
    /// <b>Differ from the DEFAULTS, or differ from the SIBLINGS?</b> The first version of this
    /// compared against the indicator's declared defaults, which is a large improvement on
    /// reciting everything and still not the rule. Cody, 2026-09-04: <i>"I don't like how it says
    /// cipher b 11 … the reason I wanted those parameters listed was so I could identify things
    /// like EMAs and SMAs on the chart so I could hear them with the period"</i>. One Cipher B
    /// with a retuned RSI length is still THE Cipher B on the chart; "11" tells the listener
    /// nothing they can act on, because there is nothing to tell it apart FROM. Two EMAs are the
    /// case the period is needed for, and there the period is needed whether or not either of
    /// them sits at the default.
    /// </para>
    ///
    /// <para>
    /// <b>And "almost", because of the EMA alone.</b> Cody, 2026-09-05: <i>"Which indicator
    /// realistically need the user to know the period? ema 50, ema 21, sma 50, etc. dema, tema,
    /// those types of things, clouds maybe"</i>. A moving average is NAMED by its period — nobody
    /// says "the EMA", they say "the 50" — so the cohort rule, which names a lone EMA "EMA",
    /// throws away the one fact the person who added it wanted to hear. That is not a list kept
    /// here: it is <see cref="IndicatorMetadata.NamedByParameters"/>, declared by the indicator,
    /// because which parameter is the name is a fact about the indicator and the author is the
    /// one who knows it. Those values are ALWAYS in the name, siblings or not, ahead of anything
    /// the cohort rule adds.
    /// </para>
    ///
    /// <para>
    /// So the rule is <see cref="For(IndicatorMetadata, IReadOnlyDictionary{string, object}, IReadOnlyList{IReadOnlyDictionary{string, object}}, int)"/>:
    /// the named-by values first, always. Then, only with siblings, the parameters on which the
    /// COHORT disagrees — in declared order, bare, which is how traders write them anyway
    /// ("EMA 20", "MACD 12 26 9") — and nothing else. Add a second EMA and the first one is
    /// renamed in the same breath, because a distinguishing suffix on one of a pair is not
    /// distinguishing.
    /// </para>
    ///
    /// <para>
    /// And a cap, because "only what the cohort disagrees on" is not a bound: two instances of an
    /// eight-parameter indicator retuned wholesale would be back where they started. Past
    /// <see cref="MaxNamedParameters"/> the suffix becomes an ordinal — "Cipher B 2" — which is
    /// short, is a name rather than a recitation, and is still unique, which is the entire job.
    /// The ordinal is the instance's POSITION in its cohort, not the cohort's size: the first
    /// version returned <c>siblings + 1</c>, which for a pair is "2" for BOTH of them.
    /// </para>
    /// </summary>
    public static class IndicatorInstanceName
    {
        /// <summary>
        /// How many parameter values may be spoken as values before the suffix collapses to an
        /// ordinal. Three covers the shapes that read naturally — "EMA 20", "MACD 12 26 9" — and
        /// stops short of the wall of numbers this exists to remove.
        /// </summary>
        public const int MaxNamedParameters = 3;

        /// <summary>
        /// The instance name for a series, given what else of the same indicator is on the chart.
        /// </summary>
        /// <param name="meta">The indicator's metadata — supplies the name, the declared parameter
        /// ORDER (which is the order a trader writes the values in), the parameters the indicator
        /// is NAMED BY, and the defaults a sibling that never stored a value falls back to.</param>
        /// <param name="parameters">This instance's parameters.</param>
        /// <param name="siblingParameters">The parameter sets of the OTHER instances of the same
        /// indicator on the chart. Empty or null means this one is alone.</param>
        /// <param name="ordinal">This instance's 1-based position in its cohort, used only when
        /// the name has to fall back to an ordinal. Zero means "the one just added", i.e. last.</param>
        public static string For(
            IndicatorMetadata meta,
            IReadOnlyDictionary<string, object>? parameters,
            IReadOnlyList<IReadOnlyDictionary<string, object>>? siblingParameters = null,
            int ordinal = 0)
        {
            var mine = parameters ?? new Dictionary<string, object>();
            var siblings = siblingParameters ?? Array.Empty<IReadOnlyDictionary<string, object>>();

            // Declared order first, then any undeclared keys alphabetically so the result is
            // stable. Undeclared keys are INCLUDED as candidates: the metadata not knowing about
            // a parameter is no reason to let two instances share a name because of it.
            var declaredOrder = meta.Parameters?.Select(p => p.Name).ToList() ?? new List<string>();
            var extraKeys = mine.Keys
                .Concat(siblings.SelectMany(d => d.Keys))
                .Where(k => !declaredOrder.Any(d => string.Equals(d, k, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

            var namedBy = meta.NamedByParameters ?? new List<string>();
            bool IsNamedBy(string key) => namedBy.Any(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase));

            var identity = new List<string>();        // always spoken
            var discriminating = new List<string>();  // spoken only with siblings, only where they disagree
            foreach (var key in declaredOrder.Concat(extraKeys))
            {
                object? mineVal = Lookup(mine, key, meta);
                if (IsNamedBy(key))
                {
                    identity.Add(Format(mineVal));
                    continue;
                }
                if (siblings.Count == 0) continue;
                bool anyDisagrees = siblings.Any(sib => !SameValue(mineVal, Lookup(sib, key, meta)));
                if (anyDisagrees) discriminating.Add(Format(mineVal));
            }

            string head = identity.Count == 0 ? meta.Name : $"{meta.Name} {string.Join(" ", identity)}";
            if (siblings.Count == 0) return head;

            // With siblings, the identity values may already tell the instances apart — two EMAs
            // at 21 and 50 need nothing more. Only when every sibling shares this instance's
            // identity values does the cohort rule have work to do.
            bool identityDistinguishes = identity.Count > 0
                && siblings.All(sib => !SameIdentity(mine, sib, meta, namedBy));
            if (identityDistinguishes) return head;

            // Nothing disagrees: the chart holds two instances configured identically. Rare, and
            // usually blocked upstream, but a name is still owed and "EMA" twice is not one.
            // The ordinal is the honest answer — they really are the same indicator twice.
            int position = ordinal > 0 ? ordinal : siblings.Count + 1;
            if (discriminating.Count == 0 || identity.Count + discriminating.Count > MaxNamedParameters)
                return $"{head} {position}";

            return $"{head} {string.Join(" ", discriminating)}";
        }

        /// <summary>
        /// Whether two instances agree on every parameter the indicator is named by.
        /// </summary>
        private static bool SameIdentity(
            IReadOnlyDictionary<string, object> a, IReadOnlyDictionary<string, object> b,
            IndicatorMetadata meta, IReadOnlyList<string> namedBy)
        {
            foreach (var key in namedBy)
                if (!SameValue(Lookup(a, key, meta), Lookup(b, key, meta))) return false;
            return true;
        }

        /// <summary>
        /// A parameter's value for one instance: what it stored, or — when it stored nothing —
        /// the indicator's declared default, which is the value that instance is running on.
        /// Falling back to the default is what makes "one EMA at 20 that never wrote a Period,
        /// one at 50 that did" come out as 20 against 50 rather than as blank against 50.
        /// </summary>
        private static object? Lookup(IReadOnlyDictionary<string, object> values, string key, IndicatorMetadata meta)
        {
            foreach (var kv in values)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;

            return meta.Parameters?
                .FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase))?
                .DefaultValue;
        }

        /// <summary>
        /// The same cap for the metadata-free path, where there are no defaults to compare
        /// against. Blunter on purpose: without knowing which values are the indicator's own, the
        /// only safe reduction is a length one.
        /// </summary>
        public static string ForValues(string name, IEnumerable<string>? values)
        {
            var list = values?.ToList() ?? new List<string>();
            if (list.Count == 0) return name;
            if (list.Count <= MaxNamedParameters) return $"{name} {string.Join(" ", list)}";
            return $"{name}, {list.Count} parameters";
        }

        /// <summary>
        /// Whether a supplied value is the declared default. Compared as NUMBERS when both parse
        /// as numbers, because the two sides arrive from different places — a default declared as
        /// <c>int 20</c> and a value that came back from a form as <c>"20"</c> or <c>20.0</c> are
        /// the same setting, and a string comparison would call every one of them a change and
        /// put the whole parameter list back into the name.
        /// </summary>
        private static bool SameValue(object? declaredDefault, object? supplied)
        {
            // Two absent values agree — otherwise a parameter neither instance has ever heard of
            // would "disagree" for every pair and land in every name.
            if (declaredDefault == null && supplied == null) return true;
            if (declaredDefault == null || supplied == null) return false;

            if (TryNumber(declaredDefault, out double a) && TryNumber(supplied, out double b))
                return Math.Abs(a - b) < 1e-9;

            return string.Equals(Format(declaredDefault), Format(supplied), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryNumber(object o, out double value)
        {
            switch (o)
            {
                case double d: value = d; return true;
                case float f:  value = f; return true;
                case int i:    value = i; return true;
                case long l:   value = l; return true;
                case decimal m: value = (double)m; return true;
                default:
                    return double.TryParse(Format(o), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            }
        }

        private static string Format(object? v) => v switch
        {
            null      => "",
            double d  => d.ToString("G", CultureInfo.InvariantCulture),
            float f   => f.ToString("G", CultureInfo.InvariantCulture),
            decimal m => m.ToString("G", CultureInfo.InvariantCulture),
            _         => v.ToString() ?? "",
        };
    }
}
