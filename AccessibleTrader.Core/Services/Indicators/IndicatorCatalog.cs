using System.Globalization;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Indicators
{
    /// <summary>
    /// The indicator choices the Add Indicator dialog and the Go bar share: the same demo and
    /// deprecated filter, the same metadata defaults, the same "where does it land" words and
    /// the same name/code/description search.
    ///
    /// <para>It lives in Core rather than in the dialog because the Go bar runs in Core and the
    /// two must never drift: a typed IND must add exactly the indicator the dialog would, with
    /// exactly the defaults the dialog would use.</para>
    /// </summary>
    public static class IndicatorCatalog
    {
        /// <summary>The dialog's list: allowed by the demo policy and not retired.</summary>
        public static IReadOnlyList<IndicatorMetadata> Available(
            IIndicatorService indicators, DemoPolicy demo)
            => indicators.GetAvailableIndicators()
                .Where(i => demo.IsIndicatorAllowed(i.Code))
                .Where(i => !i.IsDeprecated)
                .ToList();

        /// <summary>
        /// The values the dialog starts with: strings keep their default text (or empty),
        /// every other type is a double when it can be one, and a parameter with no usable
        /// default is zero. The factory formats them again through FormatParam.
        /// </summary>
        public static Dictionary<string, object> DefaultParameters(IndicatorMetadata meta)
            => meta.Parameters.ToDictionary(p => p.Name, InitialValue);

        private static object InitialValue(IndicatorParameterMetadata p)
        {
            if (p.DataType == typeof(string))
                return p.DefaultValue?.ToString() ?? string.Empty;
            if (p.DefaultValue is IConvertible c)
            {
                try { return c.ToDouble(CultureInfo.InvariantCulture); }
                catch { return 0.0; }
            }
            return 0.0;
        }

        /// <summary>
        /// Where the indicator will land, in words: the shared price or volume axis by name,
        /// or "its own pane". Read from the resolver that actually places it, so this cannot
        /// say "Oscillator pane" about a series going somewhere else.
        /// </summary>
        public static string PaneDescription(IndicatorMetadata meta)
        {
            string pane = PaneAssignmentService.PaneFor(meta);
            return PaneAssignmentService.IsSharedByDesign(pane) ? $"{pane} pane" : "its own pane";
        }

        /// <summary>
        /// The dialog's own search order for a typed name: an exact code, then an exact name,
        /// then anything whose name, code or description contains the text. The Go bar uses
        /// the first of those as its top option.
        /// </summary>
        public static IReadOnlyList<IndicatorMetadata> Search(
            IReadOnlyList<IndicatorMetadata> all, string? text)
        {
            string wanted = (text ?? "").Trim();
            if (wanted.Length == 0) return Array.Empty<IndicatorMetadata>();

            var exact = all
                .Where(i => string.Equals(i.Code, wanted, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(i.Name, wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var rest = all
                .Where(i => !exact.Contains(i))
                .Where(i => i.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                         || i.Code.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                         || (i.Description?.Contains(wanted, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

            return exact.Concat(rest).ToList();
        }
    }
}
