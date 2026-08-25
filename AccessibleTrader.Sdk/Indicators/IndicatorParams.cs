using System.Globalization;

namespace AccessibleTrader.Sdk.Indicators
{
    /// <summary>
    /// Shared readers for the <c>Dictionary&lt;string, object&gt;</c> an indicator provider is
    /// handed in <c>Calculate</c>.
    ///
    /// <para>
    /// The dictionary is built from <c>SeriesConfig.Parameters</c>, which is typed
    /// <c>double</c> — a bool declared in <c>IndicatorParameterMetadata</c> arrives as
    /// <c>1.0</c> or <c>0.0</c>, because <c>IndicatorModelFactory.TryParseParamValue</c>
    /// deliberately converts the words "true" and "false" to numbers so the value survives a
    /// numeric dictionary. Since the string-parameter work it can also arrive as a string.
    /// A provider that tests only <c>is bool</c> and <c>is string</c> therefore falls through
    /// to its default forever, which is how Cipher SR's AdaptiveBreak could not be turned off,
    /// Cipher S's AdaptiveSmoothing could not be turned on, and Spider Lines' FastMode did
    /// nothing — three providers silently ignoring a knob the dialog showed as working.
    /// </para>
    ///
    /// <para>
    /// <b>Scope.</b> Only <see cref="GetBool"/> lives here so far. The numeric accessors are
    /// duplicated across ~25 providers in several mutually-disagreeing versions (truncate vs
    /// round, ambient vs invariant culture, null → 0 vs null → default) and unifying them
    /// moves shipped indicator values, so that is its own pass with per-indicator tests. A
    /// boolean has no such ambiguity: non-zero is true, and the only question was whether the
    /// provider looked.
    /// </para>
    /// </summary>
    public static class IndicatorParams
    {
        /// <summary>
        /// Reads a boolean parameter however it survived the trip: as a <see cref="bool"/>, as
        /// the <see cref="double"/> the numeric parameter dictionary actually carries, as any
        /// other numeric boxing, or as a string ("true"/"false", or a number).
        /// Returns <paramref name="def"/> when the key is absent, the value is null, or the
        /// text is not something a boolean can be read out of.
        /// </summary>
        public static bool GetBool(IReadOnlyDictionary<string, object>? p, string key, bool def)
        {
            if (p == null || key == null) return def;
            if (!p.TryGetValue(key, out var v) || v == null) return def;

            switch (v)
            {
                case bool b: return b;
                case double d: return d != 0.0;
                case float f: return f != 0.0f;
                case int i: return i != 0;
                case long l: return l != 0L;
                case decimal m: return m != 0m;
                case string s:
                    if (bool.TryParse(s.Trim(), out bool parsed)) return parsed;
                    // InvariantCulture, not the ambient one: workspaces persist parameters as
                    // JSON and a comma-decimal locale must not change what a switch means.
                    return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                        ? n != 0.0
                        : def;
                default:
                    try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                    {
                        return def;
                    }
            }
        }
    }
}
