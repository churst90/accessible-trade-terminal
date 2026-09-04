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
    /// THE NAME'S JOB IS TO TELL TWO INSTANCES APART, and that is the whole of it. So the suffix
    /// carries only the parameters that DIFFER FROM THE INDICATOR'S OWN DEFAULTS: at defaults it
    /// is silent, and the moment you add a second EMA at 50 the difference is the thing you hear.
    /// A parameter left alone carries no information about this instance — it is a property of the
    /// indicator, and the indicator is already named.
    /// </para>
    ///
    /// <para>
    /// And a cap, because "only what differs" is not a bound: someone who retunes an eight-
    /// parameter indicator wholesale would be back where they started. Past
    /// <see cref="MaxNamedParameters"/> the suffix becomes a count, which is short, honest, and
    /// tells the user the two instances differ without reciting how.
    /// </para>
    /// </summary>
    public static class IndicatorInstanceName
    {
        /// <summary>
        /// How many differing parameter values may be spoken as values before the suffix collapses
        /// to a count. Three covers the shapes that read naturally — "EMA 20", "MACD 12 26 9" —
        /// and stops short of the wall of numbers this exists to remove.
        /// </summary>
        public const int MaxNamedParameters = 3;

        /// <summary>
        /// The instance name for a series being created from indicator metadata: the indicator's
        /// name, plus the values of any parameters the user changed.
        /// </summary>
        public static string For(IndicatorMetadata meta, IReadOnlyDictionary<string, object>? parameters)
        {
            if (parameters == null || parameters.Count == 0) return meta.Name;

            var changed = new List<string>();
            foreach (var p in parameters)
            {
                var declared = meta.Parameters?.FirstOrDefault(m =>
                    string.Equals(m.Name, p.Key, StringComparison.OrdinalIgnoreCase));

                // An UNDECLARED parameter is always spoken. The metadata cannot tell us it is at
                // its default because it does not know it exists, and silently dropping a value
                // that might be the only difference between two instances is the one outcome
                // worse than reciting one too many.
                if (declared == null) { changed.Add(Format(p.Value)); continue; }

                if (!SameValue(declared.DefaultValue, p.Value)) changed.Add(Format(p.Value));
            }

            if (changed.Count == 0) return meta.Name;
            if (changed.Count <= MaxNamedParameters) return $"{meta.Name} {string.Join(" ", changed)}";
            return $"{meta.Name}, {changed.Count} custom parameters";
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
